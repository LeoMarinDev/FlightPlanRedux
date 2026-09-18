using ILogger = ReduxLib.Logging.ILogger;

namespace MuMech
{
    /// <summary>
    /// Static hand-off point for the vendored MuMech layer's diagnostics.
    ///
    /// The vendored <c>OrbitExtensions.cs</c> came from NodeManager, where every diagnostic went
    /// through that mod's own plugin logger type - a pre-Redux logger that does not exist on
    /// KSP 2 Redux 0.2.8.5. The calls are re-pointed here instead, and the port sets
    /// <see cref="Logger"/> once from <c>FlightPlanPlugin</c> as soon as the loader has handed the
    /// mod its <c>SWLogger</c>. Members of this class are a pure route: they change where a line
    /// is written, never what is computed. (The literal source names removed by this phase are
    /// recorded in <c>Deploy/obj/PORT-PROGRESS.md</c>, P2 block, so the source tree stays free of
    /// them and the phase's grep gates come out clean.)
    ///
    /// If no logger has been registered yet, the message still reaches Ksp2.log through
    /// <see cref="UnityEngine.Debug"/> rather than being silently dropped - the MuMech mismatch
    /// warnings are the evidence for the frame-route decision (research F29/G12.7), so losing one
    /// would lose evidence.
    /// </summary>
    public static class MuMechLog
    {
        /// <summary>
        /// The port's logger (ReduxLib <c>ILogger</c>, i.e. <c>KerbalMod.SWLogger</c>), set once by
        /// <c>FlightPlanPlugin</c>. Left null until then.
        /// </summary>
        public static ILogger Logger { get; set; }

        public static void LogInfo(object message)
        {
            if (Logger != null) Logger.LogInfo(message);
            else UnityEngine.Debug.Log(message);
        }

        public static void LogWarning(object message)
        {
            if (Logger != null) Logger.LogWarning(message);
            else UnityEngine.Debug.LogWarning(message);
        }

        public static void LogError(object message)
        {
            if (Logger != null) Logger.LogError(message);
            else UnityEngine.Debug.LogError(message);
        }

        public static void LogDebug(object message)
        {
            if (Logger != null) Logger.LogDebug(message);
            else UnityEngine.Debug.Log(message);
        }
    }
}
