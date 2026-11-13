using System.Text.RegularExpressions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks debug sensor material state stays above transition UI without changing shared source materials.</summary>
    public sealed class SensorOverlayMaterialTests
    {
        /// <summary>Checks the real sensor material and authored sorting layers remain later than the transition overlay.</summary>
        [Test]
        public void AuthoredSensorMaterial_StaysAboveTransitionAndGlobalMask()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/JudgeAreas.mat");
            Assert.That(material, Is.Not.Null);
            Assert.That(material!.shader.name, Is.EqualTo("Maj/Debug/SensorBorder"));
            Assert.That(material.renderQueue, Is.EqualTo(5000));
            Assert.That(material.renderQueue, Is.GreaterThan(UiTransitionOverlay.OverlayQueue));
            var debugLayer = SortingLayer.NameToID("Debug");
            var maskLayer = SortingLayer.NameToID("GlobalMask");
            Assert.That(debugLayer, Is.Not.Zero);
            Assert.That(maskLayer, Is.Not.Zero);
            Assert.That(SortingLayer.GetLayerValueFromID(debugLayer),
                Is.GreaterThan(SortingLayer.GetLayerValueFromID(maskLayer)));
        }

        /// <summary>Checks outside-circle masks retain their original early queues and shaders while debug sensors remain later.</summary>
        /// <param name="maskPath">The opaque or alpha-blended outside-circle mask material.</param>
        /// <param name="originalPath">The unchanged source material whose shader and blending must be retained.</param>
        /// <param name="expectedQueue">The original effective opaque or transparent render queue.</param>
        [TestCase("Assets/Rendering/Materials/UI/DisplayMaskOpaque.mat", "Assets/Rendering/Materials/UI/[UI]Opaque.mat", 2000)]
        [TestCase("Assets/Rendering/Materials/UI/DisplayMaskAlpha.mat", "Assets/Rendering/Materials/UI/Default.mat", 3000)]
        public void DisplayMask_RetainsEarlyQueueWithoutCoveringDebugSensors(string maskPath, string originalPath, int expectedQueue)
        {
            var mask = AssetDatabase.LoadAssetAtPath<Material>(maskPath);
            var original = AssetDatabase.LoadAssetAtPath<Material>(originalPath);
            var sensors = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/JudgeAreas.mat");
            Assert.That(mask, Is.Not.Null);
            Assert.That(original, Is.Not.Null);
            Assert.That(sensors, Is.Not.Null);
            Assert.That(mask.shader, Is.SameAs(original.shader));
            Assert.That(mask.renderQueue, Is.EqualTo(expectedQueue));
            Assert.That(mask.renderQueue, Is.EqualTo(original.renderQueue));
            Assert.That(mask.renderQueue, Is.LessThan(UiTransitionOverlay.OverlayQueue));
            Assert.That(mask.renderQueue, Is.LessThan(sensors.renderQueue));
            Assert.That(original.renderQueue, Is.LessThan(MajUiBackdropBridge.ButtonBodyQueue));
        }

        /// <summary>Checks List retains its enabled local mask and its original resource and mesh references.</summary>
        [Test]
        public void ListCircleMask_RetainsEnabledLocalImageAndReferences()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/List/Main_Display_ribbon.prefab");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/1080Circle_Rev.png");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/DisplayMaskOpaque.mat");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(sprite, Is.Not.Null);
            Assert.That(material, Is.Not.Null);
            var mask = prefab!.transform.Find("CircleMask");
            Assert.That(mask, Is.Not.Null);
            var image = mask!.GetComponent<Image>();
            Assert.That(image, Is.Not.Null);
            Assert.That(image!.enabled, Is.True);
            Assert.That(image.gameObject.activeSelf, Is.True);
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(image.material, Is.SameAs(material));
            Assert.That(image.useSpriteMesh, Is.False);
        }

        /// <summary>Checks both the global Init mask and the local Setting mask retain their enabled authored Images.</summary>
        /// <param name="scenePath">The authored scene whose exact serialized mask is inspected without loading it.</param>
        /// <param name="imageId">The preserved authored Image component identifier.</param>
        /// <param name="ownerId">The preserved authored GameObject identifier.</param>
        /// <param name="objectName">The exact local mask object name.</param>
        /// <param name="expectedEnabled">Whether the authored mask Image remains enabled.</param>
        /// <param name="expectedSpriteMesh">Whether the mask retains the original contour mesh rather than a quad.</param>
        [TestCase("Assets/Scenes/Init.unity", "410978429", "410978427", "Mask", true, true)]
        [TestCase("Assets/Scenes/Setting.unity", "1189213612", "1189213610", "CircleMask", true, false)]
        public void SceneCircleMask_RetainsEnabledGlobalAndLocalImages(string scenePath, string imageId, string ownerId,
            string objectName, bool expectedEnabled, bool expectedSpriteMesh)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/1080Circle_Rev.png");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/DisplayMaskOpaque.mat");
            Assert.That(sprite, Is.Not.Null);
            Assert.That(material, Is.Not.Null);
            var text = FileSystem.File.Open(scenePath).ReadAllText();
            var image = Regex.Match(text, @"^--- !u!114 &" + imageId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline);
            var owner = Regex.Match(text, @"^--- !u!1 &" + ownerId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.That(image.Success && owner.Success, Is.True, "The preserved Image and GameObject fileIDs must still exist.");
            Assert.That(owner.Groups["body"].Value, Does.Contain("m_Name: " + objectName));
            Assert.That(owner.Groups["body"].Value, Does.Contain("m_IsActive: 1"));
            Assert.That(owner.Groups["body"].Value, Does.Contain("component: {fileID: " + imageId + "}"));
            var body = image.Groups["body"].Value;
            Assert.That(body, Does.Contain("m_GameObject: {fileID: " + ownerId + "}"));
            Assert.That(body, Does.Contain("m_Script: {fileID: 11500000, guid: fe87c0e1cc204ed48ad3b37840f39efc"));
            Assert.That(body, Does.Contain("m_Enabled: " + (expectedEnabled ? "1" : "0")));
            Assert.That(body, Does.Contain("m_UseSpriteMesh: " + (expectedSpriteMesh ? "1" : "0")));
            Assert.That(body, Does.Contain("m_Sprite: {fileID: 21300000, guid: " + AssetDatabase.AssetPathToGUID("Assets/Sprites/1080Circle_Rev.png")));
            Assert.That(body, Does.Contain("m_Material: {fileID: 2100000, guid: " + AssetDatabase.AssetPathToGUID("Assets/Rendering/Materials/UI/DisplayMaskOpaque.mat")));
        }

        /// <summary>Checks actual sensor fragments remain visible when a preceding pass has filled the nearest depth.</summary>
        [Test]
        [Category("GPU")]
        public void SensorFragment_IgnoresEarlierDepthAndRetainsTransparentBlend()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Sensor depth state requires a real graphics device.");
            }
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/JudgeAreas.mat");
            Assert.That(source, Is.Not.Null);
            var savedTarget = RenderTexture.active;
            var target = new RenderTexture(16, 16, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            var material = new Material(source);
            try
            {
                Assert.That(target.Create(), Is.True);
                material.SetColor("_BaseColor", new Color(0f, 1f, 0f, 0.5f));
                material.SetVector("_SensorDisplayTransform", new Vector4(1f, 1f, 0f, 0f));
                RenderTexture.active = target;
                GL.Clear(true, true, Color.red, SystemInfo.usesReversedZBuffer ? 1f : 0f);
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    Assert.That(material.SetPass(0), Is.True);
                    GL.Begin(GL.QUADS);
                    GL.Vertex3(0f, 0f, 0.5f);
                    GL.Vertex3(0f, 1f, 0.5f);
                    GL.Vertex3(1f, 1f, 0.5f);
                    GL.Vertex3(1f, 0f, 0.5f);
                    GL.End();
                }
                finally
                {
                    GL.PopMatrix();
                }
                readback.ReadPixels(new Rect(0f, 0f, 16f, 16f), 0, 0);
                readback.Apply(false, false);
                var actual = readback.GetPixel(8, 8);
                Assert.That(actual.r, Is.EqualTo(0.5f).Within(0.015f));
                Assert.That(actual.g, Is.EqualTo(0.5f).Within(0.015f));
                Assert.That(actual.b, Is.EqualTo(0f).Within(0.015f));
                Assert.That(actual.a, Is.EqualTo(1f).Within(0.015f));
                Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
            }
            finally
            {
                RenderTexture.active = savedTarget;
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(target);
            }
        }
    }
}
