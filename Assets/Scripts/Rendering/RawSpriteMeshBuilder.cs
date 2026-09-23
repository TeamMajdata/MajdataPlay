using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Rendering
{
    internal static class RawSpriteMeshBuilder
    {
        private const int MaxQuads = 65536;

        internal static Mesh Build(Sprite sprite, bool flipX, bool flipY,
            SpriteDrawMode drawMode, Vector2 size, SpriteTileMode tileMode, float threshold)
        {
            var rect = sprite.rect;
            var sourceSize = rect.size / sprite.pixelsPerUnit;
            var border = sprite.border / sprite.pixelsPerUnit;
            var tiled = drawMode == SpriteDrawMode.Tiled;
            var xCount = TileCount(size.x, sourceSize.x, border.x, border.z, tiled, tileMode, threshold);
            var yCount = TileCount(size.y, sourceSize.y, border.y, border.w, tiled, tileMode, threshold);
            if ((xCount + 2d) * (yCount + 2d) > MaxQuads)
            {
                Debug.LogWarning($"RawSpriteRenderer: '{sprite.name}' exceeds the {MaxQuads} quad limit; using Sliced geometry.", sprite);
                tiled = false;
                xCount = yCount = 1;
            }

            var xs = BuildAxis(size.x, sourceSize.x, border.x, border.z, tiled, tileMode, (int)xCount);
            var ys = BuildAxis(size.y, sourceSize.y, border.y, border.w, tiled, tileMode, (int)yCount);
            var quadCount = xs.Count * ys.Count;
            var positions = new Vector3[quadCount * 4];
            var uvs = new Vector2[positions.Length];
            var colors = new Color32[positions.Length];
            var indices = new int[quadCount * 6];
            var pivot = new Vector2(sprite.pivot.x / rect.width, sprite.pivot.y / rect.height);
            var offset = Vector2.Scale(pivot, size);
            var scale = new Vector2(flipX ? -1f : 1f, flipY ? -1f : 1f);
            GetUVTransform(sprite, out var uvOrigin, out var uvX, out var uvY);

            var q = 0;
            foreach (var y in ys)
            {
                foreach (var x in xs)
                {
                    var v = q * 4;
                    SetVertex(v, x.x, y.x, x.z, y.z);
                    SetVertex(v + 1, x.x, y.y, x.z, y.w);
                    SetVertex(v + 2, x.y, y.y, x.w, y.w);
                    SetVertex(v + 3, x.y, y.x, x.w, y.z);
                    var t = q * 6;
                    indices[t] = v;
                    indices[t + 1] = v + (flipX ^ flipY ? 2 : 1);
                    indices[t + 2] = v + (flipX ^ flipY ? 1 : 2);
                    indices[t + 3] = v;
                    indices[t + 4] = v + (flipX ^ flipY ? 3 : 2);
                    indices[t + 5] = v + (flipX ^ flipY ? 2 : 3);
                    q++;
                }
            }

            var mesh = new Mesh
            {
                name = $"RawSpriteRenderer {sprite.name} {drawMode} ({size.x}, {size.y})",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = positions.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = positions;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector2.Scale(size * 0.5f - offset, scale), size);
            return mesh;

            void SetVertex(int index, float x, float y, float u, float v)
            {
                positions[index] = new Vector3((x - offset.x) * scale.x, (y - offset.y) * scale.y, 0f);
                uvs[index] = uvOrigin + uvX * u + uvY * v;
                colors[index] = new Color32(255, 255, 255, 255);
            }
        }

        private static double TileCount(float size, float sourceSize, float start, float end,
            bool tiled, SpriteTileMode tileMode, float threshold)
        {
            var center = Math.Max(0d, (double)size - start - end);
            var tile = Math.Max(0d, (double)sourceSize - start - end);
            if (!tiled || tile <= 0d || center <= 0d)
                return 1d;

            var ratio = center / tile;
            // Adaptive uses whole tiles, stretching until the next threshold is crossed.
            return Math.Max(1d, Math.Ceiling(ratio - (tileMode == SpriteTileMode.Adaptive ? threshold : 0d)));
        }

        // Each segment stores destination start/end and normalized source start/end.
        private static List<Vector4> BuildAxis(float size, float sourceSize, float start, float end,
            bool tiled, SpriteTileMode tileMode, int count)
        {
            var segments = new List<Vector4>(count + 2);
            if (size <= 0f || sourceSize <= 0f)
                return segments;

            var borderSum = start + end;
            var borderScale = borderSum > size ? size / borderSum : 1f;
            var innerStart = start * borderScale;
            var innerEnd = size - end * borderScale;
            var uvStart = start / sourceSize;
            var uvEnd = 1f - end / sourceSize;
            Add(0f, innerStart, 0f, uvStart);

            var length = Mathf.Max(0f, innerEnd - innerStart);
            var tileLength = Mathf.Max(0f, sourceSize - start - end);
            if (!tiled || tileLength <= 0f)
            {
                Add(innerStart, innerEnd, uvStart, uvEnd);
            }
            else if (tileMode == SpriteTileMode.Adaptive)
            {
                for (var i = 0; i < count; i++)
                    Add(innerStart + length * ((float)i / count),
                        i == count - 1 ? innerEnd : innerStart + length * ((i + 1f) / count), uvStart, uvEnd);
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    var from = innerStart + i * tileLength;
                    var to = i == count - 1 ? innerEnd : Mathf.Min(innerEnd, from + tileLength);
                    var fraction = Mathf.Clamp01((to - from) / tileLength);
                    Add(from, to, uvStart, Mathf.Lerp(uvStart, uvEnd, fraction));
                }
            }
            Add(innerEnd, size, uvEnd, 1f);
            return segments;

            void Add(float from, float to, float uvFrom, float uvTo)
            {
                if (to > from)
                    segments.Add(new Vector4(from, to, uvFrom, uvTo));
            }
        }

        private static void GetUVTransform(Sprite sprite, out Vector2 origin, out Vector2 x, out Vector2 y)
        {
            // Recover the affine mapping from source vertices, including atlas rotation/flips.
            // Sliced/Tiled assets must use Full Rect and rectangular atlas packing.
            var vertices = sprite.vertices;
            var uvs = sprite.uv;
            var triangles = sprite.triangles;
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                var ab = vertices[b] - vertices[a];
                var ac = vertices[c] - vertices[a];
                var determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) <= float.Epsilon)
                    continue;

                var uvAB = uvs[b] - uvs[a];
                var uvAC = uvs[c] - uvs[a];
                var unitX = (uvAB * ac.y - uvAC * ab.y) / determinant;
                var unitY = (uvAC * ab.x - uvAB * ac.x) / determinant;
                var bottomLeft = -sprite.pivot / sprite.pixelsPerUnit;
                var delta = bottomLeft - vertices[a];
                origin = uvs[a] + unitX * delta.x + unitY * delta.y;
                x = unitX * (sprite.rect.width / sprite.pixelsPerUnit);
                y = unitY * (sprite.rect.height / sprite.pixelsPerUnit);
                return;
            }

            origin = x = y = Vector2.zero;
        }
    }
}
