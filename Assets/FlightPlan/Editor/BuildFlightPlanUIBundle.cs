// Rebuilds flightplan_ui.bundle INSIDE the real project, with the required Unity 6000.5.8f1.
//
// Run headless:
//   "$HOME/Unity/Hub/Editor/6000.5.8f1/Editor/Unity" -batchmode -nographics -quit \
//       -projectPath <the project> \
//       -executeMethod FlightPlan.EditorTools.BuildFlightPlanUIBundle.Build -logFile <log>
// or from the editor menu:  FlightPlan > Rebuild UI Bundle.
//
// This replaces the throwaway-project route (Tools/build-ui-bundle.sh, kept as the documented
// fallback). Four things are deliberate here, and each one is a measured trap from this repo:
//
//  1. THE EXPLICIT `AssetBundleBuild` OVERLOAD, NOT `.meta` MARKING. This is what makes Unity
//     actually emit the bundle's `AssetBundle` object (class 142) - the container map from
//     "assets/..." paths to objects inside the bundle. Bundles built purely by stamping the
//     importer's assetBundleName came out WITHOUT that object, while every bundle that loads in
//     this installation contains it (Tools/unity-bundle/BuildFlightPlanUIBundle.cs, measured).
//     It also means no `.meta` in the project is touched: the asset list is explicit, reviewable
//     and nothing is left marked after the build.
//
//  2. THE BUILD TARGET IS PINNED TO StandaloneWindows (5), NOT TAKEN FROM
//     EditorUserBuildSettings.activeBuildTarget. That property would resolve to the HOST
//     platform - StandaloneLinux64 on this box - and the player refuses a bundle built for
//     another platform ("...not built with the right build target..."). Every functional bundle
//     in the installed mod tree that this project's own route produced records
//     StandaloneWindows; the K2-D2 in-project route records StandaloneWindows64; both are
//     accepted by the Windows player under Proton, so the pin is the *project's own* accepted
//     value (Deploy/obj/p5-bundle.md, format table).
//
//  3. BuildAssetBundleOptions.ForceRebuildAssetBundle, NOT ChunkBasedCompression.
//     `ChunkBasedCompression` emits LZ4HC data blocks and silently omits the class-142
//     container object; the player then rejects the bundle as incompatible. Measured across all
//     77 bundles in this repo: every one carries data-block compression 4 (an LZMA-class codec),
//     including the accepted flightplan_ui.bundle and the shipped k2d2_ui.bundle. So
//     `ForceRebuildAssetBundle` (which carries no compression bits) is the option that both
//     forces the write and keeps the accepted codec.
//
//  4. THE OUTPUT LANDS IN THE PROJECT'S OWN PAYLOAD TREE, Assets/FlightPlan/Copied/assets/,
//     not straight into Deploy/. `Tools/build.sh` re-assembles Deploy/FlightPlan/assets/ from
//     that tree (`cp -a Assets/FlightPlan/Copied/assets "$OUT_DIR/assets"`) and deletes the
//     previous staging first, so the bundle has to live here to survive a build - which is also
//     the shape the proven K2-D2 tree uses (Assets/K2D2/Copied/assets/bundles/k2d2_ui.bundle,
//     tracked, byte-identical to its deployed copy).
//
// The asset set is the same one the fallback builder packs, and it is enumerated rather than
// inferred: every VisualTreeAsset/StyleSheet under Assets/FlightPlan/UI, plus every Texture2D,
// TextCore/TMP FontAsset and raw Font under it. The runtime resolves the page and its fonts by
// exact path/name from inside the bundle, so all of them have to travel.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightPlan.EditorTools
{
    public static class BuildFlightPlanUIBundle
    {
        private const string BundleName = "flightplan_ui.bundle";
        private const string OutputDir = "Assets/FlightPlan/Copied/assets/bundles";
        private const string SourceDir = "Assets/FlightPlan/UI";

        // The runtime resolves the root page by this exact path inside the bundle.
        private const string RootPage = SourceDir + "/FP_UI.uxml";

        [MenuItem("FlightPlan/Rebuild UI Bundle")]
        public static void Rebuild()
        {
            Build();
        }

        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RootPage) == null)
            {
                Debug.LogError($"[bundle] result: FAILED - root page {RootPage} is not in this " +
                               "project, so the window loader would look up a null VisualTreeAsset.");
                EditorApplication.Exit(1);
                return;
            }

            var assets = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:VisualTreeAsset t:StyleSheet", new[] { SourceDir }))
            {
                assets.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            // Images and fonts are referenced from the stylesheets and the pages, not by the
            // finder above, so they are collected by type as well. t:FontAsset covers the
            // TextCore/TMP SDF assets the pages style with; t:Font covers a raw ttf if one ships
            // alongside (the png/psd textures come in through t:Texture2D).
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D t:FontAsset t:Font", new[] { SourceDir }))
            {
                assets.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            assets = new List<string>(new HashSet<string>(assets));
            assets.Sort(System.StringComparer.Ordinal);

            Debug.Log($"[bundle] packing {assets.Count} asset(s) into '{BundleName}':");
            foreach (string asset in assets)
            {
                Debug.Log($"[bundle]   {asset}");
            }

            if (assets.IndexOf(RootPage) < 0)
            {
                Debug.LogError($"[bundle] result: FAILED - the root page {RootPage} is not in the " +
                               "collected asset set.");
                EditorApplication.Exit(1);
                return;
            }

            string targetName = System.Environment.GetEnvironmentVariable("FLIGHTPLAN_BUNDLE_TARGET");
            BuildTarget target = string.IsNullOrEmpty(targetName)
                ? BuildTarget.StandaloneWindows
                : (BuildTarget)System.Enum.Parse(typeof(BuildTarget), targetName);
            Debug.Log($"[bundle] building for {target} ({(int)target}) with "
                      + $"{BuildAssetBundleOptions.ForceRebuildAssetBundle}");

            Directory.CreateDirectory(OutputDir);

            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetBundleVariant = "",
                assetNames = assets.ToArray(),
                addressableNames = assets.ToArray(),
            };

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                OutputDir,
                new[] { build },
                BuildAssetBundleOptions.ForceRebuildAssetBundle,
                target);

            if (manifest == null)
            {
                Debug.LogError("[bundle] result: FAILED - BuildAssetBundles returned null; the " +
                               "actual error is in the console above this line.");
                EditorApplication.Exit(1);
                return;
            }

            string built = Path.Combine(OutputDir, BundleName);
            if (!File.Exists(built))
            {
                Debug.LogError($"[bundle] result: FAILED - expected output missing: {built}");
                EditorApplication.Exit(1);
                return;
            }

            // The bundle writes a `.manifest` text file and a `bundles` index for OutputDir next
            // to it; neither is read at runtime (the mod loads flightplan_ui.bundle by name), and
            // both are carried along by Tools/build.sh like the payload tree they sit in.
            Debug.Log($"[bundle] result: OK {built} ({new FileInfo(built).Length} bytes)");
        }
    }
}
