using System.Collections;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using TaskTracker = MajdataPlay.Threading.TaskTracker;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Threading
{
    /// <summary>
    /// Covers the Register and RegisterAsWorker extension methods for Task, ValueTask and UniTask, which forward to
    /// <see cref="TaskTracker"/>. The tracker's static state is saved before and restored after every test.
    /// </summary>
    public class TaskRegistrationExtensionsTests
    {
        /// <summary>Restores the tracker state after each test.</summary>
        private TaskTrackerIsolation _isolation = null!;

        /// <summary>Saves the tracker state and enables history.</summary>
        [SetUp]
        public void SetUp()
        {
            _isolation = new TaskTrackerIsolation();
            TaskTracker.EnableTracking = true;
        }

        /// <summary>Restores the tracker state.</summary>
        [TearDown]
        public void TearDown()
        {
            _isolation.Dispose();
        }

        /// <summary>Asserts the recorded name, scene and worker flag of a registration.</summary>
        /// <param name="name">The registration name.</param>
        /// <param name="scene">The expected scene.</param>
        /// <param name="isWorker">The expected worker flag.</param>
        private static void AssertRegistration(string name, string scene, bool isWorker)
        {
            var snapshot = TaskTrackerIsolation.Single(name);
            Assert.That(snapshot.SceneName, Is.EqualTo(scene));
            Assert.That(snapshot.IsWorker, Is.EqualTo(isWorker));
        }

        /// <summary>Checks the Task extensions, which return the original task.</summary>
        [Test]
        public void TaskExtensions_RegisterWithCategory()
        {
            var plain = new TaskCompletionSource<bool>();
            var worker = new TaskCompletionSource<bool>();
            var typed = new TaskCompletionSource<int>();
            var typedWorker = new TaskCompletionSource<int>();
            var names = new[]
            {
                TaskTrackerIsolation.UniqueName("task"),
                TaskTrackerIsolation.UniqueName("task-worker"),
                TaskTrackerIsolation.UniqueName("task-typed"),
                TaskTrackerIsolation.UniqueName("task-typed-worker"),
            };

            Assert.That(((Task)plain.Task).Register(names[0], "S"), Is.SameAs(plain.Task));
            Assert.That(((Task)worker.Task).RegisterAsWorker(names[1], "S"), Is.SameAs(worker.Task));
            Assert.That(typed.Task.Register(names[2], "S"), Is.SameAs(typed.Task));
            Assert.That(typedWorker.Task.RegisterAsWorker(names[3], "S"), Is.SameAs(typedWorker.Task));

            AssertRegistration(names[0], "S", false);
            AssertRegistration(names[1], "S", true);
            AssertRegistration(names[2], "S", false);
            AssertRegistration(names[3], "S", true);
            plain.SetResult(true);
            worker.SetResult(true);
            typed.SetResult(1);
            typedWorker.SetResult(1);
        }

        /// <summary>Checks that omitting the name records the task type.</summary>
        [Test]
        public void TaskExtensions_DefaultName_UsesTaskType()
        {
            var scene = TaskTrackerIsolation.UniqueName("scene");
            var source = new TaskCompletionSource<int>();

            source.Task.Register(sceneName: scene);

            Assert.That(TaskTrackerIsolation.Find(source.Task.GetType().ToString()), Has.Some.Matches<TaskTrackerSnapshot>(x => x.SceneName == scene));
            source.SetResult(1);
        }

        /// <summary>Checks that omitting the scene records the active scene, as TaskTracker.Register does.</summary>
        [Test]
        [Ignore("Known issue: the Register/RegisterAsWorker extensions default sceneName to \"\" instead of null, so the active scene is never recorded.")]
        public void TaskExtensions_DefaultScene_RecordsActiveScene()
        {
            var name = TaskTrackerIsolation.UniqueName("default-scene");
            var source = new TaskCompletionSource<int>();

            source.Task.Register(name);

            Assert.That(TaskTrackerIsolation.Single(name).SceneName, Is.EqualTo(TaskTrackerIsolation.DescribeActiveScene()));
            source.SetResult(1);
        }

        /// <summary>Checks the ValueTask extensions, which return awaitable replacements.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ValueTaskExtensions_RegisterWithCategory() => UniTask.ToCoroutine(async () =>
        {
            var names = new[]
            {
                TaskTrackerIsolation.UniqueName("valuetask"),
                TaskTrackerIsolation.UniqueName("valuetask-worker"),
                TaskTrackerIsolation.UniqueName("valuetask-typed"),
                TaskTrackerIsolation.UniqueName("valuetask-typed-worker"),
            };

            var plain = new ValueTask(Task.FromResult(true)).Register(names[0], "S");
            var worker = new ValueTask(Task.Delay(1)).RegisterAsWorker(names[1], "S");
            var typed = new ValueTask<int>(3).Register(names[2], "S");
            var typedWorker = new ValueTask<int>(Task.FromResult(4)).RegisterAsWorker(names[3], "S");

            await plain;
            await worker;
            Assert.That(await typed, Is.EqualTo(3));
            Assert.That(await typedWorker, Is.EqualTo(4));
            AssertRegistration(names[0], "S", false);
            AssertRegistration(names[1], "S", true);
            AssertRegistration(names[2], "S", false);
            AssertRegistration(names[3], "S", true);
        });

        /// <summary>Checks the UniTask extensions, which return awaitable replacements.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UniTaskExtensions_RegisterWithCategory() => UniTask.ToCoroutine(async () =>
        {
            var names = new[]
            {
                TaskTrackerIsolation.UniqueName("unitask"),
                TaskTrackerIsolation.UniqueName("unitask-worker"),
                TaskTrackerIsolation.UniqueName("unitask-typed"),
                TaskTrackerIsolation.UniqueName("unitask-typed-worker"),
            };

            var plain = UniTask.Yield().ToUniTask().Register(names[0], "S");
            var worker = UniTask.CompletedTask.RegisterAsWorker(names[1], "S");
            var typed = UniTask.FromResult(5).Register(names[2], "S");
            var typedWorker = UniTask.FromResult(6).RegisterAsWorker(names[3], "S");

            await plain;
            await worker;
            Assert.That(await typed, Is.EqualTo(5));
            Assert.That(await typedWorker, Is.EqualTo(6));
            AssertRegistration(names[0], "S", false);
            AssertRegistration(names[1], "S", true);
            AssertRegistration(names[2], "S", false);
            AssertRegistration(names[3], "S", true);
        });
    }
}
