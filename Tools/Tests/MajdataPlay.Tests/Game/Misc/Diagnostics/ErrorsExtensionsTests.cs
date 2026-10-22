using System;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.Diagnostics;
using MajdataPlay.Tests;
using ManagedBass;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Diagnostics
{
    /// <summary>
    /// Covers the Bass error logging helpers of <see cref="ErrorsExtensions"/> by capturing the output of
    /// <see cref="MajDebug"/> in a text writer and a recording logger.
    /// </summary>
    public class ErrorsExtensionsTests
    {
        /// <summary>A logger that records the messages forwarded by <see cref="MajDebug"/>.</summary>
        private sealed class RecordingLogger : ILogger
        {
            /// <summary>Gets the recorded log types and messages.</summary>
            public List<(LogType Type, string Message)> Messages { get; } = new();

            /// <summary>Gets or sets the ignored log handler.</summary>
            public ILogHandler logHandler { get; set; } = null!;

            /// <summary>Gets or sets whether logging is enabled.</summary>
            public bool logEnabled { get; set; } = true;

            /// <summary>Gets or sets the ignored filter.</summary>
            public LogType filterLogType { get; set; } = LogType.Log;

            /// <summary>Records a message.</summary>
            /// <param name="logType">The log type.</param>
            /// <param name="message">The message.</param>
            public void Log(LogType logType, object message)
            {
                Messages.Add((logType, message?.ToString() ?? string.Empty));
            }

            /// <summary>Records a message.</summary>
            /// <param name="logType">The log type.</param>
            /// <param name="message">The message.</param>
            /// <param name="context">The ignored context.</param>
            public void Log(LogType logType, object message, UnityEngine.Object context)
            {
                Log(logType, message);
            }

            /// <summary>Records a tagged message.</summary>
            /// <param name="logType">The log type.</param>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            public void Log(LogType logType, string tag, object message)
            {
                Log(logType, tag + message);
            }

            /// <summary>Records a tagged message.</summary>
            /// <param name="logType">The log type.</param>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            /// <param name="context">The ignored context.</param>
            public void Log(LogType logType, string tag, object message, UnityEngine.Object context)
            {
                Log(logType, tag + message);
            }

            /// <summary>Records a message.</summary>
            /// <param name="message">The message.</param>
            public void Log(object message)
            {
                Log(LogType.Log, message);
            }

            /// <summary>Records a tagged message.</summary>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            public void Log(string tag, object message)
            {
                Log(LogType.Log, tag, message);
            }

            /// <summary>Records a tagged message.</summary>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            /// <param name="context">The ignored context.</param>
            public void Log(string tag, object message, UnityEngine.Object context)
            {
                Log(LogType.Log, tag, message);
            }

            /// <summary>Records a warning.</summary>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            public void LogWarning(string tag, object message)
            {
                Log(LogType.Warning, tag, message);
            }

            /// <summary>Records a warning.</summary>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            /// <param name="context">The ignored context.</param>
            public void LogWarning(string tag, object message, UnityEngine.Object context)
            {
                Log(LogType.Warning, tag, message);
            }

            /// <summary>Records an error.</summary>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            public void LogError(string tag, object message)
            {
                Log(LogType.Error, tag, message);
            }

            /// <summary>Records an error.</summary>
            /// <param name="tag">The tag.</param>
            /// <param name="message">The message.</param>
            /// <param name="context">The ignored context.</param>
            public void LogError(string tag, object message, UnityEngine.Object context)
            {
                Log(LogType.Error, tag, message);
            }

            /// <summary>Records a formatted message.</summary>
            /// <param name="logType">The log type.</param>
            /// <param name="format">The format.</param>
            /// <param name="args">The arguments.</param>
            public void LogFormat(LogType logType, string format, params object[] args)
            {
                Log(logType, string.Format(format, args));
            }

            /// <summary>Records an exception.</summary>
            /// <param name="exception">The exception.</param>
            public void LogException(Exception exception)
            {
                Log(LogType.Exception, exception);
            }

            /// <summary>Records a formatted message.</summary>
            /// <param name="logType">The log type.</param>
            /// <param name="context">The ignored context.</param>
            /// <param name="format">The format.</param>
            /// <param name="args">The arguments.</param>
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                LogFormat(logType, format, args);
            }

            /// <summary>Records an exception.</summary>
            /// <param name="exception">The exception.</param>
            /// <param name="context">The ignored context.</param>
            public void LogException(Exception exception, UnityEngine.Object context)
            {
                LogException(exception);
            }

            /// <summary>Reports that every log type is enabled.</summary>
            /// <param name="logType">The log type.</param>
            /// <returns>Always true.</returns>
            public bool IsLogTypeAllowed(LogType logType)
            {
                return true;
            }
        }

        /// <summary>Checks that each helper logs the Bass error with the audio tag at its level.</summary>
        [Test]
        public void ErrorsExtensions_LogAtEachLevel()
        {
            var savedLogger = PrivateMember.GetStaticField<ILogger?>(typeof(MajDebug), "_unityLogger");
            var savedLevel = MajDebug.MinLogLevel;
            var logger = new RecordingLogger();
            using var writer = new StringWriter();
            try
            {
                PrivateMember.SetStaticField(typeof(MajDebug), "_unityLogger", logger);
                MajDebug.SetLogWriter(TextWriter.Null, disposeOld: false);
                MajDebug.MinLogLevel = (LogLevel)int.MaxValue;
                MajDebug.FlushLog();
                MajDebug.MinLogLevel = LogLevel.Debug;
                MajDebug.SetLogWriter(writer, disposeOld: false);

                Errors.FileOpen.LogDebug();
                Errors.Init.LogInfo();
                Errors.Busy.LogWarning();
                Errors.Memory.LogError();
                MajDebug.FlushLog();

                var text = writer.ToString();
                Assert.That(text, Does.Contain("[Audio][Bass]Error: FileOpen"));
                Assert.That(text, Does.Contain("[Audio][Bass]Error: Init"));
                Assert.That(text, Does.Contain("[Audio][Bass]Error: Busy"));
                Assert.That(text, Does.Contain("[Audio][Bass]Error: Memory"));
                Assert.That(logger.Messages.Exists(entry => entry.Type == LogType.Warning && entry.Message.Contains("Busy")), Is.True);
                Assert.That(logger.Messages.Exists(entry => entry.Type == LogType.Error && entry.Message.Contains("Memory")), Is.True);
            }
            finally
            {
                MajDebug.SetLogWriter(null, disposeOld: false);
                MajDebug.MinLogLevel = savedLevel;
                PrivateMember.SetStaticField(typeof(MajDebug), "_unityLogger", savedLogger);
            }
        }
    }
}
