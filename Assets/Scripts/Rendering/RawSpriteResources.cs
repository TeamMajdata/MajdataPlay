using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using static UnityEngine.Mesh;

#nullable enable
namespace MajdataPlay.Rendering
{
    // Shared GPU resources are immutable while in use. Keep references across
    // OnDisable so pooled notes can reuse them, and release on replacement/destroy.
    internal static class RawSpriteResources
    {
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly Dictionary<(Sprite, bool, bool, SpriteDrawMode, Vector2, SpriteTileMode, float), MeshEntry> Meshes = new();
        static readonly Dictionary<(Material, Texture), MaterialEntry> Materials = new();

        internal sealed class MeshEntry
        {
            internal readonly (Sprite, bool, bool, SpriteDrawMode, Vector2, SpriteTileMode, float) Key;
            internal readonly Mesh Mesh;
            internal int References;

            internal MeshEntry((Sprite, bool, bool, SpriteDrawMode, Vector2, SpriteTileMode, float) key, Mesh mesh)
            {
                Key = key;
                Mesh = mesh;
            }
        }

        internal sealed class MaterialEntry
        {
            internal readonly (Material, Texture) Key;
            internal readonly Material Material;
            internal int References;

            internal MaterialEntry((Material, Texture) key, Material material)
            {
                Key = key;
                Material = material;
            }
        }

        internal static MeshEntry? AcquireMesh(Sprite? sprite, bool flipX, bool flipY,
            SpriteDrawMode drawMode = SpriteDrawMode.Simple, Vector2 size = default,
            SpriteTileMode tileMode = SpriteTileMode.Continuous, float adaptiveModeThreshold = 0.5f)
        {
            if (sprite == null)
            {
                return null;
            }

            // Ignore settings that do not affect this mode so identical meshes stay shared.
            if (drawMode == SpriteDrawMode.Simple)
                size = default;
            if (drawMode != SpriteDrawMode.Tiled)
                tileMode = SpriteTileMode.Continuous;
            if (tileMode != SpriteTileMode.Adaptive)
                adaptiveModeThreshold = 0f;

            var key = (sprite, flipX, flipY, drawMode, size, tileMode, adaptiveModeThreshold);
            if (!Meshes.TryGetValue(key, out var entry))
            {
                var useSimple = drawMode == SpriteDrawMode.Simple;
                if (!useSimple && sprite.packed && sprite.packingMode == SpritePackingMode.Tight)
                {
                    Debug.LogWarning($"RawSpriteRenderer: '{sprite.name}' uses Tight atlas packing. Sliced/Tiled require rectangular packing; using Simple geometry.", sprite);
                    useSimple = true;
                }
                var mesh = useSimple
                    ? BuildSimpleMesh(sprite, flipX, flipY)
                    : RawSpriteMeshBuilder.Build(sprite, flipX, flipY, drawMode, size,
                        tileMode, adaptiveModeThreshold);
                entry = new MeshEntry(key, mesh);
                Meshes.Add(key, entry);
            }

            entry.References++;
            return entry;
        }

        private static Mesh BuildSimpleMesh(Sprite sprite, bool flipX, bool flipY)
        {
            //var spriteVertices = sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);
            //var spriteUVs = sprite.GetVertexAttribute<Vector2>(VertexAttribute.TexCoord0);
            //var spriteIndices = sprite.GetIndices();
            //using var sourceVertices = new NativeArray<Vector3>(spriteVertices.Length, Allocator.Temp);
            //using var sourceUVs = new NativeArray<Vector2>(spriteUVs.Length, Allocator.Temp);
            //using var sourceIndices = new NativeArray<ushort>(spriteIndices.Length, Allocator.Temp);

            //spriteVertices.CopyTo(sourceVertices);
            //spriteUVs.CopyTo(sourceUVs);
            //spriteIndices.CopyTo(sourceIndices);

            var sourceVertices = sprite.vertices;
            var sourceUVs = sprite.uv;
            var sourceIndices = sprite.triangles;

            var vertexCount = sourceVertices.Length;
            var indexCount = sourceIndices.Length;
            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var meshData = meshDataArray[0];

            using (var vertexAttributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.Temp))
            {
                var _vertexAttributes = vertexAttributes;
                _vertexAttributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
                _vertexAttributes[1] = new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1);
                _vertexAttributes[2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 2);

                meshData.SetVertexBufferParams(vertexCount, vertexAttributes);
            }

            var positions = meshData.GetVertexData<Vector3>(0);
            var uvs = meshData.GetVertexData<Vector2>(1);
            var colors = meshData.GetVertexData<Color32>(2);

            var scale = new Vector3(flipX ? -1f : 1f, flipY ? -1f : 1f, 1f);
            var defaultColor = new Color32(255, 255, 255, 255);

            for (var i = 0; i < vertexCount; i++)
            {
                var srcPos = sourceVertices[i];
                positions[i] = new Vector3(srcPos.x * scale.x, srcPos.y * scale.y, 0f);
                uvs[i] = sourceUVs[i];
                colors[i] = defaultColor;
            }

            meshData.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
            var indices = meshData.GetIndexData<ushort>();

            for (var i = 0; i < indexCount; i++)
            {
                indices[i] = sourceIndices[i];
            }

            meshData.subMeshCount = 1;
            meshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles));

            var mesh = new Mesh
            {
                name = $"RawSpriteRenderer {sprite.name} ({flipX}, {flipY})",
                hideFlags = HideFlags.HideAndDontSave
            };

            Mesh.ApplyAndDisposeWritableMeshData(
                meshDataArray,
                mesh,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            );

            var bounds = sprite.bounds;
            bounds.center = Vector3.Scale(bounds.center, scale);
            mesh.bounds = bounds;

            return mesh;
        }

        internal static MaterialEntry? AcquireMaterial(Material? source, Texture? texture)
        {
            if (source == null || texture == null)
            {
                return null;
            }

            var key = (source, texture);
            if (!Materials.TryGetValue(key, out var entry))
            {
                var material = new Material(source)
                {
                    name = $"{source.name} ({texture.name})",
                    hideFlags = HideFlags.HideAndDontSave
                };
                material.SetTexture(MainTexId, texture);
                entry = new MaterialEntry(key, material);
                Materials.Add(key, entry);
            }

            entry.References++;
            return entry;
        }

        internal static void Release(MeshEntry? entry)
        {
            if (entry == null || --entry.References != 0)
            {
                return;
            }

            Meshes.Remove(entry.Key);
            DestroyResource(entry.Mesh);
        }

        internal static void Release(MaterialEntry? entry)
        {
            if (entry == null || --entry.References != 0)
            {
                return;
            }

            Materials.Remove(entry.Key);
            DestroyResource(entry.Material);
        }

        static void DestroyResource(Object resource)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(resource);
            }
            else
            {
                Object.DestroyImmediate(resource);
            }
        }
    }
}
