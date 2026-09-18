# Changelog

All notable changes to Flight Plan (the KSP 2 Redux port). Versions are the mod's own; the pinned
game generation is named on each entry.

## 0.10.8 — 2026-09-18 — KSP 2 Redux 0.2.9.0.104521 (26w36c)

* Retargeted the mod to the `0.2.9.0.104521` pin (Unity `6000.5.8f1`): the project manifest and
  `ProjectVersion` moved to the `26w36a`/`6000.5.8f1` generation, and the 35 API-delta sites across
  six source files were re-routed against the installed 220-assembly runtime — **no code was
  reverted or stubbed**, and the re-resolved call sites (the `PanelRenderer` window root via
  `GetWindowRoot`, the Redux localization helper, `GetOrbitalSpeedAtDistance`, the `IKeplerPatch`
  orbit typing) are the ones this pin actually exposes.
* Rebuilt the UI bundle in-project at Unity `6000.5.8f1` (bundle `sver=23`) and fixed the K2-D2
  status row so it renders on the status row beside the status text.
* Fixed two in-game defects found by launch 17: the node-placement NRE (`BL-1` — a placement
  pre-check plus half-added-node cleanup in both create passes) and the refusal spam (`NL-2` — the
  deliberate refusal now logs as a warning, not an error), plus the bounded 60 s status-line pin
  (`DOC-2`).
* Certified in game over three launches (17 smoke, 18 checklist, 19 final) on 2026-09-18: the final
  run was the user's **ALL PASS**, with zero FlightPlan-attributed `[ERR ]` lines, a clean node
  create, and the P11 bundle proven at its loaded path.
* Release `FlightPlan-0.10.8.zip` cut from the deployed-and-certified triple; offline verification
  (`redux-check` and the C# build) reported 0 findings / exit 0 before the deploy.
* Recorded environment findings rather than papering over them: the post-shutdown native crash that
  ends launches 18 and 19 is **not** FlightPlan-attributed, and the never-exercised refusal/cleanup
  arm is carried as a coverage gap (see the README's Known limitations).

## 0.10.7 — 2026-09-15 — KSP 2 Redux 0.2.8.5.103184

* Ported the BepInEx / SpaceWarp 1.x-era Flight Plan to KSP 2 Redux `0.2.8.5.103184`
  (Unity `6000.4.1f1`) as a new Redux project, and **absorbed Node Manager** — its orbit math and
  node create/edit/delete/refresh logic live inside the mod, so there is no Node Manager to install.
* Shipped the core node engine (create, edit, delete, refresh, the 9-node cap refusal — proven in
  game with zero exceptions) behind the mod's first empty-load and UI milestones.
* Shipped the UI Toolkit window (`Window.Create`, AppBar button, keybinds, status line, the six
  maneuver tabs) and refined it across the UI-fix increments; a save reload was validated before
  close-out.
* Added the optional, reflection-based **K2-D2 Execute path** (discovery, preconditions, burn
  evidence), validated in game with a real burn flown.
* Rebuilt the release archive from the launch-16 validated artefacts after the last UI increment.
* Licence position recorded: GPL-3.0 (the original mod's licence, retained) with the third-party
  notices in `THIRD-PARTY-NOTICES.md`.

## Upstream lineage

Flight Plan itself is a port and fork of the KSP 1 mod
[schlosrat/FlightPlan](https://github.com/schlosrat/FlightPlan) — **GPL-3.0**, and the original
mod's licence is retained (see `LICENSE.md`).
