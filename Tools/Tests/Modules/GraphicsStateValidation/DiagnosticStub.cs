#nullable enable
namespace MajdataPlay.Diagnostics
{
    /// <summary>Forwards production warmup diagnostics to the isolated Player log.</summary>
    internal static class MajDebug
    {
        /// <summary>Writes a debug diagnostic.</summary>
        /// <param name="tag">The producing component.</param>
        /// <param name="message">The diagnostic text.</param>
        internal static void LogDebug(string tag, string message)
        {
            UnityEngine.Debug.Log(tag + ": " + message);
        }

        /// <summary>Writes a progress diagnostic.</summary>
        /// <param name="tag">The producing component.</param>
        /// <param name="message">The diagnostic text.</param>
        internal static void LogInfo(string tag, string message)
        {
            UnityEngine.Debug.Log(tag + ": " + message);
        }

        /// <summary>Writes a warning diagnostic.</summary>
        /// <param name="tag">The producing component.</param>
        /// <param name="message">The diagnostic text.</param>
        internal static void LogWarning(string tag, string message)
        {
            UnityEngine.Debug.LogWarning(tag + ": " + message);
        }
    }
}
