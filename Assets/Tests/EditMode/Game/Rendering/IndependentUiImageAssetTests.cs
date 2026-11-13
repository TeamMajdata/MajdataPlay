using System.Text.RegularExpressions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Protects standalone List UI opt-ins without changing their original materials or layout.</summary>
    public sealed class IndependentUiImageAssetTests
    {
        /// <summary>Names the authored Ribbon List hierarchy shared by the current List scene.</summary>
        private const string ListPrefabPath = "Assets/Prefabs/List/Main_Display_ribbon.prefab";

        /// <summary>Checks authored circles and covers retain their cutout source for the low quality path.</summary>
        /// <param name="path">The source prefab containing the Image component.</param>
        /// <param name="fileId">The original serialized Image component identifier.</param>
        /// <param name="shaderName">The original shader retained before a runtime quality conversion.</param>
        /// <param name="spriteGuid">The unchanged source texture GUID, or an empty string for a white default texture.</param>
        [TestCase("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 900000000000000303L, "Maj/Common/Opaque", "85371318f6efbc54a8702e8e1835ee9d")]
        [TestCase(ListPrefabPath, 5623399415559523727L, "Maj/UI/List/CoverShader", "")]
        [TestCase("Assets/Prefabs/List/Model/LevelDisplayer.prefab", 3233052182755496171L, "Maj/Common/Opaque", "e811fa03c3f1c14479e6ecd8f217a261")]
        [TestCase("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 2115732643789034301L, "Maj/UI/List/CoverShader", "fe0b463810edb304ab6d44a2c15900ce")]
        [TestCase("Assets/Prefabs/List/Model/ThumbnailDisplayer.prefab", 234687209036855421L, "Maj/UI/List/CoverShader", "fe0b463810edb304ab6d44a2c15900ce")]
        public void StandaloneCirclesAndCovers_RetainOriginalSourceProperties(
            string path, long fileId, string shaderName, string spriteGuid)
        {
            var image = FindImage(path, fileId);

            Assert.That(image, Is.TypeOf<QualityUiImage>());
            Assert.That(image.material.shader.name, Is.EqualTo(shaderName));
            Assert.That(image.type, Is.EqualTo(Image.Type.Simple));
            Assert.That(image.useSpriteMesh, Is.False);
            Assert.That(image.rectTransform.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            if (spriteGuid.Length == 0)
            {
                Assert.That(image.sprite, Is.Null, "The white favorite background must not acquire an atlas sprite.");
            }
            else
            {
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(image.sprite)), Is.EqualTo(spriteGuid));
            }
        }

        /// <summary>Checks nested prefab script migration preserves the selected circle's original layout.</summary>
        [Test]
        public void SelectedLevelReference_RetainsItsNestedCircleAndTransform()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab");
            Assert.That(prefab, Is.Not.Null);
            var selectedTransform = prefab.transform.Find("SelectedLevel/Background");
            Assert.That(selectedTransform, Is.Not.Null);
            var selected = selectedTransform.GetComponent<Image>();

            Assert.That(selected, Is.TypeOf<QualityUiImage>());
            Assert.That(selected!.rectTransform.rect.size, Is.EqualTo(new Vector2(75f, 75f)));
            Assert.That(selected.rectTransform.pivot, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(((RectTransform)selected.transform.parent).pivot, Is.EqualTo(new Vector2(0.5f, -1.9f)));
            Assert.That(selected.material.shader.name, Is.EqualTo("Maj/Common/Opaque"));
            Assert.That(selected.sprite.name, Is.EqualTo("128circle_0"));
        }

        /// <summary>Checks the liquid frame and late full-resolution cover opt in without changing source artwork or adding captures.</summary>
        [Test]
        public void CoverDecorations_OptInWithoutChangingCoverSourcesOrAddingCaptureOwners()
        {
            foreach (var item in new[]
            {
                ("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 5322621527391851573L, "CoverFrameGlass")
            })
            {
                var image = FindImage(item.Item1, item.Item2);
                Assert.That(image, Is.TypeOf<QualityUiImage>());
                var properties = new SerializedObject(image);
                var material = properties.FindProperty("_liquidGlassMaterial").objectReferenceValue as Material;
                Assert.That(material, Is.Not.Null);
                Assert.That(material!.name, Is.EqualTo(item.Item3));
                Assert.That(material.shader.name, Is.EqualTo("Maj/UI/List/Cover Decoration Glass"));
                Assert.That(image.material.shader, Is.Not.SameAs(material.shader), "The authored source must remain available to every non-liquid mode.");
                Assert.That(image.GetComponent<UiBackdropPanel>(), Is.Null, "The frame reuses existing RT3 through its explicit quality-aware owner.");
                Assert.That(properties.FindProperty("_captureBeforeBackdrop").boolValue, Is.False);
                Assert.That(material.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonBodyQueue));
                Assert.That(material.GetFloat("_ListDepthOffset"), Is.EqualTo(150.5f));
                Assert.That(image.material.renderQueue, Is.LessThan(MajUiBackdropBridge.ButtonBodyQueue), "Gaussian and ordinary modes retain their original before-capture frame.");
            }
            foreach (var item in new[]
            {
                ("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 2115732643789034301L),
                ("Assets/Prefabs/List/Model/ThumbnailDisplayer.prefab", 234687209036855421L)
            })
            {
                var image = FindImage(item.Item1, item.Item2);
                var foreground = new SerializedObject(image).FindProperty("_liquidGlassMaterial").objectReferenceValue as Material;
                if (item.Item2 == 2115732643789034301L)
                {
                    Assert.That(foreground, Is.Not.Null);
                    Assert.That(foreground!.name, Is.EqualTo("CoverForegroundAlpha"));
                    Assert.That(foreground.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound));
                    Assert.That(foreground.GetFloat("_ListDepthOffset"), Is.EqualTo(151f));
                    Assert.That(foreground.shader.name, Is.EqualTo("Maj/UI/List/Cover Alpha"));
                }
                else
                {
                    Assert.That(foreground, Is.Null, "Upper thumbnails must not join the late large-cover pass.");
                }
                Assert.That(image.material.shader.name, Is.EqualTo("Maj/UI/List/CoverShader"));
                Assert.That(image.material.renderQueue, Is.LessThan(MajUiBackdropBridge.ButtonBodyQueue));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(image.sprite)), Is.EqualTo("fe0b463810edb304ab6d44a2c15900ce"));
            }
        }

        /// <summary>Checks ring capture metadata, late difficulty labels and original source fonts survive the clearcoat ordering.</summary>
        [Test]
        public void CoverDifficultyArtwork_CapturesRingAndKeepsLabelsAboveClearcoat()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab");
            Assert.That(prefab.transform.Find("LevelColor").GetSiblingIndex(), Is.LessThan(prefab.transform.Find("SongCover").GetSiblingIndex()),
                "The original colored ring must remain underneath the full-resolution cover.");
            var ring = prefab.transform.Find("LevelColor").GetComponent<QualityUiImage>();
            var frame = prefab.transform.Find("BG").GetComponent<QualityUiImage>();
            Assert.That(new SerializedObject(frame).FindProperty("_glassInlaySource").objectReferenceValue, Is.SameAs(ring));
            Assert.That(new SerializedObject(ring).FindProperty("_liquidGlassMaterial").objectReferenceValue, Is.Null);
            Assert.That(ring.material.renderQueue, Is.LessThan(MajUiBackdropBridge.ButtonBodyQueue));
            Assert.That(ring.material.shader.name, Is.EqualTo("Maj/Common/Opaque"));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ring.sprite)), Is.EqualTo("85371318f6efbc54a8702e8e1835ee9d"));
            var levels = prefab.transform.Find("Levels");
            foreach (var background in levels.GetComponentsInChildren<QualityUiImage>(true))
            {
                var material = (Material)new SerializedObject(background).FindProperty("_liquidGlassMaterial").objectReferenceValue;
                Assert.That(material.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound + 1));
                Assert.That(material.GetFloat("_ListDepthOffset"), Is.EqualTo(151f));
                Assert.That(background.material.shader.name, Is.EqualTo("Maj/Common/Opaque"));
            }
            var texts = prefab.GetComponentsInChildren<TextMeshProUGUI>(true);
            Assert.That(texts.Length, Is.EqualTo(9));
            foreach (var text in texts)
            {
                Assert.That(text.font, Is.Not.Null);
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(text.font)), Is.EqualTo("dbc58334dc23ba04fbfee4bf5298b6a0"));
                Assert.That(text.fontSharedMaterial.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound + 1));
                Assert.That(text.fontSharedMaterial.mainTexture, Is.SameAs(text.font.atlasTextures[0]), "Late text presets must retain the original glyph atlas.");
                Assert.That(text.fontSharedMaterial.GetFloat("_ListDepthOffset"), Is.EqualTo(text.transform.IsChildOf(levels) ? 150f : 0f));
            }
            var selected = prefab.transform.Find("SelectedLevel/Background").GetComponent<QualityUiImage>();
            var selectedMaterial = (Material)new SerializedObject(selected).FindProperty("_liquidGlassMaterial").objectReferenceValue;
            Assert.That(selectedMaterial.renderQueue, Is.EqualTo(MajUiBackdropBridge.ButtonGlyphQueueLowerBound + 1));
            Assert.That(selectedMaterial.GetFloat("_ListDepthOffset"), Is.Zero);
            Assert.That(selected.rectTransform.rect.size, Is.EqualTo(new Vector2(75f, 75f)));
        }

        /// <summary>Checks the opaque viewport occluder is excluded from quality-dependent alpha conversion.</summary>
        [Test]
        public void MainDisplayOccluder_RemainsAnOrdinaryOpaqueImage()
        {
            var image = FindImage(ListPrefabPath, 4809691420882572571L);

            Assert.That(image, Is.TypeOf<Image>());
            Assert.That(image.material.shader.name, Is.EqualTo("Maj/Common/Opaque"));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(image.sprite)),
                Is.EqualTo("16e174893e05c034a9ecd9379ba56bea"));
        }

        /// <summary>Checks a nested opaque favorite instance opts in while its transparent source stays intact.</summary>
        [Test]
        public void FavoriteInstance_RetainsItsMaterialOverrideAndTransparentSource()
        {
            var source = FindImage("Assets/Prefabs/List/Favorite.prefab", 1209054723327445741L);
            Assert.That(source, Is.TypeOf<QualityUiImage>());
            Assert.That(source.material.shader.name, Is.EqualTo("Maj/UI/Default"));
            Assert.That(source.sprite.name, Is.EqualTo("icons_15"));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ListPrefabPath);
            var instanceTransform = prefab.transform.Find("CoverList/CenterCoverDisplayer/Favorite/icon");
            Assert.That(instanceTransform, Is.Not.Null);
            var instance = instanceTransform.GetComponent<Image>();
            Assert.That(instance, Is.TypeOf<QualityUiImage>());
            Assert.That(instance.material.shader.name, Is.EqualTo("Maj/Common/Opaque"));
            Assert.That(instance.rectTransform.rect.size, Is.EqualTo(new Vector2(30f, 30f)));
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(instance.sprite)),
                Is.EqualTo("6e25ef42696100148867e08e50da1426"));
        }

        /// <summary>Checks progress animation, sliced layout and independent symbol geometry remain native UGUI.</summary>
        [Test]
        public void IndependentControls_PreserveImageModesAndSourceDepth()
        {
            var random = FindImage(ListPrefabPath, 8101640552671880487L);
            Assert.That(random, Is.TypeOf<QualityUiImage>());
            Assert.That(random.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(random.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(random.fillAmount, Is.EqualTo(1f));

            var comment = FindImage(ListPrefabPath, 6145629994562058209L);
            Assert.That(comment, Is.TypeOf<QualityUiImage>());
            Assert.That(comment.type, Is.EqualTo(Image.Type.Sliced));

            var plus = FindImage(ListPrefabPath, 3292214349049898654L);
            Assert.That(plus, Is.TypeOf<QualityUiImage>());
            Assert.That(plus.rectTransform.rect.size, Is.EqualTo(new Vector2(50f, 50f)));
            Assert.That(plus.sprite.name, Is.EqualTo("icons_9"));

            var cover = FindImage("Assets/Prefabs/List/Model/SongCoverDisplayer.prefab", 2115732643789034301L);
            Assert.That(cover.material.GetFloat("_ListDepthOffset"), Is.EqualTo(151f));
            Assert.That(cover.material.GetFloat("_Brightness"), Is.EqualTo(1f));
        }

        /// <summary>Checks that quality-aware panels retain the authored shader used for automatic UI classification.</summary>
        /// <param name="path">The prefab containing the panel.</param>
        /// <param name="fileId">The unchanged Image identifier.</param>
        /// <param name="shaderName">The authored shader that determines automatic mode selection.</param>
        /// <param name="spriteName">The original paired atlas slice.</param>
        [TestCase(ListPrefabPath, 5019321880153334812L, "Maj/UI/Default", "newui@3x_22")]
        [TestCase("Assets/Prefabs/UI/ChartVisualDisplayer.prefab", 7983312345234581846L, "Maj/UI/Default", "newui@3x_8")]
        [TestCase("Assets/Prefabs/Setting/MenuTitleDisplayer.prefab", 857430918264730003L, "Maj/UI/Default", "newui@3x_6")]
        [TestCase("Assets/Prefabs/Setting/MenuTitleDisplayer.prefab", 3255417888370853433L, "Maj/UI/Rounded Rect", "")]
        public void BackgroundPanels_RetainAuthoredAlphaSource(string path, long fileId, string shaderName, string spriteName)
        {
            var image = FindImage(path, fileId);
            Assert.That(image, Is.TypeOf<QualityUiImage>());
            Assert.That(new SerializedObject(image).FindProperty("_isBackgroundPanel"), Is.Null);
            Assert.That(image.material.shader.name, Is.EqualTo(shaderName));
            Assert.That(image.color.a, Is.EqualTo(spriteName == "newui@3x_22" ? 0.85f : 1f));
            if (spriteName.Length == 0)
            {
                Assert.That(image.sprite, Is.Null);
            }
            else
            {
                Assert.That(image.sprite.name, Is.EqualTo(spriteName));
            }
        }

        /// <summary>Checks shared panels keep their alpha source without obsolete per-scene classification overrides.</summary>
        [Test]
        public void ListSceneBackgroundPanels_UseSourceMaterialsWithoutOverrides()
        {
            foreach (var fileId in new[] { 4988950831640157351L, 782174714398340511L })
            {
                var source = FindImage("Assets/Prefabs/UI/UserInfo.prefab", fileId);
                Assert.That(new SerializedObject(source).FindProperty("_isBackgroundPanel"), Is.Null);
                Assert.That(source.material.shader.name, Is.EqualTo("Maj/UI/Default"));
            }
            var scene = FileSystem.File.Open(Application.dataPath + "/Scenes/List.unity").ReadAllText();
            var userInfo = Regex.Match(scene, @"(?ms)^--- !u!1001 &6406722161307597591\r?\n.*?(?=^--- |\z)");
            Assert.That(userInfo.Success, Is.True);
            Assert.That(userInfo.Value, Does.Not.Contain("_isBackgroundPanel"));
            var ranking = Regex.Match(scene, @"(?ms)^--- !u!114 &1674856456\r?\n.*?(?=^--- |\z)");
            Assert.That(ranking.Success, Is.True);
            Assert.That(ranking.Value, Does.Contain("guid: 78a3c5eb06624f448a0238577d1de902"));
            Assert.That(ranking.Value, Does.Contain("fileID: -1733998859, guid: 04e00e0b72a3d284ebec3f7160996aa1"));
            Assert.That(scene, Does.Not.Contain("_isBackgroundPanel"));
        }

        /// <summary>Checks the real Setting background images participate through their original alpha source without manual flags.</summary>
        /// <param name="fileId">The authored Setting Image identifier.</param>
        /// <param name="spriteId">The original Ribbon Sprite identifier.</param>
        [TestCase(1053645854L, 936887466L)]
        [TestCase(489720307L, 660075131L)]
        public void SettingSceneBackgrounds_RetainQualityAwareAlphaSources(long fileId, long spriteId)
        {
            var scene = FileSystem.File.Open(Application.dataPath + "/Scenes/Setting.unity").ReadAllText();
            var component = Regex.Match(scene, @"(?ms)^--- !u!114 &" + fileId + @"\r?\n.*?(?=^--- |\z)");

            Assert.That(component.Success, Is.True);
            Assert.That(component.Value, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
            Assert.That(component.Value, Does.Contain("guid: 78a3c5eb06624f448a0238577d1de902"));
            Assert.That(component.Value, Does.Contain("fileID: " + spriteId + ", guid: 04e00e0b72a3d284ebec3f7160996aa1"));
            Assert.That(component.Value, Does.Not.Contain("_isBackgroundPanel"));
        }

        /// <summary>Finds a real authored Image by its unchanged local identifier.</summary>
        /// <param name="path">The prefab asset to inspect without instantiating or saving it.</param>
        /// <param name="fileId">The serialized Image component identifier that must survive migration.</param>
        /// <returns>The Image component with the requested original identifier.</returns>
        private static Image FindImage(string path, long fileId)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            foreach (var image in prefab.GetComponentsInChildren<Image>(true))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image, out _, out long candidateId) && candidateId == fileId)
                {
                    return image;
                }
            }
            Assert.Fail($"{path}: Image {fileId} is missing or lost its serialized binding.");
            return null!;
        }
    }
}
