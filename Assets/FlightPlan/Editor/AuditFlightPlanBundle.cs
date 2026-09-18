// Editor-side audit of the BUILT bundle: what survived the pack, read from its own bytes.
//
//   "$HOME/Unity/Hub/Editor/6000.5.8f1/Editor/Unity" -batchmode -nographics -quit \
//       -projectPath <the project> \
//       -executeMethod FlightPlan.EditorTools.AuditFlightPlanBundle.Run -logFile <log>
//
// The render proof (VerifyFlightPlanUxml) instantiates templates; this pass reports the things
// that are only visible in the delivered file:
//
//   * `GetAllAssetNames()` - the CONTAINER, i.e. the class-142 `AssetBundle` object's map. A
//     bundle whose container is empty is rejected by the player, and no string census or type
//     table can substitute for the editor resolving it. This is the authoritative container
//     check (the v22-era type-table walk cannot run on a v23 file at all).
//   * every FontAsset / Material / StyleSheet, with NULL shader / NULL main texture / unresolved
//     stylesheet slot reported as a FAILURE - a font that lost its shader or its atlas is the
//     blank-window failure mode, and it is visible here even though it only hurts in the player.
//   * the root page instantiated from the DELIVERED bytes, plus the two dropdowns whose choices
//     live in the markup: a clone that loses them is a silently empty dropdown.
//
// Bundle paths come from $FLIGHTPLAN_AUDIT_BUNDLES (';'-separated); with no env var every
// *.bundle under the project's payload tree is audited.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace FlightPlan.EditorTools
{
    public static class AuditFlightPlanBundle
    {
        private const string DefaultBundleDir = "Assets/FlightPlan/Copied/assets/bundles";
        private const string WindowPath = "Assets/FlightPlan/UI/FP_UI.uxml";

        public static void Run()
        {
            var paths = new List<string>();
            string env = Environment.GetEnvironmentVariable("FLIGHTPLAN_AUDIT_BUNDLES");
            if (!string.IsNullOrEmpty(env))
            {
                foreach (string p in env.Split(';'))
                {
                    if (!string.IsNullOrEmpty(p))
                    {
                        paths.Add(p);
                    }
                }
            }
            else
            {
                AddGlob(paths, DefaultBundleDir);
            }
            paths.Sort(StringComparer.Ordinal);

            Debug.Log($"[audit] {paths.Count} bundle(s) to audit");
            int failures = 0;
            foreach (string p in paths)
            {
                failures += AuditOne(p);
            }
            Debug.Log($"[audit] done - {failures} failure(s)");
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static void AddGlob(List<string> paths, string dir)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            string[] files = Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files)
            {
                if (f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                paths.Add(f);
            }
        }

        private static int AuditOne(string path)
        {
            int failures = 0;
            Debug.Log($"[audit] ===== {path} ({(File.Exists(path) ? new FileInfo(path).Length.ToString("N0") + " bytes" : "MISSING")})");
            if (!File.Exists(path))
            {
                return 1;
            }

            AssetBundle bundle;
            try
            {
                bundle = AssetBundle.LoadFromFile(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[audit] LoadFromFile THREW {e.GetType().Name}: {e.Message}");
                return 1;
            }
            if (bundle == null)
            {
                Debug.LogError("[audit] LoadFromFile -> null");
                return 1;
            }

            try
            {
                Debug.Log($"[audit]   bundle.name='{bundle.name}'");
                string[] names = bundle.GetAllAssetNames();
                Array.Sort(names, StringComparer.Ordinal);
                Debug.Log($"[audit]   container assets: {names.Length}");
                foreach (string n in names)
                {
                    Debug.Log($"[audit]     container: {n}");
                }
                if (names.Length == 0)
                {
                    Debug.LogError("[audit]   the container is EMPTY - the player rejects a " +
                                   "container-less bundle.");
                    failures++;
                }

                UnityEngine.Object[] all = SafeAll(bundle);
                Debug.Log($"[audit]   LoadAllAssets(): {all.Length}");
                foreach (UnityEngine.Object o in all)
                {
                    if (o == null)
                    {
                        continue;
                    }
                    if (o is FontAsset fa)
                    {
                        Debug.Log($"[audit]     FONT '{fa.name}' {DescribeFont(fa)}");
                        continue;
                    }
                    if (o is Material m)
                    {
                        Debug.Log($"[audit]     MAT '{m.name}' {DescribeMaterial(m)}");
                        continue;
                    }
                    Debug.Log($"[audit]     {o.GetType().Name} '{o.name}'");
                }

                FontAsset[] fonts = SafeAllT<FontAsset>(bundle);
                Debug.Log($"[audit]   LoadAllAssets<FontAsset>(): {fonts.Length}");
                foreach (FontAsset fa in fonts)
                {
                    string state = DescribeFont(fa);
                    Debug.Log($"[audit]     FONT '{fa.name}' {state}");
                    if (state.Contains("shader=NULL") || state.Contains("mainTex=NULL"))
                    {
                        Debug.LogError($"[audit]     the font '{fa.name}' lost its shader or its " +
                                       "main texture - the blank-window failure mode.");
                        failures++;
                    }
                }

                Material[] mats = SafeAllT<Material>(bundle);
                Debug.Log($"[audit]   LoadAllAssets<Material>(): {mats.Length}");
                foreach (Material m in mats)
                {
                    string state = DescribeMaterial(m);
                    Debug.Log($"[audit]     MAT '{m.name}' {state}");
                    if (state.Contains("shader=NULL") || state.Contains("mainTex=NULL"))
                    {
                        Debug.LogError($"[audit]     the material '{m.name}' lost its shader or " +
                                       "its main texture.");
                        failures++;
                    }
                }

                StyleSheet[] sheets = SafeAllT<StyleSheet>(bundle);
                Debug.Log($"[audit]   LoadAllAssets<StyleSheet>(): {sheets.Length}");
                foreach (StyleSheet s in sheets)
                {
                    Debug.Log($"[audit]     SHEET '{s.name}' importedWithErrors={Field(s, "m_ImportedWithErrors")} " +
                              $"importedWithWarnings={Field(s, "m_ImportedWithWarnings")}");
                    IList assets = SheetAssets(s);
                    if (assets == null)
                    {
                        Debug.Log("[audit]       m_Assets: <unreadable>");
                        continue;
                    }
                    Debug.Log($"[audit]       m_Assets.Count={assets.Count}");
                    for (int i = 0; i < assets.Count; i++)
                    {
                        UnityEngine.Object o = assets[i] as UnityEngine.Object;
                        if (o == null)
                        {
                            Debug.Log($"[audit]       m_Assets[{i}] = NULL (unresolved)");
                            continue;
                        }
                        if (o is FontAsset fa)
                        {
                            Debug.Log($"[audit]       m_Assets[{i}] FontAsset '{fa.name}' {DescribeFont(fa)}");
                        }
                        else
                        {
                            Debug.Log($"[audit]       m_Assets[{i}] {o.GetType().Name} '{o.name}'");
                        }
                    }
                }

                // The PAGE, instantiated from the DELIVERED bytes. childCount > 0 is the
                // blank-window gate; the dropdowns are the items the acceptance gate clicks, and
                // their choices live in the markup, so an empty dropdown is a silent failure.
                VisualTreeAsset page = SafeLoad<VisualTreeAsset>(bundle, WindowPath);
                if (page == null)
                {
                    Debug.LogError("[audit]   the root UXML page is MISSING from the bundle");
                    failures++;
                }
                else
                {
                    TemplateContainer root = page.Instantiate();
                    Debug.Log($"[audit]   PAGE '{page.name}' instantiated: childCount={root.childCount} " +
                              $"descendants={root.Query<VisualElement>().ToList().Count} " +
                              $"first='{(root.childCount > 0 ? root[0].name : "<none>")}'");
                    if (root.childCount <= 0)
                    {
                        Debug.LogError("[audit]   the page instantiated with childCount <= 0 - a blank window.");
                        failures++;
                    }
                    failures += DumpDropdown(root, "TargetSelectionDropdown");
                    failures += DumpDropdown(root, "BurnOptionsDropdown");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[audit] audit THREW {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                failures++;
            }
            finally
            {
                bundle.Unload(true);
            }
            return failures;
        }

        private static UnityEngine.Object[] SafeAll(AssetBundle b)
        {
            try
            {
                return b.LoadAllAssets() ?? new UnityEngine.Object[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[audit] LoadAllAssets THREW {e.GetType().Name}: {e.Message}");
                return new UnityEngine.Object[0];
            }
        }

        private static T[] SafeAllT<T>(AssetBundle b) where T : UnityEngine.Object
        {
            try
            {
                return b.LoadAllAssets<T>() ?? new T[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[audit] LoadAllAssets<{typeof(T).Name}> THREW {e.GetType().Name}: {e.Message}");
                return new T[0];
            }
        }

        private static T SafeLoad<T>(AssetBundle b, string name) where T : UnityEngine.Object
        {
            try
            {
                return b.LoadAsset<T>(name);
            }
            catch (Exception e)
            {
                Debug.LogError($"[audit] LoadAsset<{typeof(T).Name}>('{name}') THREW {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // A dropdown that came out of the clone with zero choices is a failure this proves
        // against: it renders, it opens, and it is empty.
        private static int DumpDropdown(VisualElement root, string name)
        {
            DropdownField d = root.Q<DropdownField>(name);
            if (d == null)
            {
                Debug.LogError($"[audit]   DROPDOWN '{name}' NOT FOUND in the instantiated clone");
                return 1;
            }
            Debug.Log($"[audit]   DROPDOWN '{name}' choices={d.choices.Count} index={d.index} value='{d.value}'");
            if (d.choices.Count > 0)
            {
                Debug.Log($"[audit]     choices[0]='{d.choices[0]}' choices[last]='{d.choices[d.choices.Count - 1]}'");
                return 0;
            }
            Debug.LogError($"[audit]     DROPDOWN '{name}' has ZERO choices");
            return 1;
        }

        private static string DescribeMaterial(Material m)
        {
            if (m == null)
            {
                return "material=NULL";
            }
            Shader sh = m.shader;
            Texture tex = m.mainTexture;
            string texInfo = tex == null ? "NULL" : $"'{tex.name}' ({tex.width}x{tex.height})";
            return $"mat='{m.name}' shader={(sh == null ? "NULL" : "'" + sh.name + "'")} mainTex={texInfo}";
        }

        private static string DescribeFont(FontAsset fa)
        {
            if (fa == null)
            {
                return "<null>";
            }
            StringBuilder sb = new StringBuilder();
            try { sb.Append($"family='{(fa.faceInfo.familyName ?? "?")}' "); } catch { }
            try { sb.Append($"popMode={fa.atlasPopulationMode} "); } catch { }
            try { sb.Append($"sourceFontFile={(fa.sourceFontFile == null ? "null" : "'" + fa.sourceFontFile.name + "'")} "); } catch { }
            try
            {
                Texture2D at = fa.atlasTexture;
                sb.Append($"atlasTexture={(at == null ? "null" : "'" + at.name + "' (" + at.width + "x" + at.height + ")")} ");
            }
            catch (Exception e) { sb.Append($"atlasTexture THREW {e.GetType().Name} "); }
            try { Texture2D[] ats = fa.atlasTextures; sb.Append($"atlasTextures={(ats == null ? "null" : ats.Length.ToString())} "); } catch { }
            try { sb.Append($"material={DescribeMaterial(fa.material)}"); } catch (Exception e) { sb.Append($"material THREW {e.GetType().Name}"); }
            return sb.ToString();
        }

        private static object Field(object o, string name)
        {
            try
            {
                return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(o);
            }
            catch
            {
                return null;
            }
        }

        private static IList SheetAssets(StyleSheet s)
        {
            try
            {
                FieldInfo f = typeof(StyleSheet).GetField("assets", BindingFlags.Instance | BindingFlags.NonPublic);
                return f?.GetValue(s) as IList;
            }
            catch
            {
                return null;
            }
        }
    }
}
