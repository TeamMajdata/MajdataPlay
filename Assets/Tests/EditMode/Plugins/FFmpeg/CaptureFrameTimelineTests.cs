using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies how camera renders are mapped to recording frame timestamps.</summary>
    public class CaptureFrameTimelineTests
    {
        /// <summary>Assigns a render that must be recorded and returns its frame index.</summary>
        /// <param name="timeline">The timeline under test.</param>
        /// <param name="position">The render time in frame periods.</param>
        /// <returns>The assigned frame index.</returns>
        private static long Assign(CaptureFrameTimeline timeline, double position)
        {
            Assert.That(timeline.TryAssign(position, out var frame), Is.True, "Render at " + position + " was rejected.");
            return frame;
        }

        /// <summary>Checks the state of a new timeline.</summary>
        [Test]
        public void NewTimeline_HasNoFrames()
        {
            var timeline = new CaptureFrameTimeline();

            Assert.That(timeline.LastFrameIndex, Is.EqualTo(-1));
            Assert.That(timeline.SourceGapFrames, Is.EqualTo(0));
            Assert.That(CaptureFrameTimeline.Tolerance, Is.EqualTo(0.75));
        }

        /// <summary>Checks that the first render is rounded to the nearest frame and earlier frames count as gaps.</summary>
        /// <param name="position">The first render time.</param>
        /// <param name="expected">The expected frame index.</param>
        [TestCase(0.0, 0L)]
        [TestCase(0.49, 0L)]
        [TestCase(0.5, 1L)]
        [TestCase(2.6, 3L)]
        public void FirstRender_RoundsToNearestFrame(double position, long expected)
        {
            var timeline = new CaptureFrameTimeline();

            Assert.That(Assign(timeline, position), Is.EqualTo(expected));
            Assert.That(timeline.LastFrameIndex, Is.EqualTo(expected));
            Assert.That(timeline.SourceGapFrames, Is.EqualTo(expected));
        }

        /// <summary>Checks that jittered renders near frame boundaries still produce consecutive frames without gaps.</summary>
        [Test]
        public void JitteredRenders_StayConsecutive()
        {
            var timeline = new CaptureFrameTimeline();
            var positions = new[] { 0.02, 0.97, 2.04, 2.96, 4.05, 4.98 };

            for (var i = 0; i < positions.Length; i++)
            {
                Assert.That(Assign(timeline, positions[i]), Is.EqualTo(i));
            }

            Assert.That(timeline.SourceGapFrames, Is.EqualTo(0));
        }

        /// <summary>Checks that a render more than the tolerance late resynchronizes and counts the skipped periods.</summary>
        [Test]
        public void LateRender_ResynchronizesAndCountsGap()
        {
            var timeline = new CaptureFrameTimeline();
            Assign(timeline, 0);

            Assert.That(Assign(timeline, 3.8), Is.EqualTo(4));
            Assert.That(timeline.SourceGapFrames, Is.EqualTo(3));
            Assert.That(Assign(timeline, 4.8), Is.EqualTo(5));
            Assert.That(timeline.SourceGapFrames, Is.EqualTo(3));
        }

        /// <summary>Checks that a render slightly late within the tolerance is recorded as the next frame.</summary>
        [Test]
        public void SlightlyLateRender_UsesNextFrame()
        {
            var timeline = new CaptureFrameTimeline();
            Assign(timeline, 0);

            Assert.That(Assign(timeline, 1.7), Is.EqualTo(1));
            Assert.That(timeline.SourceGapFrames, Is.EqualTo(0));
        }

        /// <summary>Checks that renders faster than the frame rate keep the render closest to each frame time.</summary>
        [Test]
        public void FastRenders_RejectEarlyRenders()
        {
            var timeline = new CaptureFrameTimeline();
            Assign(timeline, 0);

            Assert.That(timeline.TryAssign(0.5, out var rejected), Is.False);
            Assert.That(rejected, Is.EqualTo(0));
            Assert.That(Assign(timeline, 1.0), Is.EqualTo(1));
            Assert.That(timeline.TryAssign(1.5, out _), Is.False);
            Assert.That(Assign(timeline, 2.0), Is.EqualTo(2));
            Assert.That(timeline.SourceGapFrames, Is.EqualTo(0));
        }

        /// <summary>Checks that a render shortly before the next frame time is recorded as that frame.</summary>
        [Test]
        public void SecondRender_EarlyWithinToleranceIsAccepted()
        {
            var timeline = new CaptureFrameTimeline();
            Assign(timeline, 0);

            Assert.That(Assign(timeline, 0.9), Is.EqualTo(1));
        }

        /// <summary>Checks that a decreasing render time is treated as a zero interval rather than a negative one.</summary>
        [Test]
        public void DecreasingRender_IsRejectedAsEarly()
        {
            var timeline = new CaptureFrameTimeline();
            Assign(timeline, 5);

            Assert.That(timeline.TryAssign(4, out _), Is.False);
            Assert.That(timeline.LastFrameIndex, Is.EqualTo(5));
        }

        /// <summary>Checks that Reset forgets the previous recording.</summary>
        [Test]
        public void Reset_StartsNewRecording()
        {
            var timeline = new CaptureFrameTimeline();
            Assign(timeline, 0);
            Assign(timeline, 5);

            timeline.Reset();

            Assert.That(timeline.LastFrameIndex, Is.EqualTo(-1));
            Assert.That(timeline.SourceGapFrames, Is.EqualTo(0));
            Assert.That(Assign(timeline, 0.2), Is.EqualTo(0));
        }
    }
}
