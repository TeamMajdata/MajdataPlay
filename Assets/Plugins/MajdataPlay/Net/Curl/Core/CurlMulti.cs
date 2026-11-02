using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Curl.Core.PInvoke;
using MajdataPlay.Net.Curl.Diagnostics;
using MajdataPlay.Net.Curl.Lifecycle;
using MajdataPlay.Net.Curl.Utils;
using MajdataPlay.UnsafeKit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl.Core
{
    /// <summary>
    /// Owns a libcurl multi handle and the worker thread that drives every transfer added to it.
    /// </summary>
    /// <remarks>
    /// Only the worker attaches easy handles to or detaches them from the multi handle; other threads hand it work through
    /// the pending queues. Once a transfer has been detached, its native resources are released by whoever can no longer
    /// race with the worker: the worker itself when the response was never handed out, otherwise the thread that disposes
    /// the response. Disposing stops the worker first and then detaches and fails whatever is still running.
    /// </remarks>
    public class CurlMulti : CurlHandle, IAsyncDisposable
    {
        /// <summary>
        /// The long-running worker loop.
        /// </summary>
        readonly Task _workerThread;

        /// <summary>
        /// Stops the worker loop.
        /// </summary>
        readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Transfers currently attached to the multi handle. Accessed by the worker only, or by disposal after the worker has stopped.
        /// </summary>
        readonly HashSet<CurlTask> _activeTasks = new();

        /// <summary>
        /// Serializes enqueuing work and waking the worker against releasing the multi handle.
        /// </summary>
        readonly object _lifecycleLock = new();

        /// <summary>
        /// Whether disposal has started; new requests are rejected from then on.
        /// </summary>
        volatile bool _isStopping;

        /// <summary>
        /// The disposal shared by every <see cref="Dispose"/> and <see cref="DisposeAsync"/> call.
        /// </summary>
        Task? _disposeTask;

        /// <summary>
        /// Transfers waiting to be attached to the multi handle.
        /// </summary>
        readonly ConcurrentQueue<CurlTask> _pendingToSubmitTasks = new();

        /// <summary>
        /// Transfers whose cancellation token was cancelled.
        /// </summary>
        readonly ConcurrentQueue<CurlTask> _pendingToCancelTasks = new();

        /// <summary>
        /// Transfers whose reader drained enough body bytes to resume.
        /// </summary>
        readonly ConcurrentQueue<CurlTask> _pendingToResumeTasks = new();

        /// <summary>
        /// Transfers whose response was disposed while still attached to the multi handle.
        /// </summary>
        readonly ConcurrentQueue<CurlTask> _pendingToDisposeTasks = new();

        /// <summary>
        /// Cached <see cref="OnTaskResume"/> delegate shared by every transfer.
        /// </summary>
        readonly Action<CurlTask> _onResumeRequested;

        /// <summary>
        /// Cached <see cref="OnTaskDispose"/> delegate shared by every transfer.
        /// </summary>
        readonly Action<CurlTask> _onDisposeRequested;

        /// <summary>
        /// Cached <see cref="OnTaskCancel"/> delegate shared by every transfer.
        /// </summary>
        readonly Action<CurlTask> _onCancelRequested;

        /// <summary>
        /// The request counters and connection pool of this multi handle, listed in <see cref="CurlStatistics"/>.
        /// </summary>
        readonly CurlHandlerStatistics _statistics;

        /// <summary>
        /// Gets the request counters and connection pool of this multi handle.
        /// </summary>
        internal CurlHandlerStatistics Statistics
        {
            get => _statistics;
        }

        /// <summary>
        /// Creates the multi handle and starts its worker.
        /// </summary>
        /// <exception cref="CurlException">libcurl's global initialization failed.</exception>
        /// <exception cref="InvalidOperationException"><c>curl_multi_init</c> could not allocate a multi handle.</exception>
        internal CurlMulti()
        {
            LibCurlLifecycle.Retain();
            ThisHandle = LibCurl.Multi.Init();
            if (ThisHandle == IntPtr.Zero)
            {
                // Without a handle there is nothing for Dispose to release, so the reference must be returned here.
                LibCurlLifecycle.Release();
                throw new InvalidOperationException("Failed to initialize the libcurl multi handle.");
            }
            _onResumeRequested = OnTaskResume;
            _onDisposeRequested = OnTaskDispose;
            _onCancelRequested = OnTaskCancel;
            _statistics = CurlStatistics.Register();
            _workerThread = Task.Factory.StartNew(Run, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>
        /// Queues a request for the worker.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="contentStream">The request body, or <see langword="null"/> when there is none.</param>
        /// <param name="config">The handler settings captured for this request.</param>
        /// <param name="token">Cancels the request; once the response is published it aborts the body stream.</param>
        /// <returns>A task that completes with the response once the first body byte or the end of the transfer arrives.</returns>
        /// <exception cref="ObjectDisposedException">The multi handle is being or has been disposed.</exception>
        /// <exception cref="InvalidOperationException">libcurl rejected the request options.</exception>
        public Task<CurlResponse> AddToQueue(HttpRequestMessage request, Stream? contentStream, CurlHttpConfig config, CancellationToken token = default)
        {
            ThrowIfStopping();
            // Building the easy handle runs option, proxy and certificate lookups, so it stays outside the lifecycle lock.
            var curlTask = CreateTask(request, contentStream, config, token);
            var isQueued = false;
            lock (_lifecycleLock)
            {
                if (!_isStopping)
                {
                    // Registered before the task is visible to the worker, so the worker always sees the registration.
                    // An already-cancelled token calls OnTaskCancel synchronously; the lock is re-entrant.
                    curlTask.RegisterCancellation();
                    // Counted before the worker can dequeue it, so the queued count never goes negative.
                    _statistics.OnRequestQueued();
                    _pendingToSubmitTasks.Enqueue(curlTask);
                    WakeUp();
                    isQueued = true;
                }
            }
            if (!isQueued)
            {
                curlTask.ReleaseNativeResources();
                throw new ObjectDisposedException(nameof(CurlMulti));
            }
            if (curlTask.Record is not null)
            {
                CurlNetworkMonitor.Register(curlTask.Record);
            }

            return curlTask.Task;
        }

        /// <summary>
        /// Creates a transfer with every request and handler option applied.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="contentStream">The request body, or <see langword="null"/> when there is none.</param>
        /// <param name="config">The handler settings captured for this request.</param>
        /// <param name="token">The caller's cancellation token.</param>
        /// <returns>A transfer that is not queued yet.</returns>
        /// <exception cref="InvalidOperationException">libcurl rejected an option.</exception>
        CurlTask CreateTask(HttpRequestMessage request, Stream? contentStream, CurlHttpConfig config, CancellationToken token)
        {
            var curlEasy = new CurlEasy();
            var curlTask = default(CurlTask);
            try
            {
                var curlRequest = new CurlRequest(request, contentStream)
                {
                    Record = CurlNetworkMonitor.TryCreateRecord(request, config)
                };
                curlTask = new CurlTask(curlEasy, curlRequest, config, _onResumeRequested, _onDisposeRequested, _onCancelRequested, token, _statistics);

                curlRequest.ApplyTo(curlEasy);
                config.ApplyToEasy(curlEasy, curlRequest.RequestUri);

                return curlTask;
            }
            catch
            {
                if (curlTask is null)
                {
                    curlEasy.Dispose();
                }
                else
                {
                    curlTask.ReleaseNativeResources();
                }
                throw;
            }
        }

        /// <summary>
        /// Queues a paused transfer to be resumed by the worker.
        /// </summary>
        /// <param name="task">The transfer to resume.</param>
        void OnTaskResume(CurlTask task)
        {
            lock (_lifecycleLock)
            {
                if (!_isStopping)
                {
                    _pendingToResumeTasks.Enqueue(task);
                    WakeUp();
                }
            }
        }

        /// <summary>
        /// Queues a transfer whose cancellation token was cancelled.
        /// </summary>
        /// <param name="task">The transfer to cancel.</param>
        void OnTaskCancel(CurlTask task)
        {
            lock (_lifecycleLock)
            {
                if (!_isStopping)
                {
                    _pendingToCancelTasks.Enqueue(task);
                    WakeUp();
                }
            }
        }

        /// <summary>
        /// Releases a transfer whose response was disposed. A transfer still attached to the multi handle is handed to
        /// the worker; a detached one is released on the calling thread without waking the worker.
        /// </summary>
        /// <param name="task">The transfer whose response was disposed.</param>
        void OnTaskDispose(CurlTask task)
        {
            if (task.IsAttached)
            {
                lock (_lifecycleLock)
                {
                    if (ThisHandle != IntPtr.Zero)
                    {
                        // Only the worker may detach a running transfer from the multi handle.
                        _pendingToDisposeTasks.Enqueue(task);
                        WakeUp();
                        return;
                    }
                }
            }
            // The transfer has left the multi handle, or the stopped multi handle detached it, so no other thread
            // touches its easy handle any more. A response may also outlive its HTTP client and worker.
            task.ReleaseNativeResources();
        }

        /// <summary>
        /// The worker loop: applies queued work, drives libcurl and completes finished transfers until disposal.
        /// </summary>
        void Run()
        {
            var thisToken = _cts.Token;
            Thread.CurrentThread.Name = "Curl multi worker";
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            while (!thisToken.IsCancellationRequested)
            {
                try
                {
                    SubmitPendingTasks();
                    CancelPendingTasks();
                    ResumePendingTasks();
                    DisposePendingTasks();

                    if (thisToken.IsCancellationRequested)
                    {
                        return;
                    }

                    LibCurl.Multi.Perform(ThisHandle, out _);

                    if (thisToken.IsCancellationRequested)
                    {
                        return;
                    }
                    while (LibCurl.Multi.GetMessage(ThisHandle, out _) is CurlMsg multiMsg)
                    {
                        if (multiMsg.Code == CurlMsgCode.Done)
                        {
                            CompleteTask(multiMsg);
                        }
                    }

                    LibCurl.Multi.Poll(ThisHandle, IntPtr.Zero, 0, 1000, out _);
                }
                catch (Exception e)
                {
                    MajDebug.LogError($"[libcurl][Multi worker]{e}");
                    // Shutdown must interrupt the retry delay as well as curl_multi_poll.
                    thisToken.WaitHandle.WaitOne(2500);
                }
            }
        }

        /// <summary>
        /// Attaches queued transfers to the multi handle. Worker thread only.
        /// </summary>
        void SubmitPendingTasks()
        {
            while (_pendingToSubmitTasks.TryDequeue(out var pendingTask))
            {
                _statistics.OnRequestDequeued();
                if (pendingTask.CancellationToken.IsCancellationRequested)
                {
                    pendingTask.TryEnterCancelledState();
                    FinishDetachedTask(pendingTask);
                    continue;
                }
                if (!pendingTask.TryEnterSubmittedState())
                {
                    // Already cancelled through the cancellation queue.
                    FinishDetachedTask(pendingTask);
                    continue;
                }
                var returnCode = pendingTask.Config.ApplyToMulti(this);
                if (returnCode != CurlMCode.Ok)
                {
                    pendingTask.TryFail(new InvalidOperationException($"libcurl error: curl_multi_setopt returned {returnCode}"));
                    FinishDetachedTask(pendingTask);
                    continue;
                }
                _statistics.OnMaxConnectionsPerHostApplied(pendingTask.Config.MaxConnectionsPerServer);
                returnCode = LibCurl.Multi.AddEasyHandle(ThisHandle, pendingTask.Easy.Handle);
                if (returnCode != CurlMCode.Ok)
                {
                    pendingTask.TryFail(new InvalidOperationException($"libcurl error: curl_multi_add_handle returned {returnCode}"));
                    FinishDetachedTask(pendingTask);
                    continue;
                }
                pendingTask.IsAttached = true;
                _activeTasks.Add(pendingTask);
                _statistics.OnRequestAttached();
            }
        }

        /// <summary>
        /// Cancels transfers whose cancellation token was cancelled. Worker thread only.
        /// </summary>
        void CancelPendingTasks()
        {
            while (_pendingToCancelTasks.TryDequeue(out var cancelTask))
            {
                if (cancelTask.TryEnterCancelledState())
                {
                    if (cancelTask.IsAttached)
                    {
                        cancelTask.CaptureTransferInfo();
                    }
                    TryDetach(cancelTask);
                    FinishDetachedTask(cancelTask);
                }
            }
        }

        /// <summary>
        /// Resumes transfers paused by body backpressure. Worker thread only.
        /// </summary>
        void ResumePendingTasks()
        {
            while (_pendingToResumeTasks.TryDequeue(out var pausedTask))
            {
                // A transfer that already left the multi handle may have released its easy handle.
                if (_activeTasks.Contains(pausedTask))
                {
                    pausedTask.Easy.Pause(CurlPauseAction.None);
                }
            }
        }

        /// <summary>
        /// Detaches and releases transfers whose response was disposed while they were running. Worker thread only.
        /// </summary>
        void DisposePendingTasks()
        {
            while (_pendingToDisposeTasks.TryDequeue(out var disposeTask))
            {
                // Only an attached transfer is guaranteed to still own its easy handle here.
                if (disposeTask.IsAttached)
                {
                    disposeTask.CaptureTransferInfo();
                }
                TryDetach(disposeTask);
                disposeTask.TryFail(new ObjectDisposedException(nameof(CurlResponse)));
                disposeTask.ReleaseNativeResources();
                disposeTask.ReleaseCancellationRegistration();
            }
        }

        /// <summary>
        /// Wakes the worker from <c>curl_multi_poll</c>. The multi handle must still be allocated.
        /// </summary>
        void WakeUp()
        {
            LibCurl.Multi.Wakeup(ThisHandle);
        }

        /// <summary>
        /// Completes the transfer that libcurl reported as done. Worker thread only.
        /// </summary>
        /// <param name="multiMsg">The <c>CURLMSG_DONE</c> message.</param>
        void CompleteTask(CurlMsg multiMsg)
        {
            var easyHandle = multiMsg.EasyHandle;
            LibCurl.Easy.GetInfo(easyHandle, CurlInfo.Private, out IntPtr privatePtr);
            if (!UnsafeHelper.TryGetInstanceFromGCHandle<CurlTask>(privatePtr, out var curlTask) || !_activeTasks.Contains(curlTask))
            {
                return;
            }
            var result = multiMsg.Data.Result;
            // Read the error details while the easy handle is still guaranteed to be alive.
            var curlErr = CurlUtility.GetEasyException(easyHandle, result, result == CurlCode.Ok ? null : curlTask.Easy.GetErrorDetail());
            curlTask.CaptureTransferInfo();
            LibCurl.Easy.SetOption(easyHandle, CurlOption.Private, IntPtr.Zero);
            // Once detached, a published response may release the easy handle on another thread at any moment.
            TryDetach(curlTask);
            if (curlErr is not null)
            {
                curlTask.TryFail(CurlUtility.GetTransferException(curlErr));
            }
            else
            {
                curlTask.TryEnterHeaderReadState();
                curlTask.TryEnterCompletedState(result);
            }
            FinishDetachedTask(curlTask);
        }

        /// <summary>
        /// Removes an attached transfer from the multi handle. Worker thread only.
        /// </summary>
        /// <param name="task">The transfer to detach.</param>
        /// <returns><see langword="true"/> if the transfer was attached.</returns>
        bool TryDetach(CurlTask task)
        {
            if (!_activeTasks.Remove(task))
            {
                return false;
            }
            LibCurl.Multi.RemoveEasyHandle(ThisHandle, task.Easy.Handle);
            task.IsAttached = false;
            // Removing the handle has already reported a connection libcurl closed for it, such as an aborted one.
            _statistics.OnRequestDetached(task.ConnectionId);
            return true;
        }

        /// <summary>
        /// Releases what a finished, detached transfer still holds: its native resources when the response was never
        /// handed out (nobody else will dispose them), and its cancellation registration. Worker thread only.
        /// </summary>
        /// <param name="task">The finished transfer.</param>
        static void FinishDetachedTask(CurlTask task)
        {
            if (!task.IsResponsePublished)
            {
                task.ReleaseNativeResources();
            }
            task.ReleaseCancellationRegistration();
        }

        /// <summary>
        /// Stops the worker and releases the multi handle, blocking until both are done.
        /// </summary>
        public override void Dispose()
        {
            BeginDispose().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Stops the worker and releases the multi handle.
        /// </summary>
        /// <returns>A task that completes once the worker has stopped and the multi handle has been released.</returns>
        public ValueTask DisposeAsync()
        {
            return new ValueTask(BeginDispose());
        }

        /// <summary>
        /// Starts disposal once; later calls share it.
        /// </summary>
        /// <returns>The shared disposal task.</returns>
        Task BeginDispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposeTask is not null)
                {
                    return _disposeTask;
                }
                _isStopping = true;
                _cts.Cancel();
                // Keep the native handle valid until the worker has stopped using it.
                WakeUp();
                _disposeTask = DisposeCoreAsync(ThisHandle);
                return _disposeTask;
            }
        }

        /// <summary>
        /// Waits for the worker to stop, then detaches and fails every remaining transfer and releases the multi handle.
        /// </summary>
        /// <param name="handle">The multi handle to release.</param>
        /// <returns>A task that completes once everything has been released.</returns>
        async Task DisposeCoreAsync(IntPtr handle)
        {
            try
            {
                await _workerThread.ConfigureAwait(false);
            }
            finally
            {
                var abandonedTasks = default(List<CurlTask>);
                lock (_lifecycleLock)
                {
                    ThisHandle = IntPtr.Zero;
                    try
                    {
                        abandonedTasks = AbandonTasks(handle);
                    }
                    finally
                    {
                        LibCurl.Multi.CleanUp(handle);
                        LibCurlLifecycle.Release();
                        // curl_multi_cleanup closes the pooled connections without reporting them.
                        CurlStatistics.OnHandlerDisposed(_statistics);
                    }
                }
                // Releasing a registration waits for a running cancellation callback, which may be blocked on the
                // lifecycle lock, so this happens after the lock is released.
                foreach (var task in abandonedTasks)
                {
                    task.ReleaseCancellationRegistration();
                }
                _cts.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Detaches, fails and releases every transfer the stopped worker left behind. Must be called while holding
        /// <see cref="_lifecycleLock"/> after the worker has stopped.
        /// </summary>
        /// <param name="handle">The multi handle the transfers are attached to.</param>
        /// <returns>The abandoned transfers, whose cancellation registrations still have to be released.</returns>
        List<CurlTask> AbandonTasks(IntPtr handle)
        {
            var tasks = new List<CurlTask>(_activeTasks);
            while (_pendingToSubmitTasks.TryDequeue(out var task))
            {
                tasks.Add(task);
            }
            while (_pendingToDisposeTasks.TryDequeue(out var task))
            {
                tasks.Add(task);
            }
            // A transfer may be listed twice (attached and disposed); every step below is idempotent.
            foreach (var task in tasks)
            {
                try
                {
                    if (_activeTasks.Remove(task))
                    {
                        LibCurl.Multi.RemoveEasyHandle(handle, task.Easy.Handle);
                        task.IsAttached = false;
                    }
                    task.TryFail(new ObjectDisposedException(nameof(CurlMulti), "The HTTP handler was disposed before the request completed."));
                    // The body stream does not depend on the easy handle, so even a response that is being read can be released.
                    task.ReleaseNativeResources();
                }
                catch (Exception exception)
                {
                    MajDebug.LogException(exception);
                }
            }
            _activeTasks.Clear();
            while (_pendingToCancelTasks.TryDequeue(out _))
            {
            }
            while (_pendingToResumeTasks.TryDequeue(out _))
            {
            }

            return tasks;
        }

        /// <summary>
        /// Throws if disposal has started.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The multi handle is being or has been disposed.</exception>
        void ThrowIfStopping()
        {
            if (_isStopping)
            {
                throw new ObjectDisposedException(nameof(CurlMulti));
            }
            ThrowIfDisposed();
        }
    }
}
