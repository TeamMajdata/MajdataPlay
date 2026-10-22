using System;
using System.Buffers;
using System.Collections.Generic;
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
    /// Test double for the runtime logger that counts errors so every check can assert that the worker logged none.
    /// </summary>
    internal static class MajDebug
    {
        static int _errorCount;

        /// <summary>
        /// Gets how many errors and exceptions have been logged so far.
        /// </summary>
        public static int ErrorCount => Volatile.Read(ref _errorCount);

        /// <summary>
        /// Gets the most recent error message, for diagnostics when a check fails.
        /// </summary>
        public static string? LastError { get; private set; }

        /// <summary>
        /// Logs an informational message; discarded by the harness.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="obj">The logged object.</param>
        public static void LogInfo<T>(T obj)
        {
        }

        /// <summary>
        /// Logs a warning; discarded by the harness.
        /// </summary>
        /// <typeparam name="T">The type of the logged object.</typeparam>
        /// <param name="obj">The logged object.</param>
        public static void LogWarning<T>(T obj)
        {
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

        static void Record<T>(T obj)
        {
            LastError = obj?.ToString();
            Interlocked.Increment(ref _errorCount);
        }
    }
}

namespace MajdataPlay.Buffers
{
    /// <summary>
    /// Test double for the production pool that routes every rental through <see cref="TrackingArrayPool{T}"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    internal static class Pool<T>
    {
        /// <summary>
        /// Rents an array from the tracking pool.
        /// </summary>
        /// <param name="length">The minimum array length.</param>
        /// <param name="clearArrayWhenReturn">Whether the array is cleared when returned.</param>
        /// <returns>A pooled array lease.</returns>
        public static PooledArray<T> Rent(int length, bool clearArrayWhenReturn = false)
        {
            var pool = TrackingArrayPool<T>.Instance;
            return new PooledArray<T>(pool.Rent(length), pool, clearArrayWhenReturn);
        }
    }

    /// <summary>
    /// Array pool that reuses returned arrays (so a double return would hand one array to two owners), poisons returned
    /// arrays (so a read after return yields detectable bytes), and counts both kinds of misuse.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    internal sealed class TrackingArrayPool<T> : ArrayPool<T>
    {
        /// <summary>
        /// Gets the process-wide tracking pool.
        /// </summary>
        public static TrackingArrayPool<T> Instance { get; } = new();

        readonly object _sync = new();
        readonly HashSet<T[]> _outstanding = new(ReferenceEqualityComparer.Instance);
        readonly Stack<T[]> _available = new();

        /// <summary>
        /// Gets how many times an array that was not rented out was returned.
        /// </summary>
        public int DoubleReturns { get; private set; }

        /// <summary>
        /// Gets how many arrays are currently rented out.
        /// </summary>
        public int Outstanding
        {
            get
            {
                lock (_sync)
                {
                    return _outstanding.Count;
                }
            }
        }

        /// <summary>
        /// Rents an array, preferring a previously returned one.
        /// </summary>
        /// <param name="minimumLength">The minimum array length.</param>
        /// <returns>An array of at least <paramref name="minimumLength"/> elements.</returns>
        public override T[] Rent(int minimumLength)
        {
            lock (_sync)
            {
                var array = default(T[]);
                if (_available.Count > 0 && _available.Peek().Length >= minimumLength)
                {
                    array = _available.Pop();
                }
                array ??= new T[Math.Max(minimumLength, 16)];
                _outstanding.Add(array);
                return array;
            }
        }

        /// <summary>
        /// Returns an array and poisons its contents.
        /// </summary>
        /// <param name="array">The array to return.</param>
        /// <param name="clearArray">Ignored; returned arrays are always poisoned.</param>
        public override void Return(T[] array, bool clearArray = false)
        {
            lock (_sync)
            {
                if (!_outstanding.Remove(array))
                {
                    DoubleReturns++;
                    return;
                }
                if (array is byte[] bytes)
                {
                    bytes.AsSpan().Fill(Program.PoisonByte);
                }
                _available.Push(array);
            }
        }
    }
}
