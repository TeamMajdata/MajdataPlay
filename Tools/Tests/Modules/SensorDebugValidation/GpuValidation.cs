using MajdataPlay.IO;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#nullable enable

namespace MajdataPlay.Tests.SensorDebugValidation
{
    /// <summary>Checks the production sensor shader through actual SRP culling, drawing, and GPU readback.</summary>
    public static class GpuValidation
    {
        /// <summary>Counts successful assertions.</summary>
        private static int s_assertions;

        /// <summary>Runs pixel validation in the isolated Unity project with a real graphics device.</summary>
        /// <exception cref="InvalidOperationException">A graphics device or pixel assertion fails.</exception>
        public static void Run()
        {
            Check(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A real graphics device is required.");
            var previousPipeline = GraphicsSettings.defaultRenderPipeline;
            var previousQualityPipeline = QualitySettings.renderPipeline;
            var previousActive = RenderTexture.active;
            var pipeline = ScriptableObject.CreateInstance<SensorGpuPipelineAsset>();
            var root = new GameObject("GPU sensor source outside the camera");
            root.SetActive(false);
            var cameraObject = new GameObject("GPU sensor validation camera");
            var mesh = CreateQuad();
            var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(512, 512, TextureFormat.RGBA32, false, true);
            try
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/JudgeAreas.mat");
                Check(material != null && material.shader.name == "Maj/Debug/SensorBorder", "The GPU test must use the production shader.");
                Check(!ShaderUtil.ShaderHasError(material!.shader), "The production shader must compile on this graphics backend.");
                root.transform.position = new Vector3(20f, 0f, 0f);
                var child = new GameObject("Single sensor with a known rectangular silhouette");
                child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                child.AddComponent<MeshCollider>().sharedMesh = mesh;
                var component = root.AddComponent<DummyTouchPanelRenderer>();
                typeof(DummyTouchPanelRenderer).GetField("_sharedInstancedMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, material);
                typeof(DummyTouchPanelRenderer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
                root.SetActive(true);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 4f;
                camera.aspect = 1f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                target.Create();
                camera.targetTexture = target;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = null;

                var settings = MajEnv.Settings.Debug;
                settings.DisplaySensor = true;
                settings.SensorStyle = SensorDebugStyle.Classic;
                settings.SensorOpacity = 20;
                settings.SensorPosition = SensorDebugPosition.MainScreen;
                Step(component);
                var pixels = Draw(camera, target, readback);
                Check(CountColoredPixels(pixels) == 0, "The untransformed source geometry must begin entirely outside the camera.");

                settings.SensorPosition = SensorDebugPosition.SubScreen;
                InputManager.SubScreenEdge = new Vector4(-2f, 3f, 2f, 1f);
                foreach (var opacity in new[] { 20, 90 })
                {
                    foreach (var active in new[] { false, true })
                    {
                        settings.SensorOpacity = opacity;
                        InputManager.TouchPanelRawData[0] = active;
                        Step(component);
                        pixels = Draw(camera, target, readback);
                        CheckPixels(pixels, -1f, 1f, 1f, 3f);
                        var source = active ? new Color(93f / 255f, 13f / 255f, 6f / 255f) : new Color(0f, 13f / 255f, 105f / 255f);
                        var alpha = opacity / 100f;
                        var expected = source * alpha + Color.white * (1f - alpha);
                        expected.a = 1f;
                        var actual = (Color)pixels[(384 * 512) + 256];
                        Check(ColorDistance(actual, expected) < 0.015f, "Classic RGB and selected opacity must blend correctly on the GPU.");
                        Debug.Log($"GPU_CLASSIC_PIXEL active={active} opacity={opacity} actual={actual} expected={expected}");
                    }
                }

                InputManager.SubScreenEdge = new Vector4(-1f, 1f, 3f, -3f);
                Step(component);
                pixels = Draw(camera, target, readback);
                CheckPixels(pixels, -1f, 3f, -3f, 1f);
                Check(renderer.bounds.center.x >= -1f && renderer.bounds.center.x <= 3f, "Production culling bounds must follow the mapped sensor.");
                Check(root.transform.position == new Vector3(20f, 0f, 0f), "Display remapping must not move input transforms.");

                settings.SensorPosition = SensorDebugPosition.MainScreen;
                Step(component);
                pixels = Draw(camera, target, readback);
                Check(CountColoredPixels(pixels) == 0, "Returning to the main screen must remove the mapped GPU geometry.");
                camera.transform.position = new Vector3(20f, 0f, -10f);
                pixels = Draw(camera, target, readback);
                CheckPixels(pixels, -1f, 1f, -1f, 1f);
                Check(renderer.bounds.center.x > 19f, "Returning to the main screen must restore ordinary culling bounds.");

                settings.DisplaySensor = false;
                Step(component);
                pixels = Draw(camera, target, readback);
                Check(!renderer.enabled && CountColoredPixels(pixels) == 0, "Disabling sensor debug must stop real GPU submission.");
                Debug.Log($"SENSOR_DEBUG_GPU_VALIDATION_PASSED ({s_assertions} assertions) api={SystemInfo.graphicsDeviceType} gpu={SystemInfo.graphicsDeviceName}");
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = previousPipeline;
                QualitySettings.renderPipeline = previousQualityPipeline;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(pipeline);
            }
        }

        /// <summary>Creates a rectangle with deterministic pixel bounds, without duplicating the production mapping algorithm.</summary>
        /// <returns>A readable two-triangle test mesh.</returns>
        private static Mesh CreateQuad()
        {
            var mesh = new Mesh
            {
                name = "Sensor GPU silhouette",
                vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Runs both production update stages before culling.</summary>
        /// <param name="component">The real production sensor renderer.</param>
        private static void Step(DummyTouchPanelRenderer component)
        {
            component.OnPreUpdate();
            typeof(DummyTouchPanelRenderer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
        }

        /// <summary>Renders with the minimal SRP and reads actual output pixels.</summary>
        /// <param name="camera">The validation camera.</param>
        /// <param name="target">The linear color target.</param>
        /// <param name="readback">Reusable CPU texture.</param>
        /// <returns>The rendered color pixels.</returns>
        private static Color32[] Draw(Camera camera, RenderTexture target, Texture2D readback)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            Check(RenderPipeline.SupportsRenderRequest(camera, request), "The test SRP must accept an explicit render request.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            readback.Apply(false, false);
            return readback.GetPixels32();
        }

        /// <summary>Measures the rendered silhouette against fixed requested display edges.</summary>
        /// <param name="pixels">Actual GPU output.</param>
        /// <param name="left">Expected left edge relative to the camera center.</param>
        /// <param name="right">Expected right edge relative to the camera center.</param>
        /// <param name="bottom">Expected bottom edge.</param>
        /// <param name="top">Expected top edge.</param>
        private static void CheckPixels(Color32[] pixels, float left, float right, float bottom, float top)
        {
            var minX = 512;
            var minY = 512;
            var maxX = -1;
            var maxY = -1;
            for (var y = 0; y < 512; y++)
            {
                for (var x = 0; x < 512; x++)
                {
                    if (!IsColored(pixels[(y * 512) + x]))
                    {
                        continue;
                    }
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
            Check(maxX >= 0, "A sensor whose CPU bounds and shader vertices are remapped into view must actually render.");
            var actual = new Vector4((minX / 64f) - 4f, ((maxX + 1) / 64f) - 4f, (minY / 64f) - 4f, ((maxY + 1) / 64f) - 4f);
            var expected = new Vector4(left, right, bottom, top);
            Check(Vector4.Distance(actual, expected) <= 0.032f, "GPU output must touch the requested vertical edges and remain horizontally centered.");
            Debug.Log($"GPU_SENSOR_EDGES actual={actual} expected={expected}");
        }

        /// <summary>Counts non-background pixels in an output frame.</summary>
        /// <param name="pixels">Actual GPU output.</param>
        /// <returns>The number of visibly colored pixels.</returns>
        private static int CountColoredPixels(Color32[] pixels)
        {
            var count = 0;
            foreach (var pixel in pixels)
            {
                if (IsColored(pixel))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Distinguishes the sensor from the white clear color.</summary>
        /// <param name="pixel">A rendered pixel.</param>
        /// <returns>Whether the pixel contains sensor color.</returns>
        private static bool IsColored(Color32 pixel)
        {
            return pixel.r < 250 || pixel.g < 250 || pixel.b < 250;
        }

        /// <summary>Measures the largest channel error in two colors.</summary>
        /// <param name="actual">The rendered color.</param>
        /// <param name="expected">The independently expected blend result.</param>
        /// <returns>The maximum channel error.</returns>
        private static float ColorDistance(Color actual, Color expected)
        {
            return Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g), Mathf.Abs(actual.b - expected.b), Mathf.Abs(actual.a - expected.a));
        }

        /// <summary>Records a pixel or device assertion.</summary>
        /// <param name="condition">Whether the assertion passed.</param>
        /// <param name="message">The failure description.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    /// <summary>Constructs only the culling and draw submission required to test the production sensor shader.</summary>
    internal sealed class SensorGpuPipelineAsset : RenderPipelineAsset
    {
        /// <summary>Identifies the isolated render pipeline without emitting an unimplemented-property warning.</summary>
        public override Type pipelineType => typeof(SensorGpuPipeline);

        /// <summary>Accepts untagged project sensor passes without requiring another render pipeline package.</summary>
        public override string renderPipelineShaderTag => string.Empty;

        /// <summary>Creates the isolated validation pipeline.</summary>
        /// <returns>The minimal sensor pipeline.</returns>
        protected override RenderPipeline CreatePipeline()
        {
            return new SensorGpuPipeline();
        }
    }

    /// <summary>Executes real culling and renderer submission without copying the production shader or display transform.</summary>
    internal sealed class SensorGpuPipeline : RenderPipeline
    {
        /// <summary>Renders any cameras requested by the Editor.</summary>
        /// <param name="context">The active render context.</param>
        /// <param name="cameras">The cameras to render.</param>
        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var camera in cameras)
            {
                if (camera.targetTexture != null)
                {
                    Draw(context, camera, camera.targetTexture);
                }
            }
        }

        /// <summary>Accepts the standard explicit offscreen request used by the pixel tests.</summary>
        /// <param name="camera">The requested camera.</param>
        /// <param name="data">The render request.</param>
        /// <typeparam name="RequestData">The request data type.</typeparam>
        /// <returns>Whether the request is supported.</returns>
        protected override bool IsRenderRequestSupported<RequestData>(Camera camera, RequestData data)
        {
            return data is StandardRequest;
        }

        /// <summary>Renders a requested frame through the actual ScriptableRenderContext.</summary>
        /// <param name="context">The active render context.</param>
        /// <param name="camera">The requested camera.</param>
        /// <param name="renderRequest">The destination request.</param>
        /// <typeparam name="RequestData">The request data type.</typeparam>
        protected override void ProcessRenderRequests<RequestData>(ScriptableRenderContext context, Camera camera, RequestData renderRequest)
        {
            if (renderRequest is StandardRequest request)
            {
                Draw(context, camera, request.destination);
            }
        }

        /// <summary>Clears, culls, sets camera matrices, draws the production shader tag, and submits GPU work.</summary>
        /// <param name="context">The active render context.</param>
        /// <param name="camera">The validation camera.</param>
        /// <param name="target">The color and depth target.</param>
        private static void Draw(ScriptableRenderContext context, Camera camera, RenderTexture target)
        {
            if (!camera.TryGetCullingParameters(out var parameters))
            {
                throw new InvalidOperationException("The GPU validation camera cannot provide culling parameters.");
            }
            var culling = context.Cull(ref parameters);
            context.SetupCameraProperties(camera);
            var command = new CommandBuffer { name = "Sensor GPU validation" };
            command.SetRenderTarget(target);
            command.SetViewport(new Rect(0f, 0f, target.width, target.height));
            command.ClearRenderTarget(true, true, Color.white);
            context.ExecuteCommandBuffer(command);
            command.Release();
            var sorting = new SortingSettings(camera) { criteria = SortingCriteria.CommonTransparent };
            var drawing = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sorting);
            var filtering = new FilteringSettings(RenderQueueRange.transparent);
#pragma warning disable CS0618
            context.DrawRenderers(culling, ref drawing, ref filtering);
#pragma warning restore CS0618
            context.Submit();
        }
    }
}
