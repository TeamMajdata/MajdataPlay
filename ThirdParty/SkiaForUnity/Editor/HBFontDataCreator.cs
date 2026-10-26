#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using SkiaSharp.Unity.HB;

public static class HBFontDataCreator {
    private static readonly string[] FontExtensions = { ".ttf", ".otf", ".woff", ".woff2" };

    [MenuItem("Assets/Create/SkiaForUnity/HB Font Data", true)]
    static bool ValidateCreate() {
        var obj = Selection.activeObject;
        if (obj == null) return false;
        string path = AssetDatabase.GetAssetPath(obj);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return System.Array.IndexOf(FontExtensions, ext) >= 0;
    }

    [MenuItem("Assets/Create/SkiaForUnity/HB Font Data")]
    static void Create() {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        var asset = CreateFromPath(path);
        if (asset != null) {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }

    public static bool IsFontFile(Object obj) {
        if (obj == null) return false;
        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path)) return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return System.Array.IndexOf(FontExtensions, ext) >= 0;
    }

    public static HBFontData CreateFromPath(string fontPath) {
        string dir = Path.GetDirectoryName(fontPath);
        string name = Path.GetFileNameWithoutExtension(fontPath);
        string assetPath = Path.Combine(dir, name + " HBFont.asset");

        var existing = AssetDatabase.LoadAssetAtPath<HBFontData>(assetPath);
        if (existing != null) return existing;

        var fontData = ScriptableObject.CreateInstance<HBFontData>();
        fontData.fontBytes = File.ReadAllBytes(fontPath);
        AssetDatabase.CreateAsset(fontData, assetPath);
        AssetDatabase.SaveAssets();
        return fontData;
    }
}
#endif
