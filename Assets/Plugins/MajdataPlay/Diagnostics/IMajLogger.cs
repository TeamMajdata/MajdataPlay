using System;
using System.Collections.Generic;
using System.Text;
#nullable enable
namespace MajdataPlay.Diagnostics
{
    public interface IMajLogger
    {
        void Log(LogLevel level, string message);
        void Log(LogLevel level, string message, params object?[] args);

        /// <summary>
        /// Writes a log entry together with the exception that caused it, using <paramref name="message"/> as a composite
        /// format string for <paramref name="args"/>.
        /// </summary>
        /// <param name="level">The level to log at.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> when the entry has none.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>; when empty, <paramref name="message"/> is recorded as it is.</param>
        void Log(LogLevel level, Exception? exception, string message, params object?[] args);

        void Log(LogLevel level, string tag, string message);
        void Log(LogLevel level, string tag, string message, params object?[] args);

        /// <summary>
        /// Writes a tagged log entry together with the exception that caused it, using <paramref name="message"/> as a
        /// composite format string for <paramref name="args"/>.
        /// </summary>
        /// <param name="level">The level to log at.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> when the entry has none.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>; when empty, <paramref name="message"/> is recorded as it is.</param>
        void Log(LogLevel level, string tag, Exception? exception, string message, params object?[] args);
    }
}
