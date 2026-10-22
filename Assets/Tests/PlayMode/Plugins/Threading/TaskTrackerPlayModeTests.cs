using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TaskTracker = MajdataPlay.Threading.TaskTracker;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Threading.PlayMode
{
    /// <summary>
    /// Covers <see cref="TaskTracker"/> in Play Mode, where the runtime initialization has run, the player loop drives
    /// UniTask and real active scene changes update the cached scene. History added by a test is removed afterwards.
    /// </summary>
    public class TaskTrackerPlayModeTests
    {
        /// <summary>The tasks in the history before the test.</summary>
        private HashSet<object> _savedHistory = null!;

        /// <summary>Remembers the history before the test.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedHistory = new HashSet<object>(History.Keys.Cast<object>());
        }

        /// <summary>Removes the history the test added.</summary>
        [TearDown]
        public void TearDown()
        {
            lock (PrivateMember.GetStaticField<object>(typeof(TaskTracker), "Gate"))
            {
                var added = History.Keys.Cast<object>().Where(x => !_savedHistory.Contains(x)).ToArray();
                foreach (var key in added)
                {
                    History.Remove(key);
                }
            }
        }

        /// <summary>Gets the tracker's history dictionary.</summary>
        private static IDictionary History => PrivateMember.GetStaticField<IDictionary>(typeof(TaskTracker), "_entries");

        /// <summary>Gets the scene description the tracker records for a scene.</summary>
        /// <param name="scene">The scene.</param>
        /// <returns>The scene path or the unsaved scene marker.</returns>
        private static string Describe(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path;
        }

        /// <summary>Finds the snapshot registered under a name.</summary>
        /// <param name="name">The registration name.</param>
        /// <returns>The snapshot.</returns>
        private static TaskTrackerSnapshot Find(string name)
        {
            return TaskTracker.GetSnapshots().Single(x => x.Name == name);
        }

        /// <summary>Checks that a frame-driven UniTask is running until the player loop completes it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Register_FrameDrivenUniTask_CompletesWithPlayerLoop() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(Application.isPlaying, Is.True);
            var name = "playmode-" + Guid.NewGuid().ToString("N");
            var startFrame = Time.frameCount;

            var returned = TaskTracker.Register(UniTask.DelayFrame(3), name);

            var running = Find(name);
            Assert.That(running.IsCompleted, Is.False);
            Assert.That(running.SceneName, Is.EqualTo(Describe(SceneManager.GetActiveScene())));
            Assert.That(TaskTracker.HasRunningNonWorkerTasks, Is.True);

            await returned;

            Assert.That(Time.frameCount, Is.GreaterThanOrEqualTo(startFrame + 3));
            Assert.That(Find(name).Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(Find(name).Elapsed, Is.GreaterThan(TimeSpan.Zero));
        });

        /// <summary>Checks that a worker-thread registration records the scene that became active through a real scene change.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ActiveSceneChange_IsCachedForWorkerThreads()
        {
            var previous = SceneManager.GetActiveScene();
            var created = SceneManager.CreateScene("TaskTrackerScene-" + Guid.NewGuid().ToString("N"));
            var name = "scene-" + Guid.NewGuid().ToString("N");
            var source = new TaskCompletionSource<int>();
            try
            {
                SceneManager.SetActiveScene(created);
                var registration = Task.Run(() =>
                {
                    TaskTracker.Register(source.Task, name);
                });
                while (!registration.IsCompleted)
                {
                    yield return null;
                }

                var snapshot = Find(name);
                Assert.That(snapshot.SceneName, Is.EqualTo(Describe(created)));
                Assert.That(snapshot.IsSceneCached, Is.True);
            }
            finally
            {
                source.TrySetResult(0);
                SceneManager.SetActiveScene(previous);
            }

            yield return SceneManager.UnloadSceneAsync(created);
        }
    }
}
