// Pre-deploy render proof for the flightplan_ui bundle - two phases, one entry point.
//
//   "$HOME/Unity/Hub/Editor/6000.5.8f1/Editor/Unity" -batchmode -nographics -quit \
//       -projectPath <the project> \
//       -executeMethod FlightPlan.EditorTools.VerifyFlightPlanUxml.Verify -logFile <log>
//
// WHY ("loads" != "renders"): a bundle the runtime accepts can still contain VisualTreeAssets
// that clone with ZERO children - a 2022.3.5f1-built bundle does exactly that, and it is the
// blank-window class. Importing without errors is not evidence the UI renders, so this script
// instantiates every template and logs the clone's childCount. Any zero, any load failure and
// any thrown exception is a failure.
//
//  * phase 1 reads the SOURCES out of the project (AssetDatabase).
//  * phase 2 reads the BUILT BYTES back (AssetBundle.LoadFromFile) and repeats the proof on
//    what actually survived the pack.
// Phase 2 is NECESSARY BUT NOT SUFFICIENT: it also passes for bundles the player rejects - K2-D2
// measured that twice (a bundle whose UXML names the mod's own `UxmlSerializedData` clones empty
// only in the player, not in the editor, because the editor can resolve an assembly the player
// cannot). It is the strongest check available before hand-off, not a substitute for a launch.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightPlan.EditorTools
{
    public static class VerifyFlightPlanUxml
    {
        private const string UiDir = "Assets/FlightPlan/UI";
        private const string WindowPath = UiDir + "/FP_UI.uxml";
        private const string DefaultBundleDir = "Assets/FlightPlan/Copied/assets/bundles";

        // (uxml path, element name that must exist in the instantiated tree). These are the
        // markers that prove the bundle carries THIS port's page and not a stale or foreign build.
        private static readonly (string Path, string Probe)[] MarkerProbes =
        {
            (WindowPath, "GUIFrame"),
            (WindowPath, "CloseButton"),
            (WindowPath, "TabBar"),
            (WindowPath, "BottomPanel"),
            (WindowPath, "ButtonBar"),
        };

        public static void Verify()
        {
            int failures = 0;

            failures += VerifySources();
            failures += VerifyBundles();

            Debug.Log($"[uxml-verify] result: {failures} failure(s) in total");
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // --- phase 1: the sources, straight out of the project -------------------------------
        private static int VerifySources()
        {
            int failures = 0;
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { UiDir }))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort(StringComparer.Ordinal);
            Debug.Log($"[uxml-verify] found {paths.Count} VisualTreeAsset(s) under {UiDir}");

            var instantiated = new Dictionary<string, VisualElement>();
            foreach (string path in paths)
            {
                VisualTreeAsset vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (vta == null)
                {
                    Debug.LogError($"[uxml-verify] {path} childCount=LOAD-FAILED");
                    failures++;
                    continue;
                }

                try
                {
                    TemplateContainer instance = vta.Instantiate();
                    int childCount = instance == null ? -1 : instance.childCount;
                    Debug.Log($"[uxml-verify] {path} childCount={childCount}");
                    if (childCount <= 0)
                    {
                        failures++;
                    }
                    else
                    {
                        instantiated[Path.GetFileNameWithoutExtension(path)] = instance;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[uxml-verify] {path} THREW {e.GetType().Name}: {e.Message}");
                    failures++;
                }
            }

            failures += ProbeMarkers(instantiated, "[uxml-verify]");
            return failures;
        }

        // --- phase 2: the delivered bytes ----------------------------------------------------
        private static int VerifyBundles()
        {
            int failures = 0;
            var bundles = new List<string>();
            string env = Environment.GetEnvironmentVariable("FLIGHTPLAN_VERIFY_BUNDLES");
            if (!string.IsNullOrEmpty(env))
            {
                bundles.AddRange(env.Split(';'));
            }
            else if (Directory.Exists(DefaultBundleDir))
            {
                bundles.AddRange(Directory.GetFiles(DefaultBundleDir, "*.bundle"));
            }
            bundles.Sort(StringComparer.Ordinal);

            Debug.Log($"[uxml-bundle] {bundles.Count} bundle(s) to verify");
            if (bundles.Count == 0)
            {
                Debug.LogError($"[uxml-bundle] no bundle found under {DefaultBundleDir} - the " +
                               "render proof cannot run; build the bundle first.");
                return 1;
            }

            foreach (string path in bundles)
            {
                AssetBundle bundle = null;
                try
                {
                    bundle = AssetBundle.LoadFromFile(path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[uxml-bundle] {path} LoadFromFile THREW {e.GetType().Name}: {e.Message}");
                    failures++;
                    continue;
                }
                if (bundle == null)
                {
                    Debug.LogError($"[uxml-bundle] {path} LoadFromFile -> null");
                    failures++;
                    continue;
                }

                try
                {
                    string[] container = bundle.GetAllAssetNames();
                    Debug.Log($"[uxml-bundle] {path} bundle.name='{bundle.name}' " +
                              $"container={container.Length} asset(s)");

                    var templates = new List<VisualTreeAsset>();
                    try
                    {
                        foreach (VisualTreeAsset vta in bundle.LoadAllAssets<VisualTreeAsset>())
                        {
                            templates.Add(vta);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[uxml-bundle] LoadAllAssets<VisualTreeAsset> THREW " +
                                       $"{e.GetType().Name}: {e.Message}");
                        failures++;
                    }
                    templates.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

                    // "every template in the bundle" is MEASURED, not assumed: the count comes
                    // from LoadAllAssets, and it is reported even when it is what we expect.
                    Debug.Log($"[uxml-bundle] templates in the bundle: {templates.Count}");
                    var instantiated = new Dictionary<string, VisualElement>();
                    foreach (VisualTreeAsset vta in templates)
                    {
                        try
                        {
                            TemplateContainer instance = vta.Instantiate();
                            int childCount = instance == null ? -1 : instance.childCount;
                            Debug.Log($"[uxml-bundle] TEMPLATE '{vta.name}' childCount={childCount} " +
                                      $"descendants={(instance == null ? -1 : instance.Query<VisualElement>().ToList().Count)}");
                            if (childCount <= 0)
                            {
                                failures++;
                            }
                            else
                            {
                                instantiated[Path.GetFileNameWithoutExtension(vta.name)] = instance;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[uxml-bundle] TEMPLATE '{vta.name}' THREW " +
                                           $"{e.GetType().Name}: {e.Message}");
                            failures++;
                        }
                    }

                    // The page the window loader looks up, by the exact path it uses.
                    VisualTreeAsset page = bundle.LoadAsset<VisualTreeAsset>(WindowPath);
                    if (page == null)
                    {
                        Debug.LogError($"[uxml-bundle] {WindowPath} is MISSING from the bundle's " +
                                       "container - the window would load a null VisualTreeAsset.");
                        failures++;
                    }
                    else
                    {
                        TemplateContainer root = page.Instantiate();
                        var childNames = new List<string>();
                        for (int i = 0; i < root.childCount; i++)
                        {
                            childNames.Add(root[i].name);
                        }
                        Debug.Log($"[uxml-bundle] PAGE '{page.name}' childCount={root.childCount} " +
                                  $"children=[{string.Join(", ", childNames)}]");
                        if (root.childCount <= 0)
                        {
                            failures++;
                        }
                        instantiated[Path.GetFileNameWithoutExtension(WindowPath)] = root;
                    }

                    failures += ProbeMarkers(instantiated, "[uxml-bundle]");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[uxml-bundle] {path} THREW {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                    failures++;
                }
                finally
                {
                    bundle.Unload(true);
                }
            }
            return failures;
        }

        // A window whose root element exists but no longer carries its named children is the
        // blank-window class, and a marker that is missing is a hard failure, not a warning.
        private static int ProbeMarkers(Dictionary<string, VisualElement> instantiated, string prefix)
        {
            int failures = 0;
            foreach ((string path, string probe) in MarkerProbes)
            {
                if (!instantiated.TryGetValue(Path.GetFileNameWithoutExtension(path), out VisualElement root))
                {
                    continue;   // that template already failed above
                }

                VisualElement element = root.Q(probe);
                if (element == null)
                {
                    Debug.LogError($"{prefix} MARKER MISSING {path} :: {probe}");
                    failures++;
                }
                else
                {
                    Debug.Log($"{prefix} marker found: {path} :: {probe} ({element.GetType().Name})");
                }
            }
            return failures;
        }
    }
}
