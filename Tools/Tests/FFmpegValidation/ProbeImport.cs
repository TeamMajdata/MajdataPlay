using System.IO;
using UnityEditor;

public static class FFmpegImportProbe
{
    public static void Run()
    {
        var importer = AssetImporter.GetAtPath("Assets/libavutil.so.61");
        File.WriteAllText("import-result.txt", importer == null ? "null" : importer.GetType().FullName);
        var android = AssetImporter.GetAtPath("Assets/libavutil.so") as PluginImporter;
        if (android != null)
            File.WriteAllText("android-import-result.txt", "Android enabled=" + android.GetCompatibleWithPlatform(BuildTarget.Android)
                + "; CPU=" + android.GetPlatformData(BuildTarget.Android, "CPU") + "; Any=" + android.GetCompatibleWithAnyPlatform());
    }
}
