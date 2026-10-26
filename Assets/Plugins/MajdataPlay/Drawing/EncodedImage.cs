using MajdataPlay.IO.Storage;
using SkiaSharp;
using System;
using System.IO;

#nullable enable
namespace MajdataPlay.Drawing
{
    /// <summary>
    /// Holds an encoded image file in native memory and its header dimensions. The file is read once through
    /// <see cref="StorageFile.OpenRead"/>, so every storage backend is supported. All members are safe to use
    /// off the main thread.
    /// </summary>
    public sealed class EncodedImage : IDisposable
    {
        /// <summary>
        /// Holds the encoded bytes.
        /// </summary>
        private readonly SKData _data;

        /// <summary>
        /// Holds whether the native data has been released.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Gets the image width from the header, or zero when the format is unsupported.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the image height from the header, or zero when the format is unsupported.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets whether Skia recognized the format and reported positive dimensions.
        /// </summary>
        public bool IsSupported => Width > 0 && Height > 0;

        /// <summary>
        /// Wraps encoded data and reads its header.
        /// </summary>
        /// <param name="data">The encoded bytes; ownership is transferred.</param>
        private EncodedImage(SKData data)
        {
            _data = data;
            using var codec = SKCodec.Create(data);
            if (codec is not null)
            {
                Width = codec.Info.Width;
                Height = codec.Info.Height;
            }
        }

        /// <summary>
        /// Loads a file's encoded bytes into native memory and reads the image header without decoding pixels.
        /// </summary>
        /// <param name="file">The image file.</param>
        /// <returns>The encoded image; check <see cref="IsSupported"/> before decoding.</returns>
        /// <exception cref="ArgumentNullException">The file is null.</exception>
        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="IOException">Reading the file failed.</exception>
        public static EncodedImage Open(StorageFile file)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }
            SKData data;
            using (var stream = file.OpenRead())
            {
                data = SKData.Create(stream) ?? throw new IOException($"Could not read '{file.Location}'.");
            }
            try
            {
                return new EncodedImage(data);
            }
            catch
            {
                data.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Decodes the pixels into new native storage.
        /// </summary>
        /// <returns>The decoded image, or null when the format is unsupported, the data is corrupt
        /// or the image is too large for one buffer.</returns>
        /// <exception cref="ObjectDisposedException">The encoded image has been disposed.</exception>
        public DecodedImage? Decode()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EncodedImage));
            }
            if (!IsSupported)
            {
                return null;
            }
            using var codec = SKCodec.Create(_data);
            return codec is null ? null : DecodedImage.Decode(codec);
        }

        /// <summary>
        /// Releases the native encoded bytes.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _data.Dispose();
        }
    }
}
