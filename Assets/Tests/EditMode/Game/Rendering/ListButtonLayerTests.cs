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
    }
}
