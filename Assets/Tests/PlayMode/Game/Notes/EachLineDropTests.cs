using System.Collections;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Doubles;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Covers <see cref="EachLineDrop"/>, the line joining simultaneous notes: initialization, growth with the bound notes
    /// and its end when one of them ends.
    /// </summary>
    public class EachLineDropTests
    {
        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>Builds the gameplay objects.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig(initializeEffects: false);
            yield return null;
        }

        /// <summary>Destroys the gameplay objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /// <summary>Checks that Awake hides the line and that renderer changes are ignored before Init.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_HidesLine()
        {
            var line = _rig.Spawn<EachLineDrop>(_rig.EachLineTemplate, _rig.EachLineParent);
            var renderer = line.GetComponent<SpriteRenderer>();
            yield return null;

            line.RendererState = RendererStatus.On;

            Assert.That(renderer.enabled, Is.False);
            Assert.That(renderer.sprite, Is.Null);
            Assert.That(line.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(line.Active, Is.True);
            Assert.That(line.IsInitialized, Is.False);
            Assert.That(line.IsDestroyed, Is.False);
            Assert.That(line.QueueInfo, Is.Not.Null);
            Assert.That(line.RendererState, Is.EqualTo(RendererStatus.Off));
        }

        /// <summary>Checks that Init picks the curve sprite, rotates the line and ignores repeated calls.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Init_PicksCurveAndRotation()
        {
            var line = _rig.SpawnEachLine(Info(new FakeDistanceProvider(), startPos: 3, curvLength: 2));
            yield return null;

            line.Init(Info(new FakeDistanceProvider(), startPos: 5, curvLength: 4));

            Assert.That(line.State, Is.EqualTo(NoteStatus.Inited));
            Assert.That(line.IsInitialized, Is.True);
            Assert.That(line.GetComponent<SpriteRenderer>().sprite.name, Is.EqualTo("EachLines[1]"));
            Assert.That(line.transform.rotation.eulerAngles.z, Is.EqualTo(270f).Within(0.01f));
            Assert.That(line.startPosition, Is.EqualTo(3));
            Assert.That(line.curvLength, Is.EqualTo(2));
        }

        /// <summary>Checks that the line appears and grows with the distance of its notes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_FollowsNoteDistance()
        {
            var provider = new FakeDistanceProvider();
            var line = _rig.SpawnEachLine(Info(provider));
            var renderer = line.GetComponent<SpriteRenderer>();
            yield return null;

            provider.Distance = -3f;
            _rig.Step(0f);
            Assert.That(line.State, Is.EqualTo(NoteStatus.Inited));

            provider.Distance = -2f;
            _rig.Step(0.1f);
            Assert.That(line.State, Is.EqualTo(NoteStatus.Scaling));
            Assert.That(renderer.enabled, Is.False);

            provider.Distance = -1f;
            _rig.Step(0.2f);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(line.gameObject.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
            Assert.That(line.transform.localScale.x, Is.EqualTo(1.225f / 4.8f).Within(1e-5f));

            provider.Distance = 2.4f;
            _rig.Step(0.3f);
            Assert.That(line.State, Is.EqualTo(NoteStatus.Running));
            Assert.That(line.transform.localScale.x, Is.EqualTo(0.5f).Within(1e-5f));

            provider.Distance = 4.8f;
            _rig.Step(0.4f);
            Assert.That(line.transform.localScale.x, Is.EqualTo(1f).Within(1e-5f));
        }

        /// <summary>Checks that the line ends and hides as soon as one of its notes ends.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnLateUpdate_EndsWhenANoteEnds()
        {
            var provider = new FakeDistanceProvider() { Distance = 2.4f };
            var line = _rig.SpawnEachLine(Info(provider));
            yield return null;
            _rig.Step(0f);

            provider.IsAnyNoteEnded = true;
            _rig.Step(0.1f);

            Assert.That(line.State, Is.EqualTo(NoteStatus.End));
            Assert.That(line.IsDestroyed, Is.True);
            Assert.That(line.GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(line.gameObject.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            line.OnLateUpdate();
            Assert.That(line.State, Is.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that destroying a line forgets the shared curve sprites so the next skin is reloaded.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_ResetsSharedCurveSprites()
        {
            var line = _rig.Spawn<EachLineDrop>(_rig.EachLineTemplate, _rig.EachLineParent);
            yield return null;
            Assert.That(PrivateMember.GetStaticField<bool>(typeof(EachLineDrop), "_isCurvSpritesInited"), Is.True);

            Object.Destroy(line.gameObject);
            yield return null;

            Assert.That(PrivateMember.GetStaticField<bool>(typeof(EachLineDrop), "_isCurvSpritesInited"), Is.False);
            Assert.That(PrivateMember.GetStaticField<Sprite[]>(typeof(EachLineDrop), "_curvSprites"), Is.Empty);
        }

        /// <summary>Creates each line pooling information.</summary>
        /// <param name="provider">The distance source.</param>
        /// <param name="startPos">The start key.</param>
        /// <param name="curvLength">The curve length from 1 to 4.</param>
        /// <returns>The information.</returns>
        private static EachLinePoolingInfo Info(FakeDistanceProvider provider, int startPos = 1, int curvLength = 1)
        {
            return new EachLinePoolingInfo()
            {
                StartPos = startPos,
                Timing = 1f,
                Speed = 7.5f,
                CurvLength = curvLength,
                DistanceProvider = provider
            };
        }
    }
}
