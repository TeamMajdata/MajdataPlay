using MajdataPlay.Behaviours.UI;
using MajdataPlay.Editor;
using MajdataPlay.Scenes.Result;
using MajdataPlay.Scenes.Result.Components;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>
    /// Covers the edit-mode animation preview state machines of the custom inspectors, driven through their editor
    /// update callbacks rather than inspector GUI events.
    /// </summary>
    public class InspectorPreviewTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the test's objects, including inspectors.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Creates a text under a parent.</summary>
        /// <param name="name">The object name.</param>
        /// <returns>The text.</returns>
        private TextMeshProUGUI Text(string name)
        {
            return _objects.Add<TextMeshProUGUI>(name);
        }

        /// <summary>Creates a result screen manager with the texts its preview restores.</summary>
        /// <returns>The manager.</returns>
        private ResultScreenManager CreateResultScreen()
        {
            var manager = _objects.Add<ResultScreenManager>("Result");
            manager.accDX = Text("Acc");
            manager.criticalLabel = Text("CL");
            manager.perfectLabel = Text("PL");
            manager.greatLabel = Text("GL");
            manager.goodLabel = Text("GoL");
            manager.missLabel = Text("ML");
            manager.criticalCount = Text("CC");
            manager.perfectCount = Text("PC");
            manager.greatCount = Text("GC");
            manager.goodCount = Text("GoC");
            manager.missCount = Text("MC");
            manager.clearText = Text("Clear");
            var dxScore = _objects.Add<DXScoreDisplayer>("DX");
            PrivateMember.SetField(dxScore, "_dxScoreSlider", _objects.Add<Slider>("Slider"));
            PrivateMember.SetField(dxScore, "_dxScoreDisplayer", Text("DXText"));
            PrivateMember.SetField(manager, "_dxScoreDisplayer", dxScore);
            return manager;
        }

        /// <summary>Checks that an expired result preview restores the final, fully visible state.</summary>
        [Test]
        public void ResultScreenInspector_ExpiredPreviewRestoresFinalState()
        {
            var manager = CreateResultScreen();
            var color = manager.missLabel.color;
            color.a = 0f;
            manager.missLabel.color = color;
            var inspector = _objects.Track((ResultScreenManagerInspector)UnityEditor.Editor.CreateEditor(manager));
            PrivateMember.SetField(inspector, "_isPreviewing", true);
            PrivateMember.SetField(inspector, "_previewRepaintUntil", 0d);

            PrivateMember.Invoke(inspector, "RepaintPreview");

            Assert.That(PrivateMember.GetField<bool>(inspector, "_isPreviewing"), Is.False);
            Assert.That(manager.missLabel.color.a, Is.EqualTo(1f));
        }

        /// <summary>Checks that a running result preview keeps repainting until it expires.</summary>
        [Test]
        public void ResultScreenInspector_RunningPreviewKeepsRepainting()
        {
            var inspector = _objects.Track((ResultScreenManagerInspector)UnityEditor.Editor.CreateEditor(CreateResultScreen()));

            PrivateMember.Invoke(inspector, "RepaintPreview");
            PrivateMember.SetField(inspector, "_isPreviewing", true);
            PrivateMember.SetField(inspector, "_previewRepaintUntil", double.MaxValue);
            PrivateMember.Invoke(inspector, "RepaintPreview");

            Assert.That(PrivateMember.GetField<bool>(inspector, "_isPreviewing"), Is.True);
        }

        /// <summary>Checks that the button preview uses the configured duration and restores the original scale.</summary>
        /// <param name="animationDuration">The serialized duration of each scale animation.</param>
        [TestCase(0.4f)]
        [TestCase(-0.1f)]
        public void ButtonAnimationInspector_PreviewRestoresOriginalScale(float animationDuration)
        {
            var button = _objects.Add<ButtonAnimation>("Button");
            PrivateMember.SetField(button, "_releasedScale", 1.2f);
            PrivateMember.SetField(button, "_animationDuration", animationDuration);
            button.transform.localScale = new Vector3(2f, 2f, 2f);
            var inspector = _objects.Track((ButtonAnimationInspector)UnityEditor.Editor.CreateEditor(button));

            var previewStartedAt = EditorApplication.timeSinceStartup;
            PrivateMember.Invoke(inspector, "StartPreview");
            var previewCheckedAt = EditorApplication.timeSinceStartup;
            var previewDuration = Mathf.Max(animationDuration, 0f) * 2f + 0.1d;

            Assert.That(PrivateMember.GetField<bool>(inspector, "_isPreviewing"), Is.True);
            Assert.That(PrivateMember.GetField<double>(inspector, "_previewRepaintUntil"),
                Is.InRange(previewStartedAt + previewDuration, previewCheckedAt + previewDuration));
            Assert.That(button.transform.localScale.x, Is.EqualTo(1.2f).Within(1e-4f));

            PrivateMember.SetField(inspector, "_previewRepaintUntil", 0d);
            PrivateMember.Invoke(inspector, "RepaintPreview");

            Assert.That(PrivateMember.GetField<bool>(inspector, "_isPreviewing"), Is.False);
            Assert.That(button.transform.localScale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
        }

        /// <summary>Checks that the scene switcher preview applies the requested final transition state.</summary>
        [Test]
        public void SceneSwitcherInspector_PreviewFinishesInRequestedState()
        {
            var switcher = _objects.Add<SceneSwitcher>("Switcher");
            var mask = _objects.Create("Mask").AddComponent<RectTransform>();
            var main = _objects.Add<Image>("Main", mask);
            var overlay = _objects.Add<Image>("Overlay");
            var loading = _objects.Add<TextMeshProUGUI>("Loading");
            PrivateMember.SetField(switcher, "_mainImage", main);
            PrivateMember.SetField(switcher, "_overlayImage", overlay);
            PrivateMember.SetField(switcher, "_subImage", _objects.Add<Image>("Sub"));
            PrivateMember.SetField(switcher, "_loadingText", loading);
            var inspector = _objects.Track((SceneSwitcherInspector)UnityEditor.Editor.CreateEditor(switcher));

            PrivateMember.Invoke(inspector, "Preview", true);
            Assert.That(PrivateMember.GetField<bool>(inspector, "_isPreviewing"), Is.True);

            PrivateMember.SetField(inspector, "_repaintUntil", 0d);
            PrivateMember.Invoke(inspector, "RepaintPreview");

            Assert.That(PrivateMember.GetField<bool>(inspector, "_isPreviewing"), Is.False);
            Assert.That(overlay.color.a, Is.EqualTo(1f));
            Assert.That(loading.gameObject.activeSelf, Is.True);
        }
    }
}
