using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Drawing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Drawing
{
    /// <summary>
    /// Covers <see cref="SpriteLoader"/>: the shared empty sprite and the synchronous and asynchronous loaders from memory
    /// and from files, including the fallback to the empty sprite with an error log on failure.
    /// </summary>
    public class SpriteLoaderTests
    {
        /// <summary>The error logged when bytes cannot be decoded.</summary>
        private static readonly Regex s_memoryError = new Regex("Failed to load sprite from memory");

        /// <summary>The error logged when a file cannot be read.</summary>
        private static readonly Regex s_fileError = new Regex("Failed to load sprite from file");

        /// <summary>Tracks the sprites and textures created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Holds the test files.</summary>
        private TemporaryDirectory _directory = null!;

        /// <summary>Creates the object tracker and the temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _directory = new TemporaryDirectory();
        }

        /// <summary>Destroys the created objects and the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _directory.Dispose();
        }

        /// <summary>Tracks a loaded sprite and its texture unless it is the shared empty sprite.</summary>
        /// <param name="sprite">The loaded sprite.</param>
        /// <returns>The same sprite.</returns>
        private Sprite Track(Sprite sprite)
        {
            if (sprite != SpriteLoader.EmptySprite)
            {
                _objects.Track(sprite.texture);
                _objects.Track(sprite);
            }
            return sprite;
        }

        /// <summary>Writes PNG bytes of the test pattern to a temporary file.</summary>
        /// <param name="name">The file name.</param>
        /// <param name="width">The image width.</param>
        /// <param name="height">The image height.</param>
        /// <returns>The file location.</returns>
        private string WritePng(string name, int width, int height)
        {
            var file = _directory.Directory.CreateFile(name);
            file.WriteAllBytes(TestImages.CreatePng(width, height));
            return file.Location;
        }

        /// <summary>Asserts the geometry every loaded sprite shares.</summary>
        /// <param name="sprite">The sprite.</param>
        /// <param name="width">The expected width.</param>
        /// <param name="height">The expected height.</param>
        private static void AssertSprite(Sprite sprite, int width, int height)
        {
            Assert.That(sprite, Is.Not.SameAs(SpriteLoader.EmptySprite));
            Assert.That(sprite.rect, Is.EqualTo(new Rect(0, 0, width, height)));
            Assert.That(sprite.pivot, Is.EqualTo(new Vector2(width * 0.5f, height * 0.5f)));
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(100f));
        }

        /// <summary>Checks that the empty sprite is a cached, transparent, hidden one-pixel sprite.</summary>
        [Test]
        public void EmptySprite_IsCachedTransparentPixel()
        {
            var sprite = SpriteLoader.EmptySprite;

            Assert.That(SpriteLoader.EmptySprite, Is.SameAs(sprite));
            Assert.That(sprite.name, Is.EqualTo("EmptySprite"));
            Assert.That(sprite.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(sprite.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(sprite.texture.name, Is.EqualTo("EmptySpriteTexture"));
            Assert.That(sprite.texture.GetPixel(0, 0), Is.EqualTo(Color.clear));
        }

        /// <summary>Checks that encoded bytes load into a sprite with a non-readable texture by default.</summary>
        [Test]
        public void LoadFromMemory_ValidPng_CreatesSprite()
        {
            var sprite = Track(SpriteLoader.LoadFromMemory(TestImages.CreatePng(4, 2)));

            AssertSprite(sprite, 4, 2);
            Assert.That(sprite.border, Is.EqualTo(Vector4.zero));
            Assert.That(sprite.texture.isReadable, Is.False);
        }

        /// <summary>Checks that the border overload applies the border and honours the readability flag.</summary>
        [Test]
        public void LoadFromMemoryWithBorder_AppliesBorder()
        {
            var border = new Vector4(1, 1, 1, 1);

            var sprite = Track(SpriteLoader.LoadFromMemoryWithBorder(TestImages.CreatePng(4, 4), border, false));

            AssertSprite(sprite, 4, 4);
            Assert.That(sprite.border, Is.EqualTo(border));
            Assert.That(sprite.texture.isReadable, Is.True);
        }

        /// <summary>Checks that undecodable bytes log an error and yield the empty sprite.</summary>
        [Test]
        public void LoadFromMemory_InvalidData_ReturnsEmptySprite()
        {
            LogAssert.Expect(LogType.Error, s_memoryError);

            var sprite = SpriteLoader.LoadFromMemory(new byte[] { 0, 1, 2, 3 });

            Assert.That(sprite, Is.SameAs(SpriteLoader.EmptySprite));
        }

        /// <summary>Checks that a missing file yields the empty sprite without an error.</summary>
        [Test]
        public void LoadFromFile_MissingFile_ReturnsEmptySprite()
        {
            var sprite = SpriteLoader.LoadFromFile(_directory.Location + "/missing.png");

            Assert.That(sprite, Is.SameAs(SpriteLoader.EmptySprite));
        }

        /// <summary>Checks that a PNG file loads into a sprite.</summary>
        [Test]
        public void LoadFromFile_ValidPng_CreatesSprite()
        {
            var path = WritePng("valid.png", 5, 3);

            var sprite = Track(SpriteLoader.LoadFromFile(path));

            AssertSprite(sprite, 5, 3);
        }

        /// <summary>Checks that the file border overload applies the border.</summary>
        [Test]
        public void LoadFromFileWithBorder_AppliesBorder()
        {
            var path = WritePng("border.png", 4, 4);
            var border = new Vector4(1, 1, 1, 0);

            var sprite = Track(SpriteLoader.LoadFromFileWithBorder(path, border, false));

            AssertSprite(sprite, 4, 4);
            Assert.That(sprite.border, Is.EqualTo(border));
        }

        /// <summary>Checks that a file that is not an image logs an error and yields the empty sprite.</summary>
        [Test]
        public void LoadFromFile_NotAnImage_ReturnsEmptySprite()
        {
            var path = _directory.WriteText("text.png", "not an image").Location;
            LogAssert.Expect(LogType.Error, s_memoryError);

            var sprite = SpriteLoader.LoadFromFile(path);

            Assert.That(sprite, Is.SameAs(SpriteLoader.EmptySprite));
        }

        /// <summary>Checks that the asynchronous memory loaders create sprites with and without a border.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadFromMemoryAsync_ValidPng_CreatesSprites() => UniTask.ToCoroutine(async () =>
        {
            var png = TestImages.CreatePng(2, 2);
            var border = new Vector4(1, 0, 0, 0);

            var plain = Track(await SpriteLoader.LoadFromMemoryAsync(png));
            var bordered = Track(await SpriteLoader.LoadFromMemoryWithBorderAsync(png, border, false));

            AssertSprite(plain, 2, 2);
            Assert.That(plain.texture.isReadable, Is.False);
            AssertSprite(bordered, 2, 2);
            Assert.That(bordered.border, Is.EqualTo(border));
            Assert.That(bordered.texture.isReadable, Is.True);
        });

        /// <summary>Checks that undecodable bytes make the asynchronous loader log an error and return the empty sprite.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadFromMemoryAsync_InvalidData_ReturnsEmptySprite() => UniTask.ToCoroutine(async () =>
        {
            LogAssert.Expect(LogType.Error, s_memoryError);

            var sprite = await SpriteLoader.LoadFromMemoryAsync(new byte[] { 9, 9, 9 });

            Assert.That(sprite, Is.SameAs(SpriteLoader.EmptySprite));
        });

        /// <summary>Checks that the asynchronous file loaders create sprites and fall back for missing files.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadFromFileAsync_LoadsOrFallsBack() => UniTask.ToCoroutine(async () =>
        {
            var path = WritePng("async.png", 3, 4);
            var border = new Vector4(1, 1, 0, 0);

            var plain = Track(await SpriteLoader.LoadFromFileAsync(path));
            var bordered = Track(await SpriteLoader.LoadFromFileWithBorderAsync(path, border));
            var missing = await SpriteLoader.LoadFromFileAsync(_directory.Location + "/missing.png");

            AssertSprite(plain, 3, 4);
            AssertSprite(bordered, 3, 4);
            Assert.That(bordered.border, Is.EqualTo(border));
            Assert.That(missing, Is.SameAs(SpriteLoader.EmptySprite));
        });

        /// <summary>Checks that a canceled asynchronous file load logs an error and returns the empty sprite.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadFromFileAsync_Canceled_ReturnsEmptySprite() => UniTask.ToCoroutine(async () =>
        {
            var path = WritePng("canceled.png", 2, 2);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            LogAssert.Expect(LogType.Error, s_fileError);

            var sprite = await SpriteLoader.LoadFromFileAsync(path, true, cancellation.Token);

            Assert.That(sprite, Is.SameAs(SpriteLoader.EmptySprite));
        });
    }
}
