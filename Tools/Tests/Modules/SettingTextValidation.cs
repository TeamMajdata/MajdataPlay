#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.Scenes.Setting;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Runs in an isolated project with the real installed uGUI/TMP implementation.
public static class SettingTextValidation
{
    const int Steps = 120;
    static int _assertions;
    static readonly MethodInfo InternalUpdate = typeof(TextMeshProUGUI).GetMethod("InternalUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Check(TMP_Settings.instance != null, "TMP Essential Resources were not staged.");
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SettingValidation.ttf");
            Check(source != null, "The isolated test font was not imported.");
            var primary = MakeFont(source, "Primary");
            var fallback = MakeFont(source, "Fallback");
            Check(primary.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .:-"), "Primary Latin characters are unavailable.");
            primary.atlasPopulationMode = AtlasPopulationMode.Static;
            primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            var canvas = new GameObject("Validation Canvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ValidateTint(canvas.transform, primary);
            ValidateTitle(canvas.transform, primary);
            ValidateWarmup(canvas.transform, source);
            Object.DestroyImmediate(canvas);
            Object.DestroyImmediate(primary);
            Object.DestroyImmediate(fallback);
            Debug.Log($"SETTING_TEXT_VALIDATION_PASSED assertions={_assertions}");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static TMP_FontAsset MakeFont(Font source, string name)
    {
        var font = TMP_FontAsset.CreateFontAsset(source, 40, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
        font.name = name;
        // These are temporary test fonts. Isolate mesh invalidation from font feature extraction.
        font.getFontFeatures = false;
        return font;
    }

    static void ValidateTint(Transform parent, TMP_FontAsset font)
    {
        var text = MakeText(parent, "Tint Text", font);
        text.text = "A设置B";
        Settle(text);
        Check(text.textInfo.materialCount >= 2, "The test must exercise a real fallback submesh.");
        var counters = new Counters(text);
        for (var i = 0; i < Steps; i++)
        {
            text.color = Color.Lerp(Color.white, Color.gray, (i + 1f) / Steps);
            Flush(text);
        }
        Check(counters.Vertices >= Steps && counters.Generated >= Steps, "Baseline TMP.color changes did not invalidate and regenerate text.");
        Debug.Log($"SETTING_TEXT_BASELINE steps={Steps} verticesDirty={counters.Vertices} layoutDirty={counters.Layout} generated={counters.Generated}");
        text.color = Color.white;
        using (var tint = new SettingTextTint(text))
        {
            Settle(text);
            counters.Reset();
            for (var i = 0; i < Steps; i++)
            {
                var color = Color.Lerp(Color.white, new Color(.36f, .31f, .29f, 1), (i + 1f) / Steps);
                tint.SetColor(color);
                text.rectTransform.anchoredPosition = new Vector2(i, 0);
                text.rectTransform.localScale = Vector3.one * Mathf.Lerp(1, .6f, (i + 1f) / Steps);
                Flush(text);
                CheckColor(text.canvasRenderer.GetColor(), color, "Main text tint diverged.");
                foreach (var child in text.GetComponentsInChildren<TMP_SubMeshUI>(true))
                    CheckColor(child.canvasRenderer.GetColor(), color, "Fallback tint diverged during scrolling.");
            }
            Check(counters.Vertices == 0 && counters.Layout == 0 && counters.Generated == 0,
                $"Renderer tint dirtied unchanged text: vertices={counters.Vertices}, layout={counters.Layout}, generated={counters.Generated}.");
            Debug.Log($"SETTING_TEXT_TINT steps={Steps} verticesDirty={counters.Vertices} layoutDirty={counters.Layout} generated={counters.Generated}");
            text.text = "A设置语言C";
            Flush(text);
            Check(counters.Generated > 0, "A real text change must still regenerate its mesh.");
            foreach (var child in text.GetComponentsInChildren<TMP_SubMeshUI>(true))
                CheckColor(child.canvasRenderer.GetColor(), text.canvasRenderer.GetColor(), "Rebuilt fallback did not inherit current tint.");
        }
        counters.Dispose();
        Object.DestroyImmediate(text.gameObject);
    }

    static void ValidateTitle(Transform parent, TMP_FontAsset font)
    {
        var root = new GameObject("Title", typeof(RectTransform));
        root.SetActive(false);
        root.transform.SetParent(parent, false);
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(147.83f, 76.8f);
        var background = MakeImage(root.transform, "Unselected", rect.sizeDelta);
        var selected = MakeImage(root.transform, "Selected", new Vector2(254.69f, 82.3f));
        var text = MakeText(root.transform, "Title Text", font);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.sizeDelta = new Vector2(-20, 0);
        text.enableAutoSizing = true;
        text.fontSizeMin = 18;
        text.fontSizeMax = 28;
        text.color = new Color(.36f, .31f, .29f, 1);
        var title = root.AddComponent<MenuTitleDisplayer>();
        Set(title, "_unselectedBackground", background);
        Set(title, "_selectedBackground", selected);
        Set(title, "_titleDisplayer", text);
        // This executeMethod runs in Edit Mode. Unlike TMP, the production title
        // is not ExecuteAlways, so invoke its ordinary lifecycle explicitly.
        typeof(MenuTitleDisplayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(title, null);
        root.SetActive(true);
        title.Initialize("Visual settings 设置");
        title.SetDistance(1);
        Settle(text);
        var originalSize = text.rectTransform.rect.size;
        using (var counters = new Counters(text))
        {
            for (var i = 0; i < Steps; i++)
            {
                var distance = 1f - i * 2f / (Steps - 1);
                title.SetDistance(distance);
                Flush(text);
                Check((text.rectTransform.rect.size - originalSize).sqrMagnitude < .0001f, "Animated title changed its text layout dimensions.");
                foreach (var child in text.GetComponentsInChildren<TMP_SubMeshUI>(true))
                    CheckColor(child.canvasRenderer.GetColor(), text.canvasRenderer.GetColor(), "Title fallback tint diverged.");
            }
            Check(counters.Vertices == 0 && counters.Layout == 0 && counters.Generated == 0,
                $"Title movement dirtied unchanged text: vertices={counters.Vertices}, layout={counters.Layout}, generated={counters.Generated}.");
            Debug.Log($"SETTING_TEXT_TITLE steps={Steps} verticesDirty={counters.Vertices} layoutDirty={counters.Layout} generated={counters.Generated}");
            title.Initialize("Changed category 分类");
            Flush(text);
            Check(counters.Generated > 0, "A changed category title failed to regenerate.");
        }
        typeof(MenuTitleDisplayer).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(title, null);
        Object.DestroyImmediate(root);
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.rectTransform.sizeDelta = new Vector2(380, 100);
        text.fontSize = 32;
        text.raycastTarget = false;
        return text;
    }
    static void ValidateWarmup(Transform parent, Font source)
    {
        var primary = MakeFont(source, "Warmup static primary");
        var fallback = MakeFont(source, "Warmup dynamic fallback");
        primary.TryAddCharacters("A ");
        primary.atlasPopulationMode = AtlasPopulationMode.Static;
        primary.getFontFeatures = true;
        primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
        fallback.fallbackFontAssetTable = new List<TMP_FontAsset> { primary };
        var staticGlyphs = primary.characterTable.Count;
        const string corpus = "A设置语言分类0123456789";
        var warmup = new SettingFontWarmup();
        warmup.AddText(primary, corpus + "\n\r\t\uD800\uDC00\uD800");
        var requests = (Dictionary<TMP_FontAsset, HashSet<uint>>)typeof(SettingFontWarmup).GetField("_requests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(warmup);
        Check(requests[primary].Contains(0x10000) && !requests[primary].Contains(0xD800) && !requests[primary].Contains(0xDC00), "Warmup did not decode surrogate pairs and reject invalid UTF-16.");
        Check(!requests[primary].Contains('\n') && !requests[primary].Contains('\r') && !requests[primary].Contains('\t'), "Warmup treated control characters as glyphs.");
        warmup.Warmup();
        Check(primary.characterTable.Count == staticGlyphs, "Warmup modified a static font.");
        foreach (var character in corpus)
            Check(primary.characterLookupTable.ContainsKey(character) || fallback.characterLookupTable.ContainsKey(character), "Warmup omitted a supported character.");
        Check(primary.getFontFeatures && !fallback.getFontFeatures, "Warmup changed font feature preferences.");
        var warmedGlyphs = fallback.characterTable.Count;
        warmup.AddText(primary, corpus);
        warmup.Warmup();
        Check(fallback.characterTable.Count == warmedGlyphs, "Repeated warmup unnecessarily grew the atlas.");
        var text = MakeText(parent, "Warmed text", primary);
        text.text = corpus;
        Settle(text);
        Check(fallback.characterTable.Count == warmedGlyphs, "Rendering the warmed corpus added glyphs during PreRender.");
        warmup.AddText(primary, "新的文字");
        warmup.Warmup();
        warmedGlyphs = fallback.characterTable.Count;
        text.text = "新的文字";
        Flush(text);
        Check(fallback.characterTable.Count == warmedGlyphs, "New locale corpus added glyphs after warmup.");
        Debug.Log($"SETTING_TEXT_WARMUP staticGlyphs={staticGlyphs} fallbackGlyphs={warmedGlyphs} renderGlyphGrowth=0");
        Object.DestroyImmediate(text.gameObject);
        Object.DestroyImmediate(primary);
        Object.DestroyImmediate(fallback);
    }
    static Image MakeImage(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.rectTransform.sizeDelta = size;
        return image;
    }
    static void Settle(TextMeshProUGUI text)
    {
        text.ForceMeshUpdate();
        Flush(text);
        Flush(text);
    }
    static void Flush(TextMeshProUGUI text)
    {
        InternalUpdate?.Invoke(text, null);
        Canvas.ForceUpdateCanvases();
    }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static void CheckColor(Color actual, Color expected, string message) => Check(Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g) + Mathf.Abs(actual.b - expected.b) + Mathf.Abs(actual.a - expected.a) < .005f, message);
    static void Check(bool result, string message)
    {
        _assertions++;
        if (!result) throw new InvalidOperationException(message);
    }
    sealed class Counters : IDisposable
    {
        readonly TextMeshProUGUI _text;
        public int Vertices, Layout, Generated;
        public Counters(TextMeshProUGUI text)
        {
            _text = text;
            text.RegisterDirtyVerticesCallback(OnVertices);
            text.RegisterDirtyLayoutCallback(OnLayout);
            text.OnPreRenderText += OnGenerated;
        }
        void OnVertices() => Vertices++;
        void OnLayout() => Layout++;
        void OnGenerated(TMP_TextInfo info) => Generated++;
        public void Reset() => Vertices = Layout = Generated = 0;
        public void Dispose()
        {
            _text.UnregisterDirtyVerticesCallback(OnVertices);
            _text.UnregisterDirtyLayoutCallback(OnLayout);
            _text.OnPreRenderText -= OnGenerated;
        }
    }
}

namespace MajdataPlay.Extensions { internal static class EmptyNamespace { } }
namespace MajdataPlay.i18n
{
    public sealed class Language { }
    public static class Localization { public static event EventHandler<Language> OnLanguageChanged; }
}
namespace MajdataPlay
{
    public static class TestTranslations { public static string i18n(this string key) => key; }
}
#endif
