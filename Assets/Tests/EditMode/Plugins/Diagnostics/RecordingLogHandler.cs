using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Diagnostics
{
    /// <summary>A Unity log handler that records formatted messages instead of printing them.</summary>
    public sealed class RecordingLogHandler : ILogHandler
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
}
