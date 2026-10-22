using System;
using System.Threading;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies that the band executor runs every band exactly once and propagates failures.</summary>
    public class VideoBandExecutorTests
    {
        /// <summary>Counts executions of each band and optionally fails one band.</summary>
        private sealed class CountingJob : IVideoBandJob
        {
            /// <summary>Stores the execution count of each band.</summary>
            private readonly int[] _counts;
            /// <summary>Stores the band that throws, or -1 when no band fails.</summary>
            private readonly int _failingBand;
            /// <summary>Stores the managed thread identifiers that executed bands.</summary>
            private readonly int[] _threads;

            /// <summary>Creates a job for the requested band count.</summary>
            /// <param name="bandCount">The number of bands.</param>
            /// <param name="failingBand">The band that throws, or -1.</param>
            public CountingJob(int bandCount, int failingBand = -1)
            {
                _counts = new int[bandCount];
                _threads = new int[bandCount];
                _failingBand = failingBand;
            }

            /// <summary>Gets the execution count of each band.</summary>
            public int[] Counts => _counts;

            /// <summary>Gets the thread that executed each band.</summary>
            public int[] Threads => _threads;

            /// <summary>Records one band execution and throws for the failing band.</summary>
            /// <param name="band">The executed band.</param>
            public void Execute(int band)
            {
                Interlocked.Increment(ref _counts[band]);
                _threads[band] = Thread.CurrentThread.ManagedThreadId;
                // A short spin lets several threads take part in the job.
                Thread.SpinWait(2000);
                if (band == _failingBand)
                {
                    throw new InvalidOperationException("band " + band);
                }
            }
        }

        /// <summary>Checks constructor validation.</summary>
        [Test]
        public void Constructor_RejectsZeroThreads()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoBandExecutor(0));
        }

        /// <summary>Checks argument validation of Run.</summary>
        [Test]
        public void Run_ValidatesArguments()
        {
            using var executor = new VideoBandExecutor(2);

            Assert.Throws<ArgumentNullException>(() => executor.Run(null!, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.Run(new CountingJob(1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => executor.Run(new CountingJob(1), VideoBandExecutor.MaximumBandCount + 1));
        }

        /// <summary>Checks that a single-thread executor runs bands inline without starting threads.</summary>
        [Test]
        public void Run_SingleThread_RunsInline()
        {
            using var executor = new VideoBandExecutor(1);
            var job = new CountingJob(5);

            executor.Run(job, 5);

            Assert.That(job.Counts, Is.EqualTo(new[] { 1, 1, 1, 1, 1 }));
            Assert.That(job.Threads, Has.All.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.That(executor.StartedThreadCount, Is.EqualTo(0));
        }

        /// <summary>Checks that one band runs inline even when more threads are available.</summary>
        [Test]
        public void Run_OneBand_DoesNotStartThreads()
        {
            using var executor = new VideoBandExecutor(4);
            var job = new CountingJob(1);

            executor.Run(job, 1);

            Assert.That(job.Counts[0], Is.EqualTo(1));
            Assert.That(executor.StartedThreadCount, Is.EqualTo(0));
        }

        /// <summary>Checks that many bands are each executed exactly once across repeated jobs.</summary>
        [Test]
        public void Run_ManyBands_ExecutesEachBandOncePerJob()
        {
            using var executor = new VideoBandExecutor(4);
            for (var repeat = 0; repeat < 20; repeat++)
            {
                var job = new CountingJob(37);

                executor.Run(job, 37);

                Assert.That(job.Counts, Has.All.EqualTo(1), "repeat " + repeat);
            }

            Assert.That(executor.StartedThreadCount, Is.EqualTo(3));
        }

        /// <summary>Checks that a failing band is rethrown after all bands complete and the executor stays usable.</summary>
        [Test]
        public void Run_FailingBand_RethrowsAfterCompletion()
        {
            using var executor = new VideoBandExecutor(3);
            var job = new CountingJob(12, 7);

            var error = Assert.Throws<InvalidOperationException>(() => executor.Run(job, 12));

            Assert.That(error!.Message, Is.EqualTo("band 7"));
            Assert.That(job.Counts, Has.All.EqualTo(1));
            var next = new CountingJob(6);
            executor.Run(next, 6);
            Assert.That(next.Counts, Has.All.EqualTo(1));
        }

        /// <summary>Checks that a disposed executor rejects jobs and tolerates repeated disposal.</summary>
        [Test]
        public void Dispose_RejectsLaterRuns()
        {
            var executor = new VideoBandExecutor(2);
            executor.Run(new CountingJob(4), 4);

            executor.Dispose();
            executor.Dispose();

            Assert.Throws<ObjectDisposedException>(() => executor.Run(new CountingJob(2), 2));
        }

        /// <summary>Checks that disposing an executor that never started threads succeeds.</summary>
        [Test]
        public void Dispose_WithoutThreads_Succeeds()
        {
            var executor = new VideoBandExecutor(8);

            Assert.DoesNotThrow(() => executor.Dispose());
            Assert.That(executor.StartedThreadCount, Is.EqualTo(0));
        }
    }
}
