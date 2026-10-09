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

        /// <summary>Preserves observable Java initialization failures in the device test log.</summary>
        /// <param name="message">The production failure diagnostic.</param>
        public static void LogError(string message)
        {
            UnityEngine.Debug.LogError(message);
        }
    }
}

namespace MajdataPlay.Platform.Android.IO
{
    /// <summary>Leaves keyboard hardware initialization outside the storage/JNI validation scope.</summary>
    public static class AndroidKeyboard
    {
        /// <summary>Does not register an unrelated physical keyboard callback.</summary>
        public static void Init()
        {
        }
    }
}
