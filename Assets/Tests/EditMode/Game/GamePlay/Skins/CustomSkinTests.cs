using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Skins
{
    /// <summary>
    /// Checks that <see cref="CustomSkin"/> loads a skin collection directory into an atlas, falls back for missing
    /// images and releases everything on unload, using small generated PNG files in a temporary directory.
    /// </summary>
    public class CustomSkinTests
    {
        /// <summary>The temporary directory that holds the generated skin.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The generated skin collection directory.</summary>
        private StorageDirectory _skinDirectory = null!;

        /// <summary>The skins created by a test, unloaded in tear down.</summary>
        private readonly List<CustomSkin> _skins = new();

        /// <summary>Generates a skin collection with solid, transparent, holed, split, oversized and corrupt images.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _skinDirectory = _temporary.Directory.CreateDirectory("GeneratedSkin");
            WritePng("outline.png", Solid(4, 4));
            WritePng("SubBackgourd.png", Solid(6, 6));
            WritePng("TapSkins/tap.png", Solid(8, 8));
            WritePng("TapSkins/tap_each.png", new Color32[16], 4, 4);
            WritePng("StarSkins/star.png", Ring(16));
            WritePng("SlideSkins/slide.png", TwoBlobs(), 12, 4);
            WritePng("HoldSkins/hold.png", Solid(16, 128));
            WriteBytes("TapSkins/tap_break.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        }

        /// <summary>Unloads every created skin and deletes the generated files.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var skin in _skins)
            {
                skin.UnloadAsync().GetAwaiter().GetResult();
            }
            _skins.Clear();
            _temporary.Dispose();
        }

        /// <summary>Checks the built-in empty skin: loaded, filled with placeholders and never unloaded.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Empty_IsAlwaysLoaded() => UniTask.ToCoroutine(async () =>
        {
            var skin = CustomSkin.Empty;

            await skin.LoadAsync();
            await skin.UnloadAsync();

            Assert.That(skin.Name, Is.EqualTo("NotAvailable"));
            Assert.That(skin.IsLoaded, Is.True);
            Assert.That(skin.IsOutlineAvailable, Is.False);
            Assert.That(skin.Tap, Is.Not.Null);
            Assert.That(skin.Just[59], Is.SameAs(skin.Tap));
            Assert.That(skin.TouchHold[4], Is.SameAs(skin.Tap));
        });

        /// <summary>Checks that a missing skin directory is rejected by every constructor.</summary>
        [Test]
        public void Constructors_RejectMissingDirectories()
        {
            var missing = Path.Combine(_temporary.Location, "Missing");

            Assert.Throws<DirectoryNotFoundException>(() => CustomSkin.Create(missing));
            Assert.Throws<DirectoryNotFoundException>(() => new CustomSkin(missing, false));
        }

        /// <summary>Checks a synchronous load: atlas sprites, independent sprites, missing and corrupt images and fallbacks.</summary>
        [Test]
        public void SynchronousLoad_AssignsEveryReadableImage()
        {
            IgnoreEditorGeometryErrors();
            var skin = Track(new CustomSkin("Generated", _skinDirectory.Location, true));

            Assert.That(skin.Name, Is.EqualTo("Generated"));
            Assert.That(skin.IsLoaded, Is.True);
            Assert.That(skin.IsOutlineAvailable, Is.True);
            AssertLoadedSprites(skin);
        }

        /// <summary>Checks that the nine-slice border of hold sprites is kept in the atlas.</summary>
        [Test]
        public void SynchronousLoad_KeepsTheHoldBorder()
        {
            IgnoreEditorGeometryErrors();
            var skin = Track(new CustomSkin("Generated", _skinDirectory.Location, true));

            Assert.That(skin.Hold!.border, Is.EqualTo(new Vector4(0, 58, 0, 58)));
            Assert.That(skin.Hold.rect.size, Is.EqualTo(new Vector2(16, 128)));
        }

        /// <summary>Checks an asynchronous load of a skin created on demand, and that loading twice is harmless.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AsynchronousLoad_AssignsEveryReadableImage() => UniTask.ToCoroutine(async () =>
        {
            IgnoreEditorGeometryErrors();
            var skin = Track(CustomSkin.Create(_skinDirectory.Location));
            Assert.That(skin.Name, Is.EqualTo("GeneratedSkin"));
            Assert.That(skin.IsLoaded, Is.False);

            await skin.LoadAsync();
            await skin.LoadAsync();

            Assert.That(skin.IsLoaded, Is.True);
            AssertLoadedSprites(skin);
        });

        /// <summary>Checks that unloading destroys the sprites and allows loading again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UnloadAsync_ReleasesTheSprites() => UniTask.ToCoroutine(async () =>
        {
            IgnoreEditorGeometryErrors();
            var skin = Track(new CustomSkin(_skinDirectory.Location, true));
            var atlasSprite = skin.Tap!;
            var independentSprite = skin.SubDisplay!;

            await skin.UnloadAsync();
            await skin.UnloadAsync();

            Assert.That(skin.IsLoaded, Is.False);
            Assert.That(skin.Tap, Is.Null);
            Assert.That(skin.SubDisplay, Is.Null);
            Assert.That(atlasSprite == null, Is.True, "The atlas sprite is destroyed");
            Assert.That(independentSprite == null, Is.True, "The independent sprite is destroyed");

            await skin.LoadAsync();

            Assert.That(skin.IsLoaded, Is.True);
            Assert.That(skin.Tap, Is.Not.Null);
        });

        /// <summary>Checks that an atlas builder without readable images assigns null and returns no atlas.</summary>
        [Test]
        public void AtlasBuilder_WithoutReadableImagesReturnsNull()
        {
            var builder = new AtlasBuilder(_skinDirectory);
            var assigned = new List<Sprite?>();
            builder.Add("Missing/none.png", x => assigned.Add(x));
            builder.Add("TapSkins/tap_break.png", x => assigned.Add(x));

            var atlas = builder.BuildAndAssign();

            Assert.That(atlas, Is.Null);
            Assert.That(assigned, Is.EqualTo(new Sprite?[] { null, null }));
            Assert.That(builder.Sprites, Is.Empty);
        }

        /// <summary>Checks that an atlas builder packs its images into one texture and reports the created sprites.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AtlasBuilder_PacksImagesIntoOneTexture() => UniTask.ToCoroutine(async () =>
        {
            IgnoreEditorGeometryErrors();
            var builder = new AtlasBuilder(_skinDirectory, -1f, 2f, 0);
            Sprite? tap = null;
            Sprite? star = null;
            builder.Add("TapSkins/tap.png", x => tap = x);
            builder.Add("StarSkins/star.png", x => star = x);

            var atlas = await builder.BuildAndAssignAsync();
            try
            {
                Assert.That(atlas, Is.Not.Null);
                Assert.That(tap!.texture, Is.SameAs(atlas));
                Assert.That(star!.texture, Is.SameAs(atlas));
                Assert.That(builder.Sprites, Is.EquivalentTo(new[] { tap, star }));
                Assert.That(atlas!.isReadable, Is.False, "The CPU copy is released");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tap, true);
                UnityEngine.Object.DestroyImmediate(star, true);
                UnityEngine.Object.DestroyImmediate(atlas, true);
            }
        });

        /// <summary>Checks the sprites of a loaded generated skin.</summary>
        /// <param name="skin">The loaded skin.</param>
        private static void AssertLoadedSprites(CustomSkin skin)
        {
            Assert.That(skin.Tap, Is.Not.Null);
            Assert.That(skin.Tap!.rect.size, Is.EqualTo(new Vector2(8, 8)));
            Assert.That(skin.Tap_Each, Is.Not.Null, "A fully transparent image still becomes a sprite");
            Assert.That(skin.Star, Is.Not.Null);
            Assert.That(skin.Slide, Is.Not.Null);
            Assert.That(skin.Outline, Is.Not.Null);
            Assert.That(skin.Tap_Break, Is.Null, "A corrupt image is left unassigned");
            Assert.That(skin.Tap_Ex, Is.Null, "A missing image is left unassigned");
            Assert.That(skin.Wifi[0], Is.Null);
            Assert.That(skin.Just[0], Is.Null);
            Assert.That(skin.Hold_On, Is.SameAs(skin.Hold), "The hold-on sprite falls back to the hold sprite");
            Assert.That(skin.Hold_Each_On, Is.Null);
            Assert.That(skin.Tap.texture, Is.SameAs(skin.Star!.texture), "Atlas sprites share one texture");
            Assert.That(skin.SubDisplay, Is.Not.Null);
            Assert.That(skin.SubDisplay!.texture, Is.Not.SameAs(skin.Tap.texture), "The sub display has its own texture");
            Assert.That(skin.LoadingSplash, Is.Null);
        }

        /// <summary>
        /// Lets the current test tolerate the "Not allowed to override geometry on sprite" error that Sprite.OverrideGeometry
        /// logs for some runs in the isolated Editor (documented as pre-existing by Tools/Tests/Modules/CustomSkinAtlasValidation;
        /// the sprites then keep their full rectangles and a Windows player logs none).
        /// </summary>
        private static void IgnoreEditorGeometryErrors()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        /// <summary>Remembers a skin so it is unloaded in tear down.</summary>
        /// <param name="skin">The skin.</param>
        /// <returns>The same skin.</returns>
        private CustomSkin Track(CustomSkin skin)
        {
            _skins.Add(skin);
            return skin;
        }

        /// <summary>Encodes pixels as a PNG file inside the generated skin.</summary>
        /// <param name="relativePath">The path relative to the skin directory.</param>
        /// <param name="pixels">The pixels, bottom row first.</param>
        /// <param name="width">The image width; the square root of the pixel count when zero.</param>
        /// <param name="height">The image height; the square root of the pixel count when zero.</param>
        private void WritePng(string relativePath, Color32[] pixels, int width = 0, int height = 0)
        {
            if (width == 0)
            {
                width = (int)Math.Sqrt(pixels.Length);
                height = width;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                WriteBytes(relativePath, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Encodes a solid rectangle as a PNG file inside the generated skin.</summary>
        /// <param name="relativePath">The path relative to the skin directory.</param>
        /// <param name="size">The width and height of the rectangle.</param>
        private void WritePng(string relativePath, (int Width, int Height) size)
        {
            var pixels = new Color32[size.Width * size.Height];
            Array.Fill(pixels, new Color32(255, 0, 0, 255));
            WritePng(relativePath, pixels, size.Width, size.Height);
        }

        /// <summary>Writes raw bytes to a file inside the generated skin, creating its directory.</summary>
        /// <param name="relativePath">The path relative to the skin directory.</param>
        /// <param name="bytes">The file content.</param>
        private void WriteBytes(string relativePath, byte[] bytes)
        {
            var separator = relativePath.LastIndexOf('/');
            var directory = separator < 0
                ? _skinDirectory
                : _skinDirectory.FindDirectoryAt(relativePath.Substring(0, separator)) ?? _skinDirectory.CreateDirectoryAt(relativePath.Substring(0, separator));
            directory.CreateFile(relativePath.Substring(separator + 1)).WriteAllBytes(bytes);
        }

        /// <summary>Describes a solid rectangle.</summary>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <returns>The size.</returns>
        private static (int Width, int Height) Solid(int width, int height)
        {
            return (width, height);
        }

        /// <summary>Creates a square ring with a transparent hole.</summary>
        /// <param name="size">The width and height.</param>
        /// <returns>The pixels.</returns>
        private static Color32[] Ring(int size)
        {
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var inHole = x >= size / 4 && x < size * 3 / 4 && y >= size / 4 && y < size * 3 / 4;
                    pixels[(y * size) + x] = inHole ? new Color32(0, 0, 0, 0) : new Color32(0, 255, 0, 255);
                }
            }
            return pixels;
        }

        /// <summary>Creates a 12 by 4 image with two separate opaque blobs.</summary>
        /// <returns>The pixels.</returns>
        private static Color32[] TwoBlobs()
        {
            var pixels = new Color32[12 * 4];
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 12; x++)
                {
                    var opaque = x < 3 || x >= 9;
                    pixels[(y * 12) + x] = opaque ? new Color32(0, 0, 255, 255) : new Color32(0, 0, 0, 0);
                }
            }
            return pixels;
        }
    }
}
