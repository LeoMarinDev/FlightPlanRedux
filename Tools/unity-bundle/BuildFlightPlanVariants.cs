// Builds several flightplan_ui.bundle variants in a single batchmode run so the game can be
// asked, once, which ones the 6000.4.1f1 runtime actually accepts.
//
// Background: every bundle that loads in the installed game (MicroEngineer, OrbitalSurvey, and
// the game's own 2022.3.5f1 bundles) contains an `AssetBundle` object (class 142) with a
// populated container map, and a rebuild done the wrong way does not. The variants below isolate
// the plausible causes of that - and of the runtime's "not compatible with this newer version of
// the Unity runtime" rejection - instead of guessing at one:
//
//   v1_explicit_none      current recipe: explicit AssetBundleBuild, BuildAssetBundleOptions.None
//   v2_disable_typetree   as v1 but DisableWriteTypeTree, so the runtime uses its own type info
//                         instead of the editor-generated type tree stored in the bundle
//   v3_no_font            as v1 but without the TMP font assets. A TMP font drags a Material
//                         (class 21) and a Shader (class 48) into the bundle; neither
//                         MicroEngineer's nor OrbitalSurvey's working bundle contains those
//   v4_win64              as v1 but serialised for StandaloneWindows64 (19), the platform the
//                         game's own bundles record, rather than StandaloneWindows (5)
//   v5_meta_none          the .meta-marking recipe: stamp .meta assetBundleName, let
//                         BuildAssetBundles scan the AssetDatabase
//   v6_chunked            as v1 but ChunkBasedCompression, to test the block-compression theory
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildFlightPlanVariants
{
    private const string OutputDir = "BundleVariants";

    private static readonly string[] UxmlDirs =
    {
        "Assets/FlightPlan/UI",
    };

    private static readonly string[] TextureDirs =
    {
        "Assets/FlightPlan/UI",
    };

    public static void Build()
    {
        if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
        Directory.CreateDirectory(OutputDir);

        Build("v1_explicit_none", BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows, true, false);
        Build("v2_disable_typetree", BuildAssetBundleOptions.DisableWriteTypeTree, BuildTarget.StandaloneWindows, true, false);
        Build("v3_no_font", BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows, false, false);
        Build("v4_win64", BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64, true, false);
        Build("v5_meta_none", BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows, true, true);
        Build("v6_chunked", BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows, true, false);

        Debug.Log("[variants] all variants built");
        EditorApplication.Exit(0);
    }

    // The font assets are what v3 removes, so they are collected by type rather than named: a
    // port that renames or replaces its fonts must not silently stop testing them.
    private static void AddFonts(List<string> assets)
    {
        foreach (var d in UxmlDirs)
            foreach (var g in AssetDatabase.FindAssets("t:FontAsset t:Font", new[] { d }))
                assets.Add(AssetDatabase.GUIDToAssetPath(g));
    }

    private static void Build(string name, BuildAssetBundleOptions options, BuildTarget target,
                              bool includeFont, bool useMetaMarking)
    {
        string bundleName = name + ".bundle";
        string dir = Path.Combine(OutputDir, name);
        Directory.CreateDirectory(dir);

        var assets = new List<string>();
        foreach (var d in UxmlDirs)
            foreach (var g in AssetDatabase.FindAssets("t:VisualTreeAsset t:StyleSheet", new[] { d }))
                assets.Add(AssetDatabase.GUIDToAssetPath(g));
        foreach (var d in TextureDirs)
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { d }))
                assets.Add(AssetDatabase.GUIDToAssetPath(g));
        if (includeFont)
        {
            AddFonts(assets);
        }
        assets.Sort();

        AssetBundleManifest manifest;
        if (useMetaMarking)
        {
            // Clear any previous markings, stamp this variant's name, then let the AssetDatabase
            // decide what lands in the bundle (only the .uxml/.uss are marked explicitly; images
            // and the font are pulled in as dependencies).
            foreach (var g in AssetDatabase.FindAssets("t:VisualTreeAsset t:StyleSheet t:Texture2D t:Font t:FontAsset", new[] { "Assets" }))
            {
                var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g));
                if (imp != null) imp.SetAssetBundleNameAndVariant("", "");
            }
            foreach (var d in UxmlDirs)
                foreach (var g in AssetDatabase.FindAssets("t:VisualTreeAsset t:StyleSheet", new[] { d }))
                {
                    var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g));
                    if (imp != null) imp.SetAssetBundleNameAndVariant(bundleName, "");
                }
            AssetDatabase.SaveAssets();
            manifest = BuildPipeline.BuildAssetBundles(dir, options, target);
        }
        else
        {
            var build = new AssetBundleBuild
            {
                assetBundleName = bundleName,
                assetBundleVariant = "",
                assetNames = assets.ToArray(),
            };
            manifest = BuildPipeline.BuildAssetBundles(dir, new[] { build }, options, target);
        }

        string built = Path.Combine(dir, bundleName);
        if (manifest == null || !File.Exists(built))
            Debug.LogError($"[variants] {name}: FAILED (manifest={(manifest != null)}, file={File.Exists(built)})");
        else
            Debug.Log($"[variants] {name}: OK {new FileInfo(built).Length:N0} bytes, " +
                      $"options={options}, target={target}, assets={assets.Count}, font={includeFont}, meta={useMetaMarking}");
    }
}
