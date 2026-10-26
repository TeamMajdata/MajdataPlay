using MajdataPlay.Scenes.Setting;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>Covers the renderer tint used to animate setting text colors without rebuilding TMP meshes.</summary>
    public class SettingTextTintTests : SceneTestBase
    {
        /// <summary>The tinted text.</summary>
        private TextMeshProUGUI _text = null!;

        /// <summary>Creates a red text under a canvas.</summary>
        [SetUp]
        public void SetUp()
        {
            _text = CreateText("Text", CreateCanvas());
            _text.text = "Tint";
            _text.color = Color.red;
        }

        /// <summary>Checks that the tint moves the text color onto its canvas renderer.</summary>
        [Test]
        public void Constructor_MovesTextColorToRenderer()
        {
            using var tint = new SettingTextTint(_text);

            Assert.That(_text.color, Is.EqualTo(Color.white));
            Assert.That(_text.canvasRenderer.GetColor(), Is.EqualTo(Color.red));
        }

        /// <summary>Checks that a new color is applied to the renderer.</summary>
        [Test]
        public void SetColor_AppliesNewColor()
        {
            using var tint = new SettingTextTint(_text);

            tint.SetColor(Color.blue);

            Assert.That(_text.canvasRenderer.GetColor(), Is.EqualTo(Color.blue));
        }

        /// <summary>Checks that setting the current color again does not touch the renderer.</summary>
        [Test]
        public void SetColor_SameColorIsIgnored()
        {
            using var tint = new SettingTextTint(_text);
            _text.canvasRenderer.SetColor(Color.black);

            tint.SetColor(Color.red);

            Assert.That(_text.canvasRenderer.GetColor(), Is.EqualTo(Color.black));
        }

        /// <summary>Checks that a mesh rebuild reapplies the tint to the renderers.</summary>
        [Test]
        public void MeshRebuild_ReappliesTint()
        {
            using var tint = new SettingTextTint(_text);
            tint.SetColor(Color.green);
            _text.canvasRenderer.SetColor(Color.black);

            _text.text = "Rebuilt";
            _text.ForceMeshUpdate(true, true);

            Assert.That(_text.canvasRenderer.GetColor(), Is.EqualTo(Color.green));
        }

        /// <summary>Checks that a disposed tint ignores colors and mesh rebuilds and can be disposed twice.</summary>
        [Test]
        public void Dispose_StopsTinting()
        {
            var tint = new SettingTextTint(_text);
            tint.Dispose();
            _text.canvasRenderer.SetColor(Color.black);

            tint.SetColor(Color.cyan);
            _text.ForceMeshUpdate(true, true);
            tint.Dispose();

            Assert.That(_text.canvasRenderer.GetColor(), Is.EqualTo(Color.black));
        }
    }
}
