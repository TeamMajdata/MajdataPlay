#if UNITY_EDITOR_LINUX
using UnityEditor;
using UnityEngine;
using System.IO;
using System;

public class LinuxLoadResolver : EditorWindow {
  private static string libraryStatus = "Not Checked";

  [MenuItem("SKIA/[HarfBuzz] Linux Load Resolver Status")]
  public static void ShowWindow() {
    GetWindow<LinuxLoadResolver>("Linux Load Resolver Status");
  }

  private void OnGUI() {
    GUILayout.Label("HarfBuzz Library Load Status", EditorStyles.boldLabel);
    GUILayout.Label($"Status: {libraryStatus}", EditorStyles.wordWrappedLabel);

    if (GUILayout.Button("Reload Library")) {
      CheckLibraryStatus();
    }
  }

  [InitializeOnLoadMethod]
  private static void Initialize() {
    EditorApplication.delayCall += CheckLibraryStatus;
  }

  private static void CheckLibraryStatus() {
    IntPtr result = LoadHarfBuzzLibrary();
    libraryStatus = result == IntPtr.Zero ? "Failed" : "Success";
    
    if (result == IntPtr.Zero) {
        Debug.LogWarning($"[SKIA] Library Load Status: {libraryStatus}. Try opening 'Skia/[HarfBuzz] Linux Load Resolver Status' to fix.");
    }
  }

  private static IntPtr LoadHarfBuzzLibrary() {
    string packagePath = GetCurrentPackagePath();

    if (string.IsNullOrEmpty(packagePath)) {
      Debug.LogError("[SKIA] Could not find package root (looking for package.json).");
      return IntPtr.Zero;
    }

    string libraryPath = Path.Combine(packagePath, "Library", "linux", "linux-x64", "native", "libHarfBuzzSharp.so");

    if (File.Exists(libraryPath)) {
      return HarfBuzzSharp.LibraryLoader.LoadLibrary(libraryPath);
    } else {
      Debug.LogError($"[SKIA] Library not found at path: {libraryPath}");
      return IntPtr.Zero;
    }
  }

  private static string GetCurrentPackagePath() {
    string[] guids = AssetDatabase.FindAssets("LinuxLoadResolver t:Script");
    if (guids.Length == 0) return null;
    string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);

    var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
    if (packageInfo != null) {
        return packageInfo.resolvedPath;
    }

    string absolutePath = Path.GetFullPath(assetPath);
    string currentDir = Path.GetDirectoryName(absolutePath);

    while (!string.IsNullOrEmpty(currentDir)) {
        if (File.Exists(Path.Combine(currentDir, "package.json"))) {
            return currentDir;
        }
        if (Directory.GetParent(currentDir) == null) break;
        currentDir = Directory.GetParent(currentDir).FullName;
    }

    return null;
  }
}
#endif