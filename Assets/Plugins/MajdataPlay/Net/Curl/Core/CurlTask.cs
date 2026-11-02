using AOT;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl.Core
{
    /// <summary>
    /// One HTTP transfer driven by <see cref="CurlMulti"/>: its state machine, its response, and the native resources it owns.
    /// </summary>
    /// <remarks>
    /// The task owns the easy handle and the GC handles libcurl's callbacks use to find it. They are released by
    /// <see cref="ReleaseNativeResources"/> as soon as the transfer has left the multi handle and nobody can dispose
    /// the response any more (or the response has been disposed), instead of waiting for finalizers.
    /// </remarks>
    class CurlTask
    {
        /// <summary>
        /// Gets the current state of the transfer.
        /// </summary>
        public CurlTaskState State
        {
            get
            {
                return (CurlTaskState)Volatile.Read(ref _state);
            }
        }

        /// <summary>
        /// Gets the request being sent.
        /// </summary>
        public CurlRequest Request { get; }

        /// <summary>
        /// Gets the response that is published once the first body byte or the end of the transfer arrives.
        /// </summary>
        public CurlResponse Response { get; }

        /// <summary>
        /// Gets the task that completes with <see cref="Response"/>, or fails or is cancelled before it is published.
        /// </summary>
        public Task<CurlResponse> Task
        {
            get => _taskSource.Task;
        }

        /// <summary>
        /// Gets the handler settings captured when the request was sent.
        /// </summary>
        public CurlHttpConfig Config { get; }

        /// <summary>
        /// Gets the easy handle performing the transfer.
        /// </summary>
        internal CurlEasy Easy { get; }

        /// <summary>
        /// Gets the caller's cancellation token.
        /// </summary>
        internal CancellationToken CancellationToken { get; }

        /// <summary>
        /// Gets or sets whether the easy handle is currently added to the multi handle. Written only by the multi worker
        /// (or by the multi handle's disposal after the worker has stopped). While it is <see langword="false"/> after
        /// the response has been published, no other thread touches the easy handle, so it may be released by whoever
        /// disposes the response.
        /// </summary>
        internal bool IsAttached
        {
            get => _isAttached;
            set => _isAttached = value;
        }

        /// <summary>
        /// Gets whether <see cref="Response"/> has been handed to the caller, who then owns its disposal.
        /// </summary>
        internal bool IsResponsePublished
        {
            get => _isResponsePublished;
        }

        /// <summary>
        /// Gets the network monitor's record of this transfer, or <see langword="null"/> when recording was off as the
        /// request was sent.
        /// </summary>
        internal CurlNetworkRecord? Record
        {
            get => Request.Record;
        }

        /// <summary>
        /// The <see cref="CurlTaskState"/> value, stored as an integer for <see cref="Interlocked"/> transitions.
        /// </summary>
        int _state = (int)CurlTaskState.Created;

        /// <summary>
        /// Set to 1 once the owner has been asked to dispose the transfer.
        /// </summary>
        int _disposeProgress = 0;

        /// <summary>
        /// Set to 1 once the native resources have been released.
        /// </summary>
        int _isNativeReleased = 0;

        /// <summary>
        /// Backing field of <see cref="IsAttached"/>.
        /// </summary>
        volatile bool _isAttached;

        /// <summary>
        /// Backing field of <see cref="IsResponsePublished"/>.
        /// </summary>
        volatile bool _isResponsePublished;

        /// <summary>
        /// The registration on <see cref="CancellationToken"/>; released once the transfer is over so a long-lived token
        /// does not keep the response alive.
        /// </summary>
        CancellationTokenRegistration _cancellationRegistration;

        /// <summary>
        /// Weak handle passed to libcurl as the private, write and header callback data.
        /// </summary>
        GCHandle _taskHandle;

        /// <summary>
        /// Asks the owner to resume a paused transfer.
        /// </summary>
        readonly Action<CurlTask> _onResume;

        /// <summary>
        /// Asks the owner to dispose the transfer.
        /// </summary>
        readonly Action<CurlTask> _onDispose;

        /// <summary>
        /// Asks the owner to cancel the transfer.
        /// </summary>
        readonly Action<CurlTask> _onCancel;

        /// <summary>
        /// Completes <see cref="Task"/>.
        /// </summary>
        readonly TaskCompletionSource<CurlResponse> _taskSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Shared cancellation callback, so registering does not allocate a closure per request.
        /// </summary>
        static readonly Action<object?> s_onCancellationRequested = OnCancellationRequested;

        /// <summary>
        /// Creates a transfer and points libcurl's callback data at it.
        /// </summary>
        /// <param name="curlEasy">The easy handle the task takes ownership of.</param>
        /// <param name="request">The request to send.</param>
        /// <param name="config">The handler settings captured when the request was sent.</param>
        /// <param name="onResume">Asks the owner to resume a paused transfer.</param>
        /// <param name="onDispose">Asks the owner to dispose the transfer.</param>
        /// <param name="onCancel">Asks the owner to cancel the transfer.</param>
        /// <param name="token">The caller's cancellation token; observed once <see cref="RegisterCancellation"/> is called.</param>
        /// <exception cref="InvalidOperationException">libcurl rejected one of the callback options.</exception>
        public CurlTask(CurlEasy curlEasy,
            CurlRequest request,
            CurlHttpConfig config,
            Action<CurlTask> onResume,
            Action<CurlTask> onDispose,
            Action<CurlTask> onCancel,
            CancellationToken token = default)
        {
            Easy = curlEasy;
            Request = request;
            Response = new CurlResponse(curlEasy, request, OnResumeRequested, OnDisposeRequested, config);
            Config = config;
            CancellationToken = token;
            _onResume = onResume;
            _onDispose = onDispose;
            _onCancel = onCancel;

            _taskHandle = GCHandle.Alloc(this, GCHandleType.Weak);
            var handlePtr = GCHandle.ToIntPtr(_taskHandle);
            Easy.SetOption(CurlOption.Private, handlePtr);
            Easy.SetOption(CurlOption.WriteData, handlePtr);
            Easy.SetOption(CurlOption.HeaderData, handlePtr);
            if (request.Record is not null)
            {
                CurlNetworkCapture.Attach(Easy, handlePtr);
            }
        }

        ~CurlTask()
        {
            if (_taskHandle.IsAllocated)
            {
                _taskHandle.Free();
            }
        }

        /// <summary>
        /// Starts observing <see cref="CancellationToken"/>. If the token is already cancelled, the owner is notified synchronously.
        /// </summary>
        public void RegisterCancellation()
        {
            if (CancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = CancellationToken.Register(s_onCancellationRequested, this);
            }
        }

        /// <summary>
        /// Stops observing <see cref="CancellationToken"/>. This waits for a cancellation callback that is running on
        /// another thread, so it must not be called while holding a lock that callback takes.
        /// </summary>
        public void ReleaseCancellationRegistration()
        {
            _cancellationRegistration.Dispose();
        }

        /// <summary>
        /// Cleans up the easy handle and frees the GC handles used by libcurl's callbacks. Idempotent.
        /// Only call this once the easy handle is not attached to a running multi handle.
        /// </summary>
        public void ReleaseNativeResources()
        {
            if (Interlocked.Exchange(ref _isNativeReleased, 1) != 0)
            {
                return;
            }
            try
            {
                Easy.Dispose();
            }
            finally
            {
                // No callback can run once the easy handle is gone, so the handles can be freed deterministically.
                Request.ReleaseHandle();
                if (_taskHandle.IsAllocated)
                {
                    _taskHandle.Free();
                }
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Copies libcurl's timing and connection details into <see cref="Record"/>, if there is one. Only call this from
        /// the multi worker while the easy handle is attached to the multi handle, or from one of its callbacks.
        /// </summary>
        public void CaptureTransferInfo()
        {
            var record = Record;
            if (record is null || !Easy.IsAllocated)
            {
                return;
            }
            record.OnTransferInfo(CurlTransferInfo.Read(Easy.Handle));
        }

        /// <summary>
        /// Moves from <see cref="CurlTaskState.Created"/> to <see cref="CurlTaskState.Submitted"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the transition happened.</returns>
        public bool TryEnterSubmittedState()
        {
            var lastState = Interlocked.CompareExchange(ref _state, (int)CurlTaskState.Submitted, (int)CurlTaskState.Created);

            if (lastState == (int)CurlTaskState.Created)
            {
                Record?.OnSubmitted();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves from <see cref="CurlTaskState.Submitted"/> to <see cref="CurlTaskState.HeaderRead"/> and publishes <see cref="Response"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the transition happened.</returns>
        public bool TryEnterHeaderReadState()
        {
            var lastState = Interlocked.CompareExchange(ref _state, (int)CurlTaskState.HeaderRead, (int)CurlTaskState.Submitted);

            if (lastState == (int)CurlTaskState.Submitted)
            {
                _isResponsePublished = true;
                _taskSource.TrySetResult(Response);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves from <see cref="CurlTaskState.HeaderRead"/> to <see cref="CurlTaskState.Completed"/> and ends the body stream.
        /// </summary>
        /// <param name="result">The transfer result reported by libcurl.</param>
        /// <returns><see langword="true"/> if the transition happened.</returns>
        public bool TryEnterCompletedState(CurlCode result)
        {
            var lastState = Interlocked.CompareExchange(ref _state, (int)CurlTaskState.Completed, (int)CurlTaskState.HeaderRead);

            if (lastState == (int)CurlTaskState.HeaderRead)
            {
                // The record is finished before the reader can observe the end of the body.
                Record?.OnCompleted();
                Response.ResultCode = result;
                Response.Complete();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves a running transfer to <see cref="CurlTaskState.Faulted"/>, aborting the body stream and, if the
        /// response has not been published yet, failing <see cref="Task"/>.
        /// </summary>
        /// <param name="abortException">The failure reported to the caller.</param>
        /// <returns><see langword="true"/> if the transition happened.</returns>
        public bool TryFail(Exception abortException)
        {
            var state = Volatile.Read(ref _state);
            switch ((CurlTaskState)state)
            {
                case CurlTaskState.Created:
                case CurlTaskState.Submitted:
                case CurlTaskState.HeaderRead:
                    break;
                default:
                    return false;
            }
            var oldState = Interlocked.CompareExchange(ref _state, (int)CurlTaskState.Faulted, state);
            if (oldState == state)
            {
                Record?.OnFailed(abortException);
                Response.Abort(abortException);
                _taskSource.TrySetException(abortException);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves a running transfer to <see cref="CurlTaskState.Cancelled"/>, aborting the body stream and, if the
        /// response has not been published yet, cancelling <see cref="Task"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the transition happened.</returns>
        public bool TryEnterCancelledState()
        {
            var state = Volatile.Read(ref _state);

            switch ((CurlTaskState)state)
            {
                case CurlTaskState.Created:
                case CurlTaskState.Submitted:
                case CurlTaskState.HeaderRead:
                    break;

                default:
                    return false;
            }

            var oldState = Interlocked.CompareExchange(ref _state, (int)CurlTaskState.Cancelled, state);

            if (oldState == state)
            {
                Record?.OnCancelled();
                Response.Abort();
                _taskSource.TrySetCanceled(CancellationToken);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Forwards a resume request from the body stream to the owner.
        /// </summary>
        void OnResumeRequested()
        {
            _onResume(this);
        }

        /// <summary>
        /// Forwards the first dispose request from the response or its body stream to the owner.
        /// </summary>
        void OnDisposeRequested()
        {
            if (Interlocked.CompareExchange(ref _disposeProgress, 1, 0) == 0)
            {
                _onDispose(this);
            }
        }

        /// <summary>
        /// Forwards a cancellation of <see cref="CancellationToken"/> to the owner.
        /// </summary>
        /// <param name="state">The <see cref="CurlTask"/> that registered the callback.</param>
        static void OnCancellationRequested(object? state)
        {
            var task = (CurlTask)state!;
            task._onCancel(task);
        }
    }
}
