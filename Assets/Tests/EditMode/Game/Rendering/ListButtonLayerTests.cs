using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Protects the real List ring icons when their backgrounds use alpha composition.</summary>
    public sealed class ListButtonLayerTests
    {
        /// <summary>Identifies the List prefab containing the actual nested button overrides.</summary>
        private const string PrefabPath = "Assets/Prefabs/List/Main_Display_ribbon.prefab";

        /// <summary>Checks the blue ring keeps its original artwork and explicitly precedes camera extensions.</summary>
        [Test]
        public void ListBlueRing_PreservesArtworkAndOptsIntoEarlyBackgroundOrdering()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var ring = FindComponent<QualityUiImage>(prefab, 5136493199664605945L);
            var serialized = new SerializedObject(ring);
            Assert.That(serialized.FindProperty("_drawBeforeCameraExtension").boolValue, Is.True);
            Assert.That(ring.SourceMaterial.shader.name, Is.EqualTo("Maj/Common/Opaque"));
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ring.sprite, out var guid, out long id), Is.True);
            Assert.That(guid, Is.EqualTo("04e00e0b72a3d284ebec3f7160996aa1"));
            Assert.That(id, Is.EqualTo(1055266389L));
            Assert.That(ring.rectTransform.sizeDelta, Is.EqualTo(new Vector2(695f, 1042f)));
            Assert.That(ring.rectTransform.localPosition.z, Is.EqualTo(2f));
        }

        /// <summary>Checks history text remains sharp above the late cover while retaining its font and authored outline.</summary>
        /// <param name="fileId">The actual score, clear-mark or rank label identifier.</param>
        /// <param name="outlineWidth">The label's original font outline width.</param>
        [TestCase(3869631019735646211L, 0f)]
        [TestCase(7695777254672201215L, 0.301f)]
        [TestCase(6326863885416059801L, 0.301f)]
        public void ListHistoryLabels_AreAfterTheCoverWithoutChangingFontStyle(long fileId, float outlineWidth)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var label = FindComponent<TMPro.TextMeshProUGUI>(prefab, fileId);
            var material = label.fontSharedMaterial;
            var cover = AssetDatabase.LoadAssetAtPath<Material>("Assets/Rendering/Materials/UI/List/CoverForegroundAlpha.mat");
            Assert.That(material.renderQueue, Is.EqualTo(3502));
            Assert.That(material.renderQueue, Is.GreaterThan(cover.renderQueue));
            Assert.That(material.shader, Is.SameAs(label.font.material.shader));
            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(label.font.material.GetTexture("_MainTex")));
            Assert.That(material.GetFloat("_OutlineWidth"), Is.EqualTo(outlineWidth));
            Assert.That(material.GetFloat("_ListDepthOffset"), Is.Zero);
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(label.font)),
                Is.EqualTo("dbc58334dc23ba04fbfee4bf5298b6a0"));
            if (outlineWidth > 0f)
            {
                Assert.That(material.IsKeywordEnabled("UNDERLAY_ON"), Is.True);
                Assert.That(material.GetColor("_OutlineColor"), Is.EqualTo(Color.white));
            }
        }

        /// <summary>Checks the clear-mark backing joins its sharp labels without altering its original sprite or color.</summary>
        [Test]
        public void ListHistoryBacking_IsForegroundAndPreservesItsAuthoredAppearance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var image = FindComponent<Image>(prefab, 1261365039573097186L);
            Assert.That(image.material.shader.name, Is.EqualTo("Maj/UI/Atlas Alpha"));
            Assert.That(image.material.renderQueue, Is.EqualTo(3502));
            Assert.That(image.color, Is.EqualTo(new Color(0.8867924f, 0.78551507f, 0.31288713f, 0f)));
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image.sprite, out var guid, out long id), Is.True);
            Assert.That(guid, Is.EqualTo("85371318f6efbc54a8702e8e1835ee9d"));
            Assert.That(id, Is.EqualTo(21300000L));
        }

        /// <summary>Checks every authored foreground is drawn after its background without changing its depth.</summary>
        [Test]
        public void ListRingSymbols_AreAfterTheirBackgroundsInCanvasOrder()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var buttons = prefab.transform.Find("Buttons");
            Assert.That(buttons, Is.Not.Null, "Inspect the real nested Buttons instance, including List overrides.");
            var foregroundCount = 0;
            for (var buttonIndex = 1; buttonIndex <= 8; buttonIndex++)
            {
                var button = buttons.Find("Button" + buttonIndex);
                Assert.That(button, Is.Not.Null);
                var background = button.Find("BG");
                Assert.That(background, Is.Not.Null);
                Assert.That(background.GetSiblingIndex(), Is.Zero,
                    button.name + ": the alpha background must precede all foreground symbols.");
                Assert.That(background.localPosition.z, Is.EqualTo(1f),
                    button.name + ": retain the existing background depth for the cutout path.");
                foreach (Transform child in button)
                {
                    if (child == background || !child.TryGetComponent<Image>(out var icon))
                    {
                        continue;
                    }
                    Assert.That(icon, Is.TypeOf<QualityUiImage>(),
                        button.name + ": the foreground must participate in the same quality switch.");
                    Assert.That(child.GetSiblingIndex(), Is.GreaterThan(background.GetSiblingIndex()),
                        button.name + ": drawing an alpha symbol before its background hides the symbol.");
                    Assert.That(child.localPosition.z, Is.Zero,
                        button.name + ": preserve the original foreground depth as well as Canvas order.");
                    foregroundCount++;
                }
            }
            Assert.That(foregroundCount, Is.EqualTo(9), "Check all eight buttons, including both Button4 graphics.");
        }

        /// <summary>Checks reordering does not replace the +/- artwork or alter the hold-progress contract.</summary>
        [Test]
        public void ListRingSymbols_PreserveSpritesAndRadialHoldProgress()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var buttons = prefab.transform.Find("Buttons");
            Assert.That(buttons, Is.Not.Null);
            AssertSprite(buttons.Find("Button1"), 356726101L);
            AssertSprite(buttons.Find("Button8"), -1820208894L);

            var progressCount = 0;
            foreach (var image in buttons.Find("Button4").GetComponentsInChildren<Image>(true))
            {
                if (image.type != Image.Type.Filled)
                {
                    continue;
                }
                Assert.That(image.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
                Assert.That(image.fillAmount, Is.EqualTo(1f));
                Assert.That(image.fillOrigin, Is.EqualTo(2));
                Assert.That(image.fillClockwise, Is.True);
                progressCount++;
            }
            Assert.That(progressCount, Is.EqualTo(1), "Keep the existing hold-progress Image for ButtonAnimation.");
        }

        /// <summary>Finds the exact original icon Sprite beneath an authored ring button.</summary>
        /// <param name="button">The button whose direct foreground images are inspected.</param>
        /// <param name="expectedId">The original icons.png Sprite file identifier.</param>
        private static void AssertSprite(Transform button, long expectedId)
        {
            var matches = 0;
            foreach (Transform child in button)
            {
                if (!child.TryGetComponent<Image>(out var image) || image.sprite == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image.sprite, out var guid, out long id) ||
                    guid != "736513dcbc18dec438227ea2c0cb9831" || id != expectedId)
                {
                    continue;
                }
                Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(50f, 50f)));
                Assert.That(image.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0f, -70f)));
                matches++;
            }
            Assert.That(matches, Is.EqualTo(1), button.name + ": retain the exact original +/- Sprite and layout.");
        }

        /// <summary>Finds a serialized production component without instantiating scene managers.</summary>
        /// <typeparam name="T">The component type to inspect.</typeparam>
        /// <param name="prefab">The imported List prefab.</param>
        /// <param name="fileId">The component's persistent local identifier.</param>
        /// <returns>The exact component referenced by the production prefab.</returns>
        private static T FindComponent<T>(GameObject prefab, long fileId) where T : Component
        {
            Assert.That(prefab, Is.Not.Null);
            foreach (var component in prefab.GetComponentsInChildren<T>(true))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out _, out long id) && id == fileId)
                {
                    return component;
                }
            }
            Assert.Fail("The real List component is missing: " + fileId);
            return null!;
        }
    }
}
