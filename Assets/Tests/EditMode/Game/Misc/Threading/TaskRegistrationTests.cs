using System.Collections;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Threading;
using TaskTracker = MajdataPlay.Threading.TaskTracker;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Threading
{
    /// <summary>Covers the scene-aware task registration extensions for tasks, value tasks and UniTasks.</summary>
    public class TaskRegistrationTests
    {
        /// <summary>Gets the scene name the extensions attach to registrations.</summary>
        private static string Scene => SceneSwitcher.CurrentScene.ToString();

        /// <summary>Gets the number of running non-worker tasks of the current scene.</summary>
        /// <returns>The count.</returns>
        private static int NonWorkers()
        {
            return TaskTracker.GetRunningTaskCount(includeWorkers: false, sceneName: Scene);
        }

        /// <summary>Gets the number of running tasks, including workers, of the current scene.</summary>
        /// <returns>The count.</returns>
        private static int All()
        {
            return TaskTracker.GetRunningTaskCount(includeWorkers: true, sceneName: Scene);
        }

        /// <summary>Removes finished registrations.</summary>
        [TearDown]
        public void TearDown()
        {
            TaskTracker.ClearCompleted();
        }

        /// <summary>Checks that tasks are registered for the current scene as normal tasks or workers and keep their results.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TaskExtensions_RegisterForScene()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var nonWorkers = NonWorkers();
                var all = All();
                var plain = new TaskCompletionSource<bool>();
                var worker = new TaskCompletionSource<bool>();
                var typed = new TaskCompletionSource<int>();
                var typedWorker = new TaskCompletionSource<int>();

                var registered = ((Task)plain.Task).Register("plain");
                var registeredWorker = ((Task)worker.Task).RegisterAsWorker("worker");
                var registeredTyped = typed.Task.Register("typed");
                var registeredTypedWorker = typedWorker.Task.RegisterAsWorker("typed-worker");

                Assert.That(registered, Is.SameAs(plain.Task));
                Assert.That(registeredTyped, Is.SameAs(typed.Task));
                Assert.That(NonWorkers(), Is.EqualTo(nonWorkers + 2));
                Assert.That(All(), Is.EqualTo(all + 4));

                plain.SetResult(true);
                worker.SetResult(true);
                typed.SetResult(7);
                typedWorker.SetResult(8);
                await registeredWorker;

                Assert.That(await registeredTyped, Is.EqualTo(7));
                Assert.That(await registeredTypedWorker, Is.EqualTo(8));
                Assert.That(All(), Is.EqualTo(all));
            });
        }

        /// <summary>Checks that value tasks are registered and their results preserved.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ValueTaskExtensions_RegisterForScene()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var all = All();
                var nonWorkers = NonWorkers();
                var plain = new TaskCompletionSource<bool>();
                var worker = new TaskCompletionSource<bool>();
                var typed = new TaskCompletionSource<int>();
                var typedWorker = new TaskCompletionSource<int>();

                var registered = new ValueTask(plain.Task).Register("plain");
                var registeredWorker = new ValueTask(worker.Task).RegisterAsWorker("worker");
                var registeredTyped = new ValueTask<int>(typed.Task).Register("typed");
                var registeredTypedWorker = new ValueTask<int>(typedWorker.Task).RegisterAsWorker("typed-worker");

                Assert.That(All(), Is.EqualTo(all + 4));
                Assert.That(NonWorkers(), Is.EqualTo(nonWorkers + 2));
                plain.SetResult(true);
                worker.SetResult(true);
                typed.SetResult(3);
                typedWorker.SetResult(4);
                await registered;
                await registeredWorker;

                Assert.That(await registeredTyped, Is.EqualTo(3));
                Assert.That(await registeredTypedWorker, Is.EqualTo(4));
                Assert.That(All(), Is.EqualTo(all));
            });
        }

        /// <summary>Checks that UniTasks are registered and their results preserved.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UniTaskExtensions_RegisterForScene()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var nonWorkers = NonWorkers();
                var plain = new UniTaskCompletionSource();
                var worker = new UniTaskCompletionSource();
                var typed = new UniTaskCompletionSource<int>();
                var typedWorker = new UniTaskCompletionSource<int>();

                var registered = plain.Task.Register("plain");
                var registeredWorker = worker.Task.RegisterAsWorker("worker");
                var registeredTyped = typed.Task.Register("typed");
                var registeredTypedWorker = typedWorker.Task.RegisterAsWorker("typed-worker");

                Assert.That(NonWorkers(), Is.EqualTo(nonWorkers + 2));
                plain.TrySetResult();
                worker.TrySetResult();
                typed.TrySetResult(5);
                typedWorker.TrySetResult(6);
                await registered;
                await registeredWorker;

                Assert.That(await registeredTyped, Is.EqualTo(5));
                Assert.That(await registeredTypedWorker, Is.EqualTo(6));
                Assert.That(NonWorkers(), Is.EqualTo(nonWorkers));
            });
        }
    }
}
