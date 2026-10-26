using System.Collections;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Controllers;
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
    /// Covers <see cref="NoteComponentUpdater"/>, the attribute-driven updater that dispatches player loop callbacks to
    /// marked note components.
    /// </summary>
    public class NoteComponentUpdaterTests
    {
        /// <summary>The isolation scope of the current test.</summary>
        private NoteTestEnvironment _environment = null!;

        /// <summary>The updater under test.</summary>
        private NoteComponentUpdater _updater = null!;

        /// <summary>The marked note component below the updater.</summary>
        private ProbeNoteComponent _component = null!;

        /// <summary>The unmarked behaviour below the updater.</summary>
        private ProbePlainBehaviour _plain = null!;

        /// <summary>Creates the updater with one marked component and one plain behaviour as children.</summary>
        [SetUp]
        public void SetUp()
        {
            _environment = new NoteTestEnvironment();
            _updater = _environment.Objects.Add<NoteComponentUpdater>("Updater");
            _component = _environment.Objects.Add<ProbeNoteComponent>("Component", _updater.transform);
            _component.SetActive(true);
            _plain = _environment.Objects.Add<ProbePlainBehaviour>("Plain", _updater.transform);
        }

        /// <summary>Destroys the objects and restores the global state.</summary>
        [TearDown]
        public void TearDown()
        {
            _environment.Dispose();
        }

        /// <summary>Checks that the frame phases reach the marked component and never the plain behaviour.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Phases_ReachMarkedComponents() => UniTask.ToCoroutine(async () =>
        {
            await _updater.InitAsync();
            await UniTask.SwitchToMainThread();

            _updater.OnPreUpdate();
            _updater.OnUpdate();
            _updater.OnFixedUpdate();
            _updater.OnLateUpdate();

            Assert.That(_component.PreUpdateCount, Is.EqualTo(1));
            Assert.That(_component.UpdateCount, Is.EqualTo(1));
            Assert.That(_component.FixedUpdateCount, Is.EqualTo(1));
            Assert.That(_component.LateUpdateCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(_plain.UpdateCount, Is.EqualTo(0));
            Assert.That(_updater.PreUpdateElapsedMs, Is.GreaterThanOrEqualTo(0d));
            Assert.That(_updater.UpdateElapsedMs, Is.GreaterThanOrEqualTo(0d));
            Assert.That(_updater.FixedUpdateElapsedMs, Is.GreaterThanOrEqualTo(0d));
            Assert.That(_updater.LateUpdateElapsedMs, Is.GreaterThanOrEqualTo(0d));
        });

        /// <summary>Checks that late update callbacks only run in the late update phase.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Phases_RunLateCallbacksOnlyInLateUpdate() => UniTask.ToCoroutine(async () =>
        {
            await _updater.InitAsync();
            await UniTask.SwitchToMainThread();

            _updater.OnPreUpdate();
            _updater.OnUpdate();

            Assert.That(_component.LateUpdateCount, Is.EqualTo(0));
        });

        /// <summary>Checks that components that are hidden or out of play are not updated.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Phases_SkipInactiveAndFinishedComponents() => UniTask.ToCoroutine(async () =>
        {
            await _updater.InitAsync();
            await UniTask.SwitchToMainThread();

            _component.SetActive(false);
            _updater.OnUpdate();
            _component.SetActive(true);
            _component.State = NoteStatus.End;
            _updater.OnUpdate();
            _component.State = NoteStatus.Start;
            _updater.OnFixedUpdate();

            Assert.That(_component.UpdateCount, Is.EqualTo(0));
            Assert.That(_component.FixedUpdateCount, Is.EqualTo(0));
        });

        /// <summary>Checks that clearing the updater stops all dispatching, also when it is destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Clear_StopsDispatching() => UniTask.ToCoroutine(async () =>
        {
            await _updater.InitAsync();
            await UniTask.SwitchToMainThread();

            _updater.Clear();
            _updater.OnPreUpdate();
            _updater.OnUpdate();
            _updater.OnFixedUpdate();
            _updater.OnLateUpdate();

            Assert.That(_component.UpdateCount, Is.EqualTo(0));
            Object.Destroy(_updater.gameObject);
            await UniTask.Yield();
            Assert.That(_updater == null, Is.True);
        });

        /// <summary>Checks that an updater without children dispatches nothing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_HandlesEmptyHierarchy() => UniTask.ToCoroutine(async () =>
        {
            var empty = _environment.Objects.Add<NoteComponentUpdater>("Empty");

            await empty.InitAsync();
            await UniTask.SwitchToMainThread();

            Assert.DoesNotThrow(() =>
            {
                empty.OnPreUpdate();
                empty.OnUpdate();
                empty.OnFixedUpdate();
                empty.OnLateUpdate();
            });
        });
    }
}
