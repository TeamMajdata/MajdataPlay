#nullable enable

namespace MajdataPlay.Diagnostics
{
    /// <summary>A narrow logging double for the isolated AndroidRuntime compilation.</summary>
    public static class MajDebug
    {
        /// <summary>Accepts an informational message without initializing unrelated game services.</summary>
        /// <param name="message">The ignored message.</param>
        public static void LogInfo(string message)
        {
        }

        /// <summary>Accepts an error message without initializing unrelated game services.</summary>
        /// <param name="message">The ignored message.</param>
        public static void LogError(string message)
        {
        }
    }
}

namespace MajdataPlay.Platform.Android.IO
{
    /// <summary>A keyboard initialization double; keyboard hardware is outside storage validation.</summary>
    public static class AndroidKeyboard
    {
        /// <summary>Leaves keyboard callbacks unregistered in the isolated project.</summary>
        public static void Init()
        {
        }
    }
}
