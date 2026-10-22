using Cysharp.Text;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Diagnostics
{
    public static class MajDebug
    {
        public static long MaxLogSize { get; set; } = 500L * 1024 * 1024; // 500 MB
        public static LogLevel MinLogLevel { get; set; } = LogLevel.Debug;

        private static ILogger s_unityLogger = null!;
        private static TextWriter? s_logWriter;
        private static readonly CancellationTokenSource s_cancellationTokenSource = new();

        private static readonly Utf16PreparedFormat<DateTime, char> s_logOutputFormat = ZString.PrepareUtf16<DateTime, char>("[{0:yyyy-MM-dd HH:mm:ss.ffff}][{1}]");

        private static readonly object s_lockObject = new();
        private static readonly ConcurrentQueue<GameLog> s_logQueue = new();


        public static void SetLogWriter(TextWriter? writer, bool disposeOld = true)
        {
            lock (s_lockObject)
            {
                if (disposeOld && s_logWriter != null)
                {
                    s_logWriter.Flush();
                    s_logWriter.Dispose();
                }
                s_logWriter = writer;
            }
        }

        public static void FlushLog()
        {
            lock (s_lockObject)
            {
                if (s_logWriter == null)
                {
                    return;
                }
                var sb = ZString.CreateStringBuilder();
                try
                {
                    WriteLogIntoStream(ref sb);
                    s_logWriter.Flush();
                }
                finally
                {
                    sb.Dispose();
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_unityLogger = UnityEngine.Debug.unityLogger;

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                LogException(args.Exception);
                args.SetObserved();
            };

            Application.quitting += () =>
            {
                s_cancellationTokenSource.Cancel();
                FlushLog();
            };

            StartLogWritebackTask();

            Application.logMessageReceivedThreaded += (string condition, string stackTrace, LogType type) =>
            {
                var sb = ZString.CreateStringBuilder();
                sb.Append(condition);
                var log = new GameLog()
                {
                    Date = DateTime.Now,
                    Condition = sb,
                    StackTrace = stackTrace,
                    Level = ToMajdataLogLevel(type),
                    IsFromUnityLogger = true
                };
                s_logQueue.Enqueue(log);
            };
        }

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Log<T>(string? tag, T obj, LogLevel level)
        {
            var sb = ZString.CreateStringBuilder();
            if (!string.IsNullOrEmpty(tag))
            {
                sb.Append('[');
                sb.Append(tag);
                sb.Append(']');
            }
            sb.Append(obj);
            if (obj is Exception)
            {
                sb.AppendLine();
            }
            var log = new GameLog()
            {
                Date = DateTime.Now,
                Condition = sb,
#if UNITY_EDITOR || DEBUG
                StackTrace = obj is not Exception ? GetStackTrack() : string.Empty,
#else
                StackTrace = string.Empty,
#endif
                Level = level
            };
            s_logQueue.Enqueue(log);
        }

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogDebug<T>(T obj) => Log(null, obj, LogLevel.Debug);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogDebug<T>(string tag, T obj) => Log(tag, obj, LogLevel.Debug);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogInfo<T>(T obj) => Log(null, obj, LogLevel.Info);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogInfo<T>(string tag, T obj) => Log(tag, obj, LogLevel.Info);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogWarning<T>(T obj) => Log(null, obj, LogLevel.Warning);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogWarning<T>(string tag, T obj) => Log(tag, obj, LogLevel.Warning);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError<T>(T obj) => Log(null, obj, LogLevel.Error);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError<T>(string tag, T obj) => Log(tag, obj, LogLevel.Error);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogException<T>(T obj) where T : Exception => Log(null, obj, LogLevel.Error);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogException<T>(string tag, T obj) where T : Exception
            => Log(tag, obj, LogLevel.Error);


        private static string GetStackTrack()
        {
            return new StackTrace(3, true).ToString();
        }

        private static void StartLogWritebackTask()
        {
            Task.Factory.StartNew(() =>
            {
                var currentThread = Thread.CurrentThread;
                currentThread.Priority = System.Threading.ThreadPriority.Lowest;
                currentThread.IsBackground = true;

                var token = s_cancellationTokenSource.Token;
                var sb = ZString.CreateStringBuilder();
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            WriteLogIntoStream(ref sb);
                        }
                        finally
                        {
                            Thread.Sleep(100);
                        }
                    }
                }
                finally
                {
                    sb.Dispose();
                    lock (s_lockObject)
                    {
                        s_logWriter?.Dispose();
                        s_logWriter = null;
                    }
                }
            }, TaskCreationOptions.LongRunning);
        }

        private static void WriteLogIntoStream(ref Utf16ValueStringBuilder sb)
        {
            lock (s_lockObject)
            {
                if (s_logWriter is null)
                {
                    while (s_logQueue.Count > 10000 && s_logQueue.TryDequeue(out var staleLog))
                    {
                        staleLog.Condition.Dispose();
                    }
                    return;
                }

                var hasWritten = false;
                while (s_logQueue.TryDequeue(out var log))
                {
                    using var condition = log.Condition;

                    if (log.Level < MinLogLevel)
                    {
                        continue;
                    }

                    if (MaxLogSize > 0 && s_logWriter is StreamWriter sw && sw.BaseStream.CanSeek)
                    {
                        if (sw.BaseStream.Position > MaxLogSize)
                        {
                            continue;
                        }
                    }

                    s_logOutputFormat.FormatTo(ref sb, log.Date, GetLogLevelChar(log.Level));
                    sb.Append(condition.AsSpan());

                    if (!string.IsNullOrEmpty(log.StackTrace))
                    {
                        sb.AppendLine();
                        sb.Append(log.StackTrace);
                        sb.AppendLine();
                    }

                    s_logWriter.WriteLine(sb.AsSpan());
                    hasWritten = true;

#if UNITY_EDITOR || DEBUG
                    if (!log.IsFromUnityLogger)
                    {
                        s_unityLogger.Log(ToUnityLogLevel(log.Level), sb.ToString());
                    }
#endif
                    sb.Clear();
                }

                if (hasWritten)
                {
                    s_logWriter.Flush();
                }
            }
        }

        private static LogType ToUnityLogLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => LogType.Log,
                LogLevel.Info => LogType.Log,
                LogLevel.Warning => LogType.Warning,
                LogLevel.Error => LogType.Error,
                LogLevel.Fatal => LogType.Error,
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }

        private static LogLevel ToMajdataLogLevel(LogType level)
        {
            return level switch
            {
                LogType.Log => LogLevel.Info,
                LogType.Warning => LogLevel.Warning,
                LogType.Error => LogLevel.Error,
                LogType.Exception => LogLevel.Error,
                _ => LogLevel.Debug
            };
        }

        private static char GetLogLevelChar(LogLevel level)
        {
            return level switch
            {
                LogLevel.Debug => 'D',
                LogLevel.Info => 'I',
                LogLevel.Warning => 'W',
                LogLevel.Error => 'E',
                LogLevel.Fatal => 'F',
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }

        private readonly struct GameLog
        {
            public DateTime Date { get; init; }
            public Utf16ValueStringBuilder Condition { get; init; }
            public string? StackTrace { get; init; }
            public LogLevel Level { get; init; }
            public bool IsFromUnityLogger { get; init; }
        }
    }
}
