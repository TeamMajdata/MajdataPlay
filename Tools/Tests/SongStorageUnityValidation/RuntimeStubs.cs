#nullable enable
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajdataPlay.IO.Storage;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MajdataPlay
{
    /// <summary>Supplies only sort/filter values consumed by the real collection.</summary>
    public sealed class SongOrder
    {
        /// <summary>Gets or sets the search keyword.</summary>
        public string Keyword { get; set; } = string.Empty;
        /// <summary>Gets or sets the ordering mode.</summary>
        public SortType SortBy { get; set; }
    }

    /// <summary>Supplies the local playlist shape, without gameplay rules.</summary>
    public sealed class DanInfo
    {
        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; } = null!;
        /// <summary>Gets or sets the description.</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Gets or sets the ordered chart hashes.</summary>
        public string[] SongHashs { get; set; } = null!;
        /// <summary>Gets or sets whether this is a playlist rather than a course.</summary>
        public bool IsPlayList { get; set; }
        /// <summary>Gets or sets the unused gameplay option.</summary>
        public bool IsForceGameover { get; set; }
    }

    /// <summary>Supplies isolated fixture paths instead of initializing global game services.</summary>
    internal static class MajEnv
    {
        /// <summary>Gets or sets the disposable chart root.</summary>
        public static StorageDirectory ChartDirectory { get; set; } = null!;
        /// <summary>Gets or sets the disposable cache root.</summary>
        public static StorageDirectory CacheDirectory { get; set; } = null!;
        /// <summary>Gets the runtime cache directory below the disposable cache root, created on demand like production initialization.</summary>
        public static StorageDirectory RuntimeCacheDirectory
        {
            get
            {
                return CacheDirectory.CreateDirectory("Runtime");
            }
        }
        /// <summary>Gets the supported track extensions.</summary>
        public static string[] SupportedAudioFormats { get; } = new string[] { ".mp3", ".opus", ".ogg", ".aac", ".wav" };
        /// <summary>Gets the supported video extensions.</summary>
        public static string[] SupportedVideoFormats { get; } = new string[] { ".webm", ".mkv", ".mp4", ".mov" };
        /// <summary>Defines the unused download-buffer size required to compile the real online implementation.</summary>
        public const int HTTP_BUFFER_SIZE = 8192;
        /// <summary>Disables retries if a forbidden download accidentally reaches the rejecting HTTP boundary.</summary>
        public const int HTTP_REQUEST_MAX_RETRY = 0;
        /// <summary>Gets an HTTP client whose handler rejects every request before any socket is opened.</summary>
        public static HttpClient SharedHttpClient { get; } = new HttpClient(new ValidationHttpHandler());
        /// <summary>Gets the offline settings.</summary>
        public static ValidationSettings Settings { get; } = new ValidationSettings();
        /// <summary>Gets the mutable selection configuration.</summary>
        public static ValidationRuntimeConfig RuntimeConfig { get; } = new ValidationRuntimeConfig();
        /// <summary>Gets or sets the explicitly supplied offline endpoint list.</summary>
        public static ApiEndpoint[] ApiEndpoints { get; set; } = Array.Empty<ApiEndpoint>();
    }

    /// <summary>Supplies the online-enable settings boundary.</summary>
    internal sealed class ValidationSettings
    {
        /// <summary>Gets the online settings.</summary>
        public ValidationOnlineSettings Online { get; } = new ValidationOnlineSettings();
    }

    /// <summary>Keeps all network requests disabled.</summary>
    internal sealed class ValidationOnlineSettings
    {
        /// <summary>Gets or sets whether online access is enabled.</summary>
        public bool Enable { get; set; }
    }

    /// <summary>Supplies only the runtime list configuration.</summary>
    internal sealed class ValidationRuntimeConfig
    {
        /// <summary>Gets the runtime list values.</summary>
        public ValidationListConfig List { get; } = new ValidationListConfig();
    }

    /// <summary>Stores the selection and ordering values used by storage refresh.</summary>
    internal sealed class ValidationListConfig
    {
        /// <summary>Gets or sets the selected collection index.</summary>
        public int SelectedDir { get; set; }
        /// <summary>Gets or sets the selected chart index.</summary>
        public int SelectedSongIndex { get; set; }
        /// <summary>Gets or sets the stable selected collection identity.</summary>
        public Guid SelectedDirGuid { get; set; } = Guid.Empty;
        /// <summary>Gets or sets the stable selected chart hash.</summary>
        public string SelectedSongHash { get; set; } = string.Empty;
        /// <summary>Gets or sets the selected difficulty.</summary>
        public int SelectedDiff { get; set; }
        /// <summary>Gets the saved ordering.</summary>
        public SongOrder OrderBy { get; } = new SongOrder();
    }

    /// <summary>Provides event boundaries without creating a scene or application lifecycle.</summary>
    internal static class GameManager
    {
        /// <summary>Stores save subscriptions made by real SongStorage.</summary>
        public static event EventHandler? OnSave
        {
            add
            {
                s_onSave += value;
            }
            remove
            {
                s_onSave -= value;
            }
        }
        /// <summary>Stores shutdown subscriptions made by real SongStorage.</summary>
        public static event EventHandler? OnAppQuit
        {
            add
            {
                s_onAppQuit += value;
            }
            remove
            {
                s_onAppQuit -= value;
            }
        }

        /// <summary>Stores save callbacks without raising scene events.</summary>
        private static EventHandler? s_onSave;
        /// <summary>Stores shutdown callbacks without closing the main project.</summary>
        private static EventHandler? s_onAppQuit;
    }

    /// <summary>Rejects native audio access, which metadata scans must not request.</summary>
    internal static class MajInstances
    {
        /// <summary>Gets the rejecting audio boundary.</summary>
        public static ValidationAudioManager AudioManager { get; } = new ValidationAudioManager();
    }

    /// <summary>Compiles SongDetail's unused audio-loading methods.</summary>
    internal sealed class ValidationAudioManager
    {
        /// <summary>Rejects audio loading outside metadata validation.</summary>
        /// <param name="path">The unused audio path.</param>
        /// <param name="stream">The unused streaming flag.</param>
        /// <param name="cache">The unused caching flag.</param>
        /// <returns>No result; native audio is forbidden.</returns>
        /// <exception cref="NotSupportedException">Audio loading was requested.</exception>
        public Task<AudioSampleWrap> LoadMusicAsync(string path, bool stream, bool cache)
        {
            throw new NotSupportedException("Native audio is outside the metadata harness.");
        }

        /// <summary>Rejects remote preview audio without opening a network connection or loading native media.</summary>
        /// <param name="uri">The unused remote audio URI.</param>
        /// <returns>No result; preview audio is forbidden.</returns>
        /// <exception cref="NotSupportedException">Preview audio loading was requested.</exception>
        public Task<AudioSampleWrap> LoadMusicFromUriAsync(Uri uri)
        {
            throw new NotSupportedException("Preview audio is outside the metadata harness.");
        }
    }

    /// <summary>Supplies the unused empty-cover reference without a game scene.</summary>
    internal sealed class GameRuntime
    {
        /// <summary>Gets the rejecting sprite boundary.</summary>
        public ValidationSprites Sprite { get; } = new ValidationSprites();
    }

    /// <summary>Rejects cover access instead of silently claiming graphical validation.</summary>
    internal sealed class ValidationSprites
    {
        /// <summary>Gets no cover because image loading is outside the harness.</summary>
        /// <exception cref="NotSupportedException">A cover was requested.</exception>
        public Sprite EmptySongCover => throw new NotSupportedException("Covers are outside the metadata harness.");
    }
}

namespace MajdataPlay.IO
{
    /// <summary>Compiles SongDetail without loading a native audio library.</summary>
    public sealed class AudioSampleWrap : IDisposable
    {
        /// <summary>Gets an empty audio sentinel for production cache-only branches; no native audio is loaded.</summary>
        public static AudioSampleWrap Empty { get; } = new AudioSampleWrap();
        /// <summary>Releases no native resources.</summary>
        public void Dispose()
        {
        }

        /// <summary>Completes without native cleanup.</summary>
        /// <returns>An already completed cleanup operation.</returns>
        public ValueTask DisposeAsync()
        {
            return default;
        }
    }
}

namespace MajdataPlay.Drawing
{
    /// <summary>Rejects image decoding, which is not part of metadata validation.</summary>
    internal static class SpriteLoader
    {
        /// <summary>Rejects sprite loading.</summary>
        /// <param name="path">The unused image path.</param>
        /// <param name="compressed">The unused compression flag.</param>
        /// <param name="token">The unused cancellation token.</param>
        /// <returns>No sprite; this operation is forbidden.</returns>
        /// <exception cref="NotSupportedException">Image decoding was requested.</exception>
        public static Task<Sprite> LoadFromFileAsync(string path, bool compressed, CancellationToken token)
        {
            throw new NotSupportedException("Image decoding is outside the metadata harness.");
        }
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Forwards scan diagnostics to the isolated Editor log.</summary>
    internal static class MajDebug
    {
        /// <summary>Logs a scan status message.</summary>
        /// <param name="message">The non-sensitive fixture diagnostic.</param>
        public static void LogInfo(object? message)
        {
            Debug.Log(message);
        }

        /// <summary>Logs a scan failure without initializing the game logger.</summary>
        /// <param name="message">The fixture failure diagnostic.</param>
        public static void LogError(object? message)
        {
            Debug.LogError(message);
        }

        /// <summary>Logs a contextual fixture failure without initializing production logging.</summary>
        /// <param name="category">The production diagnostic category.</param>
        /// <param name="message">The non-sensitive fixture diagnostic.</param>
        public static void LogError(object? category, object? message)
        {
            Debug.LogError(category + ": " + message);
        }

        /// <summary>Logs an unused online diagnostic while retaining its production category.</summary>
        /// <param name="category">The production diagnostic category.</param>
        /// <param name="message">The fixture diagnostic.</param>
        public static void LogDebug(object? category, object? message)
        {
            Debug.Log(category + ": " + message);
        }

        /// <summary>Logs an unused online warning.</summary>
        /// <param name="message">The fixture warning.</param>
        public static void LogWarning(object? message)
        {
            Debug.LogWarning(message);
        }

        /// <summary>Logs an unused online warning with its production category.</summary>
        /// <param name="category">The production diagnostic category.</param>
        /// <param name="message">The fixture warning.</param>
        public static void LogWarning(object? category, object? message)
        {
            Debug.LogWarning(category + ": " + message);
        }

        /// <summary>Logs a fixture exception.</summary>
        /// <param name="exception">The observed exception.</param>
        public static void LogException(Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}

namespace MajdataPlay.Numerics
{
    /// <summary>Supplies only the integer clamping operation used by collections.</summary>
    internal static class ValidationNumericExtensions
    {
        /// <summary>Clamps a value to inclusive bounds.</summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="minimum">The lower bound.</param>
        /// <param name="maximum">The upper bound.</param>
        /// <returns>The clamped value.</returns>
        public static int Clamp(this int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}

namespace MajdataPlay.Extensions
{
    /// <summary>Supplies the small collection operations used by production sources.</summary>
    internal static class ValidationCollectionExtensions
    {
        /// <summary>Checks whether an array is empty.</summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="items">The array to inspect.</param>
        /// <returns>True when the array has no elements.</returns>
        public static bool IsEmpty<T>(this T[] items)
        {
            return items.Length == 0;
        }

        /// <summary>Finds a matching array index.</summary>
        /// <typeparam name="T">The array element type.</typeparam>
        /// <param name="items">The array to inspect.</param>
        /// <param name="predicate">The matching condition.</param>
        /// <returns>The first matching index, or minus one.</returns>
        public static int FindIndex<T>(this T[] items, Predicate<T> predicate)
        {
            return Array.FindIndex(items, predicate);
        }

        /// <summary>Checks a read-only span against a predicate.</summary>
        /// <typeparam name="T">The span element type.</typeparam>
        /// <param name="items">The span to inspect.</param>
        /// <param name="predicate">The matching condition.</param>
        /// <returns>True when at least one entry matches.</returns>
        public static bool Any<T>(this ReadOnlySpan<T> items, Func<T, bool> predicate)
        {
            foreach (var item in items)
            {
                if (predicate(item))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Returns untranslated progress text without the localization service.</summary>
        /// <param name="text">The progress key.</param>
        /// <returns>The original key.</returns>
        public static string i18n(this string text)
        {
            return text;
        }
    }
}

namespace MajdataPlay
{
    /// <summary>Supplies the unused game-runtime reference.</summary>
    /// <typeparam name="T">The game service type.</typeparam>
    internal static class Majdata<T> where T : class
    {
        /// <summary>Gets no runtime instance because the harness has no scene.</summary>
        public static T? Instance => null;
    }

    /// <summary>Compiles Unity-object checks without replacing Unity itself.</summary>
    internal static class ValidationUnityExtensions
    {
        /// <summary>Uses Unity's real object-null comparison on the calling thread.</summary>
        /// <param name="value">The object to inspect.</param>
        /// <returns>Whether the native object still exists.</returns>
        public static bool IsNativeAlive(this UnityEngine.Object? value)
        {
            return value != null;
        }

        /// <summary>Uses Unity's real null comparison in an already completed operation.</summary>
        /// <param name="value">The object to inspect.</param>
        /// <returns>The native existence result.</returns>
        public static ValueTask<bool> IsNativeAliveAsync(this UnityEngine.Object? value)
        {
            return new ValueTask<bool>(value != null);
        }
    }

}

namespace MajdataPlay.Utils
{
    /// <summary>Compiles unused online ID conversion without validating endpoint semantics.</summary>
    internal static class ObjectIdHelper
    {
        /// <summary>Parses a validation identity.</summary>
        /// <param name="id">The unused online identity.</param>
        /// <returns>A GUID or the empty GUID.</returns>
        public static Guid ToGuid(string id)
        {
            return Guid.TryParse(id, out var value) ? value : Guid.Empty;
        }
    }

    /// <summary>Supplies the narrow JSON boundary using Unity's installed Newtonsoft runtime.</summary>
    internal static class Serializer
    {
        /// <summary>Exposes only the JSON operations requested by real SongStorage.</summary>
        public static class Json
        {
            /// <summary>Serializes a fixture model.</summary>
            /// <typeparam name="T">The model type.</typeparam>
            /// <param name="value">The model to serialize.</param>
            /// <returns>Its JSON representation.</returns>
            public static string Serialize<T>(T value)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(value);
            }

            /// <summary>Reads a caller-owned JSON stream without closing it.</summary>
            /// <typeparam name="T">The expected model type.</typeparam>
            /// <param name="stream">The fixture stream.</param>
            /// <returns>The parse result, value and optional failure.</returns>
            public static async Task<(bool, T?, Exception?)> TryDeserializeAsync<T>(Stream stream)
            {
                try
                {
                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, true);
                    var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    return (true, Newtonsoft.Json.JsonConvert.DeserializeObject<T>(text), null);
                }
                catch (Exception exception)
                {
                    return (false, default, exception);
                }
            }
        }
    }
}

namespace MajdataPlay.Databases
{
    /// <summary>Provides an imported namespace; no database implementation is replaced here.</summary>
    internal static class ValidationNamespace
    {
    }
}

namespace MajdataPlay.Settings
{
    /// <summary>Provides an imported namespace without the game settings service.</summary>
    internal static class ValidationNamespace
    {
    }
}
