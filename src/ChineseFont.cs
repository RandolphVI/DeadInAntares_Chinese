using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace ChineseFont
{
    [BepInPlugin("com.translation.chinesefont", "Chinese Font Support", "2.0.0")]
    public class ChineseFontPlugin : BaseUnityPlugin
    {
        internal static string FontFilePath;
        internal static string ActualFontPath;
        internal static string LogFilePath;
        internal static TMP_FontAsset CJKFont;
        internal static Font CJKSourceFont;
        internal static bool InitDone;

        internal static void L(string msg)
        {
            try { File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
        }

        void Awake()
        {
            string dir = Path.GetDirectoryName(Info.Location);
            LogFilePath = Path.Combine(dir, "debug.log");
            FontFilePath = Path.Combine(dir, "fonts", "simhei.ttf");
            File.WriteAllText(LogFilePath, "");

            L($"v2.0.0 Awake. FontFile exists: {File.Exists(FontFilePath)}");

            // Manual Harmony patching with error handling
            var harmony = new Harmony("com.translation.chinesefont");

            try
            {
                // Patch 1: Hook Localization.Get(string) - called on every text lookup
                // This is our initialization trigger since Update/Start don't work
                var locGet = typeof(Localization).GetMethod("Get",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(string) }, null);
                if (locGet != null)
                {
                    var prefix = typeof(LocalizationGetPatch).GetMethod("Prefix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(locGet, prefix: new HarmonyMethod(prefix));
                    L("Patched Localization.Get");
                }
                else
                {
                    L("ERROR: Localization.Get not found!");
                }
            }
            catch (Exception ex) { L($"Patch Localization.Get failed: {ex.Message}"); }

            try
            {
                // Patch 2: Hook FontEngine.LoadFontFace(Font, int) to redirect our font
                var loadFontFace = typeof(FontEngine).GetMethod("LoadFontFace",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(Font), typeof(int) }, null);
                if (loadFontFace != null)
                {
                    var prefix = typeof(FontEnginePatch).GetMethod("Prefix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(loadFontFace, prefix: new HarmonyMethod(prefix));
                    L("Patched FontEngine.LoadFontFace(Font,int)");
                }
                else
                {
                    L("ERROR: FontEngine.LoadFontFace(Font,int) not found!");
                }
            }
            catch (Exception ex) { L($"Patch FontEngine failed: {ex.Message}"); }

            L("Awake done");
        }

        internal static void DoInit()
        {
            if (InitDone) return;
            InitDone = true;

            L("DoInit triggered");

            try
            {
                // Use higher sampling size for better SDF quality
                int samplingSize = 48;
                int atlasPad = 6;
                int atlasSize = 4096;

                FontEngine.InitializeFontEngine();

                // Load CJK font: system fonts first, then plugin directory as fallback
                string fontFile = null;
                int faceIndex = 0;
                string[] candidates = new string[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc"),
                    "C:/Windows/Fonts/msyh.ttc",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "simhei.ttf"),
                    "C:/Windows/Fonts/simhei.ttf",
                    Path.Combine(Path.GetDirectoryName(FontFilePath), "msyh.ttc"),
                    FontFilePath, // simhei.ttf in plugin dir
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { fontFile = c; break; }
                }
                if (fontFile == null) { L("No CJK font found!"); return; }
                ActualFontPath = fontFile;
                L($"Using font: {fontFile} faceIndex={faceIndex}");

                var err = FontEngine.LoadFontFace(fontFile, samplingSize, faceIndex);
                L($"FontEngine.LoadFontFace(file): {err}");
                if (err != FontEngineError.Success) return;

                var faceInfo = FontEngine.GetFaceInfo();
                L($"FaceInfo: {faceInfo.familyName}");

                // Find an existing font to steal its shader
                Shader shader = null;
                var existing = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                L($"Existing TMP fonts: {existing.Length}");
                foreach (var ef in existing)
                {
                    if (ef.material?.shader != null)
                    {
                        shader = ef.material.shader;
                        L($"Shader: {shader.name} from {ef.name}");
                        break;
                    }
                }
                if (shader == null) { L("No shader found!"); return; }

                // Create font asset
                var fa = ScriptableObject.CreateInstance<TMP_FontAsset>();
                fa.name = "CJK-SimHei";
                fa.faceInfo = faceInfo;
                fa.name = "CJK-MSYH";
                fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;

                SetField(fa, "m_AtlasWidth", atlasSize);
                SetField(fa, "m_AtlasHeight", atlasSize);
                SetField(fa, "m_AtlasPadding", atlasPad);
                SetField(fa, "m_AtlasRenderMode", GlyphRenderMode.SDFAA);
                SetField(fa, "m_IsMultiAtlasTexturesEnabled", true);

                var tex = new Texture2D(atlasSize, atlasSize, TextureFormat.Alpha8, false);
                fa.atlasTextures = new Texture2D[] { tex };

                var mat = new Material(shader);
                mat.SetTexture(ShaderUtilities.ID_MainTex, tex);
                mat.SetFloat(ShaderUtilities.ID_TextureWidth, atlasSize);
                mat.SetFloat(ShaderUtilities.ID_TextureHeight, atlasSize);
                mat.SetFloat(ShaderUtilities.ID_GradientScale, samplingSize / 2f + atlasPad);
                // Increase WeightNormal to match SourceSansPro-SemiBold thickness
                // SourceSansPro-SemiBold is between Regular and Bold, so ~0.35 simulates semi-bold
                mat.SetFloat(ShaderUtilities.ID_WeightNormal, 0.35f);
                mat.SetFloat(ShaderUtilities.ID_WeightBold, 0.75f);
                fa.material = mat;

                // Create a dummy source font - LoadFontFace calls will be intercepted
                CJKSourceFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", samplingSize);
                if (CJKSourceFont == null)
                    CJKSourceFont = Font.CreateDynamicFontFromOSFont("SimHei", samplingSize);
                SetField(fa, "m_SourceFontFile", CJKSourceFont);
                SetField(fa, "m_SourceFontFilePath", fontFile);

                UnityEngine.Object.DontDestroyOnLoad(fa);
                UnityEngine.Object.DontDestroyOnLoad(mat);
                UnityEngine.Object.DontDestroyOnLoad(tex);
                if (CJKSourceFont != null) UnityEngine.Object.DontDestroyOnLoad(CJKSourceFont);

                CJKFont = fa;
                L("CJK font created, initializing internal tables...");

                // Set version to prevent UpgradeFontAsset from being called
                SetField(fa, "m_Version", "1.1.0");

                // Initialize required internal lists that ReadFontAssetDefinition expects
                SetField(fa, "m_GlyphTable", new List<UnityEngine.TextCore.Glyph>());
                SetField(fa, "m_CharacterTable", new List<TMP_Character>());

                // FontFeatureTable - needed for kerning
                var featTableType = typeof(TMP_FontAsset).Assembly.GetType("TMPro.TMP_FontFeatureTable");
                if (featTableType != null)
                {
                    var featTable = ScriptableObject.CreateInstance(featTableType) as ScriptableObject;
                    if (featTable == null) featTable = Activator.CreateInstance(featTableType) as ScriptableObject;
                    SetField(fa, "m_FontFeatureTable", featTable ?? Activator.CreateInstance(featTableType));
                }

                // UsedGlyphRects / FreeGlyphRects
                SetField(fa, "m_UsedGlyphRects", new List<UnityEngine.TextCore.GlyphRect>());
                SetField(fa, "m_FreeGlyphRects", new List<UnityEngine.TextCore.GlyphRect> {
                    new UnityEngine.TextCore.GlyphRect(0, 0, 2048, 2048)
                });

                try
                {
                    fa.ReadFontAssetDefinition();
                    L($"ReadFontAssetDefinition OK. CharTable: {fa.characterTable?.Count}, GlyphTable: {fa.glyphTable?.Count}");
                }
                catch (Exception ex) { L($"ReadFontAssetDefinition error: {ex}"); }

                // Inject into all existing fonts
                int count = 0;
                foreach (var ef in existing)
                {
                    if (ef == null || ef == CJKFont) continue;
                    if (ef.fallbackFontAssetTable == null)
                        ef.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    bool has = false;
                    foreach (var f in ef.fallbackFontAssetTable)
                        if (f == CJKFont) { has = true; break; }
                    if (!has) { ef.fallbackFontAssetTable.Add(CJKFont); count++; }
                }
                L($"Injected into {count}/{existing.Length} fonts");

                // Global fallback
                try
                {
                    var s = TMP_Settings.instance;
                    if (s != null)
                    {
                        var field = typeof(TMP_Settings).GetField("m_fallbackFontAssets",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            var list = field.GetValue(s) as List<TMP_FontAsset>;
                            if (list == null) { list = new List<TMP_FontAsset>(); field.SetValue(s, list); }
                            if (!list.Contains(CJKFont)) { list.Add(CJKFont); L("Global fallback set"); }
                        }
                    }
                }
                catch (Exception ex) { L($"Global fallback error: {ex.Message}"); }

                // Fix italic styling for CJK: remove <i> from TMP styles that look bad in Chinese
                // CampStation and similar styles use italic which is ugly for CJK characters
                try { FixItalicStyles(); }
                catch (Exception ex) { L($"FixItalicStyles error: {ex.Message}"); }

                L("Init complete!");
            }
            catch (Exception ex) { L($"DoInit FAILED: {ex}"); }
        }

        static void FixItalicStyles()
        {
            // Only fix if current language is Chinese
            try
            {
                string lang = Localization.language;
                if (lang != "中文") return;
            }
            catch { return; }

            // Find TMP default style sheet
            var styleSheets = Resources.FindObjectsOfTypeAll<TMP_StyleSheet>();
            L($"Found {styleSheets.Length} TMP StyleSheets");

            foreach (var sheet in styleSheets)
            {
                var stylesField = typeof(TMP_StyleSheet).GetField("m_StyleList",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (stylesField == null) continue;

                var styles = stylesField.GetValue(sheet) as System.Collections.IList;
                if (styles == null) continue;

                int fixed_count = 0;
                foreach (var style in styles)
                {
                    var nameField = style.GetType().GetField("m_Name",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var openField = style.GetType().GetField("m_OpeningDefinition",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var closeField = style.GetType().GetField("m_ClosingDefinition",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (nameField == null || openField == null || closeField == null) continue;

                    string name = nameField.GetValue(style) as string ?? "";
                    string open = openField.GetValue(style) as string ?? "";
                    string close = closeField.GetValue(style) as string ?? "";

                    // Dump first 30 styles for debugging
                    if (fixed_count < 3 || name.Contains("Camp") || name.Contains("Skill") || name.Contains("State"))
                        L($"  Style [{name}]: open=\"{open}\" close=\"{close}\"");

                    // Remove <i></i> and <mark=...></mark> (the "box" effect) from ALL styles
                    // These look bad in Chinese
                    string newOpen = open;
                    string newClose = close;

                    // Remove italic and bold from styles (they look bad in Chinese)
                    newOpen = newOpen.Replace("<i>", "").Replace("<b>", "");
                    newClose = newClose.Replace("</i>", "").Replace("</b>", "");

                    // Remove mark/highlight
                    newOpen = System.Text.RegularExpressions.Regex.Replace(newOpen, @"<mark=[^>]*>", "");
                    newClose = newClose.Replace("</mark>", "");

                    if (newOpen != open || newClose != close)
                    {
                        openField.SetValue(style, newOpen);
                        closeField.SetValue(style, newClose);

                        // TagArray fields are int[] (Unicode code points), not char[]
                        var openArrField = style.GetType().GetField("m_OpeningTagArray",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        var closeArrField = style.GetType().GetField("m_ClosingTagArray",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (openArrField != null)
                        {
                            int[] arr = new int[newOpen.Length];
                            for (int ii = 0; ii < newOpen.Length; ii++) arr[ii] = newOpen[ii];
                            openArrField.SetValue(style, arr);
                        }
                        if (closeArrField != null)
                        {
                            int[] arr = new int[newClose.Length];
                            for (int ii = 0; ii < newClose.Length; ii++) arr[ii] = newClose[ii];
                            closeArrField.SetValue(style, arr);
                        }

                        fixed_count++;
                    }
                }
                L($"Fixed {fixed_count} styles (removed italic + mark/box)");
            }
        }

        static void SetField(object obj, string name, object val)
        {
            var f = obj.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (f != null) f.SetValue(obj, val);
        }
    }

    /// <summary>
    /// Patch Localization.Get(string) - called for every text lookup.
    /// We use this as our init trigger since MonoBehaviour lifecycle doesn't work.
    /// </summary>
    public static class LocalizationGetPatch
    {
        private static bool _injected;

        public static void Prefix()
        {
            if (_injected) return;
            _injected = true;
            ChineseFontPlugin.DoInit();
        }
    }

    /// <summary>
    /// Patch FontEngine.LoadFontFace(Font, int) - intercept calls for our CJK font
    /// and redirect to file-based loading. This is the KEY fix: TMP's dynamic glyph
    /// rendering calls this with sourceFontFile, but CreateDynamicFontFromOSFont fonts
    /// don't have embedded data. We redirect to LoadFontFace(filePath, size) instead.
    /// </summary>
    public static class FontEnginePatch
    {
        private static int _logCount;

        public static bool Prefix(Font font, int pointSize, ref FontEngineError __result)
        {
            if (font == null || ChineseFontPlugin.CJKSourceFont == null)
                return true;

            if (font == ChineseFontPlugin.CJKSourceFont ||
                font.name == "SimHei" || font.name == "Microsoft YaHei" ||
                font.name == "Microsoft YaHei UI")
            {
                // Use the actual font file path (could be msyh.ttc or simhei.ttf)
                string path = ChineseFontPlugin.ActualFontPath ?? ChineseFontPlugin.FontFilePath;
                __result = FontEngine.LoadFontFace(path, pointSize, 0);
                if (_logCount < 5)
                {
                    _logCount++;
                    ChineseFontPlugin.L($"FontEngine REDIRECT: {font.name} pt={pointSize} -> {__result}");
                }
                return false;
            }

            return true;
        }
    }
}
