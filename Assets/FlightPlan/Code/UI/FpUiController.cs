// The legacy file declared only these usings and relied on <ImplicitUsings>enable (C# 10) for
// System / System.Collections.Generic / System.Linq - the same toolchain assumption that let it use a
// file-scoped namespace. Both are REVERSEd here: this project builds at -langversion:9.0 with no
// implicit usings, so the three are declared explicitly.
using System;
using System.Collections.Generic;
using System.Linq;
using FlightPlan.API.Extensions;
using FPUtilities;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.DeltaV;
using KSP.Sim.impl;
using KSP.UI.Binding;
using MuMech;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightPlan
{
    /// <summary>
    ///  Flight Plan UITK GUI controller
    /// </summary>
    public class FpUiController : KerbalMonoBehaviour
    {
        // private GameInstance game;

        private static FpUiController _instance;
        public static FpUiController Instance { get => _instance; }

        // GUI Stuff
        public static VisualElement container;

        // The window's PanelRenderer, handed over by FlightPlanPlugin right after Window.Create(...)
        // returns (see Initialize below). It is the successor of the 0.2.8.5 UIDocument: at this pin
        // `Window.Create` returns a PanelRenderer and the visual tree root is FETCHED from it through
        // `UitkForKsp2.API.Extensions.GetWindowRoot(renderer)` - never read off the window object.
        private static PanelRenderer _panel;

        private bool initialized = false;
        public static bool GUIenabled = true;
        private GameState _gameState = GameState.Invalid;

        public static double TargetMRPeR_m = 100000.0;
        public static double TargetPeR_m = 100000.0;
        public static double TargetApR_m = 200000.0;
        public static double TargetSMA_m = 200000.0;
        public static double TargetAdvXferPe_m = 60000.0;
        public static double TargetInc_deg = 0.0;
        public static double TargetLAN_deg = 0.0;
        public static double TargetNodeLong_deg = 0.0;
        public static double TargetInterceptTime_s = 3600.0;
        public static double TargetInterceptDistanceCelestial_m = 100.0;
        public static double TargetInterceptDistanceVessel_m = 100.0;
        public static double Altitude_km;
        public static double TimeOffset_s;
        public static double MaxArrivalTime_s = 0.0;
        public static double TargetEcc = 0.0;


        //private double _newPeValue = 100;
        //private double _newApValue = 200;
        //private double _newIncValue = 0;
        //private double _newLANValue = 0;
        //private double _newSMAValue = 200;
        //private double _interceptValueTRMS = 3600;
        //private double _interceptValueTRMC = 3600;
        //private double _interceptValue;
        //private double _courseCorrectionValueTRMS = 100;
        //private double _courseCorrectionValueTRMC = 100;
        private double _courseCorrectionValue;

        private int NumSats = 3;
        private int NumOrb = 1;
        private double OccModAtm = 0.75;
        private double OccModVac = 0.9;
        //private double ApAltitude_km;
        //private double PeAltitude_km;
        //private double _apoapsis = 600;
        //private double _periapsis = 400;
        private double _synchronousAlt = 1900.0;
        private double _semiSynchronousAlt = 1900.0;
        private double _minLOSAlt = 400.0;
        private double _target_alt_km = 600.0;       // planned altitide for deployed satellites (destiantion orbit)
        private double _satPeriod;                   // The period of the destination orbit
        private double _xferPeriod;                  // The period of the resonant deploy orbit (_xferPeriod = _resonance*_satPeriod)
                                                     // private string _selectedTarget;
        private string _burnTimeOption;
        private VesselComponent _activeVessel;
        private SimulationObjectModel _currentTarget;

        // Data other classes and methods will need (needed to handle fixAp and fixPe maneuvers)
        public static double Ap2 { get; set; } // The resonant deploy orbit apoapsis
        public static double Pe2 { get; set; } // The resonant deploy orbit periapsis

        private StyleColor buttonBackgroundColor;
        private StyleColor buttonTextColor;
        private StyleColor buttonBorderColor;

        // Close Button
        private Button CloseButton;

        // Vessel Situation
        private Label VesselSituation;

        // TargetSelection
        private Button TargetTypeButton;
        private DropdownField TargetSelectionDropdown;

        // TabBar button boxes (used to control visibility and placement of TabBar buttons)
        // VisualElement OSMButtonBox;
        private VisualElement TRMShipToShipButtonBox;
        private VisualElement TRMShipToCelestialButtonBox;
        private VisualElement OTMMoonButtonBox;

        private VisualElement OTMPlanetButtonBox;
        // VisualElement ROMButtonBox;

        private enum Tab : int
        {
            OSM,
            TRMShipToShip,
            TRMShipToCelestial,
            OTMMoon,
            OTMPlanet,
            ROM
        }

        // TabBar buttons (control which panel is displayed)
        private Button OSMButton;
        private Button TRMShipToShipButton;
        private Button TRMShipToCelestialButton;
        private Button OTMMoonButton;
        private Button OTMPlanetButton;
        private Button ROMButton;
        private List<Button> TabButtons = new();

        // Panel Label (set this whenever activating a panel to identify the panel)
        private Label PanelLabel;

        // BurnTimeOptions dropdown
        public static DropdownField BurnOptionsDropdown;

        // UI panels (used to control center part of UI based on the selected tab bar button)
        private VisualElement OSMPanel;
        private VisualElement TRMShipToShipPanel;
        private VisualElement TRMShipToCelestialPanel;
        private VisualElement OTMMoonPanel;
        private VisualElement OTMPlanetPanel;
        private VisualElement ROMPanel;
        private List<VisualElement> panels = new();

        private List<string> panelNames = new();
        // Dictionary<string, VisualElement> tabs = new();
        // string thisTab;
        private Tab currentTabNum = Tab.OSM;

        private List<Button> toggleButtons = new();

        // OMSPanel buttons and fields
        private Button CircularizeButtonOSM;
        private Button NewPeButtonOSM;
        private TextField NewPeValueOSM;
        private Button NewApButtonOSM;
        private TextField NewApValueOSM;
        private Button NewPeApButtonOSM;
        private Button NewIncButtonOSM;
        private TextField NewIncValueOSM;
        private Button NewLANButtonOSM;
        private TextField NewLANValueOSM;
        private Button NewSMAButtonOSM;
        private TextField NewSMAValueOSM;

        // TRMS buttons and fields
        private Toggle SelectPortToggle;
        private Label TargetOrbitTRMS;
        private Label CurrentOrbitTRMS;
        private Label RelativeIncTRMS;
        private Label SynodicPeriodTRMS;
        private Label NextWindowTRMS;
        private Label NextClosestApproachTRMS;
        private Label SeparationAtCaTRMS;
        private VisualElement RelativeVelocityRowTRMS;
        private Label RelativeVelocityTRMS;
        private Button MatchPlanesButtonTRMS;
        private Button NewApButtonTRMS;
        private TextField NewApValueTRMS;
        private Button CircularizeButtonTRMS;
        private Button HohmannTransferButtonTRMS;
        private Button CourseCorrectionButtonTRMS;
        private TextField CourseCorrectionValueTRMS;
        private Button MatchVelocityButtonTRMS;
        private Button InterceptButtonTRMS;
        private TextField InterceptValueTRMS;
        private Label TRMStatus;

        // TRMCPanel buttons and fields
        private Label TargetOrbitTRMC;
        private Label CurrentOrbitTRMC;
        private Label RelativeIncTRMC;
        private Label PhaseAngleTRMC;
        private Label XferPhaseAngleTRMC;
        private Label XferTimeTRMC;
        private Label SynodicPeriodTRMC;
        private Label NextWindowTRMC;
        private Label NextClosestApproachTRMC;
        private Button MatchPlanesButtonTRMC;
        private Button HohmannTransferButtonTRMC;
        private Button CourseCorrectionButtonTRMC;
        private TextField CourseCorrectionValueTRMC;
        private Button InterceptButtonTRMC;
        private TextField InterceptValueTRMC;
        private Button MatchVelocityButtonTRMC;

        // OTMMoonPanel UI controls
        private Button ReturnFromMoonButtonOTM;
        private TextField MoonReturnPeValue;

        // OTMPlanetPanel UI controls
        private VisualElement DataDisplayGroup;
        private Label RelativeIncOTM;
        private Label PhaseAngleOTM;
        private Label XferPhaseAngleOTM;
        private Label XferTimeOTM;
        private Label SynodicPeriodOTM;
        private Label NextWindowOTM;
        private Label EjectionDvOTM;
        private Button InterplanetaryXferButton;
        private VisualElement AdvInterplanetaryXferGroup;
        private Button AdvInterplanetaryXferButton;
        private VisualElement AdvXferGroup;
        public static VisualElement LimitedTimeGroup;
        public static VisualElement PorkchopGroup;
        public static VisualElement PorkchopDisplay;
        public static TextField MaxArrivalTimeInput;
        public static Label Computing;
        public static Label XferDeltaVLabel;
        private Button ResetButton;
        public static Toggle CaptureBurnToggle;
        public static TextField AdvXferPeriapsisInput;
        private Button LowestDvButton;
        private Button ASAPButton;
        public static Label DepartureTimeLabel;
        public static Label TransitDurationTimeLabel;

        // ROMPanel UI controls
        private Button IncreasePayloadsButton;
        private Button DecreasePayloadsButton;
        private Label NumPayloads;
        private Button IncreaseOrbitsButton;
        private Button DecreaseOrbitsButton;
        private Label NumOrbits;
        private Label OrbitalResonance;
        private TextField TargetAltitudeInput;
        private Button SetApoapsisButton;
        private Label CurrentApoapsis;
        private Button SetPeriapsisButton;
        private Label CurrentPeriapsis;
        private Button SetSynchronousAltButton;
        private Label SynchronousAlt;
        private Button SetSemiSynchronousAltButton;
        private Label SemiSynchronousAlt;
        private Label SOIAlt;
        private Button SetMinLOSAltButton;
        private Label MinLOSAlt;
        private Toggle Occlusion;
        private VisualElement AtmOccRow;
        private TextField AtmOccInput;
        private VisualElement VacOccRow;
        private TextField VacOccInput;
        private Toggle Dive;
        private Label ResonantOrbitPeriod;
        private Label ResonantOrbitAp;
        private Label ResonantOrbitPe;
        private Label ResonantOrbitEcc;
        private Label ResonantOrbitInjection;
        private VisualElement FixPeGroup;
        private Button FixPeButton;
        private Label FixPeStatus;
        private VisualElement FixApGroup;
        private Button FixApButton;
        private Label FixApStatus;

        // BurnTimeOption situational inputs (display or not depending on the burn time option selected)
        private VisualElement AfterFixedTime;
        private TextField AfterFixedTimeInput;
        private VisualElement AtAnAltitude;
        private TextField ManeuverAltitudeInput;

        // General All-Purpose UI Status display field
        private Label Status;

        // BottomBar UI elements
        private VisualElement MakeNodeButtonBox;
        private VisualElement MNCButtonBox;
        private VisualElement K2D2ButtonBox;
        private Button MakeNodeButton;
        private Button MNCButton;
        private Button K2D2Button;
        private Label K2D2Status;

        public FPOtherModsInterface OtherMods = null;

        private ManeuverType selectedManeuver;

        // ============================ FIX 2: per-frame write change-detection ====================
        // The legacy `Update()` wrote ~52 `Label.text` and ~51 `style.*` values unconditionally, every
        // frame, into a tree of 468 descendants - each write marks layout/style dirty, and a window
        // drag is already a per-frame layout + repaint, so the two compound into visible hitching.
        // These helpers remember the last value written per element and only touch the UI when it
        // actually changed. The element tree is rebuilt only when the window's UI is rebuilt: the
        // controller binds through the PanelRenderer `Window.Create` returned, and on that path
        // `OnUiReload` drops these caches (InvalidateUiCaches) so they can never be compared against a
        // tree that has been replaced.
        private readonly Dictionary<VisualElement, string> _lastText = new Dictionary<VisualElement, string>();
        private readonly Dictionary<VisualElement, DisplayStyle> _lastDisplay = new Dictionary<VisualElement, DisplayStyle>();
        // UI-fix increment 4: exactly ONE cache per colour slot. Increments 2-3 kept two parallel
        // families - a Color-keyed dictionary and a StyleColor-keyed one - for the same underlying
        // `style.color` / four `style.border*Color` / `style.backgroundColor` slots. Both families
        // wrote the same slots, so a write through one left the other cache stale, and the next write
        // through that other family SKIPPED on its stale "already equal" compare: selecting operation
        // button A wrote green inline, and the later deselect of A re-sent the (unchanged) unset
        // StyleColor against an unchanged StyleColor cache, was skipped, and the inline green was
        // never cleared - every button ever selected stayed green (the launch-10 multi-select bug).
        // The Color family now delegates to the StyleColor family, so the last value written BY EITHER
        // family is the single cached truth and a green -> unset transition is a real change again.
        //
        // The struct is what gets cached, not its `.value` Color: the unselected toggle branches must
        // write the CAPTURED StyleColor struct (keyword UNSET), not `.value` - `.value` on an unset
        // StyleColor resolves to transparent/black, and writing that as an explicit inline override
        // made every unselected operation button invisible (the launch-9 regression). Writing the
        // struct itself is a no-op at the slot when it is unset, so the USS class styling shows
        // through - exactly the legacy behaviour. Caching the struct keeps the change-detection
        // discipline; the compare is keyword AND color (StyleColor's own `==` is not relied on). The
        // live value is deliberately NEVER read back off `element.style`: Unity normalises some
        // keywords, which would make that read-back compare lie.
        private readonly Dictionary<VisualElement, StyleColor> _lastTextColorStyle = new Dictionary<VisualElement, StyleColor>();
        private readonly Dictionary<VisualElement, StyleColor> _lastBorderColorStyle = new Dictionary<VisualElement, StyleColor>();
        private readonly Dictionary<VisualElement, StyleColor> _lastBackgroundColorStyle = new Dictionary<VisualElement, StyleColor>();
        private readonly Dictionary<DropdownField, List<string>> _lastChoices = new Dictionary<DropdownField, List<string>>();

        // Writes actually performed vs suppressed by the change-detection above. Reported once per
        // ~5 s of VISIBLE window time (the counters are re-armed whenever the window is lowered) so the
        // next launch can measure the fix instead of taking it on faith.
        private long _uiWritesPerformed;
        private long _uiWritesSkipped;
        private float _uiSummaryAt;
        private const float UiSummaryInterval = 5.0f;

        // UI-fix increment 2: the previous frame's gate visibility. A false -> true edge is the
        // "window just reopened" event that triggers InvalidateUiCaches() - after a hide/show
        // cycle the fresh tree may carry UXML-default styling while the caches above still hold
        // the old last-written values, which is exactly the launch-8 regression (unstyled
        // operation buttons; container stuck hidden after one AppBar toggle).
        private bool _uiWasVisible;

        // UI-fix increment 5: the ESC-menu suppression latch. True while the game's escape menu is up
        // and this mod has lowered the window *because of it* - pure presentation state: the logical
        // open/closed state (FlightPlanPlugin.InterfaceEnabled) and the AppBar toggle are untouched.
        // It has to gate Update()'s visibility computation as well as the immediate hide, because that
        // per-frame block writes `display` on the container every frame and would otherwise put the
        // window straight back on top of the menu on the very next frame.
        private static bool _escapeMenuSuppressed;

        // The target dropdown's list rebuild: `ListBodies()` walks the body tree (one string per body)
        // and `ListVessels()` rebuilds a dictionary through LINQ, both every frame in the legacy. The
        // contents only change when the target type, the active vessel, the reference body or the
        // vessel set changes, so the rebuild is gated on those plus a bounded refresh below.
        private const int TargetRelistIntervalFrames = 20;
        private int _targetRelistCountdown;
        private string _lastTargetTypeText;
        private VesselComponent _lastRelistVessel;   // reference compare: no per-frame `GlobalId.ToString()`
        private string _lastRelistBodyName;
        private SimulationObjectModel _lastShownTarget; // the target the dropdown row currently reflects
        private string _lastShownTargetChoice;          // ... and the exact choice text written for it
        private string _lastPreselectChoice;            // the display-only body currently shown
        // FIX 2 (refinement): the celestial preselect's `FindChoice` used to run on EVERY frame -
        // a full scan of the body list per frame. Its inputs only change when the choices list
        // object is replaced (any relist or target-type switch) or the orbited body's name
        // changes, so the lookup is cached behind exactly those two compares plus the first-frame
        // null-reference compare. The scan then runs once per actual input change, not per frame.
        private List<string> _lastChoicesListRef;       // the choices list the cached lookup came from
        private string _lastBodyNameSearched;           // the body name that lookup searched for
        private string _bodyChoice;                     // ... and the choice it matched (or null)
        private string _lastManeuverTypeDesc;           // FIX 2: last inputs to the ManeuverDescription
        private TimeReference _lastTimeRef;             // ... composition - both move only on events
        private bool _targetPreselectLogged;
        private bool _targetPreselectMissingLogged;

        /// <summary>`Label.text`, written only when the string differs from the last one written.</summary>
        private void SetText(Label label, string value)
        {
            if (label == null) return;
            string last;
            if (_lastText.TryGetValue(label, out last) && string.Equals(last, value, StringComparison.Ordinal))
            {
                _uiWritesSkipped++;
                return;
            }
            _lastText[label] = value;
            label.text = value;
            _uiWritesPerformed++;
        }

        /// <summary>`style.display`, written only when the value differs from the last one written.</summary>
        private void SetDisplay(VisualElement element, DisplayStyle value)
        {
            if (element == null) return;
            DisplayStyle last;
            if (_lastDisplay.TryGetValue(element, out last) && last == value)
            {
                _uiWritesSkipped++;
                return;
            }
            _lastDisplay[element] = value;
            element.style.display = value;
            _uiWritesPerformed++;
        }

        /// <summary>`style.color` (text colour) from a concrete Color. UI-fix increment 4: delegates
        /// to the StyleColor route so both families share ONE cache per slot (see the cache
        /// declaration above) - a single cache is what makes the green -> unset deselect a real write.</summary>
        private void SetTextColor(VisualElement element, Color value)
        {
            SetTextColorStyle(element, new StyleColor(value));
        }

        /// <summary>The four `style.border*Color` slots from a concrete Color (they move together).
        /// UI-fix increment 4: delegates to the StyleColor route - one cache per slot.</summary>
        private void SetBorderColor(VisualElement element, Color value)
        {
            SetBorderColorStyle(element, new StyleColor(value));
        }

        /// <summary>`style.backgroundColor` from a concrete Color. UI-fix increment 4: delegates to
        /// the StyleColor route - one cache per slot.</summary>
        private void SetBackgroundColor(VisualElement element, Color value)
        {
            SetBackgroundColorStyle(element, new StyleColor(value));
        }

        // ======================= UI-fix increment 3: StyleColor-preserving twins ==================
        // The launch-9 regression: routing the unselected toggle branches through the Color helpers
        // above coerced the captured StyleColor fields (`buttonTextColor`/`buttonBorderColor`/
        // `buttonBackgroundColor`) through `.value`, which resolves an UNSET StyleColor into a
        // concrete transparent/black and wrote it as an explicit inline override - every unselected
        // operation button became invisible until clicked. The legacy wrote the StyleColor struct
        // itself; an unset StyleColor is a no-op at that style slot, so the USS class styling
        // (`.Maneuver-button` colours) and the runtime theme's `.unity-button` pseudo-state styling
        // showed through. These overloads restore that: assign the struct, cache the struct.

        /// <summary>True when two StyleColors carry the same keyword and the same resolved color.
        /// StyleColor's own operators are not relied on - the parts are compared directly.</summary>
        private static bool SameStyleColor(StyleColor a, StyleColor b)
        {
            if (a.keyword != b.keyword) return false;
            return a.value == b.value;
        }

        /// <summary>`style.color` as a StyleColor (preserves the keyword, including UNSET), written
        /// only on a real change.</summary>
        private void SetTextColorStyle(VisualElement element, StyleColor value)
        {
            if (element == null) return;
            StyleColor last;
            if (_lastTextColorStyle.TryGetValue(element, out last) && SameStyleColor(last, value))
            {
                _uiWritesSkipped++;
                return;
            }
            _lastTextColorStyle[element] = value;
            element.style.color = value;
            _uiWritesPerformed++;
        }

        /// <summary>The four `style.border*Color` slots as StyleColors (preserves the keyword,
        /// including UNSET), written only on a real change (they move together).</summary>
        private void SetBorderColorStyle(VisualElement element, StyleColor value)
        {
            if (element == null) return;
            StyleColor last;
            if (_lastBorderColorStyle.TryGetValue(element, out last) && SameStyleColor(last, value))
            {
                _uiWritesSkipped++;
                return;
            }
            _lastBorderColorStyle[element] = value;
            element.style.borderTopColor = value;
            element.style.borderBottomColor = value;
            element.style.borderLeftColor = value;
            element.style.borderRightColor = value;
            _uiWritesPerformed++;
        }

        /// <summary>`style.backgroundColor` as a StyleColor (preserves the keyword, including
        /// UNSET), written only on a real change.</summary>
        private void SetBackgroundColorStyle(VisualElement element, StyleColor value)
        {
            if (element == null) return;
            StyleColor last;
            if (_lastBackgroundColorStyle.TryGetValue(element, out last) && SameStyleColor(last, value))
            {
                _uiWritesSkipped++;
                return;
            }
            _lastBackgroundColorStyle[element] = value;
            element.style.backgroundColor = value;
            _uiWritesPerformed++;
        }

        /// <summary>`DropdownField.choices`, written only when the name list actually changed.
        /// The legacy assigned a fresh `List&lt;string&gt;` every frame; the control's `choices`
        /// setter compares by reference, so that rebuilt the popup on every single frame. The
        /// comparison here is allocation-free when the list is unchanged.</summary>
        private void SetChoices<V>(DropdownField dropdown, Dictionary<string, V> names)
        {
            if (dropdown == null || names == null) return;
            List<string> last;
            if (_lastChoices.TryGetValue(dropdown, out last) && SameNames(last, names.Keys))
            {
                _uiWritesSkipped++;
                return;
            }
            var next = new List<string>(names.Keys);
            _lastChoices[dropdown] = next;
            dropdown.choices = next;
            _uiWritesPerformed++;
        }

        /// <summary>Finds the entry of a `choices` list whose trimmed text equals `name` (the list
        /// indents moons), or null when there is no match - used by the display-only target preselect.
        /// FIX 2 (refinement): the legacy `Trim()` allocated a fresh string for every entry examined;
        /// this trims through <see cref="MemoryExtensions.Trim"/> on a span instead, so a full scan
        /// of the body list allocates nothing. Span slicing itself never allocates.</summary>
        private static string FindChoice(List<string> choices, string name)
        {
            if (choices == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < choices.Count; i++)
            {
                string entry = choices[i];
                if (entry == null) continue;
                // Span path: no per-entry string allocation. `MemoryExtensions.SequenceEqual`
                // over `ReadOnlySpan<char>` compares code units, i.e. ordinal semantics - the
                // trimmed span against the body name directly.
                if (entry.AsSpan().Trim().SequenceEqual(name.AsSpan()))
                    return entry;
            }
            return null;
        }

        /// <summary>Sequence-equal check between the cached names and a live dictionary key collection
        /// (both enumerators are structs, so this allocates nothing).</summary>
        private static bool SameNames<V>(List<string> cached, Dictionary<string, V>.KeyCollection keys)
        {
            if (cached == null || cached.Count != keys.Count) return false;
            int i = 0;
            foreach (string key in keys)
            {
                if (!string.Equals(cached[i], key, StringComparison.Ordinal)) return false;
                i++;
            }
            return true;
        }

        /// <summary>The measured write-throughput line. Logged once per ~5 s of visible window time.</summary>
        private void TickUiWriteSummary()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (_uiSummaryAt <= 0f)
            {
                _uiSummaryAt = now + UiSummaryInterval;
                return;
            }
            if (now < _uiSummaryAt) return;
            _uiSummaryAt = now + UiSummaryInterval;
            FlightPlanPlugin.Logger.LogInfo(
                $"UI write summary: performed = {_uiWritesPerformed}, skipped = {_uiWritesSkipped} (last 5.0 s)");
            _uiWritesPerformed = 0;
            _uiWritesSkipped = 0;
        }

        /// <summary>
        /// UI-fix increment 2: drop every change-detection cache so the next helper call writes
        /// through unconditionally. Called at the three measured events where the visual tree is
        /// rebuilt or re-styled outside this controller's per-frame writes - a hidden-to-visible
        /// gate transition, a game-state transition (map/vessel switches) and CleanUp(). Without
        /// this the fresh tree keeps UXML-default styling (the launch-8 "unstyled operation
        /// buttons" regression) or the container stays hidden after one AppBar toggle (the
        /// "click twice to reopen" regression), because every helper SKIPS on the cached
        /// last-written value. The skip-when-equal behaviour itself is untouched - it is what
        /// makes dragging smooth (the launch-8 measured win) - it just starts from a blank slate
        /// after each of these rare events. Steady-state skipping resumes on the next frame.
        /// </summary>
        private void InvalidateUiCaches()
        {
            // UI-fix increment 4: ONE cache per colour slot, so the count and the clears below cover
            // the three StyleColor dictionaries only - the Color-keyed trio is gone (the launch-10
            // multi-select bug came from those two families guarding the same slots with two caches).
            int dropped = _lastText.Count
                          + _lastDisplay.Count
                          + _lastTextColorStyle.Count
                          + _lastBorderColorStyle.Count
                          + _lastBackgroundColorStyle.Count
                          + _lastChoices.Count;
            _lastText.Clear();
            _lastDisplay.Clear();
            _lastChoices.Clear();
            _lastTextColorStyle.Clear();
            _lastBorderColorStyle.Clear();
            _lastBackgroundColorStyle.Clear();
            // Rows gated behind cached-tree state, so they re-apply against the fresh tree too.
            _lastPreselectChoice = null;   // the display-only celestial row repaints
            _lastManeuverTypeDesc = null;  // the ManeuverDescription recomposes and rewrites
            // Reset the write-summary window as well: the frame(s) right after an invalidation
            // are a full repaint burst, and folding them into the previous window would report
            // a misleading ratio. The next summary measures a fresh interval that includes the
            // burst - keeping the launch-9 numbers honest.
            _uiWritesPerformed = 0;
            _uiWritesSkipped = 0;
            _uiSummaryAt = 0f;
            FlightPlanPlugin.Logger.LogDebug(
                $"UI caches invalidated ({dropped} entries) - tree rebuilt or visibility changed");
        }

        private void Start()
        {
            // Binding normally already happened: FlightPlanPlugin's bootstrap calls Window.Create(...),
            // then AddComponent<FpUiController>() onto the window's own GameObject and hands the controller
            // the renderer through Initialize(PanelRenderer), whose UI-ready callback fires immediately.
            // This is the fallback for the case where the component is added or re-enabled by some other
            // route (K2D2Window.OnEnable does the same), and for a registration whose callback did not fire
            // synchronously.
            if (container == null)
            {
                if (_panel == null)
                {
                    _panel = GetComponent<PanelRenderer>();
                }
                if (_panel == null)
                {
                    FlightPlanPlugin.Logger.LogError("[FlightPlan] FpUiController.Start: no PanelRenderer on the " +
                                                     "window's GameObject - the window cannot be wired up");
                    return;
                }
                _panel.RegisterUIReloadCallback(OnUiReload);
            }
        }

        /// <summary>
        /// Called by FlightPlanPlugin immediately after Window.Create(...) with the PanelRenderer that call
        /// returned (the K2-D2 model: K2D2_Plugin.cs:314-317 hands the renderer to K2D2Window.Initialize).
        /// Registering the UI-ready callback is how the controller gets the root VisualElement without ever
        /// searching for the component itself. The callback fires once immediately - the tree is already
        /// built synchronously by the time Window.Create returns - and again on any later live UI reload.
        /// `PanelRenderer.RegisterUIReloadCallback(PanelRenderer.UIReloadCallback)` is Unity's own member
        /// (rank 1: monodis, UnityEngine.UIElementsModule.dll) - the same call the shipped
        /// `UitkForKsp2.Panel.WindowComponent.Initialize` makes (monodis: UitkForKsp2.dll, IL_002f) - so
        /// this needs no UitkForKsp2 wrapper.
        /// </summary>
        public void Initialize(PanelRenderer panel)
        {
            if (panel == null)
            {
                FlightPlanPlugin.Logger.LogError("[FlightPlan] FpUiController.Initialize got a null " +
                                                 "PanelRenderer - the window cannot be wired up");
                return;
            }

            _panel = panel;
            _panel.RegisterUIReloadCallback(OnUiReload);
        }

        /// <summary>
        /// Fires once when the callback is registered (the UI is loaded synchronously by then) and again on
        /// any later live UI reload. The root is re-fetched from the renderer on every pass; a reload replaces
        /// the tree, so every cached last-written value is dropped and `initialized` is cleared, which makes
        /// the next Update() re-run InitializeElements() - otherwise the controller would go on writing into
        /// detached elements and the fresh tree would keep its UXML-default styling.
        /// </summary>
        private void OnUiReload(PanelRenderer panel, VisualElement root)
        {
            if (panel == null)
            {
                FlightPlanPlugin.Logger.LogError("[FlightPlan] FpUiController.OnUiReload got a null " +
                                                 "PanelRenderer - the window cannot be wired up");
                return;
            }

            SetupDocument();
            // A reload is the fourth "tree rebuilt outside this controller's writes" event (the other
            // three are the hidden->visible gate, the game-state transition and CleanUp), so the same
            // contract applies: drop the change-detection caches and re-query the elements.
            InvalidateUiCaches();
            initialized = false;
        }

        public IKeplerPatch Orbit;
        public CelestialBodyComponent ReferenceBody;

        public static string SituationToString(VesselSituations situation)
        {
            return situation switch
            {
                VesselSituations.PreLaunch => "Pre-Launch",
                VesselSituations.Landed => "Landed",
                VesselSituations.Splashed => "Splashed down",
                VesselSituations.Flying => "Flying",
                VesselSituations.SubOrbital => "Suborbital",
                VesselSituations.Orbiting => "Orbiting",
                VesselSituations.Escaping => "Escaping",
                _ => "UNKNOWN",
            };
        }

        public void SetEnabled(bool newState)
        {
            // Routed through the change-detection helper so this write and `Update()`'s cannot desync
            // the cached display value (the app-bar toggle and the frame loop both drive this element).
            SetDisplay(container, newState ? DisplayStyle.Flex : DisplayStyle.None);

            // KEEP `SetValue`. Measured 2026-09-14 - `SetOn()`/`SetOff()` are declared on
            // KSP.UI.Binding.UIValue_WriteBool (monodis:36630/36631), and `UIValue_WriteBool_Toggle`
            // is a *sibling*, not a subclass (both extend the TypeSpec `UIValueBinder<bool>` = extends=0x7ea).
            // `SetValue(T)` is inherited from KSP.UI.Binding.Core.UIValueBinder`1 (monodis:37072).
            // In-repo precedent on this exact stack: K2D2Window.cs:72-76 (in-game validated).
            GameObject.Find(FlightPlanPlugin._ToolbarFlightButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(newState);

            //interfaceEnabled = newState;
            //GameObject.Find(_ToolbarFlightButtonID)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(interfaceEnabled);
        }

        /// <summary>
        /// UI-fix increment 5: the narrow entry point `FlightPlanPlugin` uses to take the window out of
        /// the game's escape-menu z-order. Launch 11 measured the defect: the window's panel settings
        /// carry a live `sortingOrder` that `BringToFrontOnPointerDown` re-raises through
        /// `UitkForKsp2.API.Order.OrderManager` (ONE monotonic counter shared with the game's own UI),
        /// so a clicked window floats above the escape menu's overlay and its save-before-exit dialog.
        /// The click-to-front behaviour itself is wanted (launch 7) and is NOT touched - this only
        /// removes the window from the render order for as long as the menu is up.
        ///
        /// Pure presentation: `display: none` via the change-detection helper. `GUIenabled`,
        /// `FlightPlanPlugin.InterfaceEnabled` and the AppBar toggle are never written here, so the
        /// logical open/closed state survives the suppression (which is why this is not SetEnabled -
        /// that would also write `UIValue_WriteBool_Toggle.SetValue` and desync the F2/AppBar state).
        /// </summary>
        public void SuppressForEscapeMenu(bool suppressed)
        {
            if (_escapeMenuSuppressed == suppressed)
            {
                return;
            }
            _escapeMenuSuppressed = suppressed;

            if (suppressed)
            {
                // Hide immediately on the message rather than waiting for Update()'s next gate pass. A
                // `display: none` window cannot be clicked, so no repaint burst is owed on the way down.
                SetDisplay(container, DisplayStyle.None);
                // Force the hidden -> visible edge: Update()'s `!_uiWasVisible` branch is what runs
                // InvalidateUiCaches() on the way back up.
                _uiWasVisible = false;
            }
            else
            {
                // Restore. Dropping the caches here (and again on the Update() edge) is the
                // increment-2 contract for a hidden -> visible transition: the tree must be restyled
                // from a blank slate, not skipped against the values cached before the suppression.
                _uiWasVisible = false;
                InvalidateUiCaches();
            }
        }

        /// <summary>
        /// Number of manoeuvre nodes on the active vessel's plan - the F59 gate for the K2-D2 button.
        /// The legacy read `NodeManagerPlugin.Instance.Nodes.Count` (a cached list on the third-party
        /// NodeManager plugin). NodeManager is absorbed (D9); this asks the same provider the absorbed
        /// <see cref="FlightPlanNodeService"/> reads through, so it is live rather than cached.
        /// </summary>
        private int ActiveNodeCount()
        {
            if (_activeVessel == null || Game == null || Game.SpaceSimulation == null)
            {
                return 0;
            }

            var nodes = Game.SpaceSimulation.Maneuvers.GetNodesForVessel(_activeVessel.GlobalId);
            return nodes == null ? 0 : nodes.Count;
        }

        public void SetupDocument()
        {
            // The root is FETCHED from the PanelRenderer Window.Create returned - it is not a
            // `rootVisualElement` read off a UIDocument (that shape is superseded and does not apply here).
            // GetWindowRoot resolves the window's content root out of the panel's template containers
            // (`UitkForKsp2.API.Extensions::GetWindowRoot` -> `Window::ResolveWindowRoot`), which is the
            // element the legacy addressed as `document.rootVisualElement[0]`; the tree operations below
            // are therefore applied to it directly instead of through a `[0]` index.
            container = UitkForKsp2.API.Extensions.GetWindowRoot(_panel);
            if (container == null)
            {
                FlightPlanPlugin.Logger.LogError("[FlightPlan] FpUiController.SetupDocument: the root " +
                                                 "VisualElement is null - the document has not built its tree, " +
                                                 "so the controller has nothing to bind to");
                return;
            }

            // Localization is a single call on this route: it adds (or reuses) the window's
            // DocumentLocalization and registers the renderer with it. The legacy's
            // `GetComponent<UIDocument>()` + `TryGetComponent<DocumentLocalization>` + conditional
            // `EnableLocalization()` dance is the superseded shape and is not ported.
            UitkForKsp2.API.Extensions.EnableLocalization(_panel);

            // The legacy `container.transform.position.z = 42;` is NOT ported: VisualElement.transform.position
            // is CSS `translate`, not the layout slot, and on 0.2.8.5 it fights the drag/clamp idiom (AGENTS §9).
            // Placement comes from the UXML/USS (`left`/`top` offsets); the controller only writes style.left/top.

            container.StopMouseEventsPropagation();
            container.transform.position = new Vector2(500, 50);
            container.CenterByDefault();
            SetDisplay(container, DisplayStyle.None);
            // FlightPlanPlugin.Logger.LogInfo($"_container {_container.}. nValue = {evt.newValue}. setting color to white");

            container.Query<TextField>().ForEach(textField =>
            {
                // textField.RegisterCallback<FocusInEvent>(_ => GameManager.Instance?.Game?.Input.Disable());
                // textField.RegisterCallback<FocusOutEvent>(_ => GameManager.Instance?.Game?.Input.Enable());

                textField.RegisterValueChangedCallback((evt) =>
                {
                    bool pass = false;
                    FlightPlanPlugin.Logger.LogDebug($"TryParse attempt for {textField.name}. Tooltip = {textField.tooltip}");
                    if (textField.tooltip != "Time in hh:mm:ss format")
                        pass = float.TryParse(evt.newValue, out _);
                    else
                        pass = MyTryParse(evt.newValue, out _);
                    if (pass)
                    {
                        textField.RemoveFromClassList("unity-text-field-invalid");
                        FlightPlanPlugin.Logger.LogDebug($"TryParse success for {textField.name}, nValue = '{evt.newValue}': Removed unity-text-field-invalid from class list");
                    }
                    else
                    {
                        textField.AddToClassList("unity-text-field-invalid");
                        FlightPlanPlugin.Logger.LogDebug($"TryParse failure for {textField.name}, nValue = '{evt.newValue}': Added unity-text-field-invalid to class list");
                        FlightPlanPlugin.Logger.LogDebug($"container.transform.position.z = {container.transform.position.z}");
                    }
                });

                textField.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                textField.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
                textField.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());
            });
        }

        private bool MyTryParse(string input, out double totalSeconds)
        {
            totalSeconds = 0;
            int years = 0;
            int days = 0;
            int hours = 0;
            int minutes = 0;
            double seconds = 0;
            bool pass;

            string[] timeParts = input.ToLower().Split(':');
            if (timeParts.Length > 4)
                return false;

            // Process years if present
            if (input.Contains('y'))
            {
                string[] partsY = timeParts[0].Split('y');
                if (partsY.Length > 2 || !int.TryParse(partsY[0], out years))
                    return false;
                totalSeconds += (double)years * 426.08 * 6.0 * 3600.0;
                timeParts[0] = partsY[1];
            }

            // Proces days if present
            if (input.Contains('d'))
            {
                string[] partsD = timeParts[0].Split('d');
                if (partsD.Length > 2 || !int.TryParse(partsD[0], out days))
                    return false;
                totalSeconds += (double)days * 6.0 * 3600.0;
                timeParts[0] = partsD[1];
            }

            // Process hours, minutes, seconds
            // FlightPlanPlugin.Logger.LogInfo($"MyTryParse: parts_t.Length = {parts_t.Length}, parts_t = '{test}'");
            int i = 0;
            foreach (string part in timeParts.Reverse())
            {
                switch (i)
                {
                    case 0: // handle seconds
                        pass = double.TryParse(part, out seconds);
                        if (pass)
                            if (timeParts.Length > 1 || years > 0 || days > 0) // we have more than just seconds
                                if (seconds < 60) // limit seconds to less than 60
                                    totalSeconds += seconds;
                                else
                                    return false;
                            else // all we have are seconds, so no limit
                                totalSeconds += seconds;
                        else
                            return false;
                        break;
                    case 1: // handle minutes
                        pass = int.TryParse(part, out minutes);
                        if (pass && minutes < 60 && minutes >= 0)
                            totalSeconds += (double)minutes * 60.0;
                        else
                            return false;
                        break;
                    case 2: // handle hours
                        pass = int.TryParse(part, out hours);
                        if (pass && hours < 6 && hours >= 0)
                            totalSeconds += (double)hours * 3600.0;
                        else
                            return false;
                        break;
                    default: return false;
                }
                i++;
            }
            if (years > 0)
                FlightPlanPlugin.Logger.LogDebug($"MyTryParse: newValue = '{input}', time = {years}y {days}d {hours}:{minutes}:{seconds} = {totalSeconds} seconds");
            else if (days > 0)
                FlightPlanPlugin.Logger.LogDebug($"MyTryParse: newValue = '{input}', time = {days}d {hours}:{minutes}:{seconds} = {totalSeconds} seconds");
            else
                FlightPlanPlugin.Logger.LogDebug($"MyTryParse: newValue = '{input}', time = {hours}:{minutes}:{seconds} = {totalSeconds} seconds");
            return true;
        }


        private void Update()
        {
            // CelestialBodyComponent ReferenceBody = null;

            if (FlightPlanPlugin.needToCleanUp)
                CleanUp();

            if (initialized)
            {
                ReferenceBody = null;
                Orbit = null;

                _activeVessel = Game?.ViewController?.GetActiveVehicle(true)?.GetSimVessel(true);
                _currentTarget = _activeVessel?.TargetObject;
                if (_activeVessel != null)
                {
                    SetText(VesselSituation, String.Format("{0} {1}", SituationToString(_activeVessel.Situation), _activeVessel.mainBody.bodyName));

                    Orbit = _activeVessel.Orbit;
                    if (Orbit != null)
                        ReferenceBody = Orbit.referenceBody;
                }
                else
                    return;
            }
            else
            {
                FlightPlanPlugin.Logger.LogInfo($"Calling InitializeElements from Update");
                InitializeElements();
                return;
            }

            // _GUIenabled = false;
            // REVERSE: `GlobalGameState` is a `KSP.Game.GameStateMachine` (there is no `GlobalGameState`
            // type) and it exposes GetState() -> KSP.Game.GameState *and* GetGameState() ->
            // GameStateConfiguration. The configuration's `.GameState` is the same value the legacy read.
            var _newGameState = Game?.GlobalGameState?.GetGameState()?.GameState;
            // var _newLastGameState = Game?.GlobalGameState?.GetLastState();
            bool _gameStateUpdated = _gameState != _newGameState;
            if (_newGameState == null)
                return;
            //if (_newLastGameState != _gameState)
            //  FlightPlanPlugin.Logger.LogInfo($"GameState Transitioned?: {_gameState} -> {_newGameState2}");
            if (_gameStateUpdated)
            {
                FlightPlanPlugin.Logger.LogDebug($"GameState Transitioned: {_gameState} -> {_newGameState}");
                _gameState = (GameState)_newGameState;
                // UI-fix increment 2: a game-state transition (map <-> flight, vessel switches) can
                // rebuild or re-style the visual tree outside this controller's writes. Drop the
                // change-detection caches so the fresh tree is restyled on the next helper call
                // instead of being skipped against stale last-written values (the launch-8
                // "unstyled buttons / need two AppBar clicks" regression).
                InvalidateUiCaches();
            }

            //if (_gameState == GameState.Map3DView) _GUIenabled = true;
            //if (_gameState == GameState.FlightView) _GUIenabled = true;

            // FIX 2c: the gate. `uiVisible` is the single explicit visibility flag for the whole
            // per-frame block below - when it is false the method returns here and performs no UI work
            // at all. The lower branch also re-arms the write-summary window, so the next open measures
            // a fresh ~5 s of visible time rather than reporting a hidden gap.
            // UI-fix increment 2: the false -> true edge is the "window just reopened" event -
            // invalidate so the fresh tree is fully restyled this frame (this covers all three
            // SetDisplay(container, ...) sites within one frame: the app-bar toggle routes through
            // SetEnabled, whose write is superseded here, and SetupDocument runs only once).
            // UI-fix increment 5: `_escapeMenuSuppressed` joins the gate. The plugin hides the container
            // on the ESC-menu-opened message, but this per-frame block is the authority on `display` and
            // would otherwise re-show the window on the next frame, straight back above the menu's
            // overlay. Suppression is presentation-only: `GUIenabled`, `InterfaceEnabled` and the AppBar
            // toggle keep whatever value the player set.
            bool uiVisible = GUIenabled && FlightPlanPlugin.InterfaceEnabled && !_escapeMenuSuppressed;
            if (uiVisible)
            {
                if (!_uiWasVisible)
                {
                    InvalidateUiCaches();
                }
                SetDisplay(container, DisplayStyle.Flex);
                _uiWasVisible = true;
            }
            else
            {
                SetDisplay(container, DisplayStyle.None);
                _uiWasVisible = false;
                _uiSummaryAt = 0f;
                _uiWritesPerformed = 0;
                _uiWritesSkipped = 0;
                return;
            }
            TickUiWriteSummary();

            // Control which tab buttons are visible
            if (DisplayTRMShipToShipTab())
            {
                SetDisplay(TRMShipToShipButtonBox, DisplayStyle.Flex);
            }
            else
            {
                SetDisplay(TRMShipToShipButtonBox, DisplayStyle.None);
                if (currentTabNum == Tab.TRMShipToShip)
                    SetTab(Tab.OSM);
            }

            if (DisplayTRMShipToCelestialTab())
            {
                SetDisplay(TRMShipToCelestialButtonBox, DisplayStyle.Flex);
            }
            else
            {
                SetDisplay(TRMShipToCelestialButtonBox, DisplayStyle.None);
                if (currentTabNum == Tab.TRMShipToCelestial)
                    SetTab(Tab.OSM);
            }

            if (DisplayOTMMoonTab())
            {
                SetDisplay(OTMMoonButtonBox, DisplayStyle.Flex);
            }
            else
            {
                SetDisplay(OTMMoonButtonBox, DisplayStyle.None);
                if (currentTabNum == Tab.OTMMoon)
                    SetTab(Tab.OSM);
            }

            if (DisplayOTMPlanetTab())
            {
                SetDisplay(OTMPlanetButtonBox, DisplayStyle.Flex);
            }
            else
            {
                SetDisplay(OTMPlanetButtonBox, DisplayStyle.None);
                if (currentTabNum == Tab.OTMPlanet)
                    SetTab(Tab.OSM);
            }

            //if (ReferenceBody.IsStar) // We're orbiting a star
            //    OTMMoonButtonBox.style.display = DisplayStyle.None;
            //else if (ReferenceBody.Orbit.referenceBody.IsStar) // We're orbiting a planet
            //    OTMMoonButtonBox.style.display = DisplayStyle.None;
            //else // We're orbiting a moon
            //    OTMMoonButtonBox.style.display = DisplayStyle.Flex;

            //if (_currentTarget == null)
            //{
            //    // Switch off target dependent tabs
            //    TRMShipToShipButtonBox.style.display = DisplayStyle.None;
            //    TRMShipToCelestialButtonBox.style.display = DisplayStyle.None;
            //    OTMPlanetButtonBox.style.display = DisplayStyle.None;
            //}
            //else // there is a target, but what type and where?
            //{
            //    if (_currentTarget.IsVessel || _currentTarget.IsPart) // target is vessel or part of vessel
            //    {
            //        TRMShipToCelestialButtonBox.style.display = DisplayStyle.None;
            //        OTMPlanetButtonBox.style.display = DisplayStyle.None;

            //        string referenceBodyName = _currentTarget.IsPart
            //        ? _currentTarget.Part.PartOwner.SimulationObject.Vessel.Orbit.referenceBody.Name
            //        : _currentTarget.Orbit.referenceBody.Name;

            //        if (referenceBodyName == ReferenceBody.Name)
            //            TRMShipToShipButtonBox.style.display = DisplayStyle.Flex;
            //        else
            //            TRMShipToShipButtonBox.style.display = DisplayStyle.None;
            //    }
            //    else
            //    {
            //        TRMShipToShipButtonBox.style.display = DisplayStyle.None;
            //    }

            //    if (_currentTarget.IsCelestialBody)
            //    {
            //        if (_currentTarget.Orbit == null) // Target is a star
            //        {
            //            TRMShipToCelestialButtonBox.style.display = DisplayStyle.None;
            //            OTMPlanetButtonBox.style.display = DisplayStyle.None;
            //        }
            //        else
            //        {
            //            bool atPlanet = true;
            //            bool targetIsPlanet = _currentTarget.Orbit.referenceBody.IsStar;
            //            if (ReferenceBody.IsStar)
            //                atPlanet = false;
            //            else if (!ReferenceBody.Orbit.referenceBody.IsStar)
            //                atPlanet = false;
            //            if (_currentTarget.Orbit.referenceBody.Name == ReferenceBody.Name)
            //            {
            //                TRMShipToCelestialButtonBox.style.display = DisplayStyle.Flex;
            //                OTMPlanetButtonBox.style.display = DisplayStyle.None;
            //            }
            //            else if (targetIsPlanet && atPlanet) // target is a planet
            //            {
            //                if (_currentTarget.Name == ReferenceBody.Name) // target is same planet we're orbiting
            //                    OTMPlanetButtonBox.style.display = DisplayStyle.None;
            //                else
            //                    OTMPlanetButtonBox.style.display = DisplayStyle.Flex;
            //            }
            //            if (!atPlanet) // active vessel is at a moon
            //            {
            //                OTMPlanetButtonBox.style.display = DisplayStyle.None;
            //            }
            //            if (ReferenceBody.IsStar)
            //            {
            //                OTMPlanetButtonBox.style.display = DisplayStyle.None;
            //            }
            //        }
            //    }
            //}

            // If the selected option is to do an activity "at an altitude", then present an input field for the altitude to use
            if (TimeRef == TimeReference.ALTITUDE)
            {
                // FpUiController.Altitude_km = DrawLabelWithTextField("Maneuver Altitude", FpUiController.Altitude_km, "km");
                // Enable the Maneuver Altitude input
                SetDisplay(AtAnAltitude, DisplayStyle.Flex);
                if (FpUiController.Altitude_km * 1000 < Orbit.PeriapsisArl)
                {
                    FpUiController.Altitude_km = Math.Ceiling(Orbit.PeriapsisArl) / 1000;
                    //if (GUI.GetNameOfFocusedControl() != "Maneuver Altitude")
                    //  UI_Fields.TempDict["Maneuver Altitude"] = FpUiController.Altitude_km.ToString();
                }
                if (Orbit.eccentricity < 1 && FpUiController.Altitude_km * 1000 > Orbit.ApoapsisArl)
                {
                    FpUiController.Altitude_km = Math.Floor(Orbit.ApoapsisArl) / 1000;
                    //if (GUI.GetNameOfFocusedControl() != "Maneuver Altitude")
                    //  UI_Fields.TempDict["Maneuver Altitude"] = FpUiController.Altitude_km.ToString();
                }
            }
            else
                SetDisplay(AtAnAltitude, DisplayStyle.None);

            if (TimeRef == TimeReference.X_FROM_NOW)
            {
                // FpUiController.TimeOffset = DrawLabelWithTextField("Time From Now", FpUiController.TimeOffset, "s");
                // Enable the Time From Now input
                SetDisplay(AfterFixedTime, DisplayStyle.Flex);
            }
            else
                SetDisplay(AfterFixedTime, DisplayStyle.None);

            // Draw the GUI Status at the end of this tab
            double _UT = Game.UniverseModel.UniverseTime;
            if (FlightPlanPlugin.Instance._currentNode == null && FPStatus.status != FPStatus.Status.VIRGIN && FPStatus.status != FPStatus.Status.ERROR)
            {
                FPStatus.Ok("");
            }

            // FIX 2 (refinement): the legacy composed this string on every frame. Both inputs move
            // only on discrete events - `ManeuverTypeDesc` on a manoeuvre-button press, `TimeRef`
            // when the Burn Option dropdown changes - so the interpolation runs only when one of
            // them moved. The composed value is also cached so an unchanged pair writes nothing.
            if (!ReferenceEquals(ManeuverTypeDesc, _lastManeuverTypeDesc) || TimeRef != _lastTimeRef)
            {
                _lastManeuverTypeDesc = ManeuverTypeDesc;
                _lastTimeRef = TimeRef;
                string composed = $"{ManeuverTypeDesc} {BurnTimeOption.TimeRefDesc}";
                if (!string.Equals(ManeuverDescription, composed, StringComparison.Ordinal))
                    ManeuverDescription = composed;
            }
            // Indicate Status of last GUI function
            float _transparency = 1;
            if (_UT > FPStatus.StatusTime) _transparency = (float)MuUtils.Clamp(1 - (_UT - FPStatus.StatusTime) / FPStatus.StatusFadeTime.Value, 0, 1);

            Color textColor = new Color(1, 1, 1, 1);

            if (FPStatus.status == FPStatus.Status.OK)
                textColor = new Color(0, 1, 0, _transparency);
            if (FPStatus.status == FPStatus.Status.WARNING)
                textColor = new Color(1, 1, 0, _transparency);
            if (FPStatus.status == FPStatus.Status.ERROR)
                textColor = new Color(1, 0, 0, _transparency);

            SetText(Status, FPStatus.StatusText);
            SetTextColor(Status, textColor);

            // TODO: Move this to someplace where it doesn't get called every frame
            // Display MNC (or not)
            // MNC (Maneuver Node Controller) is descoped - the third-party mod is not ported to
            // 0.2.8.5 (D7). The box stays in the UXML (layout is a KEEP) and is always hidden.
            SetDisplay(MNCButtonBox, DisplayStyle.None);

            // Display K2D2 (or not). F59: show the Execute button only when K2-D2 is loaded AND the active
            // vessel actually has a node to fly. The node count comes from the absorbed service (D9):
            // `NodeManagerPlugin.Instance.Nodes.Count` was a cached list on the legacy NodeManager plugin.
            if (ActiveNodeCount() > 0 && FPOtherModsInterface.k2d2Loaded)
            {
                SetDisplay(K2D2ButtonBox, DisplayStyle.Flex);
            }
            else
            {
                SetDisplay(K2D2ButtonBox, DisplayStyle.None);
            }

            // The live K2-D2 execution status line. It is driven independently of the F59 gate above:
            // once a run is armed the flight has to stay observable (and terminally classified) even
            // if the plan empties or the node is consumed, and the plugin's own update carries the
            // poll too so a closed window cannot stall it. `GetK2D2Status()` throttles itself to
            // ~5 Hz and returns immediately when no run is armed; the row is shown only while a run
            // is live or a terminal/refusal line is still inside its linger window, and it is never
            // given a fabricated string.
            // P7 CHECK (2026-09-14, this increment): the poll itself adds NO unconditional per-frame
            // UI write and NO per-frame allocation - `Poll()` returns on the first line unless a run
            // is armed, and `GetK2D2Status()` returns before touching the runtime until its own
            // 0.2 s timer expires. The two writes it does drive are routed through the same
            // change-detection as everything else, so the row costs nothing while it is static.
            FPOtherModsInterface.Poll();
            if (FPOtherModsInterface.StatusVisible)
            {
                SetText(K2D2Status, FPOtherModsInterface.StatusLine);
                SetDisplay(K2D2Status, DisplayStyle.Flex);
            }
            else
            {
                SetDisplay(K2D2Status, DisplayStyle.None);
            }

            // PatchedConicsOrbit Orbit = _activeVessel.Orbit;
            IKeplerOrbit targetOrbit = _currentTarget?.Orbit;

            double UT = Game.UniverseModel.UniverseTime;
            double nextWindow;
            double synodicPeriod;
            double timeToClosestApproach;
            double closestApproach;
            double relVelocityNow;
            double relVelocityCA;
            double relativeInc;
            double phase;
            double transfer;
            // double transferTime;

            // FIX 2 + FIX 3b: the choices lists are now rebuilt only when their inputs could have
            // changed - the target-type button's text, the active vessel, the reference body - plus a
            // bounded refresh (20 frames ~ 0.33 s) so a vessel launched mid-flight still appears. The
            // legacy rebuilt both dictionaries and reassigned `choices` on EVERY frame: `ListBodies()`
            // allocates one string per body, `ListVessels()` is a LINQ `Where().ToDictionary()`, and
            // the `choices` setter compares by reference, so it rebuilt the control's popup every frame.
            bool relistTargets = _targetRelistCountdown <= 0
                                 || !string.Equals(_lastTargetTypeText, TargetTypeButton.text, StringComparison.Ordinal)
                                 || !ReferenceEquals(_lastRelistVessel, _activeVessel)
                                 || !string.Equals(_lastRelistBodyName, ReferenceBody == null ? null : ReferenceBody.bodyName, StringComparison.Ordinal);
            if (relistTargets)
            {
                _targetRelistCountdown = TargetRelistIntervalFrames;
                _lastTargetTypeText = TargetTypeButton.text;
                _lastRelistVessel = _activeVessel;
                _lastRelistBodyName = ReferenceBody == null ? null : ReferenceBody.bodyName;

                if (TargetTypeButton.text == "Celestial")
                {
                    // setup the TargetSelectionDropdown for celestial body choices
                    ListBodies();
                    SetChoices(TargetSelectionDropdown, targetBodies);
                }
                else
                {
                    ListVessels();
                    if (SelectPortToggle.value)
                    {
                        // setup the TargetSelectionDropdown for celestial body choices
                        SetChoices(TargetSelectionDropdown, targetPorts);
                    }
                    else
                    {
                        // setup the TargetSelectionDropdown for celestial body choices
                        SetChoices(TargetSelectionDropdown, targetVessels);
                    }
                }
            }
            _targetRelistCountdown--;

            if (_currentTarget != null)
            {
                // FIX 2: this block used to rebuild a trimmed copy of the whole `choices` list (one
                // List<string> plus one string per entry) and reassign `.value` on EVERY frame. The
                // desired row text only changes when the target or the choice list changes, so it is
                // recomputed then, and the write itself is skipped while the row already shows it.
                if (relistTargets || !ReferenceEquals(_lastShownTarget, _currentTarget))
                {
                    _lastShownTarget = _currentTarget;
                    // The choices indent moons, so match with the Trim() the legacy list applied.
                    string choice = FindChoice(TargetSelectionDropdown.choices, _currentTarget.Name);
                    _lastShownTargetChoice = choice ?? ""; // legacy behaviour: blank the row when not a choice
                    _lastPreselectChoice = null;           // a real target owns the row now
                }
                if (!string.Equals(TargetSelectionDropdown.text, _lastShownTargetChoice, StringComparison.Ordinal))
                    TargetSelectionDropdown.value = _lastShownTargetChoice;
            }
            else if (TargetTypeButton.text == "Celestial")
            {
                _lastShownTarget = null;
                _lastShownTargetChoice = null;
                // FIX 3b - DISPLAY-ONLY preselect. With no target the row used to be blank even though
                // the choices above were populated. Selecting an entry SETS the vessel's real target
                // (`_activeVessel.SetTargetByID` in this dropdown's own ValueChanged callback), so the
                // orbited body is shown with `SetValueWithoutNotify` - the value is displayed, the
                // callback never fires, and the player's target is untouched. `ReferenceBody.bodyName`
                // is the body the active vessel orbits (assigned every frame above from
                // `Orbit.referenceBody`); the matching choice is located with the same `Trim()` the
                // target path above uses, because a moon's key carries the list's indent.
                string bodyName = ReferenceBody == null ? null : ReferenceBody.bodyName;
                // FIX 2 (refinement): the lookup runs only when its inputs changed - the choices
                // list object (replaced by any relist or target-type switch) or the body's name.
                // Otherwise the cached match from the previous frame is reused and the frame
                // performs no scan and no allocation at all.
                if (!ReferenceEquals(_lastChoicesListRef, TargetSelectionDropdown.choices)
                    || !string.Equals(_lastBodyNameSearched, bodyName, StringComparison.Ordinal))
                {
                    _lastChoicesListRef = TargetSelectionDropdown.choices;
                    _lastBodyNameSearched = bodyName;
                    _bodyChoice = FindChoice(TargetSelectionDropdown.choices, bodyName);
                }
                string bodyChoice = _bodyChoice;
                if (bodyChoice != null)
                {
                    if (!string.Equals(_lastPreselectChoice, bodyChoice, StringComparison.Ordinal))
                    {
                        // `BaseField.SetValueWithoutNotify` always assigns the value and marks the
                        // field dirty (no equality check of its own), so it must NOT run every frame -
                        // only when the row does not already show this body.
                        _lastPreselectChoice = bodyChoice;
                        TargetSelectionDropdown.SetValueWithoutNotify(bodyChoice);
                    }
                    if (!_targetPreselectLogged)
                    {
                        _targetPreselectLogged = true;
                        FlightPlanPlugin.Logger.LogInfo(
                            $"TargetSelectionDropdown: showing '{bodyChoice}' (the body the active vessel orbits) " +
                            "display-only via SetValueWithoutNotify - no target is set and the player's target is untouched");
                    }
                }
                else
                {
                    _lastPreselectChoice = null; // so a later successful preselect re-applies
                    // Only the first failure is logged: on the very first frames after the window is
                    // built `ReferenceBody` can still be unset, and a later frame that succeeds must
                    // still be able to prove itself in the log.
                    if (!_targetPreselectMissingLogged)
                    {
                        _targetPreselectMissingLogged = true;
                        FlightPlanPlugin.Logger.LogInfo(
                            "TargetSelectionDropdown: no body to preselect (ReferenceBody is null or not in the " +
                            $"celestial list) - the row stays blank until the player picks a target (bodyName = {bodyName ?? "null"})");
                    }
                }
            }
            else
            {
                _lastShownTarget = null;
                _lastShownTargetChoice = null;
                // FIX 2 (refinement): the legacy assigned `value = ""` on EVERY frame here. The
                // row is blanked once, then skipped - and `_lastPreselectChoice` is cleared so a
                // later return to the Celestial branch re-applies the preselect (its own guard
                // compares against this cache; leaving it stale would suppress the re-apply).
                _lastPreselectChoice = null;
                if (!string.IsNullOrEmpty(TargetSelectionDropdown.text))
                    TargetSelectionDropdown.value = "";
            }

            switch (currentTabNum)
            {
                case Tab.OSM: // Ownship Maneuvers Tab
                        // do stuff
                    break;

                case Tab.TRMShipToShip: // Target Relative Maneuvers - Ship to Ship
                    string recommendedManeuver;
                    const int maxPhasingOrbits = 5;
                    const double closestApproachLimit1 = 3000;
                    const double closestApproachLimit2 = 100;
                    const double maxDockingSpeed = 1;
                    const double maxApproachSpeed = 3;
                    double targetDistance;
                    if (_currentTarget != null)
                    {
                        if (_currentTarget.IsVessel)
                        {
                            targetDistance = (Orbit.WorldPositionAtUT(UT) - targetOrbit.WorldPositionAtUT(UT)).magnitude;
                            if (targetDistance < closestApproachLimit1)
                            {
                                SetDisplay(SelectPortToggle, DisplayStyle.Flex);
                            }
                            else
                                SetDisplay(SelectPortToggle, DisplayStyle.None);
                        }
                        else if (_currentTarget.IsPart)
                        {
                            SetDisplay(SelectPortToggle, DisplayStyle.Flex);
                            targetOrbit = _currentTarget.Part.PartOwner.SimulationObject.Orbit;
                            targetDistance = (Orbit.WorldPositionAtUT(UT) - targetOrbit.WorldPositionAtUT(UT)).magnitude;
                        }
                        else // celstial target
                        {
                            SetTab(0);
                            break;
                        }
                    }
                    else
                    {
                        SetTab(0);
                        break;
                    }

                    synodicPeriod = Orbit.SynodicPeriod(targetOrbit);
                    timeToClosestApproach = Orbit.NextClosestApproachTime(targetOrbit, UT + 1);
                    closestApproach = Orbit.RelativeDistance(targetOrbit, timeToClosestApproach);
                    relVelocityNow = Orbit.RelativeSpeed(targetOrbit, UT);
                    relVelocityCA = Orbit.RelativeSpeed(targetOrbit, timeToClosestApproach);
                    relVelocityNow = Vector3d.Dot(Orbit.WorldOrbitalVelocityAtUT(UT) - targetOrbit.WorldOrbitalVelocityAtUT(UT), Orbit.WorldBCIPositionAtUT(UT) - targetOrbit.WorldBCIPositionAtUT(UT)) / (Orbit.WorldBCIPositionAtUT(UT) - targetOrbit.WorldBCIPositionAtUT(UT)).magnitude;
                    relativeInc = Orbit.RelativeInclination(targetOrbit);
                    phase = Orbit.PhaseAngle(targetOrbit, UT);
                    transfer = Orbit.Transfer(targetOrbit, out _);
                    if (targetOrbit.semiMajorAxis > Orbit.semiMajorAxis)
                        nextWindow = synodicPeriod * MuUtils.ClampDegrees360(phase - transfer) / 360;
                    else
                        nextWindow = synodicPeriod * MuUtils.ClampDegrees360(transfer - phase) / 360;
                    // nextWindow = TransferCalc(targetOrbit, UT, out synodicPeriod, out timeToClosestApproach, out closestApproach, out relVelocityNow, out relVelocityCA, out relativeInc, out _, out _, out _);
                    SetText(TargetOrbitTRMS, $"{targetOrbit.PeriapsisArl / 1000:N0} km x {targetOrbit.ApoapsisArl / 1000:N0} km");
                    SetText(CurrentOrbitTRMS, $"{Orbit.PeriapsisArl / 1000:N0} km x {Orbit.ApoapsisArl / 1000:N0} km");
                    SetText(RelativeIncTRMS, $"{Orbit.RelativeInclination(targetOrbit):N2}°");
                    SetText(SynodicPeriodTRMS, $"{FPUtility.SecondsToTimeString(synodicPeriod, false, false, true)}");
                    SetText(NextWindowTRMS, $"{FPUtility.SecondsToTimeString(nextWindow, false, false, true)}");
                    SetText(NextClosestApproachTRMS, $"{FPUtility.SecondsToTimeString(timeToClosestApproach, false, false, true)}");
                    if (closestApproach > 1000)
                    {
                        SetText(SeparationAtCaTRMS, $"{closestApproach / 1000:N1} km");
                        // RelativeVelocityRowTRMS.style.display = DisplayStyle.None;
                        SetDisplay(RelativeVelocityRowTRMS, DisplayStyle.Flex);
                        SetText(RelativeVelocityTRMS, $"{relVelocityCA:N1} m/s");
                    }
                    else
                    {
                        SetText(SeparationAtCaTRMS, $"{closestApproach:N1} m");
                        SetDisplay(RelativeVelocityRowTRMS, DisplayStyle.Flex);
                        SetText(RelativeVelocityTRMS, $"{relVelocityNow:N1} m/s");
                    }

                    // Compose a recommened action based on the range to the target and relative velocity now and at closest approach
                    recommendedManeuver = "None";
                    if (targetDistance < closestApproachLimit2) // If we're very close
                    {
                        if (Math.Abs(relVelocityCA) < maxDockingSpeed) // We're stopped or nearly stopped relative to the target
                            recommendedManeuver = "Ready for docking";
                        else if (relVelocityCA > 0)
                            recommendedManeuver = "Moving away from target";
                        else
                            recommendedManeuver = "Moving toward target. Match Velocity at closest approach";
                    }
                    else if (targetDistance < closestApproachLimit1) // If we're close, but not very close
                    {
                        if (Math.Abs(relVelocityCA) < maxDockingSpeed) // We're stopped or nearly stopped relative to the target
                            recommendedManeuver = $"Need to get closer. HINT: Point at target, burn GENTLY toward target ({maxApproachSpeed} m/s or less), Match Velocity at closest approch. Rinse and repeat until distance < {closestApproachLimit2} m";
                        else if (relVelocityCA > 0)
                            recommendedManeuver = $"Moving away from target. Match Velocity after fixed time to stop and plan next maneuver";
                        else
                            recommendedManeuver = $"Moving toward target. Match Velocity at closest approach";
                    }
                    else if (relativeInc > 1) // We're not in a co-plannar orbit with the target
                        recommendedManeuver = "Need to Match planes for rendezvous";
                    else if (closestApproach < closestApproachLimit2) // We're on our way
                        recommendedManeuver = $"Need to Match Velocity at closest approch.";
                    else if (closestApproach < closestApproachLimit1) // We're on our way, but we're not going to arrive inside closestApproachLimit2
                        recommendedManeuver = $"Need to Match Velocity at closest approch, then close distance for docking.";
                    else if (closestApproach < 3 * closestApproachLimit1) // We're on our way, but we're not even going to arrive within closestApproachLimit1
                        recommendedManeuver = $"Closest apporach will be {closestApproach / 1000:N1} km. Recommend Course Correction for closer arrival.";
                    else if (nextWindow / Orbit.period > maxPhasingOrbits) // We're not yet on our way and not is a good orbit to start out from
                        recommendedManeuver = $"Next intercept window would be {nextWindow / Orbit.period:N1} orbits away, which is more than the maximum of {maxPhasingOrbits} phasing orbits. Increase phasing rate by establishing a new phasing orbit at {(targetOrbit.semiMajorAxis - ReferenceBody.radius) * 2:N0} km.";
                    else // We're not yet on our way, but we are in a good orbit to start from
                        recommendedManeuver = $"Need to perform Hohmann Transfer to target";

                    SetText(TRMStatus, recommendedManeuver);
                    break;

                case Tab.TRMShipToCelestial: // Target Relative Maneuvers - Ship to Celestial
                    if (_currentTarget == null)
                    {
                        SetTab(0);
                        break;
                    }
                    if (!_currentTarget.IsCelestialBody)
                    {
                        SetTab(0);
                        break;
                    }
                    synodicPeriod = Orbit.SynodicPeriod(targetOrbit);
                    timeToClosestApproach = Orbit.NextClosestApproachTime(targetOrbit, UT + 1);
                    closestApproach = Orbit.RelativeDistance(targetOrbit, timeToClosestApproach);
                    // double relVelocityNow = Orbit.RelativeSpeed(targetOrbit, UT);
                    // double relVelocityCA = Orbit.RelativeSpeed(targetOrbit, timeToClosestApproach);
                    relativeInc = Orbit.RelativeInclination(targetOrbit);
                    phase = Orbit.PhaseAngle(targetOrbit, UT);
                    transfer = Orbit.Transfer(targetOrbit, out double _transferTime);
                    if (targetOrbit.semiMajorAxis > Orbit.semiMajorAxis)
                        nextWindow = synodicPeriod * MuUtils.ClampDegrees360(phase - transfer) / 360;
                    else
                        nextWindow = synodicPeriod * MuUtils.ClampDegrees360(transfer - phase) / 360;
                    // nextWindow = TransferCalc(targetOrbit, UT, out synodicPeriod, out timeToClosestApproach, out _, out _, out _, out relativeInc, out phase, out transfer, out transferTime);
                    SetText(TargetOrbitTRMC, $"{FPUtility.MetersToScaledDistanceString(targetOrbit.PeriapsisArl)} x {FPUtility.MetersToScaledDistanceString(targetOrbit.ApoapsisArl)}");
                    SetText(CurrentOrbitTRMC, $"{FPUtility.MetersToScaledDistanceString(Orbit.PeriapsisArl)} x {FPUtility.MetersToScaledDistanceString(Orbit.ApoapsisArl)}");
                    SetText(RelativeIncTRMC, $"{relativeInc:N2}°");
                    SetText(PhaseAngleTRMC, $"{phase:N3}°"); // _currentTarget.Name
                    SetText(XferPhaseAngleTRMC, $"{transfer:N3}°");
                    SetText(XferTimeTRMC, FPUtility.SecondsToTimeString(_transferTime, false, false, true));
                    SetText(SynodicPeriodTRMC, FPUtility.SecondsToTimeString(synodicPeriod, false, false, true));
                    SetText(NextWindowTRMC, FPUtility.SecondsToTimeString(nextWindow, false, false, true));
                    SetText(NextClosestApproachTRMC, FPUtility.SecondsToTimeString(timeToClosestApproach, false, false, true));
                    break;
                case Tab.OTMMoon: // Orbital Transfer Maneuvers - Moon
                        // do stuff

                    break;
                case Tab.OTMPlanet: // Orbital Transfer Maneuvers - Planet
                    if (_currentTarget == null)
                    {
                        SetTab(0);
                        break;
                    }
                    if (!_currentTarget.IsCelestialBody)
                    {
                        SetTab(0);
                        break;
                    }

                    // LIMITED_TIME / PORKCHOP used to drive NodeManager's `OperationAdvancedTransfer`
                    // (`op.DoParametersGUI` -> the plot inside PorkchopDisplay). NodeManager is absorbed
                    // (D9) and `OperationAdvancedTransfer` does not exist on 0.2.8.5, so this branch is a
                    // labelled drop (D10): the group stays in the layout, unpainted. Logged once, not per frame.
                    if ((TimeRef == TimeReference.LIMITED_TIME || TimeRef == TimeReference.PORKCHOP) && !_porkchopLogged)
                    {
                        _porkchopLogged = true;
                        LogPorkchopUnavailable("OTM-Planet Update");
                    }

                    synodicPeriod = ReferenceBody.Orbit.SynodicPeriod(targetOrbit);
                    relativeInc = Orbit.RelativeInclination(targetOrbit);
                    phase = ReferenceBody.Orbit.PhaseAngle(targetOrbit, UT);
                    // double phase2 = Phase();
                    transfer = ReferenceBody.Orbit.Transfer(targetOrbit, out _transferTime);
                    // double transfer2 = Transfer(out _);
                    if (targetOrbit.semiMajorAxis > ReferenceBody.Orbit.semiMajorAxis)
                        nextWindow = synodicPeriod * MuUtils.ClampDegrees360(phase - transfer) / 360;
                    else
                        nextWindow = synodicPeriod * MuUtils.ClampDegrees360(transfer - phase) / 360;
                    // nextWindow = TransferCalc(targetOrbit, UT, out synodicPeriod, out _, out _, out _, out _, out relativeInc, out phase, out transfer, out transferTime);

                    // Display Transfer Info
                    SetText(RelativeIncOTM, $"{relativeInc:N2}°");
                    SetText(PhaseAngleOTM, $"{phase:N3}°"); // _currentTarget.Name
                    SetText(XferPhaseAngleOTM, $"{transfer:N3}°");
                    SetText(XferTimeOTM, FPUtility.SecondsToTimeString(_transferTime, false, false, true));
                    SetText(SynodicPeriodOTM, FPUtility.SecondsToTimeString(synodicPeriod, false, false, true));
                    SetText(NextWindowOTM, FPUtility.SecondsToTimeString(nextWindow, false, false, true));
                    SetText(EjectionDvOTM, $"{DeltaV():N1} m/s");
                    break;
                case Tab.ROM: // Resonant Orbit Maneuvers
                        // do stuff
                    double _synchronousPeriod = _activeVessel.mainBody.rotationPeriod;
                    double _semiSynchronousPeriod = _activeVessel.mainBody.rotationPeriod / 2;
                    _synchronousAlt = SMACalc(_synchronousPeriod);
                    _semiSynchronousAlt = SMACalc(_semiSynchronousPeriod);
                    int _n, _m;

                    // Determine if synchronous or semi-synchronous orbits are possible for this body
                    if (_synchronousAlt > _activeVessel.mainBody.sphereOfInfluence)
                    {
                        _synchronousAlt = -1;
                    }
                    if (_semiSynchronousAlt > _activeVessel.mainBody.sphereOfInfluence)
                    {
                        _semiSynchronousAlt = -1;
                    }

                    // Set the _resonance factors based on diving or not
                    _m = NumSats * NumOrb;
                    if (Dive.value) // If we're going to dive under the target orbit for the deployment orbit
                        _n = _m - 1;
                    else // If not
                        _n = _m + 1;
                    double _resonance = (double)_n / _m;
                    string _resonanceStr = String.Format("{0}/{1}", _n, _m);

                    // Compute the minimum LOS altitude
                    _minLOSAlt = MinLOSCalc(NumSats, _activeVessel.mainBody.radius, _activeVessel.mainBody.hasAtmosphere);

                    SetText(OrbitalResonance, _resonanceStr);

                    SetText(CurrentApoapsis, (_activeVessel.Orbit.ApoapsisArl / 1000).ToString("N0"));
                    SetText(CurrentPeriapsis, (_activeVessel.Orbit.PeriapsisArl / 1000).ToString("N0"));

                    if (_synchronousAlt > 0)
                    {
                        SetText(SynchronousAlt, $"{FPUtility.MetersToDistanceString((_synchronousAlt - _activeVessel.mainBody.radius) / 1000):N0}");
                        SetText(SemiSynchronousAlt, $"{FPUtility.MetersToDistanceString((_semiSynchronousAlt - _activeVessel.mainBody.radius) / 1000):N0}");
                    }
                    else if (_semiSynchronousAlt > 0)
                    {
                        SetText(SynchronousAlt, "Outside SOI");
                        SetText(SemiSynchronousAlt, $"{FPUtility.MetersToDistanceString((_semiSynchronousAlt - _activeVessel.mainBody.radius) / 1000):N0}");
                    }
                    else
                    {
                        SetText(SynchronousAlt, "Outside SOI");
                        SetText(SemiSynchronousAlt, "Outside SOI");
                    }
                    SetText(SOIAlt, $"{FPUtility.MetersToDistanceString((_activeVessel.mainBody.sphereOfInfluence - _activeVessel.mainBody.radius) / 1000)}");
                    if (_minLOSAlt > 0)
                    {
                        SetText(MinLOSAlt, $"{FPUtility.MetersToDistanceString(_minLOSAlt / 1000):N0}");
                    }
                    else
                    {
                        SetText(MinLOSAlt, "Undefined");
                    }

                    // bool pass = double.TryParse(TargetAltitudeInput.text, out _target_alt_km);
                    _satPeriod = PeriodCalc(_target_alt_km * 1000 + _activeVessel.mainBody.radius);
                    _xferPeriod = _resonance * _satPeriod;
                    double _SMA2 = SMACalc(_xferPeriod);
                    double _sSMA = _target_alt_km * 1000 + _activeVessel.mainBody.radius;
                    double _divePe = 2.0 * _SMA2 - _sSMA;
                    if (_divePe < _activeVessel.mainBody.radius + _activeVessel.mainBody.atmosphereDepth) // No diving in the shallow end of the pool!
                    {
                        // FpUiController.DiveOrbit = false;
                        Dive.value = false;
                    }
                    // else
                    //   FpUiController.DiveOrbit = MainUI.DrawSoloToggle("<b>Dive</b>", FpUiController.DiveOrbit);

                    if (Dive.value)
                    {
                        Ap2 = _sSMA; // Diveing transfer orbits release at Apoapsis
                        Pe2 = _divePe;
                    }
                    else
                    {
                        Pe2 = _sSMA; // Non-diving transfer orbits release at Periapsis
                        Ap2 = 2.0 * _SMA2 - (Pe2);
                    }
                    double _ce = (Ap2 - Pe2) / (Ap2 + Pe2);

                    SetText(ResonantOrbitPeriod, $"{FPUtility.SecondsToTimeString(_xferPeriod, false, false, true)}");
                    SetText(ResonantOrbitAp, $"{FPUtility.MetersToDistanceString((Ap2 - _activeVessel.mainBody.radius) / 1000)}");
                    SetText(ResonantOrbitPe, $"{FPUtility.MetersToDistanceString((Pe2 - _activeVessel.mainBody.radius) / 1000)}");
                    SetText(ResonantOrbitEcc, _ce.ToString("N3"));
                    double dV = BurnCalc(_sSMA, _sSMA, 0, Ap2, _SMA2, _ce, _activeVessel.mainBody.gravParameter);
                    SetText(ResonantOrbitInjection, dV.ToString("N3"));

                    double _errorPe = (Pe2 - _activeVessel.Orbit.Periapsis) / 1000;
                    double _errorAp = (Ap2 - _activeVessel.Orbit.Apoapsis) / 1000;
                    string _fixPeStr, _fixApStr;

                    if (_errorPe > 0)
                        _fixPeStr = $"Raise to {((Pe2 - _activeVessel.mainBody.radius) / 1000):N2} km";
                    else
                        _fixPeStr = $"Lower to {((Pe2 - _activeVessel.mainBody.radius) / 1000):N2} km";
                    if (_errorAp > 0)
                        _fixApStr = $"Raise to {((Ap2 - _activeVessel.mainBody.radius) / 1000):N2} km";
                    else
                        _fixApStr = $"Lower to {((Ap2 - _activeVessel.mainBody.radius) / 1000):N2} km";
                    if (_activeVessel.Orbit.Apoapsis < Pe2)
                    {
                        SetDisplay(FixApGroup, DisplayStyle.Flex);
                        SetText(FixApStatus, _fixApStr);
                        SetDisplay(FixPeGroup, DisplayStyle.None);

                    }
                    else if (_activeVessel.Orbit.Periapsis > Ap2)
                    {
                        SetDisplay(FixPeGroup, DisplayStyle.Flex);
                        SetText(FixPeStatus, _fixPeStr);
                        SetDisplay(FixApGroup, DisplayStyle.None);
                    }
                    else
                    {
                        if (Pe2 > _activeVessel.mainBody.radius)
                        {
                            SetDisplay(FixPeGroup, DisplayStyle.Flex);
                            SetText(FixPeStatus, _fixPeStr);
                        }
                        SetDisplay(FixApGroup, DisplayStyle.Flex);
                        SetText(FixApStatus, _fixApStr);
                    }

                    break;
                default: break;
            }
        }

        private double TransferCalc(IKeplerOrbit targetOrbit, double UT, out double synodicPeriod, out double timeToClosestApproach,
            out double closestApproach, out double relVelocityNow, out double relVelocityCA, out double relativeInc, out double phase,
            out double transfer, out double _transferTime)
        {
            double nextWindow;
            synodicPeriod = Orbit.SynodicPeriod(targetOrbit);
            timeToClosestApproach = Orbit.NextClosestApproachTime(targetOrbit, UT + 1);
            closestApproach = Orbit.RelativeDistance(targetOrbit, timeToClosestApproach);
            // relVelocityNow = Orbit.RelativeSpeed(targetOrbit, UT);
            relVelocityCA = Orbit.RelativeSpeed(targetOrbit, timeToClosestApproach);
            relVelocityNow = Vector3d.Dot(Orbit.WorldOrbitalVelocityAtUT(UT) - targetOrbit.WorldOrbitalVelocityAtUT(UT), Orbit.WorldBCIPositionAtUT(UT) - targetOrbit.WorldBCIPositionAtUT(UT)) / (Orbit.WorldBCIPositionAtUT(UT) - targetOrbit.WorldBCIPositionAtUT(UT)).magnitude;
            relativeInc = Orbit.RelativeInclination(targetOrbit);
            phase = Orbit.PhaseAngle(targetOrbit, UT);
            transfer = Orbit.Transfer(targetOrbit, out _transferTime);
            if (transfer < phase)
                nextWindow = synodicPeriod * MuUtils.ClampDegrees360(phase - transfer) / 360;
            else
                nextWindow = synodicPeriod * MuUtils.ClampDegrees360(transfer - phase) / 360;
            return nextWindow;
        }

        private bool DisplayTRMShipToShipTab()
        {
            if (FlightPlanPlugin.Instance._currentTarget == null) return false;
            if (!FlightPlanPlugin.Instance._currentTarget.IsVessel && !FlightPlanPlugin.Instance._currentTarget.IsPart) return false;
            string referenceBodyName = FlightPlanPlugin.Instance._currentTarget.IsPart
                ? FlightPlanPlugin.Instance._currentTarget.Part.PartOwner.SimulationObject.Vessel.Orbit.referenceBody.Name
                : FlightPlanPlugin.Instance._currentTarget.Orbit.referenceBody.Name;
            return referenceBodyName == ReferenceBody.Name;
        }

        private bool DisplayTRMShipToCelestialTab()
        {
            if (FlightPlanPlugin.Instance._currentTarget == null) return false;  // If there is no target, then no tab
            if (!FlightPlanPlugin.Instance._currentTarget.IsCelestialBody) return false; // If the target is not a Celestial, then no tab
            if (FlightPlanPlugin.Instance._currentTarget.Orbit == null) return false; // And the target is a star, then no tab
            return FlightPlanPlugin.Instance._currentTarget.Orbit.referenceBody.Name == ReferenceBody.Name; // If the ActiveVessel and the _currentTarget are both orbiting the same body
        }

        private bool DisplayOTMMoonTab()
        {
            if (ReferenceBody.IsStar) return false; // if were orbiting a star, then no tab
            if (ReferenceBody.Orbit.referenceBody.IsStar) return false; // If we're orbiting a planet, then no tab
            return Orbit.eccentricity < 1;
        }

        private bool DisplayOTMPlanetTab()
        {
            if (FlightPlanPlugin.Instance._currentTarget == null) return false; // If there is no target then no tab
            if (ReferenceBody.IsStar) return false; // If we're orbiting a star then no tab
            if (!FlightPlanPlugin.Instance._currentTarget.IsCelestialBody) return false; // the current target is not a celestial object then no tab
            if (!ReferenceBody.Orbit.referenceBody.IsStar) return false; // If we're not at a planet then no tab
            if (FlightPlanPlugin.Instance._currentTarget.Orbit == null) return false; // if current target is a star then no tab
            if (!FlightPlanPlugin.Instance._currentTarget.Orbit.referenceBody.IsStar) return false; // If our target is not a planet then no tab
            return FlightPlanPlugin.Instance._currentTarget.Name != ReferenceBody.Name;// if we're targeting the same planet we're orbiting then no tab
        }

        private double DeltaV()
        {
            // GameInstance game = GameManager.Instance.Game;
            // SimulationObjectModel target = Plugin._currentTarget; // game.ViewController.GetActiveVehicle(true)?.GetSimVessel().TargetObject;
            CelestialBodyComponent cur = _activeVessel.Orbit.referenceBody; // game.ViewController.GetActiveVehicle(true)?.GetSimVessel().orbit.referenceBody;

            // This deals with if we're at a moon and backing thing off so that cur would be the planet about which this moon is orbitting
            while (cur.Orbit.referenceBody.Name != _currentTarget.Orbit.referenceBody.Name)
            {
                cur = cur.Orbit.referenceBody;
            }

            IKeplerOrbit targetOrbit = _currentTarget.Orbit;
            IKeplerOrbit currentOrbit = cur.Orbit;

            double sunEject;
            double ellipseA = (targetOrbit.semiMajorAxis + currentOrbit.semiMajorAxis) / 2;
            CelestialBodyComponent star = targetOrbit.referenceBody;

            sunEject = Mathf.Sqrt((float)(star.gravParameter) / (float)currentOrbit.semiMajorAxis) * (Mathf.Sqrt((float)targetOrbit.semiMajorAxis / (float)ellipseA) - 1);

            VesselComponent ship = _activeVessel; // game.ViewController.GetActiveVehicle(true)?.GetSimVessel(true);
            double eject = Mathf.Sqrt((2 * (float)(cur.gravParameter) * ((1 / (float)ship.Orbit.radius) - (float)(1 / cur.sphereOfInfluence))) + (float)(sunEject * sunEject));
            // `orbitalSpeed` is a concrete-only member (rank 1: the field lives on
            // Redux.Ecs.Components.PatchedConicsOrbitData and PatchedConicsOrbit exposes it as a
            // property; no orbit interface carries it), so it is computed from interface members -
            // and it is EXACTLY the same quantity, by the runtime's own definition: the same
            // PatchedConicsOrbitData update sets `orbitalSpeed = |velocity|` at the current radius
            // and then `OrbitalEnergy = orbitalSpeed^2/2 - mu/radius`, i.e. the vis-viva speed at
            // `radius` - which is what GetOrbitalSpeedAtDistance(r) returns:
            // sqrt(mu * (2/r - 1/semiMajorAxis)) (monodis --method Assembly-CSharp.dll:
            // PatchedConicsOrbitData::GetOrbitalSpeedAtDistance, IL_0000-IL_0032). Same conic, same
            // mu, same r, so no behavioural change.
            eject -= ship.Orbit.GetOrbitalSpeedAtDistance(ship.Orbit.radius);

            return Math.Round(eject, 1);
        }

        private double OccModCalc(bool hasAtmo) // Specific to Resonant Orbits
        {
            double _occMod;
            if (Occlusion.value)
            {
                if (hasAtmo)
                {
                    _occMod = OccModAtm;
                    // double.TryParse(AtmOccInput.value, out _occMod);
                }
                else
                {
                    _occMod = OccModVac;
                    // double.TryParse(VacOccInput.value, out _occMod);
                }
            }
            else
            {
                _occMod = 1;
            }
            return _occMod;
        }

        private double MinLOSCalc(int numSat, double radius, bool hasAtmo) // Specific to Resonant Orbits
        {
            if (numSat > 2)
            {
                return (radius * OccModCalc(hasAtmo)) / (Math.Cos(0.5 * (2.0 * Math.PI / numSat))) - radius;
            }
            else
            {
                return -1;
            }
        }

        public double SMACalc(double period) // General Purpose: Compute SMA given orbital period - RELOCATE TO ?
        {
            double _SMA;
            _SMA = Math.Pow((period * Math.Sqrt(_activeVessel.mainBody.gravParameter) / (2.0 * Math.PI)), (2.0 / 3.0));
            return _SMA;
        }

        public double PeriodCalc(double SMA) // General Purpose: Compute orbital period given SMA - RELOCATE TO ?
        {
            double _period;
            _period = (2.0 * Math.PI * Math.Pow(SMA, 1.5)) / Math.Sqrt(_activeVessel.mainBody.gravParameter);
            return _period;
        }

        private double BurnCalc(double sAp, double sSMA, double se, double cAp, double cSMA, double ce, double bGM)
        {
            double sta = 0;
            double cta = 0;
            if (cAp == sAp) cta = 180;
            double sr = sSMA * (1 - Math.Pow(se, 2)) / (1 + (se * Math.Cos(sta)));
            double sdv = Math.Sqrt(bGM * ((2 / sr) - (1 / sSMA)));

            double cr = cSMA * (1 - Math.Pow(ce, 2)) / (1 + (ce * Math.Cos(cta)));
            double cdv = Math.Sqrt(bGM * ((2 / sr) - (1 / cSMA)));

            return Math.Round(100 * Math.Abs(sdv - cdv)) / 100;
        }

        public void InitializeElements()
        {
            FlightPlanPlugin.Logger.LogInfo($"FP: Starting UITK GUI Initialization. initialized is set to {initialized}");

            int testLog = 0;

            if (OtherMods == null)
            {
                // init mod detection only when first needed
                OtherMods = new FPOtherModsInterface();
                OtherMods.CheckModsVersions();
            }

            // game = GameManager.Instance.Game;
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}");

            // Set up variables to be able to access UITK GUI panel groups quickly (Queries are expensive)

            // Close Button
            CloseButton = container.Q<Button>("CloseButton");
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}");

            // Vessel Situation
            VesselSituation = container.Q<Label>("VesselSituation");
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}");

            // TargetSelection
            TargetTypeButton = container.Q<Button>("TargetTypeButton");
            TargetSelectionDropdown = container.Q<DropdownField>("TargetSelectionDropdown");
            TargetTypeButton.clicked += TargetType;
            TargetSelectionDropdown.RegisterValueChangedCallback(evt =>
            {

                if (TargetTypeButton.text == "Celestial")
                {
                    FlightPlanPlugin.Logger.LogDebug($"RegisterValueChangedCallback Selected Body: '{evt.newValue}'");
                    if (targetBodies.Keys.Contains(evt.newValue))
                    {
                        FlightPlanPlugin.Logger.LogDebug($"RegisterValueChangedCallback Selected Body GlobalId: {targetBodies[evt.newValue].GlobalId}");
                        _activeVessel.SetTargetByID(targetBodies[evt.newValue].GlobalId);
                        _currentTarget = _activeVessel.TargetObject;
    #if DEBUG
                        IKeplerPatch o = (IKeplerPatch)_currentTarget.Orbit;
                        double _synchronousPeriod = _currentTarget.CelestialBody.rotationPeriod;
                        double _semiSynchronousPeriod = _synchronousPeriod / 2;
                        double _synchronousAlt = SMACalc(_synchronousPeriod);
                        double _semiSynchronousAlt = SMACalc(_semiSynchronousPeriod);
                        FlightPlanPlugin.Logger.LogInfo($"{_currentTarget.Name}:");
                        FlightPlanPlugin.Logger.LogInfo($"SMA:                    {o.semiMajorAxis} m");
                        FlightPlanPlugin.Logger.LogInfo($"Apoapsis:               {o.Apoapsis} m");
                        FlightPlanPlugin.Logger.LogInfo($"Apoapsis ARL:           {o.ApoapsisArl} m");
                        FlightPlanPlugin.Logger.LogInfo($"Periapsis:              {o.Periapsis} m");
                        FlightPlanPlugin.Logger.LogInfo($"Periapsis ARL:          {o.PeriapsisArl} m");
                        FlightPlanPlugin.Logger.LogInfo($"eccentricity:           {o.eccentricity}");
                        FlightPlanPlugin.Logger.LogInfo($"inclination:            {o.inclination}°");
                        FlightPlanPlugin.Logger.LogInfo($"Argument of Periapsis:  {o.argumentOfPeriapsis}°");
                        FlightPlanPlugin.Logger.LogInfo($"LAN:                    {o.longitudeOfAscendingNode}°");
                        FlightPlanPlugin.Logger.LogInfo($"Period:                 {o.period} s = {FPUtility.SecondsToTimeString(o.period)}");
                        FlightPlanPlugin.Logger.LogInfo($"MeanAnomaly:            {o.MeanAnomaly}°");
                        FlightPlanPlugin.Logger.LogInfo($"Mean Orbital Velocity:  {2*Math.PI*o.semiMajorAxis/o.period} m/s");
                        FlightPlanPlugin.Logger.LogInfo($"Apoapsis Velocity:      {o.WorldOrbitalVelocityAtUT(o.TimeToAp).magnitude} m/s");
                        FlightPlanPlugin.Logger.LogInfo($"Periapsis Velocity:     {o.WorldOrbitalVelocityAtUT(o.TimeToPe).magnitude} m/s");
                        FlightPlanPlugin.Logger.LogInfo($"Sphere of Influence:    {_currentTarget.CelestialBody.sphereOfInfluence} m");
                        if (_synchronousAlt < _currentTarget.CelestialBody.sphereOfInfluence)
                            FlightPlanPlugin.Logger.LogInfo($"Synchronous Orbit:      {_synchronousAlt} m");
                        else
                            FlightPlanPlugin.Logger.LogInfo($"Synchronous Orbit:      {_synchronousAlt} m (Outside of SOI)");
                        if (_synchronousAlt < _currentTarget.CelestialBody.sphereOfInfluence)
                            FlightPlanPlugin.Logger.LogInfo($"Semi-Synchronous Orbit: {_semiSynchronousAlt} m");
                        else
                            FlightPlanPlugin.Logger.LogInfo($"Semi-Synchronous Orbit: {_semiSynchronousAlt} m (Outside of SOI)");
    #endif
                    }
                }
                else if (TargetTypeButton.text == "Vessel")
                {
                    FlightPlanPlugin.Logger.LogDebug($"RegisterValueChangedCallback Selected Vessel: '{evt.newValue}'");
                    if (targetVessels.Keys.Contains(evt.newValue))
                    {
                        FlightPlanPlugin.Logger.LogDebug($"RegisterValueChangedCallback Selected Vessle GlobalId: {targetVessels[evt.newValue].GlobalId}");
                        _activeVessel.SetTargetByID(targetVessels[evt.newValue].GlobalId);
                        _currentTarget = _activeVessel.TargetObject;
                    }
                }
                else if (TargetTypeButton.text == "Port")
                {
                    FlightPlanPlugin.Logger.LogDebug($"RegisterValueChangedCallback Selected Port: '{evt.newValue}'");
                    if (targetPorts.Keys.Contains(evt.newValue))
                    {
                        FlightPlanPlugin.Logger.LogDebug($"RegisterValueChangedCallback Selected Port GlobalId: {targetPorts[evt.newValue].GlobalId}");
                        _activeVessel.SetTargetByID(targetPorts[evt.newValue].GlobalId);
                        _currentTarget = _activeVessel.TargetObject;
                    }
                }

                if (_currentTarget != null)
                {
                    FlightPlanPlugin.Logger.LogInfo($"RegisterValueChangedCallback Selected Target: {_currentTarget.Name}");
                }

            });
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}");

            buttonBackgroundColor = TargetTypeButton.style.backgroundColor;
            buttonTextColor = TargetTypeButton.style.color;
            buttonBorderColor = TargetTypeButton.style.borderTopColor;

            // TabBar button boxes (used to control visibility and placement of TabBar buttons)
            // OSMButtonBox = _container.Q<VisualElement>("OSMButtonBox");
            TRMShipToShipButtonBox = container.Q<VisualElement>("TRMShipToShipButtonBox");
            TRMShipToCelestialButtonBox = container.Q<VisualElement>("TRMShipToCelestialButtonBox");
            OTMMoonButtonBox = container.Q<VisualElement>("OTMMoonButtonBox");
            OTMPlanetButtonBox = container.Q<VisualElement>("OTMPlanetButtonBox");
            // ROMButtonBox = _container.Q<VisualElement>("ROMButtonBox");
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}");

            // TabBar buttons (control which panel is displayed)
            OSMButton = container.Q<Button>("OSMButton");
            TRMShipToShipButton = container.Q<Button>("TRMShipToShipButton");
            TRMShipToCelestialButton = container.Q<Button>("TRMShipToCelestialButton");
            OTMMoonButton = container.Q<Button>("OTMMoonButton");
            OTMPlanetButton = container.Q<Button>("OTMPlanetButton");
            ROMButton = container.Q<Button>("ROMButton");

            // FlightPlanPlugin.Logger.LogInfo($"FP: Top panel initialized. initialized is set to {initialized}");

            // Panel Label (set this whenever activating a panel to identify the panel)
            PanelLabel = container.Q<Label>("PanelLabel");
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: PanelLabel");

            // BurnTimeOptions dropdown
            BurnOptionsDropdown = container.Q<DropdownField>("BurnOptionsDropdown");
            BurnOptionsDropdown.RegisterValueChangedCallback(evt =>
            {
                _burnTimeOption = evt.newValue;
                TimeRef = BurnTimeOption.ValTimeRef[evt.newValue];

                //if (TimeRef == TimeRef.LIMITED_TIME)
                //{
                //  op.DoParametersGUI(FlightPlanPlugin.Instance._activeVessel.Orbit, Game.UniverseModel.UniverseTime, FlightPlanPlugin.Instance._currentTarget.CelestialBody, OperationAdvancedTransfer.Mode.LimitedTime);
                //}
                //if (TimeRef == TimeRef.PORKCHOP)
                //{
                //  op.DoParametersGUI(FlightPlanPlugin.Instance._activeVessel.Orbit, Game.UniverseModel.UniverseTime, FlightPlanPlugin.Instance._currentTarget.CelestialBody, OperationAdvancedTransfer.Mode.Porkchop);
                //}

                FlightPlanPlugin.Logger.LogInfo($"Burn Time Option: {_burnTimeOption}");
            });
            // FIX 3a: seed the Burn Option dropdown with a REAL entry. The legacy assigned a single
            // empty string here, so a freshly opened window showed one blank row - the control only
            // became meaningful after a manoeuvre button ran `SetOptionsList` (which is why the
            // launch-7 screenshot, taken after `New Pe & Ap`, showed "at next apoapsis" while the
            // fresh state did not). `TimeRef` is `None` (the virgin state) until that happens and
            // `TextTimeRef[None]` is the empty string, so the seed falls back to the reference the
            // first `SetOptionsList` call uses for `New Pe & Ap` - APOAPSIS, "at next apoapsis".
            // Assigning `.value` after `.choices` fires this dropdown's own callback once, which is
            // what keeps `TimeRef` and the visible entry in agreement. `SetOptionsList` still
            // overrides both `choices` and `.value` once a maneuver is configured - that path is
            // unchanged.
            TimeReference seedBurnRef = TimeRef == TimeReference.None ? TimeReference.APOAPSIS : TimeRef;
            string seedBurnOption = BurnTimeOption.TextTimeRef[seedBurnRef];
            BurnOptionsDropdown.choices = new List<string> { seedBurnOption };
            BurnOptionsDropdown.value = seedBurnOption;
            FlightPlanPlugin.Logger.LogInfo(
                $"BurnOptionsDropdown: seeded with '{seedBurnOption}' (TimeRef = {TimeRef}) - the legacy " +
                "seeded a single empty string, which opened one blank row");

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: BurnOptionsDropdown");

            // UI panels (used to control center part of UI based on the selected tab bar button)
            OSMPanel = container.Q<VisualElement>("OSMPanel");
            TRMShipToShipPanel = container.Q<VisualElement>("TRMShipToShipPanel");
            TRMShipToCelestialPanel = container.Q<VisualElement>("TRMShipToCelestialPanel");
            OTMMoonPanel = container.Q<VisualElement>("OTMMoonPanel");
            OTMPlanetPanel = container.Q<VisualElement>("OTMPlanetPanel");
            ROMPanel = container.Q<VisualElement>("ROMPanel");
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: ROMPanel");
            panels.Add(OSMPanel);
            panels.Add(TRMShipToShipPanel);
            panels.Add(TRMShipToCelestialPanel);
            panels.Add(OTMMoonPanel);
            panels.Add(OTMPlanetPanel);
            panels.Add(ROMPanel);

            //tabs.Add("Ownship Maneuvers", OSMPanel);
            //tabs.Add("Target Relative Maneuvers - Ship to Ship", TRMShipToShipPanel);
            //tabs.Add("Target Relative Maneuvers - Ship to Celestial", TRMShipToCelestialPanel);
            //tabs.Add("Orbital Transfer Maneuvers - Moon", OTMMoonPanel);
            //tabs.Add("Orbital Transfer Maneuvers - Planet", OTMPlanetPanel);
            //tabs.Add("Resonant Orbit Maneuvers", ROMPanel);

            //thisTab = "Ownship Maneuvers";

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: panels.Add(ROMPanel)");
            panelNames.Add("Ownship Maneuvers");
            panelNames.Add("Target Relative Maneuvers: Vessel");
            panelNames.Add("Target Relative Maneuvers: Celestial");
            panelNames.Add("Orbital Transfer Maneuvers: Moon");
            panelNames.Add("Orbital Transfer Maneuvers: Planet");
            panelNames.Add("Resonant Orbit Maneuvers");

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: panelNames.Add(\"Resonant Orbit Maneuvers\")");

            // OMSPanel UI controls
            CircularizeButtonOSM = container.Q<Button>("CircularizeButtonOSM");
            NewPeButtonOSM = container.Q<Button>("NewPeButtonOSM");
            NewPeValueOSM = container.Q<TextField>("NewPeValueOSM");
            NewApButtonOSM = container.Q<Button>("NewApButtonOSM");
            NewApValueOSM = container.Q<TextField>("NewApValueOSM");
            NewPeApButtonOSM = container.Q<Button>("NewPeApButtonOSM");
            NewIncButtonOSM = container.Q<Button>("NewIncButtonOSM");
            NewIncValueOSM = container.Q<TextField>("NewIncValueOSM");
            NewLANButtonOSM = container.Q<Button>("NewLANButtonOSM");
            NewLANValueOSM = container.Q<TextField>("NewLANValueOSM");
            NewSMAButtonOSM = container.Q<Button>("NewSMAButtonOSM");
            NewSMAValueOSM = container.Q<TextField>("NewSMAValueOSM");

            // PeAltitude_km = TargetPeR_m / 1000;
            NewPeValueOSM.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.PeAltitude_km = newFloat;
                    TargetPeR_m = newFloat * 1000; // + _activeVessel.Orbit.referenceBody.radius;
                    FlightPlanPlugin.Logger.LogDebug($"NewPeValueOSM: {newFloat} -> {TargetPeR_m / 1000}"); // FpUiController.PeAltitude_km
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"NewPeValueOSM: unable to parse '{evt.newValue}'");
            });
            NewPeValueOSM.value = (TargetPeR_m / 1000).ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: NewPeValueOSM.RegisterValueChangedCallback event initialized.");

            // FpUiController.ApAltitude_km = _targetApR;
            NewApValueOSM.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.ApAltitude_km = newFloat;
                    TargetApR_m = newFloat * 1000.0; // + _activeVessel.Orbit.referenceBody.radius;
                    FlightPlanPlugin.Logger.LogDebug($"NewApValueOSM: {newFloat} -> {TargetApR_m / 1000}"); // FpUiController.ApAltitude_km
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"NewApValueOSM: unable to parse '{evt.newValue}'");
            });
            NewApValueOSM.value = (TargetApR_m / 1000).ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: NewApValueOSM.RegisterValueChangedCallback event initialized.");

            NewIncValueOSM.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.TargetInc_deg = newFloat;
                    TargetInc_deg = newFloat;
                    FlightPlanPlugin.Logger.LogDebug($"NewIncValueOSM: {TargetInc_deg}"); // FpUiController.TargetInc_deg
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"NewIncValueOSM: unable to parse '{evt.newValue}'");
            });
            NewIncValueOSM.value = TargetInc_deg.ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: NewIncValueOSM.RegisterValueChangedCallback event initialized.");

            NewLANValueOSM.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.TargetLAN_deg = newFloat;
                    TargetLAN_deg = newFloat;
                    FlightPlanPlugin.Logger.LogDebug($"NewLANValueOSM: {TargetLAN_deg}"); // FpUiController.TargetLAN_deg
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"NewLANValueOSM: unable to parse '{evt.newValue}'");
            });
            NewLANValueOSM.value = TargetLAN_deg.ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: NewLANValue.RegisterValueChangedCallback event initialized.");

            NewSMAValueOSM.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.TargetSMA_km = newFloat;
                    TargetSMA_m = newFloat * 1000; // + _activeVessel.Orbit.referenceBody.radius;
                    FlightPlanPlugin.Logger.LogDebug($"NewSMAValueOSM: {TargetSMA_m / 1000}"); // FpUiController.TargetSMA_km
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"NewSMAValueOSM: unable to parse '{evt.newValue}'");
            });
            NewSMAValueOSM.value = (TargetSMA_m / 1000).ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: NewSMAValue.RegisterValueChangedCallback event initialized.");

            // Setup OSMPanel buttons
            CircularizeButtonOSM.clicked += () => Circularize(ManeuverType.circularize);
            NewPeButtonOSM.clicked += () => NewPe(ManeuverType.newPe);
            NewApButtonOSM.clicked += () => NewAp(ManeuverType.newAp);
            NewPeApButtonOSM.clicked += () => NewPeAp(ManeuverType.newPeAp);
            NewIncButtonOSM.clicked += () => NewInc(ManeuverType.newInc);
            NewLANButtonOSM.clicked += () => NewLAN(ManeuverType.newLAN);
            NewSMAButtonOSM.clicked += () => NewSMA(ManeuverType.newSMA);

            //Add the OSMPanel buttons to the list of toggle buttons
            toggleButtons.Add(CircularizeButtonOSM);
            toggleButtons.Add(NewPeButtonOSM);
            toggleButtons.Add(NewApButtonOSM);
            toggleButtons.Add(NewPeApButtonOSM);
            toggleButtons.Add(NewIncButtonOSM);
            toggleButtons.Add(NewLANButtonOSM);
            toggleButtons.Add(NewSMAButtonOSM);

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: OMSPanel initialized");

            // TRMSPanel UI controls
            SelectPortToggle = container.Q<Toggle>("SelectPortToggle");
            TargetOrbitTRMS = container.Q<Label>("TargetOrbitTRMS");
            CurrentOrbitTRMS = container.Q<Label>("CurrentOrbitTRMS");
            RelativeIncTRMS = container.Q<Label>("RelativeIncTRMS");
            SynodicPeriodTRMS = container.Q<Label>("SynodicPeriodTRMS");
            NextWindowTRMS = container.Q<Label>("NextWindowTRMS");
            NextClosestApproachTRMS = container.Q<Label>("NextClosestApproachTRMS");
            SeparationAtCaTRMS = container.Q<Label>("SeparationAtCaTRMS");
            RelativeVelocityRowTRMS = container.Q<VisualElement>("RelativeVelocityRowTRMS");
            RelativeVelocityTRMS = container.Q<Label>("RelativeVelocityTRMS");
            MatchPlanesButtonTRMS = container.Q<Button>("MatchPlanesButtonTRMS");
            NewApButtonTRMS = container.Q<Button>("NewApButtonTRMS");
            NewApValueTRMS = container.Q<TextField>("NewApValueTRMS");
            CircularizeButtonTRMS = container.Q<Button>("CircularizeButtonTRMS");
            HohmannTransferButtonTRMS = container.Q<Button>("HohmannTransferButtonTRMS");
            CourseCorrectionButtonTRMS = container.Q<Button>("CourseCorrectionButtonTRMS");
            CourseCorrectionValueTRMS = container.Q<TextField>("CourseCorrectionValueTRMS");
            MatchVelocityButtonTRMS = container.Q<Button>("MatchVelocityButtonTRMS");
            InterceptButtonTRMS = container.Q<Button>("InterceptButtonTRMS");
            InterceptValueTRMS = container.Q<TextField>("InterceptValueTRMS");
            TRMStatus = container.Q<Label>("TRMStatus");

            SelectPortToggle.RegisterValueChangedCallback((evt) => UpdateTargets());

            NewApValueTRMS.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.ApAltitude_km = newFloat;
                    TargetApR_m = newFloat * 1000.0; //  + _activeVessel.Orbit.referenceBody.radius;
                    FlightPlanPlugin.Logger.LogDebug($"NewApValueTRMS: {TargetApR_m / 1000}"); // FpUiController.ApAltitude_km;
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"NewApValueTRMS: unable to parse '{evt.newValue}'");
            });
            NewApValueTRMS.value = (TargetApR_m / 1000).ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: NewApValueTRMS.RegisterValueChangedCallback event initialized.");

            CourseCorrectionValueTRMS.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.InterceptDistanceVessel = newFloat;
                    TargetInterceptDistanceVessel_m = newFloat;
                    FlightPlanPlugin.Logger.LogDebug($"NewApValueTRMS: {TargetInterceptDistanceVessel_m}"); // FpUiController.InterceptDistanceVessel
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"NewApValueTRMS: unable to parse '{evt.newValue}'");
            });
            CourseCorrectionValueTRMS.value = TargetInterceptDistanceVessel_m.ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: CourseCorrectionValueTRMS.RegisterValueChangedCallback event initialized.");

            InterceptValueTRMS.RegisterValueChangedCallback((evt) =>
            {
                if (MyTryParse(evt.newValue, out double newTime))
                {
                    // FpUiController.InterceptTime = newFloat;
                    TargetInterceptTime_s = newTime;
                    FlightPlanPlugin.Logger.LogInfo($"InterceptValueTRMS: {TargetInterceptTime_s}"); // FpUiController.InterceptTime
                }
                else
                {
                    FlightPlanPlugin.Logger.LogInfo($"InterceptValueTRMS: unable to parse '{evt.newValue}'");
                    // InterceptValueTRMS.style.borderBottomColor = new Color.red
                }
            });
            // InterceptValueTRMS.value = TargetInterceptTime_s.ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: InterceptValueTRMS.RegisterValueChangedCallback event initialized.");

            MatchPlanesButtonTRMS.clicked += () => MatchPlanes(ManeuverType.matchPlane);
            NewApButtonTRMS.clicked += () => NewAp(ManeuverType.newAp);
            CircularizeButtonTRMS.clicked += () => Circularize(ManeuverType.circularize);
            HohmannTransferButtonTRMS.clicked += () => HohmannTransfer(ManeuverType.hohmannXfer);
            MatchVelocityButtonTRMS.clicked += () => MatchVelocity(ManeuverType.matchVelocity);
            CourseCorrectionButtonTRMS.clicked += () => CourseCorrection(ManeuverType.courseCorrection, TargetInterceptDistanceVessel_m); // FpUiController.InterceptDistanceVessel
            InterceptButtonTRMS.clicked += () => Intercept(ManeuverType.interceptTgt);

            //Add the TRMSPanel buttons to the list of toggle buttons
            toggleButtons.Add(MatchPlanesButtonTRMS);
            toggleButtons.Add(NewApButtonTRMS);
            toggleButtons.Add(CircularizeButtonTRMS);
            toggleButtons.Add(HohmannTransferButtonTRMS);
            toggleButtons.Add(MatchVelocityButtonTRMS);
            toggleButtons.Add(CourseCorrectionButtonTRMS);
            toggleButtons.Add(InterceptButtonTRMS);

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: TRMSPanel initialized");

            // TRMCPanel UI controls
            TargetOrbitTRMC = container.Q<Label>("TargetOrbitTRMC");
            CurrentOrbitTRMC = container.Q<Label>("CurrentOrbitTRMC");
            RelativeIncTRMC = container.Q<Label>("RelativeIncTRMC");
            PhaseAngleTRMC = container.Q<Label>("PhaseAngleTRMC");
            XferPhaseAngleTRMC = container.Q<Label>("XferPhaseAngleTRMC");
            XferTimeTRMC = container.Q<Label>("XferTimeTRMC");
            SynodicPeriodTRMC = container.Q<Label>("SynodicPeriodTRMC");
            NextWindowTRMC = container.Q<Label>("NextWindowTRMC");
            NextClosestApproachTRMC = container.Q<Label>("NextClosestApproachTRMC");
            MatchPlanesButtonTRMC = container.Q<Button>("MatchPlanesButtonTRMC");
            HohmannTransferButtonTRMC = container.Q<Button>("HohmannTransferButtonTRMC");
            CourseCorrectionButtonTRMC = container.Q<Button>("CourseCorrectionButtonTRMC");
            CourseCorrectionValueTRMC = container.Q<TextField>("CourseCorrectionValueTRMC");
            InterceptButtonTRMC = container.Q<Button>("InterceptButtonTRMC");
            InterceptValueTRMC = container.Q<TextField>("InterceptValueTRMC");
            MatchVelocityButtonTRMC = container.Q<Button>("MatchVelocityButtonTRMC");

            CourseCorrectionValueTRMC.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.InterceptDistanceCelestial = newFloat;
                    TargetInterceptDistanceCelestial_m = newFloat * 1000;
                    FlightPlanPlugin.Logger.LogDebug($"CourseCorrectionValueTRMC: {TargetInterceptDistanceCelestial_m / 1000}"); // FpUiController.InterceptDistanceCelestial
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"CourseCorrectionValueTRMC: unable to parse '{evt.newValue}'");
            });
            CourseCorrectionValueTRMC.value = (TargetInterceptDistanceCelestial_m / 1000).ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: CourseCorrectionValueTRMC.RegisterValueChangedCallback event initialized.");

            InterceptValueTRMC.RegisterValueChangedCallback((evt) =>
            {
                if (MyTryParse(evt.newValue, out double newTime))
                {
                    // FpUiController.InterceptTime = newFloat;
                    TargetInterceptTime_s = newTime;
                    FlightPlanPlugin.Logger.LogInfo($"InterceptValueTRMC: {TargetInterceptTime_s}"); // FpUiController.InterceptTime
                }
                else
                    FlightPlanPlugin.Logger.LogInfo($"InterceptValueTRMC: unable to parse '{evt.newValue}'");
            });
            // InterceptValueTRMC.value = TargetInterceptTime_s.ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: InterceptValueTRMC.RegisterValueChangedCallback event initialized.");

            MatchPlanesButtonTRMC.clicked += () => MatchPlanes(ManeuverType.matchPlane);
            HohmannTransferButtonTRMC.clicked += () => HohmannTransfer(ManeuverType.hohmannXfer);
            CourseCorrectionButtonTRMC.clicked += () => CourseCorrection(ManeuverType.courseCorrection, TargetInterceptDistanceCelestial_m); // FpUiController.InterceptDistanceCelestial*1000
            InterceptButtonTRMC.clicked += () => Intercept(ManeuverType.interceptTgt);
            MatchVelocityButtonTRMC.clicked += () => MatchVelocity(ManeuverType.matchVelocity);

            //Add the TRMCPanel buttons to the list of toggle buttons
            toggleButtons.Add(MatchPlanesButtonTRMC);
            toggleButtons.Add(HohmannTransferButtonTRMC);
            toggleButtons.Add(CourseCorrectionButtonTRMC);

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: TRMCPanel initialized");

            // OTMMoonPanel UI controls
            ReturnFromMoonButtonOTM = container.Q<Button>("ReturnFromMoonButtonOTM");
            MoonReturnPeValue = container.Q<TextField>("MoonReturnPeValue");

            MoonReturnPeValue.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.MoonReturnAltitude_km = newFloat;
                    // var parentPlanet = _activeVessel.Orbit.referenceBody.Orbit.referenceBody;
                    TargetMRPeR_m = newFloat * 1000; // + parentPlanet.radius;
                    FlightPlanPlugin.Logger.LogDebug($"MoonReturnPeValue: {newFloat} -> {TargetMRPeR_m / 1000}"); // FpUiController.MoonReturnAltitude_km
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"MoonReturnPeValue: unable to parse '{evt.newValue}'");
            });
            MoonReturnPeValue.value = (TargetMRPeR_m / 1000).ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: MoonReturnPeValue.RegisterValueChangedCallback event initialized.");

            ReturnFromMoonButtonOTM.clicked += () => ReturnFromMoon(ManeuverType.moonReturn);

            //Add the OTMMoonPanel buttons to the list of toggle buttons
            toggleButtons.Add(ReturnFromMoonButtonOTM);

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: OTMMoonPanel initialized");

            // OTMPlanetPanel UI controls
            DataDisplayGroup = container.Q<VisualElement>("DataDisplayGroup");
            RelativeIncOTM = container.Q<Label>("RelativeIncOTM");
            PhaseAngleOTM = container.Q<Label>("PhaseAngleOTM");
            XferPhaseAngleOTM = container.Q<Label>("XferPhaseAngleOTM");
            XferTimeOTM = container.Q<Label>("XferTimeOTM");
            SynodicPeriodOTM = container.Q<Label>("SynodicPeriodOTM");
            NextWindowOTM = container.Q<Label>("NextWindowOTM");
            EjectionDvOTM = container.Q<Label>("EjectionDvOTM");
            InterplanetaryXferButton = container.Q<Button>("InterplanetaryXferButton");
            AdvInterplanetaryXferButton = container.Q<Button>("AdvInterplanetaryXferButton");
            AdvInterplanetaryXferGroup = container.Q<VisualElement>("AdvInterplanetaryXferGroup");
            AdvXferGroup = container.Q<VisualElement>("AdvXferGroup");
            LimitedTimeGroup = container.Q<VisualElement>("LimitedTimeGroup");
            PorkchopGroup = container.Q<VisualElement>("PorkchopGroup");
            // `PorkchopToggle` is NOT in FP_UI.uxml (only PorkchopGroup/Row/Display are, phase-3 note D10)
            // and nothing reads it, so the query is deleted rather than kept as a logged null.
            MaxArrivalTimeInput = container.Q<TextField>("MaxArrivalTimeInput");
            Computing = container.Q<Label>("Computing");
            PorkchopDisplay = container.Q<VisualElement>("PorkchopDisplay");
            XferDeltaVLabel = container.Q<Label>("XferDeltaVLabel");
            ResetButton = container.Q<Button>("ResetButton");
            CaptureBurnToggle = container.Q<Toggle>("CaptureBurnToggle");
            AdvXferPeriapsisInput = container.Q<TextField>("AdvXferPeriapsisInput");
            LowestDvButton = container.Q<Button>("LowestDvButton");
            ASAPButton = container.Q<Button>("ASAPButton");
            DepartureTimeLabel = container.Q<Label>("DepartureTimeLabel");
            TransitDurationTimeLabel = container.Q<Label>("TransitDurationTimeLabel");

            MaxArrivalTimeInput.RegisterValueChangedCallback((evt) =>
            {
                if (MyTryParse(evt.newValue, out double newValue))
                {
                    // FpUiController.MaxArrivalTime_s = newValue;
                    MaxArrivalTime_s = newValue;
                    FlightPlanPlugin.Logger.LogDebug($"MaxArrivalTimeInput: {evt.newValue} -> {newValue}");
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"MaxArrivalTimeInput: unable to parse '{evt.newValue}'");
            });
            MaxArrivalTimeInput.value = FPUtility.SecondsToTimeString(0);

            InterplanetaryXferButton.clicked += () => InterplanetaryXfer(ManeuverType.planetaryXfer);
            AdvInterplanetaryXferButton.clicked += () => AdvInterplanetaryXfer(ManeuverType.advancedPlanetaryXfer);
            ResetButton.clicked += ResetPorkchop;
            LowestDvButton.clicked += LowestDv;
            ASAPButton.clicked += ASAPTransfer;

            // Prevent the user from being able to drage the GUI if they click inside the PorkchopDisplay.
            PorkchopDisplay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            PorkchopDisplay.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            PorkchopDisplay.RegisterCallback<PointerMoveEvent>(evt => evt.StopPropagation());

            // KEEP the control, drop the callback: it cancelled NodeManager's porkchop worker
            // (`op.worker = null`), and neither exists on 0.2.8.5 (D10).

            AdvXferPeriapsisInput.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    // FpUiController.MoonReturnAltitude_km = newFloat;
                    // var parentPlanet = _activeVessel.Orbit.referenceBody.Orbit.referenceBody;
                    TargetAdvXferPe_m = newFloat * 1000; // + parentPlanet.radius;
                    FlightPlanPlugin.Logger.LogDebug($"AdvXferPeriapsisInput: {newFloat} -> {TargetAdvXferPe_m / 1000}");
                }
                else
                    FlightPlanPlugin.Logger.LogDebug($"AdvXferPeriapsisInput: unable to parse '{evt.newValue}'");
            });
            AdvXferPeriapsisInput.value = (TargetAdvXferPe_m / 1000).ToString();

            //Add the TRMCPanel buttons to the list of toggle buttons
            toggleButtons.Add(InterplanetaryXferButton);
            toggleButtons.Add(AdvInterplanetaryXferButton);

            // This Shit Isn't Ready At all!
            if (FlightPlanPlugin.Instance._experimental.Value)
                AdvInterplanetaryXferGroup.style.display = DisplayStyle.Flex;
            else
                AdvInterplanetaryXferGroup.style.display = DisplayStyle.None;
            AdvXferGroup.style.display = DisplayStyle.None;

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: OTMPlanetPanel initialized");

            // ROMPanel UI controls
            IncreasePayloadsButton = container.Q<Button>("IncreasePayloadsButton");
            DecreasePayloadsButton = container.Q<Button>("DecreasePayloadsButton");
            NumPayloads = container.Q<Label>("NumPayloads");
            IncreaseOrbitsButton = container.Q<Button>("IncreaseOrbitsButton");
            DecreaseOrbitsButton = container.Q<Button>("DecreaseOrbitsButton");
            NumOrbits = container.Q<Label>("NumOrbits");
            OrbitalResonance = container.Q<Label>("OrbitalResonance");
            TargetAltitudeInput = container.Q<TextField>("TargetAltitudeInput");
            SetApoapsisButton = container.Q<Button>("SetApoapsisButton");
            CurrentApoapsis = container.Q<Label>("CurrentApoapsis");
            SetPeriapsisButton = container.Q<Button>("SetPeriapsisButton");
            CurrentPeriapsis = container.Q<Label>("CurrentPeriapsis");
            SetSynchronousAltButton = container.Q<Button>("SetSynchronousAltButton");
            SynchronousAlt = container.Q<Label>("SynchronousAlt");
            SetSemiSynchronousAltButton = container.Q<Button>("SetSemiSynchronousAltButton");
            SemiSynchronousAlt = container.Q<Label>("SemiSynchronousAlt");
            SOIAlt = container.Q<Label>("SOIAlt");
            SetMinLOSAltButton = container.Q<Button>("SetMinLOSAltButton");
            MinLOSAlt = container.Q<Label>("MinLOSAlt");
            Occlusion = container.Q<Toggle>("Occlusion");
            AtmOccRow = container.Q<VisualElement>("AtmOccRow");
            AtmOccInput = container.Q<TextField>("AtmOccInput");
            VacOccRow = container.Q<VisualElement>("VacOccRow");
            VacOccInput = container.Q<TextField>("VacOccInput");
            Dive = container.Q<Toggle>("Dive");
            ResonantOrbitPeriod = container.Q<Label>("ResonantOrbitPeriod");
            ResonantOrbitAp = container.Q<Label>("ResonantOrbitAp");
            ResonantOrbitPe = container.Q<Label>("ResonantOrbitPe");
            ResonantOrbitEcc = container.Q<Label>("ResonantOrbitEcc");
            ResonantOrbitInjection = container.Q<Label>("ResonantOrbitInjection");
            FixPeGroup = container.Q<VisualElement>("FixPeGroup");
            FixPeButton = container.Q<Button>("FixPeButton");
            FixPeStatus = container.Q<Label>("FixPeStatus");
            FixApGroup = container.Q<VisualElement>("FixApGroup");
            FixApButton = container.Q<Button>("FixApButton");
            FixApStatus = container.Q<Label>("FixApStatus");

            TargetAltitudeInput.RegisterValueChangedCallback((evt) =>
            {
                if (double.TryParse(evt.newValue, out double newValue))
                {
                    _target_alt_km = newValue;
                }
            });
            Occlusion.RegisterValueChangedCallback((evt) =>
            {
                if (evt.newValue)
                {
                    AtmOccRow.style.display = DisplayStyle.Flex;
                    VacOccRow.style.display = DisplayStyle.Flex;
                }
                else
                {
                    AtmOccRow.style.display = DisplayStyle.None;
                    VacOccRow.style.display = DisplayStyle.None;
                }
            });

            AtmOccInput.RegisterValueChangedCallback((evt) =>
            {
                if (double.TryParse(evt.newValue, out double newVal))
                    OccModAtm = newVal;
            });

            VacOccInput.RegisterValueChangedCallback((evt) =>
            {
                if (double.TryParse(evt.newValue, out double newVal))
                    OccModVac = newVal;
            });

            AtmOccRow.style.display = DisplayStyle.None;
            VacOccRow.style.display = DisplayStyle.None;
            IncreasePayloadsButton.clicked += () => IncrementPayloads(+1);
            DecreasePayloadsButton.clicked += () => IncrementPayloads(-1);
            IncreaseOrbitsButton.clicked += () => IncrementOrbits(+1);
            DecreaseOrbitsButton.clicked += () => IncrementOrbits(-1);
            SetApoapsisButton.clicked += () => SetTargetAlt(_activeVessel.Orbit.ApoapsisArl / 1000.0);
            SetPeriapsisButton.clicked += () => SetTargetAlt(_activeVessel.Orbit.PeriapsisArl / 1000.0);
            SetSynchronousAltButton.clicked += () => SetTargetAlt((_synchronousAlt - _activeVessel.mainBody.radius) / 1000);
            SetSemiSynchronousAltButton.clicked += () => SetTargetAlt((_semiSynchronousAlt - _activeVessel.mainBody.radius) / 1000);
            SetMinLOSAltButton.clicked += () => SetTargetAlt(_minLOSAlt / 1000);
            Dive.RegisterValueChangedCallback((evt) =>
            {
                // FpUiController.DiveOrbit = evt.newValue;
                UpdateResonance();
            });
            FixPeButton.clicked += () =>
            {
                if (double.TryParse(ResonantOrbitPe.text, out double targetPe))
                {
                    // PeAltitude_km = targetPe;
                    TargetPeR_m = targetPe * 1000.0;
                    NewPe(ManeuverType.fixPe);
                }
            };
            FixApButton.clicked += () =>
            {
                if (double.TryParse(ResonantOrbitAp.text, out double targetAp))
                {
                    // ApAltitude_km = targetAp;
                    TargetApR_m = targetAp * 1000.0;
                    NewAp(ManeuverType.fixAp);
                }
            };

            //Add the ROMPanel buttons to the list of toggle buttons
            toggleButtons.Add(FixPeButton);
            toggleButtons.Add(FixApButton);

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: ROMPanel initialized");

            // BurnTimeOption situational inputs (display or not depending on the burn time option selected)
            AfterFixedTime = container.Q<VisualElement>("AfterFixedTime");
            AfterFixedTimeInput = container.Q<TextField>("AfterFixedTimeInput");
            AtAnAltitude = container.Q<VisualElement>("AtAnAltitude");
            ManeuverAltitudeInput = container.Q<TextField>("ManeuverAltitudeInput");

            // TimeOffset_s = FpUiController.TimeOffset_s;
            AfterFixedTimeInput.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    FpUiController.TimeOffset_s = newFloat;
                    TimeOffset_s = newFloat;
                    FlightPlanPlugin.Logger.LogInfo($"AfterFixedTimeInput: {TimeOffset_s}"); // FpUiController.TimeOffset
                }
                else
                    FlightPlanPlugin.Logger.LogInfo($"AfterFixedTimeInput: unable to parse '{evt.newValue}'");
            });
            AfterFixedTimeInput.value = TimeOffset_s.ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: AfterFixedTimeInput.RegisterValueChangedCallback event initialized.");

            // Altitude_km = FpUiController.Altitude_km;
            ManeuverAltitudeInput.RegisterValueChangedCallback((evt) =>
            {
                if (float.TryParse(evt.newValue, out float newFloat))
                {
                    FpUiController.Altitude_km = newFloat;
                    Altitude_km = newFloat;
                    FlightPlanPlugin.Logger.LogInfo($"ManeuverAltitudeInput: {Altitude_km}"); // FpUiController.Altitude_km
                }
                else
                    FlightPlanPlugin.Logger.LogInfo($"ManeuverAltitudeInput: unable to parse '{evt.newValue}'");
            });
            ManeuverAltitudeInput.value = Altitude_km.ToString();
            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: ManeuverAltitudeInput.RegisterValueChangedCallback event initialized.");

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: Burn time option inputs initialized.");

            // General All-Purpose UI Status display field
            Status = container.Q<Label>("Status");

            // BottomBar UI elements
            MakeNodeButtonBox = container.Q<VisualElement>("MakeNodeButtonBox");
            MNCButtonBox = container.Q<VisualElement>("MNCButtonBox");
            K2D2ButtonBox = container.Q<VisualElement>("K2D2ButtonBox");
            MakeNodeButton = container.Q<Button>("MakeNodeButton");
            MNCButton = container.Q<Button>("MNCButton");
            K2D2Button = container.Q<Button>("K2D2Button");
            K2D2Status = container.Q<Label>("K2D2Status");

            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: Panel groups initialized.");

            // Initial configuration of UI
            PanelLabel.text = "Ownship Maneuvers";
            OSMPanel.style.display = DisplayStyle.Flex;
            TRMShipToShipPanel.style.display = DisplayStyle.None;
            TRMShipToCelestialPanel.style.display = DisplayStyle.None;
            OTMMoonPanel.style.display = DisplayStyle.None;
            OTMPlanetPanel.style.display = DisplayStyle.None;
            ROMPanel.style.display = DisplayStyle.None;
            AfterFixedTime.style.display = DisplayStyle.None;
            AtAnAltitude.style.display = DisplayStyle.None;
            Status.text = FPStatus.StatusText; // "Virgin";

            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: UI initialized.");

            // Setup close button
            CloseButton.clicked += () => FlightPlanPlugin.Instance.ToggleButton(false);
            // _container.Q<Button>("CloseButton").clicked += () => FlightPlanPlugin.Instance.ToggleButton(false);

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: CloseButton callback initialized.");

            // Setup TabBar buttons
            OSMButton.clicked += () => SetTab(Tab.OSM);
            TRMShipToShipButton.clicked += () => SetTab(Tab.TRMShipToShip);
            TRMShipToCelestialButton.clicked += () => SetTab(Tab.TRMShipToCelestial);
            OTMMoonButton.clicked += () => SetTab(Tab.OTMMoon);
            OTMPlanetButton.clicked += () => SetTab(Tab.OTMPlanet);
            ROMButton.clicked += () => SetTab(Tab.ROM);

            TabButtons.Add(OSMButton);
            TabButtons.Add(TRMShipToShipButton);
            TabButtons.Add(TRMShipToCelestialButton);
            TabButtons.Add(OTMMoonButton);
            TabButtons.Add(OTMPlanetButton);
            TabButtons.Add(ROMButton);

            SetTab(0);
            BurnOptions = new BurnTimeOption();
            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: TabBar button callbacks initialized.");

            // FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: OSM Panel button callbacks initialized.");

            // Setup BottomBar Buttons
            MakeNodeButton.clicked += MakeNode;
            // MNCButton is intentionally left unwired: MNC is descoped (D7).
            K2D2Button.clicked += K2D2;

            FlightPlanPlugin.Logger.LogInfo($"InitializeElements: {testLog++}: BottomBar button callback initialized.");

            initialized = true;
            FlightPlanPlugin.Logger.LogInfo($"FP: UITK GUI Initialized. initialized set to {initialized}");
        }

        private BurnTimeOption BurnOptions;

        public void BindFunctions()
        {

        }

        public void SetDefaults()
        {

        }

        public void UnbindFunctions()
        {

        }

        private void TargetType()
        {
            // If the button is set to Celestial
            if (TargetTypeButton.text == "Celestial")
            {
                // We're switching from Celestial to either Vessel or Port
                ListVessels();
                if (SelectPortToggle.value)
                {
                    TargetTypeButton.text = "Port";
                    TargetSelectionDropdown.choices = new List<string>(targetPorts.Keys);
                    FlightPlanPlugin.Logger.LogInfo($"TargetType: Setting to Target Type to 'Port'");
                    FlightPlanPlugin.Logger.LogInfo($"Choices: {string.Join(",", TargetSelectionDropdown.choices)}");
                    // FlightPlanPlugin.Logger.LogInfo($"Keys: {string.Join(",", targetPorts.Keys)}");
                }
                else
                {
                    TargetTypeButton.text = "Vessel";
                    TargetSelectionDropdown.choices = new List<string>(targetVessels.Keys);
                    FlightPlanPlugin.Logger.LogInfo($"TargetType: Setting to Target Type to 'Vessel'");
                    FlightPlanPlugin.Logger.LogInfo($"Choices: {string.Join(",", TargetSelectionDropdown.choices)}");
                    // FlightPlanPlugin.Logger.LogInfo($"Keys: {string.Join(",", targetPorts.Keys)}");
                }
            }
            else
            {
                // We're switching from either Vessel or Port to Celestial
                ListBodies();
                TargetTypeButton.text = "Celestial";
                TargetSelectionDropdown.choices = new List<string>(targetBodies.Keys);
                FlightPlanPlugin.Logger.LogInfo($"TargetType: Setting to Target Type to 'Celestial'");
                FlightPlanPlugin.Logger.LogInfo($"Choices: {string.Join(",", TargetSelectionDropdown.choices)}");
                // FlightPlanPlugin.Logger.LogInfo($"Keys: {string.Join(",", targetPorts.Keys)}");
            }
        }

        private void UpdateTargets()
        {
            // If the button is set to Celestial
            if (SelectPortToggle.value)
            {
                if (TargetTypeButton.text == "Vessel")  // RESUME HERE
                {
                    TargetTypeButton.text = "Port";
                    ListVessels();
                    TargetSelectionDropdown.choices = new List<string>(targetPorts.Keys);
                    FlightPlanPlugin.Logger.LogInfo($"TargetType: Setting to Target Type to 'Port'");
                    FlightPlanPlugin.Logger.LogInfo($"Choices: {string.Join(",", TargetSelectionDropdown.choices)}");
                    // FlightPlanPlugin.Logger.LogInfo($"Keys: {string.Join(",", targetPorts.Keys)}");
                }
                //else
                //{
                //  // TargetTypeButton.text = "Celestial";
                //  ListBodies();
                //  TargetSelectionDropdown.choices = new List<string>(targetBodies.Keys);
                //  FlightPlanPlugin.Logger.LogInfo($"TargetType: Setting to Target Type to 'Celestial'");
                //  FlightPlanPlugin.Logger.LogInfo($"Choices: {string.Join(",", TargetSelectionDropdown.choices)}");
                //  // FlightPlanPlugin.Logger.LogInfo($"Keys: {string.Join(",", targetPorts.Keys)}");
                //}
            }
            else
            {
                if (TargetTypeButton.text == "Port")  // RESUME HERE
                {
                    TargetTypeButton.text = "Vessel";
                    ListVessels();
                    TargetSelectionDropdown.choices = new List<string>(targetVessels.Keys);
                    FlightPlanPlugin.Logger.LogInfo($"TargetType: Setting to Target Type to 'Vessel'");
                    FlightPlanPlugin.Logger.LogInfo($"Choices: {string.Join(",", TargetSelectionDropdown.choices)}");
                    // FlightPlanPlugin.Logger.LogInfo($"Keys: {string.Join(",", targetPorts.Keys)}");
                }
                //else
                //{
                //  // TargetTypeButton.text = "Celestial";
                //  ListBodies();
                //  TargetSelectionDropdown.choices = new List<string>(targetBodies.Keys);
                //  FlightPlanPlugin.Logger.LogInfo($"TargetType: Setting to Target Type to 'Celestial'");
                //  FlightPlanPlugin.Logger.LogInfo($"Choices: {string.Join(",", TargetSelectionDropdown.choices)}");
                //  // FlightPlanPlugin.Logger.LogInfo($"Keys: {string.Join(",", targetPorts.Keys)}");
                //}
            }
        }

        public void CleanUp()
        {
            UnsetToggles();
            selectedManeuver = ManeuverType.None;
            ManeuverTypeDesc = "";
            // UI-fix increment 2: CleanUp re-styles the toggle buttons directly against the tree;
            // drop the change-detection caches so the next helper writes through to it.
            InvalidateUiCaches();
            FlightPlanPlugin.needToCleanUp = false;
        }

        // System.Drawing.Color col = System.Drawing.Color ColorTranslator.FromHtml("#262329");
        private void UnsetToggles()
        {
            for (int i = 0; i < toggleButtons.Count; i++)
            {
                ToggleButtonBackgroundColor(toggleButtons[i], false);
                ToggleButtonTextColor(toggleButtons[i], false);
            }
            AdvXferGroup.style.display = DisplayStyle.None;
        }

        private void Circularize(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.circularize
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(CircularizeButtonOSM, true);
            ToggleButtonTextColor(CircularizeButtonTRMS, true);
            FlightPlanPlugin.Logger.LogInfo($"Circularize: selectedManeuver = {selectedManeuver} {_burnTimeOption}");
        }

        private void NewPe(ManeuverType thisSelectedManeuver)
        {
            //_targetPeR = FpUiController.PeAltitude_km * 1000 + _activeVessel.Orbit.referenceBody.radius;
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.newPe;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(NewPeButtonOSM, true);
            ToggleButtonTextColor(FixPeButton, true);
            FlightPlanPlugin.Logger.LogInfo($"NewPe: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking {TargetPeR_m / 1000}"); // FpUiController.PeAltitude_km
        }

        private void NewAp(ManeuverType thisSelectedManeuver)
        {
            // _targetApR = FpUiController.ApAltitude_km * 1000 + _activeVessel.Orbit.referenceBody.radius;
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.newAp;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(NewApButtonOSM, true);
            ToggleButtonTextColor(NewApButtonTRMS, true);
            ToggleButtonTextColor(FixApButton, true);
            FlightPlanPlugin.Logger.LogInfo($"NewAp: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking {TargetApR_m / 1000}"); // FpUiController.ApAltitude_km
        }

        private void NewPeAp(ManeuverType thisSelectedManeuver)
        {
            // _targetPeR = FpUiController.PeAltitude_km * 1000 + _activeVessel.Orbit.referenceBody.radius;
            // _targetApR = FpUiController.ApAltitude_km * 1000 + _activeVessel.Orbit.referenceBody.radius;
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.newPeAp;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(NewPeApButtonOSM, true);
            FlightPlanPlugin.Logger.LogInfo($"NewPeAp: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking {TargetApR_m / 1000} x {TargetPeR_m / 1000}"); // FpUiController.ApAltitude_km x FpUiController.PeAltitude_km
        }

        private void NewInc(ManeuverType thisSelectedManeuver)
        {
            double.TryParse(NewIncValueOSM.value, out TargetInc_deg);
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.newInc;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(NewIncButtonOSM, true);
            FlightPlanPlugin.Logger.LogInfo($"NewInc: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking {TargetInc_deg}"); // FpUiController.TargetInc_deg
        }

        private void NewLAN(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.newLAN;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(NewLANButtonOSM, true);
            FlightPlanPlugin.Logger.LogInfo($"NewLAN: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking {TargetLAN_deg}"); // FpUiController.TargetLAN_deg
        }

        private void NewSMA(ManeuverType thisSelectedManeuver)
        {
            // _targetSMA = FpUiController.TargetSMA_km * 1000 + _activeVessel.Orbit.referenceBody.radius;
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.newSMA;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(NewSMAButtonOSM, true);
            FlightPlanPlugin.Logger.LogInfo($"NewSMA: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking {TargetSMA_m / 1000}"); // FpUiController.TargetSMA_km
        }

        private void MatchPlanes(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.matchPlane;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(MatchPlanesButtonTRMS, true);
            ToggleButtonTextColor(MatchPlanesButtonTRMC, true);
            FlightPlanPlugin.Logger.LogInfo($"MatchPlanes: selectedManeuver = {selectedManeuver} {_burnTimeOption}");
        }

        private void HohmannTransfer(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.hohmannXfer;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(HohmannTransferButtonTRMS, true);
            ToggleButtonTextColor(HohmannTransferButtonTRMC, true);
            FlightPlanPlugin.Logger.LogInfo($"HohmannTransfer: selectedManeuver = {selectedManeuver} {_burnTimeOption}");
        }

        private void MatchVelocity(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.matchVelocity;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(MatchVelocityButtonTRMS, true);
            FlightPlanPlugin.Logger.LogInfo($"MatchVelocity: selectedManeuver = {selectedManeuver} {_burnTimeOption}");
        }

        private void Intercept(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.interceptTgt;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(InterceptButtonTRMS, true);
            FlightPlanPlugin.Logger.LogInfo($"Intercept: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking {TargetInterceptTime_s}"); // FpUiController.InterceptTime
        }

        private void CourseCorrection(ManeuverType thisSelectedManeuver, double _thisCourseCorrectionValue)
        {
            _courseCorrectionValue = _thisCourseCorrectionValue;
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.courseCorrection;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(CourseCorrectionButtonTRMS, true);
            ToggleButtonTextColor(CourseCorrectionButtonTRMC, true);
            FlightPlanPlugin.Logger.LogInfo($"NewPe: CourseCorrection = {selectedManeuver} {_burnTimeOption}: Seeking {_courseCorrectionValue}");
        }

        private void ReturnFromMoon(ManeuverType thisSelectedManeuver)
        {
            var parentPlanet = _activeVessel.Orbit.referenceBody.Orbit.referenceBody;
            // FpUiController.MoonReturnAltitude_km = MainUI.DrawToggleButtonWithTextField("Moon Return", ManeuverType.moonReturn, FpUiController.MoonReturnAltitude_km, "km");
            // _targetMRPeR = FpUiController.MoonReturnAltitude_km * 1000 + parentPlanet.radius;

            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.moonReturn;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(ReturnFromMoonButtonOTM, true);
            FlightPlanPlugin.Logger.LogInfo($"ReturnFromMoon: selectedManeuver = {selectedManeuver} {_burnTimeOption}: Seeking Pe at {parentPlanet.Name} of {(TargetMRPeR_m) / 1000} km"); // FpUiController.MoonReturnAltitude_km
        }

        private void InterplanetaryXfer(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.planetaryXfer;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            DataDisplayGroup.style.display = DisplayStyle.Flex;
            AdvXferGroup.style.display = DisplayStyle.None;
            ToggleButtonTextColor(InterplanetaryXferButton, true);
            FlightPlanPlugin.Logger.LogInfo($"InterplanetaryXfer: selectedManeuver = {selectedManeuver} {_burnTimeOption}");
        }

        private void AdvInterplanetaryXfer(ManeuverType thisSelectedManeuver)
        {
            // Set the BurnTimeOptions
            selectedManeuver = thisSelectedManeuver; // ManeuverType.advancedPlanetaryXfer;
            ManeuverTypeDesc = SetOptionsList(selectedManeuver);
            // Unset all button toggles
            UnsetToggles();
            // Set this one
            ToggleButtonTextColor(AdvInterplanetaryXferButton, true);
            DataDisplayGroup.style.display = DisplayStyle.None;
            AdvXferGroup.style.display = DisplayStyle.Flex;
            //if (TimeRef == TimeRef.LIMITED_TIME)
            //{
            //  op.DoParametersGUI(FlightPlanPlugin.Instance._activeVessel.Orbit, Game.UniverseModel.UniverseTime, FlightPlanPlugin.Instance._currentTarget.CelestialBody, OperationAdvancedTransfer.Mode.LimitedTime);
            //}
            //if (TimeRef == TimeRef.PORKCHOP)
            //{
            //  op.DoParametersGUI(FlightPlanPlugin.Instance._activeVessel.Orbit, Game.UniverseModel.UniverseTime, FlightPlanPlugin.Instance._currentTarget.CelestialBody, OperationAdvancedTransfer.Mode.Porkchop);
            //}
            FlightPlanPlugin.Logger.LogInfo($"AdvInterplanetaryXfer: selectedManeuver = {selectedManeuver} {_burnTimeOption}");
        }

        // The porkchop plot's Reset / Lowest-dV / ASAP buttons. All three were thin setters on
        // NodeManager's `OperationAdvancedTransfer` (`doReset` / `doSetLowestDv` / `doSetASAP`) which is
        // not ported (D9/D10); the source kept `doSetLowestDv`/`doSetASAP` unimplemented even then.
        // The buttons stay in the UXML, wired to a labelled drop instead of a fake pass.
        private static bool _porkchopLogged;

        private static void LogPorkchopUnavailable(string context)
        {
            FlightPlanPlugin.Logger.LogWarning(
                $"[FlightPlan] Porkchop plot is not available in this port ({context}): the legacy path " +
                "drove NodeManager's OperationAdvancedTransfer, which does not exist at Redux 0.2.9.0.104521 (D10).");
        }

        private void ResetPorkchop()
        {
            LogPorkchopUnavailable("Reset button");
        }

        private void LowestDv()
        {
            LogPorkchopUnavailable("Lowest-dV button");
        }

        private void ASAPTransfer()
        {
            LogPorkchopUnavailable("ASAP button");
        }

        private void IncrementPayloads(int increment)
        {
            if (NumSats + increment > 1) NumSats += increment;
            NumPayloads.text = NumSats.ToString();

            UpdateResonance();
            FlightPlanPlugin.Logger.LogInfo($"IncrementPayloads: {NumSats}");
        }

        private void IncrementOrbits(int increment)
        {
            if (NumOrb + increment > 0) NumOrb += increment;
            NumOrbits.text = NumOrb.ToString();

            UpdateResonance();
            FlightPlanPlugin.Logger.LogInfo($"IncrementOrbits: {NumOrb}");
        }

        private void UpdateResonance()
        {
            // Set the _resonance factors based on diving or not
            int _m = NumSats * NumOrb;
            int _n;
            if (Dive.value) // If we're going to dive under the target orbit for the deployment orbit
                _n = _m - 1;
            else // If not
                _n = _m + 1;
            double _resonance = (double)_n / _m;
            string _resonanceStr = String.Format("{0}/{1}", _n, _m);

            OrbitalResonance.text = _resonanceStr;
        }

        private void SetTargetAlt(double targetAlt)
        {
            _target_alt_km = targetAlt;
            TargetAltitudeInput.value = _target_alt_km.ToString("N3");
            FlightPlanPlugin.Logger.LogInfo($"SetTargetAlt: {_target_alt_km}");
        }

        // public static TimeRef timeRef = TimeRef.None;

        private void MakeNode()
        {
            FlightPlanPlugin.Logger.LogInfo($"MakeNode: selectedManeuver {selectedManeuver} {_burnTimeOption}");

            if (selectedManeuver == ManeuverType.None)
                return;

            BurnTimeOption.Instance.SetBurnTime();
            double _requestedBurnTime = BurnTimeOption.RequestedBurnTime;

            bool _pass = false;
            var target = _currentTarget;
            double UT = Game.UniverseModel.UniverseTime;

            switch (selectedManeuver)
            {
                case ManeuverType.circularize: // Working
                    _pass = FlightPlanPlugin.Instance.Circularize(_requestedBurnTime, -0.5);
                    break;
                case ManeuverType.newPe: // Working
                    if (TargetPeR_m < Orbit.ApoapsisArl || Orbit.eccentricity >= 1)
                        _pass = FlightPlanPlugin.Instance.SetNewPe(_requestedBurnTime, TargetPeR_m + _activeVessel.Orbit.referenceBody.radius, -0.5);
                    else
                        FPStatus.Error($"Unable to set Pe above current Ap");
                    break;
                case ManeuverType.newAp:// Working
                    if (TargetApR_m > Orbit.PeriapsisArl)
                        _pass = FlightPlanPlugin.Instance.SetNewAp(_requestedBurnTime, TargetApR_m + _activeVessel.Orbit.referenceBody.radius, -0.5);
                    else
                        FPStatus.Error($"Unable to set Ap below current Pe");
                    break;
                case ManeuverType.newPeAp:// Working
                    _pass = FlightPlanPlugin.Instance.Ellipticize(_requestedBurnTime, TargetApR_m + _activeVessel.Orbit.referenceBody.radius, TargetPeR_m + _activeVessel.Orbit.referenceBody.radius, -0.5);
                    break;
                case ManeuverType.newInc:// Working
                    double.TryParse(NewIncValueOSM.value, out double stupidInc);
                    FlightPlanPlugin.Logger.LogInfo($"MakeNode: _targetInc = {TargetInc_deg}, stupidInc = {stupidInc}");
                    _pass = FlightPlanPlugin.Instance.SetInclination(_requestedBurnTime, TargetInc_deg, -0.5); // FpUiController.TargetInc_deg
                    break;
                case ManeuverType.newLAN: // Untested
                    _pass = FlightPlanPlugin.Instance.SetNewLAN(_requestedBurnTime, TargetLAN_deg, -0.5); // FpUiController.TargetLAN_deg
                    break;
                case ManeuverType.newNodeLon: // Untested
                    _pass = FlightPlanPlugin.Instance.SetNodeLongitude(_requestedBurnTime, TargetLAN_deg, -0.5);
                    break;
                case ManeuverType.newSMA: // Working
                    _pass = FlightPlanPlugin.Instance.SetNewSMA(_requestedBurnTime, TargetSMA_m + _activeVessel.Orbit.referenceBody.radius, -0.5);
                    break;
                case ManeuverType.matchPlane: // Working
                    _pass = FlightPlanPlugin.Instance.MatchPlanes(TimeRef, -0.5);
                    break;
                case ManeuverType.hohmannXfer: // Works if we start in a good enough orbit (reasonably circular, close to target's orbital plane)
                    _pass = FlightPlanPlugin.Instance.HohmannTransfer(_requestedBurnTime, -0.5);
                    break;
                case ManeuverType.interceptTgt: // Experimental
                    _pass = FlightPlanPlugin.Instance.InterceptTgt(_requestedBurnTime, TargetInterceptTime_s, -0.5); // FpUiController.InterceptTime
                    break;
                case ManeuverType.courseCorrection: // Experimental Works at least some times...
                    if (target.IsCelestialBody)
                    {
                        _pass = FlightPlanPlugin.Instance.CourseCorrection(_requestedBurnTime, TargetInterceptDistanceCelestial_m, -0.5); // FpUiController.InterceptDistanceCelestial
                    }
                    else
                    {
                        _pass = FlightPlanPlugin.Instance.CourseCorrection(_requestedBurnTime, TargetInterceptDistanceVessel_m, -0.5); // FpUiController.InterceptDistanceVessel
                    }
                    break;
                case ManeuverType.moonReturn: // Working
                    _pass = FlightPlanPlugin.Instance.MoonReturn(_requestedBurnTime, TargetMRPeR_m + _activeVessel.Orbit.referenceBody.Orbit.referenceBody.radius, -0.5);
                    break;
                case ManeuverType.matchVelocity: // Working
                    _pass = FlightPlanPlugin.Instance.MatchVelocity(_requestedBurnTime, -0.5);
                    break;
                case ManeuverType.planetaryXfer: // Mostly working, but you'll probably need to tweak the departure and also need a course correction
                    _pass = FlightPlanPlugin.Instance.PlanetaryXfer(_requestedBurnTime, -0.5);
                    break;
                case ManeuverType.advancedPlanetaryXfer:
                    // The legacy release only ran the body below in DEBUG builds; the shipped code path
                    // was the error alone. That body was `op.MakeNodes(...)` + `NodeManagerPlugin` node
                    // creation, neither of which exists on 0.2.8.5, so the manoeuvre is a labelled drop
                    // (D9/D10): still selectable, refuses with a readable message, creates nothing.
                    FPStatus.Error($"Advanced Planetary Transfer is not available in this port. Sorry!");
                    break;
                case ManeuverType.fixAp: // Working
                    _pass = FlightPlanPlugin.Instance.SetNewAp(_requestedBurnTime, Ap2, -0.5);
                    break;
                case ManeuverType.fixPe: // Working
                    _pass = FlightPlanPlugin.Instance.SetNewPe(_requestedBurnTime, Pe2, -0.5);
                    break;
                default: break;
            }

            if (_pass)
                CheckNodeQuality();

            // The `_autoLaunchMNC` tail (`FPOtherModsInterface.instance.CallMNC()`) is deleted with MNC (D7):
            // node creation now ends at `CheckNodeQuality()`, and launching K2-D2 is the Execute button's job.

        }

        private void K2D2()
        {
            // One entry point, two meanings: with a K2-D2 run idle it starts the burn, and while one
            // is live the same click is treated as Stop (see `FPOtherModsInterface.CallK2D2`).
            // Polling is *not* started here - `Update()` and the plugin's own update drive it.
            FlightPlanPlugin.Logger.LogDebug($"K2D2: UITK button pressed");
            FPOtherModsInterface.instance.CallK2D2();
        }

        private void ToggleButtonBackgroundColor(Button thisButton, bool buttonState)
        {
            // Routed through the change-detection helper (see ToggleButtonTextColor).
            if (buttonState)
            {
                // This changes the text color
                // thisButton.style.color = Color.green;
                // This changes the background color (looks like button is highlighted as when pressed)
                SetBackgroundColor(thisButton, Color.green);
            }
            else
            {
                // This changes the text color
                // thisButton.style.color = buttonBackgroundColor;
                // UI-fix increment 3: the legacy wrote the captured StyleColor struct itself - an
                // unset StyleColor is a no-op at the slot, so USS styling showed through. Writing
                // `.value` instead coerced it to a concrete transparent and hid the button
                // (the launch-9 regression). The StyleColor overload restores the legacy no-op.
                SetBackgroundColorStyle(thisButton, buttonBackgroundColor);
            }
        }

        private void ToggleButtonTextColor(Button thisButton, bool buttonState)
        {
            // Routed through the change-detection helpers: `SetTab` can be re-entered from `Update()`
            // (the tab guards call `SetTab(Tab.OSM)`), and the legacy rewrote 5 style slots per button
            // per call.
            if (buttonState)
            {
                // This changes the text color
                SetTextColor(thisButton, Color.green);
                SetBorderColor(thisButton, Color.green);
                // This changes the background color (looks like button is highlighted as when pressed)
                // thisButton.style.backgroundColor = Color.green;
            }
            else
            {
                // This changes the text color
                // UI-fix increment 3: StyleColor overloads - the legacy wrote the captured structs
                // (unset = no-op, USS shows through); `.value` coerced them to a concrete
                // transparent/black inline override and made unselected buttons invisible until
                // clicked (the launch-9 regression). Selected stays concrete green.
                SetTextColorStyle(thisButton, buttonTextColor);
                SetBorderColorStyle(thisButton, buttonBorderColor);
                // This changes the background color (looks like button is not hovered or pressed)
                // thisButton.style.backgroundColor = buttonBackgroundColor;
            }
        }

        private void SetTab(Tab thisTab)
        {
            // If this click is actually chaning the tab
            if (currentTabNum != thisTab)
                UnsetToggles();
            FlightPlanPlugin.Logger.LogDebug($"SetTab: thisTab = {thisTab}, panel = {panelNames[(int)thisTab]}");
            for (int i = 0; i < panels.Count; i++)
            {
                if (i == (int)thisTab)
                {
                    ToggleButtonTextColor(TabButtons[i], true);
                    SetDisplay(panels[i], DisplayStyle.Flex);
                    SetText(PanelLabel, panelNames[i]);
                    FlightPlanPlugin.Logger.LogDebug($"SetTab: {panelNames[i]} switchd on");
                }
                else
                {
                    ToggleButtonTextColor(TabButtons[i], false);
                    SetDisplay(panels[i], DisplayStyle.None);
                    FlightPlanPlugin.Logger.LogDebug($"SetTab: {panelNames[i]} switchd off");
                }
            }
            currentTabNum = thisTab;
        }

        public List<TimeReference> Options = new List<TimeReference>();

        public string SetOptionsList(ManeuverType type)
        {
            Options.Clear();

            VesselComponent ActiveVessel = FlightPlanPlugin.Instance._activeVessel;

            string ManeuverTypeDesc = "";

            switch (type)
            {
                case ManeuverType.None:
                    ManeuverTypeDesc = "None";
                    break;
                case ManeuverType.circularize:
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS); // "at Next Apoapsis"
                    Options.Add(TimeReference.PERIAPSIS);    // "at Next Periapsis"
                    Options.Add(TimeReference.ALTITUDE);     // "at An Altittude"
                    Options.Add(TimeReference.X_FROM_NOW);   // "after Fixed Time"

                    ManeuverTypeDesc = "Circularizing";
                    break;
                case ManeuverType.newPe:
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS); // "at Next Apoapsis"
                    Options.Add(TimeReference.PERIAPSIS);    // "at Next Periapsis"
                    Options.Add(TimeReference.X_FROM_NOW);   // "after Fixed Time"
                    Options.Add(TimeReference.ALTITUDE);     // "at An Altittude"

                    ManeuverTypeDesc = "Setting new Pe";
                    break;
                case ManeuverType.newAp:
                    Options.Add(TimeReference.PERIAPSIS);    // "at Next Periapsis"
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS); // "at Next Apoapsis"
                    Options.Add(TimeReference.X_FROM_NOW);   // "after Fixed Time"
                    Options.Add(TimeReference.ALTITUDE);     // "at An Altittude"

                    ManeuverTypeDesc = "Setting new Ap";
                    break;
                case ManeuverType.newPeAp:
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS);    // "at Next Apoapsis"
                    Options.Add(TimeReference.PERIAPSIS);       // "at Next Periapsis"
                    Options.Add(TimeReference.X_FROM_NOW);      // "after Fixed Time"
                    Options.Add(TimeReference.ALTITUDE);        // "at An Altittude"
                    Options.Add(TimeReference.EQ_ASCENDING);    // "at Equatorial AN"
                    Options.Add(TimeReference.EQ_DESCENDING);   // "at Equatorial DN"

                    ManeuverTypeDesc = "Elipticizing";
                    break;
                case ManeuverType.newInc:
                    Options.Add(TimeReference.EQ_HIGHEST_AD); // "at Cheapest eq AN/DN"
                    Options.Add(TimeReference.EQ_NEAREST_AD); // "at Nearest eq AN/DN"
                    Options.Add(TimeReference.EQ_ASCENDING);  // "at Equatorial AN"
                    Options.Add(TimeReference.EQ_DESCENDING); // "at Equatorial DN"
                    Options.Add(TimeReference.X_FROM_NOW);    // "after Fixed Time"
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS);  // "at Next Apoapsis"

                    ManeuverTypeDesc = "Setting new inclination";
                    break;
                case ManeuverType.newLAN:
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS); // "at Next Apoapsis"
                    Options.Add(TimeReference.PERIAPSIS);    // "at Next Periapsis"
                    Options.Add(TimeReference.X_FROM_NOW);   // "after Fixed Time"

                    ManeuverTypeDesc = "Setting new LAN";
                    break;
                case ManeuverType.newNodeLon:
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS); // "at Next Apoapsis"
                    Options.Add(TimeReference.PERIAPSIS);    // "at Next Periapsis"
                    Options.Add(TimeReference.X_FROM_NOW);   // "after Fixed Time"

                    ManeuverTypeDesc = "Shifting Node LongitudeN";
                    break;
                case ManeuverType.newSMA:
                    if (ActiveVessel.Orbit.eccentricity < 1)
                        Options.Add(TimeReference.APOAPSIS); // "at Next Apoapsis"
                    Options.Add(TimeReference.PERIAPSIS);    // "at Next Periapsis"
                    Options.Add(TimeReference.X_FROM_NOW);   // "after Fixed Time"

                    ManeuverTypeDesc = "Setting new SMA";
                    break;
                case ManeuverType.matchPlane:
                    Options.Add(TimeReference.REL_HIGHEST_AD); // "at Cheapest AN/DN With Target"
                    Options.Add(TimeReference.REL_NEAREST_AD); // "at Nearest AN/DN With Target"
                    Options.Add(TimeReference.REL_ASCENDING);  // "at Next AN With Target"
                    Options.Add(TimeReference.REL_DESCENDING); // "at Next DN With Target"

                    ManeuverTypeDesc = "Matching planes";
                    break;
                case ManeuverType.hohmannXfer:
                    Options.Add(TimeReference.COMPUTED); // "at Optimal Time"

                    ManeuverTypeDesc = "Performing Hohmann transfer";
                    break;
                case ManeuverType.courseCorrection:
                    Options.Add(TimeReference.COMPUTED); // "at Optimal Time"

                    ManeuverTypeDesc = "Performaing course correction";
                    break;
                case ManeuverType.interceptTgt:
                    Options.Add(TimeReference.X_FROM_NOW); // "after Fixed Time"

                    ManeuverTypeDesc = "Intercepting";
                    break;
                case ManeuverType.matchVelocity:
                    Options.Add(TimeReference.CLOSEST_APPROACH); // "at Closest Approach"
                    Options.Add(TimeReference.X_FROM_NOW);       // "after Fixed Time"

                    ManeuverTypeDesc = "Matching velocity";
                    break;
                case ManeuverType.moonReturn:
                    Options.Add(TimeReference.COMPUTED); // "at Optimal Time"

                    ManeuverTypeDesc = "Performaing moon return";
                    break;
                case ManeuverType.planetaryXfer:
                    Options.Add(TimeReference.NEXT_WINDOW); // "at the next transfer window"
                    Options.Add(TimeReference.ASAP);        // "as soon as possible"

                    ManeuverTypeDesc = "Performing planetary transfer";
                    break;
                case ManeuverType.advancedPlanetaryXfer:
                    Options.Add(TimeReference.PORKCHOP);     // "Porkchop Selection"
                    Options.Add(TimeReference.LIMITED_TIME); // "Limited Time"

                    ManeuverTypeDesc = "Performing advanced planetary transfer";
                    break;
                case ManeuverType.fixAp:
                    Options.Add(TimeReference.PERIAPSIS);  // "at Next Periapsis"
                    Options.Add(TimeReference.X_FROM_NOW); // "after Fixed Time"
                    TimeRef = TimeReference.PERIAPSIS;     // Set the default

                    ManeuverTypeDesc = "Setting new Ap";
                    break;
                case ManeuverType.fixPe:
                    Options.Add(TimeReference.APOAPSIS);   // "at Next Apoapsis"
                    Options.Add(TimeReference.X_FROM_NOW); // "after Fixed Time"
                    TimeRef = TimeReference.APOAPSIS;      // Set the default

                    ManeuverTypeDesc = "Setting new Pe";
                    break;
                default:
                    break;
            }

            if (Options.Count < 1)
                Options.Add(TimeReference.None);

            if (!Options.Contains(TimeRef))
            {
                TimeRef = Options[0];
            }
            _burnTimeOption = BurnTimeOption.TextTimeRef[TimeRef];

            List<string> optionStrings = new();
            for (int i = 0; i < Options.Count; i++)
                optionStrings.Add(BurnTimeOption.TextTimeRef[Options[i]]);

            BurnOptionsDropdown.choices = optionStrings;
            BurnOptionsDropdown.value = _burnTimeOption; // BurnOptionsDropdown.choices.FindIndex(a => a.Contains(thisOption));

            return ManeuverTypeDesc;
        }

        private bool selecting = false;
        // private Vector2 scrollPosition;
        private static List<VesselComponent> allVessels;
        private static List<PartComponent> allPorts;
        private static List<CelestialBodyComponent> allBodies;
        private static List<string> targets;
        private static Dictionary<string, VesselComponent> targetVessels;
        private static Dictionary<string, PartComponent> targetPorts;
        private static Dictionary<string, CelestialBodyComponent> targetBodies = new();
        private static SimulationObjectModel tgtVessel = null;
        public static bool SelectTarget, doNewList;
        public static bool SelectDockingPort = false;
        public static CelestialBodyComponent Kerbol;

        private void ListBodies()
        {
            CelestialBodyComponent _rootBody = _activeVessel.mainBody;
            while (_rootBody.referenceBody != null)
            {
                _rootBody = _rootBody.referenceBody;
            }
            Kerbol = _rootBody;
            // allBodies.Clear();
            if (targetBodies != null)
                targetBodies.Clear();
            ListSubBodies(_rootBody, 0);
        }

        private void ListSubBodies(CelestialBodyComponent body, int level)
        {
            foreach (CelestialBodyComponent sub in body.orbitingBodies)
            {
                string tabs = new string(' ', level * 2);
                // allBodies.Add(sub);
                targetBodies.Add(tabs + sub.Name, sub);
                ListSubBodies(sub, level + 1);
            }
        }

        private void ListVessels()
        {
            // Update the active vessel
            _activeVessel = Game?.ViewController?.GetActiveVehicle(true)?.GetSimVessel(true);
            _currentTarget = _activeVessel?.TargetObject;
            if (SelectPortToggle.value)
            {
                // If no target, bail out. We can't select a docing port unless there's either a vessel or port targeted.
                if (_currentTarget == null)
                {
                    selecting = false;
                    return;
                }

                // If we don't have a list we need one
                if (allPorts == null || allPorts.Count < 1)
                    doNewList = true;

                // If the current target is a vessel
                if (_currentTarget.IsVessel)
                {
                    // If we don't have a local copy of the vessel
                    if (tgtVessel == null)
                    {
                        tgtVessel = _currentTarget;
                        doNewList = true;
                    }

                    // If this is a different vessel, then update the local copy
                    if (tgtVessel.GlobalId != _currentTarget.GlobalId)
                    {
                        tgtVessel = _currentTarget;
                        doNewList = true;
                    }

                    // If we've not made a list for this vessel or we need a new list
                    if (doNewList)
                    {
                        allPorts = tgtVessel.PartOwner.Parts.ToList();
                        allPorts.RemoveAll(part => !part.IsPartDockingPort(out _));
                        targetPorts = allPorts.ToDictionary(x => x.Name, x => x);
                    }
                }
                else if (_currentTarget.IsPart)// We've got a part selected
                {
                    // If we don't have a local copy of the vessel
                    if (tgtVessel == null)
                    {
                        tgtVessel = _currentTarget.Part.PartOwner.SimulationObject;
                        doNewList = true;
                    }

                    // If this is a different vessel, then update the local copy
                    if (tgtVessel.GlobalId != _currentTarget.Part.PartOwner.SimulationObject.GlobalId)
                    {
                        tgtVessel = _currentTarget;
                        doNewList = true;
                    }

                    // If we've not made a list for this vessel or we need a new list
                    if (doNewList)
                    {
                        allPorts = tgtVessel.PartOwner.Parts.ToList();
                        allPorts.RemoveAll(part => !part.IsPartDockingPort(out _));
                        targetPorts = allPorts.ToDictionary(x => x.Name, x => x);
                    }
                }
                //else if (allPorts == null)
                //{
                //    // Rebuild the last list of ports
                //    // Plugin._activeVessel.SetTargetByID(thisVessel.GlobalId);
                //    // Plugin._currentTarget = Plugin._activeVessel.TargetObject;
                //    allPorts = thisVessel.PartOwner.Parts.ToList();
                //    allPorts.RemoveAll(part => !part.IsPartDockingPort(out _));
                //}
                //else if (allPorts.Count > 0)
                //{
                //    // Jump back to the last list of ports
                //    // Plugin._activeVessel.SetTargetByID(thisVessel.GlobalId);
                //    // Plugin._currentTarget = Plugin._activeVessel.TargetObject;
                //}
                else
                {
                    selecting = false;
                    return;
                }
            }
            else
            {
                // Make a list of all vessels other than this one
                allVessels = Game.SpaceSimulation.UniverseModel.GetAllVessels();
                targetVessels = allVessels.Where(v => !v.IsDebris() && v.GlobalId != _activeVessel.GlobalId).ToDictionary(x => x.Name, x => x);
            }

            //if ((SelectPortToggle.value && allPorts.Count < 1) || (!SelectPortToggle.value && allVessels.Count < 1))
            //{
            //  selecting = false;
            //}
            //else
            //{
            //  if (SelectPortToggle.value)
            //  {
            //    targets = allPorts.Select(i => i.Name).ToList();
            //    //foreach (PartComponent part in allPorts)
            //    //{
            //    //  // GUILayout.BeginHorizontal();
            //    //  if (UI_Tools.ListButton(part.Name))
            //    //  {
            //    //    selecting = false;
            //    //    _activeVessel.SetTargetByID(part.GlobalId);
            //    //    _currentTarget = _activeVessel.TargetObject;
            //    //  }

            //    //  // GUILayout.EndHorizontal();
            //    //}

            //  }
            //  else
            //  {
            //    targets = allVessels.Select(i => i.Name).ToList();
            //    //foreach (VesselComponent vessel in allVessels)
            //    //{
            //    //  // GUILayout.BeginHorizontal();
            //    //  if (UI_Tools.ListButton(vessel.Name))
            //    //  {
            //    //    selecting = false;
            //    //    _activeVessel.SetTargetByID(vessel.GlobalId);
            //    //    _currentTarget = _activeVessel.TargetObject;
            //    //  }

            //    //  // GUILayout.EndHorizontal();
            //    //}
            //  }
            //}
        }

        public ManeuverType ManeuverType = ManeuverType.None;

        public static TimeReference TimeRef = TimeReference.None;

        //public static string ManeuverTypeDesc;
        public static string ManeuverDescription;
        public static string ManeuverTypeDesc = "";

        public void SetManeuverType(ManeuverType type)
        {
            ManeuverType = type;
            ManeuverTypeDesc = SetOptionsList(type);
        }

        public void CheckNodeQuality()
        {
            if (ManeuverType == ManeuverType.None)
                return;

            BurnTimeOption.Instance.SetBurnTime();
            double _requestedBurnTime = BurnTimeOption.RequestedBurnTime;

            OrbiterComponent Orbiter = FPUtility.ActiveVessel.Orbiter;
            ManeuverPlanSolver maneuverPlanSolver = Orbiter?.ManeuverPlanSolver;

            bool _pass = false;
            double pError = 0;
            double thisEcc, nextEcc, targetEcc;
            double thisPe, nextPe, errorPe;
            double thisAp, nextAp, errorAp;
            double thisInc, nextInc;
            double thisSMA, nextSMA;
            double thisLAN, nextLAN;
            double thisCA, thisCATime, nextCA, nextCATime;
            int patchIdx;
            Vector3d tgtVel, thisVel, nextVel;
            IKeplerPatch vesselOrbit = FlightPlanPlugin.Instance._activeVessel.Orbit;
            var target = FlightPlanPlugin.Instance._currentTarget;
            double UT = Game.UniverseModel.UniverseTime;
            IKeplerOrbit targetOrbit = null;
            if (target != null)
                targetOrbit = target.Orbit;

            List<PatchedConicsOrbit> PatchedConicsList = maneuverPlanSolver.PatchedConicsList;
            switch (ManeuverType)
            {
                case ManeuverType.circularize: // Working
                    thisEcc = vesselOrbit.eccentricity;
                    nextEcc = PatchedConicsList[0].eccentricity;
                    //nextPe = PatchedConicsList[0].Periapsis;
                    //nextAp = PatchedConicsList[0].Apoapsis;
                    //errorPe = Math.Abs(TargetPeR_km - nextPe);
                    //errorAp = Math.Abs(TargetApR_km - nextAp);
                    pError = nextEcc; // thisEcc;
                    if (pError >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Eccentricity 0, got {nextEcc:N3}, off by {pError * 100:N3}%");
                    else if (pError >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Eccentricity 0, got {nextEcc:N3}, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Eccentricity 0, got {nextEcc:N3}, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.newPe: // Working
                    thisPe = vesselOrbit.Apoapsis;
                    nextPe = PatchedConicsList[0].Periapsis;
                    pError = (nextPe - TargetPeR_m) / (TargetPeR_m - thisPe);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Periapsis {(TargetPeR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) > 0.01)
                        FPStatus.Warning($"Warning: Requested Periapsis {(TargetPeR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Periapsis {(TargetPeR_km - ReferenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.newAp:// Working
                    thisAp = vesselOrbit.Apoapsis;
                    nextAp = PatchedConicsList[0].Apoapsis;
                    pError = (nextAp - TargetApR_m) / (TargetApR_m - thisAp);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Apoapsis {(TargetApR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextAp - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Apoapsis {(TargetApR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextAp - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Apoapsis {(TargetApR_km - ReferenceBody.radius) / 1000:N1} km, got {(nextAp - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.newPeAp:// Working
                    thisEcc = vesselOrbit.eccentricity;
                    nextEcc = PatchedConicsList[0].eccentricity;
                    targetEcc = (TargetApR_m - TargetPeR_m) / (TargetApR_m + TargetPeR_m);
                    nextPe = PatchedConicsList[0].Periapsis;
                    nextAp = PatchedConicsList[0].Apoapsis;
                    errorPe = Math.Abs(TargetPeR_m - nextPe);
                    errorAp = Math.Abs(TargetApR_m - nextAp);
                    pError = (nextEcc - targetEcc) / (targetEcc - thisEcc);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Eccentricity {targetEcc:N3}, got {nextEcc:N3}, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Eccentricity {targetEcc:N3}, got {nextEcc:N3}, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Eccentricity {targetEcc:N3}, got {nextEcc:N3}, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.newInc:// Working
                    thisInc = vesselOrbit.inclination;
                    nextInc = PatchedConicsList[0].inclination;
                    pError = (nextInc - TargetInc_deg) / (TargetInc_deg - thisInc);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Inclination {TargetInc_deg:N1}°, got {nextInc:N1}°, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Inclination {TargetInc_deg:N1}°, got {nextInc:N1}°, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Inclination {FPSettings.TargetInc_deg:N1}°, got {nextInc:N1}°, off by {pError*100:N3}%");
                    break;
                case ManeuverType.newLAN: // Untested
                    thisLAN = vesselOrbit.longitudeOfAscendingNode;
                    nextLAN = PatchedConicsList[0].longitudeOfAscendingNode;
                    pError = (nextLAN - TargetLAN_deg) / (TargetLAN_deg - thisLAN);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested LAN {TargetLAN_deg:N1}°, got {nextLAN:N1}°, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested LAN {TargetLAN_deg:N1}°, got {nextLAN:N1}°, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested LAN {FPSettings.TargetLAN_deg:N1}°, got {nextLAN:N1}°, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.newNodeLon: // Untested
                    thisLAN = vesselOrbit.longitudeOfAscendingNode;
                    nextLAN = PatchedConicsList[0].longitudeOfAscendingNode;
                    pError = (nextLAN - TargetNodeLong_deg) / (TargetNodeLong_deg - thisLAN);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested LAN {TargetNodeLong_deg:N1}°, got {nextLAN:N1}°, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested LAN {TargetNodeLong_deg:N1}°, got {nextLAN:N1}°, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested LAN {FPSettings.TargetLAN_deg:N1}°, got {nextLAN:N1}°, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.newSMA: // Working
                    thisSMA = vesselOrbit.semiMajorAxis;
                    nextSMA = PatchedConicsList[0].semiMajorAxis;
                    pError = (nextSMA - TargetSMA_m) / (TargetSMA_m - thisSMA);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested SMA {TargetSMA_m / 1000:N1} km, got {nextSMA / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested SMA {TargetSMA_m / 1000:N1} km, got {nextSMA / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested SMA {TargetSMA / 1000:N1} km, got {nextSMA / 1000:N1} km, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.matchPlane: // Working
                    thisInc = vesselOrbit.inclination;
                    nextInc = PatchedConicsList[0].inclination;
                    pError = (nextInc - targetOrbit.inclination) / (targetOrbit.inclination - thisInc);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Inclination {TargetInc_deg:N1}°, got {nextInc:N1}°, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Inclination {TargetInc_deg:N1}°, got {nextInc:N1}°, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable Results: Requested Inclination {FPSettings.TargetInc_deg:N1}°, got {nextInc:N1}°, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.hohmannXfer: // Works if we start in a good enough orbit (reasonably circular, close to target's orbital plane)
                    if (target.IsCelestialBody)
                    {
                        if (_requestedBurnTime < 0)
                            _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                        patchIdx = -1;
                        nextCATime = PatchedConicsList[0].NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                        nextCA = (PatchedConicsList[0].GetTruePositionAtUT(nextCATime).localPosition - targetOrbit.GetTruePositionAtUT(nextCATime).localPosition).magnitude;
                        // Find the patch with the closest new closest approach
                        for (int i = 0; i < PatchedConicsList.Count; i++)
                        {
                            if (PatchedConicsList[i].referenceBody.Name == _currentTarget.Name)
                            {
                                nextCA = PatchedConicsList[i].PeriapsisArl;
                                nextCATime = PatchedConicsList[i].TimeToPe;
                                patchIdx = i;
                                break;
                            }
                        }
                        if (patchIdx < 0)
                            FlightPlanPlugin.Logger.LogInfo($"Hohmann transfer fails to intercept {target.Name}'s SOI");
                        else
                            FlightPlanPlugin.Logger.LogInfo($"Obtained Pe of {nextCA / 1000:N3} km at {FPUtility.SecondsToTimeString(nextCATime)} from now on patch {patchIdx}");
                        // FlightPlanPlugin.Logger.LogInfo($"Found closest approach Pe {_newPeValue / 1000:N3} km on patch {patchIdx}");
                        pError = (nextCA - TargetInterceptDistanceCelestial_m) / (TargetInterceptDistanceCelestial_m);
                        if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                            FPStatus.Error($"Warning: Requested arrival Pe of {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                        else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                            FPStatus.Warning($"Warning: Requested arrival Pe of {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                        //else
                        //    FPStatus.Ok($"Acceptable: Requested arrival Pe of {TargetInterceptDistanceCelestial_km:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    }
                    else
                    {
                        if (_requestedBurnTime < 0)
                            _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                        nextCATime = PatchedConicsList[0].NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                        nextCA = (PatchedConicsList[0].GetTruePositionAtUT(nextCATime).localPosition - targetOrbit.GetTruePositionAtUT(nextCATime).localPosition).magnitude;
                        pError = (nextCA - TargetInterceptDistanceVessel_m) / (TargetInterceptDistanceVessel_m);
                        if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                            FPStatus.Error($"Warning: Requested Intercept {TargetInterceptDistanceVessel_m:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                        else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                            FPStatus.Warning($"Warning: Requested Intercept {TargetInterceptDistanceVessel_m:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                        //else
                        //    FPStatus.Ok($"Acceptable: Requested Intercept {FPSettings.InterceptDistanceVessel:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                    }
                    break;
                case ManeuverType.interceptTgt: // Experimental
                    if (_requestedBurnTime < 0)
                        _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                    //thisCATime = vesselOrbit.NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                    //thisCA = (vesselOrbit.GetTruePositionAtUT(thisCATime).localPosition - targetOrbit.GetTruePositionAtUT(thisCATime).localPosition).magnitude;
                    //FlightPlanPlugin.Logger.LogInfo($"Started with closest approach of {thisCA / 1000:N3} km at {FPUtility.SecondsToTimeString(thisCATime - UT)} from now.");

                    nextCATime = PatchedConicsList[0].NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                    nextCA = (PatchedConicsList[0].GetTruePositionAtUT(nextCATime).localPosition - targetOrbit.GetTruePositionAtUT(nextCATime).localPosition).magnitude;
                    pError = (nextCA - TargetInterceptDistanceVessel_m) / (TargetInterceptDistanceVessel_m);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Intercept {TargetInterceptDistanceVessel_m:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Intercept {TargetInterceptDistanceVessel_m:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Intercept {FPSettings.InterceptDistanceVessel:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.courseCorrection: // Experimental Works at least some times...
                    if (target.IsCelestialBody)
                    {
                        if (_requestedBurnTime < 0)
                            _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                        thisCATime = vesselOrbit.NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                        thisCA = (vesselOrbit.GetTruePositionAtUT(thisCATime).localPosition - targetOrbit.GetTruePositionAtUT(thisCATime).localPosition).magnitude;
                        FlightPlanPlugin.Logger.LogInfo($"Started with closest approach of {thisCA / 1000:N3} km at {FPUtility.SecondsToTimeString(thisCATime - UT)} from now.");

                        patchIdx = -1;
                        nextCATime = PatchedConicsList[0].NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                        nextCA = (PatchedConicsList[0].GetTruePositionAtUT(nextCATime).localPosition - targetOrbit.GetTruePositionAtUT(nextCATime).localPosition).magnitude;
                        // Find the patch with the closest new closest approach
                        for (int i = 0; i < PatchedConicsList.Count; i++)
                        {
                            if (PatchedConicsList[i].referenceBody.Name == _currentTarget.Name)
                            {
                                nextCA = PatchedConicsList[i].PeriapsisArl;
                                nextCATime = PatchedConicsList[i].TimeToPe;
                                patchIdx = i;
                                break;
                            }
                        }
                        if (patchIdx < 0)
                            FlightPlanPlugin.Logger.LogInfo($"Course correction fails to intercept {target.Name}'s SOI");
                        else
                            FlightPlanPlugin.Logger.LogInfo($"Obtained Pe of {nextCA / 1000:N3} km at {FPUtility.SecondsToTimeString(nextCATime - UT)} from now on patch {patchIdx}");
                        // FlightPlanPlugin.Logger.LogInfo($"Found closest approach Pe {_newPeValue / 1000:N3} km on patch {patchIdx}");
                        pError = (nextCA - TargetInterceptDistanceCelestial_m) / (TargetInterceptDistanceCelestial_m);
                        if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                            FPStatus.Error($"Warning: Requested Intercept {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                        else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                            FPStatus.Warning($"Warning: Requested Intercept {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                        //else
                        //    FPStatus.Ok($"Acceptable: Requested Intercept {TargetInterceptDistanceCelestial_km:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    }
                    else
                    {
                        if (_requestedBurnTime < 0)
                            _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                        thisCATime = vesselOrbit.NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                        thisCA = (vesselOrbit.GetTruePositionAtUT(thisCATime).localPosition - targetOrbit.GetTruePositionAtUT(thisCATime).localPosition).magnitude;
                        FlightPlanPlugin.Logger.LogInfo($"Started with closest approach of {thisCA / 1000:N3} km at {FPUtility.SecondsToTimeString(thisCATime - UT)} from now.");

                        nextCATime = PatchedConicsList[0].NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                        nextCA = (PatchedConicsList[0].GetTruePositionAtUT(nextCATime).localPosition - targetOrbit.GetTruePositionAtUT(nextCATime).localPosition).magnitude;
                        pError = (nextCA - TargetInterceptDistanceVessel_m) / (TargetInterceptDistanceVessel_m);
                        if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                            FPStatus.Error($"Warning: Requested Intercept {TargetInterceptDistanceVessel_m:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                        else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                            FPStatus.Warning($"Warning: Requested Intercept {TargetInterceptDistanceVessel_m:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                        //else
                        //    FPStatus.Ok($"Acceptable: Requested Intercept {TargetInterceptDistanceVessel_m:N1} m, got {nextCA:N1} m, off by {pError * 100:N3}%");
                    }
                    break;
                case ManeuverType.moonReturn: // Working
                    if (_requestedBurnTime < 0)
                        _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                    nextPe = PatchedConicsList[1].Periapsis;
                    pError = (nextPe - TargetMRPeR_m) / (TargetMRPeR_m - ReferenceBody.referenceBody.radius);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Periapsis {(TargetMRPeR_m - ReferenceBody.referenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.referenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Periapsis {(TargetMRPeR_m - ReferenceBody.referenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.referenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Periapsis {(TargetMRPeR - ReferenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.matchVelocity: // Working
                    tgtVel = targetOrbit.WorldOrbitalVelocityAtUT(_requestedBurnTime);
                    thisVel = vesselOrbit.WorldOrbitalVelocityAtUT(_requestedBurnTime);
                    nextVel = PatchedConicsList[0].WorldOrbitalVelocityAtUT(_requestedBurnTime);
                    pError = (nextVel - tgtVel).magnitude / (tgtVel - thisVel).magnitude;
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Velocity {tgtVel.magnitude:N1} m/s, got {nextVel.magnitude:N1} m/s, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Velocity {tgtVel.magnitude:N1} m/s, got {nextVel.magnitude:N1} m/s, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Periapsis {tgtVel.magnitude:N1} m/s, got {nextVel.magnitude:N1} m/s, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.planetaryXfer: // Mostly working, but you'll probably need to tweak the departure and also need a course correction
                    if (_requestedBurnTime < 0)
                        _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                    patchIdx = -1;
                    nextCATime = PatchedConicsList[0].NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                    nextCA = (PatchedConicsList[0].GetTruePositionAtUT(nextCATime).localPosition - targetOrbit.GetTruePositionAtUT(nextCATime).localPosition).magnitude;
                    // Find the patch with the closest new closest approach
                    for (int i = 0; i < PatchedConicsList.Count; i++)
                    {
                        if (PatchedConicsList[i].referenceBody.Name == FlightPlanPlugin.Instance._currentTarget.Name)
                        {
                            nextCA = PatchedConicsList[i].PeriapsisArl;
                            nextCATime = PatchedConicsList[i].TimeToPe;
                            patchIdx = i;
                            break;
                        }
                    }
                    if (patchIdx < 0)
                        FlightPlanPlugin.Logger.LogInfo($"Course correction fails to intercept {target.Name}'s SOI");
                    else
                        FlightPlanPlugin.Logger.LogInfo($"Obtained Pe of {nextCA / 1000:N3} km at {FPUtility.SecondsToTimeString(nextCATime - UT)} from now on patch {patchIdx}");
                    // FlightPlanPlugin.Logger.LogInfo($"Found closest approach Pe {_newPeValue / 1000:N3} km on patch {patchIdx}");
                    pError = (nextCA - TargetInterceptDistanceCelestial_m) / (TargetInterceptDistanceCelestial_m);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Intercept {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Intercept {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Intercept {TargetInterceptDistanceCelestial_km:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.advancedPlanetaryXfer: // Mostly working, but you'll probably need to tweak the departure and also need a course correction
                    if (_requestedBurnTime < 0)
                        _requestedBurnTime = FlightPlanPlugin.Instance._currentNode.Time;
                    patchIdx = -1;
                    nextCATime = PatchedConicsList[0].NextClosestApproachTime(targetOrbit, _requestedBurnTime);
                    nextCA = (PatchedConicsList[0].GetTruePositionAtUT(nextCATime).localPosition - targetOrbit.GetTruePositionAtUT(nextCATime).localPosition).magnitude;
                    // Find the patch with the closest new closest approach
                    for (int i = 0; i < PatchedConicsList.Count; i++)
                    {
                        if (PatchedConicsList[i].referenceBody.Name == FlightPlanPlugin.Instance._currentTarget.Name)
                        {
                            nextCA = PatchedConicsList[i].PeriapsisArl;
                            nextCATime = PatchedConicsList[i].TimeToPe;
                            patchIdx = i;
                            break;
                        }
                    }
                    if (patchIdx < 0)
                        FlightPlanPlugin.Logger.LogInfo($"Course correction fails to intercept {target.Name}'s SOI");
                    else
                        FlightPlanPlugin.Logger.LogInfo($"Obtained Pe of {nextCA / 1000:N3} km at {FPUtility.SecondsToTimeString(nextCATime - UT)} from now on patch {patchIdx}");
                    // FlightPlanPlugin.Logger.LogInfo($"Found closest approach Pe {_newPeValue / 1000:N3} km on patch {patchIdx}");
                    pError = (nextCA - TargetInterceptDistanceCelestial_m) / (TargetInterceptDistanceCelestial_m);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Intercept {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Intercept {TargetInterceptDistanceCelestial_m / 1000:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Intercept {TargetInterceptDistanceCelestial_km:N1} km, got {nextCA / 1000:N1} km, off by {pError * 100:N3}%");
                    break;
                case ManeuverType.fixAp: // Working
                    thisAp = vesselOrbit.Apoapsis;
                    nextAp = PatchedConicsList[0].Apoapsis;
                    pError = (nextAp - TargetApR_m) / (TargetApR_m - thisAp);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Apoapsis {(TargetApR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextAp - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Apoapsis {(TargetApR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextAp - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Apoapsis {(TargetApR_km - ReferenceBody.radius) / 1000:N1} km, got {(nextAp - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");

                    break;
                case ManeuverType.fixPe: // Working
                    thisPe = vesselOrbit.Apoapsis;
                    nextPe = PatchedConicsList[0].Periapsis;
                    pError = (nextPe - TargetPeR_m) / (TargetPeR_m - thisPe);
                    if (Math.Abs(pError) >= FlightPlanPlugin.Instance._largeError.Value / 100)
                        FPStatus.Error($"Warning: Requested Periapsis {(TargetPeR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    else if (Math.Abs(pError) >= FlightPlanPlugin.Instance._smallError.Value / 100)
                        FPStatus.Warning($"Warning: Requested Periapsis {(TargetPeR_m - ReferenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");
                    //else
                    //    FPStatus.Ok($"Acceptable: Requested Periapsis {(TargetPeR_km - ReferenceBody.radius) / 1000:N1} km, got {(nextPe - ReferenceBody.radius) / 1000:N1} km, off by {pError * 100:N3}%");

                    break;
            }

            // The `_autoLaunchMNC` tail is deleted with MNC (D7) - see MakeNode. `_pass` is this
            // method's own verdict; without a reader the compiler flags it, so it is logged instead of
            // silently dropped (no behaviour change: MakeNode has already logged the same outcome).
            if (!_pass)
            {
                FlightPlanPlugin.Logger.LogDebug($"CheckNodeQuality: {selectedManeuver} did not meet its target");
            }
        }

    }
}
