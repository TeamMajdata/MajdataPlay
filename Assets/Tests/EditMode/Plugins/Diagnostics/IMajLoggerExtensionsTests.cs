using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Diagnostics;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Diagnostics
{
    /// <summary>
    /// Covers <see cref="IMajLoggerExtensions"/>: every convenience overload has to reach the matching
    /// <see cref="IMajLogger.Log(LogLevel, string)"/> overload and has to forward the level, the tag, the exception, the
    /// message and the format arguments it was given unchanged. The overload resolution checks also pin down that a tag
    /// followed by a message selects the tagged overload, and that an exception placed after the message stays a format
    /// argument, as in <c>Microsoft.Extensions.Logging.LoggerExtensions</c>.
    /// </summary>
    public class IMajLoggerExtensionsTests
    {
        /// <summary>The <see cref="IMajLogger"/> overload that a forwarded call reached.</summary>
        private enum CallShape
        {
            /// <summary>The untagged overload that takes no format arguments.</summary>
            Message,

            /// <summary>The untagged overload that takes format arguments.</summary>
            MessageArgs,

            /// <summary>The untagged overload that takes an exception and format arguments.</summary>
            MessageExceptionArgs,

            /// <summary>The tagged overload that takes no format arguments.</summary>
            TagMessage,

            /// <summary>The tagged overload that takes format arguments.</summary>
            TagMessageArgs,

            /// <summary>The tagged overload that takes an exception and format arguments.</summary>
            TagMessageExceptionArgs
        }

        /// <summary>An <see cref="IMajLogger"/> that records the arguments of every call instead of writing it.</summary>
        private sealed class RecordingMajLogger : IMajLogger
        {
            /// <summary>Gets the recorded calls, in call order.</summary>
            public List<(LogLevel Level, CallShape Shape, string? Tag, Exception? Exception, string Message, object?[]? Args)> Calls { get; } = new();

            /// <summary>Records a call that reached the untagged overload without format arguments.</summary>
            /// <param name="level">The level of the entry.</param>
            /// <param name="message">The message of the entry.</param>
            public void Log(LogLevel level, string message)
            {
                Calls.Add((level, CallShape.Message, null, null, message, null));
            }

            /// <summary>Records a call that reached the untagged overload with format arguments.</summary>
            /// <param name="level">The level of the entry.</param>
            /// <param name="message">The composite format string of the entry.</param>
            /// <param name="args">The format arguments of the entry.</param>
            public void Log(LogLevel level, string message, params object?[] args)
            {
                Calls.Add((level, CallShape.MessageArgs, null, null, message, args));
            }

            /// <summary>Records a call that reached the untagged overload with an exception and format arguments.</summary>
            /// <param name="level">The level of the entry.</param>
            /// <param name="exception">The exception that caused the entry.</param>
            /// <param name="message">The composite format string of the entry.</param>
            /// <param name="args">The format arguments of the entry.</param>
            public void Log(LogLevel level, Exception? exception, string message, params object?[] args)
            {
                Calls.Add((level, CallShape.MessageExceptionArgs, null, exception, message, args));
            }

            /// <summary>Records a call that reached the tagged overload without format arguments.</summary>
            /// <param name="level">The level of the entry.</param>
            /// <param name="tag">The tag of the entry.</param>
            /// <param name="message">The message of the entry.</param>
            public void Log(LogLevel level, string tag, string message)
            {
                Calls.Add((level, CallShape.TagMessage, tag, null, message, null));
            }

            /// <summary>Records a call that reached the tagged overload with format arguments.</summary>
            /// <param name="level">The level of the entry.</param>
            /// <param name="tag">The tag of the entry.</param>
            /// <param name="message">The composite format string of the entry.</param>
            /// <param name="args">The format arguments of the entry.</param>
            public void Log(LogLevel level, string tag, string message, params object?[] args)
            {
                Calls.Add((level, CallShape.TagMessageArgs, tag, null, message, args));
            }

            /// <summary>Records a call that reached the tagged overload with an exception and format arguments.</summary>
            /// <param name="level">The level of the entry.</param>
            /// <param name="tag">The tag of the entry.</param>
            /// <param name="exception">The exception that caused the entry.</param>
            /// <param name="message">The composite format string of the entry.</param>
            /// <param name="args">The format arguments of the entry.</param>
            public void Log(LogLevel level, string tag, Exception? exception, string message, params object?[] args)
            {
                Calls.Add((level, CallShape.TagMessageExceptionArgs, tag, exception, message, args));
            }
        }

        /// <summary>Checks that each of the five levels exposes the six overloads and that each one reaches its counterpart.</summary>
        [Test]
        public void AllOverloads_ForwardLevelAndOverload()
        {
            var logger = new RecordingMajLogger();
            var exception = new InvalidOperationException("boom");

            logger.LogDebug("debug");
            logger.LogDebug("debug {0}", 1);
            logger.LogDebug(exception, "debug");
            logger.LogDebug("Debug", "debug");
            logger.LogDebug("Debug", "debug {0}", 1);
            logger.LogDebug("Debug", exception, "debug");
            logger.LogInfo("info");
            logger.LogInfo("info {0}", 2);
            logger.LogInfo(exception, "info");
            logger.LogInfo("Info", "info");
            logger.LogInfo("Info", "info {0}", 2);
            logger.LogInfo("Info", exception, "info");
            logger.LogWarning("warning");
            logger.LogWarning("warning {0}", 3);
            logger.LogWarning(exception, "warning");
            logger.LogWarning("Warning", "warning");
            logger.LogWarning("Warning", "warning {0}", 3);
            logger.LogWarning("Warning", exception, "warning");
            logger.LogError("error");
            logger.LogError("error {0}", 4);
            logger.LogError(exception, "error");
            logger.LogError("Error", "error");
            logger.LogError("Error", "error {0}", 4);
            logger.LogError("Error", exception, "error");
            logger.LogFatal("fatal");
            logger.LogFatal("fatal {0}", 5);
            logger.LogFatal(exception, "fatal");
            logger.LogFatal("Fatal", "fatal");
            logger.LogFatal("Fatal", "fatal {0}", 5);
            logger.LogFatal("Fatal", exception, "fatal");

            Assert.That(logger.Calls.Count, Is.EqualTo(30));
            Assert.That(logger.Calls.Select(x => x.Level), Is.EqualTo(new[]
            {
                LogLevel.Debug, LogLevel.Debug, LogLevel.Debug, LogLevel.Debug, LogLevel.Debug, LogLevel.Debug,
                LogLevel.Info, LogLevel.Info, LogLevel.Info, LogLevel.Info, LogLevel.Info, LogLevel.Info,
                LogLevel.Warning, LogLevel.Warning, LogLevel.Warning, LogLevel.Warning, LogLevel.Warning, LogLevel.Warning,
                LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Error, LogLevel.Error,
                LogLevel.Fatal, LogLevel.Fatal, LogLevel.Fatal, LogLevel.Fatal, LogLevel.Fatal, LogLevel.Fatal
            }));
            Assert.That(logger.Calls.Select(x => x.Shape), Is.EqualTo(new[]
            {
                CallShape.Message, CallShape.MessageArgs, CallShape.MessageExceptionArgs,
                CallShape.TagMessage, CallShape.TagMessageArgs, CallShape.TagMessageExceptionArgs,
                CallShape.Message, CallShape.MessageArgs, CallShape.MessageExceptionArgs,
                CallShape.TagMessage, CallShape.TagMessageArgs, CallShape.TagMessageExceptionArgs,
                CallShape.Message, CallShape.MessageArgs, CallShape.MessageExceptionArgs,
                CallShape.TagMessage, CallShape.TagMessageArgs, CallShape.TagMessageExceptionArgs,
                CallShape.Message, CallShape.MessageArgs, CallShape.MessageExceptionArgs,
                CallShape.TagMessage, CallShape.TagMessageArgs, CallShape.TagMessageExceptionArgs,
                CallShape.Message, CallShape.MessageArgs, CallShape.MessageExceptionArgs,
                CallShape.TagMessage, CallShape.TagMessageArgs, CallShape.TagMessageExceptionArgs
            }));
        }

        /// <summary>Checks that a tag followed by a plain message selects the tagged overload, not the untagged one.</summary>
        [Test]
        public void TagAndMessage_SelectTaggedOverload()
        {
            var logger = new RecordingMajLogger();

            logger.LogInfo("Audio", "decoder ready");

            var call = logger.Calls.Single();
            Assert.That(call.Shape, Is.EqualTo(CallShape.TagMessage));
            Assert.That(call.Tag, Is.EqualTo("Audio"));
            Assert.That(call.Message, Is.EqualTo("decoder ready"));
        }

        /// <summary>Checks that the untagged overloads report a null tag so the logger can tell them apart.</summary>
        [Test]
        public void UntaggedOverloads_ForwardNullTag()
        {
            var logger = new RecordingMajLogger();

            logger.LogError("plain");
            logger.LogError("plain {0}", 1);
            logger.LogError(new InvalidOperationException("boom"), "plain");

            Assert.That(logger.Calls.All(x => x.Tag is null), Is.True);
            Assert.That(logger.Calls[0].Shape, Is.EqualTo(CallShape.Message));
            Assert.That(logger.Calls[1].Shape, Is.EqualTo(CallShape.MessageArgs));
            Assert.That(logger.Calls[2].Shape, Is.EqualTo(CallShape.MessageExceptionArgs));
        }

        /// <summary>Checks that several format arguments are collected into the array the logger receives.</summary>
        [Test]
        public void MultipleArgs_AreCollected()
        {
            var logger = new RecordingMajLogger();

            logger.LogInfo("Runtime", "{0}-{1}-{2}", 1, "two", null);

            var call = logger.Calls.Single();
            Assert.That(call.Shape, Is.EqualTo(CallShape.TagMessageArgs));
            Assert.That(call.Args, Is.EqualTo(new object?[] { 1, "two", null }));
        }

        /// <summary>Checks that a prepared argument array is forwarded as the same instance.</summary>
        [Test]
        public void PreparedArgs_AreForwardedUnchanged()
        {
            var logger = new RecordingMajLogger();
            var args = new object?[] { "left", null, 42 };

            logger.LogWarning("Audio", "mixed {0} {1} {2}", args);

            var call = logger.Calls.Single();
            Assert.That(call.Shape, Is.EqualTo(CallShape.TagMessageArgs));
            Assert.That(call.Message, Is.EqualTo("mixed {0} {1} {2}"));
            Assert.That(call.Args, Is.SameAs(args));
        }

        /// <summary>Checks that an empty argument array reaches the overload without format arguments, so braces stay literal.</summary>
        [Test]
        public void EmptyArgs_ForwardAsMessageWithoutFormatting()
        {
            var logger = new RecordingMajLogger();
            var args = Array.Empty<object>();

            logger.LogInfo("literal {braces} stay intact", args);
            logger.LogFatal("Runtime", "literal {braces} stay intact", args);

            Assert.That(logger.Calls[0].Shape, Is.EqualTo(CallShape.Message));
            Assert.That(logger.Calls[1].Shape, Is.EqualTo(CallShape.TagMessage));
            Assert.That(logger.Calls[0].Message, Is.EqualTo("literal {braces} stay intact"));
            Assert.That(logger.Calls[1].Tag, Is.EqualTo("Runtime"));
        }

        /// <summary>Checks that the exception overloads forward the exception, the tag, the message and the arguments.</summary>
        [Test]
        public void ExceptionOverloads_ForwardException()
        {
            var logger = new RecordingMajLogger();
            var exception = new InvalidOperationException("boom");
            var args = new object?[] { 1 };

            logger.LogError(exception, "boom: {0}", args);
            logger.LogFatal("Runtime", exception, "fatal: {0}", args);

            var untagged = logger.Calls[0];
            Assert.That(untagged.Shape, Is.EqualTo(CallShape.MessageExceptionArgs));
            Assert.That(untagged.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(untagged.Exception, Is.SameAs(exception));
            Assert.That(untagged.Message, Is.EqualTo("boom: {0}"));
            Assert.That(untagged.Args, Is.SameAs(args));

            var tagged = logger.Calls[1];
            Assert.That(tagged.Shape, Is.EqualTo(CallShape.TagMessageExceptionArgs));
            Assert.That(tagged.Tag, Is.EqualTo("Runtime"));
            Assert.That(tagged.Exception, Is.SameAs(exception));
            Assert.That(tagged.Message, Is.EqualTo("fatal: {0}"));
            Assert.That(tagged.Args, Is.SameAs(args));
        }

        /// <summary>Checks that an exception placed after the message stays a format argument, as <c>ILogger</c> does.</summary>
        [Test]
        public void ExceptionAsMessageArgument_StaysFormatArgument()
        {
            var logger = new RecordingMajLogger();
            var exception = new ArgumentException("bad");

            logger.LogInfo("failed: {0}", exception);

            var call = logger.Calls.Single();
            Assert.That(call.Shape, Is.EqualTo(CallShape.MessageArgs));
            Assert.That(call.Exception, Is.Null);
            Assert.That(call.Args, Is.EqualTo(new object?[] { exception }));
        }

        /// <summary>Checks that a null exception takes the path of the overload without an exception.</summary>
        [Test]
        public void NullException_ForwardsWithoutExceptionOverload()
        {
            var logger = new RecordingMajLogger();

            logger.LogWarning((Exception?)null, "plain");
            logger.LogWarning("Runtime", (Exception?)null, "plain");

            Assert.That(logger.Calls[0].Shape, Is.EqualTo(CallShape.Message));
            Assert.That(logger.Calls[1].Shape, Is.EqualTo(CallShape.TagMessage));
            Assert.That(logger.Calls.All(x => x.Exception is null), Is.True);
        }

        /// <summary>Checks that an empty argument array combined with an exception keeps the message unformatted.</summary>
        [Test]
        public void ExceptionWithEmptyArgs_ForwardsWithoutFormatting()
        {
            var logger = new RecordingMajLogger();
            var exception = new InvalidOperationException("boom");

            logger.LogError(exception, "literal {braces} stay intact");

            var call = logger.Calls.Single();
            Assert.That(call.Shape, Is.EqualTo(CallShape.MessageExceptionArgs));
            Assert.That(call.Exception, Is.SameAs(exception));
            Assert.That(call.Message, Is.EqualTo("literal {braces} stay intact"));
            Assert.That(call.Args, Is.Empty);
        }

        /// <summary>Checks that a missing logger is reported instead of failing with a null dereference.</summary>
        [Test]
        public void NullLogger_ThrowsArgumentNullException()
        {
            var logger = (IMajLogger)null!;

            var untagged = Assert.Throws<ArgumentNullException>(() => logger.LogDebug("message"));
            var tagged = Assert.Throws<ArgumentNullException>(() => logger.LogFatal("Runtime", "message {0}", 1));
            var withException = Assert.Throws<ArgumentNullException>(() => logger.LogError(new InvalidOperationException(), "message"));

            Assert.That(untagged!.ParamName, Is.EqualTo("logger"));
            Assert.That(tagged!.ParamName, Is.EqualTo("logger"));
            Assert.That(withException!.ParamName, Is.EqualTo("logger"));
        }

        /// <summary>Checks that a null tag is treated as no tag and keeps the message unformatted.</summary>
        [Test]
        public void NullTag_ForwardsAsUntagged()
        {
            var logger = new RecordingMajLogger();

            logger.LogWarning((string)null!, "literal {braces} stay intact");

            Assert.That(logger.Calls.Single().Shape, Is.EqualTo(CallShape.Message));
        }
    }
}
