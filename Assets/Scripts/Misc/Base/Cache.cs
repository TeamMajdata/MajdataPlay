using Cysharp.Threading.Tasks;
using MajdataPlay.Caches;
using MajdataPlay.Collections;
using MajdataPlay.Drawing;
using MajdataPlay.IO;
using System;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;
#nullable enable
namespace MajdataPlay
{
    /// <summary>
    /// Process-wide least-recently-used caches for runtime-created resources, one per resource kind.
    /// </summary>
    /// <remarks>
    /// Each cache owns its values and frees them when they leave it: Unity objects are destroyed on the main thread and
    /// audio samples are disposed. A value handed out by a cache may therefore be freed once enough other values are
    /// stored after it, so the capacities are chosen to be well above what is in use at the same time.
    /// Callers must never store shared values (such as <see cref="SpriteLoader.EmptySprite"/>) in a cache.
    /// Keys are owned by the caller; use <see cref="CreateKeyScope(string)"/> to keep one owner's keys apart from another's.
    /// </remarks>
    internal static class Cache
    {
        /// <summary>The number of textures <see cref="Texture"/> keeps.</summary>
        public const int TextureCapacity = 64;

        /// <summary>The number of sprites <see cref="Sprite"/> keeps; well above the covers the song list shows at once.</summary>
        public const int SpriteCapacity = 64;

        /// <summary>The number of sprites <see cref="LargeSprite"/> keeps.</summary>
        public const int LargeSpriteCapacity = 4;

        /// <summary>The number of samples <see cref="AudioSample"/> keeps.</summary>
        public const int AudioSampleCapacity = 8;
        public const int MaidataRadarCapacity = 64;
        public const int MaidataAnalyzeCapacity = 256;

        /// <summary>Gets the cache for textures the caller created and hands over; evicted textures are destroyed.</summary>
        public static LruCache<string, Texture2D> Texture { get; } = new(TextureCapacity, ReleaseTexture);

        /// <summary>
        /// Gets the cache for small sprites such as compressed song covers. Each sprite must own its texture:
        /// an evicted sprite is destroyed together with its texture.
        /// </summary>
        public static LruCache<string, UnityEngine.Sprite> Sprite { get; } = new(SpriteCapacity, ReleaseSprite);

        /// <summary>
        /// Gets the cache for large sprites such as full-size song covers. Each sprite must own its texture:
        /// an evicted sprite is destroyed together with its texture.
        /// </summary>
        public static LruCache<string, UnityEngine.Sprite> LargeSprite { get; } = new(LargeSpriteCapacity, ReleaseSprite);

        /// <summary>Gets the cache for loaded music and preview samples; evicted samples are disposed.</summary>
        public static LruCache<string, AudioSampleWrap> AudioSample { get; } = new(AudioSampleCapacity, ReleaseAudioSample);

        public static LruCache<string, CachedRadarAnalyzeResult> MaidataRadar { get; } = new(MaidataRadarCapacity);
        public static LruCache<string, CachedMaidataAnalyzeResult> MaidataAnalyze { get; } = new(MaidataAnalyzeCapacity);

        private static long s_nextKeyScopeId = 0;

        /// <summary>Creates a key prefix that is unique for the process, so one owner's entries never collide with another's.</summary>
        /// <param name="ownerName">A readable name of the owner kind, used only to make keys easier to identify while debugging.</param>
        /// <returns>A unique prefix such as <c>SongDetail#12</c>; append a resource name to form a key.</returns>
        public static string CreateKeyScope(string ownerName)
        {
            return $"{ownerName}#{Interlocked.Increment(ref s_nextKeyScopeId)}";
        }

        /// <summary>Destroys an evicted texture on the main thread.</summary>
        /// <param name="key">The key the texture was stored under.</param>
        /// <param name="texture">The evicted texture.</param>
        private static void ReleaseTexture(string key, Texture2D texture)
        {
            RunOnMainThread(() => DestroyObject(texture));
        }

        /// <summary>Destroys an evicted sprite and the texture it owns on the main thread.</summary>
        /// <param name="key">The key the sprite was stored under.</param>
        /// <param name="sprite">The evicted sprite.</param>
        private static void ReleaseSprite(string key, UnityEngine.Sprite sprite)
        {
            RunOnMainThread(() =>
            {
                // Already destroyed elsewhere; there is nothing left to free.
                if (sprite == null)
                {
                    return;
                }
                var texture = sprite.texture;
                DestroyObject(sprite);
                DestroyObject(texture);
            });
        }

        /// <summary>Disposes an evicted audio sample.</summary>
        /// <param name="key">The key the sample was stored under.</param>
        /// <param name="sample">The evicted sample.</param>
        private static void ReleaseAudioSample(string key, AudioSampleWrap sample)
        {
            // Every backend's Dispose may be called from any thread.
            sample.Dispose();
        }

        /// <summary>Runs an action now when called on the main thread, otherwise queues it for the main thread.</summary>
        /// <param name="action">The action that touches Unity objects.</param>
        private static void RunOnMainThread(Action action)
        {
            if (PlayerLoopHelper.IsMainThread)
            {
                action();
            }
            else
            {
                UniTask.Post(action);
            }
        }

        /// <summary>Destroys a Unity object that may already have been destroyed. Must be called on the main thread.</summary>
        /// <param name="obj">The object to destroy.</param>
        private static void DestroyObject(Object? obj)
        {
            if (obj == null)
            {
                return;
            }
            // Destroy is not allowed outside play mode, such as in EditMode tests.
            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj, true);
            }
        }
    }
}
