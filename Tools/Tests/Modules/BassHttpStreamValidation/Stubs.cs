using System;
using System.Threading;

namespace AOT
{
    /// <summary>
    /// Test double for Unity's IL2CPP reverse P/Invoke marker; CoreCLR does not need it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        /// <summary>
        /// Creates the marker.
        /// </summary>
        /// <param name="type">The delegate type of the callback; unused.</param>
        public MonoPInvokeCallbackAttribute(Type type)
        {
        }
    }
}

namespace UnityEngine
{
    /// <summary>
    /// Test double for Unity's load-time initialization marker.
    /// </summary>
    internal enum RuntimeInitializeLoadType
    {
        BeforeSceneLoad,
    }

    /// <summary>
    /// Test double for Unity's load-time initialization attribute; the harness never invokes the marked method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        /// <summary>
        /// Creates the marker.
        /// </summary>
        /// <param name="loadType">When Unity would invoke the method; unused.</param>
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
        {
        }
    }

    /// <summary>
    /// Test double for the Unity application paths used by the Android CA bundle lookup.
    /// </summary>
    internal static class Application
    {
        /// <summary>
        /// Gets a writable directory; only referenced by the Android code path.
        /// </summary>
        public static string persistentDataPath => AppContext.BaseDirectory;
    }
}

namespace MajdataPlay.IO.Storage
{
    /// <summary>
    /// Keeps the production <c>using MajdataPlay.IO.Storage</c> directive resolvable; the storage API is only used on Android.
    /// </summary>
    internal static class StorageNamespaceAnchor
    {
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>
    /// Test double for the runtime logger that counts warnings and errors so checks can assert on them.
    /// </summary>
    internal static class MajDebug
    {
        static int s_errorCount;
        static int s_warningCount;

        /// <summary>
        /// Gets how many errors and exceptions have been logged so far.
        /// </summary>
        public static int ErrorCount => Volatile.Read(ref s_errorCount);

        /// <summary>
        /// Gets how many warnings have been logged so far.
        /// </summary>
        public static int WarningCount => Volatile.Read(ref s_warningCount);

        /// <summary>
        /// Gets the most recent error message, for diagnostics when a check fails.
        /// </summary>
        public static string? LastError { get; private set; }

        /// <summary>
        /// Logs a debug message; discarded by the harness.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="tag">The log category.</param>
        /// <param name="obj">The logged object.</param>
        public static void LogDebug<T>(string tag, T obj)
        {
        }

        /// <summary>
        /// Logs an informational message; discarded by the harness.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="obj">The logged object.</param>
        public static void LogInfo<T>(T obj)
        {
        }

        /// <summary>
        /// Logs an informational message; discarded by the harness.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="tag">The log category.</param>
        /// <param name="obj">The logged object.</param>
        public static void LogInfo<T>(string tag, T obj)
        {
        }

        /// <summary>
        /// Counts a warning.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="obj">The logged object.</param>
        public static void LogWarning<T>(T obj)
        {
            Interlocked.Increment(ref s_warningCount);
        }

        /// <summary>
        /// Counts a warning.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="tag">The log category.</param>
        /// <param name="obj">The logged object.</param>
        public static void LogWarning<T>(string tag, T obj)
        {
            Interlocked.Increment(ref s_warningCount);
        }

        /// <summary>
        /// Records an error.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="obj">The logged object.</param>
        public static void LogError<T>(T obj)
        {
            Record(obj);
        }

        /// <summary>
        /// Records an exception.
        /// </summary>
        /// <typeparam name="T">The exception type.</typeparam>
        /// <param name="obj">The logged exception.</param>
        public static void LogException<T>(T obj) where T : Exception
        {
            Record(obj);
        }

        /// <summary>
        /// Records an exception.
        /// </summary>
        /// <typeparam name="T">The exception type.</typeparam>
        /// <param name="tag">The log category.</param>
        /// <param name="obj">The logged exception.</param>
        public static void LogException<T>(string tag, T obj) where T : Exception
        {
            Record(obj);
        }

        static void Record<T>(T obj)
        {
            LastError = obj?.ToString();
            Interlocked.Increment(ref s_errorCount);
        }
    }
}

namespace MajdataPlay.Buffers
{
    /// <summary>
    /// Test double for the production pool, backed by the shared array pool.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    internal static class Pool<T>
    {
        /// <summary>
        /// Rents an array from the shared pool.
        /// </summary>
        /// <param name="length">The minimum array length.</param>
        /// <param name="clearArrayWhenReturn">Whether the array is cleared when returned.</param>
        /// <returns>A pooled array lease.</returns>
        public static PooledArray<T> Rent(int length, bool clearArrayWhenReturn = false)
        {
            return new PooledArray<T>(length, clearArrayWhenReturn);
        }
    }
}
