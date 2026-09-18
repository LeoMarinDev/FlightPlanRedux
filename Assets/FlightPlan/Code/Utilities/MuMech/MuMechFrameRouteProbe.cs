using System;
using System.Text;
using KSP.Api;
using KSP.Sim;
using KSP.Sim.impl;

namespace MuMech
{
    /// <summary>
    /// F29 / G12.7 frame-route oracle: measures the two candidate routes for the world-position
    /// family in <c>OrbitExtensions.cs</c> against each other and against the runtime's own
    /// arbiter, over one full orbital period.
    ///
    /// <para>
    /// The routes are (a) NodeManager's raw <c>SwapYAndZ</c> form and (b) MicroEngineer's shipped
    /// <c>celestialFrame.ToLocalPosition((ICoordinateSystem)o.ReferenceFrame, ...)</c> form, which
    /// the port adopted for members 3/4/5. NodeManager's own mismatch-warning branches exist
    /// because the two were observed to disagree; this probe replaces that observation with numbers.
    /// </para>
    ///
    /// <para>
    /// <b>What the port already established offline.</b> From the shipped runtime's IL
    /// (<c>Assembly-CSharp.dll</c>, 0.2.8.5.103184 - filters in the P2 block of
    /// <c>Deploy/obj/PORT-PROGRESS.md</c>):
    /// <c>PatchedConicsOrbit.get_ReferenceFrame()</c> returns
    /// <c>referenceBody.SimulationObject.transform.celestialFrame</c>, i.e. the very frame
    /// <c>celestialFrame</c> this probe converts into; and
    /// <c>TransformFrame.ToLocalPosition(cs, v)</c> opens with <c>if (cs == this) return v;</c>.
    /// The two routes are therefore the same value by construction, so the <c>swap</c>-vs-<c>frame</c>
    /// columns below must come out exactly <c>0</c>. The informative number is the arbiter column:
    /// the runtime's own <c>GetTruePositionAtUT</c> propagates the reference body to <c>UT</c>, while
    /// both candidate routes use the body's <i>current</i> position, so that delta is the body's
    /// motion between now and <c>UT</c>.
    /// </para>
    ///
    /// <para>
    /// The comparison is not runnable outside the game: it needs a live
    /// <see cref="PatchedConicsOrbit"/> whose frames are backed by the running universe model, and the
    /// frame math lives in engine-backed types. A synthetic "agreement" would be worthless, so
    /// <see cref="Compare"/> is the runnable artefact: P4 calls it once on the active vessel's orbit
    /// and records its output.
    /// </para>
    ///
    /// <para>
    /// The arbiter is the native
    /// <c>PatchedConicsOrbit.GetTruePositionAtUT(double, ICoordinateSystem)</c> (returns the true
    /// position in the requested frame), with
    /// <c>CelestialBodyComponent.GetTruePositionAtUT(double, ICoordinateSystem)</c> for the
    /// reference body; the truth velocity is a central difference of the two, which removes the
    /// body's own motion so it is comparable with the body-relative routes.
    /// </para>
    ///
    /// <para>
    /// Read-only: it logs and returns a string. It must never be called on a hot path.
    /// </para>
    /// </summary>
    internal static class MuMechFrameRouteProbe
    {
        /// <summary>Samples taken across one period.</summary>
        internal const int SamplesPerPeriod = 64;

        /// <summary>
        /// Compares both routes for one orbit over one full period.
        /// </summary>
        /// <param name="orbit">The live orbit to measure (never null).</param>
        /// <param name="startUT">
        /// First sample time. Defaults to the orbit's current start time so the sweep is
        /// deterministic for a given state.
        /// </param>
        /// <returns>A multi-line report; also written to the port's log.</returns>
        internal static string Compare(PatchedConicsOrbit orbit, double? startUT = null)
        {
            if (orbit == null) return "[MuMech/frame-oracle] orbit is null - not run";

            ICoordinateSystem celestial = orbit.referenceBody.transform.celestialFrame;
            double start = startUT ?? orbit.StartUT;
            double period = orbit.period;

            double maxPosRoutes = 0, maxPosSwapVsTruth = 0, maxPosFrameVsTruth = 0;
            double maxVelRoutes = 0, maxVelSwapVsTruth = 0, maxVelFrameVsTruth = 0;
            double maxAbsPos = 0;

            for (int i = 0; i < SamplesPerPeriod; i++)
            {
                double ut = start + period * i / SamplesPerPeriod;

                Vector3d swapPos = SwapRoutePosition(orbit, ut);
                Vector3d framePos = FrameRoutePosition(orbit, ut);
                Vector3d truthPos = orbit.GetTruePositionAtUT(ut, celestial);

                maxPosRoutes = Math.Max(maxPosRoutes, (swapPos - framePos).magnitude);
                maxPosSwapVsTruth = Math.Max(maxPosSwapVsTruth, (swapPos - truthPos).magnitude);
                maxPosFrameVsTruth = Math.Max(maxPosFrameVsTruth, (framePos - truthPos).magnitude);
                maxAbsPos = Math.Max(maxAbsPos, truthPos.magnitude);

                Vector3d swapVel = SwapRouteVelocity(orbit, ut);
                Vector3d frameVel = FrameRouteVelocity(orbit, ut);
                Vector3d truthVel = TruthVelocity(orbit, celestial, ut, period);

                maxVelRoutes = Math.Max(maxVelRoutes, (swapVel - frameVel).magnitude);
                maxVelSwapVsTruth = Math.Max(maxVelSwapVsTruth, (swapVel - truthVel).magnitude);
                maxVelFrameVsTruth = Math.Max(maxVelFrameVsTruth, (frameVel - truthVel).magnitude);
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[MuMech/frame-oracle] body={orbit.referenceBody.Name} samples={SamplesPerPeriod} period={period:F3}s startUT={start:F3} max|truthPos|={maxAbsPos:F3}m");
            sb.AppendLine($"[MuMech/frame-oracle] position max|swap-frame|={maxPosRoutes:G9}m max|swap-truth|={maxPosSwapVsTruth:G9}m max|frame-truth|={maxPosFrameVsTruth:G9}m");
            sb.AppendLine($"[MuMech/frame-oracle] velocity max|swap-frame|={maxVelRoutes:G9}m/s max|swap-truth|={maxVelSwapVsTruth:G9}m/s max|frame-truth|={maxVelFrameVsTruth:G9}m/s");
            sb.AppendLine($"[MuMech/frame-oracle] verdict routes: {(maxPosRoutes == 0 && maxVelRoutes == 0 ? "PASS - frame route is bit-identical to the raw route (IL-proven identity fast path)" : "FAIL - routes differ; ReferenceFrame is not the body's celestialFrame instance - escalate before porting call sites")}");
            sb.AppendLine($"[MuMech/frame-oracle] arbiter position: {(maxPosFrameVsTruth <= maxPosSwapVsTruth ? "frame-route" : "swap-route")} closer to GetTruePositionAtUT; velocity: {(maxVelFrameVsTruth <= maxVelSwapVsTruth ? "frame-route" : "swap-route")} closer (delta is the reference body's motion between now and UT)");

            string report = sb.ToString();
            MuMechLog.LogInfo(report);
            return report;
        }

        /// <summary>NodeManager's route: body position + the raw <c>SwapYAndZ</c> relative vector.</summary>
        private static Vector3d SwapRoutePosition(PatchedConicsOrbit orbit, double ut)
        {
            return orbit.referenceBody.Position.localPosition + orbit.GetRelativePositionAtUTZup(ut).SwapYAndZ;
        }

        /// <summary>MicroEngineer's route: the same vector, converted from the orbit frame to the body's celestial frame.</summary>
        private static Vector3d FrameRoutePosition(PatchedConicsOrbit orbit, double ut)
        {
            return orbit.referenceBody.transform.celestialFrame.ToLocalPosition((ICoordinateSystem)orbit.ReferenceFrame,
                orbit.referenceBody.Position.localPosition + orbit.GetRelativePositionAtUTZup(ut).SwapYAndZ);
        }

        private static Vector3d SwapRouteVelocity(PatchedConicsOrbit orbit, double ut)
        {
            return orbit.GetOrbitalVelocityAtUTZup(ut).SwapYAndZ;
        }

        private static Vector3d FrameRouteVelocity(PatchedConicsOrbit orbit, double ut)
        {
            return orbit.referenceBody.transform.celestialFrame.ToLocalPosition((ICoordinateSystem)orbit.ReferenceFrame,
                orbit.GetOrbitalVelocityAtUTZup(ut).SwapYAndZ);
        }

        /// <summary>
        /// Truth velocity: central difference of the true (absolute) position with the reference
        /// body's own motion subtracted, so the result is body-relative like the two routes.
        /// </summary>
        private static Vector3d TruthVelocity(PatchedConicsOrbit orbit, ICoordinateSystem celestial, double ut, double period)
        {
            double h = Math.Min(1.0, Math.Abs(period) / 1000.0);
            if (!(h > 0)) h = 1.0;

            Vector3d orbitNext = orbit.GetTruePositionAtUT(ut + h, celestial);
            Vector3d orbitPrev = orbit.GetTruePositionAtUT(ut - h, celestial);
            Vector3d bodyNext = orbit.referenceBody.GetTruePositionAtUT(ut + h, celestial);
            Vector3d bodyPrev = orbit.referenceBody.GetTruePositionAtUT(ut - h, celestial);

            return (orbitNext - orbitPrev - (bodyNext - bodyPrev)) / (2 * h);
        }
    }
}
