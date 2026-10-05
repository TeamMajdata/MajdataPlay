using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        if (FFmpegPlayerSmoke.Argument("-cameraCapture") == "true")
        {
            var urp = FFmpegPlayerSmoke.Argument("-captureUrp") == "true";
            if (urp)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, "Assets/CaptureRenderer.asset");
                var pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, "Assets/CapturePipeline.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
            }
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.backgroundColor = Color.black;
            foreach (var top in new[] { true, false })
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.position = new Vector3(0, top ? 0.5f : -0.5f, 0);
                quad.transform.localScale = new Vector3(4, 1, 1);
                var shader = Shader.Find(urp ? "Universal Render Pipeline/Unlit" : "Unlit/Color");
                if (shader == null) { throw new Exception("The capture fixture shader is unavailable."); }
                var material = new Material(shader);
                material.SetColor(urp ? "_BaseColor" : "_Color", top ? Color.red : Color.blue);
                AssetDatabase.CreateAsset(material, top ? "Assets/CaptureRed.mat" : "Assets/CaptureBlue.mat");
                quad.GetComponent<Renderer>().sharedMaterial = material;
            }
            if (urp)
            {
                camera.cullingMask &= ~(1 << 5);
                var overlay = new GameObject("Capture overlay").AddComponent<Camera>();
                overlay.CopyFrom(camera);
                overlay.cullingMask = 1 << 5;
                overlay.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
                overlay.transform.position = camera.transform.position;
                camera.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
                var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                marker.layer = 5;
                marker.transform.position = new Vector3(0.75f, 0.5f, -1);
                marker.transform.localScale = new Vector3(0.2f, 0.2f, 1);
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", Color.green);
                AssetDatabase.CreateAsset(material, "Assets/CaptureOverlay.mat");
                marker.GetComponent<Renderer>().sharedMaterial = material;
            }
        }
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
