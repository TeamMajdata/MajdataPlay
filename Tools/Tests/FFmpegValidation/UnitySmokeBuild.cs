using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class FFmpegPlayerSmokeBuild
{
    public static void Run()
    {
        var backend = FFmpegPlayerSmoke.Argument("-videoBackend") ?? "Mono";
        var architecture = FFmpegPlayerSmoke.Argument("-videoArchitecture") ?? "x64";
        var android = FFmpegPlayerSmoke.Argument("-videoPlatform") == "Android";
        var linux = FFmpegPlayerSmoke.Argument("-videoPlatform") == "Linux";
        var output = FFmpegPlayerSmoke.Argument("-videoOutput");
        var target = android ? BuildTarget.Android : linux ? BuildTarget.StandaloneLinux64
            : architecture == "x86" ? BuildTarget.StandaloneWindows : BuildTarget.StandaloneWindows64;
        var namedTarget = android ? NamedBuildTarget.Android : NamedBuildTarget.Standalone;
        PlayerSettings.SetScriptingBackend(namedTarget, backend == "IL2CPP" ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(namedTarget, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.High);
        PlayerSettings.SetIl2CppCompilerConfiguration(namedTarget,
            architecture == "x86" ? Il2CppCompilerConfiguration.Debug : Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, android
            ? new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 }
            : linux ? new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan }
            : new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore });
        if (android)
        {
            PlayerSettings.Android.targetArchitectures = architecture == "armv7" ? AndroidArchitecture.ARMv7 : AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.SetApplicationIdentifier(namedTarget, "net.majdata.ffmpegplayer.validation");
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        }
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 320; PlayerSettings.defaultScreenHeight = 240;
        PlayerSettings.runInBackground = true;
        PlayerSettings.allowUnsafeCode = true;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        Directory.CreateDirectory(output);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Smoke.unity" }, target = target,
            locationPathName = Path.Combine(output, android ? "VideoSmoke.apk" : linux ? "VideoSmoke.x86_64" : "VideoSmoke.exe"), options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Video Player build failed: " + report.summary.result);
        File.WriteAllText(Path.Combine(output, "build.txt"), "PASS: " + architecture + " " + backend + " High stripping");
    }
}
