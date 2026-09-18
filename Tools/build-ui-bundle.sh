#!/usr/bin/env bash
#
# Rebuilds flightplan_ui.bundle from the UI sources in this repo, and drops the result into
# Deploy/FlightPlan/assets/bundles/ ready to deploy. At the 0.2.9.0.104521 pin this is the
# DOCUMENTED FALLBACK: the primary route is the in-project Unity build (Assets/FlightPlan/Editor);
# this script is kept for when the full project cannot be opened.
#
# WHY THIS EXISTS
# ---------------
# A bundle built by an editor newer than the player's is rejected outright. Redux 0.2.9.0.104521
# ships a Unity 6000.5.8f1 player, which reads asset format SerializedFile version 23; the
# superseded 0.2.8.5.103184 player was a 6000.4.1f1 build that only read version 22. In the other
# direction, the released pre-Redux-era FlightPlan UI bundle was built for a 2022.3.5f1-era
# toolchain and did not load here at all:
#
#   Failed to load 'archive:/CAB-...'. File may be corrupted or was serialized with a newer
#   version of Unity.
#   The AssetBundle 'flightplan_ui.bundle' can't be loaded because it was not built with the right
#   version or build target.
#
# That is a serialisation-format wall, not a bad byte or a bad path, so it cannot be patched:
# the bundle has to be rebuilt by an editor of the player's generation. This script does that
# from a minimal, throwaway project instead of the full mod project, because the full project
# requires the ThunderKit toolchain and its package import is heavy.
#
# The minimal project needs no external packages at all.
#
# The root page may root on a custom control from the uitkforksp2.controls package. That package
# is not on disk in this install, so a GUID-verified asset subset is vendored under
# Tools/unity-bundle/uitkforksp2.controls (see its PROVENANCE.md), and the type is resolved from the
# managed plugin DLLs - uitkforksp2.controls.Runtime.dll plus its Addressables dependencies - staged
# from this pin's reference set (Packages/KSP2_x64, or Managed/ as the fallback). Whether the rebuilt
# UI still needs the pack at all is the P5 question PROVENANCE.md records.
#
# USAGE
#   Tools/build-ui-bundle.sh
#   UNITY=/path/to/Unity Tools/build-ui-bundle.sh
#
# The editor is selected with $UNITY, and the player the bundle must load in with $PLAYER_UNITY
# (default: 6000.5.8f1, this pin's player). The SerializedFile ceiling is derived from
# $PLAYER_UNITY rather than hard-coded, so a correct v23 bundle cannot fail its own gate.
# Everything version-dependent - ProjectVersion.txt, the com.unity.ugui package pin, and the
# post-build format assertions - is derived from the selected binary.
#
# Transplanted and de-hardcoded for FlightPlan; the origin of every file in Tools/ is recorded
# in Deploy/obj/PORT-PROGRESS.md. Every gate the original performed is kept.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

PROJECT="$REPO_ROOT/Deploy/ui-bundle-project"
OUT_BUNDLE="$REPO_ROOT/Deploy/FlightPlan/assets/bundles/flightplan_ui.bundle"

log()  { printf '\033[1;34m[bundle]\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m[bundle]\033[0m %s\n' "$*" >&2; exit 1; }

# The player generation the bundle must load in. Redux 0.2.9.0.104521 ships a Unity 6000.5.8f1
# player, which reads SerializedFile 23; the superseded 0.2.8.5.103184 player was 6000.4.1f1 and
# read only 22. The ceiling is DERIVED from this instead of hard-coded - the old `<= 22` refused a
# correct v23 bundle - and the editor default follows it, so the fallback builds for this pin's
# player unless the caller asks for another generation.
PLAYER_UNITY="${PLAYER_UNITY:-6000.5.8f1}"
case "$PLAYER_UNITY" in
    6000.5.*) MAX_SVER=23 ;;
    6000.4.*) MAX_SVER=22 ;;
    2022.3.*) MAX_SVER=22 ;;
    *) fail "unsupported PLAYER_UNITY $PLAYER_UNITY - supported: 6000.5.x (Redux 0.2.9.0), 6000.4.x (0.2.8.5), 2022.3.x" ;;
esac
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/$PLAYER_UNITY/Editor/Unity}"

[ -x "$UNITY" ] || fail "Unity editor not found or not executable: $UNITY
Set UNITY=/path/to/Editor/Unity if it lives elsewhere (PLAYER_UNITY=$PLAYER_UNITY)."
log "Unity: $("$UNITY" -version 2>/dev/null | head -1)  ($UNITY)"

# --- detect the selected editor's version (drives files and assertions below) -----------
UNITY_VERSION="$("$UNITY" -version 2>/dev/null \
    | sed -nE 's/^.*\b([0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+)\b.*$/\1/p' | head -1)"
[ -n "$UNITY_VERSION" ] || fail "could not read a Unity version from: $UNITY -version"
case "$UNITY_VERSION" in
    6000.5.*|6000.4.*|2022.3.*) ;;
    *) fail "unsupported Unity editor $UNITY_VERSION - supported: 6000.5.x (default), 6000.4.x, 2022.3.x" ;;
esac
# Revision hash for ProjectVersion.txt: the one embedded in the editor binary if it can be
# found, else the known revision for the supported versions.
if command -v strings >/dev/null 2>&1; then
    UNITY_REVISION="${UNITY_REVISION:-$(strings -a "$UNITY" 2>/dev/null \
        | sed -nE "s/^${UNITY_VERSION} \(([0-9a-f]{12})\)$/\1/p" | head -1)}"
fi
case "$UNITY_VERSION" in
    6000.5.8f1) UNITY_REVISION="${UNITY_REVISION:-5cb7df797b7d}" ;;
    6000.4.1f1) UNITY_REVISION="${UNITY_REVISION:-336a400b9ea2}" ;;
    2022.3.5f1) UNITY_REVISION="${UNITY_REVISION:-9674261d40ee}" ;;
esac

# --- sources that must exist, or the rebuild cannot be faithful -------------------------
# The UXML/USS (and the fonts/images they reference) live in the mod's asset-level UI tree.
SRC_UI_DIR="Assets/FlightPlan/UI"
# UI C# does NOT travel into the minimal project. The sibling port had to ship its control
# library because its UXML rooted on custom control types; this port's markup references only
# built-in `ui:` elements (the two `UitkForKsp2.Controls.Dropdown` sites were replaced with the
# built-in `ui:DropdownField`), so there is no custom element whose type the importer must
# resolve. Copying the C# in without the KSP assemblies fails the build outright (CS0246 on every
# KSP/ReduxLib type), which is exactly the "does this C# need to travel?" question this phase
# answers: it does not.
[ -d "$SRC_UI_DIR" ]   || fail "missing $SRC_UI_DIR"
[ -f "$SRC_UI_DIR/FP_UI.uxml" ] || fail "missing $SRC_UI_DIR/FP_UI.uxml (the root page)"

# --- uitkforksp2.controls: the package that owns UitkForKsp2.Controls.* controls ---------
# The root page may root on one of that package's controls and reference its KerbalUI.uss; the
# package is declared in Packages/manifest.json but is not present on disk anywhere in this
# install. The vendored subset carries the original .meta files, so the GUIDs the UXML/USS
# reference resolve. The runtime assembly comes from the game's own 0.2.8.5 files so its
# UxmlSerializedData attribute set matches the runtime exactly; the DLLs are never modified.
PKG_SUBSET="Tools/unity-bundle/uitkforksp2.controls"
[ -f "$PKG_SUBSET/package.json" ]            || fail "missing vendored package: $PKG_SUBSET/package.json"
[ -f "$PKG_SUBSET/Assets/Theme/KerbalUI.uss" ] || fail "missing vendored $PKG_SUBSET/Assets/Theme/KerbalUI.uss"

# --- assemble the minimal project -------------------------------------------------------
log "Creating minimal project at Deploy/ui-bundle-project ..."
rm -rf "$PROJECT"
mkdir -p "$PROJECT/Assets" "$PROJECT/Packages" "$PROJECT/ProjectSettings"

# Pin the editor to the selected version. Without this the editor treats the project as
# belonging to a different Unity and refuses to open it in batchmode. Both fields come from
# the selected binary (revision via $UNITY_REVISION if it cannot be read from the binary).
{
    printf 'm_EditorVersion: %s\n' "$UNITY_VERSION"
    if [ -n "$UNITY_REVISION" ]; then
        printf 'm_EditorVersionWithRevision: %s (%s)\n' "$UNITY_VERSION" "$UNITY_REVISION"
    fi
} > "$PROJECT/ProjectSettings/ProjectVersion.txt"

# Only built-in packages: com.unity.ugui is shipped inside the editor and provides
# TextMeshPro, which a TMP_FontAsset needs to import. Its built-in version differs by editor
# generation: 2.0.0 on Unity 6, 1.0.0 on 2022.3.
#
# com.unity.modules.assetbundle is REQUIRED. Without it, BuildPipeline builds a bundle that
# has no class-142 AssetBundle object and therefore an empty container - the 0.2.8.5 player
# rejects it with "not compatible with this newer version of the Unity runtime" (and the
# UXML lookup then null-references). The causal bisect (minimal PNG + a full asset set, both
# editors, both marking paths) is in the sibling port's Deploy/obj/bundle-fingerprints.md;
# both known-good mod projects' root manifests include this module.
case "$UNITY_VERSION" in
    2022.3.*) UGUI_VERSION="1.0.0" ;;
    *)        UGUI_VERSION="2.0.0" ;;
esac
cat > "$PROJECT/Packages/manifest.json" <<EOF
{
  "dependencies": {
    "com.unity.modules.assetbundle": "1.0.0",
    "com.unity.ugui": "$UGUI_VERSION",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0"
  }
}
EOF

# Copy the sources WITH their .meta files. The GUIDs in those .meta files are what the
# UXML/USS cross-references (project://database/...?guid=...) and the image/font references
# are resolved by, so preserving them is what keeps the rebuilt UI pointing at the same
# assets as the original. .meta files are also where the original bundle markings live.
copy_tree() {
    local src="$1" dst="$2"
    [ -e "$src" ] || { log "  skip (absent): $src"; return; }
    mkdir -p "$(dirname "$dst")"
    cp -a "$src" "$dst"
}

log "Copying UI sources ..."
copy_tree "$SRC_UI_DIR"  "$PROJECT/$SRC_UI_DIR"

# The package assets (KerbalUI.uss, its sprites and the JetBrains LED/pixel fonts) go in as an
# *embedded* package: copied into Packages/ with the original package.json and .meta files, so
# the project://database/Packages/uitkforksp2.controls/... references in the UXML/USS resolve
# without any Package Manager network access at build time.
log "Copying vendored uitkforksp2.controls package subset ..."
copy_tree "$PKG_SUBSET" "$PROJECT/Packages/uitkforksp2.controls"

# The control TYPE itself comes from the shipped runtime DLL. Without it the UXML importer
# cannot resolve a <UitkForKsp2.Controls.*> element and the root page fails to import.
log "Staging uitkforksp2.controls runtime plugins ..."
MANAGED_DIR="${FLIGHTPLAN_MANAGED:-$REPO_ROOT/Packages/KSP2_x64}"
if [ ! -f "$MANAGED_DIR/uitkforksp2.controls.Runtime.dll" ]; then
    MANAGED_DIR="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program 2/KSP2_x64_Data/Managed"
fi
for dll in uitkforksp2.controls.Runtime.dll Unity.Addressables.dll Unity.ResourceManager.dll; do
    [ -f "$MANAGED_DIR/$dll" ] || fail "missing managed plugin $MANAGED_DIR/$dll (needed to resolve UitkForKsp2.Controls types)"
done
mkdir -p "$PROJECT/Assets/Plugins"
cp -f "$MANAGED_DIR/uitkforksp2.controls.Runtime.dll" "$PROJECT/Assets/Plugins/"
cp -f "$MANAGED_DIR/Unity.Addressables.dll"           "$PROJECT/Assets/Plugins/"
cp -f "$MANAGED_DIR/Unity.ResourceManager.dll"        "$PROJECT/Assets/Plugins/"
log "  plugins from: $MANAGED_DIR"

mkdir -p "$PROJECT/Assets/Editor"
cp -f "Tools/unity-bundle/BuildFlightPlanUIBundle.cs" "$PROJECT/Assets/Editor/"
cp -f "Tools/unity-bundle/VerifyFlightPlanUxml.cs"    "$PROJECT/Assets/Editor/"
cp -f "Tools/unity-bundle/AuditFlightPlanBundle.cs"   "$PROJECT/Assets/Editor/"

log "Project contents:"
( cd "$PROJECT" && find Assets Packages -type f \( -name '*.uxml' -o -name '*.uss' -o -name '*.cs' -o -name '*.dll' \) | sort | sed 's/^/    /' )

# --- build ------------------------------------------------------------------------------
LOG_FILE="$REPO_ROOT/Deploy/obj/unity-bundle-build.log"
mkdir -p "$(dirname "$LOG_FILE")"
log "Running Unity batchmode (log: $LOG_FILE) ..."

set +e
"$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod BuildFlightPlanUIBundle.Build \
    -logFile - > "$LOG_FILE" 2>&1
UNITY_EXIT=$?
set -e

# Unity's own exit code is not trustworthy in batchmode (script compile errors still exit 0),
# so the bundle's presence *and* the absence of compiler errors are what decide success.
if grep -qE "error CS[0-9]+" "$LOG_FILE"; then
    log "C# compile errors:"
    grep -E "error CS[0-9]+" "$LOG_FILE" | sort -u | head -40 | sed 's/^/    /'
    fail "bundle project failed to compile - see $LOG_FILE"
fi

BUILT="$PROJECT/BundleOutput/flightplan_ui.bundle"
[ -f "$BUILT" ] || {
    log "Last lines of the Unity log:"
    tail -40 "$LOG_FILE" | sed 's/^/    /'
    fail "no bundle produced (Unity exit $UNITY_EXIT) - see $LOG_FILE"
}

log "Unity reported:"
grep -E "\[bundle\]" "$LOG_FILE" | sed 's/^.*\[bundle\]/[bundle]/' | sed 's/^/    /'

# --- verify it is the right format before shipping it ------------------------------------
python3 - "$BUILT" "$PLAYER_UNITY" <<'PY'
import struct, sys
path, player = sys.argv[1], sys.argv[2]
with open(path, "rb") as fh:
    head = fh.read(64)
if head[:8] != b"UnityFS\0":
    raise SystemExit("not a UnityFS bundle")
off = 8
struct.unpack_from(">I", head, off)[0]; off += 4
def cstr(buf, o):
    e = buf.index(b"\0", o); return buf[o:e].decode("utf-8", "replace"), e + 1
_, off = cstr(head, off)          # minimum player version ("5.x.x")
builder, off = cstr(head, off)    # builder revision
print(f"    builder revision : {builder}")


def generation(s):
    parts = s.split(".")
    try:
        return int(parts[0]), int(parts[1])
    except (IndexError, ValueError):
        return None


bg, pg = generation(builder), generation(player)
if bg is None:
    raise SystemExit(f"could not parse the bundle's builder revision: {builder!r}")
if bg > pg:
    raise SystemExit(f"bundle was built by {builder}, newer than the {player} player - "
                     f"a bundle from a newer editor is what the player rejects outright")
print(f"    builder <= player : {builder} <= {player}")
PY

# The inner SerializedFile version is the field that actually gates the player, and it lives in
# the decompressed directory - the outer header does not carry it. The same decompressed CAB is
# then censused for its container strings.
#
# This replaced a v22-era walk of the type table for the class-142 AssetBundle object. On a v23
# file that walk raised and printed `CHECK SKIPPED` while the build still exited 0 - a green
# script with NO container assertion at all. The census below (K2-D2's calibrated predicate) has
# no parser to skip: counting the outer (compressed) bytes reads 0 and proves nothing, so the
# count runs on the decompressed CAB, where every container-bearing bundle measured reads 1..84
# `assets/` hits and a bundle built without com.unity.modules.assetbundle reads 0. `name_hits` is
# printed for diagnosis but NOT asserted - an accepted bundle (OrbitalSurvey's swconsoleui)
# legitimately reads 0 name hits.
python3 - "$BUILT" "$MAX_SVER" "$PLAYER_UNITY" <<'PY'
import os, re, struct, sys
path, max_sver, player = sys.argv[1], int(sys.argv[2]), sys.argv[3]
sys.path.insert(0, os.path.expanduser("~/.local/lib/python3.14/site-packages"))
try:
    import UnityPy
    from UnityPy.files import BundleFile
except ImportError as exc:
    raise SystemExit(
        f"format/container verification UNAVAILABLE: UnityPy is not installed ({exc}) - "
        "refusing to ship a bundle whose format and container were not read")
cap = {}
BundleFile.read_files = lambda self, reader, files: cap.update(reader=reader, files=files)
try:
    UnityPy.load(path)
except Exception:
    pass
reader, nodes = cap.get("reader"), cap.get("files")
if reader is None or not nodes:
    raise SystemExit(
        "format/container verification UNAVAILABLE: the bundle directory could not be read")

cab = bytearray()
sver = None
for node in nodes:
    if node.path.endswith(".resS"):
        continue
    reader.Position = node.offset
    data = reader.read_bytes(node.size)
    if sver is None:
        sver = struct.unpack_from(">I", data, 8)[0]
    cab += data

if sver is None:
    raise SystemExit("no SerializedFile node found in the bundle")
print(f"    SerializedFile version : {sver}  ({player} player accepts <= {max_sver})")
if sver > max_sver:
    raise SystemExit(f"SerializedFile version {sver} is too new for the {player} player")

basename = os.path.basename(path).encode()
payload = bytes(cab)
assets_hits = len(re.findall(b"assets/", payload))
name_hits = len(re.findall(re.escape(basename), payload))
print(f"    container census       : assets_hits={assets_hits} name_hits={name_hits} "
      f"(decompressed CAB {len(payload)} B)")
if assets_hits == 0:
    raise SystemExit(
        "container census read 0 `assets/` entries - the CAB carries no container strings; a "
        "bundle built without com.unity.modules.assetbundle reads exactly this and the player "
        "rejects it as container-less")
PY

# --- render proof: instantiate every UXML page before shipping anything -----------------
# "Loads" is not "renders": a VisualTreeAsset can import cleanly and still clone with zero
# children (measured on 2022.3.5f1-built bundles). This second batchmode run
# on the same project instantiates every UXML and logs its childCount; any zero/negative is a
# hard build failure. Raw output is kept at Deploy/obj/uxml-verify.log - it is the pre-deploy
# evidence the orchestrator reads.
VERIFY_LOG="$REPO_ROOT/Deploy/obj/uxml-verify.log"
mkdir -p "$(dirname "$VERIFY_LOG")"
log "Verifying UXML instantiation (log: $VERIFY_LOG) ..."
set +e
"$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod VerifyFlightPlanUxml.Verify \
    -logFile - > "$VERIFY_LOG" 2>&1
VERIFY_EXIT=$?
set -e
if grep -qE "error CS[0-9]+" "$VERIFY_LOG"; then
    log "C# compile errors (verify run):"
    grep -E "error CS[0-9]+" "$VERIFY_LOG" | sort -u | head -20 | sed 's/^/    /'
    fail "verify project failed to compile - see $VERIFY_LOG"
fi
if ! grep -q "\[uxml-verify\] result:" "$VERIFY_LOG"; then
    log "Last lines of the verify log:"
    tail -30 "$VERIFY_LOG" | sed 's/^/    /'
    fail "UXML verification did not run to completion (Unity exit $VERIFY_EXIT) - see $VERIFY_LOG"
fi
UXML_LINES=$(grep -c "\[uxml-verify\] Assets/FlightPlan/UI/.* childCount=" "$VERIFY_LOG" || true)
UXML_ZERO=$(grep -cE "\[uxml-verify\] Assets/FlightPlan/UI/.* childCount=(0|-1|LOAD-FAILED)" "$VERIFY_LOG" || true)
log "UXML templates instantiated: $UXML_LINES  (zero/negative childCount: $UXML_ZERO)"
grep "\[uxml-verify\]" "$VERIFY_LOG" | sed 's/^.*\[uxml-verify\]/[uxml-verify]/' | sed 's/^/    /'
[ "$UXML_LINES" -gt 0 ] || fail "no UXML templates were instantiated - see $VERIFY_LOG"
[ "$UXML_ZERO" -eq 0 ]  || fail "at least one UXML template instantiated with childCount <= 0 - see $VERIFY_LOG"

# --- container + font audit, from the BUILT BYTES ---------------------------------------
# The verify pass above reads the sources out of the project, so it cannot see what survived
# the pack. This pass loads the built bundle back and lists its container, its fonts'
# material/shader/mainTexture and its stylesheets' unresolved-asset slots. It is the only
# route that reports the container keys, and a font that lost its shader or its atlas is the
# blank-window failure mode, so a NULL shader or mainTexture here is a hard build failure.
AUDIT_LOG="$REPO_ROOT/Deploy/obj/bundle-audit.log"
log "Auditing built bundle from its bytes (log: $AUDIT_LOG) ..."
set +e
FLIGHTPLAN_AUDIT_BUNDLES="$BUILT" "$UNITY" -batchmode -nographics -quit \
    -projectPath "$PROJECT" \
    -executeMethod AuditFlightPlanBundle.Run \
    -logFile - > "$AUDIT_LOG" 2>&1
AUDIT_EXIT=$?
set -e
if ! grep -q "\[audit\] done" "$AUDIT_LOG"; then
    log "Last lines of the audit log:"
    tail -30 "$AUDIT_LOG" | sed 's/^/    /'
    fail "bundle audit did not run to completion (Unity exit $AUDIT_EXIT) - see $AUDIT_LOG"
fi
grep -E "\[audit\]" "$AUDIT_LOG" | sed 's/^.*\[audit\]/[audit]/' | sed 's/^/    /'
CONTAINER_COUNT=$(sed -nE 's/^.*\[audit\]   container assets: ([0-9]+).*$/\1/p' "$AUDIT_LOG" | head -1)
[ -n "$CONTAINER_COUNT" ] || fail "the audit reported no container count - see $AUDIT_LOG"
[ "$CONTAINER_COUNT" -gt 0 ] || fail "the built bundle's container is EMPTY - the 0.2.8.5 player would reject it"
grep -q "container: assets/flightplan/ui/fp_ui.uxml" "$AUDIT_LOG" \
    || fail "the root page is not in the built bundle's container - see $AUDIT_LOG"
if grep -qE "shader=NULL|mainTex=NULL" "$AUDIT_LOG"; then
    fail "a font in the built bundle lost its shader or its main texture (blank-window cause) - see $AUDIT_LOG"
fi
[[ "$CONTAINER_COUNT" -ge 29 ]] || fail "expected at least 29 container assets (1 page + 1 sheet + 11 SDF + 15 images), got $CONTAINER_COUNT"
log "container assets in the built bundle: $CONTAINER_COUNT (root page present, no NULL shader/mainTex)"

# --- publish ----------------------------------------------------------------------------
mkdir -p "$(dirname "$OUT_BUNDLE")"
cp -f "$BUILT" "$OUT_BUNDLE"
log "Wrote $OUT_BUNDLE ($(stat -c %s "$OUT_BUNDLE") bytes)"
log ""
log "Deploy with:"
log "  cp -f \"$OUT_BUNDLE\" \"\$KSP2/mods/FlightPlan/assets/bundles/flightplan_ui.bundle\""
