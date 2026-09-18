using KSP.Game;
using ReduxLib.Configuration;
using ReduxLib.Logging;

namespace FlightPlan
{

    public class FPStatus
    {
        // Status of last Flight Plan function
        public enum Status
        {
            VIRGIN,
            OK,
            WARNING,
            ERROR
        }

        // The legacy captured `GameManager.Instance.Game` in a static readonly field at type-load
        // (the hard-crash source P2 removed from the MuMech layer) and built its own BepInEx log
        // source. Both now route through the plugin, which publishes the computed Game accessor and
        // the ReduxLib logger.
        private static GameInstance Game => FlightPlanPlugin.Game;

        static public Status status = Status.VIRGIN; // Everyone starts out this way...

        static public string StatusText;
        static public double StatusTime = 0; // _UT of last Status update

        private static ConfigValue<string> InitialStatusText;
        static public ConfigValue<double> StatusPersistence;
        static public ConfigValue<double> StatusFadeTime;

        private static ILogger Logger => FlightPlanPlugin.Logger;

        public static void K2D2Status(string txt, double duration)
        {
            StatusText = txt;
            double _UT = Game.UniverseModel.UniverseTime;
            StatusTime = _UT + duration;
        }

        public static void Ok(string txt)
        {
            set(Status.OK, txt);
            if (txt.Length > 0)
                Logger.LogInfo(txt);
        }

        public static void Warning(string txt)
        {
            set(Status.WARNING, txt);
            if (txt.Length > 0)
                Logger.LogWarning(txt);
        }

        public static void Error(string txt)
        {
            set(Status.ERROR, txt);
            if (txt.Length > 0)
                Logger.LogError(txt);
        }

        private static void set(Status status, string txt)
        {
            FPStatus.status = status;
            StatusText = txt;
            double _UT = Game.UniverseModel.UniverseTime;
            StatusTime = _UT + StatusPersistence.Value;
        }

        public static void Init(FlightPlanPlugin plugin)
        {
            // P4 fix (found in the launch-2 log): every default below is written as a DOUBLE literal
            // (`20.0`, not `20`). `IConfigFile.Bind<T>` infers T from the default, so an int literal
            // made the entry's ValueType `int`, and `new ConfigValue<double>(entry)` throws
            // `ArgumentException("entry")` (ReduxLib/Configuration/ConfigValue.cs: the ctor rejects
            // entry.ValueType != typeof(T)). The launch-2 stack was exactly:
            //   System.ArgumentException: entry
            //     at ReduxLib.Configuration.ConfigValue`1[T]..ctor (IConfigEntry entry)
            //     at FlightPlan.FPStatus.Init (FlightPlanPlugin plugin)
            //     at FlightPlan.FlightPlanPlugin.OnInitialized ()
            // which aborted OnInitialized here, so nothing after this point (the Experimental/Status
            // Reporting/Debug config binds, Harmony, the message subscriptions) ever ran. The on-disk
            // key names keep their legacy spelling - they are the JSON keys already on disk.
            StatusPersistence = new ConfigValue<double>(plugin.SWConfiguration.Bind(
                "Status Settings Section", "Satus Hold Time", 20.0,
                "Controls time DELAY (in seconds) before Status beings to fade"));
            StatusFadeTime = new ConfigValue<double>(plugin.SWConfiguration.Bind(
                "Status Settings Section", "Satus Fade Time", 20.0,
                "Controls the time (in seconds) it takes for Status to fade"));
            InitialStatusText = new ConfigValue<string>(plugin.SWConfiguration.Bind(
                "Status Settings Section", "Initial Status", "Virgin",
                "Controls the Status reported at startup prior to the first command"));

            // Set the initial and Default values based on config parameters. These don't make sense to need live update, so there're here instead of useing the configParam.Value elsewhere
            StatusText = InitialStatusText.Value;
        }
    }
}
