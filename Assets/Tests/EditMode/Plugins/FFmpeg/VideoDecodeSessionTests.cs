using System;
using System.Collections;
using System.Diagnostics;
using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the background decode session: bounded queueing, seeking, playback deadlines and shutdown.</summary>
    public class VideoDecodeSessionTests
    {
        /// <summary>The longest time a test waits for the decoding worker.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        /// <summary>Owns the fixture media.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Stores a one-second, 30 fps clip.</summary>
        private string _clip = "";
        /// <summary>Stores the session created by the current test.</summary>
        private VideoDecodeSession? _session;

        /// <summary>Encodes the fixture media once.</summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            FFmpegTestMedia.RequireNativeLibraries();
            _directory = new TemporaryDirectory();
            _clip = FFmpegTestMedia.CreateVideo(_directory, "session.mkv");
        }

        /// <summary>Deletes the fixture media.</summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            FFmpegTestMedia.DisposeWhenReleased(_directory);
        }

        /// <summary>Closes the session of the current test.</summary>
        [TearDown]
        public void TearDown()
        {
            _session?.Dispose();
            _session = null;
        }

        /// <summary>Starts a session over the fixture clip.</summary>
        /// <param name="capacity">The requested queue capacity.</param>
        /// <param name="rate">The initial playback rate.</param>
        /// <returns>The started session.</returns>
        private VideoDecodeSession Start(int capacity = 3, double rate = 1)
        {
            _session = new VideoDecodeSession(_clip, new DecoderOptions(), capacity, rate);
            return _session;
        }

        /// <summary>Checks that an invalid initial rate is rejected before the worker starts.</summary>
        [Test]
        public void Constructor_RejectsInvalidRate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoDecodeSession(_clip, new DecoderOptions(), 3, 0));
        }

        /// <summary>Checks that the queue fills to its capacity and frames are taken in presentation order.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Queue_FillsToCapacityAndDeliversInOrder()
        {
            var session = Start(3);
            var watch = Stopwatch.StartNew();
            while (session.BufferedFrames < 3 && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            Assert.That(session.BufferedFrames, Is.EqualTo(3));
            Assert.That(session.Info, Is.Not.Null);
            Assert.That(session.Info!.Width, Is.EqualTo(FFmpegTestMedia.DefaultWidth));
            Assert.That(session.Error, Is.Null);
            Assert.That(session.EndOfStream, Is.False);
            Assert.That(session.NextPresentationTime, Is.EqualTo(0).Within(0.001));
            var previous = -1.0;
            for (var i = 0; i < 6; i++)
            {
                watch.Restart();
                DecodedVideoFrame? frame = null;
                while ((frame = session.TakeFrame()) == null && watch.Elapsed < Timeout)
                {
                    yield return null;
                }

                Assert.That(frame, Is.Not.Null);
                Assert.That(frame!.PresentationTime, Is.GreaterThan(previous));
                previous = frame.PresentationTime;
                frame.Dispose();
            }

            Assert.That(session.PlaybackPosition, Is.EqualTo(0));
        }

        /// <summary>Checks that the requested capacity is clamped to the supported range.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Capacity_IsClampedToOneThroughEight()
        {
            var session = Start(0);
            var watch = Stopwatch.StartNew();
            while (session.BufferedFrames < 1 && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.That(session.BufferedFrames, Is.EqualTo(1));
            session.Dispose();
            session = Start(20);
            watch.Restart();
            while (session.BufferedFrames < 8 && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.That(session.BufferedFrames, Is.EqualTo(8));
        }

        /// <summary>Checks that taking the latest due frame releases older due frames and keeps future ones.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator TakeLatestFrame_ReturnsNewestDueFrame()
        {
            var session = Start(4);
            var watch = Stopwatch.StartNew();
            while (session.BufferedFrames < 4 && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            using (var none = session.TakeLatestFrame(-1))
            {
                Assert.That(none, Is.Null);
            }

            using (var latest = session.TakeLatestFrame(0.07))
            {
                Assert.That(latest!.PresentationTime, Is.EqualTo(2 / 30.0).Within(0.002));
            }

            Assert.That(session.NextPresentationTime, Is.EqualTo(3 / 30.0).Within(0.002));
        }

        /// <summary>Checks that seeking discards queued frames and resumes decoding at the target.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Seek_ReplacesQueuedFrames()
        {
            var session = Start(3);
            var watch = Stopwatch.StartNew();
            while (session.BufferedFrames < 3 && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            session.Seek(0.5);

            Assert.That(session.PlaybackPosition, Is.EqualTo(0.5));
            watch.Restart();
            DecodedVideoFrame? frame = null;
            while ((frame = session.TakeFrame()) == null && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            Assert.That(frame!.PresentationTime, Is.EqualTo(0.5).Within(0.034));
            frame.Dispose();
        }

        /// <summary>Checks that the worker reaches the end of the stream once every frame has been consumed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EndOfStream_AfterAllFramesAreTaken()
        {
            var session = Start(8);
            var count = 0;
            var watch = Stopwatch.StartNew();
            while (!session.EndOfStream && watch.Elapsed < Timeout)
            {
                using (var frame = session.TakeFrame())
                {
                    if (frame != null)
                    {
                        count++;
                    }
                }

                yield return null;
            }

            Assert.That(session.EndOfStream, Is.True);
            Assert.That(count, Is.EqualTo(30));
            Assert.That(double.IsNaN(session.NextPresentationTime), Is.True);
        }

        /// <summary>Checks that fast playback lets the worker replace superseded frames and finish the stream.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Playback_FastClockSupersedesFrames()
        {
            var session = Start(2, 16);
            var watch = Stopwatch.StartNew();
            while (session.Info == null && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            session.SetPlayback(position: 0, playing: true);
            watch.Restart();
            while (!session.EndOfStream && watch.Elapsed < Timeout)
            {
                using (session.TakeLatestFrame(session.PlaybackPosition))
                {
                }

                yield return null;
            }

            Assert.That(session.EndOfStream, Is.True);
            Assert.That(session.PlaybackPosition, Is.GreaterThan(0.5));
            Assert.That(session.DiscardedFrames, Is.GreaterThanOrEqualTo(0));
            Assert.That(session.CatchUpSeeks, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>Checks the playback clock controls exposed by the session.</summary>
        [Test]
        public void SetPlayback_ControlsSharedClock()
        {
            var session = Start(1);

            session.SetPlayback(position: 2);
            Assert.That(session.PlaybackPosition, Is.EqualTo(2));
            session.SetPlayback(rate: 2, playing: true);
            var spin = Stopwatch.StartNew();
            while (spin.ElapsedMilliseconds < 20)
            {
            }

            Assert.That(session.PlaybackPosition, Is.GreaterThan(2.02));
            session.SetPlayback(playing: false);
            var paused = session.PlaybackPosition;
            Assert.That(session.PlaybackPosition, Is.EqualTo(paused));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.SetPlayback(rate: 100));
        }

        /// <summary>Checks that a missing input is reported as the session error.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MissingInput_ReportsError()
        {
            _session = new VideoDecodeSession(FFmpegTestMedia.PathIn(_directory, "missing.mkv"), new DecoderOptions(), 3);
            var watch = Stopwatch.StartNew();
            while (_session.Error == null && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            Assert.That(_session.Error, Is.InstanceOf<InvalidOperationException>());
            Assert.That(_session.Info, Is.Null);
            Assert.That(_session.BufferedFrames, Is.EqualTo(0));
        }

        /// <summary>Checks that a closed session ignores playback changes, rejects seeks and releases its frames.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dispose_ReleasesFramesAndRejectsSeeks()
        {
            var session = Start(3);
            var watch = Stopwatch.StartNew();
            while (session.BufferedFrames < 3 && watch.Elapsed < Timeout)
            {
                yield return null;
            }

            session.Dispose();
            session.Dispose();

            Assert.That(session.BufferedFrames, Is.EqualTo(0));
            Assert.That(session.TakeFrame(), Is.Null);
            Assert.DoesNotThrow(() => session.SetPlayback(position: 1, rate: 2, playing: true));
            Assert.Throws<ObjectDisposedException>(() => session.Seek(0));
            Assert.That(session.Error, Is.Null);
        }
    }
}
