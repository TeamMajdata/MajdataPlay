using MajSimai.Logging;
using System;

#nullable enable
namespace MajdataPlay.Diagnostics
{
    /// <summary>
    /// Forwards the log messages of the MajSimai chart parser to <see cref="MajDebug"/>.
    /// </summary>
    /// <remarks>
    /// MajSimai writes through <see cref="SimaiLogger.Current"/>, whose default logger writes to the console, which Unity
    /// does not show. <see cref="Install"/> replaces it with this logger, so that skipped notes, dropped text and charts
    /// that failed to parse appear in the game log with the <see cref="Tag"/> tag. The configured
    /// <see cref="MajDebug.MinLogLevel"/> is read on every call, so MajSimai does not build messages that would be dropped.
    /// The logger is thread safe, because <see cref="MajDebug"/> queues its entries.
    /// </remarks>
    internal sealed class MajDebugSimaiLogger : ISimaiLogger
    {
        /// <summary>
        /// The tag of the forwarded log entries.
        /// </summary>
        internal const string Tag = "MajSimai";

        /// <summary>
        /// Gets the logger that <see cref="Install"/> sets.
        /// </summary>
        internal static MajDebugSimaiLogger Instance { get; } = new();

        /// <summary>
        /// Makes <see cref="Instance"/> the logger of MajSimai.
        /// </summary>
        internal static void Install()
        {
            SimaiLogger.Current = Instance;
        }

        /// <summary>
        /// Gets whether messages of a MajSimai level are written to the game log.
        /// </summary>
        /// <param name="level">The MajSimai level to check.</param>
        /// <returns><see langword="true"/> when <paramref name="level"/> maps to a <see cref="LogLevel"/> that is at least
        /// <see cref="MajDebug.MinLogLevel"/>; <see langword="false"/> for <see cref="SimaiLogLevel.None"/>.</returns>
        public bool IsEnabled(SimaiLogLevel level)
        {
            return TryGetLogLevel(level, out var logLevel) && logLevel >= MajDebug.MinLogLevel;
        }

        /// <summary>
        /// Writes a MajSimai message to the game log at the matching level.
        /// </summary>
        /// <param name="level">The severity of the message.</param>
        /// <param name="message">The message, including where in the chart text it applies.</param>
        /// <param name="exception">The exception that caused the message, if any; it is written after the message.</param>
        public void Log(SimaiLogLevel level, string message, Exception? exception)
        {
            if (!TryGetLogLevel(level, out var logLevel) || logLevel < MajDebug.MinLogLevel)
            {
                return;
            }
            var text = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
            switch (logLevel)
            {
                case LogLevel.Debug:
                    MajDebug.LogDebug(Tag, text);
                    break;
                case LogLevel.Info:
                    MajDebug.LogInfo(Tag, text);
                    break;
                case LogLevel.Warning:
                    MajDebug.LogWarning(Tag, text);
                    break;
                default:
                    MajDebug.LogError(Tag, text);
                    break;
            }
        }

        /// <summary>
        /// Maps a MajSimai level to the <see cref="MajDebug"/> level it is written at.
        /// </summary>
        /// <param name="level">The MajSimai level.</param>
        /// <param name="logLevel">When this method returns <see langword="true"/>, the matching level; otherwise
        /// <see cref="LogLevel.Debug"/>.</param>
        /// <returns><see langword="true"/> for <see cref="SimaiLogLevel.Debug"/>, <see cref="SimaiLogLevel.Info"/>,
        /// <see cref="SimaiLogLevel.Warning"/> and <see cref="SimaiLogLevel.Error"/>; <see langword="false"/> for
        /// <see cref="SimaiLogLevel.None"/> and unknown values, which are never written.</returns>
        internal static bool TryGetLogLevel(SimaiLogLevel level, out LogLevel logLevel)
        {
            switch (level)
            {
                case SimaiLogLevel.Debug:
                    logLevel = LogLevel.Debug;
                    return true;
                case SimaiLogLevel.Info:
                    logLevel = LogLevel.Info;
                    return true;
                case SimaiLogLevel.Warning:
                    logLevel = LogLevel.Warning;
                    return true;
                case SimaiLogLevel.Error:
                    logLevel = LogLevel.Error;
                    return true;
                default:
                    logLevel = LogLevel.Debug;
                    return false;
            }
        }
    }
}
