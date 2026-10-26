#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using MajdataPlay.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

public static class RawSpriteDrawModeValidation
{
    public static void Run()
    {
        var texture = new Texture2D(64, 64);
        var sprite = Sprite.Create(texture, new Rect(16, 8, 32, 32),
            new Vector2(0.25f, 0.75f), 16f, 0, SpriteMeshType.FullRect,
            new Vector4(4, 8, 4, 8));
        var entries = new List<RawSpriteResources.MeshEntry>();
        try
        {
            var simple = Acquire(SpriteDrawMode.Simple, Vector2.one);
            Check(simple.Mesh.vertexCount == sprite.vertices.Length, "Simple source geometry");
            Check(ReferenceEquals(simple, Acquire(SpriteDrawMode.Simple, Vector2.one * 10)), "Simple ignores Size in cache");
            var sliced = Acquire(SpriteDrawMode.Sliced, new Vector2(4, 3));
            Check(sliced.Mesh.vertexCount == 36, "Sliced nine quads");
            Near(sliced.Mesh.bounds.min.x, -1f, "Pivot X");
            Near(sliced.Mesh.bounds.min.y, -2.25f, "Pivot Y");
            Near(sliced.Mesh.bounds.size.x, 4f, "Width");
            Near(sliced.Mesh.bounds.size.y, 3f, "Height");
            Near(sliced.Mesh.vertices[2].x - sliced.Mesh.vertices[0].x, 0.25f, "Left border width");
            Near(sliced.Mesh.vertices[1].y - sliced.Mesh.vertices[0].y, 0.5f, "Bottom border height");
            Near(sliced.Mesh.uv[0].x, 0.25f, "Subrect UV X");
            Near(sliced.Mesh.uv[0].y, 0.125f, "Subrect UV Y");
            Check(ReferenceEquals(sliced, Acquire(SpriteDrawMode.Sliced, new Vector2(4, 3))), "Shared Sliced mesh");
            Check(!ReferenceEquals(sliced, Acquire(SpriteDrawMode.Sliced, new Vector2(5, 3))), "Size separates cache entries");

            var small = Acquire(SpriteDrawMode.Sliced, new Vector2(0.25f, 0.5f));
            Check(small.Mesh.vertexCount == 16, "Compressed borders omit center");
            Near(small.Mesh.vertices[2].x - small.Mesh.vertices[0].x, 0.125f, "Compressed border width");
            var tiled = Acquire(SpriteDrawMode.Tiled, new Vector2(4, 3));
            Check(tiled.Mesh.vertexCount == 80, "Continuous 5 by 4 quads");
            Near(tiled.Mesh.uv[14].x, 0.4375f, "Continuous final tile UV cropping");
            var adaptive = Acquire(SpriteDrawMode.Tiled, new Vector2(4, 3), SpriteTileMode.Adaptive);
            Check(adaptive.Mesh.vertexCount == 64, "Adaptive 4 by 4 whole tiles");
            Near(adaptive.Mesh.uv[10].x, 0.6875f, "Adaptive full center UV");
            Check(Acquire(SpriteDrawMode.Tiled, new Vector2(2.75f, 2), SpriteTileMode.Adaptive).Mesh.vertexCount == 36,
                "Adaptive exact threshold");
            Check(Acquire(SpriteDrawMode.Tiled, new Vector2(2.76f, 2), SpriteTileMode.Adaptive).Mesh.vertexCount == 48,
                "Adaptive threshold crossed");

            foreach (var flipX in new[] { false, true })
            foreach (var flipY in new[] { false, true })
            {
                var flipped = RawSpriteResources.AcquireMesh(sprite, flipX, flipY, SpriteDrawMode.Sliced, new Vector2(4, 3));
                entries.Add(flipped);
                Near(flipped.Mesh.bounds.center.x, flipX ? -1f : 1f, "Flipped pivot X");
                Near(flipped.Mesh.bounds.center.y, flipY ? 0.75f : -0.75f, "Flipped pivot Y");
                var vertices = flipped.Mesh.vertices;
                var indices = flipped.Mesh.triangles;
                Check(Vector3.Cross(vertices[indices[1]] - vertices[indices[0]],
                    vertices[indices[2]] - vertices[indices[0]]).z < 0f, "Consistent winding");
            }
            Check(Acquire(SpriteDrawMode.Tiled, Vector2.zero).Mesh.vertexCount == 0, "Zero size");
            Check(RawSpriteResources.AcquireMesh(null, false, false) == null, "Null sprite");
            Check(Acquire(SpriteDrawMode.Tiled, new Vector2(100000, 100000)).Mesh.vertexCount == 36,
                "Quad budget fallback");

            var mesh = sliced.Mesh;
            foreach (var entry in entries)
                RawSpriteResources.Release(entry);
            entries.Clear();
            Check(mesh == null, "Final reference destroys shared mesh");
            Debug.Log("RAW_SPRITE_DRAW_MODE_VALIDATION_PASSED");
        }
        finally
        {
            foreach (var entry in entries)
                RawSpriteResources.Release(entry);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        RawSpriteResources.MeshEntry Acquire(SpriteDrawMode mode, Vector2 size,
            SpriteTileMode tileMode = SpriteTileMode.Continuous)
        {
            var entry = RawSpriteResources.AcquireMesh(sprite, false, false, mode, size, tileMode, 0.5f);
            entries.Add(entry);
            return entry;
        }
    }

    private static void Near(float actual, float expected, string name)
    {
        Check(Mathf.Abs(actual - expected) < 0.0001f, $"{name}: expected {expected}, got {actual}");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception("RawSprite draw mode validation failed: " + name);
    }
}
#endif
