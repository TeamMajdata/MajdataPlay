using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Drawing;
using NUnit.Framework;
using SkiaSharp;
using SkiaSharp.Internals;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Drawing
{
    /// <summary>
    /// Covers <see cref="TextureData"/>: off-main-thread preparation, mipmap layout, disposal, and how often it takes
    /// SkiaSharp's global handle registry lock.
    /// </summary>
    public class TextureDataTests
    {
        /// <summary>Tracks the textures created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the textures created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that decoding and mipmap generation run on a worker thread and only the upload needs the main thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Decode_OnWorkerThread_UploadsOnMainThread() => UniTask.ToCoroutine(async () =>
        {
            var png = TestImages.CreatePng(6, 4);

            using var data = await Task.Run(() => TextureData.Decode(png, true));
            var texture = _objects.Track(data.ToTexture2D(false));

            Assert.That(data.MipCount, Is.EqualTo(3));
            Assert.That(data.HasMipChain, Is.True);
            Assert.That(texture.mipmapCount, Is.EqualTo(3));
            TextureConversionTests.AssertBaseLevel(texture);
            TextureConversionTests.AssertMipChain(texture);
        });

        /// <summary>Checks that skipping mipmaps stores and uploads only the base level.</summary>
        [Test]
        public void FromBitmap_WithoutMipmaps_HasOneBilinearLevel()
        {
            using var bitmap = TestImages.CreateBitmap(5, 3);

            using var data = TextureData.FromBitmap(bitmap, false);
            var texture = _objects.Track(data.ToTexture2D(false));

            Assert.That(data.MipCount, Is.EqualTo(1));
            Assert.That(data.HasMipChain, Is.False);
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            TextureConversionTests.AssertBaseLevel(texture);
        }

        /// <summary>Checks that the prepared data can be uploaded more than once.</summary>
        [Test]
        public void ToTexture2D_CanUploadTwice()
        {
            using var bitmap = TestImages.CreateBitmap(4, 2);
            using var data = TextureData.FromBitmap(bitmap, true);

            var first = _objects.Track(data.ToTexture2D(false));
            var second = _objects.Track(data.ToTexture2D(true));

            TextureConversionTests.AssertBaseLevel(first);
            Assert.That(second.isReadable, Is.False);
        }

        /// <summary>Checks that the computed chain length matches the one Unity allocates.</summary>
        /// <param name="width">The base level width.</param>
        /// <param name="height">The base level height.</param>
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(5, 3)]
        [TestCase(6, 4)]
        [TestCase(256, 256)]
        [TestCase(300, 17)]
        [TestCase(1, 1000)]
        public void ComputeMipCount_MatchesUnity(int width, int height)
        {
            var texture = _objects.Track(new Texture2D(width, height, TextureFormat.RGBA32, true));

            Assert.That(TextureData.ComputeMipCount(width, height), Is.EqualTo(texture.mipmapCount));
        }

        /// <summary>Checks that disposal is idempotent and invalidates the upload.</summary>
        [Test]
        public void Dispose_IsIdempotentAndBlocksUpload()
        {
            using var bitmap = TestImages.CreateBitmap(2, 2);
            var data = TextureData.FromBitmap(bitmap, true);

            data.Dispose();
            data.Dispose();

            Assert.Throws<ObjectDisposedException>(() => data.ToTexture2D(false));
        }

        /// <summary>Checks that invalid input is rejected with argument exceptions.</summary>
        [Test]
        public void InvalidInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => TextureData.Decode(ReadOnlySpan<byte>.Empty, true));
            Assert.Throws<ArgumentException>(() => TextureData.Decode(new byte[] { 1, 2, 3, 4 }, true));
            Assert.Throws<ArgumentNullException>(() => TextureData.FromBitmap(null!, true));
            using var empty = new SKBitmap();
            Assert.Throws<ArgumentException>(() => TextureData.FromBitmap(empty, true));
        }

        /// <summary>
        /// Regression test: SkiaSharp must not record a stack trace for every registered handle, which upstream does under
        /// DEBUG (defined in every Unity development build) while holding its global lock.
        /// </summary>
        [Test]
        public void HandleRegistry_DoesNotCaptureStackTraces()
        {
            var registry = typeof(SKObject).Assembly.GetType("SkiaSharp.HandleDictionary", true)!;

            Assert.That(registry.GetField("stackTraces", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic), Is.Null);
        }

        /// <summary>
        /// Regression test: converting an RGBA bitmap and uploading it must not touch SkiaSharp's handle registry, so it
        /// completes while another thread holds the registry's write lock. Creating an SKData, which does register, is the
        /// control that must wait for the lock.
        /// </summary>
        [Test]
        public void HandleRegistry_PixelPathRunsWhileRegistryIsLocked()
        {
            using var bitmap = TestImages.CreateBitmap(6, 4);
            Task control;
            Task<TextureData> conversion;
            bool controlWaitedForLock;
            bool convertedWhileLocked;
            var upload = new Stopwatch();

            using (var holder = RegistryWriteLockHolder.Acquire())
            {
                control = Task.Run(() =>
                {
                    using var data = SKData.CreateCopy(new byte[] { 1, 2, 3 });
                });
                conversion = Task.Run(() => TextureData.FromBitmap(bitmap, true));
                convertedWhileLocked = conversion.Wait(5000);
                controlWaitedForLock = !control.Wait(200);
                if (convertedWhileLocked)
                {
                    upload.Start();
                    _objects.Track(conversion.Result.ToTexture2D(false));
                    upload.Stop();
                }
            }
            var controlFinished = control.Wait(5000);
            conversion.Wait(5000);
            conversion.Result.Dispose();

            Assert.That(controlWaitedForLock, Is.True, "Creating an SKData must wait for the registry lock.");
            Assert.That(controlFinished, Is.True);
            Assert.That(convertedWhileLocked, Is.True, "The conversion waited for the registry lock.");
            Assert.That(upload.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "The upload waited for the registry lock.");
        }

        /// <summary>
        /// Holds SkiaSharp's handle registry write lock on a dedicated thread until disposed, or for at most ten seconds so
        /// that a test blocked on the lock fails instead of hanging.
        /// </summary>
        private sealed class RegistryWriteLockHolder : IDisposable
        {
            /// <summary>Signals that the lock is held.</summary>
            private readonly ManualResetEventSlim _acquired = new ManualResetEventSlim();

            /// <summary>Signals the holder to release the lock.</summary>
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim();

            /// <summary>The thread holding the lock.</summary>
            private readonly Thread _thread;

            /// <summary>Starts the holder thread.</summary>
            /// <param name="registryLock">The registry's lock.</param>
            private RegistryWriteLockHolder(IPlatformLock registryLock)
            {
                _thread = new Thread(() =>
                {
                    registryLock.EnterWriteLock();
                    try
                    {
                        _acquired.Set();
                        _release.Wait(10000);
                    }
                    finally
                    {
                        registryLock.ExitWriteLock();
                    }
                })
                {
                    IsBackground = true,
                    Name = nameof(RegistryWriteLockHolder)
                };
                _thread.Start();
            }

            /// <summary>Takes the registry's write lock on a new thread and waits until it is held.</summary>
            /// <returns>The holder; dispose it to release the lock.</returns>
            /// <exception cref="TimeoutException">The lock could not be taken within five seconds.</exception>
            public static RegistryWriteLockHolder Acquire()
            {
                var registry = typeof(SKObject).Assembly.GetType("SkiaSharp.HandleDictionary", true)!;
                var holder = new RegistryWriteLockHolder(PrivateMember.GetStaticField<IPlatformLock>(registry, "instancesLock"));
                if (!holder._acquired.Wait(5000))
                {
                    holder.Dispose();
                    throw new TimeoutException("The registry lock could not be taken.");
                }
                return holder;
            }

            /// <summary>Releases the lock and waits for the holder thread to exit.</summary>
            public void Dispose()
            {
                _release.Set();
                _thread.Join(10000);
                _acquired.Dispose();
                _release.Dispose();
            }
        }
    }
}
