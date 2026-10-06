using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes.Skins;
using UnityEngine;

#nullable enable
namespace CustomSkinAtlasValidation
{
    /// <summary>
    /// Overrides the capability query in the isolated production-source copy.
    /// </summary>
    internal static class DeviceCapabilities
    {
        /// <summary>
        /// Gets or sets the simulated limit; zero selects the actual device limit.
        /// </summary>
        internal static int MaximumTextureSize { get; set; }

        /// <summary>
        /// Gets or sets the number of capability queries made by production code.
        /// </summary>
        internal static int QueryCount { get; set; }

        /// <summary>
        /// Gets the device limit consumed by the production AtlasBuilder.
        /// </summary>
        public static int maxTextureSize
        {
            get
            {
                QueryCount++;
                return MaximumTextureSize == 0 ? UnityEngine.SystemInfo.maxTextureSize : MaximumTextureSize;
            }
        }
    }
}

namespace MajdataPlay.Buffers
{
    /// <summary>
    /// Supplies the list surface used by CustomSkin without unrelated pool dependencies.
    /// </summary>
    /// <typeparam name="T">The stored element type.</typeparam>
    public sealed class PooledList<T> : List<T>, IDisposable
    {
        /// <summary>
        /// Creates a list with the requested initial capacity.
        /// </summary>
        /// <param name="capacity">The initial element capacity.</param>
        /// <exception cref="ArgumentOutOfRangeException">The capacity is negative.</exception>
        public PooledList(int capacity) : base(capacity)
        {
        }

        /// <summary>
        /// Releases the references retained by this test list.
        /// </summary>
        public void Dispose()
        {
            Clear();
        }
    }

    /// <summary>
    /// Supplies real array rentals for the production outline job's temporary bytes.
    /// </summary>
    /// <typeparam name="T">The rented element type.</typeparam>
    public static class Pool<T>
    {
        /// <summary>
        /// Rents a disposable buffer from the shared array pool.
        /// </summary>
        /// <param name="length">The minimum element count.</param>
        /// <returns>The rental owning the buffer.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The requested length is negative.</exception>
        public static Rental<T> Rent(int length)
        {
            return new Rental<T>(length);
        }
    }

    /// <summary>
    /// Returns its array rental after the production outline job completes.
    /// </summary>
    /// <typeparam name="T">The rented element type.</typeparam>
    public sealed class Rental<T> : IDisposable
    {
        /// <summary>
        /// Stores the rented array.
        /// </summary>
        private readonly T[] _array;

        /// <summary>
        /// Rents a buffer of at least the requested size.
        /// </summary>
        /// <param name="length">The minimum element count.</param>
        /// <exception cref="ArgumentOutOfRangeException">The requested length is negative.</exception>
        public Rental(int length)
        {
            _array = ArrayPool<T>.Shared.Rent(length);
        }

        /// <summary>
        /// Gets the buffer passed to the production outline job.
        /// </summary>
        /// <returns>The rented array.</returns>
        public T[] AsArray()
        {
            return _array;
        }

        /// <summary>
        /// Returns the array to its pool.
        /// </summary>
        public void Dispose()
        {
            ArrayPool<T>.Shared.Return(_array);
        }
    }
}

namespace MajdataPlay.Drawing
{
    /// <summary>
    /// Uses Unity PNG decoding to supply readable RGBA32 atlas sources.
    /// </summary>
    public static class TextureLoader
    {
        /// <summary>
        /// Decodes PNG bytes on Unity's main thread.
        /// </summary>
        /// <param name="data">The encoded PNG.</param>
        /// <param name="markNonReadable">Whether to release CPU pixel data.</param>
        /// <returns>The decoded texture.</returns>
        /// <exception cref="InvalidOperationException">The PNG cannot be decoded.</exception>
        public static Texture2D LoadFromMemory(ReadOnlySpan<byte> data, bool markNonReadable)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, data.ToArray(), markNonReadable))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidOperationException("Could not decode the validation PNG.");
            }
            return texture;
        }

        /// <summary>
        /// Switches to Unity's main thread before decoding a PNG.
        /// </summary>
        /// <param name="data">The encoded PNG.</param>
        /// <param name="markNonReadable">Whether to release CPU pixel data.</param>
        /// <returns>The decoded texture.</returns>
        /// <exception cref="InvalidOperationException">The PNG cannot be decoded.</exception>
        public static async Task<Texture2D> LoadFromMemoryAsync(ReadOnlyMemory<byte> data, bool markNonReadable)
        {
            await UniTask.SwitchToMainThread();
            return LoadFromMemory(data.Span, markNonReadable);
        }
    }

    /// <summary>
    /// Supplies the unrequested empty-skin sprite dependency.
    /// </summary>
    public static class SpriteLoader
    {
        /// <summary>
        /// Gets the placeholder; AtlasBuilder tests never construct CustomSkin.Empty.
        /// </summary>
        public static Sprite EmptySprite => null!;
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>
    /// Routes the production outline fallback message to the validation log.
    /// </summary>
    public static class MajDebug
    {
        /// <summary>
        /// Logs an outline warning.
        /// </summary>
        /// <param name="context">The source component.</param>
        /// <param name="message">The warning text.</param>
        public static void LogWarning(string context, string message)
        {
            Debug.LogWarning(context + ": " + message);
        }
    }
}

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Supplies the unused skin resource service required to compile CustomSkin.
    /// </summary>
    public static class NoteSpriteResources
    {
        /// <summary>
        /// Accepts a resource release outside the tested AtlasBuilder surface.
        /// </summary>
        /// <param name="skin">The owner of the resources.</param>
        public static void Unload(CustomSkin skin)
        {
        }

        /// <summary>
        /// Accepts mesh preloading outside the tested AtlasBuilder surface.
        /// </summary>
        /// <param name="skin">The owner of the resources.</param>
        /// <param name="sprites">The skin's sprites.</param>
        public static void PreloadMeshes(CustomSkin skin, IEnumerable<Sprite> sprites)
        {
        }
    }
}
