using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>
    /// Covers <see cref="AndroidLogger"/> on a Windows Editor: the constructor tags, the composition of an entry from its
    /// tag, exception, message and arguments, and the level mapping of the path that writes to the Unity log. The entries
    /// are observed through <see cref="Application.logMessageReceived"/>, which receives everything the logger writes
    /// outside an Android player. The path that writes to logcat only exists inside an Android player and is not
    /// exercised here.
    /// </summary>
    public class AndroidLoggerTests
    {
        /// <summary>The entries received from the Unity log while the test runs.</summary>
        private readonly List<(LogType Type, string Message)> _entries = new();

        /// <summary>A value indicating whether failing log messages were ignored before the test.</summary>
        private bool _savedIgnoreFailingMessages;

        /// <summary>Records one entry received from the Unity log.</summary>
        /// <param name="condition">The message of the entry.</param>
        /// <param name="stackTrace">The stack trace of the entry, which is ignored.</param>
        /// <param name="type">The Unity log type of the entry.</param>
        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            _entries.Add((type, condition));
        }

        /// <summary>Subscribes to the Unity log and keeps error-level entries from failing the test.</summary>
        [SetUp]
        public void SetUp()
        {
            _entries.Clear();
            _savedIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLogMessageReceived;
        }

        /// <summary>Restores the failing-message setting and unsubscribes from the Unity log.</summary>
        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            LogAssert.ignoreFailingMessages = _savedIgnoreFailingMessages;
        }

        /// <summary>Gets the single entry the test wrote.</summary>
        /// <returns>The recorded log type and message.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the test wrote no entry or more than one.</exception>
        private (LogType Type, string Message) SingleEntry()
        {
            return _entries.Single();
        }

        /// <summary>Checks that the parameterless constructor uses the default logcat tag.</summary>
        [Test]
        public void Constructor_WithoutTag_UsesDefaultTag()
        {
            var logger = new AndroidLogger();

            Assert.That(logger.Tag, Is.EqualTo(AndroidLogger.DefaultTag));
            Assert.That(logger.Tag, Is.EqualTo("MajdataPlay"));
        }

        /// <summary>Checks that the tag passed to the constructor becomes the logcat tag.</summary>
        [Test]
        public void Constructor_WithTag_UsesGivenTag()
        {
            var logger = new AndroidLogger("MajdataPlay.Tests");

            Assert.That(logger.Tag, Is.EqualTo("MajdataPlay.Tests"));
        }

        /// <summary>Checks that a missing tag is reported instead of being accepted.</summary>
        [Test]
        public void Constructor_WithNullTag_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AndroidLogger(null!));
        }

        /// <summary>Checks that an entry tag is prefixed to the message instead of replacing the logcat tag.</summary>
        [Test]
        public void Log_TaggedEntry_PrefixesEntryTagAndKeepsLoggerTag()
        {
            var logger = new AndroidLogger("MajdataPlay");

            logger.Log(LogLevel.Info, "Audio", "decoder ready");

            var entry = SingleEntry();
            Assert.That(logger.Tag, Is.EqualTo("MajdataPlay"));
            Assert.That(entry.Message, Is.EqualTo("[Audio]decoder ready"));
            Assert.That(entry.Type, Is.EqualTo(LogType.Log));
        }

        /// <summary>Checks that an untagged entry is written without a prefix.</summary>
        [Test]
        public void Log_Untagged_WritesMessageAsIs()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Info, "plain");

            Assert.That(SingleEntry().Message, Is.EqualTo("plain"));
        }

        /// <summary>Checks that the message is formatted with the given arguments.</summary>
        [Test]
        public void Log_WithArgs_FormatsMessage()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Warning, "Audio", "{0} notes loaded", 42);

            Assert.That(SingleEntry().Message, Is.EqualTo("[Audio]42 notes loaded"));
        }

        /// <summary>Checks that an empty argument array leaves the message unformatted, so braces stay literal.</summary>
        [Test]
        public void Log_WithEmptyArgs_KeepsBracesLiteral()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Info, "literal {braces} stay intact", Array.Empty<object>());

            Assert.That(SingleEntry().Message, Is.EqualTo("literal {braces} stay intact"));
        }

        /// <summary>Checks that the exception text follows the message on its own lines.</summary>
        [Test]
        public void Log_WithException_AppendsExceptionText()
        {
            var logger = new AndroidLogger();
            var exception = new InvalidOperationException("boom");

            logger.Log(LogLevel.Error, exception, "failed");

            var message = SingleEntry().Message;
            Assert.That(message, Does.StartWith("failed" + Environment.NewLine));
            Assert.That(message, Does.Contain("System.InvalidOperationException: boom"));
        }

        /// <summary>Checks that the entry tag stays in front of the message when an exception is attached.</summary>
        [Test]
        public void Log_TaggedEntryWithException_PrefixesTagBeforeMessage()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Fatal, "Net", new TimeoutException("late"), "timeout");

            var message = SingleEntry().Message;
            Assert.That(message, Does.StartWith("[Net]timeout" + Environment.NewLine));
            Assert.That(message, Does.Contain("System.TimeoutException: late"));
        }

        /// <summary>Checks the mapping from MajdataPlay levels to Unity log types.</summary>
        /// <param name="level">The MajdataPlay level.</param>
        /// <param name="expected">The expected Unity log type.</param>
        [TestCase(LogLevel.Debug, LogType.Log)]
        [TestCase(LogLevel.Info, LogType.Log)]
        [TestCase(LogLevel.Warning, LogType.Warning)]
        [TestCase(LogLevel.Error, LogType.Error)]
        [TestCase(LogLevel.Fatal, LogType.Error)]
        public void Log_MapsLevelsToUnityLogTypes(LogLevel level, LogType expected)
        {
            var logger = new AndroidLogger();

            logger.Log(level, "message");

            Assert.That(SingleEntry().Type, Is.EqualTo(expected));
        }

        /// <summary>Checks that an undefined level cannot be written.</summary>
        [Test]
        public void Log_UndefinedLevel_Throws()
        {
            var logger = new AndroidLogger();

            Assert.Throws<ArgumentOutOfRangeException>(() => logger.Log((LogLevel)42, "message"));
        }
    }
}
