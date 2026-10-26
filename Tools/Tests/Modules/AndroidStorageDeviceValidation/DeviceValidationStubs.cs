#nullable enable
namespace MajdataPlay.Diagnostics
{
    /// <summary>Forwards isolated lifecycle diagnostics to the test Player log without game services.</summary>
    public static class MajDebug
    {
        /// <summary>Reports lifecycle progress to the device's Unity log.</summary>
        /// <param name="message">The production diagnostic message.</param>
        public static void LogInfo(string message)
        {
            UnityEngine.Debug.Log(message);
        }

        /// <summary>Reports a tagged lifecycle diagnostic to the device's Unity log.</summary>
        /// <param name="tag">The production diagnostic context.</param>
        /// <param name="message">The production diagnostic message.</param>
        public static void LogInfo(string tag, string message)
        {
            UnityEngine.Debug.Log(tag + ": " + message);
        }

        /// <summary>Preserves observable Java initialization failures in the device test log.</summary>
        /// <param name="message">The production failure diagnostic.</param>
        public static void LogError(string message)
        {
            UnityEngine.Debug.LogError(message);
        }
    }
}
