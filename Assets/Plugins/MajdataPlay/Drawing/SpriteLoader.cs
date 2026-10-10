using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Drawing;
using MajdataPlay.IO.Storage;
using SkiaSharp;
using SkiaSharp.Unity;
using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Drawing
{
    public static class SpriteLoader
    {
        static Sprite? _emptySprite;

        public static Sprite EmptySprite
        {
            get
            {
                if (_emptySprite != null)
                {
                    return _emptySprite;
                }

                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "EmptySpriteTexture",
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixel(0, 0, Color.clear);
                texture.Apply();

                _emptySprite = Sprite.Create(texture,
                                             new Rect(0, 0, 1, 1),
                                             new Vector2(0.5f, 0.5f),
                                             100,
                                             0,
                                             SpriteMeshType.FullRect);
                _emptySprite.name = "EmptySprite";
                _emptySprite.hideFlags = HideFlags.HideAndDontSave;
                return _emptySprite;
            }
        }

        public static Sprite LoadFromFile(string filePath, bool markNonReadable = true)
        {
            return LoadFromFileWithBorder(filePath, Vector4.zero, markNonReadable);
        }
        public static Sprite LoadFromFileWithBorder(string filePath, Vector4 border, bool markNonReadable = true)
        {
            var file = FileSystem.OpenFile(filePath);
            if (!file.Exists)
            {
                return EmptySprite;
            }
            try
            {
                if (file.Entry?.Length is not long length)
                {
                    return LoadFromMemoryWithBorder(file.ReadAllBytes(), border, markNonReadable);
                }
                using var buffer = new NativeArray<byte>(checked((int)length), Allocator.Temp);
                using var fileStream = file.OpenRead();
                var remaining = buffer.AsSpan();
                while (!remaining.IsEmpty)
                {
                    var read = fileStream.Read(remaining);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("The sprite file ended before its reported length.");
                    }
                    remaining = remaining.Slice(read);
                }

                return LoadFromMemoryWithBorder(buffer.AsReadOnlySpan(), border, markNonReadable);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load sprite from file: {filePath}\nException: {e}");
                return EmptySprite;
            }
        }
        public static Sprite LoadFromMemory(ReadOnlySpan<byte> data, bool markNonReadable = true)
        {
            return LoadFromMemoryWithBorder(data, Vector4.zero, markNonReadable);
        }
        public static Sprite LoadFromMemoryWithBorder(ReadOnlySpan<byte> data, Vector4 border, bool markNonReadable = true)
        {
            try
            {
                var texture = TextureLoader.LoadFromMemory(data, markNonReadable);
                //var texture = new Texture2D(0, 0);
                //texture.LoadImage(data, markNonReadable);

                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100, 1,
                    SpriteMeshType.FullRect, border);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load sprite from memory\nException: {e}");
                return EmptySprite;
            }
        }


        public static Task<Sprite> LoadFromFileAsync(string filePath, bool markNonReadable = true, CancellationToken token = default)
        {
            return LoadFromFileWithBorderAsync(filePath, Vector4.zero, markNonReadable, token);
        }
        public static async Task<Sprite> LoadFromFileWithBorderAsync(string filePath, 
                                                                     Vector4 border, 
                                                                     bool markNonReadable = true, 
                                                                     CancellationToken token = default)
        {
            var file = FileSystem.OpenFile(filePath);
            if (!file.Exists)
            {
                await UniTask.SwitchToMainThread();
                return EmptySprite;
            }
            try
            {
                
                if (file.Entry?.Length is not long length)
                {
                    var bytes = await file.ReadAllBytesAsync(token);
                    return await LoadFromMemoryWithBorderAsync(bytes, border, markNonReadable, token);
                }
                using var buffer = new NativeArray<byte>(checked((int)length), Allocator.Persistent);
                var bufferMemory = buffer.AsMemory();
                using var fileStream = file.OpenRead();

                var remaining = bufferMemory;
                while (!remaining.IsEmpty)
                {
                    var read = await fileStream.ReadAsync(remaining, token);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("The sprite file ended before its reported length.");
                    }
                    remaining = remaining.Slice(read);
                }

                return await LoadFromMemoryWithBorderAsync(bufferMemory, border, markNonReadable, token);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load sprite from file: {filePath}\nException: {e}");
                await UniTask.SwitchToMainThread();
                return EmptySprite;
            }
        }
        public static Task<Sprite> LoadFromMemoryAsync(ReadOnlyMemory<byte> buffer, 
                                                       bool markNonReadable = true, 
                                                       CancellationToken token = default)
        {
            return LoadFromMemoryWithBorderAsync(buffer, Vector4.zero, markNonReadable, token);
        }
        public static async Task<Sprite> LoadFromMemoryWithBorderAsync(ReadOnlyMemory<byte> dataMemory, 
                                                                       Vector4 border, 
                                                                       bool markNonReadable = true, 
                                                                       CancellationToken token = default)
        {
            try
            {
                var texture = await TextureLoader.LoadFromMemoryAsync(dataMemory, markNonReadable);
                await UniTask.SwitchToMainThread();

                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100, 1,
                    SpriteMeshType.FullRect, border);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load sprite from memory\nException: {e}");
                await UniTask.SwitchToMainThread();

                return EmptySprite;
            }
        }
    }
}
