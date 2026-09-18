// Control build: same asset set as BuildFlightPlanUIBundle, but a DISTINCT bundle name so the
// 0.2.8.5 player will load both at once.
//
// Why: the diag mod A/B-tests the shipped font state in one game launch. Unity refuses a second
// AssetBundle.LoadFromFile of a bundle whose internal CAB name is already loaded, and the CAB
// name is derived from the bundle name, so the control must be built under its own name.
//
// Used against deliberately unfixed sources to capture a known-broken state (e.g. font materials
// with a null shader, hence a null _MainTex) for comparison against the shipped bundle.
// NEVER deploy the control.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildFlightPlanControl
{
    private const string BundleName = "flightplan_ui_broken.bundle";
    private const string OutputDir = "BundleControl";

    private static readonly string[] SourceDirs =
    {
        "Assets/FlightPlan/UI",
    };

    private static readonly string[] ExtraAssetDirs =
    {
    };

    private static readonly string[] ExtraAssetFiles =
    {
    };

    public static void Build()
    {
        var assets = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:VisualTreeAsset t:StyleSheet", SourceDirs))
            assets.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (var dir in ExtraAssetDirs)
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                assets.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (var dir in SourceDirs)
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D t:FontAsset t:Font", new[] { dir }))
                assets.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (var path in ExtraAssetFiles)
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                assets.Add(path);

        assets.Sort();
        Debug.Log($"[control] packing {assets.Count} asset(s) into '{BundleName}'");

        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetBundleVariant = "",
            assetNames = assets.ToArray(),
        };

        if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
        Directory.CreateDirectory(OutputDir);

        var manifest = BuildPipeline.BuildAssetBundles(
            OutputDir, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows);

        var built = Path.Combine(OutputDir, BundleName);
        if (manifest == null || !File.Exists(built))
        {
            Debug.LogError("[control] FAILED to build " + built);
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log($"[control] OK {built} ({new FileInfo(built).Length:N0} bytes)");
        EditorApplication.Exit(0);
    }
}
