using System;
using System.Buffers;
using System.Collections.Generic;
using MajdataPlay.Scenes.Game.Notes.Skins;
using UnityEngine;

#nullable enable
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
    /// Supplies the placeholder sprite that the production empty skin assigns to every slot.
    /// </summary>
    public static class SpriteLoader
    {
        /// <summary>
        /// Gets the placeholder; the measured skins assign their own sprites.
        /// </summary>
        public static Sprite EmptySprite => null!;
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>
    /// Routes production log messages to the validation log.
    /// </summary>
    public static class MajDebug
    {
        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="tag">The source component.</param>
        /// <param name="message">The message.</param>
        public static void LogDebug<T>(string tag, T message)
        {
            Debug.Log(tag + ": " + message);
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="tag">The source component.</param>
        /// <param name="message">The message.</param>
        public static void LogInfo<T>(string tag, T message)
        {
            Debug.Log(tag + ": " + message);
        }

        /// <summary>
        /// Logs a warning.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="tag">The source component.</param>
        /// <param name="message">The warning.</param>
        public static void LogWarning<T>(string tag, T message)
        {
            Debug.LogWarning(tag + ": " + message);
        }

        /// <summary>
        /// Logs an error.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="tag">The source component.</param>
        /// <param name="message">The error.</param>
        public static void LogError<T>(string tag, T message)
        {
            Debug.LogError(tag + ": " + message);
        }
    }
}

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Supplies the skin mesh service; its meshes are small and outside the measured decode path.
    /// </summary>
    public static class NoteSpriteResources
    {
        /// <summary>
        /// Accepts a resource release.
        /// </summary>
        /// <param name="skin">The owner of the resources.</param>
        public static void Unload(CustomSkin skin)
        {
        }

        /// <summary>
        /// Accepts mesh preloading.
        /// </summary>
        /// <param name="skin">The owner of the resources.</param>
        /// <param name="sprites">The skin's sprites.</param>
        public static void PreloadMeshes(CustomSkin skin, IEnumerable<Sprite> sprites)
        {
        }
    }
}

namespace SkiaSharp.Unity
{
    /// <summary>
    /// Declares the SkiaSharp.Unity namespace imported by the staged production Skia extensions.
    /// </summary>
    internal static class NamespacePlaceholder
    {
    }
}
