using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MajdataPlay.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Diagnostics
{
    /// <summary>
    /// Covers <see cref="MajDebug"/>: message formatting, level filtering, the size limit, writer replacement and the
    /// forwarding of written logs to the Unity logger. Each test swaps in its own writer and a recording Unity logger and
    /// restores the original static state afterwards.
    /// </summary>
    public class MajDebugTests
    {
        /// <summary>The pattern of the timestamp and level prefix of every written line.</summary>
        private const string PrefixPattern = @"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{4}\]";

        /// <summary>The Unity logger in place before the test.</summary>
        private ILogger? _savedLogger;

        /// <summary>The log writer in place before the test.</summary>
        private TextWriter? _savedWriter;

        /// <summary>The minimum level in place before the test.</summary>
        private LogLevel _savedMinLevel;

        /// <summary>The size limit in place before the test.</summary>
        private long _savedMaxSize;

        /// <summary>The handler that records the logs forwarded to Unity.</summary>
        private RecordingLogHandler _handler = null!;

        /// <summary>The writer that receives the test's logs.</summary>
        private StringWriter _writer = null!;

        /// <summary>A writer that records flushes and disposal.</summary>
        private sealed class TrackingWriter : StringWriter
        {
            /// <summary>Gets the number of Flush calls.</summary>
            public int FlushCount { get; private set; }

            /// <summary>Gets a value indicating whether the writer was disposed.</summary>
            public bool IsDisposed { get; private set; }

            /// <summary>Counts the flush.</summary>
            public override void Flush()
            {
                FlushCount++;
                base.Flush();
            }

            /// <summary>Records the disposal.</summary>
            /// <param name="disposing">Whether managed resources are released.</param>
            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }

        /// <summary>Saves the static state, installs the recording logger and drains logs queued before the test.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedLogger = PrivateMember.GetStaticField<ILogger?>(typeof(MajDebug), "s_unityLogger");
            _savedWriter = PrivateMember.GetStaticField<TextWriter?>(typeof(MajDebug), "s_logWriter");
            _savedMinLevel = MajDebug.MinLogLevel;
            _savedMaxSize = MajDebug.MaxLogSize;
            _handler = new RecordingLogHandler();
            PrivateMember.SetStaticField(typeof(MajDebug), "s_unityLogger", new Logger(_handler));
            MajDebug.MinLogLevel = LogLevel.Debug;
            DrainQueue();
            _handler.Entries.Clear();
            _writer = new StringWriter();
            MajDebug.SetLogWriter(_writer, false);
        }

        /// <summary>Drains the logs the test left queued and restores the saved static state.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(typeof(MajDebug), "s_unityLogger", new Logger(new RecordingLogHandler()));
            MajDebug.MinLogLevel = LogLevel.Debug;
            DrainQueue();
            MajDebug.SetLogWriter(_savedWriter, false);
            PrivateMember.SetStaticField(typeof(MajDebug), "s_unityLogger", _savedLogger);
            MajDebug.MinLogLevel = _savedMinLevel;
            MajDebug.MaxLogSize = _savedMaxSize;
        }

        /// <summary>Writes every queued log into a throwaway writer.</summary>
        private static void DrainQueue()
        {
            MajDebug.SetLogWriter(new StringWriter(), false);
            MajDebug.FlushLog();
        }

        /// <summary>Gets the number of logs waiting in the queue.</summary>
        /// <returns>The queue length.</returns>
        private static int QueuedCount()
        {
            return ((ICollection)PrivateMember.GetStaticField<object>(typeof(MajDebug), "s_logQueue")).Count;
        }

        /// <summary>Checks that a tagged message is written with the timestamp, level and tag prefix and forwarded to Unity.</summary>
        [Test]
        public void LogInfo_WithTag_WritesPrefixedLineAndForwards()
        {
            MajDebug.LogInfo("Net", "hello");
            MajDebug.FlushLog();

            Assert.That(_writer.ToString(), Does.Match("^" + PrefixPattern + @"\[I\]\[Net\]hello"));
            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
            Assert.That(_handler.Entries[0].Type, Is.EqualTo(LogType.Log));
            Assert.That(_handler.Entries[0].Message, Does.Match("^" + PrefixPattern + @"\[I\]\[Net\]hello"));
        }

        /// <summary>Checks that every level and overload maps to the expected written level and Unity log type.</summary>
        [Test]
        public void LogMethods_MapLevels()
        {
            MajDebug.LogDebug("plain-debug");
            MajDebug.LogDebug("T", "tagged-debug");
            MajDebug.LogInfo("plain-info");
            MajDebug.LogWarning("plain-warning");
            MajDebug.LogWarning("T", "tagged-warning");
            MajDebug.LogError("plain-error");
            MajDebug.LogError("T", "tagged-error");
            MajDebug.FlushLog();

            var text = _writer.ToString();
            Assert.That(text, Does.Match(@"\[D\]plain-debug"));
            Assert.That(text, Does.Match(@"\[D\]\[T\]tagged-debug"));
            Assert.That(text, Does.Match(@"\[I\]plain-info"));
            Assert.That(text, Does.Match(@"\[W\]plain-warning"));
            Assert.That(text, Does.Match(@"\[W\]\[T\]tagged-warning"));
            Assert.That(text, Does.Match(@"\[E\]plain-error"));
            Assert.That(text, Does.Match(@"\[E\]\[T\]tagged-error"));
            var types = _handler.Entries.Select(x => x.Type).ToArray();
            Assert.That(types, Is.EqualTo(new[]
            {
                LogType.Log, LogType.Log, LogType.Log, LogType.Warning, LogType.Warning, LogType.Error, LogType.Error,
            }));
        }

        /// <summary>Checks that queued fatal-level logs are written with the F code and forwarded as Unity errors.</summary>
        [Test]
        public void WriteLogIntoStream_FatalLog_UsesFatalCode()
        {
            var queue = PrivateMember.GetStaticField<object>(typeof(MajDebug), "s_logQueue");
            var logType = typeof(MajDebug).GetNestedType("GameLog", BindingFlags.NonPublic)!;
            var log = Activator.CreateInstance(logType)!;
            var builderType = logType.GetProperty("Condition")!.PropertyType;
            var builder = builderType.GetConstructor(new[] { typeof(bool) })!.Invoke(new object[] { false });
            builderType.GetMethod("Append", new[] { typeof(string) })!.Invoke(builder, new object[] { "fatal-message" });
            PrivateMember.SetProperty(log, "Condition", builder);
            PrivateMember.SetProperty(log, "Date", DateTime.Now);
            PrivateMember.SetProperty(log, "Level", LogLevel.Fatal);
            queue.GetType().GetMethod("Enqueue")!.Invoke(queue, new[] { log });

            MajDebug.FlushLog();

            Assert.That(_writer.ToString(), Does.Match("^" + PrefixPattern + @"\[F\]fatal-message"));
            Assert.That(_handler.Entries.Single().Type, Is.EqualTo(LogType.Error));
        }

        /// <summary>Checks that an empty tag is omitted from the prefix.</summary>
        [Test]
        public void Log_EmptyTag_IsOmitted()
        {
            MajDebug.LogInfo(string.Empty, "untagged");
            MajDebug.FlushLog();

            Assert.That(_writer.ToString(), Does.Match("^" + PrefixPattern + @"\[I\]untagged"));
        }

        /// <summary>Checks that exceptions are written as errors with a trailing line break and no captured stack trace.</summary>
        [Test]
        public void LogException_WritesExceptionWithoutStackTrace()
        {
            MajDebug.LogException(new InvalidOperationException("boom"));
            MajDebug.LogException("IO", new ArgumentException("bad"));
            MajDebug.FlushLog();

            var lines = _writer.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.That(lines[0], Does.Match("^" + PrefixPattern + @"\[E\]System\.InvalidOperationException: boom$"));
            Assert.That(lines[1], Is.Empty);
            Assert.That(lines[2], Does.Match("^" + PrefixPattern + @"\[E\]\[IO\]System\.ArgumentException: bad$"));
            Assert.That(_handler.Entries.All(x => x.Type == LogType.Error), Is.True);
        }

        /// <summary>Checks that ordinary messages carry the caller's stack trace in the Editor.</summary>
        [Test]
        public void Log_Message_AppendsStackTraceInEditor()
        {
            MajDebug.LogInfo("with-stack");
            MajDebug.FlushLog();

            var lines = _writer.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
#if UNITY_EDITOR || DEBUG
            Assert.That(lines.Length, Is.GreaterThan(1));
            Assert.That(lines.Skip(1).Any(x => x.Contains(" at ")), Is.True);
#else
            Assert.That(lines.Length, Is.EqualTo(1));
#endif
        }

        /// <summary>Checks that messages below the minimum level are dropped and are not forwarded.</summary>
        [Test]
        public void MinLogLevel_DropsLowerLevels()
        {
            MajDebug.MinLogLevel = LogLevel.Warning;

            MajDebug.LogDebug("dropped-debug");
            MajDebug.LogInfo("dropped-info");
            MajDebug.LogWarning("kept-warning");
            MajDebug.FlushLog();

            var text = _writer.ToString();
            Assert.That(text, Does.Not.Contain("dropped"));
            Assert.That(text, Does.Contain("kept-warning"));
            Assert.That(_handler.Entries.Count, Is.EqualTo(1));
            Assert.That(QueuedCount(), Is.EqualTo(0));
        }

        /// <summary>Checks that logs stay queued while no writer is set and are written once one is installed.</summary>
        [Test]
        public void FlushLog_WithoutWriter_KeepsLogsQueued()
        {
            MajDebug.SetLogWriter(null, false);

            MajDebug.LogInfo("waiting");
            MajDebug.FlushLog();

            Assert.That(QueuedCount(), Is.EqualTo(1));
            Assert.That(_handler.Entries, Is.Empty);

            MajDebug.SetLogWriter(_writer, false);
            MajDebug.FlushLog();

            Assert.That(_writer.ToString(), Does.Contain("waiting"));
            Assert.That(QueuedCount(), Is.EqualTo(0));
        }

        /// <summary>Checks that replacing the writer flushes and disposes the old one only when requested.</summary>
        [Test]
        public void SetLogWriter_DisposesOldWriterOnlyWhenRequested()
        {
            var kept = new TrackingWriter();
            var disposed = new TrackingWriter();

            MajDebug.SetLogWriter(kept, false);
            MajDebug.SetLogWriter(disposed, false);
            MajDebug.SetLogWriter(_writer, true);

            Assert.That(kept.IsDisposed, Is.False);
            Assert.That(disposed.IsDisposed, Is.True);
            Assert.That(disposed.FlushCount, Is.GreaterThan(0));
        }

        /// <summary>Checks that a seekable stream writer stops receiving logs once it exceeds the size limit.</summary>
        [Test]
        public void MaxLogSize_StopsWritingPastLimit()
        {
            using var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            MajDebug.SetLogWriter(writer, false);
            MajDebug.MaxLogSize = 1;

            MajDebug.LogInfo("first");
            MajDebug.FlushLog();
            MajDebug.LogInfo("second");
            MajDebug.FlushLog();
            MajDebug.MaxLogSize = 0;
            MajDebug.LogInfo("third");
            MajDebug.FlushLog();

            var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            Assert.That(text, Does.Contain("first"));
            Assert.That(text, Does.Not.Contain("second"));
            Assert.That(text, Does.Contain("third"));
            Assert.That(_handler.Entries.Count, Is.EqualTo(2));
        }

        /// <summary>Checks that written errors and warnings reach the real Unity logger.</summary>
        [Test]
        public void FlushLog_ForwardsToUnityConsole()
        {
            PrivateMember.SetStaticField(typeof(MajDebug), "s_unityLogger", Debug.unityLogger);
            LogAssert.Expect(LogType.Error, new Regex(@"\[E\]\[Tests\]forwarded-error"));
            LogAssert.Expect(LogType.Warning, new Regex(@"\[W\]\[Tests\]forwarded-warning"));

            MajDebug.LogError("Tests", "forwarded-error");
            MajDebug.LogWarning("Tests", "forwarded-warning");
            MajDebug.FlushLog();

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Checks that the queue is trimmed to its bound while no writer is installed.</summary>
        [Test]
        public void WriteLogIntoStream_WithoutWriter_TrimsQueue()
        {
            MajDebug.SetLogWriter(null, false);
            var exception = new InvalidOperationException("queued");
            for (var i = 0; i < 10005; i++)
            {
                MajDebug.LogException(exception);
            }
            var method = typeof(MajDebug).GetMethod("WriteLogIntoStream", BindingFlags.NonPublic | BindingFlags.Static)!;
            var builder = Activator.CreateInstance(method.GetParameters()[0].ParameterType.GetElementType()!);

            method.Invoke(null, new[] { builder });

            Assert.That(QueuedCount(), Is.EqualTo(10000));
        }

        /// <summary>Checks the mapping from MajdataPlay levels to Unity log types.</summary>
        /// <param name="level">The MajdataPlay level.</param>
        /// <param name="expected">The expected Unity log type.</param>
        [TestCase(LogLevel.Debug, LogType.Log)]
        [TestCase(LogLevel.Info, LogType.Log)]
        [TestCase(LogLevel.Warning, LogType.Warning)]
        [TestCase(LogLevel.Error, LogType.Error)]
        [TestCase(LogLevel.Fatal, LogType.Error)]
        public void ToUnityLogLevel_MapsLevels(LogLevel level, LogType expected)
        {
            Assert.That(PrivateMember.InvokeStatic<LogType>(typeof(MajDebug), "ToUnityLogLevel", level), Is.EqualTo(expected));
        }

        /// <summary>Checks that an undefined level cannot be mapped to a Unity log type.</summary>
        [Test]
        public void ToUnityLogLevel_UndefinedLevel_Throws()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.InvokeStatic(typeof(MajDebug), "ToUnityLogLevel", (LogLevel)42));

            Assert.That(exception!.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
        }

        /// <summary>Checks the single-character level codes written into the line prefix.</summary>
        /// <param name="level">The MajdataPlay level.</param>
        /// <param name="expected">The expected level code.</param>
        [TestCase(LogLevel.Debug, 'D')]
        [TestCase(LogLevel.Info, 'I')]
        [TestCase(LogLevel.Warning, 'W')]
        [TestCase(LogLevel.Error, 'E')]
        [TestCase(LogLevel.Fatal, 'F')]
        public void GetLogLevelChar_MapsLevels(LogLevel level, char expected)
        {
            Assert.That(PrivateMember.InvokeStatic<char>(typeof(MajDebug), "GetLogLevelChar", level), Is.EqualTo(expected));
        }

        /// <summary>Checks that an undefined level has no level code.</summary>
        [Test]
        public void GetLogLevelChar_UndefinedLevel_Throws()
        {
            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.InvokeStatic(typeof(MajDebug), "GetLogLevelChar", (LogLevel)42));

            Assert.That(exception!.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
        }

        /// <summary>Checks the mapping from Unity log types to MajdataPlay levels.</summary>
        /// <param name="type">The Unity log type.</param>
        /// <param name="expected">The expected MajdataPlay level.</param>
        [TestCase(LogType.Log, LogLevel.Info)]
        [TestCase(LogType.Warning, LogLevel.Warning)]
        [TestCase(LogType.Error, LogLevel.Error)]
        [TestCase(LogType.Exception, LogLevel.Error)]
        [TestCase(LogType.Assert, LogLevel.Debug)]
        public void ToMajdataLogLevel_MapsTypes(LogType type, LogLevel expected)
        {
            Assert.That(PrivateMember.InvokeStatic<LogLevel>(typeof(MajDebug), "ToMajdataLogLevel", type), Is.EqualTo(expected));
        }

        /// <summary>Checks that the levels are ordered from least to most severe, which the minimum-level filter relies on.</summary>
        [Test]
        public void LogLevel_IsOrderedBySeverity()
        {
            Assert.That(LogLevel.Debug, Is.LessThan(LogLevel.Info));
            Assert.That(LogLevel.Info, Is.LessThan(LogLevel.Warning));
            Assert.That(LogLevel.Warning, Is.LessThan(LogLevel.Error));
            Assert.That(LogLevel.Error, Is.LessThan(LogLevel.Fatal));
        }
    }
}
