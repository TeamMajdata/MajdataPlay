using MajdataPlay.Scenes.Setting;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Protects authored icon/background ordering when buttons use alpha-blended artwork.</summary>
    public sealed class SettingButtonLayerTests
    {
        /// <summary>Checks category arrows stay above glass and only the Setting instance hides its former category keys.</summary>
        [Test]
        public void SettingCategoryNavigation_UsesForegroundArrowsAndLocalKeyOverrides()
        {
            const string scenePath = "Assets/Scenes/Setting.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }
            try
            {
                Transform? navigation = null;
                Transform? buttons = null;
                SettingManager? manager = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    manager ??= root.GetComponentInChildren<SettingManager>(true);
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (transform.name == "CategoryNavigation" && transform.parent?.name == "TitleDisplayers")
                        {
                            navigation = transform;
                        }
                        if (transform.name == "Buttons" && transform.Find("Button2") != null)
                        {
                            buttons = transform;
                        }
                    }
                }
                Assert.That(navigation, Is.Not.Null);
                Assert.That(buttons, Is.Not.Null);
                Assert.That(manager, Is.Not.Null);
                var group = navigation!.GetComponent<CanvasGroup>();
                Assert.That(group, Is.Not.Null);
                Assert.That(new SerializedObject(manager!).FindProperty("_navigationArrows").objectReferenceValue, Is.SameAs(group));
                Assert.That(buttons!.Find("Button1").gameObject.activeSelf, Is.True, "Keep the visible A1 delay-calibration action.");
                var calibration = buttons.Find("Button1/SettingsCalibrationIcon");
                Assert.That(calibration, Is.Not.Null, "The calibration icon must be authored in the Setting scene.");
                var icon = calibration!.GetComponent<Image>();
                Assert.That(icon, Is.Not.Null);
                Assert.That(icon!.sprite.name, Is.EqualTo("icons_33"));
                Assert.That(calibration.GetComponent<TMPro.TMP_Text>(), Is.Null);
                Assert.That(buttons.Find("Button1/SettingCalibrationLabel"), Is.Null);
                foreach (var name in new[] { "L", "R" })
                {
                    var glyph = navigation.Find(name).GetComponent<Image>();
                    Assert.That(glyph, Is.TypeOf<Image>(), "Navigation glyphs must not become glass-backed quality images.");
                    Assert.That(glyph.material.renderQueue, Is.EqualTo(3301), "Keep navigation glyphs after category glass.");
                    Assert.That(AssetDatabase.GetAssetPath(glyph.material), Is.EqualTo("Assets/Rendering/Materials/UI/List/CategoryNavigation.mat"));
                }
                var sharedButtons = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Buttons.prefab");
                Assert.That(sharedButtons, Is.Not.Null);
                foreach (var name in new[] { "Button2", "Button7" })
                {
                    Assert.That(buttons!.Find(name).gameObject.activeSelf, Is.False, "Hide the obsolete Setting category-key visuals.");
                    Assert.That(sharedButtons!.transform.Find(name).gameObject.activeSelf, Is.True, "Other scenes retain the shared ring keys.");
                }
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        /// <summary>Checks the real Setting prefab instance places vector arrows after their button backgrounds.</summary>
        [Test]
        public void SettingArrows_AreAfterTheirBackgroundsInCanvasOrder()
        {
            const string scenePath = "Assets/Scenes/Setting.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }
            try
            {
                var count = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                    {
                        if (graphic.GetType().FullName != "Unity.VectorGraphics.SVGImage" || graphic.transform.parent == null)
                        {
                            continue;
                        }
                        var parent = graphic.transform.parent;
                        var background = parent.Find("BG");
                        if (background == null || !parent.name.StartsWith("Button", System.StringComparison.Ordinal))
                        {
                            continue;
                        }
                        Assert.That(graphic.transform.GetSiblingIndex(), Is.GreaterThan(background.GetSiblingIndex()),
                            parent.name + ": alpha-blended backgrounds must be drawn before their vector icons.");
                        Assert.That(graphic.transform.localPosition.z, Is.LessThan(background.localPosition.z),
                            parent.name + ": retain the authored foreground depth as well as the Canvas order.");
                        count++;
                    }
                }
                Assert.That(count, Is.EqualTo(6), "Inspect all six actual Setting ring arrows.");
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
