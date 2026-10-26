using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the bounded capture buffer pool and background encoder of a recording session.</summary>
    public class VideoEncodeSessionTests
    {
        /// <summary>The longest time a test waits for the encoder worker.</summary>
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
        /// <summary>Owns the output files of the current test.</summary>
        private TemporaryDirectory _directory = null!;

        /// <summary>Creates a temporary output directory.</summary>
        [SetUp]
        public void SetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            _directory = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary output directory.</summary>
        [TearDown]
        public void TearDown()
        {
            FFmpegTestMedia.DisposeWhenReleased(_directory);
        }

        /// <summary>Creates small recording options.</summary>
        /// <returns>Valid options.</returns>
        private static EncoderOptions Options()
        {
            return new EncoderOptions
            {
                Width = 32,
                Height = 16,
                FrameRate = 30,
                Format = VideoEncodingFormat.MPEG4,
                MaximumSoftwareThreads = 1,
                BitRate = 100000,
                MaximumBitRate = 200000
            };
        }

        /// <summary>Checks constructor validation of options and capacity.</summary>
        [Test]
        public void Constructor_ValidatesOptionsAndCapacity()
        {
            var path = FFmpegTestMedia.PathIn(_directory, "invalid.mkv");

            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoEncodeSession(path, new EncoderOptions { Width = 1 }, 2, CancellationToken.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoEncodeSession(path, Options(), 0, CancellationToken.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoEncodeSession(path, Options(), 9, CancellationToken.None));
        }

        /// <summary>Checks a complete recording: reservations, out-of-order readbacks, statistics and finalization.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Recording_EncodesReadyFramesInTimestampOrder()
        {
            var path = FFmpegTestMedia.PathIn(_directory, "session.mkv");
            var session = new VideoEncodeSession(path, Options(), 3, CancellationToken.None);
            var watch = Stopwatch.StartNew();
            while (!session.Ready.IsCompleted && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            Assert.That(session.Ready.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(session.EncoderName, Is.EqualTo("mpeg4"));
            Assert.That(session.EncoderType, Is.EqualTo(VideoEncoderType.Software));
            Assert.That(session.RateControlMode, Is.EqualTo(VideoRateControlMode.VBR));
            Assert.That(session.HardwareFallbackReason, Is.Null);
            Assert.That(session.Capacity, Is.EqualTo(3));
            Assert.That(session.CanAcceptFrames, Is.True);
            Assert.That(session.SoftwareThreadCount, Is.EqualTo(1));
            Assert.That(session.SoftwareThreadingMode, Is.EqualTo("none"));
            Assert.That(session.ConverterThreadCount, Is.EqualTo(1));

            var first = session.TryReserve(0, false)!;
            var second = session.TryReserve(1, true)!;
            var third = session.TryReserve(2, false)!;
            Assert.That(session.TryReserve(3, false), Is.Null);
            Assert.That(session.PeakReservedFrames, Is.EqualTo(3));
            Assert.That(first.Slot, Is.Not.EqualTo(second.Slot));
            Assert.That(first.Pixels.Length, Is.EqualTo(32 * 16 * 4));
            Assert.That(second.FlipVertically, Is.True);
            Assert.That(third.FrameIndex, Is.EqualTo(2));

            session.CompleteReadback(second, true);
            session.CompleteReadback(third, false);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.That(session.EncodedFrames, Is.EqualTo(0));
            session.CompleteReadback(first, true);
            watch.Restart();
            while (session.EncodedFrames < 2 && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            Assert.That(session.EncodedFrames, Is.EqualTo(2));
            session.Stop();
            Assert.That(session.CanAcceptFrames, Is.False);
            Assert.That(session.TryReserve(5, false), Is.Null);
            watch.Restart();
            while (!session.Completion.IsCompleted && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            Assert.That(session.Completion.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(session.Finished, Is.True);
            Assert.That(session.Error, Is.Null);
            Assert.That(session.BytesWritten, Is.GreaterThan(0));
            Assert.That(session.CurrentBitRate, Is.GreaterThanOrEqualTo(0));
            using var decoder = new FFmpegVideoDecoder();
            decoder.Open(path, CancellationToken.None);
            var frames = 0;
            while (true)
            {
                using var frame = decoder.ReadFrame();
                if (frame == null)
                {
                    break;
                }

                frames++;
            }

            Assert.That(frames, Is.EqualTo(2));
        }

        /// <summary>Checks that a capture failure stops the session and faults its completion.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Fail_FaultsCompletion()
        {
            var session = new VideoEncodeSession(FFmpegTestMedia.PathIn(_directory, "failed.mkv"), Options(), 2, CancellationToken.None);
            var watch = Stopwatch.StartNew();
            while (!session.Ready.IsCompleted && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            var reserved = session.TryReserve(0, false)!;
            var failure = new IOException("readback failed");
            session.Fail(failure);
            session.Fail(new IOException("second failure"));
            session.CompleteReadback(reserved, true);
            watch.Restart();
            while (!session.Completion.IsCompleted && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            Assert.That(session.Completion.IsFaulted, Is.True);
            Assert.That(session.Completion.Exception!.InnerException, Is.SameAs(failure));
            Assert.That(session.Error, Is.SameAs(failure));
            Assert.That(session.CanAcceptFrames, Is.False);
            Assert.That(session.TryReserve(1, false), Is.Null);
        }

        /// <summary>Checks that canceling the recording lifetime cancels both tasks.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Cancellation_CancelsCompletion()
        {
            using var source = new CancellationTokenSource();
            var session = new VideoEncodeSession(FFmpegTestMedia.PathIn(_directory, "canceled.mkv"), Options(), 2, source.Token);
            var watch = Stopwatch.StartNew();
            while (!session.Ready.IsCompleted && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            source.Cancel();
            Assert.That(session.CanAcceptFrames, Is.False);
            Assert.That(session.TryReserve(0, false), Is.Null);
            watch.Restart();
            while (!session.Completion.IsCompleted && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            Assert.That(session.Completion.IsCanceled, Is.True);
            Assert.That(session.Error, Is.InstanceOf<OperationCanceledException>());
            Assert.That(session.Finished, Is.True);
        }

        /// <summary>Checks that an encoder open failure faults the ready task.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OpenFailure_FaultsReady()
        {
            var existing = _directory.WriteText("exists.mkv", "keep");
            var session = new VideoEncodeSession(existing.Location, Options(), 2, CancellationToken.None);
            var watch = Stopwatch.StartNew();
            while (!session.Completion.IsCompleted && watch.Elapsed < WaitLimit)
            {
                yield return null;
            }

            Assert.That(session.Ready.IsFaulted, Is.True);
            Assert.That(session.Completion.IsFaulted, Is.True);
            Assert.That(session.Error, Is.InstanceOf<IOException>());
            Assert.That(session.Finished, Is.True);
            Assert.That(session.EncoderName, Is.Null);
            Assert.That(existing.ReadAllText(), Is.EqualTo("keep"));
        }
    }
}
