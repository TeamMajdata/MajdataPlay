using System.Collections;
using MajdataPlay.Rendering;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
#nullable enable

namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Checks transition-only queue overrides without changing authored materials or unrelated UI.</summary>
    public sealed class UiTransitionOverlayPlayModeTests
    {
        /// <summary>Owns only the native UI objects and material instances created by this fixture.</summary>
        private TestObjects _objects = null!;

        /// <summary>Requires a real graphics device for valid shader keyword spaces and TMP materials.</summary>
        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("Transition shader-keyword and TMP material checks require a graphics device.");
            }
            _objects = new TestObjects();
        }

        /// <summary>Destroys helper-owned renderers before disposing their borrowed source materials.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects?.Dispose();
        }

        /// <summary>Checks late transition material selection preserves source state through pooling and explicit unbinding.</summary>
        /// <returns>The native Image lifecycle regression coroutine.</returns>
        [UnityTest]
        public IEnumerator ImageOverlay_PreservesSourceAndRestoresItAcrossDisableAndUnbind()
        {
            var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme");
            Assert.That(resources, Is.Not.Null);
            Assert.That(resources!.AlphaMaterial, Is.Not.Null);
            var source = _objects.Track(new Material(resources.AlphaMaterial!)
            {
                name = "Authored transition source",
                renderQueue = 3027
            });
            var materialTint = new Color(0.25f, 0.5f, 0.75f, 0.8f);
            source.SetColor("_Color", materialTint);
            source.SetFloat("_Stencil", 3f);
            source.SetFloat("_ListDepthOffset", 7.25f);
            source.SetFloat("_UseUIAlphaClip", 1f);
            source.EnableKeyword("UNITY_UI_ALPHACLIP");
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var image = _objects.Add<Image>("Transition", canvas.transform);
            var unrelated = _objects.Add<Image>("Ordinary UI", canvas.transform);
            image.material = source;
            unrelated.material = source;
            var tint = new Color(0.9f, 0.8f, 0.7f, 0.6f);
            image.color = tint;
            yield return null;
            Assert.That(source.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True);

            var overlay = UiTransitionOverlay.Ensure(image);
            Assert.That(UiTransitionOverlay.Ensure(image), Is.SameAs(overlay));
            var variant = image.materialForRendering;
            Assert.That(variant, Is.Not.SameAs(source));
            Assert.That(variant.renderQueue, Is.EqualTo(4000));
            Assert.That(variant.shader, Is.SameAs(source.shader));
            Assert.That(variant.GetColor("_Color"), Is.EqualTo(materialTint));
            Assert.That(variant.GetFloat("_Stencil"), Is.EqualTo(3f));
            Assert.That(variant.GetFloat("_ListDepthOffset"), Is.EqualTo(7.25f));
            Assert.That(variant.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True);
            Assert.That(image.material, Is.SameAs(source));
            Assert.That(unrelated.materialForRendering, Is.SameAs(source));
            Assert.That(image.color, Is.EqualTo(tint));
            Assert.That(source.renderQueue, Is.EqualTo(3027));

            overlay.enabled = false;
            yield return null;
            Assert.That(image.materialForRendering, Is.SameAs(source));
            Assert.That(variant == null, Is.True);
            overlay.enabled = true;
            var replacement = image.materialForRendering;
            Assert.That(replacement.renderQueue, Is.EqualTo(4000));
            overlay.Unbind();
            yield return null;
            Assert.That(image.materialForRendering, Is.SameAs(source));
            Assert.That(replacement == null, Is.True);
            Assert.That(source.GetColor("_Color"), Is.EqualTo(materialTint));
            Assert.That(source.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True);
            Assert.That(unrelated.materialForRendering, Is.SameAs(source));
        }

        /// <summary>Checks a TMP submesh created after initial binding joins the late transition layer without replacing font materials.</summary>
        /// <returns>The native TMP material and text-change-event regression coroutine.</returns>
        [UnityTest]
        public IEnumerator TextChanges_IncludeNewNativeSubMeshesAndUnbindThemTogether()
        {
            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Assert.That(font, Is.Not.Null);
            var source = _objects.Track(new Material(font!.material)
            {
                name = "Transition font source",
                renderQueue = 3000
            });
            var extraSource = _objects.Track(new Material(source)
            {
                name = "Dynamic transition submesh source"
            });
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var child = _objects.Create("Transition loading text", canvas.transform);
            child.SetActive(false);
            var text = child.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = source;
            text.text = "Loading";
            text.rectTransform.sizeDelta = new Vector2(400f, 100f);
            child.SetActive(true);
            text.ForceMeshUpdate();
            var overlay = UiTransitionOverlay.Ensure(text);
            var firstVariant = text.materialForRendering;
            Assert.That(firstVariant.renderQueue, Is.EqualTo(4000));
            Assert.That(text.fontSharedMaterial, Is.SameAs(source));

            var reference = new MaterialReference(1, font, null!, extraSource, 0f);
            var subMesh = TMP_SubMeshUI.AddSubTextObject(text, reference);
            _objects.Track(subMesh.gameObject);
            // Use the native notification consumed by TMP after generating additional material references.
            TMPro_EventManager.ON_TEXT_CHANGED(text);
            var subVariant = subMesh.materialForRendering;
            Assert.That(subMesh.GetComponent<UiTransitionOverlay>(), Is.Not.Null);
            Assert.That(subVariant.renderQueue, Is.EqualTo(4000));
            Assert.That(subVariant.shader, Is.SameAs(extraSource.shader));
            Assert.That(subVariant.GetTexture("_MainTex"), Is.SameAs(extraSource.GetTexture("_MainTex")));
            Assert.That(subMesh.sharedMaterial, Is.SameAs(extraSource));
            Assert.That(extraSource.renderQueue, Is.EqualTo(3000));

            overlay.Unbind();
            yield return null;
            Assert.That(text.materialForRendering, Is.SameAs(source));
            Assert.That(subMesh.materialForRendering, Is.SameAs(extraSource));
            Assert.That(firstVariant == null, Is.True);
            Assert.That(subVariant == null, Is.True);
            Assert.That(source.renderQueue, Is.EqualTo(3000));
            Assert.That(extraSource.renderQueue, Is.EqualTo(3000));
        }
    }
}
