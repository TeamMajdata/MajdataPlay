using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO;
using MajdataPlay.Tests.Game.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers how the process-wide <see cref="Cache"/> instances free the resources that leave them.</summary>
    public class CacheTests
    {
        /// <summary>The key prefix of the current test, so no production entry is touched.</summary>
        private string _keyScope = string.Empty;

        /// <summary>The keys written by the current test, removed again on tear down.</summary>
        private readonly List<string> _keys = new();

        /// <summary>Creates a fresh key scope for each test.</summary>
        [SetUp]
        public void SetUp()
        {
            _keyScope = Cache.CreateKeyScope(nameof(CacheTests));
            _keys.Clear();
        }

        /// <summary>Removes every entry written by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var key in _keys)
            {
                Cache.Texture.Remove(key);
                Cache.Sprite.Remove(key);
                Cache.LargeSprite.Remove(key);
                Cache.AudioSample.Remove(key);
            }
        }

        /// <summary>Creates a key inside the test's scope and records it for clean-up.</summary>
        /// <param name="name">The resource name.</param>
        /// <returns>The key.</returns>
        private string Key(string name)
        {
            var key = $"{_keyScope}/{name}";
            _keys.Add(key);
            return key;
        }

        /// <summary>Creates a sprite that owns its own texture, as the song cover loaders do.</summary>
        /// <param name="name">The sprite name.</param>
        /// <returns>The sprite.</returns>
        private static Sprite CreateSprite(string name)
        {
            var texture = new Texture2D(2, 2) { name = name };
            return Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Checks that the key scopes handed out are unique and carry the owner name.</summary>
        [Test]
        public void CreateKeyScope_IsUnique()
        {
            var first = Cache.CreateKeyScope("Owner");
            var second = Cache.CreateKeyScope("Owner");

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first, Does.StartWith("Owner#"));
        }

        /// <summary>Checks the capacities of the cache instances.</summary>
        [Test]
        public void Capacities_MatchConstants()
        {
            Assert.That(Cache.Texture.Capacity, Is.EqualTo(Cache.TextureCapacity));
            Assert.That(Cache.Sprite.Capacity, Is.EqualTo(Cache.SpriteCapacity));
            Assert.That(Cache.LargeSprite.Capacity, Is.EqualTo(Cache.LargeSpriteCapacity));
            Assert.That(Cache.AudioSample.Capacity, Is.EqualTo(Cache.AudioSampleCapacity));
        }

        /// <summary>Checks that removing a sprite destroys the sprite and the texture it owns.</summary>
        [Test]
        public void Sprite_Remove_DestroysSpriteAndTexture()
        {
            var sprite = CreateSprite("Removed");
            var texture = sprite.texture;
            var key = Key("removed");
            Cache.Sprite.Set(key, sprite);

            Cache.Sprite.Remove(key);

            Assert.That(sprite == null, Is.True);
            Assert.That(texture == null, Is.True);
        }

        /// <summary>Checks that a sprite pushed out by newer entries is destroyed together with its texture.</summary>
        [Test]
        public void Sprite_Eviction_DestroysOldestSprite()
        {
            var oldest = CreateSprite("Oldest");
            var oldestTexture = oldest.texture;
            Cache.Sprite.Set(Key("oldest"), oldest);
            var newer = new List<Sprite>();

            for (var i = 0; i < Cache.SpriteCapacity; i++)
            {
                var sprite = CreateSprite($"Newer{i}");
                newer.Add(sprite);
                Cache.Sprite.Set(Key($"newer{i}"), sprite);
            }

            Assert.That(oldest == null, Is.True);
            Assert.That(oldestTexture == null, Is.True);
            Assert.That(newer.TrueForAll(x => x != null), Is.True);
        }

        /// <summary>Checks that taking a sprite out leaves it alive for the caller.</summary>
        [Test]
        public void Sprite_TryRemove_KeepsSpriteAlive()
        {
            var sprite = CreateSprite("Taken");
            var key = Key("taken");
            Cache.Sprite.Set(key, sprite);

            Assert.That(Cache.Sprite.TryRemove(key, out var taken), Is.True);

            Assert.That(taken, Is.SameAs(sprite));
            Assert.That(sprite != null, Is.True);
            Object.DestroyImmediate(sprite!.texture);
            Object.DestroyImmediate(sprite);
        }

        /// <summary>Checks that releasing a sprite that was already destroyed elsewhere is harmless.</summary>
        [Test]
        public void Sprite_Remove_AlreadyDestroyed_DoesNotThrow()
        {
            var sprite = CreateSprite("Destroyed");
            var key = Key("destroyed");
            Cache.Sprite.Set(key, sprite);
            Object.DestroyImmediate(sprite.texture);
            Object.DestroyImmediate(sprite);

            Assert.DoesNotThrow(() => Cache.Sprite.Remove(key));
        }

        /// <summary>Checks that large sprites live in their own, smaller cache.</summary>
        [Test]
        public void LargeSprite_Eviction_DestroysOldestSprite()
        {
            var oldest = CreateSprite("LargeOldest");
            Cache.LargeSprite.Set(Key("largeOldest"), oldest);

            for (var i = 0; i < Cache.LargeSpriteCapacity; i++)
            {
                Cache.LargeSprite.Set(Key($"largeNewer{i}"), CreateSprite($"LargeNewer{i}"));
            }

            Assert.That(oldest == null, Is.True);
        }

        /// <summary>Checks that removing a texture destroys it.</summary>
        [Test]
        public void Texture_Remove_DestroysTexture()
        {
            var texture = new Texture2D(2, 2);
            var key = Key("texture");
            Cache.Texture.Set(key, texture);

            Cache.Texture.Remove(key);

            Assert.That(texture == null, Is.True);
        }

        /// <summary>Checks that removed and evicted audio samples are disposed once, and taken samples are not.</summary>
        [Test]
        public void AudioSample_ReleaseDisposesSample()
        {
            var removed = new FakeAudioSample("Removed", SFXSampleType.BGM);
            var evicted = new FakeAudioSample("Evicted", SFXSampleType.BGM);
            var taken = new FakeAudioSample("Taken", SFXSampleType.BGM);
            var removedKey = Key("removed");
            var takenKey = Key("taken");

            Cache.AudioSample.Set(removedKey, removed);
            Cache.AudioSample.Remove(removedKey);
            Cache.AudioSample.Set(takenKey, taken);
            Cache.AudioSample.TryRemove(takenKey, out _);
            Cache.AudioSample.Set(Key("evicted"), evicted);
            for (var i = 0; i < Cache.AudioSampleCapacity; i++)
            {
                Cache.AudioSample.Set(Key($"filler{i}"), new FakeAudioSample($"Filler{i}", SFXSampleType.BGM));
            }

            Assert.That(removed.Disposals, Is.EqualTo(1));
            Assert.That(evicted.Disposals, Is.EqualTo(1));
            Assert.That(taken.Disposals, Is.Zero);
        }

        /// <summary>
        /// Checks that a sprite released on a worker thread is destroyed later on the main thread instead of failing there.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Sprite_RemoveOnWorkerThread_DestroysOnMainThread()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var sprite = CreateSprite("Worker");
                var key = Key("worker");
                Cache.Sprite.Set(key, sprite);

                await Task.Run(() => Cache.Sprite.Remove(key));
                await UniTask.SwitchToMainThread();
                for (var i = 0; i < 10 && sprite != null; i++)
                {
                    await UniTask.Yield();
                }

                Assert.That(sprite == null, Is.True);
            });
        }
    }
}
