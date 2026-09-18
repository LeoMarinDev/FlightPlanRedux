# Flight Plan — for KSP 2 Redux

An in-game **maneuver planning tool** for Kerbal Space Program 2 (Redux). Flight Plan gives you one
window for building transfer, circularization and course-correction burns: it computes the burn,
creates the game's maneuver node for you, and — if [K2-D2](https://spacedock.info/mod/3325) is
installed — can fly the node it just made.

This is the **Redux port** of the original SpaceWarp/BepInEx mod, retargeted at
**KSP 2 Redux `0.2.9.0.104521`** (snapshot `26w36c`). The NodeManager dependency is **absorbed** —
there is no Node Manager to install, and nothing else has to be installed beyond Redux itself.

> **Status of this build — honest notes.** Validated in-game at this pin across launches 17–19: the
> core node engine (create, edit, delete, refresh, the 9-node cap, zero FlightPlan-attributed
> exceptions in the certified runs), the window (open, render, drag, ESC suppress/restore, scene
> coverage) and the K2-D2 Execute path (a real burn flown, with burn evidence). The final run was a
> full **ALL PASS**, and the build is certified over those three launches (2026-09-18); the
> run-by-run evidence stays in the private working repository.

## Lineage

This is a port of the KSP 1 mod **[schlosrat/FlightPlan](https://github.com/schlosrat/FlightPlan)**
(GPL-3.0 — the original mod's licence is retained). The release history is in
[`CHANGELOG.md`](CHANGELOG.md); third-party components and their notices are in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Compatibility

| | |
|---|---|
| Game | KSP 2 Redux **`0.2.9.0.104521`** (snapshot `26w36c`) — the pin. Newer Redux is untested and unsupported |
| Unity | `6000.5.8f1` (the Redux `0.2.9.0` player generation) |
| Runtime | Windows player under **Steam + Proton** on Linux (all validation runs); native Windows is expected to work but untested here |
| Mod loader | Redux's integrated loader — **no BepInEx**, no `BepInEx/plugins/` |
| Required dependency | `SpaceWarp2 >= 2.0.0` (ships with Redux; declared in `swinfo.json`) |
| Optional | **K2-D2** — enables the Execute button. Discovered at runtime; Flight Plan loads and works without it |

`mod_id`: `FlightPlan` · version `0.10.8` · assembly `FlightPlan.dll`

## Installation

1. Make sure you have KSP 2 Redux `0.2.9.0.104521` installed and that it starts.
2. Unzip **`FlightPlan-0.10.8.zip`** into your game folder — the archive's top folder is already
   `FlightPlan/`, so unzip it into `…/Kerbal Space Program 2/mods/` (lowercase — that is the folder
   the game reads).
3. You should end up with exactly this:

```text
Kerbal Space Program 2/
└── mods/
    └── FlightPlan/
        ├── FlightPlan.dll
        ├── swinfo.json
        └── assets/
            └── bundles/
                └── flightplan_ui.bundle
```

The bundle path is load-bearing: `assets/bundles/flightplan_ui.bundle` must stay beside the DLL.
**To uninstall:** delete the `mods/FlightPlan/` folder (and, if you want a clean slate, the
`FlightPlan-config.json` inside it).

## How to use it

Open the window from the **Flight Plan** button on the app bar (flight view), or with the keybind —
**`Alt+P`** by default, with a second binding on **right-Alt/AltGr+P**. The window is available in
the flight and map views; it hides itself in other scenes.

The workflow is the classic one:

1. **Pick a tab.** All six tabs are always present; what adapts to your situation is the maneuver
   types and burn-time options offered inside them (a hyperbolic orbit is not offered "at
   Apoapsis", for example):

   | Tab | What it plans |
   |---|---|
   | Ownship Maneuvers | Circularize · New Pe · New Ap · New Pe & Ap · New Inclination · New LAN · New SMA |
   | Target Relative Maneuvers: Vessel | Match Planes · Match Velocity · Course Correction · Intercept (vessel to vessel) |
   | Target Relative Maneuvers: Celestial | Same set, relative to a celestial target |
   | Orbital Transfer Maneuvers: Moon | Hohmann transfer to a moon · Return From Moon |
   | Orbital Transfer Maneuvers: Planet | Interplanetary transfer, synodic period / phase angle / transfer time readouts |
   | Resonant Orbit Maneuvers | Resonant-orbit / constellation deploy (period, Ap, Pe, eccentricity, injection Δv readouts) |

2. **Click a maneuver type.** The burn-time option list is populated for you (options that make no
   sense in your current orbit are not offered) and defaults to the last valid choice.
3. **Adjust the inputs** to the right of the maneuver button if you want something other than the
   default (desired Pe/Ap in km, inclination in degrees, etc.).
4. **Press Make Node.** The status line tells you what happened: the computed burn, or the reason it
   was refused (a target is missing, the plan is full, the burn is unsafe, …).
5. **Optional — press the K2-D2 button** to have K2-D2 fly the node. This button only appears when
   K2-D2 is loaded *and* the active vessel has at least one node.

The status line's colour is meaningful: **green** = OK, **yellow** = warning, **red** = the action was
refused (the text says why). The colour fades out after a short time.

### The node plan and the 9-node cap

Flight Plan edits the game's own maneuver plan, so whatever you make shows up in the stock node UI.
The game supports **at most 9 nodes**; attempting a 10th makes the game's own solver throw every
frame. Flight Plan therefore **refuses to create a 10th node** and says so. Delete a node (or let
K2-D2 consume one) before adding another.

### K2-D2 integration (optional)

* The Execute path asks K2-D2 to fly **the earliest node in the plan**, and it reports K2-D2's own
  status line back into the window.
* Because Flight Plan can create a node at any burn time, a new node is not necessarily the earliest
  one. If the selected node is **not** the earliest, the request is refused **by name** — press
  K2-D2's own button in that case, or delete the earlier node.
* The integration is deliberately **reflection-based and optional**: no assembly dependency, no
  `swinfo.json` entry, and if K2-D2 is absent, everything else keeps working.
* A debug switch (`Force K2-D2 Unavailable`) lets you exercise the "K2-D2 is not installed" path
  without uninstalling anything.

## Configuration

Redux writes the config to `mods/FlightPlan/FlightPlan-config.json`. Edit it with the game closed;
it is read at startup.

| Section | Entry | Default | Meaning |
|---|---|---|---|
| Keybindings | First Keybind | `Alt+P` | toggles the window |
| Keybindings | Second Keybind | right-Alt/AltGr+P | second toggle binding |
| Experimental Section | Experimental Features | `false` | shows the experimental **Advanced Interplanetary Transfer** group in the Planet tab (the group whose plot is the logged drop described below) |
| Status Reporting Section | Small % Error Threashold | `1.0` | below this the burn is reported as accurate |
| Status Reporting Section | Large % Error Threashold | `2.0` | above this the burn is reported as off |
| Debug Section | Self-Driving Node Probe | `false` | arms the single-node probe (debug) |
| Debug Section | Multi-Node Probe | `false` | arms the multi-node probe (debug) |
| Debug Section | Force K2-D2 Unavailable | `false` | makes the Execute path behave as if K2-D2 were absent |

The Debug Section switches exist for the port's in-game verification; leave them off in normal play
(the probes create and delete nodes on your live vessel).

## Differences from the original (SpaceWarp 1.x / BepInEx) mod

* **Loader and layout** — Redux's integrated loader and `mods/FlightPlan/`; no BepInEx, no
  `BepInEx/plugins/flight_plan/`.
* **Node Manager is absorbed.** Its orbit math and node create/edit/delete/refresh logic live inside
  Flight Plan now; the separate mod is neither required nor used.
* **Maneuver Node Controller is not ported.** The MNC button is unreachable in this build (the code
  path is removed; the element is kept for layout but always hidden and unwired). Fine-tuning a node
  is the game's own node editor's job for now.
* **The advanced-interplanetary (porkchop) plot is not available.** The tab, its buttons and its
  computed readouts (synodic period, phase angle, transfer time) still work; asking for the
  `LIMITED_TIME`/`PORKCHOP` plot logs a one-line refusal by name instead of failing silently.
* **The UI ships as a prebuilt AssetBundle** (`assets/bundles/flightplan_ui.bundle`) — the Redux
  helper that loads UXML by address resolves against the *game's* Addressables catalogue, which a mod
  cannot write to.
* **Node creation is capped at 9** with an explicit refusal, per the game's own limit.
* Older features with no counterpart in the ported design (the MNC auto-launch tail, the legacy
  `OperationAdvancedTransfer` path) were **dropped and documented**, not stubbed.

## Known limitations

* **The 60 s status-pin expiry is player-observed, not log-proven.** The pin and its supersede are
  evidence-backed; the row clearing itself rests on the user's in-game pass — the runs that tested
  it ended before the window could elapse.
* **The refusal path is certified in the shipped artefact, not flown.** No burn has been refused
  since the fix that made refusals a warning instead of an error, so that arm has never run live.
* **Scene coverage is what the runs reached** — a save load into flight, VAB → flight,
  flight ↔ map, the KSC buildings and the ESC menu. A revert-to-launch has not been a deliberate test.
* The **Hohmann/return-from-moon** two-burn path shares one plan with the cap logic; it is built but
  its interaction with the 9-node cap deserves a look in play.
* The window is **flight/map only** by design; it hides elsewhere rather than drawing over the KSC.

## Licence

Flight Plan is licensed **GPL-3.0** (the licence of the original mod is retained; the full text is
[`LICENSE.md`](LICENSE.md)). Third-party components and their notices — alglib (GPL-2.0-or-later),
MechJebLib (permissive SPDX disjunction), NodeManager-derived files (MIT), MechJeb2-derived files
(GPL-3.0), and the bundled Liberation Sans and Orbitron fonts (SIL OFL 1.1) — are listed in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

Upstream project: <https://github.com/schlosrat/FlightPlan>
Maintained for KSP 2 Redux by **LeoMarinDev**.

## Building it yourself

This repository is the Redux `0.2.9.0` Unity mod project itself. Build the C# with `Tools/build.sh`
(it compiles against the managed assemblies of your installed KSP 2 Redux and stages the
deployable), then build the UI bundle with the **in-project Unity route** at the required editor
`6000.5.8f1` (`-executeMethod FlightPlan.EditorTools.BuildFlightPlanUIBundle.Build`), or with
`Tools/build-ui-bundle.sh` (kept as the documented fallback; it defaults to the same editor
generation — pass `UNITY=/path/to/Editor/Unity` if yours lives elsewhere). Then deploy the three
files: `FlightPlan.dll`, `swinfo.json` and `assets/bundles/flightplan_ui.bundle`.

Note that the C# build re-stages the deployable's `assets/` tree, so **build the bundle after the
code**. The package manifest is `Packages/manifest.json`; the template generation is pinned in
`template.version`.
