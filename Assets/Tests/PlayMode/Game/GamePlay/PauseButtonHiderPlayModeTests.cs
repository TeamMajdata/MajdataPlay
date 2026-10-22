using System.Collections;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks that <see cref="PauseButtonHider"/> hides the pause button on desktop platforms in Play Mode.</summary>
    public class PauseButtonHiderPlayModeTests
    {
        /// <summary>Checks that the button image becomes fully transparent once Start ran.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_MakesTheButtonTransparent()
        {
            using var objects = new TestObjects();
            var image = objects.Add<Image>("PauseButton");
            image.color = Color.white;
            image.gameObject.AddComponent<PauseButtonHider>();

            yield return null;

            Assert.That(image.color, Is.EqualTo(new Color(0, 0, 0, 0)));
        }
    }
}
