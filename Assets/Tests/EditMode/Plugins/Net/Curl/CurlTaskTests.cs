#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>Covers the transfer state machine and how it publishes, completes, fails and cancels its response.</summary>
    public class CurlTaskTests
    {
        /// <summary>Checks the initial state of a transfer that has not been submitted.</summary>
        [Test]
        public void NewTask_IsCreatedAndUnpublished()
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config());
            var task = fixture.Task;

            Assert.That(task.State, Is.EqualTo(CurlTaskState.Created));
            Assert.That(task.Task.IsCompleted, Is.False);
            Assert.That(task.IsResponsePublished, Is.False);
            Assert.That(task.IsAttached, Is.False);
            Assert.That(task.Request, Is.SameAs(fixture.Request));
            Assert.That(task.Response.RequestMessage, Is.SameAs(fixture.Message));
            Assert.That(task.Response.Request, Is.SameAs(fixture.Request));
            Assert.That(task.Config.MaxResponseHeadersLength, Is.EqualTo(1024));
            Assert.That(task.Easy.IsAllocated, Is.True);
        }

        /// <summary>Checks that the attachment flag is stored for the multi worker.</summary>
        [Test]
        public void IsAttached_IsWritable()
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config());

            fixture.Task.IsAttached = true;
            var attached = fixture.Task.IsAttached;
            fixture.Task.IsAttached = false;

            Assert.That(attached, Is.True);
        }

        /// <summary>Checks the forward-only transitions from creation to completion and the published result.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Transitions_PublishAndCompleteResponse() => UniTask.ToCoroutine(async () =>
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config());
            var task = fixture.Task;

            var headerBeforeSubmit = task.TryEnterHeaderReadState();
            var completedBeforeHeaders = task.TryEnterCompletedState(CurlCode.Ok);
            var submitted = task.TryEnterSubmittedState();
            var submittedAgain = task.TryEnterSubmittedState();
            var completedBeforeHeaderRead = task.TryEnterCompletedState(CurlCode.Ok);
            var headerRead = task.TryEnterHeaderReadState();
            var headerReadAgain = task.TryEnterHeaderReadState();
            var completed = task.TryEnterCompletedState(CurlCode.Ok);
            var response = await task.Task;
            var body = await response.Content.ReadAsByteArrayAsync();

            Assert.That(headerBeforeSubmit, Is.False);
            Assert.That(completedBeforeHeaders, Is.False);
            Assert.That(submitted, Is.True);
            Assert.That(submittedAgain, Is.False);
            Assert.That(completedBeforeHeaderRead, Is.False);
            Assert.That(headerRead, Is.True);
            Assert.That(headerReadAgain, Is.False);
            Assert.That(completed, Is.True);
            Assert.That(task.State, Is.EqualTo(CurlTaskState.Completed));
            Assert.That(task.IsResponsePublished, Is.True);
            Assert.That(response, Is.SameAs(task.Response));
            Assert.That(response.ResultCode, Is.EqualTo(CurlCode.Ok));
            Assert.That(body, Is.Empty);
            Assert.That(task.TryFail(new IOException()), Is.False);
            Assert.That(task.TryEnterCancelledState(), Is.False);
        });

        /// <summary>Checks that a failure before publication faults the task and the body stream.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TryFail_BeforePublicationFaultsTask() => UniTask.ToCoroutine(async () =>
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config()).Submit();
            var failure = new IOException("transfer failed");

            var failed = fixture.Task.TryFail(failure);
            var failedAgain = fixture.Task.TryFail(new IOException());
            var thrown = await NetAsyncAssert.ThrowsAsync<IOException>(() => fixture.Task.Task);

            Assert.That(failed, Is.True);
            Assert.That(failedAgain, Is.False);
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(fixture.Task.State, Is.EqualTo(CurlTaskState.Faulted));
            Assert.That(fixture.Task.TryEnterCancelledState(), Is.False);
        });

        /// <summary>Checks that a failure after publication keeps the published response but aborts its body.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TryFail_AfterPublicationAbortsBody() => UniTask.ToCoroutine(async () =>
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config()).Submit();
            fixture.Task.TryEnterHeaderReadState();
            var failure = new IOException("connection reset");

            fixture.Task.TryFail(failure);
            var response = await fixture.Task.Task;
            var stream = await response.Content.ReadAsStreamAsync();

            Assert.That(Assert.Throws<IOException>(() => stream.Read(new byte[1], 0, 1)), Is.SameAs(failure));
        });

        /// <summary>Checks that cancellation before publication cancels the task with the caller's token.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TryEnterCancelledState_CancelsUnpublishedTask() => UniTask.ToCoroutine(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config(), cancellation.Token);

            var cancelled = fixture.Task.TryEnterCancelledState();
            var cancelledAgain = fixture.Task.TryEnterCancelledState();
            var exception = await NetAsyncAssert.ThrowsAsync<OperationCanceledException>(() => fixture.Task.Task);

            Assert.That(cancelled, Is.True);
            Assert.That(cancelledAgain, Is.False);
            Assert.That(fixture.Task.State, Is.EqualTo(CurlTaskState.Cancelled));
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(fixture.Task.TryFail(new IOException()), Is.False);
        });

        /// <summary>Checks that cancellation after publication aborts the body with an operation cancelled failure.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TryEnterCancelledState_AbortsPublishedBody() => UniTask.ToCoroutine(async () =>
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config()).Submit();
            fixture.Task.TryEnterHeaderReadState();

            fixture.Task.TryEnterCancelledState();
            var response = await fixture.Task.Task;
            var stream = await response.Content.ReadAsStreamAsync();

            Assert.Throws<OperationCanceledException>(() => stream.Read(new byte[1], 0, 1));
        });

        /// <summary>Checks that a cancelled token reaches the owner synchronously on registration and only while registered.</summary>
        [Test]
        public void RegisterCancellation_ForwardsCancellationWhileRegistered()
        {
            using var preCancelled = new CancellationTokenSource();
            preCancelled.Cancel();
            using var live = new CancellationTokenSource();
            using var released = new CancellationTokenSource();
            using var cancelledFixture = new CurlTaskFixture(CurlTaskFixture.Config(), preCancelled.Token);
            using var liveFixture = new CurlTaskFixture(CurlTaskFixture.Config(), live.Token);
            using var releasedFixture = new CurlTaskFixture(CurlTaskFixture.Config(), released.Token);
            using var noTokenFixture = new CurlTaskFixture(CurlTaskFixture.Config());

            cancelledFixture.Task.RegisterCancellation();
            liveFixture.Task.RegisterCancellation();
            releasedFixture.Task.RegisterCancellation();
            noTokenFixture.Task.RegisterCancellation();
            releasedFixture.Task.ReleaseCancellationRegistration();
            live.Cancel();
            released.Cancel();

            Assert.That(cancelledFixture.CancelCount, Is.EqualTo(1));
            Assert.That(liveFixture.CancelCount, Is.EqualTo(1));
            Assert.That(releasedFixture.CancelCount, Is.Zero);
            Assert.That(noTokenFixture.CancelCount, Is.Zero);
            Assert.That(liveFixture.Task.CancellationToken, Is.EqualTo(live.Token));
        }

        /// <summary>Checks that disposing the response and its body notifies the owner exactly once.</summary>
        [Test]
        public void ResponseDisposal_NotifiesOwnerOnce()
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config()).Submit();
            var response = fixture.Task.Response;

            response.Dispose();
            response.Dispose();

            Assert.That(fixture.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>Checks that releasing native resources disposes the easy handle once.</summary>
        [Test]
        public void ReleaseNativeResources_IsIdempotent()
        {
            var fixture = new CurlTaskFixture(CurlTaskFixture.Config());

            fixture.Task.ReleaseNativeResources();
            fixture.Task.ReleaseNativeResources();

            Assert.That(fixture.Task.Easy.IsAllocated, Is.False);
            fixture.Dispose();
        }

        /// <summary>Checks that the first body chunk publishes the response and that the bytes reach the reader.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WriteCallback_PublishesResponseAndBuffersBody() => UniTask.ToCoroutine(async () =>
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config()).Submit();

            var first = CurlCallbackInvoker.Write(fixture.UserData, new byte[] { 1, 2, 3 });
            var second = CurlCallbackInvoker.Write(fixture.UserData, new byte[] { 4 });
            fixture.Task.TryEnterCompletedState(CurlCode.Ok);
            var response = await fixture.Task.Task;
            var body = await response.Content.ReadAsByteArrayAsync();

            Assert.That(first, Is.EqualTo((UIntPtr)3u));
            Assert.That(second, Is.EqualTo((UIntPtr)1u));
            Assert.That(fixture.Task.IsResponsePublished, Is.True);
            Assert.That(body, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        });

        /// <summary>Checks that the body callback rejects unknown user data and failed transfers.</summary>
        [Test]
        public void WriteCallback_RejectsUnknownOrFailedTransfers()
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config()).Submit();
            fixture.Task.TryFail(new IOException());

            Assert.That(CurlCallbackInvoker.Write(IntPtr.Zero, new byte[] { 1 }), Is.EqualTo(UIntPtr.Zero));
            Assert.That(CurlCallbackInvoker.Write(fixture.UserData, new byte[] { 1 }), Is.EqualTo(CurlCallbackReturn.Write.Error));
        }

        /// <summary>Checks that body backpressure pauses the transfer and that draining the body asks the owner to resume.</summary>
        [Test]
        public void WriteCallback_BackpressureRequestsResume()
        {
            using var fixture = new CurlTaskFixture(CurlTaskFixture.Config(bodyBuffer: 100)).Submit();
            var stream = fixture.Task.Response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();

            CurlCallbackInvoker.Write(fixture.UserData, new byte[60]);
            var paused = CurlCallbackInvoker.Write(fixture.UserData, new byte[30]);
            stream.Read(new byte[30], 0, 30);

            Assert.That(paused, Is.EqualTo(CurlCallbackReturn.Write.Pause));
            Assert.That(fixture.ResumeCount, Is.EqualTo(1));
        }
    }
}
