#nullable enable
using System;

namespace MajdataPlay.Diagnostics
{
    /// <summary>
    /// Provides the level-specific convenience overloads of <see cref="IMajLogger"/>, shaped after the
    /// <c>Microsoft.Extensions.Logging.LoggerExtensions</c> family: the tagged overloads take a <c>tag</c> where the
    /// framework takes an <c>EventId</c>, and the exception-carrying overloads take the exception before the message.
    /// </summary>
    /// <remarks>
    /// Every overload forwards the entry unchanged to the matching <see cref="IMajLogger.Log(LogLevel, string)"/> overload,
    /// so level filtering, message formatting and output stay owned by the logger implementation. The overloads that take
    /// a <c>tag</c> reach the tagged interface overloads, and the overloads without one reach the untagged ones.
    /// An empty argument array is treated as "no format arguments" and the message is forwarded unformatted, so a message
    /// that happens to contain braces never reaches the formatter and never fails to format. An overload that takes an
    /// <c>exception</c> forwards a <see langword="null"/> exception through the overload of the same shape that has none,
    /// so a missing exception never changes which entry the logger sees.
    /// </remarks>
    public static class IMajLoggerExtensions
    {
        /// <summary>
        /// Writes a debug-level entry without a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogDebug(this IMajLogger logger, string message)
        {
            Forward(logger, LogLevel.Debug, null, message, null);
        }

        /// <summary>
        /// Writes a debug-level entry without a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogDebug(this IMajLogger logger, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Debug, null, message, args);
        }

        /// <summary>
        /// Writes a debug-level entry without a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogDebug(this IMajLogger logger, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Debug, null, exception, message, args);
        }

        /// <summary>
        /// Writes a debug-level entry with a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogDebug(this IMajLogger logger, string tag, string message)
        {
            Forward(logger, LogLevel.Debug, tag, message, null);
        }

        /// <summary>
        /// Writes a debug-level entry with a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogDebug(this IMajLogger logger, string tag, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Debug, tag, message, args);
        }

        /// <summary>
        /// Writes a debug-level entry with a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogDebug(this IMajLogger logger, string tag, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Debug, tag, exception, message, args);
        }

        /// <summary>
        /// Writes an info-level entry without a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogInfo(this IMajLogger logger, string message)
        {
            Forward(logger, LogLevel.Info, null, message, null);
        }

        /// <summary>
        /// Writes an info-level entry without a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogInfo(this IMajLogger logger, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Info, null, message, args);
        }

        /// <summary>
        /// Writes an info-level entry without a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogInfo(this IMajLogger logger, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Info, null, exception, message, args);
        }

        /// <summary>
        /// Writes an info-level entry with a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogInfo(this IMajLogger logger, string tag, string message)
        {
            Forward(logger, LogLevel.Info, tag, message, null);
        }

        /// <summary>
        /// Writes an info-level entry with a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogInfo(this IMajLogger logger, string tag, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Info, tag, message, args);
        }

        /// <summary>
        /// Writes an info-level entry with a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogInfo(this IMajLogger logger, string tag, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Info, tag, exception, message, args);
        }

        /// <summary>
        /// Writes a warning-level entry without a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogWarning(this IMajLogger logger, string message)
        {
            Forward(logger, LogLevel.Warning, null, message, null);
        }

        /// <summary>
        /// Writes a warning-level entry without a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogWarning(this IMajLogger logger, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Warning, null, message, args);
        }

        /// <summary>
        /// Writes a warning-level entry without a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogWarning(this IMajLogger logger, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Warning, null, exception, message, args);
        }

        /// <summary>
        /// Writes a warning-level entry with a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogWarning(this IMajLogger logger, string tag, string message)
        {
            Forward(logger, LogLevel.Warning, tag, message, null);
        }

        /// <summary>
        /// Writes a warning-level entry with a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogWarning(this IMajLogger logger, string tag, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Warning, tag, message, args);
        }

        /// <summary>
        /// Writes a warning-level entry with a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogWarning(this IMajLogger logger, string tag, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Warning, tag, exception, message, args);
        }

        /// <summary>
        /// Writes an error-level entry without a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogError(this IMajLogger logger, string message)
        {
            Forward(logger, LogLevel.Error, null, message, null);
        }

        /// <summary>
        /// Writes an error-level entry without a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogError(this IMajLogger logger, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Error, null, message, args);
        }

        /// <summary>
        /// Writes an error-level entry without a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogError(this IMajLogger logger, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Error, null, exception, message, args);
        }

        /// <summary>
        /// Writes an error-level entry with a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogError(this IMajLogger logger, string tag, string message)
        {
            Forward(logger, LogLevel.Error, tag, message, null);
        }

        /// <summary>
        /// Writes an error-level entry with a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogError(this IMajLogger logger, string tag, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Error, tag, message, args);
        }

        /// <summary>
        /// Writes an error-level entry with a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogError(this IMajLogger logger, string tag, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Error, tag, exception, message, args);
        }

        /// <summary>
        /// Writes a fatal-level entry without a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogFatal(this IMajLogger logger, string message)
        {
            Forward(logger, LogLevel.Fatal, null, message, null);
        }

        /// <summary>
        /// Writes a fatal-level entry without a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogFatal(this IMajLogger logger, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Fatal, null, message, args);
        }

        /// <summary>
        /// Writes a fatal-level entry without a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogFatal(this IMajLogger logger, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Fatal, null, exception, message, args);
        }

        /// <summary>
        /// Writes a fatal-level entry with a tag.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogFatal(this IMajLogger logger, string tag, string message)
        {
            Forward(logger, LogLevel.Fatal, tag, message, null);
        }

        /// <summary>
        /// Writes a fatal-level entry with a tag, formatting <paramref name="message"/> with <paramref name="args"/>.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogFatal(this IMajLogger logger, string tag, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Fatal, tag, message, args);
        }

        /// <summary>
        /// Writes a fatal-level entry with a tag, together with the exception that caused it.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="tag">The tag that identifies the source, for example the name of the component that logs.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> to log the message alone.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        public static void LogFatal(this IMajLogger logger, string tag, Exception? exception, string message, params object?[] args)
        {
            Forward(logger, LogLevel.Fatal, tag, exception, message, args);
        }

        /// <summary>
        /// Forwards one entry that carries no exception to the <see cref="IMajLogger.Log(LogLevel, string)"/> overload that
        /// matches the tag and the format arguments it was given.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="level">The level to log at.</param>
        /// <param name="tag">The tag of the entry, or <see langword="null"/> to log without a tag.</param>
        /// <param name="message">The message to write, or the composite format string when <paramref name="args"/> holds any object.</param>
        /// <param name="args">The objects to format into <paramref name="message"/>, or <see langword="null"/> when there are none.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        private static void Forward(IMajLogger logger, LogLevel level, string? tag, string message, object?[]? args)
        {
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }
            if (args is null || args.Length == 0)
            {
                if (tag is null)
                {
                    logger.Log(level, message);
                }
                else
                {
                    logger.Log(level, tag, message);
                }
                return;
            }
            if (tag is null)
            {
                logger.Log(level, message, args);
            }
            else
            {
                logger.Log(level, tag, message, args);
            }
        }

        /// <summary>
        /// Forwards one entry that may carry an exception to the
        /// <see cref="IMajLogger.Log(LogLevel, Exception, string, object[])"/> overload that matches the tag it was given.
        /// An entry without an exception is forwarded by the overload above, so both overload groups behave alike for a
        /// <see langword="null"/> exception.
        /// </summary>
        /// <param name="logger">The logger that receives the entry.</param>
        /// <param name="level">The level to log at.</param>
        /// <param name="tag">The tag of the entry, or <see langword="null"/> to log without a tag.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> when the entry has none.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects to format into <paramref name="message"/>, or <see langword="null"/> when there are none.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <see langword="null"/>.</exception>
        private static void Forward(IMajLogger logger, LogLevel level, string? tag, Exception? exception, string message, object?[]? args)
        {
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }
            if (exception is null)
            {
                Forward(logger, level, tag, message, args);
                return;
            }
            var formatArgs = args ?? Array.Empty<object?>();
            if (tag is null)
            {
                logger.Log(level, exception, message, formatArgs);
            }
            else
            {
                logger.Log(level, tag, exception, message, formatArgs);
            }
        }
    }
}
