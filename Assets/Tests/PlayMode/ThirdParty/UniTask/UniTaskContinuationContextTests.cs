using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.ThirdParty.Cysharp.PlayMode
{
    /// <summary>
    /// Covers where the code after an <c>await</c> of a UniTask resumes for each <see cref="UniTaskContinuationContext"/>, for the
    /// tasks of async methods and of the completion sources. Runs in Play Mode, where the player loop runs the main-thread continuations.
    /// </summary>
    public class UniTaskContinuationContextTests
    {
        /// <summary>The longest time a test waits for a continuation before it fails instead of hanging.</summary>
        private static readonly TimeSpan ContinuationTimeout = TimeSpan.FromSeconds(10);

        /// <summary>The kinds of UniTask sources the continuation contexts are checked with.</summary>
        public enum SourceKind
        {
            /// <summary>The task of a <see cref="UniTaskCompletionSource"/>.</summary>
            CompletionSource,
            /// <summary>The task of a <see cref="UniTaskCompletionSource{T}"/>.</summary>
            GenericCompletionSource,
            /// <summary>The task of an <see cref="AutoResetUniTaskCompletionSource"/>.</summary>
            AutoResetCompletionSource,
            /// <summary>The task of an <see cref="AutoResetUniTaskCompletionSource{T}"/>.</summary>
            GenericAutoResetCompletionSource,
            /// <summary>The task returned by an <c>async UniTask</c> method.</summary>
            AsyncMethod,
            /// <summary>The task returned by an <c>async UniTask&lt;T&gt;</c> method.</summary>
            GenericAsyncMethod,
        }

        /// <summary>The threads a test awaits or completes a task on.</summary>
        public enum ThreadKind
        {
            /// <summary>The Unity main thread.</summary>
            MainThread,
            /// <summary>A thread pool thread.</summary>
            ThreadPool,
        }

        /// <summary>The result the generic sources complete with.</summary>
        private const int GenericResult = 42;

        /// <summary>
        /// Checks every combination of source, awaiting thread, completing thread and continuation context: the code after the await
        /// resumes on the main thread for <see cref="UniTaskContinuationContext.MainThread"/>, on the thread pool for
        /// <see cref="UniTaskContinuationContext.ThreadPool"/> and on the kind of thread that awaited for
        /// <see cref="UniTaskContinuationContext.CapturedContext"/>, and it runs inline when the task completes on that kind of thread.
        /// </summary>
        /// <param name="kind">The source of the awaited task.</param>
        /// <param name="awaitOn">The thread that awaits the task.</param>
        /// <param name="completeOn">The thread that completes the task.</param>
        /// <param name="context">The continuation context of the awaited task.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Await_ResumesInContinuationContext(
            [Values] SourceKind kind,
            [Values] ThreadKind awaitOn,
            [Values] ThreadKind completeOn,
            [Values] UniTaskContinuationContext context) => UniTask.ToCoroutine(async () =>
        {
            var operation = PendingOperation.Create(kind, completeOn);
            var pending = default(UniTask<ResumePoint>);
            if (awaitOn == ThreadKind.MainThread)
            {
                pending = operation.StartAwaiting(context);
            }
            else
            {
                // A statement lambda binds to RunOnThreadPool(Action); a lambda returning the task would be awaited on the pool.
                await UniTask.RunOnThreadPool(() =>
                {
                    pending = operation.StartAwaiting(context);
                });
            }

            Assert.That(pending.Status, Is.EqualTo(UniTaskStatus.Pending), "The await must still be pending before the source completes.");

            var completerThreadId = 0;
            var completedSynchronously = false;
            if (completeOn == ThreadKind.MainThread)
            {
                completerThreadId = Thread.CurrentThread.ManagedThreadId;
                operation.Complete();
                completedSynchronously = pending.Status.IsCompleted();
            }
            else
            {
                await UniTask.RunOnThreadPool(() =>
                {
                    completerThreadId = Thread.CurrentThread.ManagedThreadId;
                    operation.Complete();
                    completedSynchronously = pending.Status.IsCompleted();
                });
            }

            var point = await pending.Timeout(ContinuationTimeout, DelayType.Realtime);

            var expected = ExpectedResumeThread(context, awaitOn);
            if (expected == ThreadKind.MainThread)
            {
                Assert.That(point.IsMainThread, Is.True, "The continuation must resume on the main thread.");
            }
            else
            {
                Assert.That(point.IsMainThread, Is.False, "The continuation must not resume on the main thread.");
                Assert.That(point.IsThreadPoolThread, Is.True, "The continuation must resume on a thread pool thread.");
            }

            if (expected == completeOn)
            {
                Assert.That(completedSynchronously, Is.True, "The continuation must run inline when the task completes in its context.");
                Assert.That(point.ThreadId, Is.EqualTo(completerThreadId), "The inline continuation must run on the completing thread.");
            }

            if (operation.IsGeneric)
            {
                Assert.That(point.Result, Is.EqualTo(GenericResult));
            }
        });

        /// <summary>
        /// Checks that the tasks of async methods and of all completion sources, and the default task, capture the context unless
        /// configured otherwise.
        /// </summary>
        [Test]
        public void Tasks_DefaultToCapturedContext()
        {
            var completionSource = new UniTaskCompletionSource();
            var genericCompletionSource = new UniTaskCompletionSource<int>();
            var autoResetCompletionSource = AutoResetUniTaskCompletionSource.Create();
            var genericAutoResetCompletionSource = AutoResetUniTaskCompletionSource<int>.Create();
            var gate = new UniTaskCompletionSource();
            var asyncMethodTask = WaitForGateAsync(gate, UniTaskContinuationContext.CapturedContext);
            var genericAsyncMethodTask = WaitForGateAsync(gate, UniTaskContinuationContext.CapturedContext, GenericResult);

            Assert.That(default(UniTask).ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(default(UniTask<int>).ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(UniTask.FromResult(1).ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(completionSource.Task.ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(genericCompletionSource.Task.ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(autoResetCompletionSource.Task.ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(genericAutoResetCompletionSource.Task.ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(asyncMethodTask.ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));
            Assert.That(genericAsyncMethodTask.ContinuationContext, Is.EqualTo(UniTaskContinuationContext.CapturedContext));

            completionSource.TrySetResult();
            genericCompletionSource.TrySetResult(1);
            autoResetCompletionSource.TrySetResult();
            genericAutoResetCompletionSource.TrySetResult(1);
            gate.TrySetResult();
            asyncMethodTask.GetAwaiter().GetResult();
            genericAsyncMethodTask.GetAwaiter().GetResult();
            autoResetCompletionSource.Task.GetAwaiter().GetResult();
            genericAutoResetCompletionSource.Task.GetAwaiter().GetResult();
        }

        /// <summary>Checks that configuring a task keeps its source, token and result and only changes the continuation context.</summary>
        /// <param name="context">The configured continuation context.</param>
        [Test]
        public void ConfigureAwait_ChangesOnlyTheContinuationContext([Values] UniTaskContinuationContext context)
        {
            var source = new UniTaskCompletionSource();
            var genericSource = new UniTaskCompletionSource<int>();

            var configured = source.Task.ConfigureAwait(context);
            var genericConfigured = genericSource.Task.ConfigureAwait(context);
            var configuredResult = UniTask.FromResult(GenericResult).ConfigureAwait(context);

            Assert.That(configured.ContinuationContext, Is.EqualTo(context));
            Assert.That(genericConfigured.ContinuationContext, Is.EqualTo(context));
            Assert.That(configuredResult.ContinuationContext, Is.EqualTo(context));
            Assert.That(configured.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(genericConfigured.Status, Is.EqualTo(UniTaskStatus.Pending));

            source.TrySetResult();
            genericSource.TrySetResult(GenericResult);

            Assert.That(configured.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(genericConfigured.GetAwaiter().GetResult(), Is.EqualTo(GenericResult));
            Assert.That(configuredResult.GetAwaiter().GetResult(), Is.EqualTo(GenericResult));
            Assert.That(new UniTask(source, 0, context).ContinuationContext, Is.EqualTo(context));
            Assert.That(new UniTask<int>(genericSource, 0, context).ContinuationContext, Is.EqualTo(context));
        }

        /// <summary>Checks that a value outside the enumeration is rejected instead of silently falling back to a default.</summary>
        [Test]
        public void ConfigureAwait_UndefinedValue_Throws()
        {
            var undefined = (UniTaskContinuationContext)3;
            var source = new UniTaskCompletionSource<int>();

            Assert.Throws<ArgumentOutOfRangeException>(() => UniTask.CompletedTask.ConfigureAwait(undefined));
            Assert.Throws<ArgumentOutOfRangeException>(() => UniTask.FromResult(1).ConfigureAwait(undefined));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniTask(source, 0, undefined));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UniTask<int>(source, 0, undefined));
        }

        /// <summary>Checks that the conversions that wrap the same pending operation keep the configured continuation context.</summary>
        /// <param name="context">The configured continuation context.</param>
        [Test]
        public void Conversions_OfPendingTask_KeepContinuationContext(
            [Values(UniTaskContinuationContext.MainThread, UniTaskContinuationContext.ThreadPool)] UniTaskContinuationContext context)
        {
            var source = new UniTaskCompletionSource<int>();
            var task = source.Task.ConfigureAwait(context);
            UniTask converted = task;
            var nonGeneric = new UniTaskCompletionSource().Task.ConfigureAwait(context);

            Assert.That(converted.ContinuationContext, Is.EqualTo(context));
            Assert.That(task.AsUniTask().ContinuationContext, Is.EqualTo(context));
            Assert.That(task.Preserve().ContinuationContext, Is.EqualTo(context));
            Assert.That(task.SuppressCancellationThrow().ContinuationContext, Is.EqualTo(context));
            Assert.That(nonGeneric.Preserve().ContinuationContext, Is.EqualTo(context));
            Assert.That(nonGeneric.SuppressCancellationThrow().ContinuationContext, Is.EqualTo(context));
            Assert.That(nonGeneric.AsAsyncUnitUniTask().ContinuationContext, Is.EqualTo(context));

            source.TrySetResult(1);
        }

        /// <summary>Checks that the conversions of completed tasks keep the configured continuation context as well.</summary>
        /// <param name="context">The configured continuation context.</param>
        [Test]
        public void Conversions_OfCompletedTask_KeepContinuationContext(
            [Values(UniTaskContinuationContext.MainThread, UniTaskContinuationContext.ThreadPool)] UniTaskContinuationContext context)
        {
            var completed = UniTask.CompletedTask.ConfigureAwait(context);
            var completedResult = UniTask.FromResult(GenericResult).ConfigureAwait(context);

            Assert.That(completed.Preserve().ContinuationContext, Is.EqualTo(context));
            Assert.That(completed.SuppressCancellationThrow().ContinuationContext, Is.EqualTo(context));
            Assert.That(completed.AsAsyncUnitUniTask().ContinuationContext, Is.EqualTo(context));
            Assert.That(completedResult.AsUniTask().ContinuationContext, Is.EqualTo(context));
            Assert.That(completedResult.Preserve().ContinuationContext, Is.EqualTo(context));
            Assert.That(completedResult.SuppressCancellationThrow().ContinuationContext, Is.EqualTo(context));
            Assert.That(completedResult.SuppressCancellationThrow().GetAwaiter().GetResult(), Is.EqualTo((false, GenericResult)));
        }

        /// <summary>
        /// Checks that a captured custom synchronization context receives the continuation through <see cref="SynchronizationContext.Post"/>
        /// when the task completes on another thread.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CapturedContext_CustomContext_PostsContinuationFromOtherThread() => UniTask.ToCoroutine(async () =>
        {
            var context = new RecordingSynchronizationContext();
            var source = new UniTaskCompletionSource();
            UniTask<ResumePoint> pending;
            using (new SynchronizationContextScope(context))
            {
                pending = AwaitAndCaptureAsync(source.Task);
            }

            await UniTask.RunOnThreadPool(() =>
            {
                source.TrySetResult();
            });
            var point = await pending.Timeout(ContinuationTimeout, DelayType.Realtime);

            Assert.That(context.PostCount, Is.EqualTo(1));
            Assert.That(point.Context, Is.SameAs(context));
            Assert.That(point.IsMainThread, Is.True);
        });

        /// <summary>Checks that the continuation runs inline when the task completes while the captured context is current.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CapturedContext_CustomContext_RunsInlineInSameContext() => UniTask.ToCoroutine(async () =>
        {
            var context = new RecordingSynchronizationContext();
            var source = new UniTaskCompletionSource();
            UniTask<ResumePoint> pending;
            bool completedSynchronously;
            using (new SynchronizationContextScope(context))
            {
                pending = AwaitAndCaptureAsync(source.Task);
                source.TrySetResult();
                completedSynchronously = pending.Status.IsCompleted();
            }

            var point = await pending;

            Assert.That(completedSynchronously, Is.True);
            Assert.That(context.PostCount, Is.EqualTo(0));
            Assert.That(point.Context, Is.SameAs(context));
        });

        /// <summary>
        /// Checks that an await on the main thread under <see cref="UniTaskSynchronizationContext"/>, which player builds install as the
        /// main thread's context, resumes on the main thread when the task completes on the thread pool.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CapturedContext_UniTaskSynchronizationContext_ResumesOnMainThread() => UniTask.ToCoroutine(async () =>
        {
            var source = new UniTaskCompletionSource<int>();
            UniTask<ResumePoint> pending;
            using (new SynchronizationContextScope(new UniTaskSynchronizationContext()))
            {
                pending = AwaitAndCaptureAsync(source.Task);
            }

            await UniTask.RunOnThreadPool(() =>
            {
                source.TrySetResult(GenericResult);
            });
            var point = await pending.Timeout(ContinuationTimeout, DelayType.Realtime);

            Assert.That(point.IsMainThread, Is.True);
            Assert.That(point.Result, Is.EqualTo(GenericResult));
        });

        /// <summary>Checks that an already completed task continues synchronously on the awaiting thread whatever its context.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CompletedTask_ContinuesOnAwaitingThread() => UniTask.ToCoroutine(async () =>
        {
            await UniTask.SwitchToThreadPool();
            await UniTask.CompletedTask.ConfigureAwait(UniTaskContinuationContext.MainThread);
            var poolPoint = ResumePoint.Capture(null);

            await UniTask.SwitchToMainThread();
            await UniTask.FromResult(1).ConfigureAwait(UniTaskContinuationContext.ThreadPool);
            var mainPoint = ResumePoint.Capture(null);

            Assert.That(poolPoint.IsThreadPoolThread, Is.True);
            Assert.That(mainPoint.IsMainThread, Is.True);
        });

        /// <summary>Checks that a fault or cancellation set on the thread pool is rethrown after resuming on the main thread.</summary>
        /// <param name="canceled">Whether the task is canceled instead of faulted.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CapturedContext_FailureFromThreadPool_ThrowsOnMainThread([Values] bool canceled) => UniTask.ToCoroutine(async () =>
        {
            var source = new UniTaskCompletionSource();
            var pending = AwaitAndCaptureFailureAsync(source.Task);

            await UniTask.RunOnThreadPool(() =>
            {
                if (canceled)
                {
                    source.TrySetCanceled();
                }
                else
                {
                    source.TrySetException(new InvalidOperationException("expected"));
                }
            });
            var (point, error) = await pending.Timeout(ContinuationTimeout, DelayType.Realtime);

            Assert.That(point.IsMainThread, Is.True);
            if (canceled)
            {
                Assert.That(error, Is.InstanceOf<OperationCanceledException>());
            }
            else
            {
                Assert.That(error, Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("expected"));
            }
        });

        /// <summary>
        /// Checks that pooled sources stay valid when the continuation is deferred to another thread: the awaiter's <c>GetResult</c>
        /// runs later than the completion, and only then returns the source and the async method runner to their pools, so every
        /// iteration reads its own result and later iterations reuse the returned source.
        /// </summary>
        /// <param name="awaitOn">The thread that awaits; the source is completed on the other one.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PooledSources_DeferredContinuation_ReturnsAfterGetResult([Values] ThreadKind awaitOn) => UniTask.ToCoroutine(async () =>
        {
            const int Iterations = 50;
            var reused = false;
            var previous = default(AutoResetUniTaskCompletionSource<int>);
            for (var i = 0; i < Iterations; i++)
            {
                var source = AutoResetUniTaskCompletionSource<int>.Create();
                reused |= ReferenceEquals(source, previous);
                previous = source;
                var value = i;
                var pending = default(UniTask<ResumePoint>);
                if (awaitOn == ThreadKind.MainThread)
                {
                    pending = AwaitAndCaptureAsync(source.Task);
                    await UniTask.RunOnThreadPool(() =>
                    {
                        source.TrySetResult(value);
                    });
                }
                else
                {
                    await UniTask.RunOnThreadPool(() =>
                    {
                        pending = AwaitAndCaptureAsync(source.Task);
                    });
                    source.TrySetResult(value);
                }

                var point = await pending.Timeout(ContinuationTimeout, DelayType.Realtime);

                Assert.That(point.Result, Is.EqualTo(value));
                Assert.That(point.IsMainThread, Is.EqualTo(awaitOn == ThreadKind.MainThread));
            }

            Assert.That(reused, Is.True, "Sources returned by deferred GetResult calls must be reused by later iterations.");
        });

        /// <summary>Checks that callbacks registered with <c>SourceOnCompleted</c> ignore the context and run on the completing thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SourceOnCompleted_RunsOnCompletingThread() => UniTask.ToCoroutine(async () =>
        {
            var source = new UniTaskCompletionSource();
            var callbackThreadId = 0;
            source.Task.ConfigureAwait(UniTaskContinuationContext.MainThread).GetAwaiter().SourceOnCompleted(
                _ => callbackThreadId = Thread.CurrentThread.ManagedThreadId,
                null);

            var completerThreadId = 0;
            await UniTask.RunOnThreadPool(() =>
            {
                completerThreadId = Thread.CurrentThread.ManagedThreadId;
                source.TrySetResult();
            });

            Assert.That(callbackThreadId, Is.EqualTo(completerThreadId));
        });

        /// <summary>Gets the kind of thread an await resumes on.</summary>
        /// <param name="context">The continuation context of the awaited task.</param>
        /// <param name="awaitOn">The thread that awaited the task.</param>
        /// <returns>The kind of thread the continuation has to run on.</returns>
        private static ThreadKind ExpectedResumeThread(UniTaskContinuationContext context, ThreadKind awaitOn)
        {
            switch (context)
            {
                case UniTaskContinuationContext.MainThread:
                    return ThreadKind.MainThread;
                case UniTaskContinuationContext.ThreadPool:
                    return ThreadKind.ThreadPool;
                default:
                    return awaitOn;
            }
        }

        /// <summary>Gets the continuation context that resumes on a kind of thread.</summary>
        /// <param name="thread">The kind of thread.</param>
        /// <returns><see cref="UniTaskContinuationContext.MainThread"/> or <see cref="UniTaskContinuationContext.ThreadPool"/>.</returns>
        private static UniTaskContinuationContext ContextOf(ThreadKind thread)
        {
            return thread == ThreadKind.MainThread ? UniTaskContinuationContext.MainThread : UniTaskContinuationContext.ThreadPool;
        }

        /// <summary>Awaits a task and records where the code after the await runs.</summary>
        /// <param name="task">The task to await.</param>
        /// <returns>The point the await resumed at.</returns>
        private static async UniTask<ResumePoint> AwaitAndCaptureAsync(UniTask task)
        {
            await task;
            return ResumePoint.Capture(null);
        }

        /// <summary>Awaits a task with a result and records where the code after the await runs.</summary>
        /// <param name="task">The task to await.</param>
        /// <returns>The point the await resumed at, with the task's result.</returns>
        private static async UniTask<ResumePoint> AwaitAndCaptureAsync(UniTask<int> task)
        {
            var result = await task;
            return ResumePoint.Capture(result);
        }

        /// <summary>Awaits a task that is expected to fail and records where the exception is observed.</summary>
        /// <param name="task">The task to await.</param>
        /// <returns>The point the await resumed at and the exception it threw, or <c>null</c> when it did not throw.</returns>
        private static async UniTask<(ResumePoint Point, Exception? Error)> AwaitAndCaptureFailureAsync(UniTask task)
        {
            try
            {
                await task;
                return (ResumePoint.Capture(null), null);
            }
            catch (Exception exception)
            {
                return (ResumePoint.Capture(null), exception);
            }
        }

        /// <summary>An async method that completes after a gate opens.</summary>
        /// <param name="gate">The source whose completion lets the method complete.</param>
        /// <param name="context">The continuation context of the method's await of <paramref name="gate"/>.</param>
        /// <returns>The task of the async method.</returns>
        private static async UniTask WaitForGateAsync(UniTaskCompletionSource gate, UniTaskContinuationContext context)
        {
            await gate.Task.ConfigureAwait(context);
        }

        /// <summary>An async method that returns a result after a gate opens.</summary>
        /// <param name="gate">The source whose completion lets the method complete.</param>
        /// <param name="context">The continuation context of the method's await of <paramref name="gate"/>.</param>
        /// <param name="result">The value the method returns.</param>
        /// <returns>The task of the async method.</returns>
        private static async UniTask<int> WaitForGateAsync(UniTaskCompletionSource gate, UniTaskContinuationContext context, int result)
        {
            await gate.Task.ConfigureAwait(context);
            return result;
        }

        /// <summary>Where the code after an await ran.</summary>
        private readonly struct ResumePoint
        {
            /// <summary>Gets the managed ID of the thread.</summary>
            public int ThreadId { get; }

            /// <summary>Gets whether the thread is the Unity main thread.</summary>
            public bool IsMainThread { get; }

            /// <summary>Gets whether the thread is a thread pool thread.</summary>
            public bool IsThreadPoolThread { get; }

            /// <summary>Gets the synchronization context that was current.</summary>
            public SynchronizationContext? Context { get; }

            /// <summary>Gets the result of the awaited task, or <c>null</c> for a task without result.</summary>
            public int? Result { get; }

            /// <summary>Creates a resume point.</summary>
            /// <param name="threadId">The managed ID of the thread.</param>
            /// <param name="isMainThread">Whether the thread is the Unity main thread.</param>
            /// <param name="isThreadPoolThread">Whether the thread is a thread pool thread.</param>
            /// <param name="context">The synchronization context that was current.</param>
            /// <param name="result">The result of the awaited task.</param>
            private ResumePoint(int threadId, bool isMainThread, bool isThreadPoolThread, SynchronizationContext? context, int? result)
            {
                ThreadId = threadId;
                IsMainThread = isMainThread;
                IsThreadPoolThread = isThreadPoolThread;
                Context = context;
                Result = result;
            }

            /// <summary>Records the current thread and synchronization context.</summary>
            /// <param name="result">The result of the awaited task, or <c>null</c> for a task without result.</param>
            /// <returns>The resume point of the calling code.</returns>
            public static ResumePoint Capture(int? result)
            {
                var thread = Thread.CurrentThread;
                return new ResumePoint(thread.ManagedThreadId, PlayerLoopHelper.IsMainThread, thread.IsThreadPoolThread, SynchronizationContext.Current, result);
            }
        }

        /// <summary>A pending task of one <see cref="SourceKind"/> and the action that completes it.</summary>
        private sealed class PendingOperation
        {
            /// <summary>Configures the pending task and awaits it.</summary>
            private readonly Func<UniTaskContinuationContext, UniTask<ResumePoint>> _startAwaiting;

            /// <summary>Completes the pending task.</summary>
            private readonly Action _complete;

            /// <summary>Creates an operation.</summary>
            /// <param name="startAwaiting">Configures the pending task with the given context and awaits it.</param>
            /// <param name="complete">Completes the pending task on the calling thread.</param>
            /// <param name="isGeneric">Whether the task has a result.</param>
            private PendingOperation(Func<UniTaskContinuationContext, UniTask<ResumePoint>> startAwaiting, Action complete, bool isGeneric)
            {
                _startAwaiting = startAwaiting;
                _complete = complete;
                IsGeneric = isGeneric;
            }

            /// <summary>Gets whether the task completes with <see cref="GenericResult"/>.</summary>
            public bool IsGeneric { get; }

            /// <summary>
            /// Creates a pending task of a kind. <see cref="Complete"/> completes the task synchronously on the calling thread, which
            /// for async methods has to be <paramref name="completeOn"/> because their await of the gate resumes there.
            /// </summary>
            /// <param name="kind">The source of the task.</param>
            /// <param name="completeOn">The thread <see cref="Complete"/> will be called on.</param>
            /// <returns>The pending operation.</returns>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
            public static PendingOperation Create(SourceKind kind, ThreadKind completeOn)
            {
                switch (kind)
                {
                    case SourceKind.CompletionSource:
                    {
                        var source = new UniTaskCompletionSource();
                        return new PendingOperation(x => AwaitAndCaptureAsync(source.Task.ConfigureAwait(x)), () => source.TrySetResult(), false);
                    }
                    case SourceKind.GenericCompletionSource:
                    {
                        var source = new UniTaskCompletionSource<int>();
                        return new PendingOperation(x => AwaitAndCaptureAsync(source.Task.ConfigureAwait(x)), () => source.TrySetResult(GenericResult), true);
                    }
                    case SourceKind.AutoResetCompletionSource:
                    {
                        var source = AutoResetUniTaskCompletionSource.Create();
                        return new PendingOperation(x => AwaitAndCaptureAsync(source.Task.ConfigureAwait(x)), () => source.TrySetResult(), false);
                    }
                    case SourceKind.GenericAutoResetCompletionSource:
                    {
                        var source = AutoResetUniTaskCompletionSource<int>.Create();
                        return new PendingOperation(x => AwaitAndCaptureAsync(source.Task.ConfigureAwait(x)), () => source.TrySetResult(GenericResult), true);
                    }
                    case SourceKind.AsyncMethod:
                    {
                        var gate = new UniTaskCompletionSource();
                        var task = WaitForGateAsync(gate, ContextOf(completeOn));
                        return new PendingOperation(x => AwaitAndCaptureAsync(task.ConfigureAwait(x)), () => gate.TrySetResult(), false);
                    }
                    case SourceKind.GenericAsyncMethod:
                    {
                        var gate = new UniTaskCompletionSource();
                        var task = WaitForGateAsync(gate, ContextOf(completeOn), GenericResult);
                        return new PendingOperation(x => AwaitAndCaptureAsync(task.ConfigureAwait(x)), () => gate.TrySetResult(), true);
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
                }
            }

            /// <summary>Configures the pending task with a continuation context and awaits it on the calling thread.</summary>
            /// <param name="context">The continuation context of the await.</param>
            /// <returns>The task of the await, which completes with the point the await resumed at.</returns>
            public UniTask<ResumePoint> StartAwaiting(UniTaskContinuationContext context)
            {
                return _startAwaiting(context);
            }

            /// <summary>Completes the pending task on the calling thread.</summary>
            public void Complete()
            {
                _complete();
            }
        }

        /// <summary>
        /// A synchronization context that counts posted callbacks and runs them in the next player loop update with itself as the
        /// current context.
        /// </summary>
        private sealed class RecordingSynchronizationContext : SynchronizationContext
        {
            /// <summary>The number of callbacks posted so far.</summary>
            private int _postCount;

            /// <summary>Gets the number of callbacks posted so far.</summary>
            public int PostCount => Volatile.Read(ref _postCount);

            /// <summary>Counts the callback and queues it to the main thread.</summary>
            /// <param name="d">The callback.</param>
            /// <param name="state">The state passed to <paramref name="d"/>.</param>
            public override void Post(SendOrPostCallback d, object? state)
            {
                Interlocked.Increment(ref _postCount);
                UniTask.Post(() => Run(d, state));
            }

            /// <summary>Runs a callback with this context as the current context.</summary>
            /// <param name="d">The callback.</param>
            /// <param name="state">The state passed to <paramref name="d"/>.</param>
            private void Run(SendOrPostCallback d, object? state)
            {
                using (new SynchronizationContextScope(this))
                {
                    d(state);
                }
            }
        }

        /// <summary>Makes a synchronization context current and restores the previous one when disposed.</summary>
        private sealed class SynchronizationContextScope : IDisposable
        {
            /// <summary>The context that was current before the scope.</summary>
            private readonly SynchronizationContext? _previous;

            /// <summary>Makes a context current.</summary>
            /// <param name="context">The context to make current.</param>
            public SynchronizationContextScope(SynchronizationContext context)
            {
                _previous = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(context);
            }

            /// <summary>Restores the context that was current before the scope.</summary>
            public void Dispose()
            {
                SynchronizationContext.SetSynchronizationContext(_previous);
            }
        }
    }
}
