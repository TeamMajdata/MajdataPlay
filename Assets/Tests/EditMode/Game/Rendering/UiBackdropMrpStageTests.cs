using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks backdrop boundaries using the actual MRP renderer and native camera draws.</summary>
    public sealed class UiBackdropMrpStageTests
    {
        /// <summary>Checks configured menu MSAA, ordered prefixes and stencil preservation across capture registration.</summary>
        /// <param name="samples">The configured MSAA count used by both menu and ordinary camera rendering.</param>
        /// <param name="optimizeAttachments">Whether supported Vulkan native segments are permitted.</param>
        [TestCase(1, false)]
        [TestCase(4, false)]
        [TestCase(1, true)]
        [TestCase(4, true)]
        [Category("GPU")]
        public void ActualMrp_RecordsPvBoardsCoverCategoriesAndPeripheralInOrder(int samples, bool optimizeAttachments)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Native MRP stage validation requires a graphics device.");
            }
            var oldPipeline = GraphicsSettings.defaultRenderPipeline;
            var oldQualityPipeline = QualitySettings.renderPipeline;
            var oldCamera = SceneSwitcher.MainCamera;
            var oldTarget = RenderTexture.active;
            var oldGuiZTest = Shader.GetGlobalInt("unity_GUIZTestMode");
            var oldButtons = Shader.GetGlobalTexture("_MajUiButtonBlurTexture");
            var oldButtonsReady = Shader.GetGlobalFloat("_MajUiButtonBlurReady");
            var oldClip = Shader.GetGlobalVector("_MajUiButtonClipUvX");
            var previousFactory = (Func<IMajCameraRenderExtension>?)typeof(MajCameraRenderExtensions)
                .GetField("_factory", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            using (var objects = new TestObjects())
            {
                var final = objects.Track(new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
                var prefixes = new RenderTexture[3];
                Assert.That(final.Create(), Is.True);
                for (var index = 0; index < prefixes.Length; index++)
                {
                    prefixes[index] = objects.Track(new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
                    Assert.That(prefixes[index].Create(), Is.True);
                }
                var pipeline = objects.Track(ScriptableObject.CreateInstance<MajRenderPipelineAsset>());
                var copy = Shader.Find("Hidden/Maj/FinalCopy");
                Assert.That(copy, Is.Not.Null);
                typeof(MajRenderPipelineAsset).GetField("_finalCopyShader", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pipeline, copy);
                var descriptor = new RenderTextureDescriptor(64, 64, RenderTextureFormat.ARGB32, 24) { msaaSamples = samples };
                Assert.That(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor), Is.EqualTo(samples),
                    "This native regression requires the requested sample count rather than a silent downgrade.");
                pipeline.ConfigureRuntimeRendering(1f, (MajMSAAMode)samples, false);
                typeof(MajRenderPipelineAsset).GetField("_optimizeAttachments", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pipeline, optimizeAttachments);
                Shader.SetGlobalInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
                var camera = objects.Add<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 1f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.cullingMask = 1 << 5;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.pixelRect = new Rect(0f, 0f, 64f, 64f);
                var mesh = objects.Track(new Mesh());
                mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 1f, 0f),
                    new Vector3(1f, 1f, 0f), new Vector3(1f, -1f, 0f) };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                mesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
                mesh.RecalculateBounds();
                var shader = Shader.Find("Maj/Common/Opaque");
                Assert.That(shader, Is.Not.Null);
                var queues = new[] { 2000, MajUiBackdropBridge.BoardQueue,
                    MajUiBackdropBridge.CategoryBackgroundQueue, MajUiBackdropBridge.CategoryQueue, 3100,
                    MajUiBackdropBridge.ButtonBodyQueue };
                var colors = new[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.magenta };
                var markers = new MeshRenderer[queues.Length];
                for (var index = 0; index < queues.Length; index++)
                {
                    var marker = objects.Create("Native MRP stage marker " + queues[index]);
                    marker.layer = 5;
                    marker.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var masked = index == 2 || index == 4 || index == 5;
                    var material = objects.Track(new Material(masked ? Shader.Find("Maj/UI/Atlas Alpha") : shader));
                    if (masked)
                    {
                        material.SetFloat("_Stencil", 1f);
                        material.SetFloat("_StencilComp", (float)CompareFunction.Equal);
                        material.SetFloat("_StencilWriteMask", 0f);
                    }
                    material.SetTexture("_MainTex", Texture2D.whiteTexture);
                    material.SetColor("_Color", colors[index]);
                    material.renderQueue = queues[index];
                    markers[index] = marker.AddComponent<MeshRenderer>();
                    markers[index].sharedMaterial = material;
                }
                var mask = objects.Create("Native persistent left-half stencil");
                mask.layer = 5;
                mask.transform.position = new Vector3(-0.5f, 0f, 0f);
                mask.transform.localScale = new Vector3(0.5f, 1f, 1f);
                mask.AddComponent<MeshFilter>().sharedMesh = mesh;
                var maskMaterial = objects.Track(new Material(Shader.Find("Maj/UI/Atlas Alpha")));
                maskMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                maskMaterial.SetFloat("_Stencil", 1f);
                maskMaterial.SetFloat("_StencilComp", (float)CompareFunction.Always);
                maskMaterial.SetFloat("_StencilOp", (float)StencilOp.Replace);
                maskMaterial.SetFloat("_ColorMask", 0f);
                maskMaterial.renderQueue = 2001;
                mask.AddComponent<MeshRenderer>().sharedMaterial = maskMaterial;
                // The real blue ring is only part of the PV prefix. A small marker leaves
                // an uncovered corner so a missing ring and an over-broad first capture differ.
                var ringColor = new Color(64f / 255f, 128f / 255f, 192f / 255f, 1f);
                var ring = objects.Create("PV blue-ring prefix marker");
                ring.layer = 5;
                ring.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
                ring.AddComponent<MeshFilter>().sharedMesh = mesh;
                var ringMaterial = objects.Track(new Material(shader));
                ringMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                ringMaterial.SetColor("_Color", ringColor);
                ringMaterial.renderQueue = MajUiBackdropBridge.BackgroundQueueUpperBound;
                ring.AddComponent<MeshRenderer>().sharedMaterial = ringMaterial;
                // The early opaque mask must reject later covered fragments across all capture
                // cuts. Capture padding is tested separately through the real filter service.
                var displayMaskSource = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Rendering/Materials/UI/DisplayMaskOpaque.mat");
                var displayMaskSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/1080Circle_Rev.png");
                Assert.That(displayMaskSource, Is.Not.Null);
                Assert.That(displayMaskSprite, Is.Not.Null);
                var displayMask = objects.Create("Early outside-circle depth and color mask");
                displayMask.layer = 5;
                displayMask.transform.position = new Vector3(0f, 0f, -0.5f);
                displayMask.AddComponent<MeshFilter>().sharedMesh = mesh;
                var displayMaskMaterial = objects.Track(new Material(displayMaskSource));
                displayMaskMaterial.SetTexture("_MainTex", displayMaskSprite.texture);
                Assert.That(displayMaskMaterial.renderQueue, Is.EqualTo(2000));
                var displayMaskRenderer = displayMask.AddComponent<MeshRenderer>();
                displayMaskRenderer.sharedMaterial = displayMaskMaterial;
                var events = new List<int>();
                var capture = new PrefixCapture(camera, prefixes, events);
                var extensionMaterial = objects.Track(new Material(shader));
                extensionMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                extensionMaterial.SetColor("_Color", Color.yellow);
                var extension = new MarkerExtension(camera, mesh, extensionMaterial, events);
                Func<IMajCameraRenderExtension> factory = () => extension;
                Action<RenderTargetIdentifier, CommandBuffer> finalCapture = (source, commands) => commands.Blit(source, final);
                var reader = objects.Track(new Texture2D(1, 1, TextureFormat.RGBA32, false, true));
                try
                {
                    SceneSwitcher.MainCamera = camera;
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                    QualitySettings.renderPipeline = pipeline;
                    MajCameraRenderExtensions.Register(factory);
                    MajUiBackdropBridge.Register(capture);
                    MajCameraCaptureBridge.AddCaptureAction(camera, finalCapture);
                    camera.Render();
                    var renderer = typeof(MajRenderPipeline).GetField("_renderer", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(RenderPipelineManager.currentPipeline)!;
                    var attachments = renderer.GetType().GetField("_attachments", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!;
                    var actualSamples = (int)attachments.GetType().GetProperty("Samples", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(attachments)!;
                    Assert.That(actualSamples, Is.EqualTo(samples), "Backdrop capture must respect the configured MSAA count.");
                    Assert.That(events, Is.EqualTo(new[] { 1, 10, 2, 3 }),
                        "The real renderer must capture PV before extensions, then boards/cover, then complete UI before peripherals.");
                    AssertColor(Read(prefixes[0], reader), ringColor, "RT1 must include the designated blue-ring prefix while excluding the later cover");
                    AssertColor(Read(prefixes[0], reader, 4, 4), Color.black, "The early mask must write opaque black and depth before the first capture");
                    AssertColor(Read(prefixes[1], reader, 16, 32), Color.blue, "RT2 includes completed boards and the cover, excluding categories");
                    AssertColor(Read(prefixes[2], reader, 16, 32), Color.cyan, "RT3 includes completed categories and ordinary UI, excluding peripheral bodies");
                    AssertColor(Read(final, reader, 16, 32), Color.magenta, "Peripheral bodies draw after their own capture");
                    AssertColor(Read(prefixes[1], reader, 48, 32), Color.green, "RT2 must retain stencil rejection on the right half");
                    AssertColor(Read(prefixes[2], reader, 48, 32), Color.yellow, "RT3 must retain stencil rejection after categories");
                    AssertColor(Read(final, reader, 48, 32), Color.yellow, "Final peripheral pass must retain stencil rejection");
                    AssertColor(Read(prefixes[2], reader, 60, 4), Color.black,
                        "The early mask depth must survive all capture cuts and reject later ordinary UI outside the circle");
                    AssertColor(Read(final, reader, 60, 4), Color.black,
                        "Covered final pixels must remain black without a repeated late color mask");
                    AssertColor(Read(final, reader, 4, 4), Color.black,
                        "The early mask must also reject the stencil-enabled peripheral fragments on the left");
                    var nativeExpected = optimizeAttachments && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan
                        && SystemInfo.supportsStoreAndResolveAction && !UnityEditor.Handles.ShouldRenderGizmos();
                    Assert.That(capture.NativeModes, Is.EqualTo(new[] { nativeExpected, nativeExpected, nativeExpected }));
                    Assert.That((bool)renderer.GetType().GetField("_usingBackdropNativePass", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!, Is.False);
                    Assert.That((bool)renderer.GetType().GetField("_backdropNativePassOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!, Is.False);
                    TestContext.WriteLine($"MRP_NATIVE_PREFIX api={SystemInfo.graphicsDeviceType} samples={actualSamples} optimize={optimizeAttachments} storeResolve={SystemInfo.supportsStoreAndResolveAction} native={nativeExpected} stencil=preserved");
                    displayMaskRenderer.enabled = false;
                    camera.Render();
                    AssertColor(Read(prefixes[0], reader, 4, 4), Color.red,
                        "The negative control must expose the background where the disabled early mask previously wrote depth");
                    AssertColor(Read(prefixes[2], reader, 4, 4), Color.cyan,
                        "The negative control must prove ordinary UI really covers the masked corner without early depth");
                    AssertColor(Read(final, reader, 4, 4), Color.magenta,
                        "The negative control must prove peripheral bodies also draw outside the circle when the early mask is disabled");
                    displayMaskRenderer.enabled = true;
                    // Reuse this actual pipeline fixture to verify the authored optical overlay,
                    // rather than simulating its order with a separate graphics command stream.
                    markers[5].enabled = false;
                    // Keep the synthetic full-screen prefix marker behind the real ring in this focused segment.
                    markers[4].sharedMaterial.renderQueue = 2999;
                    var frameObject = objects.Create("Authored liquid frame after RT3");
                    frameObject.layer = 5;
                    frameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var frameSource = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/List/CoverFrameGlass.mat");
                    Assert.That(frameSource, Is.Not.Null);
                    var frameMaterial = objects.Track(new Material(frameSource));
                    frameMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                    frameMaterial.SetFloat("_ListDepthOffset", 0f);
                    frameMaterial.SetFloat("_Refraction", 0f);
                    frameMaterial.SetFloat("_Transmission", 0.75f);
                    frameMaterial.SetFloat("_HighlightStrength", 0f);
                    frameMaterial.SetColor("_ShadowColor", Color.clear);
                    frameMaterial.SetFloat("_Stencil", 1f);
                    frameMaterial.SetFloat("_StencilComp", (float)CompareFunction.Equal);
                    var glassMesh = objects.Track(UnityEngine.Object.Instantiate(mesh));
                    glassMesh.SetUVs(0, new List<Vector4>
                    {
                        new Vector4(0f, 0f, 0.4f, 1f), new Vector4(0f, 1f, 0.4f, 1f),
                        new Vector4(1f, 1f, 0.4f, 1f), new Vector4(1f, 0f, 0.4f, 1f)
                    });
                    frameObject.GetComponent<MeshFilter>().sharedMesh = glassMesh;
                    var frameRenderer = frameObject.AddComponent<MeshRenderer>();
                    frameRenderer.sharedMaterial = frameMaterial;
                    var coverObject = objects.Create("Full-resolution cover after optical frame");
                    coverObject.layer = 5;
                    coverObject.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
                    coverObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var coverSource = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/List/CoverForegroundAlpha.mat");
                    Assert.That(coverSource, Is.Not.Null);
                    var coverMaterial = objects.Track(new Material(coverSource));
                    var coverColor = new Color(0.8f, 0.2f, 0.1f, 1f);
                    coverMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                    coverMaterial.SetColor("_Color", Color.white);
                    coverMaterial.SetFloat("_ListDepthOffset", 0f);
                    coverMaterial.SetFloat("_Stencil", 1f);
                    coverMaterial.SetFloat("_StencilComp", (float)CompareFunction.Equal);
                    Assert.That(coverMaterial.renderQueue, Is.GreaterThan(frameMaterial.renderQueue));
                    var coverMesh = objects.Track(UnityEngine.Object.Instantiate(mesh));
                    coverMesh.colors = new[] { coverColor, coverColor, coverColor, coverColor };
                    coverObject.GetComponent<MeshFilter>().sharedMesh = coverMesh;
                    var ringObject = objects.Create("Difficulty ring in the clearcoat capture prefix");
                    ringObject.layer = 5;
                    ringObject.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
                    var ringMesh = objects.Track(UnityEngine.Object.Instantiate(mesh));
                    ringMesh.colors = new[] { Color.green, Color.green, Color.green, Color.green };
                    ringObject.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                    var ringRenderer = ringObject.AddComponent<MeshRenderer>();
                    var prefixRingMaterial = objects.Track(new Material(coverMaterial) { renderQueue = 3000 });
                    ringRenderer.sharedMaterial = prefixRingMaterial;
                    var coverRenderer = coverObject.AddComponent<MeshRenderer>();
                    coverRenderer.sharedMaterial = coverMaterial;
                    var badge = objects.Create("Late difficulty badge queue marker");
                    badge.layer = 5;
                    badge.transform.position = new Vector3(-0.25f, 0.5f, 0f);
                    badge.transform.localScale = new Vector3(0.1f, 0.1f, 1f);
                    badge.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var badgeSource = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/List/CoverLabelForegroundAlpha.mat");
                    Assert.That(badgeSource, Is.Not.Null);
                    var badgeMaterial = objects.Track(new Material(badgeSource));
                    badgeMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                    badgeMaterial.SetColor("_Color", Color.magenta);
                    badgeMaterial.SetFloat("_Stencil", 1f);
                    badgeMaterial.SetFloat("_StencilComp", (float)CompareFunction.Equal);
                    badge.AddComponent<MeshRenderer>().sharedMaterial = badgeMaterial;
                    capture.PublishButtonTexture = true;
                    camera.Render();
                    AssertColor(Read(prefixes[2], reader, 5, 32), Color.cyan, "RT3 includes the ordinary prefix but excludes the optical frame itself.");
                    AssertColor(Read(prefixes[2], reader, 7, 32), Color.green, "RT3 must include the original difficulty ring before clearcoat.");
                    AssertColor(Read(prefixes[2], reader, 16, 32), Color.green, "The late full-resolution cover must not overwrite the captured ring.");
                    AssertColor(Read(prefixes[2], reader, 24, 48), Color.green, "The last difficulty badge must not contaminate RT3.");
                    AssertColor(Read(final, reader, 7, 32), Color.green, "Clearcoat must preserve captured difficulty RGB without adding a white ring.");
                    AssertColor(Read(final, reader, 24, 48), Color.magenta, "Difficulty badges must retain their color above both clearcoat and cover.");
                    AssertColor(Read(final, reader, 16, 32), coverColor, "The final cover must retain its original full-resolution color after the glass frame.");
                    var rim = Read(final, reader, 5, 32);
                    AssertColor(rim, new Color(0.25f, 1f, 1f, 1f), "Outside the colored ring, white glass must use the ordinary 25/75 composition.");
                    AssertColor(Read(final, reader, 48, 32), Color.yellow, "The frame cannot bypass the existing stencil.");
                    AssertColor(Read(final, reader, 4, 4), Color.black, "The frame cannot bypass the existing early outside-circle depth mask.");
                    var transition = objects.Create("Transition after optical frame");
                    transition.layer = 5;
                    transition.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var transitionMaterial = objects.Track(new Material(shader));
                    transitionMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
                    transitionMaterial.SetColor("_Color", Color.gray);
                    transitionMaterial.renderQueue = UiTransitionOverlay.OverlayQueue;
                    transition.AddComponent<MeshRenderer>().sharedMaterial = transitionMaterial;
                    camera.Render();
                    AssertColor(Read(prefixes[2], reader, 5, 32), Color.cyan, "The transition must not be copied into the frame's background.");
                    AssertColor(Read(final, reader, 5, 32), Color.gray, "The transition must cover the late optical frame.");
                    MajUiBackdropBridge.Unregister(capture);
                    camera.Render();
                    var nonMenuSamples = (int)attachments.GetType().GetProperty("Samples", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(attachments)!;
                    Assert.That(nonMenuSamples, Is.EqualTo(samples),
                        "Disabling the menu provider must preserve configured MSAA for ordinary cameras and gameplay.");
                    MajUiBackdropBridge.Register(capture);
                    camera.Render();
                    var resumedMenuSamples = (int)attachments.GetType().GetProperty("Samples", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(attachments)!;
                    Assert.That(resumedMenuSamples, Is.EqualTo(samples),
                        "Re-entering a backdrop menu must continue to respect configured MSAA.");
                }
                finally
                {
                    MajUiBackdropBridge.Unregister(capture);
                    MajCameraCaptureBridge.RemoveCaptureAction(camera, finalCapture);
                    MajCameraRenderExtensions.Unregister(factory);
                    if (previousFactory != null)
                    {
                        MajCameraRenderExtensions.Register(previousFactory);
                    }
                    GraphicsSettings.defaultRenderPipeline = oldPipeline;
                    QualitySettings.renderPipeline = oldQualityPipeline;
                    SceneSwitcher.MainCamera = oldCamera;
                    RenderTexture.active = oldTarget;
                    Shader.SetGlobalInt("unity_GUIZTestMode", oldGuiZTest);
                    Shader.SetGlobalTexture("_MajUiButtonBlurTexture", oldButtons);
                    Shader.SetGlobalFloat("_MajUiButtonBlurReady", oldButtonsReady);
                    Shader.SetGlobalVector("_MajUiButtonClipUvX", oldClip);
                }
            }
        }

        /// <summary>Reads the center of a completed real render target.</summary>
        /// <param name="source">The native output to inspect.</param>
        /// <param name="reader">A reusable one-texel readback texture.</param>
        /// <param name="x">The source column, or a negative value for the center.</param>
        /// <param name="y">The source row, or a negative value for the center.</param>
        /// <returns>The completed pixel at the requested source location.</returns>
        private static Color Read(RenderTexture source, Texture2D reader, int x = -1, int y = -1)
        {
            RenderTexture.active = source;
            reader.ReadPixels(new Rect(x < 0 ? source.width / 2 : x, y < 0 ? source.height / 2 : y, 1, 1), 0, 0);
            reader.Apply(false, false);
            return reader.GetPixel(0, 0);
        }

        /// <summary>Compares actual native output with an unambiguous solid queue marker.</summary>
        /// <param name="actual">The GPU output.</param>
        /// <param name="expected">The solid marker expected at this boundary.</param>
        /// <param name="message">The boundary whose exclusion is being protected.</param>
        private static void AssertColor(Color actual, Color expected, string message)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(1f / 255f + 0.00001f), message);
            }
        }

        /// <summary>Copies real renderer prefixes without substituting the renderer's queue splitting.</summary>
        private sealed class PrefixCapture : IMajUiCategoryBackdropCapture
        {
            /// <summary>Identifies the one camera owned by the fixture.</summary>
            private readonly Camera _camera;
            /// <summary>Stores independent prefix snapshots.</summary>
            private readonly RenderTexture[] _targets;
            /// <summary>Records synchronous callback order.</summary>
            private readonly List<int> _events;
            /// <summary>Stores the selected native mode at each closed capture boundary.</summary>
            internal List<bool> NativeModes { get; } = new List<bool>();
            /// <summary>Publishes the existing peripheral snapshot to the actual optical overlay after its prefix copy.</summary>
            internal bool PublishButtonTexture { get; set; }
            /// <summary>Observes actual attachment lifetime before external filtering.</summary>
            private void ObserveNativeBoundary()
            {
                var renderer = typeof(MajRenderPipeline).GetField("_renderer", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(RenderPipelineManager.currentPipeline)!;
                NativeModes.Add((bool)renderer.GetType().GetField("_usingBackdropNativePass", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!);
                Assert.That((bool)renderer.GetType().GetField("_backdropNativePassOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(renderer)!, Is.False);
            }
            /// <summary>Initializes the recorder.</summary>
            /// <param name="camera">The fixture camera.</param>
            /// <param name="targets">Three independent owned readback targets.</param>
            /// <param name="events">The actual renderer callback sequence.</param>
            internal PrefixCapture(Camera camera, RenderTexture[] targets, List<int> events)
            {
                _camera = camera;
                _targets = targets;
                _events = events;
            }
            /// <inheritdoc/>
            public bool IsActive(Camera camera)
            {
                return camera == _camera;
            }
            /// <inheritdoc/>
            public bool RequiresCategoryCapture(Camera camera)
            {
                return camera == _camera;
            }
            /// <inheritdoc/>
            public bool RequiresButtonCapture(Camera camera)
            {
                return camera == _camera;
            }
            /// <inheritdoc/>
            public void Record(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
                ObserveNativeBoundary();
                _events.Add(1);
                buffer.Blit(resolvedColor, _targets[0]);
            }
            /// <inheritdoc/>
            public void RecordCategories(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
                ObserveNativeBoundary();
                _events.Add(2);
                buffer.Blit(resolvedColor, _targets[1]);
            }
            /// <inheritdoc/>
            public void RecordButtons(CommandBuffer buffer, Camera camera, RenderTexture resolvedColor)
            {
                ObserveNativeBoundary();
                _events.Add(3);
                buffer.Blit(resolvedColor, _targets[2]);
                if (PublishButtonTexture)
                {
                    buffer.SetGlobalTexture("_MajUiButtonBlurTexture", _targets[2]);
                    buffer.SetGlobalFloat("_MajUiButtonBlurReady", 1f);
                    buffer.SetGlobalVector("_MajUiButtonClipUvX", Vector4.zero);
                }
            }
        }

        /// <summary>Uses the real extension hook to detect accidental contamination of the PV-only prefix.</summary>
        private sealed class MarkerExtension : IMajCameraRenderExtension
        {
            /// <summary>Identifies the accepted camera.</summary>
            private readonly Camera _camera;
            /// <summary>Draws a real native mesh through the extension's command stream.</summary>
            private readonly Mesh _mesh;
            /// <summary>Contains the extension marker color.</summary>
            private readonly Material _material;
            /// <summary>Records the extension's position among the three capture callbacks.</summary>
            private readonly List<int> _events;
            /// <summary>Initializes a non-owning extension fixture.</summary>
            /// <param name="camera">The accepted camera.</param>
            /// <param name="mesh">The fixture-owned marker quad.</param>
            /// <param name="material">The fixture-owned marker material.</param>
            /// <param name="events">The real synchronous render sequence.</param>
            internal MarkerExtension(Camera camera, Mesh mesh, Material material, List<int> events)
            {
                _camera = camera;
                _mesh = mesh;
                _material = material;
                _events = events;
            }
            /// <inheritdoc/>
            public bool Prepare(Camera camera)
            {
                return camera == _camera;
            }
            /// <inheritdoc/>
            public bool RequiresIntermediateTarget
            {
                get
                {
                    return false;
                }
            }
            /// <inheritdoc/>
            public void Render(CommandBuffer buffer, Camera camera, RenderTexture? colorTarget, RenderTexture? depthTarget, int width, int height)
            {
                _events.Add(10);
                buffer.DrawMesh(_mesh, Matrix4x4.identity, _material);
            }
            /// <inheritdoc/>
            public void EndCamera()
            {
            }
            /// <inheritdoc/>
            public void Dispose()
            {
            }
        }
    }
}
