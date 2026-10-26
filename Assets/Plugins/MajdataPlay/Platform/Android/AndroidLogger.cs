#if UNITY_ANDROID && !UNITY_EDITOR
#define ANDROID_RUNTIME
#endif
using MajdataPlay.Diagnostics;
using System;
using UnityEngine;
#if ANDROID_RUNTIME
using AndroidLog = MajdataPlay.Platform.Android.Runtime.Bindings.Android.Util.Log;
#endif

#nullable enable
namespace MajdataPlay.Platform.Android
{
    /// <summary>
    /// Forwards <see cref="IMajLogger"/> entries to the Android log, so a running Android player can be inspected with
    /// adb logcat.
    /// </summary>
    /// <remarks>
    /// The tag passed to the constructor is the logcat tag shared by every entry this logger writes. The tag of an entry
    /// that comes from the <see cref="IMajLogger"/> overloads is prefixed to the message instead, because logcat keeps a
    /// single tag per entry and the constructor tag is the one that identifies the logger. Outside an Android player the
    /// entries are written to the Unity log instead, so the same code path stays observable in the Editor. An entry that
    /// is written from a background thread attaches that thread to the JVM on first use.
    /// </remarks>
    internal sealed class AndroidLogger : IMajLogger
    {
        /// <summary>The logcat tag used by the parameterless constructor.</summary>
        public const string DefaultTag = "MajdataPlay";

        /// <summary>Gets the logcat tag that every entry written by this logger carries.</summary>
        public string Tag { get; }

#if ANDROID_RUNTIME
        /// <summary>
        /// Tracks whether the calling thread is already attached to the JVM, so an entry written from that thread does not
        /// repeat the attach. Log entries commonly come from the background log-writer thread, which Unity does not attach
        /// itself.
        /// </summary>
        [ThreadStatic]
        private static bool t_isThreadAttached;
#endif

        /// <summary>Initializes a logger that writes every entry with <see cref="DefaultTag"/>.</summary>
        public AndroidLogger()
            : this(DefaultTag)
        {
        }

        /// <summary>Initializes a logger that writes every entry with the given logcat tag.</summary>
        /// <param name="tag">The logcat tag that identifies this logger in adb logcat.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tag"/> is <see langword="null"/>.</exception>
        public AndroidLogger(string tag)
        {
            if (tag is null)
            {
                throw new ArgumentNullException(nameof(tag));
            }
            Tag = tag;
        }

        /// <summary>Writes an entry without a tag and without an exception.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        public void Log(LogLevel level, string message)
        {
            Write(level, null, null, message, null);
        }

        /// <summary>Writes an entry without a tag and without an exception, formatting <paramref name="message"/> with <paramref name="args"/>.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>; when empty, <paramref name="message"/> is written as it is.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        /// <exception cref="FormatException">Thrown when <paramref name="message"/> is not a valid format string for <paramref name="args"/>.</exception>
        public void Log(LogLevel level, string message, params object?[] args)
        {
            Write(level, null, null, message, args);
        }

        /// <summary>Writes an entry without a tag, together with the exception that caused it.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> when the entry has none.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>; when empty, <paramref name="message"/> is written as it is.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        /// <exception cref="FormatException">Thrown when <paramref name="message"/> is not a valid format string for <paramref name="args"/>.</exception>
        public void Log(LogLevel level, Exception? exception, string message, params object?[] args)
        {
            Write(level, null, exception, message, args);
        }

        /// <summary>Writes a tagged entry without an exception.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="tag">The tag of the entry, which is prefixed to the message.</param>
        /// <param name="message">The message to write.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        public void Log(LogLevel level, string tag, string message)
        {
            Write(level, tag, null, message, null);
        }

        /// <summary>Writes a tagged entry without an exception, formatting <paramref name="message"/> with <paramref name="args"/>.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="tag">The tag of the entry, which is prefixed to the message.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>; when empty, <paramref name="message"/> is written as it is.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        /// <exception cref="FormatException">Thrown when <paramref name="message"/> is not a valid format string for <paramref name="args"/>.</exception>
        public void Log(LogLevel level, string tag, string message, params object?[] args)
        {
            Write(level, tag, null, message, args);
        }

        /// <summary>Writes a tagged entry, together with the exception that caused it.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="tag">The tag of the entry, which is prefixed to the message.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> when the entry has none.</param>
        /// <param name="message">The composite format string of the entry.</param>
        /// <param name="args">The objects format-inserted into <paramref name="message"/>; when empty, <paramref name="message"/> is written as it is.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        /// <exception cref="FormatException">Thrown when <paramref name="message"/> is not a valid format string for <paramref name="args"/>.</exception>
        public void Log(LogLevel level, string tag, Exception? exception, string message, params object?[] args)
        {
            Write(level, tag, exception, message, args);
        }

        /// <summary>Formats one entry and writes it to logcat or, outside an Android player, to the Unity log.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="tag">The tag of the entry, or <see langword="null"/> when the entry has none.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> when there is none.</param>
        /// <param name="message">The message of the entry, or its composite format string when <paramref name="args"/> holds any object.</param>
        /// <param name="args">The objects to format into <paramref name="message"/>, or <see langword="null"/> when there are none.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        /// <exception cref="FormatException">Thrown when <paramref name="message"/> is not a valid format string for <paramref name="args"/>.</exception>
        private void Write(LogLevel level, string? tag, Exception? exception, string message, object?[]? args)
        {
            var text = ComposeMessage(tag, exception, message, args);
#if ANDROID_RUNTIME
            WriteToAndroid(level, text);
#else
            WriteToUnity(level, text);
#endif
        }

        /// <summary>Builds the text of one entry from its tag, exception, message and format arguments.</summary>
        /// <param name="tag">The tag of the entry, or <see langword="null"/> when the entry has none.</param>
        /// <param name="exception">The exception that caused the entry, or <see langword="null"/> when there is none.</param>
        /// <param name="message">The message of the entry, or its composite format string when <paramref name="args"/> holds any object.</param>
        /// <param name="args">The objects to format into <paramref name="message"/>, or <see langword="null"/> when there are none.</param>
        /// <returns>The text of the entry, prefixed with the entry tag and followed by the exception when it has one.</returns>
        /// <exception cref="FormatException">Thrown when <paramref name="message"/> is not a valid format string for <paramref name="args"/>.</exception>
        private static string ComposeMessage(string? tag, Exception? exception, string message, object?[]? args)
        {
            var text = args is null || args.Length == 0 ? message : string.Format(message, args);
            if (tag is not null)
            {
                text = string.Concat("[", tag, "]", text);
            }
            if (exception is not null)
            {
                text = string.Concat(text, Environment.NewLine, exception.ToString());
            }
            return text;
        }

#if ANDROID_RUNTIME
        /// <summary>Writes one composed entry to the Android log through the generated <c>android.util.Log</c> binding.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="text">The composed entry.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        private void WriteToAndroid(LogLevel level, string text)
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                WriteToUnity(level, text);
                return;
            }
            EnsureThreadAttached();
            switch (level)
            {
                case LogLevel.Debug:
                    AndroidLog.D(Tag, text);
                    break;
                case LogLevel.Info:
                    AndroidLog.I(Tag, text);
                    break;
                case LogLevel.Warning:
                    AndroidLog.W(Tag, text);
                    break;
                case LogLevel.Error:
                    AndroidLog.E(Tag, text);
                    break;
                case LogLevel.Fatal:
                    AndroidLog.Wtf(Tag, text);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }

        /// <summary>
        /// Attaches the calling thread to the JVM once, which the generated bindings require before any JNI access from a
        /// thread that Unity did not attach itself.
        /// </summary>
        private static void EnsureThreadAttached()
        {
            if (t_isThreadAttached)
            {
                return;
            }
            AndroidJNI.AttachCurrentThread();
            t_isThreadAttached = true;
        }
#endif

        /// <summary>Writes one composed entry to the Unity log, which is what an Editor or a portable host can observe.</summary>
        /// <param name="level">The level to write the entry at.</param>
        /// <param name="text">The composed entry.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="level"/> is not a defined level.</exception>
        private static void WriteToUnity(LogLevel level, string text)
        {
            var logType = level switch
            {
                LogLevel.Debug => LogType.Log,
                LogLevel.Info => LogType.Log,
                LogLevel.Warning => LogType.Warning,
                LogLevel.Error => LogType.Error,
                LogLevel.Fatal => LogType.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
            Debug.unityLogger.Log(logType, text);
        }
    }
}
