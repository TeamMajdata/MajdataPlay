using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Exercises quality-aware Images through real Play Mode lifecycle and Selectable state changes.</summary>
    public sealed class QualityUiImagePlayModeTests
    {
        /// <summary>Owns only objects created by each test, never imported project assets.</summary>
        private TestObjects _objects = null!;

        /// <summary>Retains the project's real unoutlined Sprite slices.</summary>
        private Sprite[] _sprites = null!;

        /// <summary>Provides an original cutout material that must remain unchanged.</summary>
        private Material _cutout = null!;

        /// <summary>Creates independent runtime state and loads the actual paired artwork.</summary>
        [SetUp]
        public void SetUp()
        {
            Assert.That(Application.isPlaying, Is.True);
            ResetTheme();
            _objects = new TestObjects();
#if UNITY_EDITOR
            var assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Ribbon/newui@3x.png");
            var sprites = new List<Sprite>();
            foreach (var asset in assets)
            {
                if (asset is Sprite sprite)
                {
                    sprites.Add(sprite);
                }
            }
            _sprites = sprites.ToArray();
#else
            Assert.Ignore("This fixture requires the imported project artwork and currently runs in Editor Play Mode.");
            _sprites = Array.Empty<Sprite>();
#endif
            Assert.That(_sprites.Length, Is.GreaterThanOrEqualTo(2));
            var shader = Shader.Find("Maj/Common/Opaque");
            Assert.That(shader, Is.Not.Null);
            _cutout = _objects.Track(new Material(shader) { name = "Original quality-test cutout" });
            _cutout.SetFloat("_ListDepthOffset", 151f);
            _cutout.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 0.7f));
            _cutout.SetVector("_MainTex_ST", new Vector4(0.75f, 0.5f, 0.125f, 0.25f));
        }

        /// <summary>Destroys test objects and releases the generated theme material cache.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects?.Dispose();
            ResetTheme();
            UiAtlasMeshGenerator.InvalidateSpriteMappings();
        }

        /// <summary>Checks cached imported mappings follow Sprite Swap, reject unrelated textures and reload edited metadata.</summary>
        [Test]
        public void SpriteMappings_CacheSuccessfulPairsAndInvalidateEditedResources()
        {
            UiOutlineTheme.Configure(UiOutlineMode.All);
            var first = _sprites[0];
            var second = _sprites[1];
            Assert.That(UiAtlasMeshGenerator.TryGetLegacy(first, out var legacy, out var uv), Is.True);
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(first, out var outlined), Is.True);
            var image = CreateImage(_cutout);
            image.sprite = first;
            Assert.That(image.mainTexture, Is.SameAs(outlined.texture));
            image.overrideSprite = second;
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(second, out var secondOutline), Is.True);
            Assert.That(image.mainTexture, Is.SameAs(secondOutline.texture));
            image.overrideSprite = first;
            Assert.That(image.mainTexture, Is.SameAs(outlined.texture));

            var foreignTexture = _objects.Track(new Texture2D(2, 2) { name = "Unrelated atlas" });
            var foreign = _objects.Track(Sprite.Create(foreignTexture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f));
            foreign.name = first.name;
            Assert.That(UiAtlasMeshGenerator.TryGetLegacy(foreign, out _, out _), Is.False);
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(foreign, out _), Is.False);

            UiAtlasMeshGenerator.InvalidateSpriteMappings();
            UiOutlineTheme.InvalidateSpriteMappings();
            Assert.That(UiAtlasMeshGenerator.TryGetLegacy(first, out var reloaded, out var reloadedUv), Is.True);
            Assert.That(reloaded, Is.SameAs(legacy));
            Assert.That(reloadedUv, Is.EqualTo(uv));
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(first, out var reloadedOutline), Is.True);
            Assert.That(reloadedOutline, Is.SameAs(outlined));
            UiOutlineTheme.Configure(UiOutlineMode.None);
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(first, out _), Is.False);
            UiOutlineTheme.Configure(UiOutlineMode.All);
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(first, out var restored), Is.True);
            Assert.That(restored, Is.SameAs(outlined));

            var resizedTexture = _objects.Track(new Texture2D(first.texture.width, first.texture.height,
                TextureFormat.Alpha8, false) { name = first.texture.name });
            var clone = _objects.Track(Sprite.Create(resizedTexture, first.rect, first.pivot / first.rect.size,
                first.pixelsPerUnit, 0, SpriteMeshType.FullRect, first.border));
            clone.name = first.name;
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(clone, out var cloneOutline), Is.True);
            Assert.That(cloneOutline, Is.SameAs(outlined));
            Assert.That(resizedTexture.Reinitialize(first.texture.width / 2, first.texture.height,
                TextureFormat.Alpha8, false), Is.True);
            LogAssert.Expect(LogType.Error, new Regex("Outlined UI resources are missing"));
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(clone, out _), Is.False,
                "Runtime texture resizing must invalidate an otherwise unchanged Sprite pair.");
            Assert.That(resizedTexture.Reinitialize(first.texture.width, first.texture.height,
                TextureFormat.Alpha8, false), Is.True);
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(clone, out var recovered), Is.True,
                "A failed validation must not remain negatively cached after dimensions recover.");
            Assert.That(recovered, Is.SameAs(outlined));
        }

        /// <summary>Checks style variants follow Configure on the owned surface and its installed UGUI stencil clone.</summary>
        /// <returns>The real Canvas rebuild and style-switch lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator BackdropStyle_LocalKeywordTracksOwnedAndStencilWithoutReplacingArtwork()
        {
            var root = _objects.Track(new GameObject("Glass style Canvas", typeof(RectTransform), typeof(Canvas)));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var maskObject = _objects.Track(new GameObject("Glass style Mask", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Mask)));
            maskObject.transform.SetParent(root.transform, false);
            maskObject.GetComponent<RectTransform>().sizeDelta = new Vector2(800f, 800f);
            maskObject.GetComponent<Image>().sprite = _sprites[0];
            var image = CreateImage(_cutout);
            image.transform.SetParent(maskObject.transform, false);
            image.color = new Color(0.8f, 0.7f, 0.6f, 0.45f);
            image.rectTransform.sizeDelta = new Vector2(254.69f, 82.3f);
            image.gameObject.AddComponent<UiBackdropPanel>();
            var sprite = image.sprite;
            var color = image.color;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
            var owned = image.material;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var rendered = image.canvasRenderer.GetMaterial(0);
            Assert.That(rendered, Is.Not.Null);
            Assert.That(rendered, Is.Not.SameAs(owned), "An actual parent Mask must install a separate stencil material.");
            Assert.That(rendered.GetFloat("_Stencil"), Is.GreaterThan(0f));

            foreach (var style in new[] { UiBackdropStyle.LiquidGlass, UiBackdropStyle.GaussianBlur, UiBackdropStyle.LiquidGlass })
            {
                UiPvBackdropBlur.Configure(true, style);
                var liquid = style == UiBackdropStyle.LiquidGlass;
                Assert.That(owned.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword), Is.EqualTo(liquid));
                Assert.That(rendered.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword), Is.EqualTo(liquid),
                    "The installed clone must switch immediately, before its next Canvas rebuild.");
                Canvas.ForceUpdateCanvases();
                yield return null;
                rendered = image.canvasRenderer.GetMaterial(0);
                Assert.That(rendered.IsKeywordEnabled(UiBackdropPanel.LiquidStyleKeyword), Is.EqualTo(liquid));
                Assert.That(image.material, Is.SameAs(owned));
                Assert.That(image.SourceMaterial, Is.SameAs(_cutout));
                Assert.That(image.sprite, Is.SameAs(sprite));
                Assert.That(image.color, Is.EqualTo(color));
            }
        }

        /// <summary>Checks both quality directions without mutating Image source data or layout.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator QualityChange_SwitchesTextureAndMaterialWithoutChangingSourceState()
        {
            var image = CreateImage(_cutout);
            image.type = Image.Type.Sliced;
            image.color = new Color(0.9f, 0.7f, 0.5f, 0.3f);
            var rect = image.rectTransform;
            rect.anchoredPosition = new Vector2(17f, -31f);
            rect.sizeDelta = new Vector2(254.69f, 82.3f);
            var source = image.sprite;
            var color = image.color;
            var position = rect.anchoredPosition;
            var size = rect.sizeDelta;
            yield return null;
            Assert.That(image.mainTexture, Is.SameAs(source.texture));
            Assert.That(image.material, Is.SameAs(_cutout));

            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(source, out var outlined), Is.True);
            Assert.That(image.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(image.material, Is.Not.SameAs(_cutout));
            Assert.That(image.material.shader.name, Is.EqualTo("Maj/UI/Atlas Alpha"));
            Assert.That(image.material.renderQueue, Is.EqualTo(3000));
            Assert.That(image.material.GetFloat("_ListDepthOffset"), Is.EqualTo(151f));
            Assert.That(image.material.GetColor("_Color"), Is.EqualTo(_cutout.GetColor("_Color")));
            Assert.That(image.material.GetVector("_MainTex_ST"), Is.EqualTo(_cutout.GetVector("_MainTex_ST")));
            Assert.That(image.material.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.False);
            Assert.That(image.material.GetFloat("_UseUIAlphaClip"), Is.Zero);
            AssertSourceState(image, source, color, position, size);
            Assert.That(_cutout.shader.name, Is.EqualTo("Maj/Common/Opaque"));

            UiOutlineTheme.Configure(UiOutlineMode.None);
            yield return null;
            Assert.That(image.mainTexture, Is.SameAs(source.texture));
            Assert.That(image.material, Is.SameAs(_cutout));
            AssertSourceState(image, source, color, position, size);
        }

        /// <summary>Checks alpha source materials and tint survive automatic mode-dependent material selection.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator TransparentMaterials_KeepTheirSourceMaterialAndTint()
        {
            foreach (var shaderName in new[] { "Maj/UI/Default", "Maj/UI/Rounded Rect" })
            {
                var shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, shaderName);
                var material = _objects.Track(new Material(shader));
                material.SetColor("_Color", new Color(0.4f, 0.5f, 0.6f, 0.25f));
                var image = CreateImage(material);
                image.color = new Color(1f, 1f, 1f, 0.35f);
                UiOutlineTheme.Configure(UiOutlineMode.All);
                yield return null;
                Assert.That(UiOutlineTheme.TryGetOutlinedSprite(image.sprite, out var outlined), Is.True);
                Assert.That(image.mainTexture, Is.SameAs(outlined.texture));
                Assert.That(image.material, Is.SameAs(material));
                Assert.That(image.color.a, Is.EqualTo(0.35f));
                Assert.That(material.GetColor("_Color").a, Is.EqualTo(0.25f));
                UiOutlineTheme.Configure(UiOutlineMode.None);
                yield return null;
                Assert.That(image.material, Is.Not.SameAs(material));
                Assert.That(image.SourceMaterial, Is.SameAs(material));
                Assert.That(image.material.shader.name, Is.EqualTo(shaderName == "Maj/UI/Default"
                    ? "Maj/UI/Atlas Cutoff" : "Maj/UI/Rounded Rect"));
                Assert.That(image.mainTexture, Is.SameAs(image.sprite.texture));
            }
        }

        /// <summary>Checks real Button Sprite Swap callbacks without replacing the Button's authored SpriteState.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator ButtonSpriteSwap_UsesOutlinedTextureWhileRetainingHardSpriteReferences()
        {
            var image = CreateImage(_cutout);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            var state = new SpriteState { highlightedSprite = _sprites[1], pressedSprite = _sprites[1], selectedSprite = _sprites[1] };
            button.spriteState = state;
            var source = image.sprite;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;
            button.OnPointerEnter(new PointerEventData(null!));
            yield return null;
            Assert.That(image.overrideSprite, Is.SameAs(_sprites[1]));
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(image.overrideSprite, out var outlined), Is.True);
            Assert.That(outlined.name, Is.EqualTo(_sprites[1].name));
            Assert.That(image.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(image.sprite, Is.SameAs(source));
            Assert.That(button.spriteState, Is.EqualTo(state));
            button.OnPointerExit(new PointerEventData(null!));
            yield return null;
            Assert.That(image.overrideSprite, Is.SameAs(source));
            Assert.That(button.spriteState, Is.EqualTo(state));
        }

        /// <summary>Checks pooled Image registration follows real disable and enable callbacks.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator Pooling_UnregistersDisabledImagesAndAppliesTheLatestThemeOnEnable()
        {
            var image = CreateImage(_cutout);
            yield return null;
            Assert.That(RegisteredImages(), Has.Member(image));
            image.gameObject.SetActive(false);
            Assert.That(RegisteredImages(), Has.No.Member(image));
            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;
            image.gameObject.SetActive(true);
            yield return null;
            Assert.That(RegisteredImages().Count, Is.EqualTo(1));
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(image.sprite, out var outlined), Is.True);
            Assert.That(image.mainTexture, Is.SameAs(outlined.texture));
            image.enabled = false;
            Assert.That(RegisteredImages(), Has.No.Member(image));
            image.enabled = true;
            Assert.That(RegisteredImages().Count, Is.EqualTo(1));
            UiOutlineTheme.Configure(UiOutlineMode.None);
            yield return null;
            Assert.That(image.mainTexture, Is.SameAs(image.sprite.texture));
        }

        /// <summary>Checks authored cover decorations switch only in liquid mode while their cover images and startup pools stay unchanged.</summary>
        /// <returns>The material-selection and unchanged-source lifecycle regression.</returns>
        [UnityTest]
        public IEnumerator CoverDecoration_UsesLiquidOnlyAndPreservesCoverSourcesAndPools()
        {
#if UNITY_EDITOR
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab");
            var thumbnailSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/List/Model/ThumbnailDisplayer.prefab");
            var frame = CreateImage(_cutout);
            var cover = CreateImage(_cutout);
            var thumbnail = CreateImage(_cutout);
            var ring = CreateImage(_cutout);
            var badge = CreateImage(_cutout);
            var badgeSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/List/Model/LevelDisplayer.prefab");
            EditorUtility.CopySerialized(source.transform.Find("LevelColor").GetComponent<QualityUiImage>(), ring);
            EditorUtility.CopySerialized(badgeSource.transform.Find("Background").GetComponent<QualityUiImage>(), badge);
            EditorUtility.CopySerialized(source.transform.Find("BG").GetComponent<QualityUiImage>(), frame);
            EditorUtility.CopySerialized(source.transform.Find("SongCover").GetComponent<QualityUiImage>(), cover);
            EditorUtility.CopySerialized(thumbnailSource.transform.Find("Cover").GetComponent<QualityUiImage>(), thumbnail);
            var ringColor = new Color(0.2f, 0.4f, 0.8f, 0.75f);
            ring.color = ringColor;
            frame.rectTransform.sizeDelta = new Vector2(200f, 200f);
            ring.rectTransform.sizeDelta = new Vector2(168f, 168f);
            typeof(QualityUiImage).GetField("_glassInlaySource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(frame, ring);
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(false, UiBackdropStyle.GaussianBlur);
            yield return null;
            var original = frame.SourceMaterial;
            var ordinaryFrame = frame.material;
            var coverMaterial = cover.material;
            var coverSourceMaterial = cover.SourceMaterial;
            var thumbnailMaterial = thumbnail.material;
            var ringMaterial = ring.material;
            var badgeMaterial = badge.material;
            var frameSprite = frame.sprite;
            var coverSprite = cover.sprite;
            var thumbnailSprite = thumbnail.sprite;
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            yield return null;
            Assert.That(frame.material.shader.name, Is.EqualTo("Maj/UI/List/Cover Decoration Glass"));
            Assert.That(frame.material.GetFloat("_ListDepthOffset"), Is.EqualTo(150.5f));
            Assert.That(frame.material.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonBodyQueue));
            Assert.That(ring.material, Is.SameAs(ringMaterial), "The original difficulty ring must be captured before the white glass.");
            Assert.That(ring.material.renderQueue, Is.LessThan(MajUiBackdropBridge.ButtonBodyQueue));
            Assert.That(badge.material.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound + 1));
            Assert.That(cover.material.shader.name, Is.EqualTo("Maj/UI/List/Cover Alpha"));
            Assert.That(cover.material.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound));
            Assert.That(cover.SourceMaterial, Is.SameAs(coverSourceMaterial));
            var authoredColor = frame.color;
            var sharedFrameMaterial = frame.material;
            var populate = typeof(QualityUiImage).GetMethod("OnPopulateMesh", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(VertexHelper) }, null)!;
            using var vertices = new VertexHelper();
            populate.Invoke(frame, new object[] { vertices });
            var vertex = new UIVertex();
            vertices.PopulateUIVertex(ref vertex, 0);
            Assert.That(((Color)vertex.color).r, Is.EqualTo(authoredColor.r).Within(1f / 255f));
            Assert.That(((Color)vertex.color).b, Is.EqualTo(authoredColor.b).Within(1f / 255f));
            Assert.That(frame.color, Is.EqualTo(Color.white), "The liquid frame must retain its authored white color.");
            Assert.That(frame.material, Is.SameAs(sharedFrameMaterial), "The white glass frame must retain its shared static material.");
            Assert.That(vertex.uv0.z, Is.EqualTo(0.42f).Within(0.00001f));
            Assert.That(vertex.uv0.w, Is.EqualTo(0.75f));
            var position = vertex.position;
            var uv = new Vector2(vertex.uv0.x, vertex.uv0.y);
            var color = vertex.color;
            frame.rectTransform.sizeDelta = new Vector2(320f, 320f);
            ring.rectTransform.sizeDelta = new Vector2(256f, 256f);
            ring.color = new Color(ringColor.r, ringColor.g, ringColor.b, 0.3f);
            populate.Invoke(frame, new object[] { vertices });
            vertices.PopulateUIVertex(ref vertex, 0);
            Assert.That(vertex.uv0.z, Is.EqualTo(0.4f).Within(0.00001f));
            Assert.That(vertex.uv0.w, Is.EqualTo(0.3f));
            frame.rectTransform.sizeDelta = new Vector2(200f, 200f);
            ring.rectTransform.sizeDelta = new Vector2(168f, 168f);
            ring.color = ringColor;
            populate.Invoke(frame, new object[] { vertices });
            vertices.PopulateUIVertex(ref vertex, 0);
            Assert.That(vertex.position, Is.EqualTo(position));
            Assert.That(new Vector2(vertex.uv0.x, vertex.uv0.y), Is.EqualTo(uv));
            Assert.That(vertex.color, Is.EqualTo(color), "Inlay metadata must not alter native vertex color, alpha or UV XY.");
            populate.Invoke(ring, new object[] { vertices });
            Assert.That(vertices.currentVertCount, Is.GreaterThan(0), "Liquid must retain the original colored ring in the capture prefix.");
            vertices.PopulateUIVertex(ref vertex, 0);
            Assert.That(((Color)vertex.color).r, Is.EqualTo(ringColor.r).Within(1f / 255f));
            Assert.That(((Color)vertex.color).b, Is.EqualTo(ringColor.b).Within(1f / 255f));
            Assert.That(ring.color, Is.EqualTo(ringColor));
            var poolNames = new[] { "s_liquidCategoryTexture", "s_liquidButtonTexture", "s_boardBlurTexture", "s_categoryBlurTexture", "s_buttonBlurTexture", "s_blurScratchTexture" };
            var retained = new object?[poolNames.Length];
            for (var index = 0; index < poolNames.Length; index++)
            {
                retained[index] = typeof(UiPvBackdropBlur).GetField(poolNames[index], BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            }
            foreach (var style in new[] { UiBackdropStyle.GaussianBlur, UiBackdropStyle.LiquidGlass })
            {
                UiPvBackdropBlur.Configure(style == UiBackdropStyle.GaussianBlur, style);
                yield return null;
                Assert.That(frame.material, Is.SameAs(ordinaryFrame), "Gaussian and disabled liquid mode must restore the existing themed frame.");
                Assert.That(frame.SourceMaterial, Is.SameAs(original));
                Assert.That(frame.sprite, Is.SameAs(frameSprite));
                Assert.That(cover.material, Is.SameAs(coverMaterial));
                Assert.That(cover.sprite, Is.SameAs(coverSprite));
                Assert.That(thumbnail.material, Is.SameAs(thumbnailMaterial));
                Assert.That(thumbnail.sprite, Is.SameAs(thumbnailSprite));
                Assert.That(ring.material, Is.SameAs(ringMaterial));
                Assert.That(badge.material, Is.SameAs(badgeMaterial));
                Assert.That(frame.material.renderQueue, Is.LessThan(MajUiBackdropBridge.ButtonBodyQueue));
                populate.Invoke(frame, new object[] { vertices });
                vertices.PopulateUIVertex(ref vertex, 0);
                Assert.That(((Color)vertex.color).r, Is.EqualTo(authoredColor.r).Within(1f / 255f), "Style switching must preserve the authored white frame vertices.");
                Assert.That(vertex.uv0.z, Is.Zero);
                Assert.That(vertex.uv0.w, Is.Zero, "Gaussian and disabled styles must keep ordinary UV metadata.");
                populate.Invoke(ring, new object[] { vertices });
                Assert.That(vertices.currentVertCount, Is.GreaterThan(0), "The ring must remain visible through Gaussian and disabled styles.");
                for (var index = 0; index < poolNames.Length; index++)
                {
                    Assert.That(typeof(UiPvBackdropBlur).GetField(poolNames[index], BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null), Is.SameAs(retained[index]), "Static decoration materials cannot replace the six shared targets.");
                }
            }
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            yield return null;
            populate.Invoke(frame, new object[] { vertices });
            vertices.PopulateUIVertex(ref vertex, 0);
            Assert.That(((Color)vertex.color).r, Is.EqualTo(authoredColor.r).Within(1f / 255f));
            populate.Invoke(ring, new object[] { vertices });
            Assert.That(vertices.currentVertCount, Is.GreaterThan(0), "Returning to liquid must retain the same ring without recreating or moving the object.");
            Assert.That(ring.material, Is.SameAs(ringMaterial));
            Assert.That(ring.color, Is.EqualTo(ringColor));
#else
            Assert.Ignore("This fixture requires the imported authored cover prefabs.");
            yield break;
#endif
        }

        /// <summary>Checks explicitly opted-in standalone artwork smooths its material without replacing the texture.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator StandaloneArtwork_SwitchesOnlyItsMaterial()
        {
            var image = CreateImage(_cutout);
            var texture = _objects.Track(new Texture2D(8, 8) { name = "Unrelated runtime cover" });
            var sprite = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * 0.5f));
            image.sprite = sprite;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(sprite, out _), Is.False);
            Assert.That(image.mainTexture, Is.SameAs(texture));
            Assert.That(image.material.shader.name, Is.EqualTo("Maj/UI/Atlas Alpha"));
            UiOutlineTheme.Configure(UiOutlineMode.None);
            yield return null;
            Assert.That(image.mainTexture, Is.SameAs(texture));
            Assert.That(image.material, Is.SameAs(_cutout));
        }

        /// <summary>Checks ordinary Images and non-UI users of the shared cutout material cannot be converted globally.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator OrdinaryImage_PreservesItsSharedCutoutMaterial()
        {
            var gameObject = _objects.Track(new GameObject("Unchanged ordinary Image", typeof(RectTransform), typeof(CanvasRenderer)));
            var image = gameObject.AddComponent<Image>();
            image.material = _cutout;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;
            Assert.That(image.material, Is.SameAs(_cutout));
            Assert.That(_cutout.shader.name, Is.EqualTo("Maj/Common/Opaque"));
        }

        /// <summary>Checks procedural circles load the alpha template without an atlas lookup and preserve masking and filled geometry.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator ListCircleWithoutSprite_PreservesShapeDepthAndStencilAcrossQualityChanges()
        {
            var shader = Shader.Find("Maj/UI/List/CoverShader");
            Assert.That(shader, Is.Not.Null);
            var original = _objects.Track(new Material(shader));
            original.SetFloat("_ListDepthOffset", 151f);
            original.SetFloat("_Brightness", 0.75f);
            original.SetVector("_Center", new Vector4(0.45f, 0.55f, 0f, 0f));
            original.SetFloat("_Radius", 0.4f);
            original.SetFloat("_Feather", 0.002f);
            original.SetFloat("_StencilComp", 3f);
            original.SetFloat("_Stencil", 2f);
            original.SetFloat("_StencilReadMask", 3f);
            original.SetFloat("_StencilWriteMask", 0f);
            var image = CreateImage(original);
            image.sprite = null;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillAmount = 0.35f;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;

            var variant = image.material;
            Assert.That(variant.shader.name, Is.EqualTo("Maj/UI/List/Cover Alpha"));
            foreach (var property in new[] { "_ListDepthOffset", "_Brightness", "_Radius", "_Feather", "_StencilComp", "_Stencil", "_StencilReadMask", "_StencilWriteMask" })
            {
                Assert.That(variant.GetFloat(property), Is.EqualTo(original.GetFloat(property)), property);
            }
            Assert.That(variant.GetVector("_Center"), Is.EqualTo(original.GetVector("_Center")));
            Assert.That(image.material, Is.SameAs(variant));
            Assert.That(image.sprite, Is.Null);
            Assert.That(image.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(image.fillAmount, Is.EqualTo(0.35f));
            Assert.That(variant.renderQueue, Is.EqualTo(3000));
            Assert.That(original.shader.name, Is.EqualTo("Maj/UI/List/CoverShader"));

            UiOutlineTheme.Configure(UiOutlineMode.None);
            yield return null;
            Assert.That(image.material, Is.SameAs(original));
            Assert.That(image.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(image.fillAmount, Is.EqualTo(0.35f));
        }

        /// <summary>Checks variants remain shared per original while retaining distinct List depth offsets.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator MaterialVariants_PreserveDistinctDepthOffsetsAndShareIdenticalSources()
        {
            var first = CreateImage(_cutout);
            var second = CreateImage(_cutout);
            var other = _objects.Track(new Material(_cutout) { name = "Other List depth" });
            other.SetFloat("_ListDepthOffset", 152f);
            var third = CreateImage(other);
            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;
            Assert.That(first.material, Is.SameAs(second.material));
            Assert.That(first.material, Is.Not.SameAs(third.material));
            Assert.That(first.material.GetFloat("_ListDepthOffset"), Is.EqualTo(151f));
            Assert.That(third.material.GetFloat("_ListDepthOffset"), Is.EqualTo(152f));
            Assert.That(_cutout.GetFloat("_ListDepthOffset"), Is.EqualTo(151f));
            Assert.That(other.GetFloat("_ListDepthOffset"), Is.EqualTo(152f));
        }

        /// <summary>Protects alpha-template render state when converting real legacy material assets.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator LegacyOpaqueAssets_PreserveAlphaColorWritesAndStencilDefaults()
        {
#if UNITY_EDITOR
            foreach (var path in new[]
            {
                "Assets/Rendering/Materials/UI/[UI]Opaque.mat",
                "Assets/Rendering/Materials/UI/[UI]RingLine.mat",
                "Assets/Rendering/Materials/UI/List/[List]CoverLevelBackground.mat"
            })
            {
                var original = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(original, Is.Not.Null, path);
                var image = CreateImage(original);
                UiOutlineTheme.Configure(UiOutlineMode.All);
                yield return null;

                var variant = image.material;
                Assert.That(variant, Is.Not.SameAs(original), path);
                Assert.That(variant.shader.name, Is.EqualTo("Maj/UI/Atlas Alpha"), path);
                Assert.That(variant.GetFloat("_ColorMask"), Is.EqualTo(15f), path);
                Assert.That(variant.GetFloat("_StencilComp"), Is.EqualTo(8f), path);
                Assert.That(variant.GetFloat("_Stencil"), Is.Zero, path);
                Assert.That(variant.GetFloat("_StencilOp"), Is.Zero, path);
                Assert.That(variant.GetFloat("_StencilReadMask"), Is.EqualTo(255f), path);
                Assert.That(variant.GetFloat("_StencilWriteMask"), Is.EqualTo(255f), path);
                Assert.That(variant.GetFloat("_UseUIAlphaClip"), Is.Zero, path);
                Assert.That(variant.renderQueue, Is.EqualTo(3000), path);
                Assert.That(variant.GetColor("_Color"), Is.EqualTo(original.GetColor("_Color")), path);
                Assert.That(variant.GetFloat("_ListDepthOffset"), Is.EqualTo(original.GetFloat("_ListDepthOffset")), path);
                Assert.That(original.shader.name, Is.EqualTo("Maj/Common/Opaque"), path);
            }
#else
            Assert.Ignore("This regression loads authored legacy materials in Editor Play Mode.");
            yield break;
#endif
        }

        /// <summary>Checks Performance automatically outlines alpha-source quality Images while cutout and ordinary Images stay unchanged.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator BackgroundPanels_PerformanceUsesOutlinedAlphaWithoutChangingOtherUi()
        {
            var alpha = _objects.Track(new Material(Shader.Find("Maj/UI/Default")));
            var panel = CreateImage(alpha);
            var ordinary = CreateImage(_cutout);
            var transparent = CreateImage(alpha);
            ParentToBackgroundCanvas(panel, ordinary, transparent);
            var nativeObject = _objects.Track(new GameObject("Unchanged alpha Image", typeof(RectTransform), typeof(CanvasRenderer)));
            var native = nativeObject.AddComponent<Image>();
            native.sprite = panel.sprite;
            native.material = alpha;
            native.transform.SetParent(panel.transform.parent, false);
            var source = panel.sprite;
            var size = panel.rectTransform.sizeDelta;

            UiOutlineTheme.Configure(UiOutlineMode.Performance);
            yield return null;
            Canvas.ForceUpdateCanvases();

            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(panel, out var outlined), Is.True);
            Assert.That(panel.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(panel.material, Is.SameAs(alpha));
            Assert.That(panel.canvasRenderer.GetMaterial(0), Is.SameAs(alpha));
            Assert.That(ordinary.mainTexture, Is.SameAs(ordinary.sprite.texture));
            Assert.That(ordinary.material, Is.SameAs(_cutout));
            Assert.That(transparent.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(transparent.material, Is.SameAs(alpha));
            Assert.That(native.mainTexture, Is.SameAs(native.sprite.texture));
            Assert.That(native.material, Is.SameAs(alpha));
            Assert.That(panel.sprite, Is.SameAs(source));
            Assert.That(panel.rectTransform.sizeDelta, Is.EqualTo(size));

            UiOutlineTheme.Configure(UiOutlineMode.All);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(panel.material, Is.SameAs(alpha));
            Assert.That(ordinary.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(ordinary.material.shader.name, Is.EqualTo("Maj/UI/Atlas Alpha"));
            Assert.That(transparent.material, Is.SameAs(alpha), "An existing alpha material remains unchanged in All mode.");
            Assert.That(native.mainTexture, Is.SameAs(native.sprite.texture));
            Assert.That(native.material, Is.SameAs(alpha));

            UiOutlineTheme.Configure(UiOutlineMode.Performance);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(panel.material, Is.SameAs(alpha));
            Assert.That(ordinary.mainTexture, Is.SameAs(ordinary.sprite.texture));
            Assert.That(ordinary.material, Is.SameAs(_cutout));
            Assert.That(transparent.mainTexture, Is.SameAs(outlined.texture));
            Assert.That(native.mainTexture, Is.SameAs(native.sprite.texture));
            Assert.That(panel.rectTransform.sizeDelta, Is.EqualTo(size));
        }

        /// <summary>Checks background mode changes refresh real renderers even while the ordinary UI style remains disabled.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator BackgroundPanels_SwitchCutoffAndAlphaWithSharedMaterialsAndUnchangedSourceState()
        {
            var alpha = _objects.Track(new Material(Shader.Find("Maj/UI/Default")));
            alpha.SetColor("_Color", new Color(0.7f, 0.6f, 0.5f, 0.4f));
            var panel = CreateImage(alpha);
            var second = CreateImage(alpha);
            ParentToBackgroundCanvas(panel, second);
            panel.color = new Color(0.5f, 0.8f, 0.9f, 0.3f);
            var tint = panel.color;
            var source = panel.sprite;

            UiOutlineTheme.Configure(UiOutlineMode.None);
            yield return null;
            Canvas.ForceUpdateCanvases();

            var cutoff = panel.material;
            Assert.That(cutoff, Is.Not.SameAs(alpha));
            Assert.That(cutoff.shader.name, Is.EqualTo("Maj/UI/Atlas Cutoff"));
            Assert.That(panel.canvasRenderer.GetMaterial(0).shader, Is.SameAs(cutoff.shader));
            Assert.That(second.material, Is.SameAs(cutoff));
            Assert.That(panel.mainTexture, Is.SameAs(source.texture));
            Assert.That(cutoff.renderQueue, Is.EqualTo(alpha.renderQueue));
            Assert.That(cutoff.GetFloat("_ColorMask"), Is.EqualTo(alpha.GetFloat("_ColorMask")));
            Assert.That(cutoff.GetFloat("_StencilComp"), Is.EqualTo(alpha.GetFloat("_StencilComp")));
            Assert.That(cutoff.GetColor("_Color"), Is.EqualTo(alpha.GetColor("_Color")));
            Assert.That(panel.color, Is.EqualTo(tint));
            Assert.That(alpha.shader.name, Is.EqualTo("Maj/UI/Default"));

            UiOutlineTheme.Configure(UiOutlineMode.Performance);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.material, Is.SameAs(alpha));
            Assert.That(panel.canvasRenderer.GetMaterial(0), Is.SameAs(alpha));
            Assert.That(UiOutlineTheme.TryGetOutlinedSprite(panel, out var outlined), Is.True);
            Assert.That(panel.mainTexture, Is.SameAs(outlined.texture));

            panel.gameObject.SetActive(false);
            UiOutlineTheme.Configure(UiOutlineMode.None);
            panel.gameObject.SetActive(true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(panel.material, Is.SameAs(cutoff));
            Assert.That(panel.sprite, Is.SameAs(source));
            Assert.That(panel.color, Is.EqualTo(tint));
        }

        /// <summary>Checks actual Setting scene alpha artwork follows all modes without manual background flags.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator SettingAlphaAssets_ApplyAllModesFromTheirSourceMaterials()
        {
#if UNITY_EDITOR
            var alpha = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/Default.mat");
            Assert.That(alpha, Is.Not.Null);
            var panels = new List<QualityUiImage>();
            var setting = FileSystem.File.Open(Application.dataPath + "/Scenes/Setting.unity").ReadAllText();
            foreach (var fileId in new[] { 1053645854L, 489720307L })
            {
                var component = Regex.Match(setting, @"(?ms)^--- !u!114 &" + fileId + @"\r?\n.*?(?=^--- |\z)");
                Assert.That(component.Success, Is.True);
                Assert.That(component.Value, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
                Assert.That(component.Value, Does.Contain("guid: 78a3c5eb06624f448a0238577d1de902"));
                var sprite = Regex.Match(component.Value, @"m_Sprite: \{fileID: (-?\d+), guid: 04e00e0b72a3d284ebec3f7160996aa1");
                Assert.That(sprite.Success, Is.True);
                var panel = CreateImage(alpha);
                panel.sprite = FindImportedSprite(long.Parse(sprite.Groups[1].Value));
                panels.Add(panel);
            }
            ParentToBackgroundCanvas(panels.ToArray());

            foreach (var mode in new[] { UiOutlineMode.None, UiOutlineMode.Performance, UiOutlineMode.All })
            {
                UiOutlineTheme.Configure(mode);
                yield return null;
                Canvas.ForceUpdateCanvases();
                foreach (var panel in panels)
                {
                    Assert.That(panel.SourceMaterial.shader.name, Is.EqualTo("Maj/UI/Default"));
                    if (mode == UiOutlineMode.None)
                    {
                        Assert.That(panel.material.shader.name, Is.EqualTo("Maj/UI/Atlas Cutoff"));
                        Assert.That(panel.canvasRenderer.GetMaterial(0).shader, Is.SameAs(panel.material.shader));
                        Assert.That(panel.mainTexture, Is.SameAs(panel.sprite.texture));
                        Assert.That(UiOutlineTheme.TryGetOutlinedSprite(panel, out _), Is.False);
                    }
                    else
                    {
                        Assert.That(panel.material, Is.SameAs(panel.SourceMaterial));
                        Assert.That(UiOutlineTheme.TryGetOutlinedSprite(panel, out var outlined), Is.True);
                        Assert.That(panel.mainTexture, Is.SameAs(outlined.texture));
                    }
                }
            }
#else
            Assert.Ignore("This regression reads the actual Setting scene assets.");
            yield break;
#endif
        }

        /// <summary>Checks procedural rounded panels change compositing without losing their geometry, source state or legacy material defaults.</summary>
        /// <returns>The lifecycle test coroutine.</returns>
        [UnityTest]
        public IEnumerator RoundedRectAssets_SwitchOpaqueAndAlphaWithoutChangingSourceMaterial()
        {
#if UNITY_EDITOR
            var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/[Setting]RoundedRect - Menu.mat");
            Assert.That(original, Is.Not.Null);
            Assert.That(original.GetFloat("_MajUIOpaque"), Is.Zero);
            Assert.That(original.GetFloat("_MajUISrcBlend"), Is.EqualTo(5f));
            Assert.That(original.GetFloat("_MajUIDstBlend"), Is.EqualTo(10f));
            var image = CreateImage(original);
            image.sprite = null;
            var second = CreateImage(original);
            second.sprite = null;
            ParentToBackgroundCanvas(image, second);
            Material? cached = null;
            foreach (var mode in new[] { UiOutlineMode.None, UiOutlineMode.Performance, UiOutlineMode.All, UiOutlineMode.None })
            {
                UiOutlineTheme.Configure(mode);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var material = image.material;
                Assert.That(image.SourceMaterial, Is.SameAs(original));
                Assert.That(material.shader.name, Is.EqualTo("Maj/UI/Rounded Rect"));
                Assert.That(image.canvasRenderer.GetMaterial(0), Is.SameAs(material));
                Assert.That(material.GetColor("_Color"), Is.EqualTo(original.GetColor("_Color")));
                foreach (var radius in new[] { "_RadiusTL", "_RadiusTR", "_RadiusBL", "_RadiusBR" })
                {
                    Assert.That(material.GetFloat(radius), Is.EqualTo(original.GetFloat(radius)), radius);
                }
                if (mode == UiOutlineMode.None)
                {
                    Assert.That(material, Is.Not.SameAs(original));
                    Assert.That(second.material, Is.SameAs(material));
                    Assert.That(material.GetFloat("_MajUIOpaque"), Is.EqualTo(1f));
                    Assert.That(material.GetFloat("_MajUISrcBlend"), Is.EqualTo(1f));
                    Assert.That(material.GetFloat("_MajUIDstBlend"), Is.Zero);
                    if (cached != null)
                    {
                        Assert.That(material, Is.SameAs(cached));
                    }
                    cached = material;
                }
                else
                {
                    Assert.That(material, Is.SameAs(original));
                }
                Assert.That(original.GetFloat("_MajUIOpaque"), Is.Zero);
                Assert.That(original.GetFloat("_MajUISrcBlend"), Is.EqualTo(5f));
                Assert.That(original.GetFloat("_MajUIDstBlend"), Is.EqualTo(10f));
            }
#else
            Assert.Ignore("This regression loads the actual rounded Setting material.");
            yield break;
#endif
        }

        /// <summary>Checks explicit background ordering preserves each UI shader and survives glass changes and pooling.</summary>
        /// <returns>The real Image enable, material-selection and queue-restoration lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator DrawBeforeCameraExtension_KeepsRingBehindLive2DInEveryStyle()
        {
            var image = CreateImage(_cutout);
            var originalQueue = _cutout.renderQueue;
            image.sprite = FindImportedSprite(1055266389L);
            typeof(QualityUiImage).GetField("_drawBeforeCameraExtension", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(image, true);
            var board = CreateImage(_cutout);
            board.gameObject.SetActive(false);
            var panel = board.gameObject.AddComponent<UiBackdropPanel>();
            typeof(UiBackdropPanel).GetField("_backgroundImage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(panel, image);
            ParentToBackgroundCanvas(image, board);
            var position = image.rectTransform.anchoredPosition;
            var size = image.rectTransform.sizeDelta;
            var sprite = image.sprite;

            foreach (var mode in new[] { UiOutlineMode.None, UiOutlineMode.Performance, UiOutlineMode.All })
            {
                UiPvBackdropBlur.Configure(false);
                UiOutlineTheme.Configure(mode);
                image.RefreshMaterialSelection();
                yield return null;
                Canvas.ForceUpdateCanvases();
                var material = image.material;
                var expectedShader = mode == UiOutlineMode.All ? "Maj/UI/Atlas Alpha" : "Maj/Common/Opaque";
                Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.GeometryLast));
                Assert.That(material.shader.name, Is.EqualTo(expectedShader));
#if UNITY_EDITOR
                var shader = FileSystem.File.Open(AssetDatabase.GetAssetPath(material.shader)).ReadAllText();
                Assert.That(shader, Does.Contain(mode == UiOutlineMode.All ? "ZWrite Off" : "ZWrite On"));
#endif
                Assert.That(image.SourceMaterial, Is.SameAs(_cutout));
                Assert.That(image.sprite, Is.SameAs(sprite));
                Assert.That(image.rectTransform.anchoredPosition, Is.EqualTo(position));
                Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(size));
                image.enabled = false;
                Assert.That(typeof(QualityUiImage).GetField("_captureMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(image), Is.Null);
                image.enabled = true;
                Assert.That(image.material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.GeometryLast));
            }

            UiOutlineTheme.Configure(UiOutlineMode.All);
            board.gameObject.SetActive(true);
            foreach (var style in new[] { UiBackdropStyle.GaussianBlur, UiBackdropStyle.LiquidGlass })
            {
                UiPvBackdropBlur.Configure(true, style);
                Assert.That(image.material.renderQueue, Is.EqualTo(MajUiBackdropBridge.BackgroundQueueUpperBound));
                Assert.That(image.material.shader.name, Is.EqualTo("Maj/UI/Atlas Alpha"));
            }
            UiPvBackdropBlur.Configure(false);
            Assert.That(image.material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.GeometryLast));
            board.gameObject.SetActive(false);
            Assert.That(image.material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.GeometryLast));
            Assert.That(_cutout.renderQueue, Is.EqualTo(originalQueue));
        }

        /// <summary>Checks the blur switch rebuilds only opted-in background materials even when the UI mode stays unchanged.</summary>
        /// <returns>The real Canvas material-selection lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator CaptureBeforeBackdrop_FollowsEffectWithoutChangingSourceOrGeometry()
        {
            var shader = Shader.Find("Maj/UI/Default");
            Assert.That(shader, Is.Not.Null);
            var source = _objects.Track(new Material(shader));
            source.SetColor("_Color", new Color(0.2f, 0.4f, 0.6f, 0.8f));
            source.SetFloat("_Stencil", 7f);
            source.SetFloat("_UseUIAlphaClip", 1f);
            source.EnableKeyword("UNITY_UI_ALPHACLIP");
            var image = CreateImage(source);
            var ordinary = CreateImage(source);
            typeof(QualityUiImage).GetField("_captureBeforeBackdrop", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(image, true);
            image.color = new Color(0.8f, 0.7f, 0.6f, 0.35f);
            image.rectTransform.anchoredPosition = new Vector2(19f, -23f);
            image.rectTransform.sizeDelta = new Vector2(430f, 510f);
            var sprite = image.sprite;
            var color = image.color;
            var position = image.rectTransform.anchoredPosition;
            var size = image.rectTransform.sizeDelta;
            UiOutlineTheme.Configure(UiOutlineMode.All);
            ParentToBackgroundCanvas(image, ordinary);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(image.canvasRenderer.GetMaterial(), Is.SameAs(source));
            var materialChanges = 0;
            var vertexChanges = 0;
            image.RegisterDirtyMaterialCallback(() => materialChanges++);
            image.RegisterDirtyVerticesCallback(() => vertexChanges++);

            UiPvBackdropBlur.Configure(true);

            Assert.That(materialChanges, Is.GreaterThan(0));
            Assert.That(vertexChanges, Is.Zero, "A blur-only change must not rebuild atlas geometry.");
            Assert.That(image.material.shaderKeywords, Is.EquivalentTo(source.shaderKeywords),
                "The queue-only variant must preserve keywords before native Canvas submission.");
            Canvas.ForceUpdateCanvases();
            var captured = image.canvasRenderer.GetMaterial();
            Assert.That(captured, Is.Not.SameAs(source));
            Assert.That(captured.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryBackgroundQueue));
            Assert.That(captured.shader, Is.SameAs(source.shader));
            Assert.That(captured.GetColor("_Color"), Is.EqualTo(source.GetColor("_Color")));
            Assert.That(captured.GetFloat("_Stencil"), Is.EqualTo(7f));
            Assert.That(captured.shaderKeywords, Is.EquivalentTo(source.shaderKeywords),
                "The queue-only variant must retain the current native UI keyword state.");
            Assert.That(ordinary.canvasRenderer.GetMaterial(), Is.SameAs(source));
            Assert.That(source.renderQueue, Is.EqualTo(3000));
            Assert.That(image.SourceMaterial, Is.SameAs(source));
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(image.color, Is.EqualTo(color));
            Assert.That(image.rectTransform.anchoredPosition, Is.EqualTo(position));
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(size));

            UiPvBackdropBlur.Configure(false);

            Assert.That(typeof(QualityUiImage).GetField("_captureMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(image), Is.Null);
            Canvas.ForceUpdateCanvases();
            Assert.That(image.canvasRenderer.GetMaterial(), Is.SameAs(source));
            yield return null;
            yield return null;
            Assert.That(captured == null, Is.True, "The queue-only variant must be destroyed when filtering is disabled.");
            Assert.That(source != null, Is.True);
        }

        /// <summary>Checks queue variants follow native material replacement and pooling without retaining stale or owned resources.</summary>
        /// <returns>The source-replacement, disable and destruction lifecycle coroutine.</returns>
        [UnityTest]
        public IEnumerator CaptureBeforeBackdrop_ReplacesSourceAndReleasesVariantsAcrossPooling()
        {
            var shader = Shader.Find("Maj/UI/Default");
            Assert.That(shader, Is.Not.Null);
            var source = _objects.Track(new Material(shader));
            var replacement = _objects.Track(new Material(shader) { renderQueue = 3040 });
            replacement.SetColor("_Color", Color.cyan);
            var image = CreateImage(source);
            typeof(QualityUiImage).GetField("_captureBeforeBackdrop", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(image, true);
            UiOutlineTheme.Configure(UiOutlineMode.All);
            UiPvBackdropBlur.Configure(true);
            var first = image.material;
            Assert.That(first.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryBackgroundQueue));
            source.SetColor("_Color", Color.magenta);
            Assert.That(image.material, Is.SameAs(first));
            Assert.That(first.GetColor("_Color"), Is.EqualTo(Color.magenta));

            image.material = replacement;
            var second = image.material;

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.GetColor("_Color"), Is.EqualTo(Color.cyan));
            Assert.That(second.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryBackgroundQueue));
            Assert.That(image.SourceMaterial, Is.SameAs(replacement));
            yield return null;
            yield return null;
            Assert.That(first == null, Is.True);
            image.enabled = false;
            Assert.That(typeof(QualityUiImage).GetField("_captureMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(image), Is.Null);
            Assert.That(image.material, Is.SameAs(replacement));
            yield return null;
            yield return null;
            Assert.That(second == null, Is.True);

            image.enabled = true;
            var third = image.material;

            Assert.That(third.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryBackgroundQueue));
            Assert.That(third.shader, Is.SameAs(replacement.shader));
            UnityEngine.Object.Destroy(image.gameObject);
            yield return null;
            yield return null;
            Assert.That(third == null, Is.True);
            Assert.That(source != null && replacement != null, Is.True, "Borrowed source materials must survive the Image.");
            Assert.That(source.renderQueue, Is.EqualTo(3000));
            Assert.That(replacement.renderQueue, Is.EqualTo(3040));
        }

        /// <summary>Checks explicit liquid foregrounds work for a Board owner and restore authored state outside liquid glass.</summary>
        /// <returns>The style, source-state and pooled-owner lifecycle regression.</returns>
        [UnityTest]
        public IEnumerator LiquidExplicitForeground_RestoresQueuesAcrossStylesAndPooling()
        {
            var shader = Shader.Find("Maj/UI/Default");
            Assert.That(shader, Is.Not.Null);
            var source = _objects.Track(new Material(shader) { renderQueue = 3031 });
            source.SetColor("_Color", Color.cyan);
            source.SetFloat("_Stencil", 3f);
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var owner = CreateImage(_cutout);
            owner.gameObject.SetActive(false);
            owner.transform.SetParent(canvas.transform, false);
            var panel = owner.gameObject.AddComponent<UiBackdropPanel>();
            typeof(UiBackdropPanel).GetField("_useStrongBlur", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, true);
            var bubbles = _objects.Create("Authored comments", canvas.transform);
            var bubble = _objects.Add<Image>("Comment", bubbles.transform);
            bubble.material = source;
            bubble.sprite = _sprites[0];
            bubble.color = new Color(0.2f, 0.4f, 0.7f, 0.6f);
            var color = bubble.color;
            var size = bubble.rectTransform.sizeDelta;
            typeof(UiBackdropPanel).GetField("_liquidForegroundRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, bubbles.transform);
            owner.gameObject.SetActive(true);
            yield return null;
            Assert.That(panel.SourceRole, Is.EqualTo(UiBackdropSourceRole.Board));
            var helper = bubble.GetComponent<UiBackdropForeground>();
            Assert.That(helper, Is.Not.Null, "A Board owner must bind its explicit layer even without the legacy foreground root.");
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
            Assert.That(bubble.materialForRendering, Is.SameAs(source));

            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var variant = bubble.materialForRendering;
            Assert.That(variant.renderQueue, Is.EqualTo(3503));
            Assert.That(variant.shader, Is.SameAs(source.shader));
            Assert.That(variant.GetColor("_Color"), Is.EqualTo(Color.cyan));
            Assert.That(variant.GetFloat("_Stencil"), Is.EqualTo(3f));
            Assert.That(bubble.sprite, Is.SameAs(_sprites[0]));
            Assert.That(bubble.color, Is.EqualTo(color));
            Assert.That(bubble.rectTransform.sizeDelta, Is.EqualTo(size));
            Assert.That(bubble.material, Is.SameAs(source));
            Assert.That(source.renderQueue, Is.EqualTo(3031));

            UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
            Assert.That(bubble.materialForRendering, Is.SameAs(source));
            yield return null;
            yield return null;
            Assert.That(variant == null, Is.True);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var pooledVariant = bubble.materialForRendering;
            panel.enabled = false;
            Assert.That(helper!.IsBoundTo(panel), Is.False);
            Assert.That(bubble.materialForRendering, Is.SameAs(source));
            yield return null;
            Assert.That(pooledVariant == null, Is.True);
            panel.enabled = true;
            Assert.That(bubble.GetComponent<UiBackdropForeground>(), Is.SameAs(helper));
            Assert.That(bubble.materialForRendering.renderQueue, Is.EqualTo(3503));
            UiPvBackdropBlur.Configure(false);
            Assert.That(bubble.materialForRendering, Is.SameAs(source));
        }

        /// <summary>Checks explicit liquid queues reach later TMP fallback submeshes while legacy category foregrounds keep their stage.</summary>
        /// <returns>The TMP event, dual-style, ownership and native-material cleanup regression.</returns>
        [UnityTest]
        public IEnumerator LiquidExplicitText_PropagatesToNewFallbacksWithoutChangingLegacyForegrounds()
        {
            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Assert.That(font, Is.Not.Null);
            var source = _objects.Track(new Material(font!.material) { renderQueue = 3000 });
            var fallbackSource = _objects.Track(new Material(source));
            fallbackSource.SetFloat("_Stencil", 7f);
            var canvas = _objects.Add<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var owner = CreateImage(_cutout);
            owner.gameObject.SetActive(false);
            owner.transform.SetParent(canvas.transform, false);
            var panel = owner.gameObject.AddComponent<UiBackdropPanel>();
            typeof(UiBackdropPanel).GetField("_isCategory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, true);
            var nativeRoot = _objects.Create("Legacy category foreground", canvas.transform);
            var native = _objects.Add<Image>("Native category icon", nativeRoot.transform);
            native.material = _cutout;
            var comments = _objects.Create("Authored liquid comment foreground", canvas.transform);
            var textObject = _objects.Create("Comment text", comments.transform);
            textObject.SetActive(false);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = source;
            text.text = "Comment";
            text.rectTransform.sizeDelta = new Vector2(400f, 100f);
            textObject.SetActive(true);
            text.ForceMeshUpdate();
            typeof(UiBackdropPanel).GetField("_buttonForegroundRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, nativeRoot.transform);
            typeof(UiBackdropPanel).GetField("_liquidForegroundRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(panel, comments.transform);
            owner.gameObject.SetActive(true);
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
            yield return null;
            Assert.That(native.materialForRendering.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
            Assert.That(text.materialForRendering, Is.SameAs(source));
            var helper = text.GetComponent<UiBackdropForeground>();
            Assert.That(helper, Is.Not.Null);
            var submesh = TMP_SubMeshUI.AddSubTextObject(text, new MaterialReference(1, font, null!, fallbackSource, 0f));
            _objects.Track(submesh.gameObject);
            TMPro_EventManager.ON_TEXT_CHANGED(text);
            Assert.That(submesh.materialForRendering, Is.SameAs(fallbackSource));

            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            var variant = text.materialForRendering;
            var fallbackVariant = submesh.materialForRendering;
            Assert.That(variant.renderQueue, Is.EqualTo(3503));
            Assert.That(fallbackVariant.renderQueue, Is.EqualTo(3503));
            Assert.That(fallbackVariant.shader, Is.SameAs(fallbackSource.shader));
            Assert.That(fallbackVariant.GetFloat("_Stencil"), Is.EqualTo(7f));
            Assert.That(fallbackVariant.GetTexture("_MainTex"), Is.SameAs(fallbackSource.GetTexture("_MainTex")));
            Assert.That(submesh.sharedMaterial, Is.SameAs(fallbackSource));
            Assert.That(native.materialForRendering.renderQueue, Is.EqualTo(MajUiBackdropBridge.CategoryQueue));
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.GaussianBlur);
            Assert.That(text.materialForRendering, Is.SameAs(source));
            Assert.That(submesh.materialForRendering, Is.SameAs(fallbackSource));
            UiPvBackdropBlur.Configure(true, UiBackdropStyle.LiquidGlass);
            panel.enabled = false;
            Assert.That(helper!.IsBoundTo(panel), Is.False);
            Assert.That(text.materialForRendering, Is.SameAs(source));
            Assert.That(submesh.materialForRendering, Is.SameAs(fallbackSource));
            yield return null;
            yield return null;
            Assert.That(variant == null && fallbackVariant == null, Is.True);
            Assert.That(source.renderQueue, Is.EqualTo(3000));
            Assert.That(fallbackSource.renderQueue, Is.EqualTo(3000));
        }

        /// <summary>Finds an imported atlas slice by the identifier stored in an authored Setting scene Image.</summary>
        /// <param name="fileId">The source Sprite subasset identifier.</param>
        /// <returns>The matching original Sprite.</returns>
        private Sprite FindImportedSprite(long fileId)
        {
#if UNITY_EDITOR
            foreach (var sprite in _sprites)
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out _, out long candidate) && candidate == fileId)
                {
                    return sprite;
                }
            }
#endif
            Assert.Fail("The real Setting atlas slice is unavailable: " + fileId);
            return null!;
        }

        /// <summary>Provides a real Canvas so background policy changes rebuild native CanvasRenderer materials.</summary>
        /// <param name="images">The test images to parent to the owned overlay Canvas.</param>
        private void ParentToBackgroundCanvas(params QualityUiImage[] images)
        {
            var canvasObject = _objects.Track(new GameObject("List background lifecycle Canvas", typeof(RectTransform), typeof(Canvas)));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            foreach (var image in images)
            {
                image.transform.SetParent(canvasObject.transform, false);
            }
        }

        /// <summary>Creates an enabled real Image whose source properties are authored before enable.</summary>
        /// <param name="material">The original material to retain in serialized Image state.</param>
        /// <returns>The enabled quality-aware Image.</returns>
        private QualityUiImage CreateImage(Material material)
        {
            var gameObject = _objects.Track(new GameObject("Quality UI lifecycle", typeof(RectTransform), typeof(CanvasRenderer)));
            gameObject.SetActive(false);
            var image = gameObject.AddComponent<QualityUiImage>();
            image.sprite = _sprites[0];
            image.material = material;
            gameObject.SetActive(true);
            return image;
        }

        /// <summary>Checks the original data continues to drive layout, animation, and Sprite Swap.</summary>
        /// <param name="image">The Image under test.</param>
        /// <param name="sprite">The original source sprite.</param>
        /// <param name="color">The original color, including animation alpha.</param>
        /// <param name="position">The original anchored position.</param>
        /// <param name="size">The original size delta.</param>
        private static void AssertSourceState(QualityUiImage image, Sprite sprite, Color color, Vector2 position, Vector2 size)
        {
            Assert.That(image.sprite, Is.SameAs(sprite));
            Assert.That(image.color, Is.EqualTo(color));
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(image.rectTransform.anchoredPosition, Is.EqualTo(position));
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(size));
        }

        /// <summary>Returns the production registration list only for lifecycle assertions.</summary>
        /// <returns>The enabled quality-aware Image registrations.</returns>
        private static IList RegisteredImages()
        {
            return (IList)typeof(UiOutlineTheme).GetField("s_images", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        }

        /// <summary>Clears the production cache without substituting any rendering or mapping implementation.</summary>
        private static void ResetTheme()
        {
            UiPvBackdropBlur.Configure(false);
            typeof(UiOutlineTheme).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        }
    }
}
