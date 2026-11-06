using MajdataPlay.Diagnostics;
using MajSimai.Logging;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Diagnostics
{
    /// <summary>Verifies how <see cref="MajDebugSimaiLogger"/> maps MajSimai log levels to <see cref="MajDebug"/>.</summary>
    public class MajDebugSimaiLoggerTests
    {
        /// <summary>The minimum level of <see cref="MajDebug"/> before the test.</summary>
        private LogLevel _minLogLevel;

        /// <summary>The MajSimai logger before the test.</summary>
        private ISimaiLogger _simaiLogger = null!;

        /// <summary>Remembers the global logger state that the tests change.</summary>
        [SetUp]
        public void SetUp()
        {
            _minLogLevel = MajDebug.MinLogLevel;
            _simaiLogger = SimaiLogger.Current;
        }

        /// <summary>Restores the global logger state.</summary>
        [TearDown]
        public void TearDown()
        {
            MajDebug.MinLogLevel = _minLogLevel;
            SimaiLogger.Current = _simaiLogger;
        }

        /// <summary>Checks that every MajSimai level that carries messages maps to the MajDebug level of the same name.</summary>
        /// <param name="level">The MajSimai level.</param>
        /// <param name="expected">The expected MajDebug level.</param>
        [TestCase(SimaiLogLevel.Debug, LogLevel.Debug)]
        [TestCase(SimaiLogLevel.Info, LogLevel.Info)]
        [TestCase(SimaiLogLevel.Warning, LogLevel.Warning)]
        [TestCase(SimaiLogLevel.Error, LogLevel.Error)]
        public void TryGetLogLevel_MapsLevels(SimaiLogLevel level, LogLevel expected)
        {
            var isMapped = MajDebugSimaiLogger.TryGetLogLevel(level, out var logLevel);

            Assert.That(isMapped, Is.True);
            Assert.That(logLevel, Is.EqualTo(expected));
        }

        /// <summary>Checks that <see cref="SimaiLogLevel.None"/> and unknown values are never written.</summary>
        /// <param name="level">The level that has no MajDebug level.</param>
        [TestCase(SimaiLogLevel.None)]
        [TestCase((SimaiLogLevel)42)]
        public void TryGetLogLevel_RejectsLevelsWithoutMessages(SimaiLogLevel level)
        {
            Assert.That(MajDebugSimaiLogger.TryGetLogLevel(level, out _), Is.False);
            MajDebug.MinLogLevel = LogLevel.Debug;
            Assert.That(MajDebugSimaiLogger.Instance.IsEnabled(level), Is.False);
        }

        /// <summary>Checks that only levels at or above the configured MajDebug level are enabled.</summary>
        [Test]
        public void IsEnabled_FollowsMinLogLevel()
        {
            var logger = MajDebugSimaiLogger.Instance;

            MajDebug.MinLogLevel = LogLevel.Warning;

            Assert.That(logger.IsEnabled(SimaiLogLevel.Debug), Is.False);
            Assert.That(logger.IsEnabled(SimaiLogLevel.Info), Is.False);
            Assert.That(logger.IsEnabled(SimaiLogLevel.Warning), Is.True);
            Assert.That(logger.IsEnabled(SimaiLogLevel.Error), Is.True);

            MajDebug.MinLogLevel = LogLevel.Debug;

            Assert.That(logger.IsEnabled(SimaiLogLevel.Debug), Is.True);
            Assert.That(logger.IsEnabled(SimaiLogLevel.Info), Is.True);

            MajDebug.MinLogLevel = LogLevel.Fatal;

            Assert.That(logger.IsEnabled(SimaiLogLevel.Error), Is.False);
        }

        /// <summary>Checks that installing the logger makes it the logger of MajSimai.</summary>
        [Test]
        public void Install_SetsCurrentLogger()
        {
            MajDebugSimaiLogger.Install();

            Assert.That(SimaiLogger.Current, Is.SameAs(MajDebugSimaiLogger.Instance));
        }
    }
}
