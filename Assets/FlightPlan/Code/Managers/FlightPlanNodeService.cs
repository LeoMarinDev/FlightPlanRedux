using FPUtilities;
using KSP.Game;
using KSP.Map;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using MuMech;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// `ILogger` is ambiguous here (UnityEngine.ILogger vs ReduxLib.Logging.ILogger) because this file
// imports UnityEngine. Every `ILogger` in the FlightPlan sources is the ReduxLib one.
using ILogger = ReduxLib.Logging.ILogger;

namespace FlightPlan
{
    /// <summary>
    /// FlightPlan's maneuver-node service: the absorption of the NodeManager members FlightPlan used
    /// to call across a mod boundary, re-implemented against the 0.2.8.5 public surface.
    ///
    /// Absorbed (source = mods-outdated/NodeManager/src/NodeManager/NodeManagerPlugin.cs):
    ///   CreateManeuverNodeAtUT(:524) -> CreateNodeAtUT / CreateNodeAtDeltaV
    ///   Nodes(:92)                   -> Nodes          (ManeuverPlanComponent.GetNodes(), the live list)
    ///   currentNode(:91)             -> CurrentNode / ActiveNode
    ///   DeleteNode(:420)             -> DeleteNode
    ///   DeletePastNodes(:441)        -> DeletePastNodes  (+ DeleteNodes(:458) -> DeleteNodes)
    ///   RefreshNodes(:1028)          -> RefreshNodesCo   (+ private RefreshNodeStates(:1060))
    ///   AddNode(:997) default-UT     -> DefaultNodeUT
    ///   SpitNode(:311,:367)          -> SpitNode
    ///
    /// Two rules hold everywhere in this file:
    ///  1. The node API is the *public* 0.2.8.5 surface. The update calls take a `ManeuverNodeData`
    ///     overload whose `Guid` twin is `private hidebysig` in the shipped assembly; the public
    ///     wrapper is the route (IL-verified: it forwards, so nothing is lost by it). A publicizer is
    ///     not an option here.
    ///  2. Every map call is guarded. `Game.Map` exists in every scene but its core does not, so
    ///     `MapCoreOrNull()` returns null outside the map and every caller degrades to a log line.
    /// </summary>
    public static class FlightPlanNodeService
    {
        // -----------------------------------------------------------------------------------------
        // Policy - every constant here is ported, not invented; the K-item is the follow-up.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The node cap, inherited from NodeManagerPlugin.cs:93 (which set 9).
        ///
        /// K14 — RE-MEASURED in game on Redux 0.2.8.5.103184 (launch 3, 2026-09-14). NodeManager's
        /// number was RIGHT and its stated mechanism was WRONG. It wrote "This seems to be a hard
        /// limit on KSP2's capacity for nodes. Even making them manually in the game you will get
        /// NREs if you try to make a 10th one." Measured, by adding nodes through the vanilla UI:
        ///
        ///   * the game ACCEPTS the 10th node - no exception at creation time;
        ///   * it then REFUSES the 11th (further clicks produce nothing);
        ///   * the map stops drawing the post-maneuver trajectory;
        ///   * and `ManeuverPlanSolver.SolveManeuver` throws `IndexOutOfRangeException`
        ///     (`List.set_Item`) from `UpdateManeuverTrajectory` on EVERY FRAME thereafter -
        ///     11,041 error blocks in ~4 minutes, at one per ~26 ms, growing 50 KB/s of Ksp2.log.
        ///
        /// So the game does not reject the 10th node; it accepts it and then breaks its own solver,
        /// permanently, for as long as that vessel exists. 9 is therefore a REAL limit, not a
        /// policy choice - a 10-node plan puts the runtime into a per-frame error loop, and saving
        /// it persists the broken vessel. This constant must not be raised to 10.
        /// </summary>
        public const int MaxNodes = 9;

        /// <summary>The legacy floor: never create a node at now or in the past (`burnUT >= UT + 1`).</summary>
        public const double MinLeadTime_s = 1.0;

        /// <summary>The legacy's >= 1 s separation fix-up between the new node and an existing one.</summary>
        public const double MinNodeSeparation_s = 1.0;

        /// <summary>The legacy FlightPlan proximity rule (FlightPlanPlugin.cs:471): a requested burn
        /// within 30 s of an existing node is refused before any node is created.</summary>
        public const double NodeProximity_s = 30.0;

        /// <summary>K12 / decision D6: how long to wait for the solver to write BurnDuration, counted
        /// in fixed updates (~0.4 s at 50 Hz). The wait is logged whichever way it goes.</summary>
        private const int SolverWaitMaxFixedUpdates = 20;

        /// <summary>
        /// Verification increment 3, fix 1: the settle criterion. `Nodes.Count` counts as settled once
        /// the same value has been observed <see cref="SettleStableSamples"/> times in a row - the entry
        /// read counts as the first sample. Launch 5 measured the count moving 1 -> 3 -> 2 inside 46 ms
        /// while the game's own `ManeuverPlanComponent.RebuildNodes` coroutine was in flight, so a single
        /// read is not evidence of anything.
        /// </summary>
        private const int SettleStableSamples = 3;

        /// <summary>The settle wait's bound: 60 fixed updates (~1.2 s at 50 Hz), then it gives up and
        /// says so rather than hanging.</summary>
        private const int SettleMaxFixedUpdates = 60;

        /// <summary>Fix 3's tighter bound for the create coroutine: 10 fixed updates (~0.2 s). A create
        /// may wait for a still snapshot, but only briefly - past that it validates against the latest
        /// read and logs that it could not settle.</summary>
        private const int SnapshotSettleMaxFixedUpdates = 10;

        /// <summary>Fix 3 treats a snapshot as settled when two consecutive reads agree.</summary>
        private const int SnapshotStableSamples = 2;

        private static ILogger Log { get { return FlightPlanPlugin.Logger; } }

        // -----------------------------------------------------------------------------------------
        // Read accessors
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The active vessel's maneuver nodes - the absorbed `NodeManagerPlugin.Nodes`.
        ///
        /// This is the *live* `ManeuverPlanComponent._currentNodes` list: `GetNodes()` returns the
        /// field itself, not a copy (IL-verified), so additions and removals are visible through an
        /// already-fetched list. Never hand it to `RemoveNodesFromVessel` - see RemoveNodes().
        /// </summary>
        public static List<ManeuverNodeData> Nodes
        {
            get
            {
                ManeuverPlanComponent plan = PlanFor(ActiveVessel());
                List<ManeuverNodeData> nodes = plan == null ? null : plan.GetNodes();
                return nodes == null ? new List<ManeuverNodeData>() : nodes;
            }
        }

        /// <summary>
        /// The absorbed `NodeManagerPlugin.currentNode` (`Nodes.FirstOrDefault()`, filled by the
        /// legacy's RefreshActiveVesselAndCurrentManeuver at :293).
        /// </summary>
        public static ManeuverNodeData CurrentNode
        {
            get
            {
                List<ManeuverNodeData> nodes = Nodes;
                return nodes.Count > 0 ? nodes[0] : null;
            }
        }

        /// <summary>
        /// The component's own computed node (`_state.maneuvers.Count > 0 ? _currentNodes[0] : null`,
        /// IL-verified). It is backed by the *committed* state, so it can read null for a frame right
        /// after AddNodeToVessel while `CurrentNode` already sees the node - both are exposed because
        /// the absorbed `currentNode` and the component's `ActiveNode` are not the same guarantee.
        /// </summary>
        public static ManeuverNodeData ActiveNode
        {
            get
            {
                ManeuverPlanComponent plan = PlanFor(ActiveVessel());
                return plan == null ? null : plan.ActiveNode;
            }
        }

        /// <summary>`Game.UniverseModel.UniverseTime`, or 0 when there is no simulation yet.</summary>
        public static double CurrentUT()
        {
            UniverseModel universe = Game == null ? null : Game.UniverseModel;
            return universe == null ? 0.0 : universe.UniverseTime;
        }

        // -----------------------------------------------------------------------------------------
        // Create - the hoisted create sequence (K2D2 oracle, ManeuverCreator.cs:257-290)
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Creates a node from a **deltaV** (world frame) - the shape the legacy plugin's API methods
        /// used (`CreateManeuverNodeCaller` at FlightPlanPlugin.cs:459). Converts to maneuver-node
        /// coordinates, then delegates to the one create path.
        /// </summary>
        public static bool CreateNodeAtDeltaV(Vector3d deltaV, double burnUT, double burnOffsetFactor = -0.5)
        {
            VesselComponent vessel = ActiveVessel();
            IKeplerPatch orbit = vessel == null ? null : vessel.Orbit;
            if (orbit == null)
            {
                Warn("CreateNodeAtDeltaV: no active vessel/orbit - node creation refused");
                return false;
            }

            double UT = CurrentUT();
            Vector3d burnParams = orbit.DeltaVToManeuverNodeCoordinates(burnUT, deltaV);
            LogInfo($"CreateNodeAtDeltaV: solution deltaV = {deltaV:F3} m/s = {deltaV.magnitude:F3} m/s {FPUtility.SecondsToTimeString(burnUT - UT)} from now");
            LogInfo($"CreateNodeAtDeltaV: burnParams = {burnParams:F3} m/s = {burnParams.magnitude:F3} m/s {FPUtility.SecondsToTimeString(burnUT - UT)} from now");
            return CreateNodeAtUT(burnParams, burnUT, burnOffsetFactor, deltaV);
        }

        /// <summary>
        /// Creates a node at `burnUT` from a **burnVector** (maneuver-node coordinates), then applies
        /// the `burnDurationOffsetFactor` convention (decision D6). This is the signature the UI's
        /// advanced path needs (legacy `FpUiController.cs:2524`).
        ///
        /// Returns false for a refusal decided by the synchronous entry pass (no vessel, cap reached, an
        /// existing node within 30 s, no orbit patch containing the burn time - the P8 placement
        /// pre-check). It returns true when the request is accepted into the create
        /// coroutine - and that coroutine re-runs the same cap/separation/proximity/placement rules
        /// against a *settled* snapshot before it adds anything (fix 3, verification increment 3). A
        /// `true` result can therefore still end without a node when the settled plan refuses the
        /// request: the refusal is logged under `CreateNodeAtUT [settled]` or by the settled placement
        /// pre-check, and `LastCreatedNode` stays null.
        /// </summary>
        public static bool CreateNodeAtUT(Vector3d burnVector, double burnUT, double burnOffsetFactor = -0.5)
        {
            // Reconstruct the requested deltaV so the post-offset re-point compares like with like:
            // BurnVecToDv and DeltaVToManeuverNodeCoordinates are an exact pair (OrbitExtensions.cs
            // :891,:898 - an orthonormal frame decomposition and its recomposition).
            VesselComponent vessel = ActiveVessel();
            IKeplerPatch orbit = vessel == null ? null : vessel.Orbit;
            Vector3d requestedDeltaV = orbit == null ? burnVector : orbit.BurnVecToDv(burnUT, burnVector);
            return CreateNodeAtUT(burnVector, burnUT, burnOffsetFactor, requestedDeltaV);
        }

        /// <summary>The one create path: every rule is applied here, then the sequence is handed to
        /// the coroutine.</summary>
        private static bool CreateNodeAtUT(Vector3d burnVector, double burnUT, double burnOffsetFactor, Vector3d requestedDeltaV)
        {
            VesselComponent vessel = ActiveVessel();
            if (vessel == null)
            {
                Warn("CreateNodeAtUT: no active vessel - node creation refused");
                return false;
            }

            if (CreateInFlight)
            {
                Warn("CreateNodeAtUT: a create sequence is already running - refused");
                return false;
            }

            List<ManeuverNodeData> nodes = Nodes;
            double UT = CurrentUT();
            if (UT <= 0)
            {
                Warn("CreateNodeAtUT: no universe time (not in a flight scene) - node creation refused");
                return false;
            }

            // The rules below are applied TWICE: here, on the synchronous entry snapshot (which is what
            // keeps a refusal synchronous - the cap test in the probe depends on it), and again in the
            // create coroutine on a settled snapshot (fix 3, verification increment 3), which is the
            // authoritative pass. They are the same rules either way, which is why they live in shared
            // helpers instead of in two copies.
            string capRefusal = CheckNodeCap(nodes.Count, "CreateNodeAtUT");
            if (capRefusal != null)
            {
                Warn(capRefusal);
                return false;
            }

            // The legacy floor: never create at now or in the past (NodeManagerPlugin.cs:540).
            ApplyFloorClamp(UT, ref burnUT, "CreateNodeAtUT");

            string ruleRefusal = ApplySeparationAndProximity(nodes, UT, ref burnUT, "CreateNodeAtUT");
            if (ruleRefusal != null)
            {
                Warn(ruleRefusal);
                return false;
            }

            // P8 (launch-17 BL-1): the placement rule joins the cap/separation/proximity rules -
            // applied here on the synchronous snapshot, which is what lets this refusal reach the
            // user through the plugin's FPStatus call, and applied again in the coroutine on the
            // settled snapshot (the authoritative pass) just before the add.
            string placementRefusal = CheckNodePlacement(vessel, burnUT);
            if (placementRefusal != null)
            {
                Warn(placementRefusal);
                return false;
            }

            LogInfo($"CreateNodeAtUT: burnVector = [{burnVector.x:F3}, {burnVector.y:F3}, {burnVector.z:F3}] = {burnVector.magnitude:F3} m/s, burnUT = {burnUT - UT:F3} s from now, offsetFactor = {burnOffsetFactor}, Nodes.Count = {nodes.Count}");

            if (FlightPlanPlugin.Instance == null)
            {
                Warn("CreateNodeAtUT: plugin instance is gone - cannot run the create coroutine");
                return false;
            }

            CreateInFlight = true;
            LastCreatedNode = null;
            FlightPlanPlugin.Instance.StartCoroutine(CreateNodeAtUTCo(vessel, burnVector, requestedDeltaV, burnUT, burnOffsetFactor));
            return true;
        }

        // -----------------------------------------------------------------------------------------
        // Create rules - shared by the synchronous entry pass and the settled-snapshot pass
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The cap rule (`Nodes.Count >= MaxNodes`). Returns null when the request may proceed, or the
        /// line to log when it is refused. K14's measurement is why the cap is 9 and it is the safety
        /// rule of the whole create path; it runs on both passes (see the fix 3 comment in
        /// <see cref="CreateNodeAtUTCo"/>), so a transient low read at the cap cannot let a 10th node
        /// through.
        /// </summary>
        private static string CheckNodeCap(int nodeCount, string caller)
        {
            if (nodeCount < MaxNodes)
            {
                return null;
            }

            return $"{caller}: node cap reached ({nodeCount} >= {MaxNodes}) - refused. K14 measured 2026-09-14: the game accepts a 10th node and then its own ManeuverPlanSolver throws IndexOutOfRangeException every frame, so the cap is a real limit.";
        }

        /// <summary>
        /// The legacy floor (NodeManagerPlugin.cs:540): never create at now or in the past. Moves
        /// `burnUT` forward and logs it; the logged delta is the pre-clamp one, exactly as the in-line
        /// version logged it.
        /// </summary>
        private static void ApplyFloorClamp(double UT, ref double burnUT, string caller)
        {
            if (burnUT < UT + MinLeadTime_s)
            {
                LogInfo($"{caller}: burnUT {burnUT - UT:F3} s from now is below the {MinLeadTime_s} s floor - clamped");
                burnUT = UT + MinLeadTime_s;
            }
        }

        /// <summary>
        /// The two rules that read the node list. The legacy separation fix-up (:548-564): keep >= 1 s
        /// between the new node and its nearest neighbour, so two nodes never share a UT; it adjusts
        /// `burnUT` in place. The legacy FlightPlan proximity rule (:462-479): a node within 30 s of the
        /// request means the solution is already covered, and the caller is told so. Returns null when
        /// the request may proceed, or the line to log when it is refused.
        /// </summary>
        private static string ApplySeparationAndProximity(List<ManeuverNodeData> nodes, double UT, ref double burnUT, string caller)
        {
            if (nodes.Count > 0)
            {
                double minDeltaT = double.MaxValue;
                int closestNode = -1;
                for (int i = 0; i < nodes.Count; i++)
                {
                    double deltaT = Math.Abs(nodes[i].Time - burnUT);
                    if (deltaT < minDeltaT)
                    {
                        minDeltaT = deltaT;
                        closestNode = i;
                    }
                }
                if (closestNode >= 0 && minDeltaT < MinNodeSeparation_s)
                {
                    burnUT = nodes[closestNode].Time + (burnUT < nodes[closestNode].Time ? -MinNodeSeparation_s : MinNodeSeparation_s);
                    Warn($"{caller}: Node {closestNode} is {minDeltaT:F3} s away - burnUT moved to {burnUT - UT:F3} s from now so the gap is >= {MinNodeSeparation_s} s");
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                double deltaT = nodes[i].Time - burnUT;
                if (Math.Abs(deltaT) < NodeProximity_s)
                {
                    return $"{caller}: requested burn is {deltaT:F3} s from Node {i + 1} (< {NodeProximity_s} s) - creation refused";
                }
            }

            return null;
        }

        /// <summary>True while a create coroutine is mid-sequence. The probe waits on this instead of
        /// guessing a frame count.</summary>
        public static bool CreateInFlight { get; private set; }

        /// <summary>The node the last create produced (set as soon as it is added to the vessel).</summary>
        public static ManeuverNodeData LastCreatedNode { get; private set; }

        /// <summary>
        /// The hoisted create sequence. Steps 1-6 are the K2D2 oracle (in-game proven on this
        /// runtime); step 6a is NodeManager's own call at :601, restored because the oracle's omission
        /// is a latent NRE (see the comment on it). Steps 7-9 are the legacy FlightPlan burn-centred
        /// convention (:662-684).
        ///
        /// Step 0 (verification increment 3, fix 3) settles the plan before any snapshot is
        /// validated against it: the cap, separation and proximity rules are re-run here on a settled
        /// node list, and a refusal at this point stops the create before anything is added. The
        /// comment in the body names the launch-5 measurement that is the evidence for it.
        ///
        /// The synchronous halves are wrapped so that a game-side exception comes out as a `[FlightPlan]`
        /// log line with a stack trace instead of a silently dead coroutine - the launch test greps for
        /// exactly that. `yield return` cannot sit inside a try/catch, hence the error-flag shape.
        /// </summary>
        private static IEnumerator CreateNodeAtUTCo(VesselComponent vessel, Vector3d burnVector, Vector3d requestedDeltaV, double burnUT, double burnOffsetFactor)
        {
            double UT = CurrentUT();

            // -------------------------------------------------------------------------------------
            // FIX 3 (verification increment 3): the snapshot the create rules validate against is the
            // SETTLED one, not whatever the synchronous entry pass happened to read.
            //
            // Evidence - launch 5, 2026-09-14. Four lines, all inside 46 ms, with the game's own
            // `ManeuverPlanComponent.RebuildNodes` coroutine in flight:
            //   MULTI-PROBE create #4: ... with Nodes.Count = 1      <- the probe's `before`
            //   CreateNodeAtUT: burnVector = ... Nodes.Count = 1     <- this service's entry snapshot
            //   CreateNodeAtUT: complete - Nodes.Count = 3           <- this service, at completion
            //   MULTI-PROBE create #4 OK: Nodes.Count = 2 (was 1)    <- the probe after the create
            // The count went 1 -> 3 -> 2. `GetNodes()` returns the live `_currentNodes` list, so the
            // entry read captured a transient: it saw 1 while a node at UT+596.8 was already live, and
            // the 30 s proximity rule - the very rule that exists to catch this - was run against a
            // list that did not contain it. A create at UT+600 was therefore accepted 3.2 s from a
            // live node instead of being refused.
            //
            // THE RULE IS NOT RELAXED. The cap, the >= 1 s separation fix-up and the 30 s proximity
            // refusal all still run; they simply run again here, against a count that has held still
            // for two consecutive reads. If that cannot be reached within
            // SnapshotSettleMaxFixedUpdates, the latest read is used and the failure to settle is
            // logged - a create is never refused merely because the plan would not hold still, and
            // this coroutine never waits unbounded.
            //
            // This is a PRODUCTION hazard, not a probe one: any two nodes created back to back can hit
            // it. `FlightPlanPlugin.HohmannTransfer` takes TWO burns from one solution
            // (`_deltaV1`/`_burnUT1` and `_deltaV2`/`_burnUT2`, FlightPlanPlugin.cs:834/:843) and the
            // port currently creates only the first; when the UI drives a second create through this
            // service its entry snapshot can be just as transient. Recorded here so that review is not
            // lost - nothing in this increment changes the plugin.
            // -------------------------------------------------------------------------------------
            // The settle spans fixed updates while `CreateInFlight` is held, so it is driven through
            // GuardedCo (this file's own idiom): a throw inside it would otherwise leave
            // `CreateInFlight == true` forever, which would refuse every later create for the rest of
            // the session. On a throw the create stops, says so, and releases the flag.
            bool snapshotFailed = false;
            yield return GuardedCo(WaitForSettledCreateSnapshot("create snapshot"), delegate (Exception e)
            {
                snapshotFailed = true;
                Error($"CreateNodeAtUT: EXCEPTION while waiting for the plan to settle - the create stops here, nothing was added: {e}");
            });
            if (snapshotFailed)
            {
                CreateInFlight = false;
                yield break;
            }

            // The settle cost real time, so re-derive `now`: every log line below and the
            // `targetUT < UT` guard use the UT at the moment of the add, not the UT at the request.
            UT = CurrentUT();

            List<ManeuverNodeData> settledNodes = Nodes;
            string settledRefusal = CheckNodeCap(settledNodes.Count, "CreateNodeAtUT [settled]");
            if (settledRefusal == null)
            {
                ApplyFloorClamp(UT, ref burnUT, "CreateNodeAtUT [settled]");
                settledRefusal = ApplySeparationAndProximity(settledNodes, UT, ref burnUT, "CreateNodeAtUT [settled]");
            }
            if (settledRefusal != null)
            {
                Warn(settledRefusal);
                Warn("CreateNodeAtUT: the settle pass refused the request - no node was added. The synchronous pass above accepted it against a transient list (see the launch-5 evidence comment above).");
                CreateInFlight = false;
                yield break;
            }

            IKeplerPatch orbit = vessel.Orbit;
            ManeuverPlanComponent plan = PlanFor(vessel);
            if (orbit == null || plan == null)
            {
                Warn("CreateNodeAtUT: the vessel lost its orbit/plan while the plan was settling - no node was added");
                CreateInFlight = false;
                yield break;
            }
            ManeuverNodeData nodeData = new ManeuverNodeData(vessel.SimulationObject.GlobalId, false, burnUT);
            string addFailure = null;

            // 6d-bis. P8 (launch-17 BL-1): the placement pre-check, before anything is added. The
            //     game's own `AddNode` -> `UpdateNodeDetails` resolves the node's patch with
            //     `GetSimulationObject().Vessel.Orbiter.PatchedConicSolver.FindPatchContainingUT(
            //     node.Time)` and dereferences the result without a null check, so a burn time beyond
            //     the last propagated patch throws a NullReferenceException from inside the game -
            //     after `AddNode` has already appended the node. Asking the same solver the same
            //     question first (see CheckNodePlacement) turns that into a clean refusal.
            string placementRefusal = CheckNodePlacement(vessel, burnUT);
            if (placementRefusal != null)
            {
                Warn(placementRefusal);
                CreateInFlight = false;
                yield break;
            }

            // True only once `AddNodeToVessel` has returned: the cleanup below must never remove a
            // node the add actually completed (the writes after it are simple field assignments).
            bool added = false;

            try
            {
                // 6a. NodeManagerPlugin.cs:601 - load-bearing, not defensive. The constructor leaves
                //     SimTransform null (IL-verified: it sets NodeName, RelatedSimID, Time, NodeID,
                //     IsOnManeuverTrajectory and BurnRequiredDV, and nothing else), and
                //     ManeuverPlanComponent.UpdateNodeDetails dereferences SimTransform
                //     unconditionally at its tail - `ldfld SimTransform` then SetLocalPosition /
                //     SetLocalRotation, no null check on either branch - and AddNode calls
                //     UpdateNodeDetails before it commits. This is P4's one deliberate divergence from
                //     the K2D2 oracle, which omits the call; see the ledger.
                if (nodeData.SimTransform == null)
                {
                    nodeData.InitializeTransform();
                }

                // 6b/6c. NO SetManeuverState here - it cannot be called at this pin. Its parameter is
                //     the concrete `PatchedConicsOrbit` (rank 1: monodis --method
                //     Assembly-CSharp.dll -> `ManeuverNodeData::SetManeuverState(class
                //     KSP.Sim.impl.PatchedConicsOrbit)`), and the live vessel orbit is
                //     `Redux.Ecs.Components.CurrentPatchedConicsOrbit` (rank 1: VesselComponent.get_Orbit()
                //     returns KSP.Sim.IKeplerPatch; the ECS component implements the interfaces but is not
                //     the concrete class), so any cast to it throws `InvalidCastException` in flight -
                //     K2-D2 measured exactly that and shipped the fix
                //     (mods/remote/K2D2Redux/.../ManeuverCreator/ManeuverCreator.cs ~:292-360):
                //     `Map3DManeuvers.OnAddManeuver()` - the real "add node" UI path - only calls
                //     SetManeuverState when `IsOnManeuverTrajectory` is true (a node added on top of an
                //     existing maneuver-plan segment). This node is constructed with
                //     `isOnManeuverTrajectory: false` above, i.e. first/only node, so the engine's own
                //     ManeuverPlanComponent.AddNode pipeline resolves `ManeuverTrajectoryPatch` itself
                //     (the field is public and typed PatchedConicsOrbit, filled by the solver, not by us).
                //     `orbit.PatchEndTransition = PatchTransitionType.Maneuver;` went with the
                //     SetManeuverState call: writing the patch transition was the pre-assignment that
                //     pair existed for, and mutation of the live ECS orbit is exactly the kind of
                //     "the game owns this" state the K2D2 oracle leaves alone. The boundary of the
                //     patch the *game* computes is visible in `UpdateNodeDetails`/the solver below,
                //     which is also what step 7 waits on.

                // 6d. the burn, in maneuver-node coordinates.
                nodeData.BurnVector = burnVector;

                // 6e. hand it to the game: AddNodeToVessel resolves the vessel by RelatedSimID, calls
                //     ManeuverPlanComponent.AddNode (_currentNodes.Add -> UpdateNodeDetails -> fire
                //     OnManeuverNodeAdded -> CommitToState), then UpdateActiveNode (IL-verified).
                Game.SpaceSimulation.Maneuvers.AddNodeToVessel(nodeData);
                added = true;
                LastCreatedNode = nodeData;

                // The legacy `_currentNode` (`FlightPlanPlugin.cs:105`) is still written, because the
                // legacy UI read it as "the node I just made" and P5 inherits that expectation. It is
                // the same instance the legacy's coroutine assigned at :508.
                if (FlightPlanPlugin.Instance != null)
                {
                    FlightPlanPlugin.Instance._currentNode = nodeData;
                }
            }
            catch (Exception e)
            {
                addFailure = e.ToString();

                // P8 (launch-17 BL-1, second half): `AddNode` appends the node to `_currentNodes`
                // before it calls `UpdateNodeDetails` (IL-verified), so the exception above can leave
                // a half-added node in the plan. Remove exactly that node, best effort, and log the
                // outcome - the original failure is already captured in `addFailure` and is logged
                // after this block either way.
                if (!added)
                {
                    CleanUpHalfAddedNode(vessel, plan, nodeData);
                }
            }

            if (addFailure != null)
            {
                Error($"CreateNodeAtUT: EXCEPTION while adding the node - the sequence stops here: {addFailure}");
                CreateInFlight = false;
                yield break;
            }

            // 6f. one fixed update, then the map half - guarded, because the map core only exists in
            //     the map scene.
            yield return new WaitForFixedUpdate();
            UpdateGizmoForNode(nodeData.NodeID, "CreateNodeAtUT");

            // 7. the solver's turn (K12): BurnDuration has exactly one writer in the whole assembly -
            //    ManeuverPlanSolver (asm.il: line 1506144, the only `stfld BurnDuration`) - never the
            //    calls above. Wait for it, bounded, and log what happened: this is the measurement.
            int waited = 0;
            while (nodeData.BurnDuration <= 0 && waited < SolverWaitMaxFixedUpdates)
            {
                yield return new WaitForFixedUpdate();
                waited++;
            }
            LogInfo($"CreateNodeAtUT: solver wait {waited} fixed update(s); BurnDuration = {nodeData.BurnDuration:F3} s");
            SpitNode(nodeData, false, "after create");

            string offsetFailure = null;

            try
            {
                // 8. the burn-centred convention (decision D6): the node is moved back by
                //    |offsetFactor| x BurnDuration so the burn straddles it. Guarded on both sides.
                if (burnOffsetFactor != 0)
                {
                    if (nodeData.BurnDuration <= 0)
                    {
                        Warn("CreateNodeAtUT: BurnDuration is still 0 - burn-centred offset skipped, the node stays at the requested UT");
                    }
                    else
                    {
                        double targetUT = nodeData.Time + nodeData.BurnDuration * burnOffsetFactor;
                        if (targetUT < UT)
                        {
                            Warn($"CreateNodeAtUT: burn-centred offset would move the node to {targetUT - UT:F3} s from now (before UT) - skipped");
                        }
                        else
                        {
                            double nodeTimeAdj = nodeData.BurnDuration * burnOffsetFactor;
                            LogInfo($"CreateNodeAtUT: burn-centred offset: Time = {nodeData.Time - UT:F3} s from now, BurnDuration = {nodeData.BurnDuration:F3} s, adjustment = {nodeTimeAdj:F3} s");
                            // The public overload forwards to the private Guid twin, which assigns
                            // Time, calls UpdateNodeDetails, fires OnManeuverNodePositionChanged and
                            // commits - so one call is the whole edit (IL-verified).
                            plan.UpdateTimeOnNode(nodeData, targetUT);
                            LogInfo($"CreateNodeAtUT: Time after offset = {nodeData.Time - UT:F3} s from now");

                            // 9. the legacy re-point (:672-684): moving the node changed what the same
                            //    burn vector does, so the burn is corrected back to the requested
                            //    deltaV. The correction is applied *additively* because
                            //    UpdateChangeOnNode is `node.BurnVector += change` (IL-verified) - the
                            //    correction term is what goes in, not an absolute vector.
                            Vector3d newDeltaV = orbit.BurnVecToDv(nodeData.Time, nodeData.BurnVector);
                            Vector3d deltaDeltaV = requestedDeltaV - newDeltaV;
                            Vector3d newBurnParams = orbit.DeltaVToManeuverNodeCoordinates(nodeData.Time, deltaDeltaV);
                            LogInfo($"CreateNodeAtUT: re-point: deltaV at the new time = {newDeltaV.magnitude:F3} m/s, correction = {deltaDeltaV.magnitude:F3} m/s");
                            plan.UpdateChangeOnNode(nodeData, newBurnParams);
                            LogInfo($"CreateNodeAtUT: BurnVector after re-point = {nodeData.BurnVector:F3} m/s = {nodeData.BurnVector.magnitude:F3} m/s");
                            UpdateGizmoForNode(nodeData.NodeID, "CreateNodeAtUT:re-point");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                offsetFailure = e.ToString();
            }

            if (offsetFailure != null)
            {
                Error($"CreateNodeAtUT: EXCEPTION during the burn-centred offset/re-point - the node exists, the offset did not complete: {offsetFailure}");
            }

            SpitNode(nodeData, false, "create complete");
            LogInfo($"CreateNodeAtUT: complete - Nodes.Count = {Nodes.Count}, NodeID = {nodeData.NodeID}");
            CreateInFlight = false;
        }

        /// <summary>
        /// P8 placement pre-check (launch-17 BL-1). The game computes a node's patch in
        /// `ManeuverPlanComponent.UpdateNodeDetails` as
        /// `GetSimulationObject().Vessel.Orbiter.PatchedConicSolver.FindPatchContainingUT(node.Time)`
        /// and dereferences the result unconditionally at its `IOrbit::get_ReferenceFrame()` call
        /// (IL-verified: `_tmp/p4/Assembly-CSharp.il:1738914-1738915`, the deref at `IL_0035`), so a
        /// burn time beyond the last propagated patch makes the game throw a NullReferenceException
        /// from inside `AddNode` - after `AddNode` has already appended the node.
        ///
        /// Asking the same solver the same question before the add turns that throw into a clean
        /// refusal. The solver is the same instance the game will use: `plan` was resolved from this
        /// vessel's own simulation object (`PlanFor`), and `UpdateNodeDetails` resolves through
        /// `this.GetSimulationObject().Vessel` - the same vessel. The time is the same value the
        /// game will read: `ManeuverNodeData`'s constructor stores its third argument in `Time`
        /// (IL-verified), and that is what `UpdateNodeDetails` passes to `FindPatchContainingUT`.
        ///
        /// Degrades open on purpose: when the orbiter or the solver is not available (not in flight,
        /// no solver yet) it returns null and the game's own add-time behaviour is left in place -
        /// refusing on a state we cannot read would refuse creates the mod has never tested. It also
        /// degrades open if the query itself throws: the body is a scan of the solver's own
        /// trajectory list, which is `initonly` and assigned in the solver's constructor
        /// (IL-verified: `PatchedConicSolver::'<CurrentTrajectory>k__BackingField'` is an initonly
        /// field stored in the ctor, as is `_universeModel`), so it is non-null for any constructed
        /// solver and a throw would mean a state the game has never shown - the add-time call stays
        /// the authority there, exactly as it was before this pre-check existed.
        /// </summary>
        private static string CheckNodePlacement(VesselComponent vessel, double burnUT)
        {
            OrbiterComponent orbiter = vessel == null ? null : vessel.Orbiter;
            if (orbiter == null) return null;

            PatchedConicSolver solver = orbiter.PatchedConicSolver;
            if (solver == null) return null;

            IKeplerPatch patch;
            try
            {
                patch = solver.FindPatchContainingUT(burnUT);
            }
            catch (Exception e)
            {
                // Degrade open - see the summary. A check that throws must not become a new failure
                // surface on the synchronous path (this method is called from the UI thread before
                // the coroutine exists), and the game's own add-time check is the authority anyway.
                Warn($"CreateNodeAtUT: the placement pre-check could not query the orbit solver ({e.GetType().Name}: {e.Message}) - falling through to the game's own add-time check");
                return null;
            }

            if (patch != null) return null;

            return $"CreateNodeAtUT: refused - no orbit patch contains the burn time (UT {burnUT:F1}): " +
                   "it lies beyond the propagated orbit patch, so the game's own node solver " +
                   "(ManeuverPlanComponent.UpdateNodeDetails -> PatchedConicSolver.FindPatchContainingUT) " +
                   "cannot place it and would throw a NullReferenceException; no node was added";
        }

        /// <summary>
        /// P8 (launch-17 BL-1, second half). `ManeuverPlanComponent.AddNode` appends the node to
        /// `_currentNodes` *before* it calls `UpdateNodeDetails` (IL-verified:
        /// `_tmp/p4/Assembly-CSharp.il:1738589-1738593`), so an exception thrown inside
        /// `UpdateNodeDetails` escapes `AddNodeToVessel` with the half-built node already in the
        /// plan - the launch-17 `Nodes.Count` read 0 at the mod's log point, but the node object was
        /// in the plan's private list. `ManeuverPlanComponent.TryGetNode(Guid, out node)` is the
        /// game's own membership test (public, IL-verified) and `RemoveNodes` is this file's single
        /// proven removal path, so the cleanup removes exactly the node this create built.
        ///
        /// Best effort by design: a failed cleanup must not swallow the original add failure, and
        /// both outcomes are logged distinctly (a failed cleanup at `Error`).
        /// </summary>
        private static void CleanUpHalfAddedNode(VesselComponent vessel, ManeuverPlanComponent plan, ManeuverNodeData nodeData)
        {
            bool present;
            try
            {
                present = plan.TryGetNode(nodeData.NodeID, out _);
            }
            catch (Exception scanFailure)
            {
                Warn($"CreateNodeAtUT: cleanup could not read the plan for the half-added node {nodeData.NodeID} ({scanFailure.GetType().Name}: {scanFailure.Message}) - the plan is left alone");
                return;
            }

            if (!present)
            {
                Warn($"CreateNodeAtUT: cleanup - the failed add left no node in the plan (NodeID {nodeData.NodeID} absent); nothing to remove");
                return;
            }

            try
            {
                RemoveNodes(vessel, new List<ManeuverNodeData> { nodeData }, $"CreateNodeAtUT:cleanup[{nodeData.NodeID}]");
                Warn($"CreateNodeAtUT: cleanup - removed the half-added node {nodeData.NodeID} the failed add left in the plan");
            }
            catch (Exception cleanupFailure)
            {
                Error($"CreateNodeAtUT: cleanup FAILED for the half-added node {nodeData.NodeID} ({cleanupFailure.GetType().Name}: {cleanupFailure.Message}) - the node may still be in the plan; delete it by hand before saving");
            }
        }

        // -----------------------------------------------------------------------------------------
        // Settle - fix 1 / fix 3 (verification increment 3): never measure or validate a plan in flight
        // -----------------------------------------------------------------------------------------

        /// <summary>True when the node count was identical for the required number of consecutive
        /// samples (the entry read counting as the first).</summary>
        public static bool LastSettleSettled { get; private set; }

        /// <summary>The last observed `Nodes.Count` - the settled value when
        /// <see cref="LastSettleSettled"/> is true.</summary>
        public static int LastSettleCount { get; private set; }

        /// <summary>True when <see cref="LastSettleCount"/> >= the `expectedMinimum` the call asked for.</summary>
        public static bool LastSettleMetMinimum { get; private set; }

        /// <summary>How many counts were observed (the entry read counts as the first sample).</summary>
        public static int LastSettleSamples { get; private set; }

        /// <summary>How many fixed updates the wait actually cost.</summary>
        public static int LastSettleFixedUpdates { get; private set; }

        /// <summary>
        /// FIX 1 (launch-5, 2026-09-14). Every measurement of the plan - the probe's baseline, the
        /// growth check after a create, and the create path's own snapshot - must not be taken while
        /// the game's `ManeuverPlanComponent.RebuildNodes` coroutine is mid-flight: launch 5 showed the
        /// count going 1 -> 3 -> 2 inside 46 ms, so a single `Nodes.Count` read is not evidence of
        /// anything. This samples `Nodes.Count` once per `WaitForFixedUpdate` and settles when the same
        /// value has been seen <see cref="SettleStableSamples"/> times in a row, or when
        /// <see cref="SettleMaxFixedUpdates"/> fixed updates have passed.
        ///
        /// The FULL trace - every observed count, in order - is logged whichever way it goes: that
        /// trace IS the evidence for the oscillation finding, so it is not summarised away. (`SpitNode`
        /// lines are separate, verbose, and not part of a trace.) `label` is carried in every line, so a
        /// trace is attributable to the exact step that took it.
        ///
        /// A coroutine cannot return a tuple in C# 9, so the outcome is published in the LastSettle*
        /// properties - read them immediately after the yield, because the next settle call overwrites
        /// them.
        /// </summary>
        public static IEnumerator WaitForPlanToSettle(int expectedMinimum, string label)
        {
            yield return WaitForSettledCount(expectedMinimum, label, SettleStableSamples, SettleMaxFixedUpdates);
        }

        /// <summary>
        /// Fix 3's snapshot wait, run at the head of the create coroutine: two consecutive reads that
        /// agree, bounded at <see cref="SnapshotSettleMaxFixedUpdates"/> fixed updates.
        /// </summary>
        private static IEnumerator WaitForSettledCreateSnapshot(string label)
        {
            yield return WaitForSettledCount(0, label, SnapshotStableSamples, SnapshotSettleMaxFixedUpdates);
        }

        /// <summary>
        /// The one settle implementation: sample `Nodes.Count` once per fixed update until
        /// `stableSamplesRequired` consecutive samples agree, or until `maxFixedUpdates` have elapsed.
        /// Publishes the LastSettle* properties and logs the trace.
        /// </summary>
        private static IEnumerator WaitForSettledCount(int expectedMinimum, string label, int stableSamplesRequired, int maxFixedUpdates)
        {
            string tag = "SETTLE" + Tag(label);
            List<int> trace = new List<int>();
            int count = Nodes.Count;
            trace.Add(count);
            int stable = 1;
            int fixedUpdates = 0;
            bool settled = stable >= stableSamplesRequired;

            while (!settled && fixedUpdates < maxFixedUpdates)
            {
                yield return new WaitForFixedUpdate();
                fixedUpdates++;
                int sample = Nodes.Count;
                trace.Add(sample);
                if (sample == count)
                {
                    stable++;
                }
                else
                {
                    count = sample;
                    stable = 1;
                }
                if (stable >= stableSamplesRequired)
                {
                    settled = true;
                }
            }

            LastSettleSettled = settled;
            LastSettleCount = count;
            LastSettleMetMinimum = count >= expectedMinimum;
            LastSettleSamples = trace.Count;
            LastSettleFixedUpdates = fixedUpdates;

            // Built by concatenation (the local idiom) so no BCL generic overload has to resolve.
            string traceText = "";
            for (int i = 0; i < trace.Count; i++)
            {
                if (i > 0)
                {
                    traceText += ", ";
                }
                traceText += trace[i].ToString();
            }

            LogInfo($"{tag}: settled = {settled} after {trace.Count} sample(s) in {fixedUpdates} fixed update(s); expected >= {expectedMinimum} -> {(LastSettleMetMinimum ? "MET" : "NOT MET")}; final Nodes.Count = {count}; count trace (in order) = [{traceText}]");
            if (!settled)
            {
                Warn($"{tag}: the plan did NOT settle within {maxFixedUpdates} fixed update(s) - proceeding on the last observed count ({count}), not on a settled one; the trace above is the evidence");
            }
            if (!LastSettleMetMinimum)
            {
                Warn($"{tag}: final Nodes.Count = {count} < the expected minimum {expectedMinimum}");
            }
        }

        // -----------------------------------------------------------------------------------------
        // Edit - the absolute forms of the two primitives
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Moves a node to an **absolute** UT. `ManeuverPlanComponent.UpdateTimeOnNode(node, time)`
        /// assigns `node.Time = time` (IL-verified), so no read-modify-write dance is needed - unlike
        /// the burn.
        /// </summary>
        public static bool SetNodeTime(ManeuverNodeData node, double absoluteUT)
        {
            ManeuverPlanComponent plan = PlanFor(ActiveVessel());
            if (plan == null || node == null)
            {
                Warn("SetNodeTime: no plan/node - refused");
                return false;
            }

            double UT = CurrentUT();
            if (absoluteUT < UT + MinLeadTime_s)
            {
                Warn($"SetNodeTime: {absoluteUT - UT:F3} s from now is below the {MinLeadTime_s} s floor - clamped");
                absoluteUT = UT + MinLeadTime_s;
            }

            LogInfo($"SetNodeTime: NodeID = {node.NodeID}, Time {node.Time - UT:F3} s -> {absoluteUT - UT:F3} s from now");
            plan.UpdateTimeOnNode(node, absoluteUT);
            UpdateGizmoForNode(node.NodeID, "SetNodeTime");
            return true;
        }

        /// <summary>
        /// Changes a node's burn by an explicit change term. `UpdateChangeOnNode` is
        /// `node.BurnVector += change` (IL-verified), so this is the additive primitive the legacy used
        /// at :681; it also recomputes node details and commits internally.
        /// </summary>
        public static bool ChangeNodeBurn(ManeuverNodeData node, Vector3d burnVectorChange)
        {
            ManeuverPlanComponent plan = PlanFor(ActiveVessel());
            if (plan == null || node == null)
            {
                Warn("ChangeNodeBurn: no plan/node - refused");
                return false;
            }

            LogInfo($"ChangeNodeBurn: NodeID = {node.NodeID}, change = {burnVectorChange:F3} m/s, BurnVector was {node.BurnVector:F3} m/s");
            plan.UpdateChangeOnNode(node, burnVectorChange);
            UpdateGizmoForNode(node.NodeID, "ChangeNodeBurn");
            return true;
        }

        /// <summary>
        /// Sets a node's burn to an **absolute** burn vector. Because the game primitive is additive,
        /// the absolute edit is a read-modify-write: `change = target - node.BurnVector`. This is the
        /// shape a UI numeric field needs (K8).
        /// </summary>
        public static bool SetNodeBurn(ManeuverNodeData node, Vector3d absoluteBurnVector)
        {
            if (node == null)
            {
                Warn("SetNodeBurn: node is null - refused");
                return false;
            }

            Vector3d change = absoluteBurnVector - node.BurnVector;
            LogInfo($"SetNodeBurn: NodeID = {node.NodeID}, BurnVector {node.BurnVector:F3} -> {absoluteBurnVector:F3} m/s (change = {change:F3} m/s, read-modify-write over the additive primitive)");
            return ChangeNodeBurn(node, change);
        }

        // -----------------------------------------------------------------------------------------
        // Delete
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The absorbed `NodeManagerPlugin.DeleteNode(:420)`. One-for-one with the legacy guard: only a
        /// node that is *in the past and off the maneuver trajectory* is removed. A node the player is
        /// still going to burn is kept, and the refusal is logged (KEEP/VERIFY row in the ledger - the
        /// guard is the legacy's, not the port's invention).
        /// </summary>
        public static int DeleteNode(int selectedNodeIndex)
        {
            VesselComponent vessel = ActiveVessel();
            List<ManeuverNodeData> nodes = Nodes;
            if (vessel == null || nodes.Count == 0)
            {
                Warn("DeleteNode: no vessel or no nodes - nothing to do");
                return 0;
            }

            if (selectedNodeIndex + 1 > nodes.Count)
            {
                selectedNodeIndex = Math.Max(0, nodes.Count - 1);
            }

            double UT = CurrentUT();
            ManeuverNodeData node = nodes[selectedNodeIndex];
            List<ManeuverNodeData> toDelete = new List<ManeuverNodeData>();
            if (node.Time < UT && !node.IsOnManeuverTrajectory)
            {
                toDelete.Add(node);
            }
            else
            {
                LogInfo($"DeleteNode: Node {selectedNodeIndex} kept (Time {node.Time - UT:F3} s from now, IsOnManeuverTrajectory = {node.IsOnManeuverTrajectory})");
                return 0;
            }

            return RemoveNodes(vessel, toDelete, $"DeleteNode[{selectedNodeIndex}]");
        }

        /// <summary>
        /// The absorbed `NodeManagerPlugin.DeletePastNodes(:441)`: every node that is off the maneuver
        /// trajectory or already in the past, in one call.
        /// </summary>
        public static int DeletePastNodes()
        {
            VesselComponent vessel = ActiveVessel();
            List<ManeuverNodeData> nodes = Nodes;
            if (vessel == null || nodes.Count == 0)
            {
                Warn("DeletePastNodes: no vessel or no nodes - nothing to do");
                return 0;
            }

            double UT = CurrentUT();
            List<ManeuverNodeData> toDelete = new List<ManeuverNodeData>();
            for (int i = 0; i < nodes.Count; i++)
            {
                ManeuverNodeData node = nodes[i];
                if (!toDelete.Contains(node) && (!node.IsOnManeuverTrajectory || node.Time < UT))
                {
                    toDelete.Add(node);
                }
            }

            if (toDelete.Count == 0)
            {
                LogInfo("DeletePastNodes: no past/off-trajectory nodes");
                return 0;
            }

            return RemoveNodes(vessel, toDelete, "DeletePastNodes");
        }

        /// <summary>
        /// The absorbed `NodeManagerPlugin.DeleteNodes(:458)`: the selected node *and every subsequent
        /// one* (the "this node and subsequent" selector), with the legacy's off-trajectory exception.
        /// </summary>
        public static int DeleteNodes(int selectedNodeIndex)
        {
            VesselComponent vessel = ActiveVessel();
            List<ManeuverNodeData> nodes = Nodes;
            if (vessel == null || nodes.Count == 0)
            {
                Warn("DeleteNodes: no vessel or no nodes - nothing to do");
                return 0;
            }

            if (selectedNodeIndex + 1 > nodes.Count)
            {
                selectedNodeIndex = Math.Max(0, nodes.Count - 1);
            }

            ManeuverNodeData nodeToDelete = nodes[selectedNodeIndex];
            List<ManeuverNodeData> toDelete = new List<ManeuverNodeData>();
            toDelete.Add(nodeToDelete);
            for (int i = 0; i < nodes.Count; i++)
            {
                ManeuverNodeData node = nodes[i];
                if (!toDelete.Contains(node) && (!nodeToDelete.IsOnManeuverTrajectory || nodeToDelete.Time < node.Time))
                {
                    toDelete.Add(node);
                }
            }

            return RemoveNodes(vessel, toDelete, $"DeleteNodes[{selectedNodeIndex}]");
        }

        /// <summary>
        /// The single removal path. `toDelete` must be a **fresh** List, never `GetNodes()`:
        /// `ManeuverPlanComponent.RemoveNodes` enumerates the list it is given while `TryRemoveNode`
        /// does `_currentNodes.RemoveAt(index)` (IL-verified), so passing the live list would throw
        /// "Collection was modified" - which is why the legacy built a new list at every call site.
        /// </summary>
        private static int RemoveNodes(VesselComponent vessel, List<ManeuverNodeData> toDelete, string caller)
        {
            ManeuverProvider maneuvers = Game == null || Game.SpaceSimulation == null ? null : Game.SpaceSimulation.Maneuvers;
            if (maneuvers == null)
            {
                Warn($"{caller}: no SpaceSimulation - nothing removed");
                return 0;
            }

            maneuvers.RemoveNodesFromVessel(vessel.GlobalId, toDelete);
            LogInfo($"{caller}: removed {toDelete.Count} node(s); Nodes.Count = {Nodes.Count}");
            UpdateGizmoForNode(System.Guid.Empty, caller + ":after-remove");
            return toDelete.Count;
        }

        // -----------------------------------------------------------------------------------------
        // Refresh - the absorbed RefreshNodes(:1028) + private RefreshNodeStates(:1060)
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The absorbed `NodeManagerPlugin.RefreshNodes()` coroutine: re-derive every node's details,
        /// re-point each node at the patch that now contains it, wait a fixed update, and do a second
        /// pass. A single pass is not always enough because the game's own fixed-update handler runs in
        /// between (the legacy's own comment at :1040).
        /// </summary>
        public static IEnumerator RefreshNodesCo()
        {
            VesselComponent vessel = ActiveVessel();
            ManeuverPlanComponent plan = PlanFor(vessel);
            if (plan == null)
            {
                Warn("RefreshNodes: no active vessel/plan - nothing to refresh");
                yield break;
            }

            List<ManeuverNodeData> nodes = Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                plan.UpdateNodeDetails(nodes[i]);
            }

            yield return new WaitForFixedUpdate();

            RefreshNodeStates(plan, "RefreshNodes pass 1", false);

            yield return new WaitForFixedUpdate();

            RefreshNodeStates(plan, "RefreshNodes pass 2", true);

            LogInfo($"RefreshNodes: done, Nodes.Count = {Nodes.Count}");
        }

        /// <summary>
        /// The absorbed private `RefreshNodeStates(:1060)`: for every node that sits on a maneuver
        /// patch, ask the component to re-point it at the patch containing its UT. A NullReference
        /// from the game is caught, logged and dumped rather than escaping into the caller's frame
        /// (the legacy's `catch (NullReferenceException)` at :1077).
        /// </summary>
        private static void RefreshNodeStates(ManeuverPlanComponent plan, string label, bool updateNodeDetails)
        {
            List<ManeuverNodeData> nodes = Nodes;
            if (updateNodeDetails)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    plan.UpdateNodeDetails(nodes[i]);
                }
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].ManeuverTrajectoryPatch != null)
                {
                    try
                    {
                        plan.RefreshManeuverNodeState(i);
                    }
                    catch (NullReferenceException e)
                    {
                        Error($"{label}: suppressed NRE for Node {i}: {e}");
                        SpitNode(i, true);
                    }
                }
            }
        }

        // -----------------------------------------------------------------------------------------
        // Policy + logging
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The absorbed `NodeManagerPlugin.AddNode(burnUT = 0)` default-UT policy (:997-1016):
        /// UT + TimeToAp when the orbit is elliptic and there is no node yet, else the last node plus
        /// min(period / 10, 600 s), else 30 s out.
        /// </summary>
        public static double DefaultNodeUT(List<ManeuverNodeData> nodes, VesselComponent vessel, double UT)
        {
            IKeplerPatch orbit = vessel == null ? null : vessel.Orbit;
            if (orbit == null)
            {
                return UT + 30.0;
            }

            if (orbit.eccentricity < 1 && (nodes == null || nodes.Count == 0))
            {
                return UT + orbit.TimeToAp;
            }

            if (nodes != null && nodes.Count > 0)
            {
                return nodes[nodes.Count - 1].Time + Math.Min(orbit.period / 10.0, 600.0);
            }

            return UT + 30.0;
        }

        /// <summary>The absorbed `NodeManagerPlugin.SpitNode(:311,:367)` field dump, as port-local
        /// logging: every field the legacy printed, one line each, all under the `[FlightPlan]`
        /// prefix so a log grep finds the whole picture of a node in one place.</summary>
        public static void SpitNode(int selectedNodeIndex, bool isError = false)
        {
            List<ManeuverNodeData> nodes = Nodes;
            if (selectedNodeIndex < 0 || selectedNodeIndex >= nodes.Count)
            {
                Warn($"SpitNode: Node[{selectedNodeIndex}] does not exist (Nodes.Count = {nodes.Count})");
                return;
            }

            SpitNode(nodes[selectedNodeIndex], isError);
        }

        /// <summary>The node-instance form of SpitNode.</summary>
        public static void SpitNode(ManeuverNodeData node, bool isError = false, string label = null)
        {
            if (node == null)
            {
                Warn($"SpitNode{Tag(label)}: null node");
                return;
            }

            string prefix = $"SpitNode{Tag(label)}: ";
            Action<string> write = isError ? (Action<string>)Error : LogInfo;
            write($"{prefix}NodeID = {node.NodeID}");
            write($"{prefix}NodeName = {node.NodeName}");
            write($"{prefix}Time = {node.Time:F3} s (UT), {node.Time - CurrentUT():F3} s from now");
            write($"{prefix}BurnVector = [{node.BurnVector.x:F3}, {node.BurnVector.y:F3}, {node.BurnVector.z:F3}] = {node.BurnVector.magnitude:F3} m/s");
            write($"{prefix}BurnDuration = {node.BurnDuration:F3} s");
            write($"{prefix}BurnRequiredDV = {node.BurnRequiredDV:F3} m/s");
            write($"{prefix}IsOnManeuverTrajectory = {node.IsOnManeuverTrajectory}");
            write($"{prefix}CachedManeuverPatchEndUT = {node.CachedManeuverPatchEndUT:F3} s");
            write($"{prefix}RelatedSimID = {node.RelatedSimID}");
            write($"{prefix}SimTransform = {(node.SimTransform == null ? "null" : node.SimTransform.Guid)}");
            if (node.ManeuverTrajectoryPatch == null)
            {
                write($"{prefix}ManeuverTrajectoryPatch = null");
            }
            else
            {
                write($"{prefix}ManeuverTrajectoryPatch.StartUT = {node.ManeuverTrajectoryPatch.StartUT:F3}, EndUT = {node.ManeuverTrajectoryPatch.EndUT:F3}");
            }
        }

        // -----------------------------------------------------------------------------------------
        // Debug probe - off unless the config flag says otherwise
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The self-driving node probe: creates one node, waits for the solver, applies the
        /// burn-centred offset, logs the node, edits its time and burn, and logs the same NodeID
        /// again. No UI and no input are needed, and the whole run is one grep.
        ///
        /// The plugin starts it only when the `Debug Section / Self-Driving Node Probe` config entry is
        /// true, and only once per session. It waits (bounded) for a flight vessel and for the map core
        /// so the gizmo half of the sequence is exercised too; if the map never loads it still runs and
        /// says so.
        /// </summary>
        public static IEnumerator SelfDrivingProbeCo()
        {
            const double probeBurnUTLead_s = 60.0;
            const double probeTimeEdit_s = 120.0;
            const float vesselWaitMax_s = 180f;
            const float mapWaitMax_s = 300f;

            LogInfo("PROBE: armed - waiting for a flight vessel (no UI or input needed)");

            VesselComponent vessel = null;
            float waited = 0f;
            while (waited < vesselWaitMax_s)
            {
                vessel = ActiveVessel();
                if (vessel != null && vessel.SimulationObject != null && PlanFor(vessel) != null)
                {
                    break;
                }
                vessel = null;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (vessel == null)
            {
                Warn($"PROBE: no vessel with a maneuver plan after {waited:F0} s - probe abandoned (load a flight scene and set the flag again)");
                yield break;
            }

            LogInfo($"PROBE: vessel found after {waited:F0} s; waiting up to {mapWaitMax_s:F0} s for the map core (open the map to exercise the gizmo path)");
            waited = 0f;
            while (MapCoreOrNull() == null && waited < mapWaitMax_s)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            bool mapLoaded = MapCoreOrNull() != null;
            LogInfo($"PROBE: map loaded = {mapLoaded} after {waited:F0} s");

            double UT = CurrentUT();
            LogInfo($"PROBE: Nodes.Count before = {Nodes.Count}");

            // Create. The production offset factor is used on purpose: this exercises the whole
            // sequence (solver wait -> burn-centred offset -> deltaV re-point), not just the add.
            bool started = CreateNodeAtUT(new Vector3d(0.0, 0.0, 100.0), UT + probeBurnUTLead_s, -0.5);
            if (!started)
            {
                Error("PROBE: node creation was refused - see the CreateNodeAtUT line above");
                yield break;
            }

            float createWaited = 0f;
            while (CreateInFlight && createWaited < 30f)
            {
                createWaited += Time.unscaledDeltaTime;
                yield return null;
            }

            // FIX 1 (verification increment 3): measure the plan only once the count has held still -
            // the same launch-5 reason as the multi-node probe (1 -> 3 -> 2 inside 46 ms).
            yield return WaitForPlanToSettle(1, "single-probe create");

            ManeuverNodeData node = LastCreatedNode;
            if (node == null)
            {
                Error("PROBE: the create sequence did not produce a node");
                yield break;
            }

            LogInfo($"PROBE: create done in {createWaited:F2} s - Nodes.Count = {Nodes.Count}, requested burnUT = UT+{probeBurnUTLead_s:F1}, node Time = UT+{node.Time - CurrentUT():F3}, NodeID = {node.NodeID}");
            SpitNode(node, false, "probe after create");

            // Edit 1: absolute time, +120 s so the change is unmistakable.
            double editUT = node.Time + probeTimeEdit_s;
            LogInfo($"PROBE: edit 1 - moving NodeID {node.NodeID} to UT+{editUT - CurrentUT():F1}");
            SetNodeTime(node, editUT);
            LogInfo($"PROBE: NodeID = {node.NodeID}, Time after the time edit = UT+{node.Time - CurrentUT():F3}");

            // Edit 2: absolute burn, 1.5x the current one, on the *same* node.
            Vector3d targetBurn = new Vector3d(node.BurnVector.x, node.BurnVector.y, node.BurnVector.z * 1.5);
            LogInfo($"PROBE: edit 2 - setting the burn of NodeID {node.NodeID} to {targetBurn:F3} m/s (absolute)");
            SetNodeBurn(node, targetBurn);
            LogInfo($"PROBE: NodeID = {node.NodeID}, BurnVector after the burn edit = {node.BurnVector:F3} m/s = {node.BurnVector.magnitude:F3} m/s");

            SpitNode(node, false, "probe after edits");
            LogInfo($"PROBE COMPLETE: Nodes.Count = {Nodes.Count}, NodeID = {node.NodeID}, Time = UT+{node.Time - CurrentUT():F3}, BurnVector = {node.BurnVector.magnitude:F3} m/s, BurnDuration = {node.BurnDuration:F3} s, mapLoaded = {mapLoaded}");
        }

        // -----------------------------------------------------------------------------------------
        // Multi-node debug probe - off unless the second config flag says otherwise
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The multi-node probe: drives the node service through the paths the single-node probe never
        /// reached (a plan at the cap, the delete paths, `RefreshNodesCo` on a multi-node plan), and
        /// does it inside the game's real 9-node limit.
        ///
        /// Sequence - every step logs under `[FlightPlan]`, so the whole run is one grep:
        ///   a. bounded wait for a flight vessel and the map core (the single-node probe's shape);
        ///   a2. clean slate (verification increment 2): log every pre-existing node with `SpitNode`,
        ///      then delete them all through the service's own delete paths until `Nodes.Count == 0`
        ///      (`DeletePastNodes` first, then `DeleteNodes(0)` for the rest; bounded at
        ///      `MaxNodes + 5` attempts, and an ERROR + abort if the plan will not empty). The fill
        ///      loop therefore always starts from a known baseline of zero - which is what turns its
        ///      `after <= before` check into proof that the create path APPENDS to the vessel's plan
        ///      rather than replacing it (the clean-slate block below carries the full reasoning);
        ///   a3. settle (verification increment 3, fix 1): `WaitForPlanToSettle(0, "post-clean-slate")`
        ///      before the fill loop, and again after every create with `expectedMinimum = before + 1`
        ///      (including the re-create). Each call logs the full count trace, in order, because
        ///      launch 5 measured the count oscillating 1 -> 3 -> 2 inside 46 ms while the game's own
        ///      `ManeuverPlanComponent.RebuildNodes` coroutine was in flight;
        ///   b. create nodes one at a time on the PRODUCTION path
        ///      (`CreateNodeAtUT(burnVector, burnUT, -0.5)`) with a distinct burn UT 300 s apart taken
        ///      from a MONOTONIC index (fix 2: a live `Nodes.Count` used as an index made create #4
        ///      reuse create #2's burn time), and measure each create only once the plan has settled,
        ///      until the service refuses;
        ///   c. the cap test: exactly ONE create call made at `Nodes.Count == MaxNodes`, which the
        ///      service must refuse. It is the only create call the probe ever makes at the cap and
        ///      it is never repeated;
        ///   d. health check: with the plan at the cap, the game's own plan API must still answer
        ///      (`UpdateNodeDetails` on every node, `BurnDuration` on the active node) after two
        ///      fixed updates - a throw is caught and logged as the negative result;
        ///   e. delete path: `DeleteNode(last)` (whose legacy guard keeps a future node - reported
        ///      honestly), then `DeleteNodes(last)` to force a real removal, then one re-create to
        ///      prove the cap released, then cleanup via `DeletePastNodes` + a bounded
        ///      `DeleteNodes(0)` sweep so the vessel is left clean;
        ///   f. refresh path: `RefreshNodesCo` at the cap and again after the re-create;
        ///   g. one `MULTI-NODE PROBE COMPLETE` line carrying every count.
        ///
        /// SAFETY - the whole point of the exercise: K14 measured that a 10th node breaks the game's
        /// own `ManeuverPlanSolver` (`IndexOutOfRangeException` every frame, forever, and saving the
        /// vessel persists it). So: `Nodes.Count >= MaxNodes` is re-checked before every create in
        /// the fill loop; creation is closed permanently on the first refusal or on the cap test, and
        /// no create is ever made after that; the cap test is a single call; and if the cap ever fails
        /// to refuse, the probe closes creation, deletes every node it can and logs a FAIL - it never
        /// goes on creating.
        /// </summary>
        public static IEnumerator MultiNodeProbeCo()
        {
            const double probeBurnLead_s = 300.0;     // first node: UT + 300 s
            const double probeBurnStep_s = 300.0;     // every later node: 300 s after the previous
            const double probeReCreateLead_s = 150.0; // extra +150 s, so the re-create's UT is distinct from the fill loop's last
            const float vesselWaitMax_s = 180f;
            const float mapWaitMax_s = 120f;
            const float createWaitMax_s = 30f;

            int creationsAttempted = 0;
            int creationsSucceeded = 0;
            int creationsRefused = 0;
            int maxCountReached = 0;
            int deletesPerformed = 0;
            int deleteRefusals = 0;
            int refreshesRun = 0;
            bool capRefusalObserved = false;
            bool capTestAccepted = false;
            bool anyStepThrew = false;
            bool creationClosed = false;   // once true, no create call is ever made again
            int startCount = -1;
            int preExistingRemoved = 0;    // the clean-slate phase's tally, reported in the final line
            int nextBurnIndex = 0;         // fix 2: the monotonic burn-UT index - NEVER Nodes.Count

            LogInfo("MULTI-PROBE: armed - waiting for a flight vessel (no UI or input needed)");

            VesselComponent vessel = null;
            float waited = 0f;
            while (waited < vesselWaitMax_s)
            {
                vessel = ActiveVessel();
                if (vessel != null && vessel.SimulationObject != null && PlanFor(vessel) != null)
                {
                    break;
                }
                vessel = null;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (vessel == null)
            {
                Error($"MULTI-NODE PROBE ABORTED: no vessel with a maneuver plan after {waited:F0} s - load a flight scene and set the flag again");
                yield break;
            }

            LogInfo($"MULTI-PROBE: vessel found after {waited:F0} s; waiting up to {mapWaitMax_s:F0} s for the map core (open the map to exercise the gizmo path)");
            waited = 0f;
            while (MapCoreOrNull() == null && waited < mapWaitMax_s)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            bool mapLoaded = MapCoreOrNull() != null;
            LogInfo($"MULTI-PROBE: map loaded = {mapLoaded} after {waited:F0} s");

            double UT = CurrentUT();
            startCount = Nodes.Count;
            LogInfo($"MULTI-PROBE pre-state: Nodes.Count = {startCount}, MaxNodes = {MaxNodes}, UT = {UT:F3}");

            // -------------------------------------------------------------------------------------
            // a2. clean slate (verification increment 2): clear whatever plan the loaded save brought
            //     in, so the fill loop below starts from a KNOWN baseline of zero - no matter which
            //     save the user loads.
            //
            //     Why this matters beyond convenience: a zero baseline is what turns the fill loop's
            //     existing `after <= before` check into an append proof. The first create takes
            //     0 -> 1, which a *replacing* create would also do - but a replacement fails on the
            //     second create, where before = 1 and the count would stay 1, and the check fires.
            //     A run that climbs 0 -> 1 -> ... -> {MaxNodes} can therefore only have been built
            //     by a create that APPENDS to the vessel's plan instead of clobbering it - and that
            //     is a production requirement, not a probe artefact: FlightPlan creates nodes on
            //     vessels that already have some, and a create that silently replaced a live plan
            //     would be a data-loss bug the cap test alone could never see.
            //
            //     The removals go through the service's own delete paths (never a hand-rolled
            //     removal), for the same reason the delete-path step below does: those are the
            //     production paths, and this phase is also their first use on a save the probe did
            //     not create.
            //
            //     Bounded, and fatal if it cannot finish: a plan that will not empty means the
            //     vessel is in a state this probe must not hand nine new nodes to - the launch-4
            //     save (a stale node in the past, with the game's own RebuildNodes coroutine already
            //     throwing) is the precedent.
            // -------------------------------------------------------------------------------------
            const int cleanSlateMaxAttempts = MaxNodes + 5;
            int cleanSlateAttempts = 0;
            if (Nodes.Count > 0)
            {
                List<ManeuverNodeData> preExisting = Nodes;
                LogInfo($"MULTI-PROBE pre-state: {preExisting.Count} pre-existing node(s) - the loaded save's plan, node by node:");
                for (int i = 0; i < preExisting.Count; i++)
                {
                    SpitNode(preExisting[i], false, $"pre-existing node {i}");
                }

                while (Nodes.Count > 0 && cleanSlateAttempts < cleanSlateMaxAttempts)
                {
                    cleanSlateAttempts++;
                    int beforeSlate = Nodes.Count;
                    // DeletePastNodes first - it is the path for the stale/off-trajectory nodes
                    // (launch-4's in-the-past node is exactly its case). DeleteNodes(0) is the
                    // fallback for whatever survives it: it always removes the selected node and
                    // every node after it, so one call clears a list that is only ever walked from
                    // the front.
                    bool tryPastNodes = cleanSlateAttempts == 1;
                    string slateCall = tryPastNodes ? "DeletePastNodes" : "DeleteNodes(0)";
                    int removedSlate = 0;
                    string slateError = null;
                    try
                    {
                        removedSlate = tryPastNodes ? DeletePastNodes() : DeleteNodes(0);
                    }
                    catch (Exception e)
                    {
                        slateError = e.ToString();
                    }

                    if (slateError != null)
                    {
                        anyStepThrew = true;
                        Error($"MULTI-PROBE clean slate {cleanSlateAttempts}: {slateCall} THREW: {slateError}");
                        break;
                    }

                    preExistingRemoved += removedSlate;
                    deletesPerformed += removedSlate;
                    if (removedSlate == 0)
                    {
                        deleteRefusals++;
                    }
                    LogInfo($"MULTI-PROBE clean slate {cleanSlateAttempts}: {slateCall} -> removed {removedSlate}; Nodes.Count = {Nodes.Count} (was {beforeSlate})");

                    if (Nodes.Count == 0)
                    {
                        break;
                    }
                    if (!tryPastNodes && Nodes.Count >= beforeSlate)
                    {
                        // The fallback delete made no progress - stop instead of spinning, and let
                        // the abort check below turn it into a single decisive ERROR.
                        break;
                    }
                    yield return new WaitForFixedUpdate();
                }
            }

            // -------------------------------------------------------------------------------------
            // a3. settle (verification increment 3, fix 1). The clean slate's removals go through the
            //     game's own list and its `ManeuverPlanComponent.RebuildNodes` coroutine can still be
            //     in flight, so a bare `Nodes.Count` read here is not yet evidence that the plan IS
            //     empty. Wait (bounded) for the count to hold still, log the whole trace, and only then
            //     decide - aborting if the settled count is not 0, exactly as before.
            // -------------------------------------------------------------------------------------
            yield return WaitForPlanToSettle(0, "post-clean-slate");

            if (LastSettleCount != 0)
            {
                anyStepThrew = true;
                Error($"MULTI-PROBE ABORTED: clean slate failed - {LastSettleCount} pre-existing node(s) are still present on the settled count (settled = {LastSettleSettled} after {LastSettleSamples} sample(s) in {LastSettleFixedUpdates} fixed update(s); {cleanSlateAttempts} bounded removal attempt(s), limit {cleanSlateMaxAttempts}); NO create call was made. A plan that will not empty is a vessel state that must not be given {MaxNodes} new nodes (see the removal lines above; launch-4's stale-node abort is the precedent). Delete the remaining nodes by hand and re-arm the probe.");
                yield break;
            }

            LogInfo($"MULTI-PROBE clean slate: Nodes.Count = 0 (pre-existing nodes removed = {preExistingRemoved})");

            // -------------------------------------------------------------------------------------
            // b. create one node at a time until the cap is reached (the cap test is a separate,
            //    single call below). The hard self-check stands in front of every create.
            // -------------------------------------------------------------------------------------
            while (!creationClosed)
            {
                int before = Nodes.Count;

                // Hard self-check (requirement: never make the call that could create a 10th node).
                // The fill loop stops here; the single cap-test call below is the only one made at
                // the cap and it exists so the service's own refusal can be observed.
                if (before >= MaxNodes)
                {
                    LogInfo($"MULTI-PROBE fill: Nodes.Count = {before} == MaxNodes = {MaxNodes} - fill loop stopped, no create call made");
                    break;
                }

                // FIX 2 (verification increment 3): the burn UT's index is a counter that only ever
                // increases. It used to be `Nodes.Count`, and because the live count oscillated
                // (launch 5: 1 -> 3 -> 2) create #4 landed on UT+600 - byte-for-byte create #2's burn
                // time, 3.2 s from a live node at UT+596.8. That collision is what the production 30 s
                // proximity rule was left to catch. With a monotonic index, two consecutive creates
                // cannot collide on a burn time at all.
                int burnIndex = nextBurnIndex;
                nextBurnIndex++;
                double burnUT = UT + probeBurnLead_s + probeBurnStep_s * burnIndex;
                creationsAttempted++;
                LogInfo($"MULTI-PROBE create #{creationsAttempted}: CreateNodeAtUT(prograde 100 m/s, burnUT = UT+{burnUT - UT:F1} s, burn index = {burnIndex}, offset = -0.5) with Nodes.Count = {before}");

                bool started = false;
                string callError = null;
                try
                {
                    started = CreateNodeAtUT(new Vector3d(0.0, 0.0, 100.0), burnUT, -0.5);
                }
                catch (Exception e)
                {
                    callError = e.ToString();
                }

                if (callError != null)
                {
                    anyStepThrew = true;
                    Error($"MULTI-PROBE create #{creationsAttempted}: EXCEPTION out of CreateNodeAtUT - creation closed: {callError}");
                    creationClosed = true;
                    break;
                }

                if (!started)
                {
                    creationsRefused++;
                    int atRefusal = Nodes.Count;
                    LogInfo($"MULTI-PROBE create #{creationsAttempted} REFUSED: Nodes.Count = {atRefusal}, MaxNodes = {MaxNodes}; the service's own refusal line above names the branch - with Nodes.Count == MaxNodes that is the cap branch");
                    if (atRefusal >= MaxNodes)
                    {
                        capRefusalObserved = true;
                    }
                    creationClosed = true;
                    break;
                }

                float createdWaited = 0f;
                while (CreateInFlight && createdWaited < createWaitMax_s)
                {
                    createdWaited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (CreateInFlight)
                {
                    anyStepThrew = true;
                    Error($"MULTI-PROBE create #{creationsAttempted}: the create coroutine did not finish within {createWaitMax_s:F0} s - creation closed");
                    creationClosed = true;
                    break;
                }

                // The additivity proof (see the clean-slate block above): the plan started this loop
                // EMPTY, so a correct create appends exactly one node to whatever is there.
                // `after <= before` thus does double duty - it is a growth check *and* a clobber
                // check: a create that REPLACED the plan would pass on the first node (0 -> 1) but
                // fail here on the second (before = 1, after = 1), so a run that climbs
                // 0 -> 1 -> ... -> MaxNodes can only have been built additively.
                //
                // FIX 1 (verification increment 3): `after` is now the count measured AFTER the plan
                // has been seen to hold still for three consecutive samples. Launch 5 read 1 -> 3 -> 2
                // inside 46 ms, so a bare read here can report movement that is really the game's
                // `RebuildNodes` coroutine mid-flight. The comparison itself is unchanged.
                yield return WaitForPlanToSettle(before + 1, $"create #{creationsAttempted}");
                int after = LastSettleCount;
                ManeuverNodeData created = LastCreatedNode;
                if (after <= before)
                {
                    anyStepThrew = true;
                    Error($"MULTI-PROBE create #{creationsAttempted}: CreateNodeAtUT returned true but Nodes.Count did not grow ({before} -> {after}) - creation closed [settled = {LastSettleSettled}, samples = {LastSettleSamples}, {LastSettleFixedUpdates} fixed update(s)]");
                    creationClosed = true;
                    break;
                }
                if (!LastSettleSettled)
                {
                    Warn($"MULTI-PROBE create #{creationsAttempted}: Nodes.Count grew {before} -> {after}, but the plan never held still for {SettleStableSamples} consecutive samples - {after} is the last observed count, not a settled one");
                }

                creationsSucceeded++;
                if (after > maxCountReached)
                {
                    maxCountReached = after;
                }
                LogInfo($"MULTI-PROBE create #{creationsAttempted} OK: Nodes.Count = {after} (was {before}), NodeID = {(created == null ? "null" : created.NodeID.ToString())}, Time = UT+{(created == null ? 0.0 : created.Time - CurrentUT()):F3}, BurnDuration = {(created == null ? 0.0 : created.BurnDuration):F3} s");
                yield return new WaitForFixedUpdate();
            }

            // -------------------------------------------------------------------------------------
            // c. the cap test: the single deliberate create call made at the cap. Whatever it says,
            //    creation is closed after it - the probe never tries to create a 10th node twice.
            // -------------------------------------------------------------------------------------
            if (!creationClosed && Nodes.Count == MaxNodes)
            {
                int atCap = Nodes.Count;
                creationsAttempted++;
                LogInfo($"MULTI-PROBE CAP TEST: Nodes.Count = {atCap} == MaxNodes = {MaxNodes}; making the single deliberate create call that the cap must refuse - no create call is ever made at the cap again");

                bool capStarted = false;
                string capError = null;
                try
                {
                    capStarted = CreateNodeAtUT(new Vector3d(0.0, 0.0, 100.0), UT + probeBurnLead_s + probeBurnStep_s * atCap, -0.5);
                }
                catch (Exception e)
                {
                    capError = e.ToString();
                }
                creationClosed = true;

                if (capError != null)
                {
                    anyStepThrew = true;
                    Error($"MULTI-PROBE CAP TEST: EXCEPTION out of CreateNodeAtUT: {capError}");
                }
                else if (capStarted)
                {
                    capTestAccepted = true;
                    Error($"MULTI-PROBE CAP TEST FAILED: the create was ACCEPTED at Nodes.Count = {atCap} == MaxNodes = {MaxNodes}. The cap did not protect the runtime; creation is closed and the probe will delete every node it can. THIS IS A FAIL - report the whole log.");
                }
                else
                {
                    creationsRefused++;
                    int afterRefusal = Nodes.Count;
                    if (afterRefusal == atCap && afterRefusal == MaxNodes)
                    {
                        capRefusalObserved = true;
                        LogInfo($"MULTI-PROBE CAP REFUSAL CONFIRMED: create #{creationsAttempted} returned false and Nodes.Count = {afterRefusal} is still == MaxNodes = {MaxNodes} - the service's refusal line above is the cap branch. The 10th node was NOT created.");
                    }
                    else
                    {
                        Warn($"MULTI-PROBE CAP TEST: create returned false but Nodes.Count moved {atCap} -> {afterRefusal} - inspect the service's refusal line above");
                    }
                }
            }
            else
            {
                Error($"MULTI-PROBE CAP TEST SKIPPED: creation closed early at Nodes.Count = {Nodes.Count} < MaxNodes = {MaxNodes} (see the refusal/exception line above) - the cap refusal is NOT proven by this run");
            }

            // The cap failure path: best-effort cleanup, because a node beyond the cap breaks the
            // game's solver for as long as the vessel exists. The log above stays the evidence.
            if (capTestAccepted)
            {
                float emergencyWaited = 0f;
                while (CreateInFlight && emergencyWaited < createWaitMax_s)
                {
                    emergencyWaited += Time.unscaledDeltaTime;
                    yield return null;
                }
                yield return new WaitForFixedUpdate();

                int removedEmergency = 0;
                string emergencyError = null;
                try
                {
                    removedEmergency = DeleteNodes(0);
                }
                catch (Exception e)
                {
                    emergencyError = e.ToString();
                }

                if (emergencyError != null)
                {
                    Error($"MULTI-PROBE EMERGENCY CLEANUP threw: {emergencyError}");
                }
                else
                {
                    deletesPerformed += removedEmergency;
                    Error($"MULTI-PROBE EMERGENCY CLEANUP: removed {removedEmergency} node(s); Nodes.Count = {Nodes.Count}. This ran because the cap did not refuse - report it.");
                }
            }

            // -------------------------------------------------------------------------------------
            // d. health check: with the plan at the cap the game's plan API must still answer.
            // -------------------------------------------------------------------------------------
            if (Nodes.Count > 0)
            {
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();

                int healthCount = -1;
                double activeDuration = -1.0;
                string activeId = "none";
                string healthError = null;
                bool detailsOk = false;
                try
                {
                    ManeuverPlanComponent plan = PlanFor(ActiveVessel());
                    List<ManeuverNodeData> nodes = Nodes;
                    healthCount = nodes.Count;
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        plan.UpdateNodeDetails(nodes[i]);
                    }
                    detailsOk = true;
                    ManeuverNodeData active = ActiveNode;
                    if (active != null)
                    {
                        activeId = active.NodeID.ToString();
                        activeDuration = active.BurnDuration;
                    }
                }
                catch (Exception e)
                {
                    healthError = e.ToString();
                }

                if (healthError != null)
                {
                    anyStepThrew = true;
                    Error($"MULTI-PROBE HEALTH CHECK: the maneuver plan THREW with {healthCount} node(s) present - the negative result: {healthError}");
                }
                else
                {
                    LogInfo($"MULTI-PROBE HEALTH CHECK: the plan answered with Nodes.Count = {healthCount}, UpdateNodeDetails OK on all nodes = {detailsOk}, active NodeID = {activeId}, BurnDuration = {activeDuration:F3} s");
                }
                SpitNode(ActiveNode, false, "multi-probe active node at cap");
            }

            // -------------------------------------------------------------------------------------
            // f (first refresh): RefreshNodesCo on a genuine multi-node plan (the cap).
            // -------------------------------------------------------------------------------------
            if (Nodes.Count > 1)
            {
                int beforeRefresh = Nodes.Count;
                bool refreshThrew = false;
                LogInfo($"MULTI-PROBE refresh (pre-delete, multi-node): RefreshNodesCo with Nodes.Count = {beforeRefresh}");
                yield return GuardedCo(RefreshNodesCo(), delegate (Exception e)
                {
                    refreshThrew = true;
                    anyStepThrew = true;
                    Error($"MULTI-PROBE refresh (pre-delete): RefreshNodesCo THREW: {e}");
                });
                if (!refreshThrew)
                {
                    refreshesRun++;
                    LogInfo($"MULTI-PROBE refresh (pre-delete): done, Nodes.Count = {Nodes.Count} (was {beforeRefresh})");
                }
            }
            else
            {
                Warn($"MULTI-PROBE refresh (pre-delete) SKIPPED: Nodes.Count = {Nodes.Count} is not multi-node");
            }

            // -------------------------------------------------------------------------------------
            // e. delete path: DeleteNode, then the forced DeleteNodes removal, then one re-create.
            // -------------------------------------------------------------------------------------
            if (Nodes.Count > 0)
            {
                List<ManeuverNodeData> nodeList = Nodes;
                int index = nodeList.Count - 1;
                string flags = "";
                for (int i = 0; i < nodeList.Count; i++)
                {
                    if (i > 0)
                    {
                        flags += ", ";
                    }
                    flags += $"{i}:{nodeList[i].IsOnManeuverTrajectory}";
                }
                LogInfo($"MULTI-PROBE delete: Nodes.Count = {nodeList.Count}, IsOnManeuverTrajectory per index = [{flags}]; DeleteNode({index}) first (the legacy guard is expected to keep a future node)");

                int beforeDelete = Nodes.Count;
                int removedByDeleteNode = 0;
                string deleteNodeError = null;
                try
                {
                    removedByDeleteNode = DeleteNode(index);
                }
                catch (Exception e)
                {
                    deleteNodeError = e.ToString();
                }

                if (deleteNodeError != null)
                {
                    anyStepThrew = true;
                    Error($"MULTI-PROBE delete: DeleteNode({index}) THREW: {deleteNodeError}");
                }
                else
                {
                    deletesPerformed += removedByDeleteNode;
                    if (removedByDeleteNode == 0)
                    {
                        deleteRefusals++;
                    }
                    LogInfo($"MULTI-PROBE delete: DeleteNode({index}) -> removed {removedByDeleteNode}; Nodes.Count = {Nodes.Count} (was {beforeDelete})");
                }

                if (Nodes.Count == beforeDelete)
                {
                    int beforeForce = Nodes.Count;
                    int removedByDeleteNodes = 0;
                    string deleteNodesError = null;
                    try
                    {
                        removedByDeleteNodes = DeleteNodes(index);
                    }
                    catch (Exception e)
                    {
                        deleteNodesError = e.ToString();
                    }

                    if (deleteNodesError != null)
                    {
                        anyStepThrew = true;
                        Error($"MULTI-PROBE delete: DeleteNodes({index}) THREW: {deleteNodesError}");
                    }
                    else
                    {
                        deletesPerformed += removedByDeleteNodes;
                        if (removedByDeleteNodes == 0)
                        {
                            deleteRefusals++;
                        }
                        LogInfo($"MULTI-PROBE delete: DeleteNodes({index}) -> removed {removedByDeleteNodes}; Nodes.Count = {Nodes.Count} (was {beforeForce})");
                    }
                }

                int beforeReCreate = Nodes.Count;
                if (beforeReCreate < MaxNodes)
                {
                    // FIX 2: the re-create's burn UT also comes from the monotonic index, for the same
                    // reason as the fill loop's - `index` is `Nodes.Count - 1`, a live count.
                    int reBurnIndex = nextBurnIndex;
                    nextBurnIndex++;
                    double reCreateUT = UT + probeBurnLead_s + probeBurnStep_s * reBurnIndex + probeReCreateLead_s;
                    creationsAttempted++;
                    LogInfo($"MULTI-PROBE re-create: CreateNodeAtUT(prograde 100 m/s, burnUT = UT+{reCreateUT - UT:F1} s, burn index = {reBurnIndex}, offset = -0.5) with Nodes.Count = {beforeReCreate} < MaxNodes = {MaxNodes} (the cap must have released for this to succeed)");

                    bool reStarted = false;
                    string reCreateError = null;
                    try
                    {
                        reStarted = CreateNodeAtUT(new Vector3d(0.0, 0.0, 100.0), reCreateUT, -0.5);
                    }
                    catch (Exception e)
                    {
                        reCreateError = e.ToString();
                    }

                    if (reCreateError != null)
                    {
                        anyStepThrew = true;
                        Error($"MULTI-PROBE re-create: EXCEPTION out of CreateNodeAtUT: {reCreateError}");
                    }
                    else if (!reStarted)
                    {
                        creationsRefused++;
                        Error($"MULTI-PROBE re-create REFUSED at Nodes.Count = {beforeReCreate} < MaxNodes = {MaxNodes} - the cap was not the reason, so the delete path did not release the plan (see the service's refusal line above)");
                    }
                    else
                    {
                        float reWaited = 0f;
                        while (CreateInFlight && reWaited < createWaitMax_s)
                        {
                            reWaited += Time.unscaledDeltaTime;
                            yield return null;
                        }
                        // FIX 1: the re-create is a create too, so its growth is measured the same way.
                        yield return WaitForPlanToSettle(beforeReCreate + 1, "re-create");
                        int afterReCreate = LastSettleCount;
                        if (afterReCreate > beforeReCreate)
                        {
                            creationsSucceeded++;
                            if (afterReCreate > maxCountReached)
                            {
                                maxCountReached = afterReCreate;
                            }
                            LogInfo($"MULTI-PROBE re-create OK: Nodes.Count = {afterReCreate} (was {beforeReCreate}), NodeID = {(LastCreatedNode == null ? "null" : LastCreatedNode.NodeID.ToString())} - the cap released after a delete");
                        }
                        else
                        {
                            anyStepThrew = true;
                            Error($"MULTI-PROBE re-create: CreateNodeAtUT returned true but Nodes.Count did not grow ({beforeReCreate} -> {afterReCreate}) [settled = {LastSettleSettled}, samples = {LastSettleSamples}, {LastSettleFixedUpdates} fixed update(s)]");
                        }
                    }
                }
                else
                {
                    Warn($"MULTI-PROBE re-create SKIPPED: Nodes.Count = {beforeReCreate} is still at the cap (the deletes removed nothing) - the cap-release check cannot run");
                }
            }
            else
            {
                Warn("MULTI-PROBE delete path SKIPPED: there are no nodes to delete");
            }

            // -------------------------------------------------------------------------------------
            // f (second refresh): after the delete/re-create.
            // -------------------------------------------------------------------------------------
            if (Nodes.Count > 0)
            {
                int beforeRefresh2 = Nodes.Count;
                bool refresh2Threw = false;
                LogInfo($"MULTI-PROBE refresh (post-re-create): RefreshNodesCo with Nodes.Count = {beforeRefresh2}");
                yield return GuardedCo(RefreshNodesCo(), delegate (Exception e)
                {
                    refresh2Threw = true;
                    anyStepThrew = true;
                    Error($"MULTI-PROBE refresh (post-re-create): RefreshNodesCo THREW: {e}");
                });
                if (!refresh2Threw)
                {
                    refreshesRun++;
                    LogInfo($"MULTI-PROBE refresh (post-re-create): done, Nodes.Count = {Nodes.Count} (was {beforeRefresh2})");
                }
            }

            // -------------------------------------------------------------------------------------
            // e (cleanup). Leave the vessel clean: no probe may leave a pile of nodes behind.
            // DeletePastNodes covers the second legacy delete entry point; DeleteNodes(0) then
            // clears what is left (it always removes the selected node and every node after it).
            // -------------------------------------------------------------------------------------
            if (Nodes.Count > 0)
            {
                int beforeCleanup = Nodes.Count;
                int removedPast = 0;
                string pastError = null;
                try
                {
                    removedPast = DeletePastNodes();
                }
                catch (Exception e)
                {
                    pastError = e.ToString();
                }

                if (pastError != null)
                {
                    anyStepThrew = true;
                    Error($"MULTI-PROBE cleanup: DeletePastNodes THREW: {pastError}");
                }
                else
                {
                    deletesPerformed += removedPast;
                    LogInfo($"MULTI-PROBE cleanup: DeletePastNodes -> removed {removedPast}; Nodes.Count = {Nodes.Count} (was {beforeCleanup})");
                }

                int sweep = 0;
                while (Nodes.Count > 0 && sweep < 6)
                {
                    sweep++;
                    int beforeSweep = Nodes.Count;
                    int removedSweep = 0;
                    string sweepError = null;
                    try
                    {
                        removedSweep = DeleteNodes(0);
                    }
                    catch (Exception e)
                    {
                        sweepError = e.ToString();
                    }

                    if (sweepError != null)
                    {
                        anyStepThrew = true;
                        Error($"MULTI-PROBE cleanup sweep {sweep}: DeleteNodes(0) THREW: {sweepError}");
                        break;
                    }

                    deletesPerformed += removedSweep;
                    if (removedSweep == 0)
                    {
                        deleteRefusals++;
                    }
                    LogInfo($"MULTI-PROBE cleanup sweep {sweep}: DeleteNodes(0) -> removed {removedSweep}; Nodes.Count = {Nodes.Count} (was {beforeSweep})");
                    if (Nodes.Count >= beforeSweep)
                    {
                        break;
                    }
                    yield return new WaitForFixedUpdate();
                }
            }

            int finalCount = Nodes.Count;
            if (finalCount != 0)
            {
                anyStepThrew = true;
                Error($"MULTI-PROBE: the vessel is NOT clean - {finalCount} node(s) remain after the cleanup; delete them manually before saving");
            }

            LogInfo($"MULTI-NODE PROBE COMPLETE: creations attempted = {creationsAttempted}, creations succeeded = {creationsSucceeded}, creations refused = {creationsRefused} (expect exactly 1, the cap test), max count reached = {maxCountReached} (expect {MaxNodes}), cap refusal observed = {capRefusalObserved}, cap test accepted a 10th node = {capTestAccepted}, deletes performed = {deletesPerformed}, delete refusals = {deleteRefusals}, refreshes run = {refreshesRun}, start Nodes.Count = {startCount}, pre-existing nodes removed = {preExistingRemoved}, final Nodes.Count = {finalCount} (expect 0), any step threw = {anyStepThrew}");
            LogInfo($"MULTI-NODE PROBE COMPLETE: map loaded = {mapLoaded}, UT = {UT:F3}");
        }

        /// <summary>
        /// Runs a nested coroutine with its exception surfaced as a `[FlightPlan]` line. `yield return`
        /// cannot sit inside a try/catch, so the `MoveNext` call is wrapped here instead and the
        /// caller's callback sets its own error flag - the same error-flag shape the create sequence
        /// uses. Without this, a throw inside `RefreshNodesCo` would kill the probe coroutine silently
        /// (Unity logs it, but with no `[FlightPlan]` prefix and no final summary line).
        /// </summary>
        private static IEnumerator GuardedCo(IEnumerator inner, Action<Exception> onError)
        {
            while (true)
            {
                object current = null;
                bool hasNext = false;
                bool failed = false;
                try
                {
                    hasNext = inner.MoveNext();
                    if (hasNext)
                    {
                        current = inner.Current;
                    }
                }
                catch (Exception e)
                {
                    failed = true;
                    if (onError != null)
                    {
                        onError(e);
                    }
                }

                if (failed || !hasNext)
                {
                    yield break;
                }

                yield return current;
            }
        }

        // -----------------------------------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------------------------------

        private static GameInstance Game
        {
            get { return FlightPlanPlugin.Game; }
        }

        /// <summary>
        /// The active vessel, resolved per call. The plugin's `_activeVessel` is only refreshed from
        /// OnGUI, so a service call from a message handler or a probe must not depend on a frame that
        /// has rendered. This is the same expression FPUtility already uses.
        /// </summary>
        private static VesselComponent ActiveVessel()
        {
            FPUtility.RefreshActiveVesselAndCurrentManeuver();
            return FPUtility.ActiveVessel;
        }

        private static ManeuverPlanComponent PlanFor(VesselComponent vessel)
        {
            SimulationObjectModel simObject = vessel == null ? null : vessel.SimulationObject;
            return simObject == null ? null : simObject.FindComponent<ManeuverPlanComponent>();
        }

        /// <summary>
        /// `Game.Map` exists in every scene, but its core is only built for the map: `IsLoaded` plus
        /// `TryGetMapCore` is the guard the whole file funnels through, and a null here is always
        /// logged, never dereferenced.
        /// </summary>
        private static MapCore MapCoreOrNull()
        {
            GameInstance game = Game;
            MapProvider map = game == null ? null : game.Map;
            if (map == null || !map.IsLoaded)
            {
                return null;
            }

            MapCore mapCore = null;
            return map.TryGetMapCore(out mapCore) ? mapCore : null;
        }

        /// <summary>
        /// The map half of the create sequence and the follow-up for every edit that moves a node,
        /// guarded. `System.Guid.Empty` means "the set changed, not one node" - the data is re-fetched
        /// for all vessels and no single gizmo is repositioned.
        /// </summary>
        private static void UpdateGizmoForNode(System.Guid nodeId, string caller)
        {
            MapCore mapCore = MapCoreOrNull();
            if (mapCore == null)
            {
                LogInfo($"{caller}: map core not loaded - gizmo update skipped for node {(nodeId == System.Guid.Empty ? "(all)" : nodeId.ToString())}");
                return;
            }

            mapCore.map3D.ManeuverManager.GetNodeDataForVessels();
            if (nodeId != System.Guid.Empty)
            {
                mapCore.map3D.ManeuverManager.UpdatePositionForGizmo(nodeId);
            }
            LogInfo($"{caller}: gizmo updated for node {(nodeId == System.Guid.Empty ? "(all)" : nodeId.ToString())}");
        }

        private static string Tag(string label)
        {
            return string.IsNullOrEmpty(label) ? "" : "[" + label + "]";
        }

        private static void LogInfo(string message)
        {
            ILogger logger = Log;
            if (logger != null)
            {
                logger.LogInfo("[FlightPlan] " + message);
            }
        }

        private static void Warn(string message)
        {
            ILogger logger = Log;
            if (logger != null)
            {
                logger.LogWarning("[FlightPlan] " + message);
            }
        }

        private static void Error(string message)
        {
            ILogger logger = Log;
            if (logger != null)
            {
                logger.LogError("[FlightPlan] " + message);
            }
        }
    }
}
