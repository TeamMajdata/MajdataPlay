using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Diagnostics;
using MajdataPlay.Platform.Android;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>
    /// Covers <see cref="AndroidLogger"/> on a Windows Editor: the constructor tags, the composition of an entry from its
    /// tag, exception, message and arguments, and the level mapping of the path that writes to the Unity log. The path that
    /// writes to logcat only exists inside an Android player and is not exercised here.
    /// </summary>
    public class AndroidLoggerTests
    {
        /// <summary>The Unity logger in place before the test.</summary>
        private ILogger? _savedLogger;

        /// <summary>The Unity log handler that records the entries written during the test.</summary>
        private RecordingUnityLogHandler _handler = null!;

        /// <summary>A Unity log handler that records formatted messages instead of printing them.</summary>
        private sealed class RecordingUnityLogHandler : ILogHandler
        {
            /// <summary>Gets the recorded log types and messages, in call order.</summary>
            public List<(LogType Type, string Message)> Entries { get; } = new();

            /// <summary>Records a formatted message.</summary>
            /// <param name="logType">The Unity log type.</param>
            /// <param name="context">The context object, which is ignored.</param>
            /// <param name="format">The composite format string.</param>
            /// <param name="args">The format arguments.</param>
            public void LogFormat(LogType logType, Object? context, string format, params object[] args)
            {
                Entries.Add((logType, string.Format(format, args)));
            }

            /// <summary>Records an exception as an exception entry.</summary>
            /// <param name="exception">The exception.</param>
            /// <param name="context">The context object, which is ignored.</param>
            public void LogException(Exception exception, Object? context)
            {
                Entries.Add((LogType.Exception, exception.ToString()));
            }
        }

        /// <summary>Replaces the Unity logger with one that records the entries.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedLogger = Debug.unityLogger;
            _handler = new RecordingUnityLogHandler();
            Debug.unityLogger = new Logger(_handler);
        }

        /// <summary>Restores the Unity logger in place before the test.</summary>
        [TearDown]
        public void TearDown()
        {
            if (_savedLogger is not null)
            {
                Debug.unityLogger = _savedLogger;
            }
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

            Assert.That(logger.Tag, Is.EqualTo("MajdataPlay"));
            Assert.That(_handler.Entries.Single().Message, Is.EqualTo("[Audio]decoder ready"));
            Assert.That(_handler.Entries.Single().Type, Is.EqualTo(LogType.Log));
        }

        /// <summary>Checks that an untagged entry is written without a prefix.</summary>
        [Test]
        public void Log_Untagged_WritesMessageAsIs()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Info, "plain");

            Assert.That(_handler.Entries.Single().Message, Is.EqualTo("plain"));
        }

        /// <summary>Checks that the message is formatted with the given arguments.</summary>
        [Test]
        public void Log_WithArgs_FormatsMessage()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Warning, "Audio", "{0} notes loaded", 42);

            Assert.That(_handler.Entries.Single().Message, Is.EqualTo("[Audio]42 notes loaded"));
        }

        /// <summary>Checks that an empty argument array leaves the message unformatted, so braces stay literal.</summary>
        [Test]
        public void Log_WithEmptyArgs_KeepsBracesLiteral()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Info, "literal {braces} stay intact", Array.Empty<object>());

            Assert.That(_handler.Entries.Single().Message, Is.EqualTo("literal {braces} stay intact"));
        }

        /// <summary>Checks that the exception text follows the message on its own lines.</summary>
        [Test]
        public void Log_WithException_AppendsExceptionText()
        {
            var logger = new AndroidLogger();
            var exception = new InvalidOperationException("boom");

            logger.Log(LogLevel.Error, exception, "failed");

            var message = _handler.Entries.Single().Message;
            Assert.That(message, Does.StartWith("failed" + Environment.NewLine));
            Assert.That(message, Does.Contain("System.InvalidOperationException: boom"));
        }

        /// <summary>Checks that the entry tag stays in front of the message when an exception is attached.</summary>
        [Test]
        public void Log_TaggedEntryWithException_PrefixesTagBeforeMessage()
        {
            var logger = new AndroidLogger();

            logger.Log(LogLevel.Fatal, "Net", new TimeoutException("late"), "timeout");

            var message = _handler.Entries.Single().Message;
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

            Assert.That(_handler.Entries.Single().Type, Is.EqualTo(expected));
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
