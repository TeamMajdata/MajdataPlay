using System;
using System.Threading;
using MajdataPlay.Tests;
using MajdataPlay.Timer;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Timer
{
    /// <summary>Covers <see cref="MajTimeline"/>, <see cref="MajTimer"/> and the built-in time providers.</summary>
    public class MajTimelineTests
    {
        /// <summary>A time provider whose ticks are set by the test.</summary>
        private sealed class ManualTimeProvider : ITimeProvider
        {
            /// <summary>Gets the provider kind reported to callers.</summary>
            public BuiltInTimeProvider Type => BuiltInTimeProvider.Stopwatch;

            /// <summary>Gets or sets the current ticks.</summary>
            public long Ticks { get; set; }

            /// <summary>Gets the number of pre-update notifications received.</summary>
            public int PreUpdateCount { get; private set; }

            /// <summary>Counts the pre-update notification.</summary>
            public void OnPreUpdate()
            {
                PreUpdateCount++;
            }
        }

        /// <summary>The provider installed before the test.</summary>
        private ITimeProvider _savedProvider = null!;

        /// <summary>The time scale installed before the test.</summary>
        private float _savedTimeScale;

        /// <summary>The last update time stored before the test.</summary>
        private TimeSpan _savedLastUpdateTime;

        /// <summary>The provider driven by the test.</summary>
        private ManualTimeProvider _provider = null!;

        /// <summary>Installs a manual time provider.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedProvider = MajTimeline.TimeProvider;
            _savedTimeScale = MajTimeline.TimeScale;
            _savedLastUpdateTime = PrivateMember.GetStaticField<TimeSpan>(typeof(MajTimeline), "_lastUpdateTime");
            _provider = new ManualTimeProvider();
            MajTimeline.TimeProvider = _provider;
            MajTimeline.TimeScale = 1f;
        }

        /// <summary>Restores the timeline state.</summary>
        [TearDown]
        public void TearDown()
        {
            MajTimeline.TimeProvider = _savedProvider;
            MajTimeline.TimeScale = _savedTimeScale;
            PrivateMember.SetStaticField(typeof(MajTimeline), "_lastUpdateTime", _savedLastUpdateTime);
        }

        /// <summary>Checks that the timeline reads its time from the installed provider and applies the time scale.</summary>
        [Test]
        public void Time_FollowsProviderAndScale()
        {
            _provider.Ticks = TimeSpan.FromSeconds(4).Ticks;

            Assert.That(MajTimeline.TimeProvider, Is.SameAs(_provider));
            Assert.That(MajTimeline.UnscaledTime, Is.EqualTo(TimeSpan.FromSeconds(4)));

            MajTimeline.TimeScale = 0.5f;

            Assert.That(MajTimeline.Time, Is.EqualTo(TimeSpan.FromSeconds(2)));
        }

        /// <summary>Checks that the pre-update step measures the frame delta and counts frames.</summary>
        [Test]
        public void OnPreUpdate_ComputesDeltaAndCountsFrames()
        {
            _provider.Ticks = TimeSpan.FromSeconds(1).Ticks;
            MajTimeline.OnPreUpdate();
            var frame = MajTimeline.FrameCount;

            _provider.Ticks = TimeSpan.FromSeconds(1.5).Ticks;
            MajTimeline.OnPreUpdate();

            Assert.That(_provider.PreUpdateCount, Is.EqualTo(2));
            Assert.That(MajTimeline.FrameCount, Is.EqualTo(frame + 1));
            Assert.That(MajTimeline.UnscaledDeltaTime, Is.EqualTo(0.5f).Within(1e-5f));
            MajTimeline.TimeScale = 2f;
            Assert.That(MajTimeline.DeltaTime, Is.EqualTo(0.25f).Within(1e-5f));
        }

        /// <summary>Checks the fixed delta time accessors, which forward to Unity's time settings.</summary>
        [Test]
        public void FixedDeltaTime_ForwardsToUnity()
        {
            var saved = UnityEngine.Time.fixedDeltaTime;
            try
            {
                MajTimeline.FixedDeltaTime = 0.01f;

                Assert.That(UnityEngine.Time.fixedDeltaTime, Is.EqualTo(0.01f).Within(1e-6f));
                Assert.That(MajTimeline.FixedDeltaTime, Is.EqualTo(0.01f).Within(1e-6f));
                Assert.That(MajTimeline.FixedUnscaledDeltaTime, Is.EqualTo(UnityEngine.Time.fixedUnscaledDeltaTime));
            }
            finally
            {
                UnityEngine.Time.fixedDeltaTime = saved;
            }
        }

        /// <summary>Checks that a timer measures time from its creation and applies its own time scale.</summary>
        [Test]
        public void CreateTimer_MeasuresElapsedTime()
        {
            _provider.Ticks = TimeSpan.FromSeconds(10).Ticks;
            var before = DateTime.Now;
            var timer = MajTimeline.CreateTimer();

            _provider.Ticks = TimeSpan.FromSeconds(12).Ticks;

            Assert.That(timer.StartAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(timer.TimeScale, Is.EqualTo(1d));
            Assert.That(timer.UnscaledElapsedTime, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(timer.UnscaledElapsedSeconds, Is.EqualTo(2d));
            Assert.That(timer.UnscaledElapsedSecondsAsFloat, Is.EqualTo(2f));
            Assert.That(timer.UnscaledElapsedMilliseconds, Is.EqualTo(2000d));
            Assert.That(timer.UnscaledElapsedMillisecondsAsFloat, Is.EqualTo(2000f));
            Assert.That(timer.UnscaledElapsedTicks, Is.EqualTo(TimeSpan.FromSeconds(2).Ticks));

            timer.TimeScale = 2;

            Assert.That(timer.ElapsedTime, Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(timer.ElapsedSeconds, Is.EqualTo(4d));
            Assert.That(timer.ElapsedSecondsAsFloat, Is.EqualTo(4f));
            Assert.That(timer.ElapsedMilliseconds, Is.EqualTo(4000d));
            Assert.That(timer.ElapsedMillisecondsAsFloat, Is.EqualTo(4000f));
            Assert.That(timer.ElapsedTicks, Is.EqualTo(TimeSpan.FromSeconds(4).Ticks));
        }

        /// <summary>Checks that the built-in providers are listed in enum order.</summary>
        [Test]
        public void BuiltInTimeProviders_ListEveryKind()
        {
            var providers = MajTimeline.BuiltInTimeProviders.Span;

            Assert.That(providers.Length, Is.EqualTo(3));
            Assert.That(providers[0].Type, Is.EqualTo(BuiltInTimeProvider.Unity));
            Assert.That(providers[1].Type, Is.EqualTo(BuiltInTimeProvider.Winapi));
            Assert.That(providers[2].Type, Is.EqualTo(BuiltInTimeProvider.Stopwatch));
        }

        /// <summary>Checks that the stopwatch provider only advances when it is updated.</summary>
        [Test]
        public void StopwatchProvider_AdvancesOnUpdate()
        {
            var provider = new StopwatchTimeProvider();

            Assert.That(provider.Ticks, Is.EqualTo(0));
            Thread.Sleep(5);
            provider.OnPreUpdate();

            Assert.That(provider.Ticks, Is.GreaterThan(0));
            Assert.That(provider.Type, Is.EqualTo(BuiltInTimeProvider.Stopwatch));
        }

        /// <summary>Checks that the Windows provider measures non-negative elapsed file time.</summary>
        [Test]
        public void WinapiProvider_MeasuresElapsedTime()
        {
            var provider = new WinapiTimeProvider();

            Thread.Sleep(5);
            provider.OnPreUpdate();
            var first = provider.Ticks;
            Thread.Sleep(5);
            provider.OnPreUpdate();

            Assert.That(provider.Type, Is.EqualTo(BuiltInTimeProvider.Winapi));
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            Assert.That(provider.Ticks, Is.GreaterThanOrEqualTo(first));
        }

        /// <summary>Checks that the Unity provider converts Unity's unscaled time into ticks.</summary>
        [Test]
        public void UnityProvider_ReadsUnscaledTime()
        {
            var provider = new UnityTimeProvider();

            provider.OnPreUpdate();

            Assert.That(provider.Type, Is.EqualTo(BuiltInTimeProvider.Unity));
            Assert.That(provider.Ticks, Is.GreaterThanOrEqualTo(0));
            Assert.That(TimeSpan.FromTicks(provider.Ticks).TotalSeconds, Is.EqualTo(UnityEngine.Time.unscaledTimeAsDouble).Within(1d));
        }
    }
}
