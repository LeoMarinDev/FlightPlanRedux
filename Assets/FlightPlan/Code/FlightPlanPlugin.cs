using FPUtilities;
using KSP.Game;
using KSP.Map;
using KSP.Messages;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using KSP.UI.Binding;
using MuMech;
using Redux.ExtraModTypes;
using ReduxLib.Configuration;
using ReduxLib.Input;
using ReduxLib.Logging;
using SpaceWarp2.UI.API.Appbar;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;
// `ILogger` is ambiguous here (UnityEngine.ILogger vs ReduxLib.Logging.ILogger) because this file
// imports UnityEngine. Every `ILogger` in the FlightPlan sources is the ReduxLib one.
using ILogger = ReduxLib.Logging.ILogger;

namespace FlightPlan
{

    /// <summary>
    ///  The selected mode in UI
    /// </summary>
    public enum ManeuverType
    {
        None,
        circularize,
        newPe,
        newAp,
        newPeAp,
        newInc,
        newLAN,
        newNodeLon,
        newSMA,
        matchPlane,
        hohmannXfer,
        courseCorrection,
        interceptTgt,
        matchVelocity,
        moonReturn,
        planetaryXfer,
        advancedPlanetaryXfer,
        fixAp,
        fixPe
    }

    /// <summary>
    ///  The selected time Reference
    /// </summary>
    public enum TimeReference
    {
        None,
        COMPUTED,
        APOAPSIS,
        PERIAPSIS,
        CLOSEST_APPROACH,
        EQ_ASCENDING,
        EQ_DESCENDING,
        REL_ASCENDING,
        REL_DESCENDING,
        X_FROM_NOW,
        ALTITUDE,
        EQ_NEAREST_AD,
        EQ_HIGHEST_AD,
        REL_NEAREST_AD,
        REL_HIGHEST_AD,
        LIMITED_TIME,
        PORKCHOP,
        NEXT_WINDOW,
        ASAP
    }

    // Identity now lives in swinfo.json (spec 2.0, mod_id FlightPlan);
    // the legacy [BepInPlugin]/[BepInDependency] attributes are gone with BepInEx. The constants are
    // kept because other code (and other mods) reads them, and because the legacy plugin published them.
    public class FlightPlanPlugin : KerbalMod
    {
        public static FlightPlanPlugin Instance { get; set; }

        // These are useful in case some other mod wants to add a dependency to this one
        public const string ModGuid = "FlightPlan";
        public const string ModName = "Flight Plan";
        public const string ModVer = "0.10.8";

        // GUI stuff
        private static bool Loaded = false;
        public static bool InterfaceEnabled = false;

        private ConfigValue<KeyboardShortcut> _keybind;
        private ConfigValue<KeyboardShortcut> _keybind2;

        // P4 debug path: the self-driving node probe. Off by default; the Debug Section flag turns it
        // on, and `_nodeProbeStarted` keeps it to one run per session.
        internal ConfigValue<bool> _debugNodeProbe;
        // P4 verification increment: the multi-node probe (create-to-cap, delete, refresh paths). Off
        // by default; it shares `_nodeProbeStarted` with the single-node probe, and if BOTH flags are
        // set the multi-node probe wins (the two must not run at once - they would share the vessel).
        internal ConfigValue<bool> _debugMultiNodeProbe;
        // The K2-D2 absent-path test switch: after discovery this forces `k2d2Loaded = false` and
        // clears the execution binding, so the "K2-D2 not installed" behaviour can be exercised
        // without moving another mod's folder (which is never touched - AGENTS §6.8).
        internal ConfigValue<bool> _debugForceK2D2Unavailable;
        private static bool _nodeProbeStarted;

        // Config parameters (the legacy `_autoLaunchMNC` entry is deleted - D10: the MNC path is dead)
        internal ConfigValue<bool> _experimental;
        internal ConfigValue<double> _smallError;
        internal ConfigValue<double> _largeError;

        // mod-wide Data
        internal VesselComponent _activeVessel;
        internal SimulationObjectModel _currentTarget;
        internal ManeuverNodeData _currentNode = null;
        private List<ManeuverNodeData> ActiveNodes;

        // ============================= the UI controller member ==================================
        // The window's controller. It is attached by the bootstrap in OnInitialized() (the window owns
        // the GameObject, the controller is a component on it), so it is non-null as soon as the window
        // exists - but never assume it: the bundle can fail to load, and then there is no controller.
        //
        // The three UI-owned numbers the maneuver API reads (`TargetInterceptTime_s`,
        // `TargetInterceptDistanceCelestial_m`, `TargetInterceptDistanceVessel_m`) now live on
        // FpUiController, which is where the text fields write them. CloseWindow() is the only path that
        // lowers the window on a game-state change; ToggleButton() drives the AppBar state.
        private FpUiController controller;

        // The mod's own prebuilt AssetBundle (Tools/build-ui-bundle.sh -> assets/bundles/). Held for the
        // process lifetime: the live window's tree, its stylesheet and every font/image in it are
        // resolved through this bundle, so unloading it would leave the window pointing at destroyed
        // assets. Loaded once in OnInitialized(); null means the UI could not be built at all.
        private static AssetBundle _uiBundle;
        // =========================================================================================

        // App bar Button(s)
        public const string _ToolbarFlightButtonID = "BTN-FlightPlanFlight";

        // ReduxLib logger. The legacy shadowed the base BepInEx logger with a static ManualLogSource so
        // that every other file in the assembly could log through FlightPlanPlugin.Logger; that shape is
        // kept because the vendored MechJebLib files call FlightPlanPlugin.Logger.LogInfo(...) directly.
        public static ILogger Logger { get; set; }

        public static MessageCenter MessageCenter;

        // UI-fix increment 6: the MessageCenter instance the current subscriptions are bound to.
        // A save reload (ESC -> Load Game while in flight) tears down and replaces the game
        // session's MessageCenter; every subscription made on the old instance is silently
        // orphaned (launch 13: zero messages delivered for ~11 minutes across two reloads).
        // `SubscribeGameMessages()` re-runs whenever the live instance differs from this one.
        private static MessageCenter _subscribedCenter;

        // UI-fix increment 7 diagnostic: what `SubscribeGameMessages()`'s guard did, as a short state
        // string that is logged ONLY when it changes. The launch-14 session carried no
        // `... message subscribed`, no `...(N families ok, M failed)` and no `MessageCenter instance`
        // line at all - yet the subscriptions demonstrably took effect (a game-state handler ran and
        // flipped GUIenabled, and the S2 burn-message subscribe line from FPOtherModsInterface, which
        // needs FlightPlanPlugin.MessageCenter to be non-null, also logged). So the method returns at
        // one of its three exits. This records which one, and when it changes. Unconditional logging
        // here would flood: Update() calls the method every frame.
        private static string _lastSubscribeOutcome;

        // UI-fix increment 8: per-family completion for the centre held in `_subscribedCenter`, as a
        // 3-bit mask (family 1 = the game-state family, 2 = the scene-load family, 3 = the ESC pair).
        // Launch 15 proved why a PARTIAL bind must not look like a bind. `Update()` ticked for
        // ~11 s / 241 frames BEFORE SpaceWarp called `OnInitialized()`, so `Logger` was still null:
        // the first frame's `_subscribedCenter = messages` ran, the three family-1 `Subscribe<...>`
        // calls SUCCEEDED, and then `Logger.LogInfo` threw inside the try; the handler's
        // `Logger.LogWarning` threw AGAIN - from inside a catch block, so nothing caught it - and
        // that second throw escaped `SubscribeGameMessages` into `Update()`. Families 2 and 3 were
        // never subscribed, and because `_subscribedCenter` already looked bound, the ReferenceEquals
        // guard made every later call a no-op for the life of that centre. The mask makes each family
        // retryable, on the centre it belongs to: "bound" now means this centre AND all three bits.
        private const int FamilyGameState = 1 << 0;
        private const int FamilySceneLoad = 1 << 1;
        private const int FamilyEscapeMenu = 1 << 2;
        private const int AllFamiliesBound = FamilyGameState | FamilySceneLoad | FamilyEscapeMenu;
        private static int _familiesBound;

        // UI-fix increment 8: set at the top of `OnInitialized()`, i.e. as soon as `Logger` (and
        // `MuMechLog.Logger`) are live, and never cleared. `Update()` returns until this is true, so
        // no frame can run half-initialised polling/subscription work while the loader is still
        // wiring the mod up - and because it is never cleared, it cannot mask the two per-frame
        // polls once the mod IS initialised (the save-reload fix depends on them).
        private static bool _initialized;

        // The legacy BaseSpaceWarpPlugin exposed an instance `Game`. `KerbalMonoBehaviour` (the base
        // of `KerbalMod`) does expose one - hence the `new` - but a
        // `static readonly GameInstance Game = GameManager.Instance.Game` field would capture at
        // type-load, the hard-crash source P2 removed from the MuMech layer. Computed per call,
        // with the null guard the base property does not have, so static callers (FPUtility,
        // FPStatus, BurnTimeOption) can never NRE on it.
        internal static new GameInstance Game => GameManager.Instance == null ? null : GameManager.Instance.Game;

        /// <summary>
        /// Runs when the mod is first initialized.
        /// </summary>
        public override void OnInitialized()
        {
            base.OnInitialized();

            Instance = this;

            Logger = SWLogger;
            // P2 wired the vendored MuMech layer through MuMechLog; keep that hand-off here.
            MuMechLog.Logger = SWLogger;

            // UI-fix increment 8: from this point the mod is initialised enough for `Update()` to be
            // allowed to run - it returns while this is false. Set here rather than at the end of the
            // method on purpose: a throw later in this body (a failed bundle load, a missing
            // config section) must not be able to leave the mod with a permanently muted Update().
            _initialized = true;

            // ================================== the UI bootstrap ====================================
            // This is the legacy `AssetManager.GetAsset<VisualTreeAsset>($"{Info.Metadata.GUID}/fp_ui/
            // ui/fp_ui.uxml")` + `Window.CreateFromUxml` + `AddComponent<FpUiController>` sequence,
            // ported to 0.2.8.5's asset route and window API.
            //
            // DELIVERY ROUTE (D4), decided by measurement, not preference:
            //   Redux's `Redux.UI.UITKHelper.{LoadUxml, LoadAddressableAssetAsync<T>,
            //   CreateWindowFromUxml}` resolves the GAME's Addressables catalogue. A mod's own assets
            //   have no addressable route on this stack - the mod is not in that catalogue - so the
            //   mod loads its own prebuilt AssetBundle, rebuilt inside this project by
            //   Assets/FlightPlan/Editor/BuildFlightPlanUIBundle.cs with the required Unity
            //   6000.5.8f1 and BuildAssetBundleOptions.ForceRebuildAssetBundle (the throwaway
            //   Tools/build-ui-bundle.sh is the documented fallback). One route carries everything the UI
            //   needs: the UXML page, the stylesheet it references, every font/image those reference,
            //   and the AppBar icon. Evidence: Deploy/obj/addressables-verdict.md.
            //
            // SWMetadata.Folder is a System.IO.DirectoryInfo (not a string) - `.FullName` is the
            // accessor. The bundle path must match the deploy layout exactly; Linux is case-sensitive.
            string bundlePath = SWMetadata.Folder.FullName + "/assets/bundles/flightplan_ui.bundle";
            _uiBundle = AssetBundle.LoadFromFile(bundlePath);

            if (_uiBundle == null)
            {
                Logger.LogError($"[FlightPlan] could not load the UI bundle at {bundlePath} - the window " +
                                "cannot be built. Deploy assets/bundles/flightplan_ui.bundle beside the DLL.");
            }
            else
            {
                Logger.LogInfo($"[FlightPlan] UI bundle loaded: {bundlePath}");

                // Inside the bundle an asset is addressed by the project path it was packed from, and
                // the container stores it lowercased (the audit prints the exact keys - the root page
                // is `assets/flightplan/ui/fp_ui.uxml`). LoadAsset resolves that name case-insensitively.
                VisualTreeAsset fpUiTree = _uiBundle.LoadAsset<VisualTreeAsset>("Assets/FlightPlan/UI/FP_UI.uxml");
                if (fpUiTree == null)
                {
                    Logger.LogError("[FlightPlan] FP_UI.uxml is not in the bundle (null VisualTreeAsset) - " +
                                    "Window.Create would render nothing");
                }
                else
                {
                    // Rebased on `WindowOptions.Default` (MicroEngineer's shape,
                    // `Assets/MicroEngineer/Code/UI/Uxmls.cs:82-94`). The legacy built this as a
                    // zero-initialised struct, which silently left four properties at `false`:
                    // `BringToFrontOnPointerDown` (the measured cause of the window sitting BEHIND other
                    // UI - it never raises itself when clicked), `UseStockScale`, `BlockGameInput` and
                    // `ResizeOptions`. The claim this comment used to carry - that `WindowOptions.Default`
                    // leaves moving disabled - is FALSE: the IL of the shipped `UitkForKsp2.dll`
                    // (`WindowOptions::get_Default`) sets IsHidingEnabled/UseStockScale/
                    // DisableGameInputForTextFields/BringToFrontOnPointerDown/BlockGameInput true and
                    // copies `MoveOptions.Default`, whose `IsMovingEnabled` is also true.
                    // Only the mod-specific values are overridden; everything else is inherited.
                    WindowOptions windowOptions = WindowOptions.Default;
                    windowOptions.WindowId = "FlightPlan";
                    windowOptions.Parent = null;
                    windowOptions.IsHidingEnabled = true;
                    windowOptions.DisableGameInputForTextFields = true;
                    // The window is dragged by its own root (the controller stops pointer propagation
                    // over the PorkchopDisplay), so the move options are stated explicitly.
                    windowOptions.MoveOptions = new MoveOptions
                    {
                        IsMovingEnabled = true,
                        CheckScreenBounds = true
                    };

                    // At this pin `Window.Create(WindowOptions, VisualTreeAsset)` returns the window's
                    // PanelRenderer. `UIDocument` is the superseded 0.2.8.5 shape and is NOT the window
                    // root here: the visual tree root is fetched from the renderer through
                    // `UitkForKsp2.API.Extensions.GetWindowRoot(renderer)` (FpUiController.SetupDocument).
                    // The tree is built synchronously inside the call (Window.Create -> CreateInternal ->
                    // WindowComponent.ResolveNow -> Resolve, which resolves the content root).
                    //
                    // The legacy's `document.hideFlags |= HideFlags.HideAndDontSave` is not ported, and
                    // not because it is inconvenient: `Window.CreateInternal` already calls
                    // `DontDestroyOnLoad` on the window's GameObject and ORs
                    // `HideFlags.DontUnloadUnusedAsset` (32) into its hideFlags (monodis:
                    // UitkForKsp2.API.Window::CreateInternal, IL_0025 and IL_002c-IL_0034), so the old
                    // line was already redundant. The shipped K2-D2 window sets nothing extra on its
                    // renderer either (K2D2_Plugin.cs:314-317).
                    PanelRenderer panel = Window.Create(windowOptions, fpUiTree);
                    if (panel == null)
                    {
                        Logger.LogError("[FlightPlan] Window.Create returned no PanelRenderer - " +
                                        "the controller has nothing to bind to");
                    }
                    else
                    {
                        // The controller binds through the renderer it is handed: Initialize registers the
                        // UI-ready callback, which fires once immediately (the tree already exists by now)
                        // and again on any later live UI reload (the K2-D2 model, K2D2Window.Initialize).
                        controller = panel.gameObject.AddComponent<FpUiController>();
                        controller.Initialize(panel);
                    }

                    // The AppBar icon comes out of the same bundle, by its exact asset path. The legacy
                    // asked for `Icon.png` while the payload ships `icon.png`; naming the asset that is
                    // genuinely in the container (audit: assets/flightplan/ui/images/icon.png) is the fix.
                    Texture2D icon = _uiBundle.LoadAsset<Texture2D>("Assets/FlightPlan/UI/Images/icon.png");
                    if (icon == null)
                    {
                        Logger.LogWarning("[FlightPlan] the AppBar icon is not in the bundle - the button " +
                                          "will register without one");
                    }

                    Appbar.RegisterAppButton("Flight Plan", _ToolbarFlightButtonID, icon, ToggleButton);
                    Logger.LogInfo("[FlightPlan] window created and AppBar button registered");
                }
            }
            // =========================================================================================

            // Setup keybindings with default values
            _keybind = new ConfigValue<KeyboardShortcut>(SWConfiguration.Bind(
                "Keybindings",
                "First Keybind",
                new KeyboardShortcut(KeyCode.P, KeyCode.LeftAlt),
                "Keybind to open mod window"
            ));

            _keybind2 = new ConfigValue<KeyboardShortcut>(SWConfiguration.Bind(
                "Keybindings",
                "Second Keybind",
                new KeyboardShortcut(KeyCode.P, KeyCode.RightAlt, KeyCode.AltGr),
                "Keybind to open mod window"
            ));

            Logger.LogInfo("Loaded");
            if (Loaded)
            {
                Destroy(this);
            }
            Loaded = true;

            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);

            // Register all Harmony patches in the project
            CreateHarmonyAndPatchAll();

            // Fetch a configuration value or create a default one if it does not exist
            FPStatus.Init(this);

            _experimental = new ConfigValue<bool>(SWConfiguration.Bind(
                "Experimental Section", "Experimental Features", false,
                "Enable/Disable experimental features for testing - Warrantee Void if Enabled!"));
            // P4 fix: double literals - see the note in FPStatus.Init. `Bind<T>` infers T from the
            // default, so `1`/`2` would make an int entry and `new ConfigValue<double>` would throw.
            _smallError = new ConfigValue<double>(SWConfiguration.Bind(
                "Status Reporting Section", "Small % Error Threashold", 1.0,
                "Percent error threshold used to assess quality of maneuver node goal for warning (yellow) status"));
            _largeError = new ConfigValue<double>(SWConfiguration.Bind(
                "Status Reporting Section", "Large % Error Threashold", 2.0,
                "Percent error threshold used to assess quality of maneuver node goal for error (red) status"));

            Logger.LogInfo($"Experimental Features: {_experimental.Value}");

            // P4 debug path: off unless the player turns it on. The probe needs a flight scene, so the
            // flag is only *read* here and acted on in GameStateEntered/GameStateChanged.
            _debugNodeProbe = new ConfigValue<bool>(SWConfiguration.Bind(
                "Debug Section", "Self-Driving Node Probe", false,
                "Create, offset and edit one maneuver node automatically without the UI, logging every step under [FlightPlan]. For testing the node service only - leave off in normal play"));
            Logger.LogInfo($"Self-Driving Node Probe: {_debugNodeProbe.Value}");

            // P4 verification increment: the multi-node probe. `bool` on purpose - `Bind<T>` infers T
            // from the default literal, and a mistyped numeric literal is the launch-2 defect (see
            // FPStatus.Init). A bool default cannot get that wrong.
            _debugMultiNodeProbe = new ConfigValue<bool>(SWConfiguration.Bind(
                "Debug Section", "Multi-Node Probe", false,
                "Clears the vessel's existing maneuver nodes first (each one is logged with SpitNode before it is removed), then creates nodes through the production path until the 9-node cap refuses (the cap is a real game limit), then exercises the delete and refresh paths and leaves the vessel clean, logging every step under [FlightPlan]. For testing the node service only - leave off in normal play"));
            Logger.LogInfo($"Multi-Node Probe: {_debugMultiNodeProbe.Value}");

            // The absent-path test switch for the K2-D2 integration. Discovery happens first (so the
            // log still shows what was found), and this then forces the mod to behave exactly as it
            // would if K2-D2 were not installed: `k2d2Loaded = false` hides the Execute button (F59)
            // and no invoke can happen, because the execution binding is cleared with it. It never
            // touches K2-D2's own folder or install - the absent path is reproduced in *our* state.
            _debugForceK2D2Unavailable = new ConfigValue<bool>(SWConfiguration.Bind(
                "Debug Section", "Force K2-D2 Unavailable", false,
                "Absent-path test: after discovery, force k2d2Loaded = false so the Execute button is hidden and the K2-D2 execution path is unreachable, exactly as if K2-D2 were not installed. FlightPlan must load and work normally (planning, create/edit/delete) with this on. Leave off in normal play"));
            Logger.LogInfo($"Force K2-D2 Unavailable: {_debugForceK2D2Unavailable.Value}");

            RefreshGameManager();

            // Discover the other mods FlightPlan integrates with (K2-D2), and bind its execution
            // surface. Both halves live in FPOtherModsInterface: the descriptor lookup by `mod_id`
            // plus the four-method reflection binding, the Execute/Stop sequence, the ~5 Hz status
            // poll and the terminal classifier.
            FPOtherModsInterface.instance = new FPOtherModsInterface();
            FPOtherModsInterface.instance.CheckModsVersions();

            if (_debugForceK2D2Unavailable.Value)
            {
                FPOtherModsInterface.k2d2Loaded = false;
                FPOtherModsInterface.ClearExecutionBinding();
                Logger.LogWarning("[FlightPlan] K2D2: k2d2Loaded = false forced by the Debug Section " +
                                  "switch 'Force K2-D2 Unavailable' (absent-path test) - the Execute button is hidden " +
                                  "and no K2-D2 member can be invoked; K2-D2's own install is untouched");
            }

            // UI-fix increment 6: every game-message subscription (the scene-load family, the
            // game-state family and the ESC-menu pair) moved into SubscribeGameMessages(), which
            // is idempotent per MessageCenter *instance* and re-runs whenever the game replaces
            // the centre. A save reload does exactly that and silently orphaned every subscription
            // made here at the old centre (launch 13: not one message delivered after the first
            // reload, so the window could not be opened again). RefreshGameManager() above already
            // performed the initial subscription on the live centre.
        }

        // ================== UI-fix increment 6: MessageCenter re-subscription =====================
        // ROOT CAUSE (launch 13): a save reload tears down and replaces the game session's
        // MessageCenter. Every subscription made on the old instance is silently orphaned - the
        // game-state family (GUI visibility), the ESC-menu pair (z-order suppression) and - with
        // the same disease in FPOtherModsInterface - the S2 burn-evidence pair all died together,
        // while the AppBar toggle and container still worked. The legacy mod was immune only
        // because it polled the game state every frame and did not rely on messages for GUI state.
        //
        // API (verified against the shipped runtime, monodis 2026-09-15):
        //   KSP.Messages.MessageCenter.Subscribe<T>(Action<MessageCenterMessage>) -> SubscriptionHandle
        //   KSP.Messages.MessageCenter.Unsubscribe<T>(Action<MessageCenterMessage>) -> bool
        //   KSP.Messages.MessageCenter.Unsubscribe(ref SubscriptionHandle)
        // Unsubscribe<T> EXISTS, but the handlers below use method groups captured by the compiler;
        // per-family rollback on a failed subscribe would need to track which families succeeded.
        // We deliberately do NOT unsubscribe: the old centre is dead after a reload (no delivery,
        // no leak the player can observe), and a partial rollback on failure would leave the
        // *live* centre with fewer handlers than a fresh subscribe would add. Re-subscription
        // targets only the new instance; the old one is abandoned whole.

        /// <summary>
        /// UI-fix increment 6: (re)subscribe every game message on the live MessageCenter. Idempotent
        /// per MessageCenter instance AND per family: a call whose instance equals
        /// <see cref="_subscribedCenter"/> with all three families in returns without logging, and a
        /// partially-bound centre retries only the families that are still missing. Called from
        /// RefreshGameManager() and from the per-frame state poll in Update(), so a save reload
        /// re-arms every subscription on the frame the new session is first seen.
        /// </summary>
        private void SubscribeGameMessages(string reason)
        {
            MessageCenter messages = GameManager.Instance?.Game?.Messages;
            if (messages == null)
            {
                LogSubscribeOutcome("messages=null");
                return;
            }

            bool sameCentre = ReferenceEquals(messages, _subscribedCenter);
            // UI-fix increment 8: "bound" means THIS centre AND every family. A partially-bound
            // centre (launch 15: family 1 in, families 2 and 3 never attempted) must not look bound,
            // or the families that were lost can never be retried.
            if (sameCentre && _familiesBound == AllFamiliesBound)
            {
                LogSubscribeOutcome("centre unchanged");
                return;
            }

            bool firstTime = _subscribedCenter == null;
            if (!sameCentre)
            {
                // A different instance means the old session's centre (a save reload replaces it) -
                // every subscription made on it is orphaned, so the binding starts over for the new
                // one. The old centre is abandoned whole; there is no unsubscribe path, and the
                // increment-6 note above explains why.
                _subscribedCenter = messages;
                _familiesBound = 0;
            }
            MessageCenter = messages;

            LogSubscribeOutcome(sameCentre
                ? $"retry (reason={reason}, bound={_familiesBound})"
                : $"binding (reason={reason})");

            int families = 0;
            int failed = 0;
            int missing = AllFamiliesBound & ~_familiesBound;

            // Family 1 - the game-state family (GUI visibility). This is the one whose loss
            // left the window dead after a reload.
            if ((missing & FamilyGameState) != 0)
            {
                try
                {
                    messages.Subscribe<GameStateEnteredMessage>(GameStateEntered);
                    messages.Subscribe<GameStateLeftMessage>(GameStateLeft);
                    messages.Subscribe<GameStateChangedMessage>(GameStateChanged);
                    // The bit is set BEFORE the log line, and that order is the point: the
                    // subscribes ARE the binding, so nothing a log call can do may leave a
                    // completed family looking unbound (a retry would double-subscribe it).
                    _familiesBound |= FamilyGameState;
                    families++;
                    LogInfo("GameStateEnteredMessage + GameStateLeftMessage + GameStateChangedMessage message subscribed");
                }
                catch (Exception e)
                {
                    failed++;
                    LogWarn($"[FlightPlan] could not subscribe the game-state family " +
                            $"({e.GetType().Name}: {e.Message}) - GUI state falls back to the per-frame poll");
                }
            }

            // Family 2 - the scene-load family (cleanup and vessel tracking).
            if ((missing & FamilySceneLoad) != 0)
            {
                try
                {
                    messages.Subscribe<VesselChangedMessage>(VesselChanged);
                    messages.Subscribe<TrainingCenterLoadedMessage>(TrainingCenterLoaded);
                    messages.Subscribe<TrackingStationLoadedAudioCueMessage>(TrackingStationLoaded);
                    messages.Subscribe<GameLoadFinishedMessage>(GameLoadFinished);
                    _familiesBound |= FamilySceneLoad;
                    families++;
                    LogInfo("VesselChangedMessage + TrainingCenterLoadedMessage + TrackingStationLoadedAudioCueMessage + GameLoadFinishedMessage message subscribed");
                }
                catch (Exception e)
                {
                    failed++;
                    LogWarn($"[FlightPlan] could not subscribe the scene-load family " +
                            $"({e.GetType().Name}: {e.Message})");
                }
            }

            // Family 3 - the ESC-menu pair (UI-fix increment 5). Folded in here so a reload
            // re-arms it too; the old one-shot latch is retired with the old centre.
            if ((missing & FamilyEscapeMenu) != 0)
            {
                try
                {
                    messages.Subscribe<EscapeMenuOpenedMessage>(OnEscapeMenuOpened);
                    messages.Subscribe<EscapeMenuClosedMessage>(OnEscapeMenuClosed);
                    _familiesBound |= FamilyEscapeMenu;
                    families++;
                    LogInfo("EscapeMenuOpenedMessage + EscapeMenuClosedMessage message subscribed");
                }
                catch (Exception e)
                {
                    failed++;
                    LogWarn($"[FlightPlan] ESC-menu suppression: could not subscribe to the ESC-menu " +
                            $"messages ({e.GetType().Name}: {e.Message}) - the window keeps its " +
                            "click-to-front order while the escape menu is open");
                }
            }

            string instanceState = sameCentre
                ? "retried a partial binding"
                : (firstTime ? "bound" : "changed");
            LogInfo($"MessageCenter instance {instanceState} - " +
                    $"(re)subscribed game messages ({reason}: {families} families ok, {failed} failed, " +
                    $"bound={_familiesBound}/{AllFamiliesBound})");
        }

        /// <summary>
        /// UI-fix increment 7 diagnostic: log which exit <see cref="SubscribeGameMessages"/> took, but
        /// only when that outcome CHANGES - the method is called every frame from Update(), so an
        /// unconditional line here would flood the log. Not logged at all while <c>Logger</c> is still
        /// unset (the state is recorded on the next call that can report it, so no transition is lost).
        /// </summary>
        private static void LogSubscribeOutcome(string outcome)
        {
            if (outcome == _lastSubscribeOutcome)
            {
                return;
            }
            if (Logger == null)
            {
                // Cannot report it yet. Deliberately NOT recorded: the next call that CAN report
                // still has to emit the line, or the transition would be lost (increment 7).
                return;
            }
            _lastSubscribeOutcome = outcome;
            LogInfo($"[diag] SubscribeGameMessages: {outcome}");
        }

        // ---- UI-fix increment 8: the null-safe logging shim for the boot path -------------------
        // `Logger` is assigned by `OnInitialized()` (from `SWLogger`), but Unity ticks `Update()`
        // from the frame the plugin's GameObject exists - launch 15 measured 241 frames / ~11 s of
        // `Update()` before `OnInitialized()` ran. Every `Logger.…` on that path threw, and a throw
        // from inside a `catch` block escapes its own try/catch entirely - which is how the
        // launch-15 defect killed families 2 and 3 for the whole session. These helpers no-op
        // instead of throwing, and are used for EVERY log call in `SubscribeGameMessages`,
        // `LogSubscribeOutcome`, `ApplyEscapeSuppression` and the two polls in `Update()`.
        // `LogSubscribeOutcome`'s `Logger == null` check above is the same pattern, generalised.
        private static void LogInfo(string message)
        {
            if (Logger == null)
            {
                return;
            }
            Logger.LogInfo(message);
        }

        private static void LogWarn(string message)
        {
            if (Logger == null)
            {
                return;
            }
            Logger.LogWarning(message);
        }

        private static void LogDebug(string message)
        {
            if (Logger == null)
            {
                return;
            }
            Logger.LogDebug(message);
        }
        // =========================================================================================

        // ================== UI-fix increment 5: ESC-menu z-order suppression =====================
        // The launch-11 defect: our window floated above the ESC menu's full-screen overlay and above
        // the game's save-before-exit dialog. Cause (measured by monodis on the shipped UitkForKsp2.dll):
        // `UitkForKsp2.API.Order.OrderManager` is ONE global monotonic counter shared by every
        // registered UI - `Register(PanelSettings)` assigns `sortingOrder = Next()`, `BringToFront(x)`
        // assigns a fresh, higher `Next()` - and `WindowOptions.Default` sets
        // `BringToFrontOnPointerDown = true`, so every click on our window raises it above *anything*,
        // including the game's own pause canvases. K2-D2 does not show this only because its
        // WindowOptions is a zero-initialised struct (BringToFrontOnPointerDown stays false) - but
        // launch 7's complaint was the opposite (our window behind other UI), which is exactly what
        // the click-to-front behaviour fixes. Both are wanted, just not at once: keep the option, hide
        // the window while the menu is up. Message IDs resolved from the pinned set (ffc94930):
        //   T:KSP.Messages.EscapeMenuOpenedMessage / T:KSP.Messages.EscapeMenuClosedMessage
        // and both exist in the installed Assembly-CSharp.dll (monodis --typedef: ids 7662 / 7661).
        // UI-fix increment 6: the pair is now subscribed inside SubscribeGameMessages() (family 3)
        // and re-arms on every MessageCenter instance change - the one-shot
        // `_escapeMenuMessagesSubscribed` latch died with the old centre on a save reload and is
        // retired. UI-fix increment 7: the pair is a fast path only; `OnEscapeMenuOpened` and
        // `OnEscapeMenuClosed` now just call the shared `ApplyEscapeSuppression`, which the
        // per-frame ESC poll in Update() drives too.

        // UI-fix increment 5: `_windowSuppressedForEscapeMenu` below is the latch.
        // UI-fix increment 7: the suppression is now POLL-driven as well. The message pair is kept as a
        // fast path (it costs nothing and helps whenever `Publish` completes), but message DELIVERY is
        // not a dependency any more - see `ApplyEscapeSuppression`.

        // True only while the escape menu is up AND this mod hid the window because of it. This is what
        // makes the restore conditional: if the window was closed when the menu opened, or the player
        // closed it while the menu was up, the menu closing must not open it. The logical state
        // (`InterfaceEnabled` + the AppBar toggle) is never touched by this path.
        private static bool _windowSuppressedForEscapeMenu;

        // UI-fix increment 7: the last value the per-frame `UIManager.IsEscapeVisible()` poll saw.
        // Initialised to false because a session begins with the ESC menu closed - so the first poll
        // frame is not a transition and emits no line. A poll that cannot resolve `UIManager` does not
        // touch this field at all, so a moment with no UI manager can never fake a menu transition.
        private static bool _lastEscapeVisible;

        // UI-fix increment 6: `EnsureEscapeMenuSubscription()` (the increment-5 one-shot subscribe)
        // is deleted - the pair is family 3 of SubscribeGameMessages() and re-arms on every
        // MessageCenter instance change.

        /// <summary>
        /// UI-fix increment 5: the escape-menu-opened message arrived - lower the window.
        /// </summary>
        private void OnEscapeMenuOpened(MessageCenterMessage message)
        {
            ApplyEscapeSuppression(true);
        }

        /// <summary>
        /// UI-fix increment 5: the escape-menu-closed message arrived - restore only what this mod hid.
        /// </summary>
        private void OnEscapeMenuClosed(MessageCenterMessage message)
        {
            ApplyEscapeSuppression(false);
        }

        /// <summary>
        /// UI-fix increment 7: the ONE body both ESC triggers drive - the message pair (the fast path,
        /// when delivery reaches us) and the per-frame `UIManager.IsEscapeVisible()` poll in
        /// <see cref="Update"/> (the reliable path). Rounding the window down and back up therefore
        /// happens exactly once per menu transition, whichever trigger gets there first.
        ///
        /// WHY the poll exists. **Corrected 2026-09-15** - this block used to claim that
        /// `MessageCenter.Publish(Type, MessageCenterMessage)` has ZERO exception isolation and that ONE
        /// throwing subscriber aborts the dispatch for every subscriber after it. That is wrong. Scoped
        /// over the method body, the shipped `Publish` carries 1 opening `.try` and 1
        /// `catch (System.Exception)` INSIDE its per-subscriber loop, and the handler does
        /// `Debug.LogException` then `leave`s back into the loop - so a throwing subscriber is logged
        /// and the dispatch continues. The launch-14 `ToggleFlyout` stack trace once cited here as proof
        /// of the opposite in fact contradicts it: it shows `UnityEngine.Debug:LogException` called
        /// *from* `MessageCenter:Publish(...)`, which is that `catch` firing.
        ///
        /// The poll is still required, on different grounds: a save reload replaces the `MessageCenter`
        /// instance and orphans every handler on the old one, and `Publish` to a type with **no live
        /// subscriber** only warns `"Publishing message with no subscriber."` and returns without
        /// delivering. The poll does not depend on delivery at all.
        ///
        /// Idempotent by design: the second trigger for the same transition is a no-op, so the two Info
        /// lines below fire once per transition, not once per trigger.
        /// </summary>
        private void ApplyEscapeSuppression(bool menuVisible)
        {
            if (menuVisible)
            {
                if (_windowSuppressedForEscapeMenu)
                {
                    LogDebug("ESC suppression: the window is already suppressed - nothing to do");
                    return;
                }
                // `GUIenabled && InterfaceEnabled` is exactly the gate FpUiController.Update() uses for
                // visibility, so this suppresses only a window the player could actually see.
                if (controller == null || !InterfaceEnabled || !FpUiController.GUIenabled)
                {
                    LogDebug("ESC suppression: the FlightPlan window is not visible - nothing to suppress");
                    return;
                }
                _windowSuppressedForEscapeMenu = true;
                controller.SuppressForEscapeMenu(true);
                LogInfo("window suppressed while the ESC menu is open");
                return;
            }

            // Restore only the suppression this mod applied. The latch is cleared before the restore
            // call and regardless of the outcome, so it can never outlive the menu.
            if (!_windowSuppressedForEscapeMenu)
            {
                LogDebug("ESC suppression: this mod did not suppress the window - nothing to restore");
                return;
            }
            _windowSuppressedForEscapeMenu = false;
            controller?.SuppressForEscapeMenu(false);
            LogInfo("window restored after the ESC menu closed");
        }
        // =========================================================================================

        public static GameStateConfiguration ThisGameState;
        public static GameState? LastGameState;
        public static CurtainContext ThisCurtainContext;

        public static bool needToCleanUp;

        // The UI-state hand-off. The state lives on the controller (FpUiController.GUIenabled), because
        // the controller's Update() is what reads it: it shows the window only while
        // `GUIenabled && FlightPlanPlugin.InterfaceEnabled`, and returns early otherwise. So lowering
        // the window here as well is what makes a state change take effect on the current frame rather
        // than on the next one (legacy behaviour, kept). `container` is null until the window is built.
        private static void SetGuiState(bool enabled)
        {
            FpUiController.GUIenabled = enabled;
            if (!enabled)
            {
                // UI-fix increment 5: a game-state change out of flight/map also drops any ESC-menu
                // suppression. The escape menu cannot still be up after the transition, so an
                // unpaired latch (the open message fired, the close message did not) must not survive
                // it - otherwise the window would stay hidden for the rest of the session.
                _windowSuppressedForEscapeMenu = false;
                Instance?.controller?.SuppressForEscapeMenu(false);

                if (FpUiController.container != null)
                {
                    FpUiController.container.style.display = DisplayStyle.None;
                }
            }
        }

        public static void RefreshGameManager()
        {
            ThisGameState = GameManager.Instance?.Game?.GlobalGameState?.GetGameState();
            LastGameState = GameManager.Instance?.Game?.GlobalGameState?.GetLastGameState()?.GameState;
            MessageCenter = GameManager.Instance?.Game?.Messages;
            ThisCurtainContext = (CurtainContext)(GameManager.Instance?.Game?.UI.Curtain.CurtainContextData.CurtainContext);
            Logger.LogDebug($"RefreshGameManager ThisCurtainContext = {ThisCurtainContext}");

            // UI-fix increment 6: re-subscribe when the game replaced the MessageCenter. No-op while
            // the instance is unchanged; re-arms every family on a save reload.
            Instance?.SubscribeGameMessages("RefreshGameManager");

            if (ThisGameState == null)
            {
                // Defensive: OnInitialized runs before a game session exists in some flows. The legacy
                // dereferenced ThisGameState unconditionally here; P1's shape keeps the mod loadable.
                Logger.LogDebug("RefreshGameManager: no GameStateConfiguration yet - skipping state checks");
                return;
            }

            if (ThisGameState.GameState == GameState.MainMenu)
            {
                needToCleanUp = true;
                Instance.CleanUp();
            }
            else if (ThisGameState.GameState == GameState.FlightView || ThisGameState.GameState == GameState.Map3DView)
            {
                // KEEP the legacy re-run: a scene change that never saw the MainMenu branch can leave
                // the flag set, and the controller's CleanUp() is what clears it (it writes
                // FlightPlanPlugin.needToCleanUp = false itself).
                if (needToCleanUp && FpUiController.Instance != null)
                {
                    Logger.LogInfo($"RefreshGameManager calling CleanUp() while GameState = {ThisGameState.GameState}");
                    Instance.CleanUp();
                }
            }
            else
            {
                Logger.LogInfo($"RefreshGameManager ThisGameState.GameState = {ThisGameState.GameState}");
            }
        }

        private void CleanUp()
        {
            ThisGameState = GameManager.Instance?.Game?.GlobalGameState?.GetGameState();

            // KEEP the legacy shape: the flag is cleared *by* the controller's CleanUp(), so it is only
            // cleared when a controller exists. Without one (a failed bundle load) the flag stays set,
            // which is exactly the legacy behaviour.
            if (FpUiController.Instance != null)
            {
                Logger.LogInfo($"CleanUp calling FpUiController.Instance.CleanUp() from {ThisGameState.GameState}");
                FpUiController.Instance.CleanUp();
            }

            // Lower the GUI
            if (InterfaceEnabled)
            {
                Logger.LogInfo($"CleanUp calling Instance.ToggleButton(false) from {ThisGameState.GameState} with needToCleanUp = {needToCleanUp}");
                Instance.ToggleButton(false);
            }
        }

        private void VesselChanged(MessageCenterMessage message)
        {
            // Do the right thing here!
            Logger.LogDebug($"VesselChanged message recieved. Resetting StatusTime to 0");
            FPStatus.StatusTime = 0;
        }

        /// <summary>
        /// P4 debug path: start one node probe once, when a flight scene is entered and a config flag
        /// says so. Started from the message handlers rather than OnInitialized because the probe needs
        /// a vessel and a universe time, and neither exists until a flight scene does.
        ///
        /// P4 verification increment: two flags now select between the probes. If both are set the
        /// multi-node probe wins and the single-node one is not started - running both would have them
        /// editing the same vessel's plan at once, which neither is written for.
        /// </summary>
        private void MaybeStartNodeProbe()
        {
            if (_nodeProbeStarted)
            {
                return;
            }

            bool multiNode = _debugMultiNodeProbe != null && _debugMultiNodeProbe.Value;
            bool singleNode = _debugNodeProbe != null && _debugNodeProbe.Value;
            if (!multiNode && !singleNode)
            {
                return;
            }

            _nodeProbeStarted = true;

            if (multiNode)
            {
                if (singleNode)
                {
                    Logger.LogWarning("[FlightPlan] PROBE: BOTH debug flags are set - the Multi-Node probe wins, the 'Self-Driving Node Probe' will not run");
                }
                Logger.LogWarning("[FlightPlan] PROBE enabled by the Debug Section 'Multi-Node Probe' flag - starting the multi-node node probe");
                StartCoroutine(FlightPlanNodeService.MultiNodeProbeCo());
                return;
            }

            Logger.LogWarning("[FlightPlan] PROBE enabled by the Debug Section 'Self-Driving Node Probe' flag - starting the node probe");
            StartCoroutine(FlightPlanNodeService.SelfDrivingProbeCo());
        }

        private void GameStateChanged(MessageCenterMessage message)
        {
            RefreshGameManager();
            Logger.LogDebug($"GameStateChanged Message Recived. GameState: {LastGameState}  ->  {ThisGameState.GameState}");
            if (ThisGameState.GameState == GameState.FlightView || ThisGameState.GameState == GameState.Map3DView)
            {
                SetGuiState(true);
                MaybeStartNodeProbe();
            }
            else
            {
                SetGuiState(false);
            }
            Logger.LogDebug($"GameStateChanged FpUiController.GUIenabled = {FpUiController.GUIenabled}");
        }

        private void GameLoadFinished(MessageCenterMessage message)
        {
            Instance.CleanUp();
        }

        private void GameStateEntered(MessageCenterMessage message)
        {
            RefreshGameManager();
            Logger.LogDebug($"GameStateEntered Message Recived. GameState: {LastGameState}  ->  {ThisGameState.GameState}");
            if (ThisGameState.GameState == GameState.FlightView || ThisGameState.GameState == GameState.Map3DView)
            {
                SetGuiState(true);
                MaybeStartNodeProbe();
            }
            else
            {
                SetGuiState(false);
            }
            Logger.LogDebug($"GameStateEntered FpUiController.GUIenabled = {FpUiController.GUIenabled}");
        }

        private void GameStateLeft(MessageCenterMessage message)
        {
            RefreshGameManager();
            Logger.LogDebug($"GameStateLeft Message Recived. GameState: {ThisGameState.GameState}");

            SetGuiState(false);

            Logger.LogDebug($"GameStateLeft FpUiController.GUIenabled = {FpUiController.GUIenabled}");
        }

        private void TrackingStationLoaded(MessageCenterMessage message)
        {
            RefreshGameManager();
            Logger.LogDebug($"TrackingStationLoadedAudioCue Message Recived. GameState: {LastGameState}  ->  {ThisGameState.GameState}");

            SetGuiState(false);

            Logger.LogDebug($"TrackingStationLoadedAudioCue FpUiController.GUIenabled = {FpUiController.GUIenabled}");
        }

        private void TrainingCenterLoaded(MessageCenterMessage message)
        {
            RefreshGameManager();
            Logger.LogDebug($"TrainingCenterLoaded Message Recived. GameState: {LastGameState}  ->  {ThisGameState.GameState}");

            SetGuiState(false);

            Logger.LogDebug($"TrainingCenterLoaded FpUiController.GUIenabled = {FpUiController.GUIenabled}");
        }


        public void ToggleButton(bool toggle)
        {
            // KEEP: the legacy refused to toggle while the game's own UI is hidden - an identity test
            // against the singleton view (`UIStateViews.get_UIHiddenView()`, monodis:30912), not a
            // game-state test. Null-guarded because `Game` is null before a session exists; the legacy
            // dereferenced `Game.UI.ViewController` directly and would have thrown there.
            var viewController = Game?.UI?.ViewController;
            if (viewController != null && viewController.CurrentView == UIStateViews.UIHiddenView)
            {
                // Info, not Debug: if an AppBar click is ignored, this line is the only evidence why.
                Logger.LogInfo($"ToggleButton({toggle}) ignored: the game UI is hidden (CurrentView == UIHiddenView).");
                return;
            }
            InterfaceEnabled = toggle;
            GameObject.Find(_ToolbarFlightButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(InterfaceEnabled);
            // `SetEnabled` is what actually shows/lowers the window's root element. A null controller is
            // a real state (the bundle failed to load), not an error worth throwing over.
            controller?.SetEnabled(toggle);
        }

        private void Awake()
        {

        }

        private void Update()
        {
            // UI-fix increment 8: no frame may run half-initialised work.
            // `OnInitialized()` is what wires the logger, the config, the keybinds, the window and
            // the subscriptions - and Unity ticks `Update()` from the frame the plugin's GameObject
            // exists, which launch 15 measured as ~11 s / 241 frames BEFORE `OnInitialized()` ran.
            // Every one of those frames threw a NullReferenceException out of this method (241 of
            // them in the log) because `Logger` was still null. `_initialized` is set at the top of
            // `OnInitialized()` and is never cleared, so after the mod comes up this return cannot
            // fire again: the two per-frame polls below (the save-reload state poll and the ESC
            // poll) keep running on every frame, which is what the reload fix depends on.
            if (!_initialized)
            {
                return;
            }

            if ((_keybind != null && _keybind.Value.Down) || (_keybind2 != null && _keybind2.Value.Down))
            {
                ToggleButton(!InterfaceEnabled);
                if (_keybind != null && _keybind.Value.Down)
                    Logger.LogDebug($"Update: UI toggled with _keybind, hotkey {_keybind.Value}");
                if (_keybind2 != null && _keybind2.Value.Down)
                    Logger.LogDebug($"Update: UI toggled with _keybind2, hotkey {_keybind2.Value}");
            }

            // UI-fix increment 6, HALF 2 (cheap part): a save reload replaces the MessageCenter
            // instance. A ReferenceEquals per frame is all it takes to notice; the actual
            // re-subscription runs inside SubscribeGameMessages and is transition-gated.
            SubscribeGameMessages("poll detected change");

            // UI-fix increment 7, ESC half: the same treatment for the ESC-menu suppression that
            // increment 6 gave GUI visibility - poll it every frame instead of trusting message
            // delivery. Delivery is a fast path, never a guarantee: a save reload replaces the
            // `MessageCenter` instance and orphans every handler on the old one, so a handler may
            // simply never fire (see ApplyEscapeSuppression). This asks the game's own UI
            // manager directly. `Game.UI` is `KSP.Game.UIManager` (`get_UI`, param 21545 in the
            // installed Assembly-CSharp.dll) and `IsEscapeVisible()` is a public instance bool on it
            // (param 23243) - the same `Game?.UI` chain RefreshGameManager already compiles.
            //
            // Transition-gated: one line per ESC open and one per close, never per frame. A poll that
            // cannot resolve the UI manager is a silent no-op - it does NOT write _lastEscapeVisible,
            // so a transiently missing manager can never look like a menu close and thrash the latch.
            var ui = Game?.UI;
            if (ui != null)
            {
                bool escapeUp = ui.IsEscapeVisible();
                if (escapeUp != _lastEscapeVisible)
                {
                    _lastEscapeVisible = escapeUp;
                    LogInfo($"ESC-menu poll: visible={escapeUp} - {(escapeUp ? "suppressing" : "restoring")} the window");
                    ApplyEscapeSuppression(escapeUp);
                }
            }

            // UI-fix increment 6, HALF 1: the per-frame game-state poll, like the legacy. The
            // message handlers alone are not enough: launch 13 proved a save reload can orphan
            // them (the old MessageCenter dies with the old session), and then `GUIenabled` can
            // never become true again - the AppBar toggle ran, the container went Flex, and
            // FpUiController.Update()'s same-frame gate (`uiVisible = GUIenabled &&
            // InterfaceEnabled && !_escapeMenuSuppressed`) re-hid the window because `GUIenabled`
            // was false. Polling the state every frame makes GUI visibility independent of
            // message delivery: after ANY reload, the first Update frame that sees
            // FlightView/Map3DView re-enables.
            //
            // Transition-gated: the log fires only on an actual flip (a couple of lines per scene
            // change, never per frame), and the work below it is two cheap compares on every
            // other frame.
            var gs = Game?.GlobalGameState?.GetGameState()?.GameState;
            bool inUiScene = gs == GameState.FlightView || gs == GameState.Map3DView;
            if (inUiScene != FpUiController.GUIenabled)
            {
                LogInfo($"state poll: GUIenabled {FpUiController.GUIenabled} -> {inUiScene} (GameState={gs})");
                SetGuiState(inUiScene);
                if (inUiScene)
                {
                    MaybeStartNodeProbe();
                }
            }

            // The K2-D2 run poll. It is a no-op unless a run is armed and throttles itself to ~5 Hz,
            // so driving it here as well as from the window's update costs nothing - and it is what
            // keeps a burn observable (and terminally classified) while the FlightPlan window is
            // closed or hidden by a game-state change.
            FPOtherModsInterface.Poll();
        }

        /// <summary>
        /// Draws a simple UI window when <code>this._isWindowOpen</code> is set to <code>true</code>.
        /// </summary>
        private void OnGUI()
        {
            _activeVessel = GameManager.Instance?.Game?.ViewController?.GetActiveVehicle(true)?.GetSimVessel(true);
            _currentTarget = _activeVessel?.TargetObject;
        }

        private ManeuverNodeData GetCurrentNode()
        {
            ActiveNodes = Game.SpaceSimulation.Maneuvers.GetNodesForVessel(Game.ViewController.GetActiveVehicle(true).Guid);
            return (ActiveNodes.Count() > 0) ? ActiveNodes[0] : null;
        }

        private void CloseWindow()
        {
            GameObject.Find(_ToolbarFlightButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(false);
            InterfaceEnabled = false;
            ToggleButton(InterfaceEnabled);
        }

        private bool CreateManeuverNodeCaller(Vector3d deltaV, double burnUT, double burnOffsetFactor = -0.5)
        {
            // P4: the guard and the create sequence were hoisted into FlightPlanNodeService. The
            // service owns every rule (the 30 s proximity check, the UT floor, the >= 1 s separation
            // fix-up, the 9-node cap, the burn-centred offset) and it enforces the proximity check
            // before anything is created, so this method is now the UI-side translation of a refusal.
            // The precise delta is logged by the service (`[FlightPlan] CreateNodeAtUT: ...`).
            bool created = FlightPlanNodeService.CreateNodeAtDeltaV(deltaV, burnUT, burnOffsetFactor);
            if (!created)
            {
                // P8 (launch-17 NL-2): every synchronous refusal is intentional (the >= 30 s
                // proximity rule, the node cap, the UT floor, the new placement pre-check) and the
                // service already logs each one at Warn. Rendering it as an error here put 17 false
                // `[ERR ` positives in the launch-17 triage and showed the player a red status for
                // a refusal that is the feature working. Genuine failures keep their `[ERR ` lines
                // from the service (the add/settle exceptions), so triage loses nothing.
                FPStatus.Warning($"Node creation refused for a burn {FPUtility.SecondsToTimeString(burnUT - Game.UniverseModel.UniverseTime)} from now. See the FlightPlan log for the reason.");
            }
            return created;
        }

        // P4: the create coroutine that P3 staged here (the inline 0.2.8.5 sequence + the burn-centred
        // offset/re-point) has been hoisted into FlightPlanNodeService.CreateNodeAtUT. The plugin keeps
        // no second copy of the sequence: CreateManeuverNodeCaller above is the only entry point, and
        // every rule it used to apply now lives in the service. `_currentNode` is still assigned there
        // (the service writes FlightPlanPlugin.Instance._currentNode) so the legacy field keeps its
        // meaning for the UI phase.

        // Flight Plan API Methods
        public bool Circularize(double burnUT, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"Circularize {BurnTimeOption.TimeRefDesc}");
            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVToCircularize(_orbit, burnUT);

            FPStatus.Ok($"Ready to Circularize {BurnTimeOption.TimeRefDesc}");

            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Circularize Now: Solution Not Found!");
                return false;
            }
        }

        public bool SetNewPe(double burnUT, double newPe, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"SetNewPe {BurnTimeOption.TimeRefDesc}");

            FPStatus.Ok($"Ready to Change Pe {BurnTimeOption.TimeRefDesc}");

            Logger.LogDebug($"Seeking Solution: TargetPeR_km {newPe} m, currentPeR {_orbit.Periapsis} m, body.radius {_orbit.referenceBody.radius} m");
            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVToChangePeriapsis(_orbit, burnUT, newPe);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Set New Pe: Solution Not Found!");
                return false;
            }
        }

        public bool SetNewAp(double burnUT, double newAp, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"SetNewAp {BurnTimeOption.TimeRefDesc}");

            FPStatus.Ok($"Ready to Change Ap {BurnTimeOption.TimeRefDesc}");

            Logger.LogDebug($"Seeking Solution: TargetApR_km {newAp} m, currentApR {_orbit.Apoapsis} m");
            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVToChangeApoapsis(_orbit, burnUT, newAp);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Set New Ap: Solution Not Found!");
                return false;
            }
        }

        public bool Ellipticize(double burnUT, double newAp, double newPe, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"Ellipticize: Set New Pe and Ap {BurnTimeOption.TimeRefDesc}");

            FPStatus.Ok($"Ready to Ellipticize {BurnTimeOption.TimeRefDesc}");

            if (newPe > newAp)
            {
                (newPe, newAp) = (newAp, newPe);
                FPStatus.Warning("Pe Setting > Ap Setting");
            }

            Logger.LogDebug($"Seeking Solution: TargetPeR_km {newPe} m, TargetApR_km {newAp} m, body.radius {_orbit.referenceBody.radius} m");
            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVToEllipticize(_orbit, burnUT, newPe, newAp);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Set New Pe and Ap: Solution Not Found !");
                return false;
            }
        }

        public bool SetInclination(double burnUT, double inclination, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"SetInclination: Set New Inclination {inclination}° {BurnTimeOption.TimeRefDesc}");
            Vector3d _deltaV;

            FPStatus.Ok($"Ready to Change Inclination {BurnTimeOption.TimeRefDesc}");

            _deltaV = OrbitalManeuverCalculator.DeltaVToChangeInclination(_orbit, burnUT, inclination);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Set New Inclination: Solution Not Found !");
                return false;
            }
        }

        public bool SetNewLAN(double burnUT, double newLANvalue, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"SetNewLAN: Set New LAN {newLANvalue}° {BurnTimeOption.TimeRefDesc}");

            if (Math.Abs(_orbit.inclination) < 10)
                FPStatus.Warning($"WARNING: Orbital plane has low inclination of {_orbit.inclination:N2}° (recommend i > 10°). Maneuver many not be accurate.");
            else
                FPStatus.Warning($"Experimental LAN Change {BurnTimeOption.TimeRefDesc}");

            Logger.LogDebug($"Seeking Solution: newLANvalue {newLANvalue}°");
            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVToShiftLAN(_orbit, burnUT, newLANvalue);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Set New LAN: Solution Not Found !");
                return false;
            }
        }

        public bool SetNodeLongitude(double burnUT, double newNodeLongValue, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"SetNodeLongitude: Set Node Longitude {newNodeLongValue}° {BurnTimeOption.TimeRefDesc}");

            FPStatus.Warning($"Experimental Node Longitude Change {BurnTimeOption.TimeRefDesc}");

            Logger.LogDebug($"Seeking Solution: newNodeLongValue {newNodeLongValue}°");
            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVToShiftNodeLongitude(_orbit, burnUT, newNodeLongValue);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Shift Node Longitude: Solution Not Found !");
                return false;
            }
        }

        public bool SetNewSMA(double burnUT, double newSMA, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            Logger.LogDebug($"SetNewSMA {BurnTimeOption.TimeRefDesc}");

            FPStatus.Ok($"Ready to Change SMA Change {BurnTimeOption.TimeRefDesc}");

            Logger.LogDebug($"Seeking Solution: newSMA {newSMA} m");
            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVForSemiMajorAxis(_orbit, burnUT, newSMA);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error("Set New SMA: Solution Not Found!");
                return false;
            }
        }

        // No longer takes double burnUT. Need to sort out how this can be called as an API method
        public bool MatchPlanes(TimeReference time_ref, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            IKeplerOrbit tgtOrbit = null;
            if (_currentTarget.IsPart)
            {
                tgtOrbit = _currentTarget.Part.PartOwner.SimulationObject.Orbit;
            }
            else if (_currentTarget.IsVessel || _currentTarget.IsCelestialBody)
            {
                tgtOrbit = _currentTarget.Orbit;
            }

            Logger.LogDebug($"MatchPlanes: Match Planes with {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");
            double burnUTout = _UT + 1;

            FPStatus.Ok($"Ready to Match Planes with {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");

            Vector3d _deltaV = Vector3d.zero;
            if (time_ref == TimeReference.REL_ASCENDING)
                _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesAscending(_orbit, tgtOrbit, _UT, out burnUTout);
            else if (time_ref == TimeReference.REL_DESCENDING)
                _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesDescending(_orbit, tgtOrbit, _UT, out burnUTout);
            else if (time_ref == TimeReference.REL_NEAREST_AD)
            {
                if (_orbit.TimeOfAscendingNode(tgtOrbit, _UT) < _orbit.TimeOfDescendingNode(tgtOrbit, _UT))
                    _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesAscending(_orbit, tgtOrbit, _UT, out burnUTout);
                else
                    _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesDescending(_orbit, tgtOrbit, _UT, out burnUTout);
            }
            else if (time_ref == TimeReference.REL_HIGHEST_AD)
            {
                var anTime = _orbit.TimeOfAscendingNode(tgtOrbit, _UT);
                var dnTime = _orbit.TimeOfDescendingNode(tgtOrbit, _UT);
                if (_orbit.Radius(anTime) > _orbit.Radius(dnTime))
                    _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesAscending(_orbit, tgtOrbit, _UT, out burnUTout);
                else
                    _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesDescending(_orbit, tgtOrbit, _UT, out burnUTout);
            }
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUTout, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error($"Match Planes with {_currentTarget.Name} at AN: Solution Not Found!");
                return false;
            }
        }

        public bool HohmannTransfer(double burnUT, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            IKeplerOrbit tgtOrbit = null;
            if (_currentTarget.IsPart)
            {
                tgtOrbit = _currentTarget.Part.PartOwner.SimulationObject.Orbit;
            }
            else if (_currentTarget.IsVessel || _currentTarget.IsCelestialBody)
            {
                tgtOrbit = _currentTarget.Orbit;
            }

            Logger.LogDebug($"HohmannTransfer: Hohmann Transfer to {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");
            double _burnUT1, _burnUT2;
            Vector3d _deltaV1, _deltaV2;

            FPStatus.Warning($"Ready to Transfer to {_currentTarget.Name}");

            double LagTime = 0.0;

            bool Rendezvous = true;
            bool Coplanar = false;
            bool Capture = true;

            double lagTime = Rendezvous ? LagTime : 0;

            bool fixedTime = false;

            bool _simpleTransfer = false;
            if (_simpleTransfer)
            {
                double offsetDist = 0;
                if (_currentTarget.IsCelestialBody)
                {
                    offsetDist = _currentTarget.CelestialBody.radius + FpUiController.TargetInterceptDistanceCelestial_m;
                    Logger.LogDebug($"HohmannTransfer: OffsetDist for celestial encounter {offsetDist / 1000:N2} km");
                }
                else
                {
                    offsetDist = FpUiController.TargetInterceptDistanceVessel_m;
                    Logger.LogDebug($"HohmannTransfer: OffsetDist for non-celestial encounter {offsetDist:N2} m");
                }

                Logger.LogDebug($"Hohmann Transfer: Calling DeltaVAndTimeForHohmannTransfer");
                (_deltaV1, _burnUT1, _deltaV2, _burnUT2) = OrbitalManeuverCalculator.DeltaVAndTimeForHohmannTransfer(_orbit, tgtOrbit, _UT,
                    lagTime, fixedTime, Coplanar, Rendezvous, Capture);
            }
            else
            {
                // The legacy's `bool _intercept_only` was set to `true` in both branches of an
                // if/else and read nowhere - deleted (D10, dormant code). It never reached a call
                // or a field, so removing it is behaviourally inert.
                Logger.LogDebug($"Hohmann Transfer: Calling DeltaVAndTimeForHohmannTransfer");
                (_deltaV1, _burnUT1, _deltaV2, _burnUT2) = OrbitalManeuverCalculator.DeltaVAndTimeForHohmannTransfer(_orbit, tgtOrbit, _UT,
                    lagTime, fixedTime, Coplanar, Rendezvous, Capture);
            }

            if (_deltaV1 != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV1, _burnUT1, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error($"Hohmann Transfer to {_currentTarget.Name}: Solution Not Found !");
                return false;
            }
        }

        public bool InterceptTgt(double burnUT, double tgtUT, double burnOffsetFactor = -0.5)
        {
            // Experimental - also not working at all. Places node at wrong time, often on the wrong side of mainbody (lowering when should be raising and vice versa)
            // Adapted from call found in MechJebModuleScriptActionRendezvous.cs for "Get Closer"
            // Similar to code in MechJebModuleRendezvousGuidance.cs for "Get Closer" Button code.

            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            IKeplerOrbit tgtOrbit = null;
            if (_currentTarget.IsPart)
            {
                tgtOrbit = _currentTarget.Part.PartOwner.SimulationObject.Orbit;
            }
            else if (_currentTarget.IsVessel || _currentTarget.IsCelestialBody)
            {
                tgtOrbit = _currentTarget.Orbit;
            }

            Logger.LogDebug($"InterceptTgt: Intercept {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");
            double _interceptUT = _UT + tgtUT;
            double _offsetDistance;
            Vector3d _deltaV;

            FPStatus.Warning($"Experimental Intercept of {_currentTarget.Name} Ready");

            Logger.LogDebug($"Seeking Solution: InterceptTime {FpUiController.TargetInterceptTime_s} s");
            if (_currentTarget.IsCelestialBody) // For a target that is a celestial
                _offsetDistance = _currentTarget.Orbit.referenceBody.radius + 50000;
            else
                _offsetDistance = 100;
            (_deltaV, _) = OrbitalManeuverCalculator.DeltaVToInterceptAtTime(_orbit, burnUT, tgtOrbit, _interceptUT, _offsetDistance);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error($"Intercept {_currentTarget.Name}: No Solution Found !");
                return false;
            }
        }

        public bool CourseCorrection(double burnUT, double interceptDistance, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            IKeplerOrbit tgtOrbit = null;
            if (_currentTarget.IsPart)
            {
                tgtOrbit = _currentTarget.Part.PartOwner.SimulationObject.Orbit;
            }
            else if (_currentTarget.IsVessel || _currentTarget.IsCelestialBody)
            {
                tgtOrbit = _currentTarget.Orbit;
            }

            Logger.LogDebug($"CourseCorrection: Course Correction burn to improve trajectory to {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");
            double _burnUTout;
            Vector3d _deltaV;

            FPStatus.Ok("Course Correction Ready");

            if (_currentTarget.IsCelestialBody) // For a target that is a celestial
            {
                if (interceptDistance < 0)
                    interceptDistance = _currentTarget.CelestialBody.radius + 50000; // m (PeR at celestial target)
                else
                    interceptDistance += _currentTarget.CelestialBody.radius;
                Logger.LogDebug($"Seeking Solution for Celestial Target with Pe {interceptDistance}");
                _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeForCheapestCourseCorrection(_orbit, _UT, tgtOrbit, _currentTarget.Orbit.referenceBody, interceptDistance, out _burnUTout);
            }
            else // For a tartget that is not a celestial
            {
                if (interceptDistance < 0)
                    interceptDistance = 100; // m
                Logger.LogDebug($"Seeking Solution for Non-Celestial Target with closest approach {interceptDistance}");
                _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeForCheapestCourseCorrection(_orbit, _UT, tgtOrbit, interceptDistance, out _burnUTout);
            }
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, _burnUTout, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error($"Course Correction for tragetory to {_currentTarget.Name}: No Solution Found !");
                return false;
            }
        }

        public bool MoonReturn(double burnUT, double targetMRPeR, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;
            Vector3d _deltaV;

            Logger.LogDebug($"MoonReturn: Return from {_orbit.referenceBody.Name} {BurnTimeOption.TimeRefDesc} seeking Pe {targetMRPeR / 1000:N3} km");
            var _e = _orbit.eccentricity;

            FPStatus.Warning($"Ready to Return from {_orbit.referenceBody.Name}?");

            if (_e > 0.2)
            {
                FPStatus.Error($"Moon Return: Starting Orbit Eccentrity Too Large {_e.ToString("F2")} is > 0.2");
                return false;
            }
            else
            {
                double _burnUTout;
                Logger.LogDebug($"Moon Return Attempting to Solve...");
                (_deltaV, _burnUTout) = OrbitalManeuverCalculator.DeltaVAndTimeForMoonReturnEjection(_orbit, _UT, targetMRPeR);
                if (_deltaV != Vector3d.zero)
                {
                    return CreateManeuverNodeCaller(_deltaV, _burnUTout, burnOffsetFactor);
                }
                else
                {
                    FPStatus.Error("Moon Return: No Solution Found!");
                    return false;
                }
            }
        }

        public bool MatchVelocity(double burnUT, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            IKeplerOrbit tgtOrbit = null;
            if (_currentTarget.IsPart)
            {
                tgtOrbit = _currentTarget.Part.PartOwner.SimulationObject.Orbit;
            }
            else if (_currentTarget.IsVessel || _currentTarget.IsCelestialBody)
            {
                tgtOrbit = _currentTarget.Orbit;
            }


            Logger.LogDebug($"MatchVelocity: Match Velocity with {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");

            FPStatus.Ok($"Ready to Match Velocity with {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");

            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVToMatchVelocities(_orbit, burnUT, tgtOrbit);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, burnUT, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error($"Match Velocity with {_currentTarget.Name} at Closest Approach: No Solution Found!");
                return false;
            }
        }

        public bool PlanetaryXfer(double burnUT, double burnOffsetFactor = -0.5)
        {
            double _UT = Game.UniverseModel.UniverseTime;
            IKeplerPatch _orbit = _activeVessel.Orbit;

            IKeplerOrbit tgtOrbit = null;
            if (_currentTarget.IsPart)
            {
                tgtOrbit = _currentTarget.Part.PartOwner.SimulationObject.Orbit;
            }
            else if (_currentTarget.IsVessel || _currentTarget.IsCelestialBody)
            {
                tgtOrbit = _currentTarget.Orbit;
            }

            Logger.LogDebug($"PlanetaryXfer: Transfer to {_currentTarget.Name} {BurnTimeOption.TimeRefDesc}");
            // The legacy also declared `_burnUTout2` here; its only consumer was the commented-out
            // Lambert ejection call (`DeltaVAndTimeForInterpolanetaryLambertTransferEjection`), which the
            // legacy never invoked. Not ported - it would be a second, unused out-slot.
            double _burnUTout;

            // The legacy read the dropdown itself here, because the dropdown is what mirrors the
            // selected TimeReference. `DropdownField.value` is the *selected choice's text*, which is
            // the same string `TextTimeRef` maps the TimeReference to - so this compares like for like.
            // Guarded: the controller exists whenever this method is reachable (it is only called from
            // the UI), but a null dropdown must not throw inside a maneuver calculation.
            bool _syncPhaseAngle = FpUiController.BurnOptionsDropdown != null
                                   && FpUiController.BurnOptionsDropdown.value == BurnTimeOption.TextTimeRef[TimeReference.NEXT_WINDOW];

            if (_orbit.referenceBody.referenceBody == null)
                Logger.LogDebug($"PlanetaryXfer: doesn't make sense to plot an interplanetary transfer from an orbit around {_orbit.referenceBody.Name}");

            if (_orbit.referenceBody.referenceBody != tgtOrbit.referenceBody)
            {
                if (_orbit.referenceBody == tgtOrbit.referenceBody)
                    Logger.LogDebug($"PlanetaryXfer: use regular Hohmann transfer function to intercept another body orbiting {_orbit.referenceBody.Name}");
                Logger.LogDebug($"PlanetaryXfer: an interplanetary transfer from within {_orbit.referenceBody.Name}'s sphere of influence must target a body that orbits {_orbit.referenceBody.Name}'s parent, {_orbit.referenceBody.referenceBody.Name}");
            }

            // Simple warnings
            if (_orbit.referenceBody.Orbit.RelativeInclination(tgtOrbit) > 30)
            {
                Logger.LogWarning($"PlanetaryXfer: target's orbital plane is at a {_orbit.RelativeInclination(tgtOrbit).ToString("F0")}º angle to {_orbit.referenceBody.Name}'s orbital plane (recommend at most 30º). Planned interplanetary transfer may not intercept target properly.");
            }
            else
            {
                double relativeInclination = Vector3d.Angle(_orbit.OrbitNormal(), _orbit.referenceBody.Orbit.OrbitNormal());
                if (relativeInclination > 10)
                {
                    Logger.LogWarning($"PlanetaryXfer: Recommend starting interplanetary transfers from {_orbit.referenceBody.Name} from an orbit in the same plane as {_orbit.referenceBody.Name}'s orbit around {_orbit.referenceBody.referenceBody.Name}. Starting orbit around {_orbit.referenceBody.Name} is inclined {relativeInclination.ToString("F1")}º with respect to {_orbit.referenceBody.Name}'s orbit around {_orbit.referenceBody.referenceBody.Name} (recommend < 10º). Planned transfer may not intercept target properly.");
                }
                else if (_orbit.eccentricity > 0.2)
                {
                    Logger.LogWarning($"PlanetaryXfer: Recommend starting interplanetary transfers from a near-circular orbit (eccentricity < 0.2). Planned transfer is starting from an orbit with eccentricity {_orbit.eccentricity.ToString("F2")} and so may not intercept target properly.");
                }
            }
            FPStatus.Warning($"Experimental Transfer to {_currentTarget.Name} Ready");

            Vector3d _deltaV = OrbitalManeuverCalculator.DeltaVAndTimeForInterplanetaryTransferEjection(_orbit, _UT, tgtOrbit, _syncPhaseAngle, out _burnUTout);
            if (_deltaV != Vector3d.zero)
            {
                return CreateManeuverNodeCaller(_deltaV, _burnUTout, burnOffsetFactor);
            }
            else
            {
                FPStatus.Error($"Planetary Transfer to {_currentTarget.Name}: No Solution Found!");
                return false;
            }
        }
    }
}
