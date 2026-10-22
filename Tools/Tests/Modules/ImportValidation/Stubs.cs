#nullable enable
using MajdataPlay.IO.Storage;
using MajdataPlay.Platform.Android.Runtime.Bindings.Android.Content;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Cysharp.Threading.Tasks
{
    /// <summary>Supplies the awaitable player-loop boundaries without a Unity synchronization context.</summary>
    internal static class UniTask
    {
        /// <summary>Gets an already completed operation for the production static initializer.</summary>
        public static ValueTask CompletedTask => ValueTask.CompletedTask;

        /// <summary>Completes the current-step yield synchronously so an import runs on the calling thread.</summary>
        /// <returns>A completed awaitable.</returns>
        public static Task Yield()
        {
            return Task.CompletedTask;
        }

        /// <summary>Completes a production delay immediately for deterministic validation.</summary>
        /// <param name="millisecondsDelay">The ignored requested delay.</param>
        /// <returns>A completed awaitable.</returns>
        public static Task Delay(int millisecondsDelay)
        {
            return Task.CompletedTask;
        }
    }
}

namespace UnityEngine
{
    /// <summary>Supplies the color value read by the production error path.</summary>
    internal struct Color
    {
        /// <summary>Gets the red color used to report a failed chart scan.</summary>
        public static Color red => default;
    }

    /// <summary>Supplies the Unity initialization phases used by the production attribute.</summary>
    internal enum RuntimeInitializeLoadType
    {
        /// <summary>Runs after subsystem registration.</summary>
        SubsystemRegistration,
    }

    /// <summary>Supplies the Unity runtime initializer attribute without loading Unity.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        /// <summary>Creates the marker for one initialization phase.</summary>
        /// <param name="loadType">The phase requesting the call.</param>
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
        {
        }
    }
}

namespace MajdataPlay
{
    /// <summary>Supplies the directories resolved by the production importer.</summary>
    internal static class MajEnv
    {
        /// <summary>Gets or sets the case-owned chart root that plays the role of <c>MaiCharts</c>.</summary>
        public static StorageDirectory ChartDirectory { get; set; } = null!;

        /// <summary>Gets or sets the case-owned extraction root.</summary>
        public static StorageDirectory TempDirectory { get; set; } = null!;
    }

    /// <summary>Supplies the scene identifiers compared by the production importer.</summary>
    internal enum MajScenes
    {
        /// <summary>The startup scene.</summary>
        Init,
        /// <summary>The title scene.</summary>
        Title,
        /// <summary>The login scene.</summary>
        Login,
        /// <summary>The loading scene used during an import.</summary>
        Empty,
        /// <summary>The chart list scene.</summary>
        List,
    }

    /// <summary>Records the scene operations requested by the production importer.</summary>
    internal sealed class SceneSwitcher
    {
        /// <summary>Gets or sets the scene reported to the production importer.</summary>
        public static MajScenes CurrentScene { get; set; } = MajScenes.List;

        /// <summary>Gets the last text the production importer displayed.</summary>
        public string LastLoadingText { get; private set; } = string.Empty;

        /// <summary>Completes the fade immediately.</summary>
        /// <returns>A completed awaitable.</returns>
        public Task FadeInAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>Completes the requested scene change immediately.</summary>
        /// <param name="sceneName">The requested scene name.</param>
        /// <param name="fade">Whether the request asked for a fade.</param>
        /// <returns>A completed awaitable.</returns>
        public Task SwitchSceneAsync(string sceneName, bool fade = true)
        {
            return Task.CompletedTask;
        }

        /// <summary>Records the loading text set by the production importer.</summary>
        /// <param name="text">The requested loading text.</param>
        public void SetLoadingText(string text)
        {
            LastLoadingText = text;
        }

        /// <summary>Records the loading text and color set by the production importer.</summary>
        /// <param name="text">The requested loading text.</param>
        /// <param name="color">The requested text color.</param>
        public void SetLoadingText(string text, Color color)
        {
            LastLoadingText = text;
        }
    }

    /// <summary>Supplies the singleton accessor used by the production importer.</summary>
    internal static class MajInstances
    {
        /// <summary>Gets the recording scene switcher.</summary>
        public static SceneSwitcher SceneSwitcher { get; } = new SceneSwitcher();
    }

    /// <summary>Records the local chart list refresh requested by the production importer.</summary>
    internal static class SongStorage
    {
        /// <summary>Defines the chart metadata filename used by the import fixtures.</summary>
        public const string ChartFileName = "maidata.txt";

        /// <summary>Supplies the supported track filenames inspected by the production importer.</summary>
        public static readonly string[] SupportedTrackFileNames = new string[]
        {
            "track.mp3",
            "track.opus",
            "track.ogg",
            "track.aac",
            "track.wav"
        };

        /// <summary>Gets the number of completed local chart list refreshes.</summary>
        public static int RefreshCount { get; private set; }

        /// <summary>Counts a local refresh request without scanning any chart.</summary>
        /// <param name="progressReporter">The ignored progress reporter.</param>
        /// <param name="cancellationToken">The token that cancels the refresh before it is counted.</param>
        /// <returns>A completed awaitable.</returns>
        /// <exception cref="OperationCanceledException">The supplied cancellation token has been canceled.</exception>
        internal static Task RefreshLocal(IProgress<string>? progressReporter = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Supplies the localization extension used by the production error path.</summary>
    internal static class StringExtensions
    {
        /// <summary>Returns the translation key unchanged.</summary>
        /// <param name="origin">The translation key.</param>
        /// <param name="args">The ignored format arguments.</param>
        /// <returns>The supplied key.</returns>
        public static string i18n(this string origin, params object[] args)
        {
            return origin;
        }
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Records the diagnostics reported by the production importer.</summary>
    internal static class MajDebug
    {
        private static readonly List<string> s_errors = new List<string>();
        private static readonly List<string> s_warnings = new List<string>();

        /// <summary>Gets the recorded error messages.</summary>
        public static IReadOnlyList<string> Errors => s_errors;

        /// <summary>Gets the recorded warning messages.</summary>
        public static IReadOnlyList<string> Warnings => s_warnings;

        /// <summary>Discards the diagnostics recorded by the previous case.</summary>
        public static void Clear()
        {
            s_errors.Clear();
            s_warnings.Clear();
        }

        /// <summary>Ignores one debug message.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="message">The ignored message.</param>
        public static void LogDebug<T>(T message)
        {
        }

        /// <summary>Ignores one informational message.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="message">The ignored message.</param>
        public static void LogInfo<T>(T message)
        {
        }

        /// <summary>Records one warning message.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="message">The recorded message.</param>
        public static void LogWarning<T>(T message)
        {
            s_warnings.Add(message?.ToString() ?? string.Empty);
        }

        /// <summary>Records one tagged warning message.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="tag">The ignored diagnostic tag.</param>
        /// <param name="message">The recorded message.</param>
        public static void LogWarning<T>(string tag, T message)
        {
            s_warnings.Add(message?.ToString() ?? string.Empty);
        }

        /// <summary>Records one error message.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="message">The recorded message.</param>
        public static void LogError<T>(T message)
        {
            s_errors.Add(message?.ToString() ?? string.Empty);
        }

        /// <summary>Records one tagged error message.</summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="tag">The ignored diagnostic tag.</param>
        /// <param name="message">The recorded message.</param>
        public static void LogError<T>(string tag, T message)
        {
            s_errors.Add(message?.ToString() ?? string.Empty);
        }

        /// <summary>Records one exception.</summary>
        /// <typeparam name="T">The exception type.</typeparam>
        /// <param name="exception">The recorded exception.</param>
        public static void LogException<T>(T exception) where T : Exception
        {
            s_errors.Add(exception.ToString());
        }
    }
}

namespace MajdataPlay.Platform.Android.Runtime.Bindings.Android.Content
{
    /// <summary>Supplies the intent surface read by the production importer.</summary>
    internal sealed class Intent
    {
        /// <summary>Returns no data URI for these storage cases.</summary>
        /// <returns>Always null.</returns>
        public string? GetDataString()
        {
            return null;
        }
    }
}

namespace MajdataPlay.Platform.Android
{
    /// <summary>Supplies the new-intent event subscribed by the production initializer.</summary>
    internal static class AndroidRuntime
    {
        /// <summary>Occurs when the activity receives a new intent.</summary>
        public static event EventHandler<Intent?>? OnNewIntent;

        /// <summary>Raises the event for a later intent regression.</summary>
        /// <param name="intent">The delivered intent.</param>
        public static void Raise(Intent? intent)
        {
            OnNewIntent?.Invoke(null, intent);
        }
    }
}
