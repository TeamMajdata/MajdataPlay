using System.Collections;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers <see cref="MultTouchHandler"/> and <see cref="TouchBorder"/>, which show stacked borders when several
    /// touches wait on the same sensor.
    /// </summary>
    public class MultTouchHandlerTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Builds the gameplay objects and lets the borders run their Start messages.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig(initializePools: false, initializeEffects: false);
            yield return null;
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks that one hidden border is created and placed on every sensor.</summary>
        [Test]
        public void Start_CreatesBorderForEverySensor()
        {
            var handler = _rig.TouchHandler.transform;

            Assert.That(handler.childCount, Is.EqualTo(33));
            var border = handler.GetChild((int)SensorArea.E3).GetComponent<TouchBorder>();
            Assert.That(border.AreaPosition, Is.EqualTo(SensorArea.E3));
            var expected = NoteHelper.GetTouchAreaPosition(SensorArea.E3);
            Assert.That(border.transform.position.x, Is.EqualTo(expected.x).Within(1e-4f));
            Assert.That(border.transform.position.y, Is.EqualTo(expected.y).Within(1e-4f));
            Assert.That(Renderer(SensorArea.E3, 0).enabled, Is.False);
            Assert.That(Renderer(SensorArea.E3, 0).sprite.name, Is.EqualTo("TouchBorder[0]"));
            Assert.That(Renderer(SensorArea.E3, 1).sprite.name, Is.EqualTo("TouchBorder[1]"));
            Assert.That(Majdata<MultTouchHandler>.Instance, Is.SameAs(_rig.TouchHandler));
        }

        /// <summary>Checks that the second and third queued touches show borders styled after themselves.</summary>
        [Test]
        public void Register_ShowsStackedBorders()
        {
            _rig.TouchHandler.Register(SensorArea.C, false, false);
            Assert.That(Renderer(SensorArea.C, 0).enabled, Is.False);

            _rig.TouchHandler.Register(SensorArea.C, true, false);
            Assert.That(Renderer(SensorArea.C, 0).enabled, Is.True);
            Assert.That(Renderer(SensorArea.C, 0).sprite.name, Is.EqualTo("TouchBorder_Each[0]"));
            Assert.That(Renderer(SensorArea.C, 1).enabled, Is.False);

            _rig.TouchHandler.Register(SensorArea.C, false, true);
            Assert.That(Renderer(SensorArea.C, 1).enabled, Is.True);
            Assert.That(Renderer(SensorArea.C, 1).sprite.name, Is.EqualTo("TouchBorder_Break[1]"));

            _rig.TouchHandler.Register(SensorArea.C, true, false);
            _rig.TouchHandler.Unregister(SensorArea.C);
            Assert.That(Renderer(SensorArea.C, 0).sprite.name, Is.EqualTo("TouchBorder_Break[0]"));
            Assert.That(Renderer(SensorArea.C, 1).sprite.name, Is.EqualTo("TouchBorder_Each[1]"));

            _rig.TouchHandler.Unregister(SensorArea.C);
            _rig.TouchHandler.Register(SensorArea.C, false, false);
            Assert.That(Renderer(SensorArea.C, 1).sprite.name, Is.EqualTo("TouchBorder[1]"));
            _rig.TouchHandler.Register(SensorArea.C, false, false);
            _rig.TouchHandler.Unregister(SensorArea.C);
            _rig.TouchHandler.Unregister(SensorArea.C);
            Assert.That(Renderer(SensorArea.C, 0).sprite.name, Is.EqualTo("TouchBorder[0]"));
        }

        /// <summary>Checks that removing more touches than were queued and clearing hide every border.</summary>
        [Test]
        public void UnregisterAndClear_HideBorders()
        {
            _rig.TouchHandler.Register(SensorArea.A1, false, false);
            _rig.TouchHandler.Register(SensorArea.A1, false, false);
            _rig.TouchHandler.Register(SensorArea.B1, false, false);
            _rig.TouchHandler.Register(SensorArea.B1, false, false);

            _rig.TouchHandler.Unregister(SensorArea.A1);
            _rig.TouchHandler.Unregister(SensorArea.A1);
            Assert.DoesNotThrow(() => _rig.TouchHandler.Unregister(SensorArea.A1));
            _rig.TouchHandler.Clear();

            Assert.That(Renderer(SensorArea.A1, 0).enabled, Is.False);
            Assert.That(Renderer(SensorArea.B1, 0).enabled, Is.False);
        }

        /// <summary>Checks that the border scale follows the touch scale setting.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_AppliesTouchScale()
        {
            _rig.Settings.Display.TouchScale = 2f;
            var border = Object.Instantiate(_rig.TouchBorderTemplate, _rig.Objects.Create("Parent").transform).GetComponent<TouchBorder>();
            border.AreaPosition = SensorArea.A2;
            yield return null;

            Assert.That(border.transform.localScale.x, Is.EqualTo(2f).Within(1e-5f));
        }

        /// <summary>Checks that destroying the handler frees the singleton.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_FreesSingleton()
        {
            Object.Destroy(_rig.TouchHandler.gameObject);
            yield return null;

            Assert.That(Majdata<MultTouchHandler>.IsNull, Is.True);
        }

        /// <summary>Gets one of the two border renderers of a sensor.</summary>
        /// <param name="area">The sensor.</param>
        /// <param name="index">0 for the second touch, 1 for the third.</param>
        /// <returns>The renderer.</returns>
        private SpriteRenderer Renderer(SensorArea area, int index)
        {
            return _rig.TouchHandler.transform.GetChild((int)area).GetChild(index).GetComponent<SpriteRenderer>();
        }
    }
}
