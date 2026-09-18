// Pre-deploy render proof for the flightplan_ui bundle.
//
// Invoked by Tools/build-ui-bundle.sh via a second batchmode run on the same generated
// project:
//
//   Unity -batchmode -nographics -quit -projectPath <proj> \
//         -executeMethod VerifyFlightPlanUxml.Verify -logFile -
//
// Why this exists ("loads" != "renders"): a bundle the runtime accepts can still contain
// VisualTreeAssets that clone with ZERO children - a 2022.3.5f1-built bundle does exactly that.
// Importing without errors is not evidence the UI renders, so this script instantiates every
// VisualTreeAsset in the project and logs the clone's childCount. Any zero, any load failure and
// any thrown exception is a failure.
//
// It also probes a handful of element names that must exist in the instantiated window. Those
// names are what proves the rebuilt bundle carries this port's pages and not a stale/other
// build - a marker that is missing is a hard failure, not a warning.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class VerifyFlightPlanUxml
{
    private const string UiDir = "Assets/FlightPlan/UI";
    private const string WindowPath = "Assets/FlightPlan/UI/FP_UI.uxml";

    // (uxml path, element name that must exist in the instantiated tree)
    private static readonly (string Path, string Probe)[] MarkerProbes =
    {
        ("Assets/FlightPlan/UI/FP_UI.uxml", "GUIFrame"),
        ("Assets/FlightPlan/UI/FP_UI.uxml", "CloseButton"),
        ("Assets/FlightPlan/UI/FP_UI.uxml", "TabBar"),
        ("Assets/FlightPlan/UI/FP_UI.uxml", "BottomPanel"),
        ("Assets/FlightPlan/UI/FP_UI.uxml", "ButtonBar"),
    };

    public static void Verify()
    {
        int failures = 0;

        var guids = AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { UiDir });
        var paths = new List<string>();
        foreach (var guid in guids)
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(StringComparer.Ordinal);

        Debug.Log($"[uxml-verify] found {paths.Count} VisualTreeAsset(s) under {UiDir}");

        var instantiated = new Dictionary<string, VisualElement>();
        foreach (var path in paths)
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (vta == null)
            {
                Debug.LogError($"[uxml-verify] {path} childCount=LOAD-FAILED");
                failures++;
                continue;
            }

            try
            {
                var instance = vta.Instantiate();
                int childCount = instance == null ? -1 : instance.childCount;
                Debug.Log($"[uxml-verify] {path} childCount={childCount}");
                if (childCount <= 0)
                    failures++;
                else
                    instantiated[path] = instance;
            }
            catch (Exception e)
            {
                Debug.LogError($"[uxml-verify] {path} THREW {e.GetType().Name}: {e.Message}");
                failures++;
            }
        }

        foreach (var probe in MarkerProbes)
        {
            if (!instantiated.TryGetValue(probe.Path, out var root))
                continue;   // that template already failed above

            var element = root.Q(probe.Probe);
            if (element == null)
            {
                Debug.LogError($"[uxml-verify] MARKER MISSING {probe.Path} :: {probe.Probe}");
                failures++;
            }
            else
            {
                Debug.Log($"[uxml-verify] marker found: {probe.Path} :: {probe.Probe} ({element.GetType().Name})");
            }
        }

        // A window whose root element exists but has no children would still be a blank window,
        // so log the window's own child tree explicitly and fail if the root page is empty.
        if (instantiated.TryGetValue(WindowPath, out var window))
        {
            var childNames = new List<string>();
            for (int i = 0; i < window.childCount; i++)
                childNames.Add(window[i].name);
            Debug.Log($"[uxml-verify] {WindowPath} root ({window.GetType().Name}) childCount={window.childCount} " +
                      $"children=[{string.Join(", ", childNames)}]");
            if (window.childCount <= 0)
                failures++;
        }

        Debug.Log($"[uxml-verify] result: {paths.Count} template(s), {failures} failure(s)");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }
}
