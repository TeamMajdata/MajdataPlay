#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cysharp.Threading.Tasks
{
    /// <summary>Supplies the awaitable thread-switch boundary without a Unity synchronization context.</summary>
    internal static class UniTask
    {
        /// <summary>Returns an already completed thread-switch task for these sequential storage cases.</summary>
        /// <returns>A completed task.</returns>
        public static Task SwitchToThreadPool()
        {
            return Task.CompletedTask;
        }
    }
}

namespace MajdataPlay
{
    /// <summary>Supplies only the song metadata inspected by the production collection.</summary>
    public interface ISongDetail
    {
        /// <summary>Gets the chart identity used by collection lookup.</summary>
        string Hash { get; }
        /// <summary>Gets the title used by sorting and filtering.</summary>
        string Title { get; }
        /// <summary>Gets the artist used by filtering.</summary>
        string Artist { get; }
        /// <summary>Gets the description used by filtering.</summary>
        string Description { get; }
        /// <summary>Gets the designers used by sorting and filtering.</summary>
        ReadOnlySpan<string> Designers { get; }
        /// <summary>Gets the difficulty labels used by sorting and filtering.</summary>
        ReadOnlySpan<string> Levels { get; }
        /// <summary>Gets the timestamp used by time ordering.</summary>
        DateTime Timestamp { get; }
    }

    /// <summary>Supplies the playlist model referenced by the production collection.</summary>
    public sealed class DanInfo
    {
    }

    /// <summary>Supplies the sort/filter settings inspected by the production collection.</summary>
    public sealed class SongOrder
    {
        /// <summary>Gets the filter text.</summary>
        public string Keyword { get; init; } = string.Empty;
        /// <summary>Gets the sorting mode.</summary>
        public SortType SortBy { get; init; }
    }

    /// <summary>Supplies the root directory and offset unit read by settings storage.</summary>
    internal static class MajEnv
    {
        /// <summary>Gets or sets the case-owned settings directory.</summary>
        public static string RootPath { get; set; } = string.Empty;
        /// <summary>Gets the runtime setting tree read by the production unit conversion.</summary>
        public static TestSettings Settings { get; } = new TestSettings();
        /// <summary>Defines the production frame duration required for unit conversion.</summary>
        public const float FRAME_LENGTH_SEC = 1f / 60f;
    }

    /// <summary>Supplies the debug settings container used by storage.</summary>
    internal sealed class TestSettings
    {
        /// <summary>Gets the debug settings.</summary>
        public TestDebugSettings Debug { get; } = new TestDebugSettings();
    }

    /// <summary>Supplies the offset unit configured for loaded chart settings.</summary>
    internal sealed class TestDebugSettings
    {
        /// <summary>Gets the fixture's offset unit.</summary>
        public Settings.OffsetUnitOption OffsetUnit { get; } = Settings.OffsetUnitOption.Second;
    }

    /// <summary>Supplies the save event subscribed to by production settings storage.</summary>
    internal static class GameManager
    {
        /// <summary>Runs registered storage saves.</summary>
        public static event EventHandler? OnSave;

        /// <summary>Invokes the save event synchronously for deterministic assertions.</summary>
        public static void Save()
        {
            OnSave?.Invoke(null, EventArgs.Empty);
        }
    }
}

namespace MajdataPlay.Settings
{
    /// <summary>Supplies the offset units used by production settings storage.</summary>
    internal enum OffsetUnitOption
    {
        /// <summary>Expresses an offset in seconds.</summary>
        Second,
        /// <summary>Expresses an offset in frames.</summary>
        Frame
    }

    /// <summary>Supplies the serializable values accessed by production settings storage.</summary>
    internal sealed class ChartSetting
    {
        /// <summary>Gets the chart identity.</summary>
        public string Hash { get; init; } = string.Empty;
        /// <summary>Gets or sets the offset's unit.</summary>
        public OffsetUnitOption Unit { get; set; }
        /// <summary>Gets or sets the playback offset used to verify repeated saves.</summary>
        public float AudioOffset { get; set; }
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Supplies the production storage's error sink without Unity logging.</summary>
    internal static class MajDebug
    {
        /// <summary>Reports malformed-data load errors to the validation output.</summary>
        /// <param name="message">The diagnostic context.</param>
        public static void LogError(object? message)
        {
            Console.WriteLine(message);
        }

        /// <summary>Reports unexpected save errors to the validation output.</summary>
        /// <param name="exception">The exception reported by storage.</param>
        public static void LogException(Exception exception)
        {
            Console.WriteLine(exception);
        }
    }
}

namespace MajdataPlay.Utils
{
    /// <summary>Supplies deterministic JSON operations at the production storage's serialization boundary.</summary>
    internal static class Serializer
    {
        /// <summary>Provides the narrow serialization surface used by chart settings storage.</summary>
        public static class Json
        {
            /// <summary>Encodes the provided values as JSON text.</summary>
            /// <typeparam name="T">The serializable model type.</typeparam>
            /// <param name="value">The model values to write.</param>
            /// <returns>The encoded JSON text.</returns>
            public static string Serialize<T>(T value)
            {
                return JsonSerializer.Serialize(value);
            }

            /// <summary>Reads JSON from a stream and reports parse failures as a result tuple.</summary>
            /// <typeparam name="T">The expected model type.</typeparam>
            /// <param name="stream">The caller-owned source stream.</param>
            /// <returns>The parse status, parsed value and optional error.</returns>
            public static async Task<(bool, T?, Exception?)> TryDeserializeAsync<T>(Stream stream)
            {
                try
                {
                    var value = await JsonSerializer.DeserializeAsync<T>(stream);
                    return (true, value, null);
                }
                catch (Exception exception)
                {
                    return (false, default, exception);
                }
            }

            /// <summary>Reads JSON text and reports parse failures as a result tuple.</summary>
            /// <typeparam name="T">The expected model type.</typeparam>
            /// <param name="text">The source JSON text.</param>
            /// <returns>The parse status, parsed value and optional error.</returns>
            public static Task<(bool, T?, Exception?)> TryDeserializeAsync<T>(string text)
            {
                try
                {
                    return Task.FromResult<(bool, T?, Exception?)>((true, JsonSerializer.Deserialize<T>(text), null));
                }
                catch (Exception exception)
                {
                    return Task.FromResult<(bool, T?, Exception?)>((false, default, exception));
                }
            }
        }
    }
}

namespace MajdataPlay.Numerics
{
    /// <summary>Supplies the collection's integer clamping boundary.</summary>
    internal static class NumericExtensions
    {
        /// <summary>Clamps the integer to the supplied inclusive bounds.</summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="minimum">The inclusive lower bound.</param>
        /// <param name="maximum">The inclusive upper bound.</param>
        /// <returns>The clamped integer.</returns>
        public static int Clamp(this int value, int minimum, int maximum)
        {
            return Math.Clamp(value, minimum, maximum);
        }
    }
}

namespace MajdataPlay.Extensions
{
    /// <summary>Supplies the collection utilities used by linked production source.</summary>
    internal static class CollectionExtensions
    {
        /// <summary>Checks whether an array contains no entries.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="items">The array to inspect.</param>
        /// <returns>Whether the array is empty.</returns>
        public static bool IsEmpty<T>(this T[] items)
        {
            return items.Length == 0;
        }

        /// <summary>Finds the first array entry matching a predicate.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="items">The array to search.</param>
        /// <param name="predicate">The condition evaluated for each entry.</param>
        /// <returns>The matching index, or minus one.</returns>
        public static int FindIndex<T>(this T[] items, Predicate<T> predicate)
        {
            return Array.FindIndex(items, predicate);
        }

        /// <summary>Checks whether a span contains an entry matching a predicate.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="items">The span to search.</param>
        /// <param name="predicate">The condition evaluated for each entry.</param>
        /// <returns>Whether an entry matches.</returns>
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
    }
}

namespace UnityEngine
{
    /// <summary>Provides the namespace imported by the collection without loading Unity assemblies.</summary>
    internal static class NamespaceMarker
    {
    }
}
