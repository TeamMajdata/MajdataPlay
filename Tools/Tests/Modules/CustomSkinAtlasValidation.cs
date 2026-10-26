#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Game.Notes.Skins;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class CustomSkinAtlasValidation
{
    public static int MaximumAtlasSize = 8192;

    private readonly struct Input
    {
        public readonly int Width, Height;
        public readonly Rect Solid;
        public readonly Vector4 Border;
        public Input(int width, int height, Rect solid, Vector4 border)
        {
            Width = width;
            Height = height;
            Solid = solid;
            Border = border;
        }
    }

    private static Input[] Inputs =
    {
        new Input(1024, 512, new Rect(128, 64, 640, 320), Vector4.zero),
        new Input(1023, 768, new Rect(0, 0, 1023, 768), new Vector4(20, 10, 24, 12)),
        new Input(1023, 257, new Rect(0, 0, 1023, 257), new Vector4(20, 1, 24, 1)),
        new Input(257, 1023, new Rect(0, 0, 257, 1023), new Vector4(1, 20, 1, 24))
    };
    private static readonly Bounds[] ReferenceBounds = new Bounds[Inputs.Length];

    public static async void Run()
    {
        try
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../SourceTextures"));
            Directory.CreateDirectory(directory);
            await ValidateInputs(directory);
            // Also cover textures whose individual edges exceed the graphics device limit.
            Inputs = new[]
            {
                new Input(2048, 1024, new Rect(256, 128, 1280, 640), Vector4.zero),
                new Input(513, 384, new Rect(0, 0, 513, 384), new Vector4(20, 10, 24, 12)),
                new Input(1025, 257, new Rect(0, 0, 1025, 257), new Vector4(20, 1, 24, 1)),
                new Input(257, 1025, new Rect(0, 0, 257, 1025), new Vector4(1, 20, 1, 24))
            };
            await ValidateInputs(directory);
            ValidateResizePixels();
            Debug.Log("CUSTOM_SKIN_ATLAS_VALIDATION_PASSED");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static async UniTask ValidateInputs(string directory)
    {
        for (var i = 0; i < Inputs.Length; i++)
        {
            var texture = CreateTexture(Inputs[i]);
            try { File.WriteAllBytes(Path.Combine(directory, i + ".png"), texture.EncodeToPNG()); }
            finally { Object.DestroyImmediate(texture); }
        }
        ValidateIndependent();
        await ValidatePacked(directory, 8192, false);
        await ValidatePacked(directory, 1024, false);
        await ValidatePacked(directory, 1024, true);
    }

    private static void ValidateResizePixels()
    {
        var source = new Texture2D(4, 2, TextureFormat.RGBA32, false);
        var a = new Color32(200, 40, 10, 128);
        var b = new Color32(100, 80, 30, 64);
        var c = new Color32(10, 80, 220, 255);
        var d = new Color32(30, 100, 100, 127);
        source.SetPixels32(new[] { a, b, c, d, a, b, c, d });
        source.Apply();
        Texture2D resized = null;
        try
        {
            var resize = typeof(AtlasBuilder).GetMethod("ResizeForAtlas",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            resized = (Texture2D)resize.Invoke(null, new object[] { source, 0.5f });
            Check(resized.width == 2 && resized.height == 1, "Uniform resize dimensions");
            var pixels = resized.GetPixels32();
            Check(pixels[0].Equals(new Color32(150, 60, 20, 96)) &&
                pixels[1].Equals(new Color32(20, 90, 160, 191)), "Bilinear RGBA interpolation");
            Check(ReferenceEquals(source, resize.Invoke(null, new object[] { source, 1f })), "Full resolution texture reused");
            Check(source.width == 4 && source.height == 2 && source.GetPixels32()[0].Equals(a), "Original pixels preserved");
        }
        finally
        {
            if (resized != null && resized != source) Object.DestroyImmediate(resized);
            Object.DestroyImmediate(source);
        }
    }

    private static async UniTask ValidatePacked(string directory, int limit, bool asynchronous)
    {
        MaximumAtlasSize = limit;
        var sprites = new Sprite[Inputs.Length];
        var builder = new AtlasBuilder(directory);
        for (var i = 0; i < Inputs.Length; i++)
        {
            var index = i;
            builder.Add(i + ".png", sprite => sprites[index] = sprite, Inputs[i].Border);
        }
        Texture2D atlas = null;
        try
        {
            atlas = asynchronous ? await builder.BuildAndAssignAsync() : builder.BuildAndAssign();
            Check(atlas != null, "Atlas created");
            Check(atlas.width <= limit && atlas.height <= limit, "Atlas respects forced limit");
            for (var i = 0; i < sprites.Length; i++)
            {
                var sprite = sprites[i];
                Check(sprite != null, "Packed sprite assigned");
                Debug.Log($"Input {Inputs[i].Width}x{Inputs[i].Height}, packed {sprite.rect.size}, PPU {sprite.pixelsPerUnit}");
                ValidateSprite(sprite, Inputs[i], ReferenceBounds[i], limit == 8192 ? 0.0001f : 1f / sprite.pixelsPerUnit + 0.0001f);
                if (limit == 8192) Near(sprite.pixelsPerUnit, 100f, 0.0001f, "Full resolution PPU");
                else Check(sprite.pixelsPerUnit < 100f, "Forced limit actually downscales input");
            }
            var referenceEdge = Mathf.Max(Inputs[0].Width, Inputs[0].Height);
            for (var i = 1; i < sprites.Length; i++)
            {
                var sourceEdge = Mathf.Max(Inputs[i].Width, Inputs[i].Height);
                var roundingTolerance = 100f / referenceEdge + 100f / sourceEdge + 0.0001f;
                Near(sprites[i].pixelsPerUnit, sprites[0].pixelsPerUnit, roundingTolerance,
                    "All atlas inputs share the same resolution scale, input " + i);
            }
            Check(!atlas.isReadable, "Atlas releases CPU pixels after generating outlines");
            Debug.Log($"Atlas {atlas.width}x{atlas.height}, limit {limit}, async {asynchronous}: size, geometry, borders and UV passed");
        }
        finally
        {
            foreach (var sprite in sprites) if (sprite != null) Object.DestroyImmediate(sprite);
            if (atlas != null) Object.DestroyImmediate(atlas);
        }
    }

    private static void ValidateIndependent()
    {
        for (var i = 0; i < Inputs.Length; i++)
        {
            var input = Inputs[i];
            var texture = CreateTexture(input);
            Sprite sprite = null;
            try
            {
                sprite = AtlasBuilder.CreateOutlinedSprite(texture, new Rect(0, 0, input.Width, input.Height),
                    texture, "Independent", input.Border, 1f, 1f, 0);
                Near(sprite.pixelsPerUnit, 100f, 0.0001f, "Independent PPU");
                ReferenceBounds[i] = sprite.bounds;
                ValidateSprite(sprite, input, ReferenceBounds[i], 0.0001f);
            }
            finally
            {
                if (sprite != null) Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }

    private static void ValidateSprite(Sprite sprite, Input input, Bounds referenceBounds, float roundingTolerance)
    {
        var ppu = sprite.pixelsPerUnit;
        var dominantWidth = input.Width >= input.Height;
        Near(sprite.rect.width / ppu, input.Width / 100f, dominantWidth ? 0.0001f : roundingTolerance, "Logical width");
        Near(sprite.rect.height / ppu, input.Height / 100f, dominantWidth ? roundingTolerance : 0.0001f, "Logical height");
        Near(sprite.bounds.min.x, referenceBounds.min.x, roundingTolerance, "Geometry left");
        Near(sprite.bounds.min.y, referenceBounds.min.y, roundingTolerance, "Geometry bottom");
        Near(sprite.bounds.size.x, referenceBounds.size.x, roundingTolerance, "Geometry width");
        Near(sprite.bounds.size.y, referenceBounds.size.y, roundingTolerance, "Geometry height");
        for (var axis = 0; axis < 4; axis++)
            Near(sprite.border[axis] / ppu, input.Border[axis] / 100f, roundingTolerance, "Logical border " + axis);

        Check(sprite.triangles.Length > 0, "Geometry has triangles");
        var rect = sprite.rect;
        foreach (var uv in sprite.uv)
        {
            var x = uv.x * sprite.texture.width;
            var y = uv.y * sprite.texture.height;
            Check(x >= rect.xMin - 0.001f && x <= rect.xMax + 0.001f &&
                y >= rect.yMin - 0.001f && y <= rect.yMax + 0.001f, "Geometry UV stays inside packed rect");
        }
    }

    private static Texture2D CreateTexture(Input input)
    {
        var texture = new Texture2D(input.Width, input.Height, TextureFormat.RGBA32, false);
        var pixels = new Color32[input.Width * input.Height];
        for (var y = (int)input.Solid.yMin; y < input.Solid.yMax; y++)
        for (var x = (int)input.Solid.xMin; x < input.Solid.xMax; x++)
            pixels[y * input.Width + x] = new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    private static void Near(float actual, float expected, float tolerance, string label)
    {
        Check(Mathf.Abs(actual - expected) <= tolerance, $"{label}: expected {expected}, got {actual}, tolerance {tolerance}");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("CustomSkin atlas validation failed: " + label);
    }
}

// Isolated adapters for unrelated project services; Unity packing, geometry and package jobs are real.
namespace MajdataPlay.Buffers
{
    public sealed class PooledList<T> : List<T>, IDisposable
    {
        public PooledList(int capacity) : base(capacity) { }
        public void Dispose() { }
    }
    public static class Pool<T>
    {
        public static Rental Rent(int length) => new Rental(length);
        public sealed class Rental : IDisposable
        {
            private readonly T[] _array;
            public Rental(int length) { _array = new T[length]; }
            public T[] AsArray() => _array;
            public void Dispose() { }
        }
    }
}
namespace MajdataPlay.Diagnostics
{
    public static class MajDebug
    {
        public static void LogWarning(string category, string message) => Debug.LogWarning(category + ": " + message);
    }
}
namespace MajdataPlay.Drawing
{
    public static class TextureLoader
    {
        public static Texture2D LoadFromMemory(byte[] data, bool markNonReadable)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (texture.LoadImage(data, markNonReadable)) return texture;
            Object.DestroyImmediate(texture);
            return null;
        }
        public static UniTask<Texture2D> LoadFromMemoryAsync(byte[] data, bool markNonReadable) =>
            UniTask.FromResult(LoadFromMemory(data, markNonReadable));
    }
}
#endif
