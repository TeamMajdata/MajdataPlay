#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MajdataPlay.Tests.GraphicsStates
{
    /// <summary>Exercises real Android Vulkan tracing, serialization and production asynchronous warmup.</summary>
    public sealed class GraphicsStateDeviceProbe : MonoBehaviour
    {
        /// <summary>Starts the probe in its dedicated scene without serialized components.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            new GameObject(nameof(GraphicsStateDeviceProbe)).AddComponent<GraphicsStateDeviceProbe>();
        }

        private async void Start()
        {
            try
            {
                await RunAsync(destroyCancellationToken);
            }
            catch (Exception exception)
            {
                Debug.LogError("GRAPHICS_STATE_DEVICE_FAILED: " + exception);
                WriteResult("FAIL: " + exception);
            }
        }

        /// <summary>Runs rendering, trace, reload, metadata, cancellation, and warmup assertions.</summary>
        /// <param name="token">Stops the probe when its host is destroyed.</param>
        /// <returns>The asynchronous validation operation.</returns>
        /// <exception cref="InvalidOperationException">An expected rendering or collection contract fails.</exception>
        /// <exception cref="OperationCanceledException">The host is destroyed.</exception>
        private static async UniTask RunAsync(CancellationToken token)
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Require(Application.platform == RuntimePlatform.Android, "Expected an Android Player.");
            Require(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan, "Expected Vulkan.");
            Require(IntPtr.Size == 8, "Expected an ARM64 process.");
#if !ENABLE_IL2CPP
            throw new InvalidOperationException("Expected the IL2CPP backend.");
#endif
            var collection = new GraphicsStateCollection();
            GraphicsStateCollection? reloaded = null;
            try
            {
                Require(collection.BeginTrace(), "BeginTrace failed.");
                CreateRenderers();
                for (var frame = 0; frame < 120; frame++)
                {
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, token);
                }
                collection.EndTrace();
                Require(!collection.isTracing, "EndTrace did not stop tracing.");
                Require(collection.variantCount >= 3, "Expected at least three traced shader variants.");
                Require(collection.totalGraphicsStateCount >= 3, "Expected rendered graphics states.");
                var variants = new List<GraphicsStateCollection.ShaderVariant>();
                collection.GetVariants(variants);
                var names = new HashSet<string>();
                foreach (var variant in variants)
                {
                    names.Add(variant.shader.name);
                }
                Require(names.Contains("Maj/Sprites/Default"), "Project sprite shader was not traced: " + string.Join(",", names));
                Require(names.Contains("Maj/UI/Default"), "Project UI shader was not traced: " + string.Join(",", names));
                Require(names.Contains("Unlit/Color"), "Built-in shader was not traced.");
                var path = Application.persistentDataPath + "/probe.graphicsstate";
                Require(collection.SaveToFile(path), "SaveToFile failed.");
                reloaded = new GraphicsStateCollection();
                Require(reloaded.LoadFromFile(path), "LoadFromFile failed.");
                Require(reloaded.variantCount == collection.variantCount, "Variant count changed after reload.");
                Require(reloaded.totalGraphicsStateCount == collection.totalGraphicsStateCount, "State count changed after reload.");
                var quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
                Require(AndroidGraphicsStateWarmup.IsCompatible(reloaded, GraphicsDeviceType.Vulkan, quality), "Matching collection was rejected.");
                Require(!AndroidGraphicsStateWarmup.IsCompatible(reloaded, GraphicsDeviceType.OpenGLES3, quality), "Wrong graphics API was accepted.");
                Require(!AndroidGraphicsStateWarmup.IsCompatible(reloaded, GraphicsDeviceType.Vulkan, quality + "-different"), "Wrong quality was accepted.");
                Require(!AndroidGraphicsStateWarmup.IsCompatible(null, GraphicsDeviceType.Vulkan, quality), "Null collection was accepted.");
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                var canceledObserved = false;
                try
                {
                    await AndroidGraphicsStateWarmup.WarmUpAsync(reloaded, canceled.Token);
                }
                catch (OperationCanceledException)
                {
                    canceledObserved = true;
                }
                Require(canceledObserved, "Already canceled warmup did not cancel.");
                await AndroidGraphicsStateWarmup.WarmUpAsync(reloaded, token);
                Require(reloaded.isWarmedUp, "Warmup did not finish.");
                Require(reloaded.completedWarmupCount > 0, "No graphics states were warmed.");
                var result = "PASS: Android ARM64 IL2CPP Vulkan; GPU=" + SystemInfo.graphicsDeviceName +
                    "; quality=" + quality + "; variants=" + reloaded.variantCount +
                    "; states=" + reloaded.totalGraphicsStateCount + "; completed=" + reloaded.completedWarmupCount +
                    "; shaders=" + string.Join(",", names) + "; cancellation=passed; metadata=passed; trace/reload=passed";
                WriteResult(result);
                Debug.Log("GRAPHICS_STATE_DEVICE_PASSED: " + result);
            }
            finally
            {
                if (collection.isTracing)
                {
                    collection.EndTrace();
                }
                Destroy(collection);
                if (reloaded != null)
                {
                    Destroy(reloaded);
                }
            }
        }

        /// <summary>Creates actual camera, sprite, mesh, and overlay Canvas renderers.</summary>
        /// <exception cref="InvalidOperationException">A packaged probe material is missing.</exception>
        private static void CreateRenderers()
        {
            var camera = new GameObject("ProbeCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 3;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var spriteRenderer = new GameObject("ProjectSprite").AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1);
            spriteRenderer.sharedMaterial = Resources.Load<Material>("Probe/Material0") ?? throw new InvalidOperationException("Missing sprite material.");
            spriteRenderer.color = Color.green;
            spriteRenderer.transform.position = Vector3.left;
            var meshObject = new GameObject("BuiltInMesh");
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0, -1, 0), new Vector3(0, 1, 0), new Vector3(2, 1, 0), new Vector3(2, -1, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right },
                colors = new[] { Color.white, Color.white, Color.white, Color.white }
            };
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshObject.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Probe/Material2") ?? throw new InvalidOperationException("Missing built-in material.");
            var canvas = new GameObject("ProjectCanvas").AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var imageObject = new GameObject("ProjectImage", typeof(RectTransform));
            imageObject.transform.SetParent(canvas.transform, false);
            var graphic = imageObject.AddComponent<Image>();
            graphic.material = Resources.Load<Material>("Probe/Material1") ?? throw new InvalidOperationException("Missing UI material.");
            graphic.color = Color.red;
            graphic.rectTransform.sizeDelta = new Vector2(150, 150);
            graphic.rectTransform.anchoredPosition = new Vector2(0, 200);
            // Project shaders use SRPDefaultUnlit. Explicitly select their authored pass in this
            // small built-in-pipeline probe, without rewriting shaders or pretending to exercise MRP.
            var commands = new CommandBuffer { name = "Project shader pass trace" };
            commands.DrawMesh(mesh, Matrix4x4.Translate(new Vector3(-2, 0, 0)), spriteRenderer.sharedMaterial, 0, 0);
            commands.DrawMesh(mesh, Matrix4x4.Translate(new Vector3(0, -2, 0)), graphic.material, 0, 0);
            camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, commands);
        }

        /// <summary>Writes a durable result in the dedicated application's private external files.</summary>
        /// <param name="result">The pass/fail evidence to persist.</param>
        /// <exception cref="System.IO.IOException">The result file cannot be created or written.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to the result file is denied.</exception>
        private static void WriteResult(string result)
        {
            FileSystem.File.Create(Application.persistentDataPath + "/result.txt", overwrite: true).WriteAllText(result);
        }

        /// <summary>Rejects an unmet validation condition.</summary>
        /// <param name="condition">The condition that must be true.</param>
        /// <param name="message">The failure explanation.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
