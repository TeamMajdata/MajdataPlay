using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.ThirdParty.Cysharp.PlayMode
{
    /// <summary>
    /// Covers the Task-like state properties <c>IsCompleted</c>, <c>IsCompletedSuccessfully</c>, <c>IsFaulted</c> and
    /// <c>IsCanceled</c> of <see cref="UniTask"/> and <see cref="UniTask{T}"/> for completion sources, async methods and the
    /// factory methods. Every faulted task is observed afterwards, so no unobserved exception is reported to later tests.
    /// </summary>
    public class UniTaskStatePropertiesTests
    {
        /// <summary>The sources whose tasks are checked.</summary>
        public enum SourceKind
        {
            /// <summary>A <see cref="UniTaskCompletionSource"/> or <see cref="UniTaskCompletionSource{T}"/>.</summary>
            CompletionSource,
            /// <summary>An <see cref="AutoResetUniTaskCompletionSource"/> or <see cref="AutoResetUniTaskCompletionSource{T}"/>.</summary>
            AutoResetCompletionSource,
            /// <summary>An <c>async UniTask</c> or <c>async UniTask&lt;T&gt;</c> method awaiting a gate.</summary>
            AsyncMethod,
        }

        /// <summary>The result the generic tasks complete with.</summary>
        private const int Result = 7;

        /// <summary>Checks that the state properties of a <see cref="UniTask"/> agree with each state its source can be in.</summary>
        /// <param name="kind">The source of the task.</param>
        /// <param name="state">The state the task is put into.</param>
        [Test]
        public void Task_StatePropertiesMatchStatus([Values] SourceKind kind, [Values] UniTaskStatus state)
        {
            var task = CreateTask(kind, state);
            try
            {
                AssertState(state, task.Status, task.IsCompleted, task.IsCompletedSuccessfully, task.IsFaulted, task.IsCanceled);
            }
            finally
            {
                Observe(task, state);
            }
        }

        /// <summary>Checks that the state properties of a <see cref="UniTask{T}"/> agree with each state its source can be in.</summary>
        /// <param name="kind">The source of the task.</param>
        /// <param name="state">The state the task is put into.</param>
        [Test]
        public void GenericTask_StatePropertiesMatchStatus([Values] SourceKind kind, [Values] UniTaskStatus state)
        {
            var task = CreateGenericTask(kind, state);
            try
            {
                AssertState(state, task.Status, task.IsCompleted, task.IsCompletedSuccessfully, task.IsFaulted, task.IsCanceled);
            }
            finally
            {
                Observe(task.AsUniTask(), state);
            }
        }

        /// <summary>Checks the state properties of the tasks the factory methods create in a completed state.</summary>
        [Test]
        public void FactoryTasks_ReportTheirState()
        {
            var faulted = UniTask.FromException(new InvalidOperationException("expected"));
            var genericFaulted = UniTask.FromException<int>(new InvalidOperationException("expected"));
            try
            {
                AssertSucceeded(default(UniTask));
                AssertSucceeded(UniTask.CompletedTask);
                AssertSucceeded(UniTask.FromResult(Result).AsUniTask());
                AssertSucceeded(new UniTask<int>(Result).AsUniTask());
                Assert.That(default(UniTask<int>).IsCompletedSuccessfully, Is.True);
                Assert.That(UniTask.FromResult(Result).IsCompletedSuccessfully, Is.True);
                AssertState(UniTaskStatus.Faulted, faulted.Status, faulted.IsCompleted, faulted.IsCompletedSuccessfully, faulted.IsFaulted, faulted.IsCanceled);
                AssertState(UniTaskStatus.Faulted, genericFaulted.Status, genericFaulted.IsCompleted, genericFaulted.IsCompletedSuccessfully, genericFaulted.IsFaulted, genericFaulted.IsCanceled);

                var canceled = UniTask.FromCanceled();
                var genericCanceled = UniTask.FromCanceled<int>();
                AssertState(UniTaskStatus.Canceled, canceled.Status, canceled.IsCompleted, canceled.IsCompletedSuccessfully, canceled.IsFaulted, canceled.IsCanceled);
                AssertState(UniTaskStatus.Canceled, genericCanceled.Status, genericCanceled.IsCompleted, genericCanceled.IsCompletedSuccessfully, genericCanceled.IsFaulted, genericCanceled.IsCanceled);
            }
            finally
            {
                Observe(faulted, UniTaskStatus.Faulted);
                Observe(genericFaulted.AsUniTask(), UniTaskStatus.Faulted);
            }
        }

        /// <summary>Checks that a task configured with another continuation context reports the state of the same source.</summary>
        [Test]
        public void ConfiguredTask_ReportsStateOfSameSource()
        {
            var source = new UniTaskCompletionSource<int>();
            var configured = source.Task.ConfigureAwait(UniTaskContinuationContext.ThreadPool);

            Assert.That(configured.IsCompleted, Is.False);

            source.TrySetResult(Result);

            Assert.That(configured.IsCompleted, Is.True);
            Assert.That(configured.IsCompletedSuccessfully, Is.True);
            Assert.That(configured.AsUniTask().IsCompletedSuccessfully, Is.True);
        }

        /// <summary>
        /// Checks that the properties validate the token like <see cref="UniTask.Status"/>: once a pooled source is returned by the
        /// await, reading them throws instead of reporting another operation's state, while a non-pooled source stays readable.
        /// </summary>
        [Test]
        public void StateProperties_AfterAwait_ValidateTokenLikeStatus()
        {
            var pooled = AutoResetUniTaskCompletionSource.Create();
            var pooledTask = pooled.Task;
            pooled.TrySetResult();
            pooledTask.GetAwaiter().GetResult();

            var plain = new UniTaskCompletionSource();
            var plainTask = plain.Task;
            plain.TrySetResult();
            plainTask.GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => _ = pooledTask.Status);
            Assert.Throws<InvalidOperationException>(() => _ = pooledTask.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => _ = pooledTask.IsCompletedSuccessfully);
            Assert.Throws<InvalidOperationException>(() => _ = pooledTask.IsFaulted);
            Assert.Throws<InvalidOperationException>(() => _ = pooledTask.IsCanceled);
            Assert.That(plainTask.IsCompletedSuccessfully, Is.True);
        }

        /// <summary>Asserts the status and the state properties expected for a state.</summary>
        /// <param name="state">The expected state.</param>
        /// <param name="status">The task's <c>Status</c>.</param>
        /// <param name="isCompleted">The task's <c>IsCompleted</c>.</param>
        /// <param name="isCompletedSuccessfully">The task's <c>IsCompletedSuccessfully</c>.</param>
        /// <param name="isFaulted">The task's <c>IsFaulted</c>.</param>
        /// <param name="isCanceled">The task's <c>IsCanceled</c>.</param>
        private static void AssertState(UniTaskStatus state, UniTaskStatus status, bool isCompleted, bool isCompletedSuccessfully, bool isFaulted, bool isCanceled)
        {
            Assert.That(status, Is.EqualTo(state), nameof(UniTask.Status));
            Assert.That(isCompleted, Is.EqualTo(state != UniTaskStatus.Pending), nameof(UniTask.IsCompleted));
            Assert.That(isCompletedSuccessfully, Is.EqualTo(state == UniTaskStatus.Succeeded), nameof(UniTask.IsCompletedSuccessfully));
            Assert.That(isFaulted, Is.EqualTo(state == UniTaskStatus.Faulted), nameof(UniTask.IsFaulted));
            Assert.That(isCanceled, Is.EqualTo(state == UniTaskStatus.Canceled), nameof(UniTask.IsCanceled));
        }

        /// <summary>Asserts that a task reports success through its status and all state properties.</summary>
        /// <param name="task">The task.</param>
        private static void AssertSucceeded(UniTask task)
        {
            AssertState(UniTaskStatus.Succeeded, task.Status, task.IsCompleted, task.IsCompletedSuccessfully, task.IsFaulted, task.IsCanceled);
        }

        /// <summary>Creates a task of a kind in a state.</summary>
        /// <param name="kind">The source of the task.</param>
        /// <param name="state">The state the task is put into.</param>
        /// <returns>The task.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
        private static UniTask CreateTask(SourceKind kind, UniTaskStatus state)
        {
            switch (kind)
            {
                case SourceKind.CompletionSource:
                {
                    var source = new UniTaskCompletionSource();
                    Apply(source, state);
                    return source.Task;
                }
                case SourceKind.AutoResetCompletionSource:
                {
                    var source = AutoResetUniTaskCompletionSource.Create();
                    Apply(source, state);
                    return source.Task;
                }
                case SourceKind.AsyncMethod:
                {
                    var gate = new UniTaskCompletionSource();
                    var task = WaitForGateAsync(gate);
                    Apply(gate, state);
                    return task;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>Creates a task with a result of a kind in a state.</summary>
        /// <param name="kind">The source of the task.</param>
        /// <param name="state">The state the task is put into.</param>
        /// <returns>The task.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
        private static UniTask<int> CreateGenericTask(SourceKind kind, UniTaskStatus state)
        {
            switch (kind)
            {
                case SourceKind.CompletionSource:
                {
                    var source = new UniTaskCompletionSource<int>();
                    Apply(source, state);
                    return source.Task;
                }
                case SourceKind.AutoResetCompletionSource:
                {
                    var source = AutoResetUniTaskCompletionSource<int>.Create();
                    Apply(source, state);
                    return source.Task;
                }
                case SourceKind.AsyncMethod:
                {
                    var gate = new UniTaskCompletionSource();
                    var task = WaitForGateAndReturnAsync(gate);
                    Apply(gate, state);
                    return task;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>Puts a promise into a state; <see cref="UniTaskStatus.Pending"/> leaves it pending.</summary>
        /// <param name="promise">The promise.</param>
        /// <param name="state">The state.</param>
        private static void Apply(IPromise promise, UniTaskStatus state)
        {
            switch (state)
            {
                case UniTaskStatus.Succeeded:
                    promise.TrySetResult();
                    break;
                case UniTaskStatus.Faulted:
                    promise.TrySetException(new InvalidOperationException("expected"));
                    break;
                case UniTaskStatus.Canceled:
                    promise.TrySetCanceled();
                    break;
            }
        }

        /// <summary>Puts a promise with a result into a state; <see cref="UniTaskStatus.Pending"/> leaves it pending.</summary>
        /// <param name="promise">The promise.</param>
        /// <param name="state">The state.</param>
        private static void Apply(IPromise<int> promise, UniTaskStatus state)
        {
            switch (state)
            {
                case UniTaskStatus.Succeeded:
                    promise.TrySetResult(Result);
                    break;
                case UniTaskStatus.Faulted:
                    promise.TrySetException(new InvalidOperationException("expected"));
                    break;
                case UniTaskStatus.Canceled:
                    promise.TrySetCanceled();
                    break;
            }
        }

        /// <summary>Reads the outcome of a completed task, so that its exception counts as observed and pooled sources are returned.</summary>
        /// <param name="task">The task.</param>
        /// <param name="state">The state the task was put into; pending tasks are left alone.</param>
        private static void Observe(UniTask task, UniTaskStatus state)
        {
            if (state == UniTaskStatus.Pending)
            {
                return;
            }

            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception) when (state != UniTaskStatus.Succeeded)
            {
                // The fault or cancellation was put there by the test.
            }
        }

        /// <summary>An async method that completes like the gate it awaits.</summary>
        /// <param name="gate">The source the method awaits.</param>
        /// <returns>The task of the async method.</returns>
        private static async UniTask WaitForGateAsync(UniTaskCompletionSource gate)
        {
            await gate.Task;
        }

        /// <summary>An async method that completes like the gate it awaits and returns <see cref="Result"/> on success.</summary>
        /// <param name="gate">The source the method awaits.</param>
        /// <returns>The task of the async method.</returns>
        private static async UniTask<int> WaitForGateAndReturnAsync(UniTaskCompletionSource gate)
        {
            await gate.Task;
            return Result;
        }
    }
}
