# Third-party notices — Flight Plan (Redux port)

This file records the licences of every third-party component in the Flight Plan source tree and in
the shipped payload, and reproduces the notices that must be retained. It is the companion to
`Deploy/obj/licence-audit.md`, which quotes the evidence for each claim (file path + raw header).

**Summary of the licence position:** Flight Plan itself is **GPL-3.0**. Everything it borrows is
either permissive (MechJebLib's SPDX disjunction, NodeManager's MIT), or **GPL-family and therefore
compatible** (alglib GPL-2.0-or-later, MechJeb2 GPL-3.0). **There is no licence conflict in this
mod.** The only gap is documentary: the port plan's original licence row omitted the
MechJeb2-inherited files entirely; they are included below.

| Component | Files in this mod | Licence | Where that was read |
|---|---|---|---|
| Flight Plan (this work) | 29 files, `Assets/FlightPlan/Code/**` | **GPL-3.0** | `mods-outdated/FlightPlan/license.md:1-11` (the file also carries the full GPL-3.0 text, 636 lines) |
| alglib 3.19.0 | 13 files, `Code/Utilities/MuMech/alglib/**` | **GPL-2.0-or-later** | header of every file, e.g. `alglib/alglibinternal.cs:1-12` |
| MechJebLib | 24 files, `Code/Utilities/MuMech/MechJebLib/**` (20 carry the SPDX line) | **LicenseRef-PD-hp OR Unlicense OR CC0-1.0 OR 0BSD OR MIT-0 OR MIT OR LGPL-2.1+** | `MechJebLib/Functions/Astro.cs:1-4` |
| MechJeb2-derived | 5 files, `Code/Utilities/MuMech/{OrbitExtensions,MuUtils,MathExtensions}.cs`, `MechJeb2/OrbitalManeuverCalculator.cs`, `MechJebLibBindings/MathExtensions.cs` | **GPL-3.0** (inherited; the header names no licence) | header `OrbitExtensions.cs:1-6` + upstream MechJeb2 `LICENSE.md` |
| NodeManager-derived / -vendored | the MuMech layer and the node-service logic (`Code/Managers/FlightPlanNodeService.cs`) | **MIT** (© 2023-2024 schlosrat) | `mods-outdated/NodeManager/LICENSE:1-3` |
| Liberation Sans | 4 faces, `Assets/FlightPlan/UI/Fonts/LiberationSans-*.ttf` → 4 packed `FontAsset`s | **SIL OFL 1.1** | the font binaries' own name tables |
| Orbitron | 7 faces, `Assets/FlightPlan/UI/Fonts/Orbitron-*.ttf` → 7 packed `FontAsset`s | **SIL OFL 1.1, with Reserved Font Name "Orbitron"** | the font binaries' own name tables |

---

## 1. Flight Plan — GNU GPL version 3

```text
This software is released under the GNU GPL version 3, 29 July 2007.
The complete copy of the license is attached to this document.

SUMMARY:

    FlightPlan  Copyright (C) 2023-2024
    This program comes with ABSOLUTELY NO WARRANTY!
    This is free software, and you are welcome to redistribute it
    under certain conditions, as outlined in the full content of
    the GNU General Public License (GNU GPL), version 3, revision
    date 29 June 2007.
```

The complete GPL-3.0 text is carried verbatim in `mods-outdated/FlightPlan/license.md` (the licence
of the original mod, retained by this port). Upstream: <https://github.com/schlosrat/FlightPlan>.

## 2. NodeManager-derived files — MIT (notice retained verbatim)

The MuMech layer (`OrbitExtensions`, `MuUtils`, `MathExtensions`, the `V3`/`M3`/`Q3` primitives,
the frame route) and the node-service logic absorbed into `Code/Managers/FlightPlanNodeService.cs`
originate in **NodeManager** and are used under its MIT licence. The notice, verbatim from
`mods-outdated/NodeManager/LICENSE`:

```text
MIT License

Copyright (c) 2023-2024 schlosrat

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 3. alglib — GPL-2.0-or-later

**13 of 13** files under `Assets/FlightPlan/Code/Utilities/MuMech/alglib/` open with this header,
verbatim from `alglib/alglibinternal.cs`:

```text
/*************************************************************************
ALGLIB 3.19.0 (source code generated 2022-06-07)
Copyright (c) Sergey Bochkanov (ALGLIB project).

>>> SOURCE LICENSE >>>
This program is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation (www.fsf.org); either version 2 of the
License, or (at your option) any later version.
```

GPL-2.0**-or-later** may be combined with GPL-3.0 code because the "or later" option permits
upgrading — no conflict with §1. Full text: <https://www.gnu.org/licenses/old-licenses/gpl-2.0.html>.
alglib project: <https://www.alglib.net/>.

## 4. MechJebLib — permissive SPDX disjunction

**20 of 24** MechJebLib files carry this header, verbatim from `MechJebLib/Functions/Astro.cs`:

```text
/*
 * Copyright Lamont Granquist, Sebastien Gaggini and the MechJeb contributors
 * SPDX-License-Identifier: LicenseRef-PD-hp OR Unlicense OR CC0-1.0 OR 0BSD OR MIT-0 OR MIT OR LGPL-2.1+
 */
```

The `OR` is a **disjunction**: a redistributor elects any one of the listed licences. This release
relies on the permissive options (`MIT`/`MIT-0`/`0BSD`/`CC0-1.0`/`Unlicense`/`PD-hp`), all of which
are compatible with the mod's GPL-3.0. Nothing in MechJebLib is copyleft-only.

**Recorded because it is not uniform:** four MechJebLib files carry **no header at all** —
`Maneuvers/Simple.cs`, `Maneuvers/TwoImpulseTransfer.cs`, `Utils/MechJebLibException.cs` and
`Rootfinding/Newton.cs`. They are MechJebLib files by origin and namespace, so the project's licence
governs them; the SPDX line's absence is a documentation wart, not a different grant.

## 5. MechJeb2-derived files — GPL-3.0 (inherited)

Five files are MechJeb2 derivatives (≈3,250 lines — the largest third-party block in the mod). Their
header names no licence and defers to the originating project, verbatim from
`OrbitExtensions.cs`:

```text
/*
 * This Software was obtained from the MechJeb2 project (https://github.com/MuMech/MechJeb2) on 3/25/23
 * and was further modified as needed for compatibility with KSP2 and/or for incorporation into the
 * FlightPlan project (https://github.com/schlosrat/FlightPlan)
 *
 * This work is relaesed under the same license(s) inherited from the originating version.
 */
```

*(The misspelling "relaesed" is in the vendored source and is reproduced here for fidelity.)*

The originating project, `MuMech/MechJeb2`, is licensed **GPL-3.0**:

```text
This software is released under the GNU GPL version 3, 29 July 2007.
…
 MechJeb2 Copyright (C) 2013
```

GPL-3.0 + GPL-3.0 is compatible, so these files ship as-is; the notice obligation is discharged by
this file and by the GPL-3.0 text that accompanies Flight Plan.

## 6. Fonts — SIL Open Font License 1.1

The mod ships **11 packed font assets**: Liberation Sans (Regular, Bold, Italic, Bold-Italic) and
Orbitron (Black, Bold, ExtraBold, Medium, Regular, SemiBold, Variable). Both families are licensed
under the SIL Open Font License, Version 1.1, per the licence strings in the font binaries' own name
tables. Orbitron carries a Reserved Font Name: **"Orbitron"**.

The fonts are shipped **unmodified** — the same `.ttf` glyph data, repacked as TextCore distance-field
assets for the UI bundle — so the RFN restriction does not apply to anything distributed here.

The licence text below is the canonical OFL 1.1 from <https://openfontlicense.org>:

```text
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply to any
document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may include
source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting, or
substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical writer or
other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining a copy
of the Font Software, to use, study, copy, merge, embed, modify, redistribute,
and sell modified and unmodified copies of the Font Software, subject to the
following conditions:

1) Neither the Font Software nor any of its individual components, in
Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or in
the appropriate machine-readable metadata fields within text or binary
files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any Modified
Version, except to acknowledge the contribution(s) of the Copyright
Holder(s) and the Author(s) or with their explicit written permission.

5) The Font Software, modified or unmodified, in part or in whole, must be
distributed entirely under this license, and must not be distributed under
any other license. The requirement for fonts to remain under this license
does not apply to any document created using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are not
met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM OTHER DEALINGS
IN THE FONT SOFTWARE.
```

Copyright holders (from the font binaries): the **Liberation fonts** project (Ascender/Red Hat,
OFL 1.1) and **The Orbitron Project Authors**, © 2018
(<https://github.com/theleagueof/orbitron>), with Reserved Font Name "Orbitron".

*Recorded gap:* neither font ships a licence file anywhere in the legacy tree or this project — the
OFL text above is supplied here because the originals do not carry it in-repo.

## 7. What is **not** distributed

* **KSP 2 / Redux / SpaceWarp2 / UitkForKsp2 assemblies** — the mod compiles against the game's own
  runtime and ships **none** of it (the archive contains exactly one DLL, the mod's own).
* **`Tools/unity-bundle/uitkforksp2.controls/`** — a *build-time-only* package for the throwaway
  bundle project (its own licence, and a non-commercial EULA) is **never packed**: the bundle audit
  finds all 39 container assets under `assets/flightplan/**` and zero third-party theme content.
* **`Assets/FlightPlan/Copied/`** — empty; nothing from the legacy's copied-assets folder is
  deployed (`Deploy/obj/port-code-delta.md` §1).

## 8. Where the full licence texts live

| Licence | Text |
|---|---|
| GPL-3.0 (this mod, and MechJeb2-derived files) | `mods-outdated/FlightPlan/license.md` (verbatim, 636 lines) · <https://www.gnu.org/licenses/gpl-3.0.html> |
| GPL-2.0-or-later (alglib) | <https://www.gnu.org/licenses/old-licenses/gpl-2.0.html> |
| MIT (NodeManager) | §2 above (verbatim) · `mods-outdated/NodeManager/LICENSE` |
| MechJebLib SPDX | §4 above (verbatim) |
| SIL OFL 1.1 (fonts) | §6 above (verbatim) · <https://openfontlicense.org> |

**Packaging note (decision recorded, not taken):** the release archive is **payload-only** — three
files, matching the deploy tree it must be byte-identical to. All notices live here, in the
repository, beside the mod's own GPL-3.0 text. If the zip is ever published as a standalone download,
ship this file and the GPL-3.0 text alongside (or inside) it; that changes the archive's byte-identity
claim, which is why it was not done unilaterally during the port. Full reasoning:
`Deploy/obj/licence-audit.md` §7.
