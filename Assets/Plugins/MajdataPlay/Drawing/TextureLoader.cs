using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Drawing
{
    /// <summary>
    /// Loads encoded images (PNG, JPEG, WebP, ...) into clamped RGBA32 textures.
    /// </summary>
    public static class TextureLoader
    {
        /// <summary>
        /// Decodes an image into a trilinear texture with a full mipmap chain on the calling thread, which must be the
        /// main thread. Prefer <see cref="LoadFromMemoryAsync(ReadOnlyMemory{byte}, bool)"/> to keep decoding off it.
        /// </summary>
        /// <param name="data">The encoded image bytes.</param>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <returns>The uploaded texture.</returns>
        /// <exception cref="ArgumentException">The bytes are empty, not a supported image or corrupt.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        public static Texture2D LoadFromMemory(ReadOnlySpan<byte> data, bool markNonReadable)
        {
            return LoadFromMemory(data, markNonReadable, true);
        }

        /// <summary>
        /// Decodes an image into a texture on the calling thread, which must be the main thread. Prefer
        /// <see cref="LoadFromMemoryAsync(ReadOnlyMemory{byte}, bool, bool, CancellationToken)"/> to keep decoding off it.
        /// </summary>
        /// <param name="data">The encoded image bytes.</param>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <param name="generateMipmaps">
        /// Whether to build a full box-filtered mipmap chain and use trilinear filtering; otherwise the texture has one
        /// level and bilinear filtering.
        /// </param>
        /// <returns>The uploaded texture.</returns>
        /// <exception cref="ArgumentException">The bytes are empty, not a supported image or corrupt.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        public static Texture2D LoadFromMemory(ReadOnlySpan<byte> data, bool markNonReadable, bool generateMipmaps)
        {
            using var textureData = TextureData.Decode(data, generateMipmaps);

            return textureData.ToTexture2D(markNonReadable);
        }

        /// <summary>
        /// Decodes an image into a trilinear texture with a full mipmap chain, off the main thread.
        /// </summary>
        /// <param name="dataMemory">The encoded image bytes; they must stay valid until the task completes.</param>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <returns>A task returning the uploaded texture; it completes on the main thread.</returns>
        /// <exception cref="ArgumentException">The bytes are empty, not a supported image or corrupt.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        public static Task<Texture2D> LoadFromMemoryAsync(ReadOnlyMemory<byte> dataMemory, bool markNonReadable)
        {
            return LoadFromMemoryAsync(dataMemory, markNonReadable, true);
        }

        /// <summary>
        /// Decodes an image into a texture. Decoding, conversion and mipmap generation run on a worker thread (the
        /// calling one when it already is a worker); only the texture creation and upload run on the main thread.
        /// </summary>
        /// <param name="dataMemory">The encoded image bytes; they must stay valid until the task completes.</param>
        /// <param name="markNonReadable">Whether to release the texture's CPU copy after upload.</param>
        /// <param name="generateMipmaps">
        /// Whether to build a full box-filtered mipmap chain and use trilinear filtering; otherwise the texture has one
        /// level and bilinear filtering.
        /// </param>
        /// <param name="token">Cancels the load before decoding or before switching back to the main thread.</param>
        /// <returns>A task returning the uploaded texture; it completes on the main thread.</returns>
        /// <exception cref="ArgumentException">The bytes are empty, not a supported image or corrupt.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The image does not fit in one buffer.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="token"/> was canceled.</exception>
        public static async Task<Texture2D> LoadFromMemoryAsync(ReadOnlyMemory<byte> dataMemory,
                                                                bool markNonReadable,
                                                                bool generateMipmaps,
                                                                CancellationToken token = default)
        {
            if (PlayerLoopHelper.IsMainThread)
            {
                await UniTask.SwitchToThreadPool();
            }
            token.ThrowIfCancellationRequested();
            var textureData = TextureData.Decode(dataMemory.Span, generateMipmaps);
            try
            {
                await UniTask.SwitchToMainThread(token);

                return textureData.ToTexture2D(markNonReadable);
            }
            finally
            {
                textureData.Dispose();
            }
        }
    }
}
