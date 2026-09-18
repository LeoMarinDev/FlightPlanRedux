using System;
using System.Collections.Generic;
using System.Reflection;
using KSP.Game;
using KSP.Messages;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using ReduxLib.Logging;
using SpaceWarp2.API.Mods;

namespace FlightPlan
{
    /// <summary>
    /// The K2-D2 integration: discovery by `mod_id`, the four-method reflection binding, the
    /// Execute/Stop call sequence with its preconditions, the ~5 Hz status poll, and the terminal
    /// classifier.
    ///
    /// Design record (evidence: `research/notes/phase-9-k2d2-api.md`; `research/port-maneuver-research.md` §4):
    ///
    ///  * **Discovery** is `PluginList.TryGetDescriptor("K2D2")`. The loader keys plugins by their
    ///    `swinfo.json` `mod_id`; the legacy `com.github.cfloutier.k2d2` key read through BepInEx's
    ///    `Chainloader.PluginInfos` is BepInEx-only and misses entirely on Redux. `Outdated` /
    ///    `Unsupported` are set by Redux itself and are read, never "fixed".
    ///  * **Binding** is reflection on `SpaceWarpPluginDescriptor.Plugin` - a public *field* (never
    ///    reflect `get_Plugin`). `k2d2Loaded` is "all four members non-null", which is also the
    ///    "K2-D2 present but older" test. There is no compile-time reference and no `swinfo`
    ///    dependency, so FlightPlan loads and works without K2-D2 minus the Execute button.
    ///  * **`FlyNode()` flies the earliest-`Time` node of the active vessel**, not "our" node:
    ///    `NodeExPilot` resolves `GetNextManeuveurNode()` → `ManeuverProvider.GetNodesForVessel` →
    ///    `ManeuverPlanComponent.GetNodes()`, and `GetNodes()` *sorts* the plan (`ManeuverNodeData`
    ///    is `IComparable` by `Time`). The click is therefore refused unless the selected node's
    ///    `NodeID` equals `ManeuverPlanComponent.ActiveNode.NodeID`, and the refusal names the node
    ///    that would have flown.
    ///  * **Every invoke is wrapped** in `try/catch (TargetInvocationException)`: a latent NRE
    ///    inside K2-D2 (`KSPVessel.GetNextManeuveurNode` dereferences a null node list) escapes the
    ///    reflection `Invoke` that way.
    ///  * **`GetStatus()`'s `"Done"` is NOT a success signal.** It is returned for a completed burn,
    ///    for a `StopFlyNode()`, and after an `onReset()` alike - K2-D2 exposes no success/failure
    ///    discriminator. The outcome is therefore classified from three independent signals (S1 the
    ///    node list moved on, S2 a burn message was observed, S3 the vessel's orbit matches the
    ///    node's planned patch) and reported as *evidence*. The word "Success" is never printed.
    ///  * Nothing here ever touches K2-D2's own folder, config or window. `FlyNode()` also
    ///    aborts any running K2-D2 pilot and un-pauses the game - that is K2-D2's own contract and
    ///    is surfaced to the user rather than prevented.
    /// </summary>
    public class FPOtherModsInterface
    {
        // Discovery is keyed by the plugin GUID, which for a Redux mod is its swinfo `mod_id`.
        // K2-D2 ships `"mod_id": "K2D2"`; the loader confirms it in the live log
        // (`[Space Warp] Attempting to register mod: K2D2, K2-D2`).
        private const string K2D2ModId = "K2D2";

        public static FPOtherModsInterface instance;

        // Read by the UI to degrade gracefully (F59): the Execute-node control is hidden when false.
        public static bool k2d2Loaded = false;

        // The discovery product: the descriptor, plus the reflection handles bound on its `Plugin`.
        public static SpaceWarpPluginDescriptor K2D2Descriptor;

        private static object k2d2Plugin;
        private static MethodInfo K2D2FlyNodeMethodInfo;
        private static MethodInfo K2D2StopFlyNodeMethodInfo;
        private static MethodInfo K2D2IsFlyNodeRunningMethodInfo;
        private static MethodInfo K2D2GetStatusMethodInfo;
        // UI-fix increment 3 (launch-9 user request): reflection handles for opening K2-D2's own
        // window when Execute starts. `main_window` is a private instance field of K2-D2's plugin
        // class (K2D2_Plugin.cs:65 in the read-only in-repo reference); its public `IsWindowOpen`
        // setter shows the window AND syncs K2-D2's own AppBar toggle. Same reflection-only contract
        // as the four binds above - never a hard reference - and a miss only loses the convenience.
        private static FieldInfo K2D2MainWindowFieldInfo;
        private static PropertyInfo K2D2WindowOpenPropertyInfo;
        private static bool windowOpenHookMissingLogged = false;
        private static string k2d2Version = "unknown";

        // ================================ the run state ========================================
        // `checkK2D2status` is the legacy name for "a run is armed": set by CallK2D2 after a
        // successful invoke, cleared by the terminal classification or by a failure. The UI polls
        // only while it is set.
        public static bool checkK2D2status = false;

        // The line the UI renders in the `K2D2Status` label. It is a *phase prefix* while a run is
        // live (`Turning:` / `Warping:` / ...) and the classified outcome at terminal.
        public static string k2d2Status = "";

        // Terminal/refusal lines linger in the label for this long (unscaled seconds) so the user
        // can read them; they then hide again.
        private const float StatusLingerSeconds = 20f;
        private static float statusVisibleUntil = 0f;

        // UI-fix increment 4 (launch-10 defect B): a terminal evidence line is the whole point of the
        // burn-evidence feature, and a 20 s timer was too short to read it after the burn. Every
        // terminal/refusal `SetStatus(..., linger: true)` call also sets this flag, which marks the
        // row as the *current* terminal line: a new run supersedes it (the run-start block) and
        // ResetRun() clears it. The intermediate "stopping..." write at the Stop path keeps the
        // timer only - it is not a terminal line, so it is not pinned.
        //
        // P8 (launch-17 backlog, the user's request): the pin is no longer permanent. A pinned line
        // is readable for StatusPinnedSeconds and then hides on its own - the row used to stay up
        // until the next Execute, which is the persistence the user reported.
        //
        // An early "the flown node is gone from the plan" clear was designed and then NOT shipped:
        // launch 17's own S1 evidence (Deploy/obj/launch-17-log-archive/Ksp2-2-launch17.log
        // 22:19:23-22:23:42) shows the game has already removed the flown node at terminal
        // classification in 6 of 7 successful runs (`now=0, flown node still present=False`), so
        // that clear would have hidden the terminal evidence line about a second after it appeared -
        // the opposite of what the pin exists for. The bounded window alone is what the user asked
        // for ("disappeared automatically after some time").
        private static bool statusPinned = false;

        // The pinned line's own wall-clock window (unscaled seconds, the same convention as the
        // linger timer below: K2-D2 warps and UT is paused on `pause_on_end`). 60 s is 3x the 20 s
        // linger that was already too short to read a terminal line after a burn, so a terminal
        // line stays readable for a minute - and is never permanent.
        private const float StatusPinnedSeconds = 60f;
        private static float statusPinnedUntil = 0f;

        // ~5 Hz. Timed on unscaled wall-clock seconds, not UT: K2-D2 warps, and UT is paused on
        // `pause_on_end` - neither may be allowed to stall or burst the poll.
        private const float PollIntervalSeconds = 0.2f;
        private static float nextPollAt = 0f;

        // Terminal classification state, snapshotted immediately before the invoke.
        private struct BurnBaseline
        {
            public bool Valid;
            public System.Guid NodeId;
            public double NodeTime;
            public int NodeCount;
            public bool HasPatch;
            public double Apoapsis;
            public double Periapsis;
            public string BodyName;
        }

        private static BurnBaseline burn;
        private static bool stopRequested = false;
        private static int burnMessagesSeen = 0;
        private static bool messagesSubscribed = false;
        // UI-fix increment 6: the MessageCenter instance the S2 subscriptions are bound to. A save
        // reload (ESC -> Load Game in flight) replaces the game session's MessageCenter and
        // silently orphans every subscription on the old one - launch 13 lost the S2 burn evidence
        // together with the GUI-state messages. The one-shot `messagesSubscribed` bool alone could
        // never notice; the guard now also compares against the live centre.
        private static MessageCenter subscribedCenter;

        // The status prefixes K2-D2's `ApiStatus()` can return. Only the prefix is stable: the
        // suffix after the colon is transient presentation text with `F2`-formatted numbers.
        private const string PhaseNoNode = "No Maneuver Node";
        private const string PhaseInvalidNode = "Invalid Maneuver Node";
        private const string PhaseTurning = "Turning:";
        private const string PhaseWarping = "Warping:";
        private const string PhaseWaitingToBurn = "Waiting to Burn:";
        private const string PhaseBurning = "Burning:";
        private const string PhaseDone = "Done";

        // ============================== logging helpers ========================================
        // Routed through the plugin so every line carries the `[FlightPlan]` marker the log greps
        // key on. Null-safe: discovery can run before the plugin has published its logger.
        private static ILogger Log { get { return FlightPlanPlugin.Logger; } }

        private static void LogInfo(string message)
        {
            ILogger logger = Log;
            if (logger != null) logger.LogInfo("[FlightPlan] " + message);
        }

        private static void LogWarn(string message)
        {
            ILogger logger = Log;
            if (logger != null) logger.LogWarning("[FlightPlan] " + message);
        }

        private static void LogError(string message)
        {
            ILogger logger = Log;
            if (logger != null) logger.LogError("[FlightPlan] " + message);
        }

        private static GameInstance Game { get { return FlightPlanPlugin.Game; } }

        private static float UnscaledNow { get { return UnityEngine.Time.unscaledTime; } }

        // ============================ discovery and binding ====================================

        /// <summary>
        /// One-time discovery. Extends the P5 descriptor lookup with the execution binding: the four
        /// public instance methods of `desc.Plugin`'s runtime type, and the S2 message subscription.
        /// Every degraded exit leaves `k2d2Loaded == false`, which is what hides the Execute button
        /// (F59) and keeps every planning feature working.
        /// </summary>
        public void CheckModsVersions()
        {
            instance = this;

            k2d2Loaded = false;
            K2D2Descriptor = null;
            k2d2Plugin = null;
            K2D2FlyNodeMethodInfo = null;
            K2D2StopFlyNodeMethodInfo = null;
            K2D2IsFlyNodeRunningMethodInfo = null;
            K2D2GetStatusMethodInfo = null;
            K2D2MainWindowFieldInfo = null;
            K2D2WindowOpenPropertyInfo = null;
            k2d2Version = "unknown";
            checkK2D2status = false;

            // `TryGetDescriptor` returns null (it does not throw) for a mod_id that is not in the list.
            K2D2Descriptor = PluginList.TryGetDescriptor(K2D2ModId);

            if (K2D2Descriptor == null)
            {
                LogWarn("K2D2: unavailable - no descriptor for mod_id=K2D2 (K2-D2 is not installed); " +
                        "the Execute button stays hidden and every planning feature works without it");
                return;
            }

            k2d2Version = DescriptorVersion(K2D2Descriptor);

            if (K2D2Descriptor.Outdated || K2D2Descriptor.Unsupported)
            {
                LogWarn($"K2D2: unavailable - the descriptor for mod_id=K2D2 is flagged by the loader " +
                        $"(Outdated={K2D2Descriptor.Outdated}, Unsupported={K2D2Descriptor.Unsupported}, " +
                        $"version={k2d2Version}); Redux is not forced to load it");
                return;
            }

            // `Plugin` is a public FIELD of declared type ISpaceWarpMod (never reflect `get_Plugin`).
            k2d2Plugin = K2D2Descriptor.Plugin;
            if (k2d2Plugin == null)
            {
                LogWarn($"K2D2: unavailable - the K2-D2 descriptor (version={k2d2Version}) carries no " +
                        "plugin instance; discovery ran before K2-D2 finished loading");
                return;
            }

            Type pluginType = k2d2Plugin.GetType();
            const BindingFlags instancePublic = BindingFlags.Instance | BindingFlags.Public;
            K2D2FlyNodeMethodInfo = pluginType.GetMethod("FlyNode", instancePublic);
            K2D2StopFlyNodeMethodInfo = pluginType.GetMethod("StopFlyNode", instancePublic);
            K2D2IsFlyNodeRunningMethodInfo = pluginType.GetMethod("IsFlyNodeRunning", instancePublic);
            K2D2GetStatusMethodInfo = pluginType.GetMethod("GetStatus", instancePublic);

            k2d2Loaded = K2D2FlyNodeMethodInfo != null
                         && K2D2StopFlyNodeMethodInfo != null
                         && K2D2IsFlyNodeRunningMethodInfo != null
                         && K2D2GetStatusMethodInfo != null;

            if (!k2d2Loaded)
            {
                LogWarn($"K2D2: unavailable - K2-D2 {k2d2Version} is present but its execution API is incomplete " +
                        $"(FlyNode={K2D2FlyNodeMethodInfo != null}, StopFlyNode={K2D2StopFlyNodeMethodInfo != null}, " +
                        $"IsFlyNodeRunning={K2D2IsFlyNodeRunningMethodInfo != null}, GetStatus={K2D2GetStatusMethodInfo != null}); " +
                        "K2-D2 1.2+ is required - the Execute button stays hidden, planning is unaffected");
                k2d2Plugin = null;
                return;
            }

            LogInfo($"K2D2: descriptor found (mod_id={K2D2ModId}, version={k2d2Version}) - plugin type {pluginType.FullName}");
            LogInfo("K2D2: FlyNode/StopFlyNode/IsFlyNodeRunning/GetStatus bound");

            // ---- UI-fix increment 3: the window-open hook (best-effort, never load-bearing) -----
            // Same reflection-only shape as the four binds. A miss on either handle (older/newer
            // K2-D2) just means Execute will not auto-open K2-D2's window; log it once, keep going.
            K2D2MainWindowFieldInfo = null;
            K2D2WindowOpenPropertyInfo = null;
            windowOpenHookMissingLogged = false;
            try
            {
                K2D2MainWindowFieldInfo = pluginType.GetField("main_window", BindingFlags.NonPublic | BindingFlags.Instance);
                if (K2D2MainWindowFieldInfo == null)
                {
                    LogInfo("K2D2: window-open hook unavailable - no 'main_window' field on " + pluginType.FullName);
                    windowOpenHookMissingLogged = true;
                }
                else
                {
                    object window = K2D2MainWindowFieldInfo.GetValue(k2d2Plugin);
                    if (window == null)
                    {
                        // Legitimate at bind time: K2-D2 assigns main_window inside its own
                        // OnInitialized, which may run after this discovery. Re-resolved lazily
                        // at open time, so this only costs the hook if it stays null forever.
                        LogInfo("K2D2: window-open hook - 'main_window' is null at bind time; will retry on first Execute");
                    }
                    else
                    {
                        K2D2WindowOpenPropertyInfo = window.GetType().GetProperty("IsWindowOpen", BindingFlags.Public | BindingFlags.Instance);
                        if (K2D2WindowOpenPropertyInfo == null)
                        {
                            LogInfo("K2D2: window-open hook unavailable - no public 'IsWindowOpen' on " + window.GetType().FullName);
                            windowOpenHookMissingLogged = true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                K2D2MainWindowFieldInfo = null;
                K2D2WindowOpenPropertyInfo = null;
                windowOpenHookMissingLogged = true;
                LogWarn($"K2D2: window-open hook unavailable ({e.GetType().Name}: {e.Message}) - Execute will not auto-open K2-D2's window");
            }

            EnsureMessageSubscription();
        }

        /// <summary>
        /// The absent-path test hook. Clears the execution binding exactly as if K2-D2 were not
        /// installed, without touching K2-D2's own folder. Used by the `Debug Section` switch
        /// "Force K2-D2 Unavailable" in <see cref="FlightPlanPlugin"/>.
        /// </summary>
        public static void ClearExecutionBinding()
        {
            k2d2Plugin = null;
            K2D2FlyNodeMethodInfo = null;
            K2D2StopFlyNodeMethodInfo = null;
            K2D2IsFlyNodeRunningMethodInfo = null;
            K2D2GetStatusMethodInfo = null;
            K2D2MainWindowFieldInfo = null;
            K2D2WindowOpenPropertyInfo = null;
            checkK2D2status = false;
        }

        private static string DescriptorVersion(SpaceWarpPluginDescriptor descriptor)
        {
            try
            {
                var swinfo = descriptor.SWInfo;
                if (swinfo == null) return "unknown";
                string version = Convert.ToString(swinfo.Version);
                return string.IsNullOrEmpty(version) ? "unknown" : version;
            }
            catch (Exception e)
            {
                LogWarn($"K2D2: could not read the descriptor version ({e.GetType().Name}: {e.Message})");
                return "unknown";
            }
        }

        // ============================== the Execute path ========================================

        /// <summary>
        /// The Execute button's entry point: fly the vessel's earliest manoeuvre node through K2-D2.
        /// Runs the precondition chain in order and refuses with a readable reason at the first
        /// failure; while a K2-D2 run is live an extra click is treated as **Stop**.
        /// </summary>
        public void CallK2D2()
        {
            instance = this;

            // ---- 1. K2-D2 present and bound ------------------------------------------------
            if (!k2d2Loaded || K2D2FlyNodeMethodInfo == null || K2D2IsFlyNodeRunningMethodInfo == null)
            {
                LogWarn($"K2D2: Execute refused - the execution API is not available (k2d2Loaded={k2d2Loaded})");
                SetStatus("not available - FlightPlan is planning only", true);
                return;
            }

            // ---- 2. not already running (a re-entrant click is a Stop) ----------------------
            bool running;
            if (!TryIsRunning(out running)) return;
            if (running)
            {
                StopK2D2("Execute pressed while a K2-D2 run was live");
                return;
            }

            if (checkK2D2status)
            {
                // A run is armed but K2-D2's pilot is already stopped: the run has ended and only
                // the poll has not classified it yet. Close it out *now* rather than calling Stop on
                // a stopped pilot - that would set `stopRequested` and mislabel a finished burn as
                // "Stopped".
                nextPollAt = 0f;
                GetK2D2Status();
                return;
            }

            // ---- 3. a node is selected ------------------------------------------------------
            FlightPlanPlugin plugin = FlightPlanPlugin.Instance;
            ManeuverNodeData selected = plugin == null ? null : plugin._currentNode;
            if (selected == null)
            {
                LogWarn("K2D2: Execute refused - no node selected (FlightPlan has not created/selected one " +
                        "this session; the plan may still hold nodes from an earlier one)");
                SetStatus("No manoeuvre node selected - use Make Node first", true);
                return;
            }

            // ---- 4. the selected node is the one K2-D2 will actually fly ---------------------
            // `FlyNode()` is hard-wired to `GetNodes()[0]` = the earliest `Time`, not to "our" node.
            VesselComponent vessel;
            ManeuverPlanComponent plan = ActivePlan(out vessel);
            if (plan == null)
            {
                LogWarn("K2D2: Execute refused - the active vessel has no ManeuverPlanComponent");
                SetStatus("No manoeuvre plan on the active vessel", true);
                return;
            }

            ManeuverNodeData earliest = plan.ActiveNode;
            if (earliest == null || earliest.NodeID != selected.NodeID)
            {
                string earliestText = earliest == null
                    ? "the plan reports no earliest node"
                    : $"node {earliest.NodeID} at UT {earliest.Time:F1}";
                LogWarn($"K2D2 will fly the earliest node, not this one: selected node {selected.NodeID} at " +
                        $"UT {selected.Time:F1}, but K2-D2 flies {earliestText} - no burn requested");
                SetStatus("will fly the earliest node, not the selected one - see the log", true);
                return;
            }

            // ---- 5. the node is in the future ----------------------------------------------
            double ut = UniverseTime();
            if (selected.Time <= ut)
            {
                LogWarn($"K2D2: node already in the past - replot (node UT {selected.Time:F1} <= now {ut:F1})");
                SetStatus("Node already in the past - replot it", true);
                return;
            }

            // ---- 6. in flight ---------------------------------------------------------------
            // `TryGetActiveSimVessel(out vessel, true)` is the accessor K2-D2's own KSPVessel
            // resolves the active vessel through (monodis id 38434 on Assembly-CSharp.dll).
            if (Game == null || Game.ViewController == null
                || !Game.ViewController.TryGetActiveSimVessel(out vessel, true) || vessel == null)
            {
                LogWarn("K2D2: Execute refused - no active vessel in the simulation (this only works in flight/map)");
                SetStatus("can only fly a node in flight", true);
                return;
            }

            // ---- 7. the vessel has control --------------------------------------------------
            if (!vessel.HasControlForEditingManeuvers())
            {
                LogWarn($"K2D2: Execute refused - {vessel.Name} has no control (hibernation, no commnet, " +
                        "or no command module); a burn cannot be commanded");
                SetStatus("Vessel has no control - K2-D2 cannot fly this node", true);
                return;
            }

            // ---- 8. map guard ---------------------------------------------------------------
            // Deliberately no `MapProvider.IsLoaded`/`TryGetMapCore` check here: the Execute path
            // touches **no** map member (no gizmo, no node creation, no popup). K2-D2 flies the node
            // from the vessel's own state, and the S1/S3 signals read the plan and the orbit, not
            // the map. The map guard is the create/edit path's, in `FlightPlanNodeService`.

            // ---- 9. snapshot for the terminal classifier, and arm S2 ------------------------
            SnapshotBaseline(selected, plan, vessel);
            stopRequested = false;
            burnMessagesSeen = 0;
            EnsureMessageSubscription();

            // ---- 10. FlyNode() --------------------------------------------------------------
            try
            {
                K2D2FlyNodeMethodInfo.Invoke(k2d2Plugin, null);
            }
            catch (TargetInvocationException e)
            {
                LogInvokeFailure("FlyNode", e);
                // UI-fix increment 4 ordering: ResetRun() clears the pin, so it must run BEFORE the
                // refusal line is pinned - otherwise the "failed to start the burn" row would be
                // unpinned the instant it was written.
                ResetRun();
                SetStatus("failed to start the burn - check Ksp2.log", true);
                return;
            }
            catch (Exception e)
            {
                LogInvokeFailure("FlyNode", e);
                // UI-fix increment 4 ordering: ResetRun() clears the pin, so it must run BEFORE the
                // refusal line is pinned - otherwise the "failed to start the burn" row would be
                // unpinned the instant it was written.
                ResetRun();
                SetStatus("failed to start the burn - check Ksp2.log", true);
                return;
            }

            LogInfo($"K2D2: FlyNode invoked on node {selected.NodeID} at UT {selected.Time:F1} " +
                    $"(vessel {vessel.Name}, {plan.GetNodes().Count} node(s) on the plan, " +
                    $"burn {selected.BurnRequiredDV:F1} m/s over {selected.BurnDuration:F1} s). " +
                    "K2-D2 flies the earliest node and aborts any other running K2-D2 pilot; it also un-pauses the game.");

            // UI-fix increment 3 (launch-9 user request): bring K2-D2's own window up with the burn.
            // Convenience only - it must never break the burn path, hence the all-guarded helper.
            OpenK2D2Window();

            checkK2D2status = true;
            nextPollAt = 0f;
            statusVisibleUntil = 0f;
            statusPinned = false;   // UI-fix increment 4: a new run supersedes the previous pinned line
            statusPinnedUntil = 0f; // P8: the superseded line's bounded window goes with it
            k2d2Status = "K2-D2: starting the burn...";
        }

        /// <summary>
        /// The polling half, driven once per UI update (and once per plugin update, so a run stays
        /// observable with the window closed). Throttled to ~5 Hz; a no-op unless a run is armed.
        /// </summary>
        public void GetK2D2Status()
        {
            if (!checkK2D2status) return;

            if (!k2d2Loaded || K2D2GetStatusMethodInfo == null || K2D2IsFlyNodeRunningMethodInfo == null)
            {
                LogWarn("K2D2: the execution API disappeared mid-run (K2-D2 unloaded or was replaced) - " +
                        "polling stopped, the node is untouched");
                checkK2D2status = false;
                SetStatus("status unavailable - stop and check", true);
                return;
            }

            float now = UnscaledNow;
            if (now < nextPollAt) return;
            nextPollAt = now + PollIntervalSeconds;

            string raw;
            bool running;
            try
            {
                raw = (string)K2D2GetStatusMethodInfo.Invoke(k2d2Plugin, null);
                running = (bool)K2D2IsFlyNodeRunningMethodInfo.Invoke(k2d2Plugin, null);
            }
            catch (TargetInvocationException e)
            {
                LogInvokeFailure("GetStatus/IsFlyNodeRunning", e);
                checkK2D2status = false;
                SetStatus("status unavailable - stop and check", true);
                return;
            }
            catch (Exception e)
            {
                LogInvokeFailure("GetStatus/IsFlyNodeRunning", e);
                checkK2D2status = false;
                SetStatus("status unavailable - stop and check", true);
                return;
            }

            if (running)
            {
                // Display the phase PREFIX only - the suffix is transient presentation text.
                string phase = StatusPrefix(raw);
                k2d2Status = "K2-D2: " + phase;
                return;
            }

            // ---- terminal: K2-D2's pilot has stopped (its `isRunning` is `mode != Off`) ------
            // `"Done"` is returned for a finished burn, a `StopFlyNode()` and a reset alike, so the
            // outcome comes from the classifier below and is reported as evidence.
            LogInfo($"K2D2: status={raw} running={running}");
            checkK2D2status = false;
            string evidence = ClassifyBurnEvidence();
            LogInfo($"K2D2: burn evidence: {evidence}");

            if (stopRequested)
            {
                LogInfo("K2D2: the run was stopped on request (or a second Execute click) - no success claim");
                SetStatus("Stopped", true);
            }
            else if (evidence != "none")
            {
                LogInfo($"K2D2: Finished - burn evidence: {evidence}");
                SetStatus($"Finished - burn evidence: {evidence}", true);
            }
            else
            {
                LogWarn("K2D2: K2D2 says Done - no burn evidence; check the node");
                SetStatus("says Done - no burn evidence; check the node", true);
            }
        }

        /// <summary>
        /// The plugin-side driver: a no-op unless a run is armed, so it is safe to call every frame
        /// in addition to the window's update. Without it a burn started with the window closed
        /// would never reach its terminal classification.
        /// </summary>
        public static void Poll()
        {
            if (instance == null || !checkK2D2status) return;
            instance.GetK2D2Status();
        }

        // ============================== the Stop path ============================================

        /// <summary>
        /// UI-fix increment 3: opens K2-D2's own window when Execute starts, through the same
        /// reflection-only contract as the execution binds - `main_window` (private instance field)
        /// and its public `IsWindowOpen` setter, which shows the root element and syncs K2-D2's own
        /// AppBar toggle. Never load-bearing: every failure path logs once at most and returns, and
        /// the burn that was just started is unaffected. Handles are re-resolved lazily here because
        /// K2-D2 assigns `main_window` in its own `OnInitialized`, which can run after our discovery.
        /// </summary>
        private static void OpenK2D2Window()
        {
            try
            {
                if (!k2d2Loaded || k2d2Plugin == null) return;

                if (K2D2WindowOpenPropertyInfo == null && !windowOpenHookMissingLogged)
                {
                    if (K2D2MainWindowFieldInfo == null)
                    {
                        LogInfoOnce("K2D2: window-open hook unavailable - no 'main_window' field on the K2-D2 plugin");
                        return;
                    }
                    object window = K2D2MainWindowFieldInfo.GetValue(k2d2Plugin);
                    if (window == null)
                    {
                        LogInfoOnce("K2D2: window-open hook unavailable - 'main_window' is null (K2-D2 window not created)");
                        return;
                    }
                    K2D2WindowOpenPropertyInfo = window.GetType().GetProperty("IsWindowOpen", BindingFlags.Public | BindingFlags.Instance);
                    if (K2D2WindowOpenPropertyInfo == null)
                    {
                        LogInfoOnce("K2D2: window-open hook unavailable - no public 'IsWindowOpen' on " + window.GetType().FullName);
                        return;
                    }
                }

                if (K2D2WindowOpenPropertyInfo == null) return;

                object target = K2D2MainWindowFieldInfo.GetValue(k2d2Plugin);
                if (target == null) { LogInfoOnce("K2D2: window-open hook lost - 'main_window' became null"); return; }
                K2D2WindowOpenPropertyInfo.SetValue(target, true, null);
                LogInfo("K2D2: window opened (IsWindowOpen = true)");
            }
            catch (TargetInvocationException e)
            {
                // The setter syncs K2-D2's AppBar toggle via GameObject.Find; a latent failure in
                // K2-D2's own code escapes as a TIE. Swallow, log once, never touch the burn path.
                LogInfoOnce($"K2D2: opening K2-D2's window failed (IsWindowOpen setter threw: {e.InnerException?.GetType().Name ?? e.GetType().Name})");
            }
            catch (Exception e)
            {
                LogInfoOnce($"K2D2: opening K2-D2's window failed ({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>The window-open hook's log discipline: at most one line per session.</summary>
        private static void LogInfoOnce(string message)
        {
            if (windowOpenHookMissingLogged) return;
            windowOpenHookMissingLogged = true;
            LogInfo(message);
        }

        /// <summary>
        /// `StopFlyNode()` - valid at any time. K2-D2 zeroes the throttle and returns its mode to
        /// Off; it never deletes or mutates a node, so nothing has to be undone here.
        /// </summary>
        private static void StopK2D2(string reason)
        {
            LogInfo($"K2D2: stop requested ({reason})");
            stopRequested = true;

            if (K2D2StopFlyNodeMethodInfo == null || k2d2Plugin == null)
            {
                checkK2D2status = false;
                LogWarn("K2D2: stop requested but the StopFlyNode binding is gone - polling stopped");
                SetStatus("status unavailable - stop and check", true);
                return;
            }

            try
            {
                K2D2StopFlyNodeMethodInfo.Invoke(k2d2Plugin, null);
            }
            catch (TargetInvocationException e)
            {
                LogInvokeFailure("StopFlyNode", e);
                checkK2D2status = false;
                SetStatus("status unavailable - stop and check", true);
                return;
            }
            catch (Exception e)
            {
                LogInvokeFailure("StopFlyNode", e);
                checkK2D2status = false;
                SetStatus("status unavailable - stop and check", true);
                return;
            }

            // Leave the poll armed: the next tick sees `!running` and classifies the outcome as
            // "Stopped" through the same terminal path as a completed burn.
            statusVisibleUntil = UnscaledNow + StatusLingerSeconds;
            k2d2Status = "K2-D2: stopping...";
        }

        // ============================ terminal classifier ========================================

        /// <summary>
        /// Classifies the outcome from three independent signals and returns the evidence list
        /// (e.g. "S1,S2" or "none"). Never a verdict: each signal is logged with its raw numbers.
        /// </summary>
        private static string ClassifyBurnEvidence()
        {
            List<string> signals = new List<string>();

            // ---- S1: the node list moved on --------------------------------------------------
            VesselComponent vessel;
            ManeuverPlanComponent plan = ActivePlan(out vessel);
            if (plan == null)
            {
                LogInfo("K2D2: S1 node list: the plan is unavailable - cannot evaluate");
            }
            else
            {
                List<ManeuverNodeData> nodes = plan.GetNodes();
                bool stillPresent = false;
                for (int i = 0; i < (nodes == null ? 0 : nodes.Count); i++)
                {
                    if (nodes[i].NodeID == burn.NodeId) { stillPresent = true; break; }
                }
                bool advanced = nodes != null && nodes.Count > 0 && nodes[0].NodeID != burn.NodeId;
                bool timePassed = burn.NodeTime <= UniverseTime();
                bool credited = !stillPresent || advanced;
                if (credited) signals.Add("S1");
                LogInfo($"K2D2: S1 node list: before={burn.NodeCount}, now={(nodes == null ? 0 : nodes.Count)}, " +
                        $"flown node still present={stillPresent}, list advanced={advanced}, " +
                        $"flown UT passed={timePassed} - S1 {(credited ? "credited" : "not credited")}");
            }

            // ---- S2: a game message observed between arm and terminal ------------------------
            if (burnMessagesSeen > 0) signals.Add("S2");
            LogInfo($"K2D2: S2 game messages: ManeuverFinishedMessage/ActiveManeuverNodeReachedMessage " +
                    $"observed {burnMessagesSeen} time(s) during the run");

            // ---- S3: the vessel's orbit matches the node's planned patch ---------------------
            if (vessel != null && vessel.Orbit != null && burn.HasPatch)
            {
                double nowAp = vessel.Orbit.Apoapsis;
                double nowPe = vessel.Orbit.Periapsis;
                string nowBody = vessel.mainBody == null ? "unknown" : vessel.mainBody.bodyName;
                bool sameBody = string.Equals(nowBody, burn.BodyName, StringComparison.Ordinal);
                bool apOk = WithinTolerance(nowAp, burn.Apoapsis);
                bool peOk = WithinTolerance(nowPe, burn.Periapsis);
                LogInfo($"K2D2: S3 orbit match (within max(1 km, 2%)): now Ap={nowAp:F1} Pe={nowPe:F1} " +
                        $"body={nowBody} vs planned Ap={burn.Apoapsis:F1} Pe={burn.Periapsis:F1} " +
                        $"body={burn.BodyName} -> Ap {apOk}, Pe {peOk}, same body {sameBody}");
                if (apOk && peOk && sameBody) signals.Add("S3");
            }
            else
            {
                LogInfo($"K2D2: S3 orbit match: not evaluable (planned patch captured={burn.HasPatch}, " +
                        $"vessel orbit available={(vessel != null && vessel.Orbit != null)})");
            }

            return signals.Count == 0 ? "none" : string.Join(",", signals.ToArray());
        }

        /// <summary>Apoapsis/Periapsis tolerance: the larger of 1 km and 2% of the planned value.</summary>
        private static bool WithinTolerance(double actual, double planned)
        {
            double tolerance = Math.Max(1000.0, Math.Abs(planned) * 0.02);
            double delta = Math.Abs(actual - planned);
            return !double.IsNaN(delta) && delta <= tolerance;
        }

        private static void SnapshotBaseline(ManeuverNodeData node, ManeuverPlanComponent plan, VesselComponent vessel)
        {
            burn = new BurnBaseline();
            burn.Valid = true;
            burn.NodeId = node.NodeID;
            burn.NodeTime = node.Time;
            List<ManeuverNodeData> nodes = plan.GetNodes();
            burn.NodeCount = nodes == null ? 0 : nodes.Count;
            PatchedConicsOrbit patch = node.ManeuverTrajectoryPatch;
            burn.HasPatch = patch != null;
            if (patch != null)
            {
                burn.Apoapsis = patch.Apoapsis;
                burn.Periapsis = patch.Periapsis;
            }
            burn.BodyName = vessel == null || vessel.mainBody == null ? "unknown" : vessel.mainBody.bodyName;
            LogInfo($"K2D2: baseline captured - node {burn.NodeId} at UT {burn.NodeTime:F1}, " +
                    $"{burn.NodeCount} node(s) on the plan, planned patch " +
                    (burn.HasPatch
                        ? $"Ap={burn.Apoapsis:F1} Pe={burn.Periapsis:F1} around {burn.BodyName}"
                        : "not available (ManeuverTrajectoryPatch is null)"));
        }

        private static void ResetRun()
        {
            checkK2D2status = false;
            statusPinned = false;   // UI-fix increment 4: a fresh run never inherits the previous line
            statusPinnedUntil = 0f; // P8: ... nor its bounded window
            burn = new BurnBaseline();
            stopRequested = false;
            burnMessagesSeen = 0;
        }

        // ============================== the S2 subscription ======================================

        private static void EnsureMessageSubscription()
        {
            MessageCenter messages = FlightPlanPlugin.MessageCenter;
            if (messages == null)
            {
                LogWarn("K2D2: the message centre is not available yet - S2 burn-message evidence cannot be observed");
                return;
            }
            // UI-fix increment 6: the old one-shot bool alone left S2 dead after a save reload -
            // the centre it was bound to no longer exists. Re-arm when the live instance differs.
            if (messagesSubscribed && ReferenceEquals(messages, subscribedCenter)) return;

            try
            {
                messages.Subscribe<ManeuverFinishedMessage>(OnBurnMessage);
                messages.Subscribe<ActiveManeuverNodeReachedMessage>(OnBurnMessage);
                bool rebind = messagesSubscribed;
                messagesSubscribed = true;
                subscribedCenter = messages;
                LogInfo($"K2D2: subscribed to ManeuverFinishedMessage + ActiveManeuverNodeReachedMessage " +
                        $"(S2 evidence, {(rebind ? "message centre changed - re-armed" : "first bind")})");
            }
            catch (Exception e)
            {
                LogWarn($"K2D2: could not subscribe to the burn messages ({e.GetType().Name}: {e.Message}) - " +
                        "S2 evidence will be absent, S1/S3 still apply");
            }
        }

        private static void OnBurnMessage(MessageCenterMessage message)
        {
            if (!checkK2D2status) return;
            burnMessagesSeen++;
            LogInfo($"K2D2: burn message observed: {message.GetType().Name} (S2)");
        }

        // ================================== helpers =============================================

        private static ManeuverPlanComponent ActivePlan(out VesselComponent vessel)
        {
            vessel = null;
            GameInstance game = Game;
            if (game == null || game.ViewController == null) return null;
            if (!game.ViewController.TryGetActiveSimVessel(out vessel, true) || vessel == null) return null;
            SimulationObjectModel simObject = vessel.SimulationObject;
            return simObject == null ? null : simObject.FindComponent<ManeuverPlanComponent>();
        }

        private static double UniverseTime()
        {
            GameInstance game = Game;
            return game == null || game.UniverseModel == null ? 0.0 : game.UniverseModel.UniverseTime;
        }

        private bool TryIsRunning(out bool running)
        {
            running = false;
            try
            {
                running = (bool)K2D2IsFlyNodeRunningMethodInfo.Invoke(k2d2Plugin, null);
                return true;
            }
            catch (TargetInvocationException e)
            {
                LogInvokeFailure("IsFlyNodeRunning", e);
            }
            catch (Exception e)
            {
                LogInvokeFailure("IsFlyNodeRunning", e);
            }
            checkK2D2status = false;
            SetStatus("status unavailable - stop and check", true);
            return false;
        }

        /// <summary>
        /// Maps K2-D2's status string to its stable phase prefix. The suffix is never parsed: it
        /// carries `F2`-formatted numbers, a live countdown and occasionally a newline. An
        /// unrecognised string is shown verbatim rather than guessed at.
        /// </summary>
        public static string StatusPrefix(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "(no status)";
            if (raw.StartsWith(PhaseNoNode, StringComparison.Ordinal)) return PhaseNoNode;
            if (raw.StartsWith(PhaseInvalidNode, StringComparison.Ordinal)) return PhaseInvalidNode;
            if (raw.StartsWith(PhaseTurning, StringComparison.Ordinal)) return PhaseTurning;
            if (raw.StartsWith(PhaseWarping, StringComparison.Ordinal)) return PhaseWarping;
            if (raw.StartsWith(PhaseWaitingToBurn, StringComparison.Ordinal)) return PhaseWaitingToBurn;
            if (raw.StartsWith(PhaseBurning, StringComparison.Ordinal)) return PhaseBurning;
            if (raw.StartsWith(PhaseDone, StringComparison.Ordinal)) return PhaseDone;
            return raw;
        }

        /// <summary>Logs an invoke failure with the inner exception - the escaping-NRE path.</summary>
        private static void LogInvokeFailure(string member, Exception e)
        {
            TargetInvocationException tie = e as TargetInvocationException;
            Exception inner = tie != null && tie.InnerException != null ? tie.InnerException : e;
            LogError($"K2D2: {member} threw {inner.GetType().Name}: {inner.Message} - " +
                     $"the node is untouched and the burn will not start; see the stack below");
            LogError(inner.ToString());
        }

        /// <summary>Sets the UI status line, with the linger that keeps it readable. UI-fix increment 4:
        /// `linger: true` marks a terminal/refusal line, so it is also PINNED. P8: the pin is bounded -
        /// the row stays up for StatusPinnedSeconds, or until the next Execute / ResetRun clears it.</summary>
        private static void SetStatus(string text, bool linger)
        {
            k2d2Status = "K2-D2: " + text;
            if (linger)
            {
                statusVisibleUntil = UnscaledNow + StatusLingerSeconds;
                statusPinned = true;
                statusPinnedUntil = UnscaledNow + StatusPinnedSeconds;
                // One line per terminal/refusal event - this path runs once per run, never per frame -
                // so launch 11's log proves the row was pinned even if the user reads it minutes later.
                LogInfo($"K2D2: status line pinned: \"{k2d2Status}\" (stays up to {StatusPinnedSeconds:F0} s, or until the next Execute)");
            }
        }

        /// <summary>
        /// True while the `K2D2Status` row should be visible: a live run, a terminal/refusal line
        /// inside its linger window, or a PINNED terminal/refusal line (UI-fix increment 4) still
        /// inside its own bounded window (P8: StatusPinnedSeconds; the next Execute / ResetRun
        /// clears the pin itself).
        /// </summary>
        public static bool StatusVisible
        {
            get { return checkK2D2status || (statusPinned && UnscaledNow < statusPinnedUntil) || UnscaledNow < statusVisibleUntil; }
        }

        /// <summary>The line the UI renders. Empty until a run or a refusal has something to say.</summary>
        public static string StatusLine { get { return k2d2Status; } }
    }
}
