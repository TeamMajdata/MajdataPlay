#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class StripArchitecturesPostProcess : IPostprocessBuildWithReport
{
    private const string LogPrefix = "[SkiaSharp]";

    private static readonly string[] ArchitecturesToStrip = { "x86_64", "i386", "x86" };

    public int callbackOrder => 999;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS)
            return;

#if !UNITY_EDITOR_OSX
        Debug.LogWarning($"{LogPrefix} iOS architecture stripping requires macOS. Skipping.");
        return;
#else
        string frameworksPath = Path.Combine(report.summary.outputPath, "Frameworks");

        if (!Directory.Exists(frameworksPath))
        {
            Debug.Log($"{LogPrefix} No Frameworks directory found at: {frameworksPath}");
            return;
        }

        string[] frameworkDirs = Directory.GetDirectories(frameworksPath, "*.framework");

        if (frameworkDirs.Length == 0)
        {
            Debug.Log($"{LogPrefix} No .framework bundles found in: {frameworksPath}");
            return;
        }

        foreach (string frameworkDir in frameworkDirs)
        {
            string frameworkName = Path.GetFileNameWithoutExtension(frameworkDir);
            string binaryPath = Path.Combine(frameworkDir, frameworkName);

            if (!File.Exists(binaryPath))
            {
                Debug.LogWarning($"{LogPrefix} Framework binary not found: {binaryPath}");
                continue;
            }

            StripNonArm64Architectures(binaryPath, frameworkDir);
        }
#endif
    }

#if UNITY_EDITOR_OSX
    private static void StripNonArm64Architectures(string binaryPath, string frameworkDir)
    {
        string lipoInfoOutput = RunProcess("lipo", $"-info \"{binaryPath}\"");

        if (string.IsNullOrEmpty(lipoInfoOutput))
        {
            Debug.LogWarning($"{LogPrefix} Could not determine architectures for: {binaryPath}");
            return;
        }

        bool didStrip = false;

        foreach (string arch in ArchitecturesToStrip)
        {
            if (!lipoInfoOutput.Contains(arch))
                continue;

            Debug.Log($"{LogPrefix} Stripping {arch} from: {binaryPath}");

            string result = RunProcess("lipo", $"-remove {arch} \"{binaryPath}\" -output \"{binaryPath}\"");

            if (result == null)
            {
                Debug.LogError($"{LogPrefix} Failed to strip {arch} from: {binaryPath}");
                return;
            }

            didStrip = true;
        }

        if (didStrip)
        {
            Debug.Log($"{LogPrefix} Re-codesigning: {frameworkDir}");

            string codesignResult = RunProcess("codesign",
                $"--force --sign - --preserve-metadata=identifier,entitlements \"{frameworkDir}\"");

            if (codesignResult == null)
            {
                Debug.LogError($"{LogPrefix} Failed to re-codesign: {frameworkDir}");
            }
            else
            {
                string verifyOutput = RunProcess("lipo", $"-info \"{binaryPath}\"");
                Debug.Log($"{LogPrefix} Post-strip architectures: {verifyOutput?.Trim()}");
            }
        }
        else
        {
            Debug.Log($"{LogPrefix} No stripping needed for: {Path.GetFileName(frameworkDir)} ({lipoInfoOutput.Trim()})");
        }
    }

    private static string RunProcess(string fileName, string arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Debug.LogError($"{LogPrefix} Process failed: {fileName} {arguments}\n"
                        + $"Exit code: {process.ExitCode}\n"
                        + $"stderr: {stderr}");
                    return null;
                }

                return string.IsNullOrEmpty(stdout) ? stderr : stdout;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{LogPrefix} Exception running {fileName}: {ex.Message}");
            return null;
        }
    }
#endif
}
#endif
