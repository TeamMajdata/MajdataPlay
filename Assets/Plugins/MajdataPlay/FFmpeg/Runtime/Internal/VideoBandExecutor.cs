#nullable enable
using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace MajdataPlay.FFmpeg.Internal
{
    /// <summary>Processes one band of a video job that a <see cref="VideoBandExecutor"/> distributes across threads.</summary>
    /// <remarks>Implementations run on background band threads and must not call Unity APIs, including the profiler.</remarks>
    internal interface IVideoBandJob
    {
        /// <summary>Processes one band; distinct bands of the same job may run concurrently.</summary>
        /// <param name="band">The zero-based index of the band to process.</param>
        void Execute(int band);
    }

    /// <summary>Runs the bands of one job at a time on the calling thread and lazily created background threads.</summary>
    /// <remarks>
    /// One decoder or encoder worker owns each executor and calls <see cref="Run"/> and <see cref="Dispose"/> sequentially.
    /// A 64-bit claim word packs the job generation with its band count and next unclaimed band, so a band thread that
    /// wakes late can never claim a band of a finished or newer job. Job descriptors are published before the generation,
    /// and <see cref="Run"/> waits for every claimed band before it returns or rethrows. Steady-state runs allocate no
    /// managed memory, and waits never escalate to timer-based sleeps.
    /// </remarks>
    internal sealed class VideoBandExecutor : IDisposable
    {
        /// <summary>Limits the band count to the 16-bit fields of the claim word.</summary>
        internal const int MaximumBandCount = ushort.MaxValue;
        /// <summary>Masks one 16-bit band field of the claim word.</summary>
        private const long BandFieldMask = 0xFFFF;
        /// <summary>Stores the total number of threads, including the calling thread, that execute bands.</summary>
        private readonly int _threadCount;
        /// <summary>Wakes idle band threads without the spin-then-sleep escalation of <see cref="ManualResetEventSlim"/>.</summary>
        /// <remarks>The semaphore owns no wait handle and is never disposed, so a late band thread cannot observe disposal.</remarks>
        private readonly SemaphoreSlim _wake = new SemaphoreSlim(0, int.MaxValue);
        /// <summary>Owns the background band threads, created on the first multi-band run.</summary>
        private Thread[]? _workers;
        /// <summary>Packs the job generation (high 32 bits), band count (bits 16-31), and next unclaimed band (bits 0-15).</summary>
        private long _claim;
        /// <summary>Stores the job whose bands are being claimed, published before its generation.</summary>
        private IVideoBandJob? _job;
        /// <summary>Counts completed bands of the current job, including bands that failed.</summary>
        private int _completedBands;
        /// <summary>Stores the first exception raised by a band of the current job.</summary>
        private Exception? _error;
        /// <summary>Stores the generation of the most recently published job.</summary>
        private int _generation;
        /// <summary>Indicates that band threads must exit at their next wake-up.</summary>
        private volatile bool _disposed;

        /// <summary>Initializes an executor that uses up to the specified number of threads for each job.</summary>
        /// <param name="threadCount">The total number of threads, including the calling thread, that may execute bands.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="threadCount"/> is less than one.</exception>
        internal VideoBandExecutor(int threadCount)
        {
            if (threadCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(threadCount), "At least one band thread is required.");
            }

            _threadCount = threadCount;
        }

        /// <summary>Gets the number of background band threads that have been started.</summary>
        internal int StartedThreadCount => _workers?.Length ?? 0;

        /// <summary>Executes every band of a job and returns after all of them have completed.</summary>
        /// <param name="job">The job whose bands are executed; it must remain unchanged until this method returns.</param>
        /// <param name="bandCount">The number of bands to execute.</param>
        /// <remarks>A single band, or an executor limited to one thread, runs inline without starting threads.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="job"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="bandCount"/> is not between one and <see cref="MaximumBandCount"/>.</exception>
        /// <exception cref="ObjectDisposedException">This executor has been disposed.</exception>
        /// <exception cref="Exception">The first exception raised by a band, rethrown after all claimed bands have completed.</exception>
        internal void Run(IVideoBandJob job, int bandCount)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            if (bandCount < 1 || bandCount > MaximumBandCount)
            {
                throw new ArgumentOutOfRangeException(nameof(bandCount));
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VideoBandExecutor));
            }

            if (bandCount == 1 || _threadCount == 1)
            {
                for (var band = 0; band < bandCount; band++)
                {
                    job.Execute(band);
                }

                return;
            }

            var workers = EnsureWorkers();
            _job = job;
            _error = null;
            Volatile.Write(ref _completedBands, 0);
            _generation = unchecked(_generation + 1);
            // The full fence publishes the descriptors above before any thread can claim this generation.
            Interlocked.Exchange(ref _claim, ((long)(uint)_generation << 32) | ((long)bandCount << 16));
            _wake.Release(Math.Min(workers.Length, bandCount - 1));
            while (TryClaim(out var band))
            {
                ExecuteBand(job, band);
            }

            WaitForBands(bandCount);
            _job = null;
            var error = _error;
            _error = null;
            if (error != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        /// <summary>Signals idle band threads to exit without waiting for them.</summary>
        /// <remarks>Band threads are background threads; any thread that is still waking up exits without touching a job.</remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_workers != null)
            {
                _wake.Release(_workers.Length);
            }
        }

        /// <summary>Starts the background band threads on the first multi-band run.</summary>
        /// <returns>The started band threads.</returns>
        private Thread[] EnsureWorkers()
        {
            if (_workers != null)
            {
                return _workers;
            }

            var workers = new Thread[_threadCount - 1];
            for (var index = 0; index < workers.Length; index++)
            {
                var worker = new Thread(RunWorker)
                {
                    IsBackground = true,
                    Name = "FFmpeg Video Band " + (index + 1)
                };
                worker.Start();
                workers[index] = worker;
            }

            _workers = workers;
            return workers;
        }

        /// <summary>Claims and executes bands after each wake-up until the executor is disposed.</summary>
        private void RunWorker()
        {
            while (true)
            {
                _wake.Wait();
                if (_disposed)
                {
                    return;
                }

                while (TryClaim(out var band))
                {
                    // A successful claim proves the job is unfinished, so its descriptor is still current.
                    ExecuteBand(Volatile.Read(ref _job), band);
                }
            }
        }

        /// <summary>Atomically claims the next unclaimed band of the current job.</summary>
        /// <param name="band">Receives the claimed zero-based band index, or -1 when every band has been claimed.</param>
        /// <returns><see langword="true"/> when a band was claimed; otherwise, <see langword="false"/>.</returns>
        private bool TryClaim(out int band)
        {
            while (true)
            {
                var observed = Interlocked.Read(ref _claim);
                var bandCount = (int)((observed >> 16) & BandFieldMask);
                var next = (int)(observed & BandFieldMask);
                if (next >= bandCount)
                {
                    band = -1;
                    return false;
                }

                // The count and generation travel in the same word, so only the published job can advance here.
                if (Interlocked.CompareExchange(ref _claim, observed + 1, observed) == observed)
                {
                    band = next;
                    return true;
                }
            }
        }

        /// <summary>Executes one claimed band, recording its first failure, and marks it complete.</summary>
        /// <param name="job">The job that owns the claimed band; null is recorded as a failure of that band.</param>
        /// <param name="band">The claimed zero-based band index.</param>
        private void ExecuteBand(IVideoBandJob? job, int band)
        {
            try
            {
                job!.Execute(band);
            }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref _error, error, null);
            }
            finally
            {
                Interlocked.Increment(ref _completedBands);
            }
        }

        /// <summary>Waits until every band of the current job has completed.</summary>
        /// <param name="bandCount">The number of bands in the current job.</param>
        /// <remarks>Claimed bands are short pixel copies or conversions, so this spins and yields like <see cref="GpuCompletionWait"/> without a timer-based sleep.</remarks>
        private void WaitForBands(int bandCount)
        {
            var spinWait = new SpinWait();
            while (Volatile.Read(ref _completedBands) < bandCount)
            {
                if (spinWait.NextSpinWillYield)
                {
                    Thread.Yield();
                    spinWait.Reset();
                }
                else
                {
                    spinWait.SpinOnce();
                }
            }
        }
    }
}
