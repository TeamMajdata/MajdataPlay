using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class OniimaiBuild
{
    [MenuItem("MajdataPlay/Build Oniimai Android")]
    public static void Android()
    {
        OniimaiFrameChecks.Run();
        OniimaiStatsChecks.Run();
        var destination = Environment.GetEnvironmentVariable("ONIIMAI_APK");
        if (string.IsNullOrEmpty(destination)) destination = "Build/Oniimai-MajdataPlay.apk";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
        PlayerSettings.productName = "MajdataPlay Oniimai";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "net.majdata.majdataplay.oniimai");
        PlayerSettings.Android.bundleVersionCode = 22;
        PlayerSettings.bundleVersion = "2.0.3-oniimai.22";
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.buildApkPerCpuArchitecture = false;
        PlayerSettings.Android.splitApplicationBinary = false;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        PlayerSettings.Android.displayOptions = AndroidDisplayOptions.None;
        PlayerSettings.Android.optimizedFramePacing = false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.Android.useCustomKeystore = false;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            target = BuildTarget.Android,
            locationPathName = destination,
            options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Oniimai Android build failed: " + report.summary.result);
        if (!File.Exists(destination)) throw new BuildFailedException("Expected a single APK file: " + destination);
        Debug.Log("ONIIMAI_APK_READY " + destination);
    }
}
