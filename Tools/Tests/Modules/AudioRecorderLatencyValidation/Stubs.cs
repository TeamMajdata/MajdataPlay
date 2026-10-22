#nullable enable
using System;
using System.Threading;

namespace AOT
{
    /// <summary>Test double for Unity's IL2CPP reverse P/Invoke marker; CoreCLR does not need it.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        /// <summary>Creates the marker.</summary>
        /// <param name="type">The delegate type of the callback; unused.</param>
        public MonoPInvokeCallbackAttribute(Type type)
        {
        }
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Test double for the runtime logger that prints messages and counts warnings and errors.</summary>
    internal static class MajDebug
    {
        /// <summary>Counts logged warnings.</summary>
        private static int s_warningCount;
        /// <summary>Counts logged errors.</summary>
        private static int s_errorCount;

        /// <summary>Gets how many warnings have been logged.</summary>
        public static int WarningCount => Volatile.Read(ref s_warningCount);
        /// <summary>Gets how many errors have been logged.</summary>
        public static int ErrorCount => Volatile.Read(ref s_errorCount);

        /// <summary>Prints an informational message.</summary>
        /// <param name="tag">The logging category.</param>
        /// <param name="message">The message.</param>
        public static void LogInfo(string tag, string message)
        {
            Console.WriteLine($"  [Info][{tag}] {message}");
        }

        /// <summary>Prints and counts a warning.</summary>
        /// <param name="tag">The logging category.</param>
        /// <param name="message">The message.</param>
        public static void LogWarning(string tag, string message)
        {
            Interlocked.Increment(ref s_warningCount);
            Console.WriteLine($"  [Warning][{tag}] {message}");
        }

        /// <summary>Prints and counts an error.</summary>
        /// <param name="tag">The logging category.</param>
        /// <param name="message">The message.</param>
        public static void LogError(string tag, string message)
        {
            Interlocked.Increment(ref s_errorCount);
            Console.Error.WriteLine($"  [Error][{tag}] {message}");
        }
    }
}
