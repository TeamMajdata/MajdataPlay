using MajdataPlay.FFmpeg.Internal;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the one-second compressed bit-rate estimator used by the decoder and encoder.</summary>
    public class VideoBitRateTrackerTests
    {
        /// <summary>Checks that an empty tracker reports zero.</summary>
        [Test]
        public void Measure_WithoutSamples_ReturnsZero()
        {
            var tracker = new VideoBitRateTracker();

            Assert.That(tracker.Measure(1), Is.EqualTo(0));
        }

        /// <summary>Checks a steady stream of packets over a full window.</summary>
        [Test]
        public void Measure_SteadyStream_ReportsBitsPerSecond()
        {
            var tracker = new VideoBitRateTracker();
            for (var i = 0; i < 30; i++)
            {
                tracker.Add(i / 30.0, 1 / 30.0, 1000);
            }

            Assert.That(tracker.Measure(1.0), Is.EqualTo(240000).Within(1));
        }

        /// <summary>Checks that a partial history is measured over the covered time only.</summary>
        [Test]
        public void Measure_PartialHistory_UsesCoveredSeconds()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(0, 0.5, 1000);

            Assert.That(tracker.Measure(0.5), Is.EqualTo(16000));
        }

        /// <summary>Checks that a packet crossing the window start contributes proportionally.</summary>
        [Test]
        public void Measure_PacketCrossingWindow_ContributesProportionally()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(0, 1, 1000);
            tracker.Add(1, 1, 1000);

            Assert.That(tracker.Measure(1.5), Is.EqualTo(8000));
        }

        /// <summary>Checks that out-of-order packet timestamps are measured correctly.</summary>
        [Test]
        public void Measure_OutOfOrderPackets_AreAccepted()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(0.5, 0.5, 500);
            tracker.Add(0, 0.5, 500);

            Assert.That(tracker.Measure(1), Is.EqualTo(8000));
        }

        /// <summary>Checks that invalid samples are ignored.</summary>
        [Test]
        public void Add_IgnoresInvalidSamples()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(double.NaN, 1, 100);
            tracker.Add(0, double.PositiveInfinity, 100);
            tracker.Add(0, 0, 100);
            tracker.Add(0, -1, 100);
            tracker.Add(0, 1, 0);
            tracker.Add(0, 1, -5);
            tracker.Add(double.MaxValue, double.MaxValue, 100);
            tracker.Add(1e20, 1e-10, 100);

            Assert.That(tracker.Measure(1), Is.EqualTo(0));
        }

        /// <summary>Checks that non-finite frame ends report zero.</summary>
        [Test]
        public void Measure_NonFiniteFrameEnd_ReturnsZero()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(0, 1, 100);

            Assert.That(tracker.Measure(double.NaN), Is.EqualTo(0));
            Assert.That(tracker.Measure(double.PositiveInfinity), Is.EqualTo(0));
        }

        /// <summary>Checks that a frame end before all samples reports zero covered time.</summary>
        [Test]
        public void Measure_BeforeFirstSample_ReturnsZero()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(5, 1, 100);

            Assert.That(tracker.Measure(4), Is.EqualTo(0));
        }

        /// <summary>Checks that samples older than the window are dropped and yield zero.</summary>
        [Test]
        public void Measure_ExpiredSamples_AreDiscarded()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(0, 0.5, 1000);

            Assert.That(tracker.Measure(3), Is.EqualTo(0));
            Assert.That(tracker.Measure(0.5), Is.EqualTo(0));
        }

        /// <summary>Checks that Reset discards history.</summary>
        [Test]
        public void Reset_DiscardsHistory()
        {
            var tracker = new VideoBitRateTracker();
            tracker.Add(0, 1, 1000);

            tracker.Reset();

            Assert.That(tracker.Measure(1), Is.EqualTo(0));
            tracker.Add(1, 1, 1000);
            Assert.That(tracker.Measure(2), Is.EqualTo(8000));
        }

        /// <summary>Checks that capacity overflow marks the window incomplete until it has passed.</summary>
        [Test]
        public void Add_CapacityOverflow_ReportsIncompleteWindowAsZero()
        {
            var tracker = new VideoBitRateTracker();
            for (var i = 0; i < 4097; i++)
            {
                tracker.Add(i * 0.0001, 0.0001, 10);
            }

            Assert.That(tracker.Measure(0.4097), Is.EqualTo(0));
            tracker.Add(2, 1, 1000);
            Assert.That(tracker.Measure(3), Is.EqualTo(8000));
        }

        /// <summary>Checks that an extremely large estimate saturates at the maximum value.</summary>
        [Test]
        public void Measure_HugeRate_Saturates()
        {
            var tracker = new VideoBitRateTracker();
            for (var i = 0; i < 4000; i++)
            {
                tracker.Add(0, 1e-300, int.MaxValue);
            }

            Assert.That(tracker.Measure(1e-300), Is.EqualTo(long.MaxValue));
        }
    }
}
