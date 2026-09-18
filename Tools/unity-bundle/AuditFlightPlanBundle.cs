// Editor-side audit of BUILT asset bundles.
//
// The render proof instantiates the UXML pages from the throwaway PROJECT, which proves the
// sources are valid but says nothing about what survives the bundle round-trip. This script
// loads the built bundle back (in the editor, where shader references always resolve) and dumps
// the state of every TextCore FontAsset / Material / StyleSheet in it. If a font's material or
// its material's main texture is null here, the loss happened at build/pack time (recipe bug).
// If everything is fine here but the player's TextUtilities.GetTextCoreSettingsForElement still
// null-references, the loss is player-specific (e.g. a shader reference that only resolves in
// the editor) - which is exactly the distinction the in-game diag then measures.
//
// Invoked via:  Unity -batchmode -nographics -quit -projectPath <proj> \
//                     -executeMethod AuditFlightPlanBundle.Run -logFile -
// Bundle paths come from $FLIGHTPLAN_AUDIT_BUNDLES (';'-separated); with no env var, every
// *.bundle under BundleOutput/ and BundleVariants/ is audited.
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

public static class AuditFlightPlanBundle
{
    public static void Run()
    {
        List<string> paths = new List<string>();
        string env = Environment.GetEnvironmentVariable("FLIGHTPLAN_AUDIT_BUNDLES");
        if (!string.IsNullOrEmpty(env))
        {
            foreach (string p in env.Split(';'))
                if (!string.IsNullOrEmpty(p)) paths.Add(p);
        }
        else
        {
            AddGlob(paths, "BundleOutput");
            AddGlob(paths, "BundleVariants");
        }

        Debug.Log($"[audit] {paths.Count} bundle(s) to audit");
        foreach (string p in paths) AuditOne(p);
        Debug.Log("[audit] done");
        EditorApplication.Exit(0);
    }

    private static void AddGlob(List<string> paths, string dir)
    {
        if (!Directory.Exists(dir)) return;
        string[] files = Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string f in files)
        {
            if (f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)) continue;
            paths.Add(f);
        }
    }

    private static void AuditOne(string path)
    {
        Debug.Log($"[audit] ===== {path} ({(File.Exists(path) ? new FileInfo(path).Length.ToString("N0") + " bytes" : "MISSING")})");
        if (!File.Exists(path)) return;

        AssetBundle bundle;
        try { bundle = AssetBundle.LoadFromFile(path); }
        catch (Exception e) { Debug.LogError($"[audit] LoadFromFile THREW {e.GetType().Name}: {e.Message}"); return; }
        if (bundle == null) { Debug.LogError("[audit] LoadFromFile -> null"); return; }

        try
        {
            Debug.Log($"[audit]   bundle.name='{bundle.name}'");
            string[] names = bundle.GetAllAssetNames();
            Array.Sort(names, StringComparer.Ordinal);
            Debug.Log($"[audit]   container assets: {names.Length}");
            foreach (string n in names) Debug.Log($"[audit]     container: {n}");

            UnityEngine.Object[] all = SafeAll(bundle);
            Debug.Log($"[audit]   LoadAllAssets(): {all.Length}");
            foreach (UnityEngine.Object o in all)
            {
                if (o == null) continue;
                FontAsset fa = o as FontAsset;
                if (fa != null) { Debug.Log($"[audit]     FONT '{fa.name}' {DescribeFont(fa)}"); continue; }
                Material m = o as Material;
                if (m != null) { Debug.Log($"[audit]     MAT '{m.name}' {DescribeMaterial(m)}"); continue; }
                Debug.Log($"[audit]     {o.GetType().Name} '{o.name}'");
            }

            FontAsset[] fonts = SafeAllT<FontAsset>(bundle);
            Debug.Log($"[audit]   LoadAllAssets<FontAsset>(): {fonts.Length}");
            foreach (FontAsset fa in fonts) Debug.Log($"[audit]     FONT '{fa.name}' {DescribeFont(fa)}");

            Material[] mats = SafeAllT<Material>(bundle);
            Debug.Log($"[audit]   LoadAllAssets<Material>(): {mats.Length}");
            foreach (Material m in mats) Debug.Log($"[audit]     MAT '{m.name}' {DescribeMaterial(m)}");

            StyleSheet[] sheets = SafeAllT<StyleSheet>(bundle);
            Debug.Log($"[audit]   LoadAllAssets<StyleSheet>(): {sheets.Length}");
            foreach (StyleSheet s in sheets)
            {
                Debug.Log($"[audit]     SHEET '{s.name}' importedWithErrors={Field(s, "m_ImportedWithErrors")} importedWithWarnings={Field(s, "m_ImportedWithWarnings")}");
                IList assets = SheetAssets(s);
                if (assets == null) { Debug.Log("[audit]       m_Assets: <unreadable>"); continue; }
                Debug.Log($"[audit]       m_Assets.Count={assets.Count}");
                for (int i = 0; i < assets.Count; i++)
                {
                    UnityEngine.Object o = assets[i] as UnityEngine.Object;
                    if (o == null) { Debug.Log($"[audit]       m_Assets[{i}] = NULL (unresolved)"); continue; }
                    FontAsset fa = o as FontAsset;
                    Debug.Log(fa != null
                        ? $"[audit]       m_Assets[{i}] FontAsset '{fa.name}' {DescribeFont(fa)}"
                        : $"[audit]       m_Assets[{i}] {o.GetType().Name} '{o.name}'");
                }
            }

            // -------------------------------------------------------------------------------------
            // The PAGE, instantiated from the DELIVERED bytes. `childCount > 0` is the blank-window
            // gate, and the two dropdowns are the acceptance-gate items ("click Burn Options; the
            // list must populate"): the legacy markup carried the choices in the UXML itself, so a
            // clone that loses them is a silently empty dropdown that only shows up in the player.
            VisualTreeAsset page = SafeLoad<VisualTreeAsset>(bundle, "Assets/FlightPlan/UI/FP_UI.uxml");
            if (page == null)
            {
                Debug.LogError("[audit]   the root UXML page is MISSING from the bundle");
            }
            else
            {
                TemplateContainer root = page.Instantiate();
                Debug.Log($"[audit]   PAGE '{page.name}' instantiated: childCount={root.childCount} " +
                          $"descendants={root.Query<VisualElement>().ToList().Count} first='{(root.childCount > 0 ? root[0].name : "<none>")}'");
                DumpDropdown(root, "TargetSelectionDropdown");
                DumpDropdown(root, "BurnOptionsDropdown");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[audit] audit THREW {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static UnityEngine.Object[] SafeAll(AssetBundle b)
    {
        try { return b.LoadAllAssets() ?? new UnityEngine.Object[0]; }
        catch (Exception e) { Debug.LogWarning($"[audit] LoadAllAssets THREW {e.GetType().Name}: {e.Message}"); return new UnityEngine.Object[0]; }
    }

    private static T[] SafeAllT<T>(AssetBundle b) where T : UnityEngine.Object
    {
        try { return b.LoadAllAssets<T>() ?? new T[0]; }
        catch (Exception e) { Debug.LogWarning($"[audit] LoadAllAssets<{typeof(T).Name}> THREW {e.GetType().Name}: {e.Message}"); return new T[0]; }
    }

    private static T SafeLoad<T>(AssetBundle b, string name) where T : UnityEngine.Object
    {
        try { return b.LoadAsset<T>(name); }
        catch (Exception e) { Debug.LogError($"[audit] LoadAsset<{typeof(T).Name}>('{name}') THREW {e.GetType().Name}: {e.Message}"); return null; }
    }

    // A dropdown that came out of the clone with zero choices is the failure this proves against:
    // it renders, it opens, and it is empty.
    private static void DumpDropdown(VisualElement root, string name)
    {
        DropdownField d = root.Q<DropdownField>(name);
        if (d == null)
        {
            Debug.LogError($"[audit]   DROPDOWN '{name}' NOT FOUND in the instantiated clone");
            return;
        }
        Debug.Log($"[audit]   DROPDOWN '{name}' choices={d.choices.Count} index={d.index} value='{d.value}'");
        if (d.choices.Count > 0)
        {
            Debug.Log($"[audit]     choices[0]='{d.choices[0]}' choices[last]='{d.choices[d.choices.Count - 1]}'");
        }
    }

    private static string DescribeMaterial(Material m)
    {
        if (m == null) return "material=NULL";
        Shader sh = m.shader;
        Texture tex = m.mainTexture;
        string texInfo = tex == null ? "NULL" : $"'{tex.name}' ({tex.width}x{tex.height})";
        return $"mat='{m.name}' shader={(sh == null ? "NULL" : "'" + sh.name + "'")} mainTex={texInfo}";
    }

    private static string DescribeFont(FontAsset fa)
    {
        if (fa == null) return "<null>";
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
        try { return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(o); }
        catch { return null; }
    }

    private static IList SheetAssets(StyleSheet s)
    {
        try
        {
            FieldInfo f = typeof(StyleSheet).GetField("assets", BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(s) as IList;
        }
        catch { return null; }
    }
}
