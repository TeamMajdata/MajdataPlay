using System.Collections.Generic;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>
    /// Verifies the shared mesh and material cache <see cref="RawSpriteResources"/> and the Sliced/Tiled geometry of
    /// <see cref="RawSpriteMeshBuilder"/>, ported from the RawSprite draw mode validation.
    /// </summary>
    public class RawSpriteResourcesTests
    {
        /// <summary>The created textures, sprites and materials.</summary>
        private TestObjects _objects = null!;

        /// <summary>The bordered test sprite.</summary>
        private Sprite _sprite = null!;

        /// <summary>The mesh entries acquired by the test and released after it.</summary>
        private readonly List<RawSpriteResources.MeshEntry> _meshes = new();

        /// <summary>The material entries acquired by the test and released after it.</summary>
        private readonly List<RawSpriteResources.MaterialEntry> _materials = new();

        /// <summary>Creates a 32x32 sprite inside a 64x64 texture with borders and an off-centre pivot.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            var texture = _objects.Track(new Texture2D(64, 64));
            _sprite = _objects.Track(Sprite.Create(texture, new Rect(16, 8, 32, 32), new Vector2(0.25f, 0.75f), 16f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 8, 4, 8)));
        }

        /// <summary>Releases every acquired entry and destroys the test objects.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var entry in _meshes)
            {
                RawSpriteResources.Release(entry);
            }
            foreach (var entry in _materials)
            {
                RawSpriteResources.Release(entry);
            }
            _meshes.Clear();
            _materials.Clear();
            _objects.Dispose();
        }

        /// <summary>Checks that Simple meshes copy the sprite geometry and ignore the size in the cache key.</summary>
        [Test]
        public void Simple_CopiesSpriteGeometryAndIgnoresSize()
        {
            var simple = Acquire(SpriteDrawMode.Simple, Vector2.one);

            Assert.That(simple.Mesh.vertexCount, Is.EqualTo(_sprite.vertices.Length));
            Assert.That(Acquire(SpriteDrawMode.Simple, Vector2.one * 10), Is.SameAs(simple));
            Assert.That(simple.References, Is.EqualTo(2));
            Assert.That(simple.Mesh.bounds.center, Is.EqualTo((Vector3)_sprite.bounds.center));
        }

        /// <summary>Checks that flipped Simple meshes mirror the sprite vertices and bounds.</summary>
        [Test]
        public void Simple_FlipsMirrorVertices()
        {
            var normal = Track(RawSpriteResources.AcquireMesh(_sprite, false, false)!);
            var flipped = Track(RawSpriteResources.AcquireMesh(_sprite, true, true)!);

            Assert.That(flipped, Is.Not.SameAs(normal));
            Assert.That(flipped.Mesh.vertices[0].x, Is.EqualTo(-normal.Mesh.vertices[0].x).Within(1e-5f));
            Assert.That(flipped.Mesh.vertices[0].y, Is.EqualTo(-normal.Mesh.vertices[0].y).Within(1e-5f));
            Assert.That(flipped.Mesh.bounds.center.x, Is.EqualTo(-normal.Mesh.bounds.center.x).Within(1e-5f));
        }

        /// <summary>Checks the nine-slice layout, pivot, borders and sub-rectangle UVs of a Sliced mesh.</summary>
        [Test]
        public void Sliced_BuildsNineQuadsAroundThePivot()
        {
            var sliced = Acquire(SpriteDrawMode.Sliced, new Vector2(4, 3));
            var mesh = sliced.Mesh;

            Assert.That(mesh.vertexCount, Is.EqualTo(36));
            Assert.That(mesh.bounds.min.x, Is.EqualTo(-1f).Within(1e-4f), "pivot x");
            Assert.That(mesh.bounds.min.y, Is.EqualTo(-2.25f).Within(1e-4f), "pivot y");
            Assert.That(mesh.bounds.size.x, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(mesh.bounds.size.y, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(mesh.vertices[2].x - mesh.vertices[0].x, Is.EqualTo(0.25f).Within(1e-4f), "left border");
            Assert.That(mesh.vertices[1].y - mesh.vertices[0].y, Is.EqualTo(0.5f).Within(1e-4f), "bottom border");
            Assert.That(mesh.uv[0].x, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(mesh.uv[0].y, Is.EqualTo(0.125f).Within(1e-4f));
            Assert.That(mesh.colors32[0], Is.EqualTo(new Color32(255, 255, 255, 255)));
        }

        /// <summary>Checks that Sliced meshes are shared per size and separated by size.</summary>
        [Test]
        public void Sliced_IsSharedPerSize()
        {
            var sliced = Acquire(SpriteDrawMode.Sliced, new Vector2(4, 3));

            Assert.That(Acquire(SpriteDrawMode.Sliced, new Vector2(4, 3)), Is.SameAs(sliced));
            Assert.That(Acquire(SpriteDrawMode.Sliced, new Vector2(5, 3)), Is.Not.SameAs(sliced));
        }

        /// <summary>Checks that borders larger than the size are compressed and the empty centre is omitted.</summary>
        [Test]
        public void Sliced_CompressedBorders_OmitTheCentre()
        {
            var small = Acquire(SpriteDrawMode.Sliced, new Vector2(0.25f, 0.5f));

            Assert.That(small.Mesh.vertexCount, Is.EqualTo(16));
            Assert.That(small.Mesh.vertices[2].x - small.Mesh.vertices[0].x, Is.EqualTo(0.125f).Within(1e-4f));
        }

        /// <summary>Checks that continuous tiling repeats the centre and crops the final tile's UVs.</summary>
        [Test]
        public void TiledContinuous_RepeatsAndCropsTheLastTile()
        {
            var tiled = Acquire(SpriteDrawMode.Tiled, new Vector2(4, 3));

            Assert.That(tiled.Mesh.vertexCount, Is.EqualTo(80));
            Assert.That(tiled.Mesh.uv[14].x, Is.EqualTo(0.4375f).Within(1e-4f));
        }

        /// <summary>Checks that adaptive tiling stretches whole tiles and respects the threshold.</summary>
        [Test]
        public void TiledAdaptive_UsesWholeTilesAndTheThreshold()
        {
            var adaptive = Acquire(SpriteDrawMode.Tiled, new Vector2(4, 3), SpriteTileMode.Adaptive);

            Assert.That(adaptive.Mesh.vertexCount, Is.EqualTo(64));
            Assert.That(adaptive.Mesh.uv[10].x, Is.EqualTo(0.6875f).Within(1e-4f));
            Assert.That(Acquire(SpriteDrawMode.Tiled, new Vector2(2.75f, 2), SpriteTileMode.Adaptive).Mesh.vertexCount, Is.EqualTo(36));
            Assert.That(Acquire(SpriteDrawMode.Tiled, new Vector2(2.76f, 2), SpriteTileMode.Adaptive).Mesh.vertexCount, Is.EqualTo(48));
        }

        /// <summary>Checks that every flip combination keeps the pivot mirrored and a consistent triangle winding.</summary>
        [Test]
        public void Sliced_FlipsKeepWinding()
        {
            foreach (var flipX in new[] { false, true })
            {
                foreach (var flipY in new[] { false, true })
                {
                    var flipped = Track(RawSpriteResources.AcquireMesh(_sprite, flipX, flipY, SpriteDrawMode.Sliced, new Vector2(4, 3))!);
                    var vertices = flipped.Mesh.vertices;
                    var indices = flipped.Mesh.triangles;

                    Assert.That(flipped.Mesh.bounds.center.x, Is.EqualTo(flipX ? -1f : 1f).Within(1e-4f));
                    Assert.That(flipped.Mesh.bounds.center.y, Is.EqualTo(flipY ? 0.75f : -0.75f).Within(1e-4f));
                    var normal = Vector3.Cross(vertices[indices[1]] - vertices[indices[0]], vertices[indices[2]] - vertices[indices[0]]);
                    Assert.That(normal.z, Is.LessThan(0f), $"winding {flipX} {flipY}");
                }
            }
        }

        /// <summary>Checks the degenerate cases: zero size, a missing sprite and the quad budget fallback.</summary>
        [Test]
        public void DegenerateInputs()
        {
            Assert.That(Acquire(SpriteDrawMode.Tiled, Vector2.zero).Mesh.vertexCount, Is.EqualTo(0));
            Assert.That(RawSpriteResources.AcquireMesh(null, false, false), Is.Null);
            Assert.That(Acquire(SpriteDrawMode.Tiled, new Vector2(100000, 100000)).Mesh.vertexCount, Is.EqualTo(36));
        }

        /// <summary>Checks that the last release destroys a shared mesh and earlier releases keep it.</summary>
        [Test]
        public void Release_DestroysOnTheLastReference()
        {
            var first = RawSpriteResources.AcquireMesh(_sprite, false, false, SpriteDrawMode.Sliced, new Vector2(3, 3))!;
            var second = RawSpriteResources.AcquireMesh(_sprite, false, false, SpriteDrawMode.Sliced, new Vector2(3, 3))!;
            var mesh = first.Mesh;

            RawSpriteResources.Release(first);
            Assert.That(mesh != null, Is.True);
            RawSpriteResources.Release(second);
            Assert.That(mesh == null, Is.True);
            Assert.DoesNotThrow(() => RawSpriteResources.Release((RawSpriteResources.MeshEntry?)null));
            Assert.DoesNotThrow(() => RawSpriteResources.Release((RawSpriteResources.MaterialEntry?)null));
        }

        /// <summary>Checks that materials are shared per source and texture, carry the texture and are destroyed on the last release.</summary>
        [Test]
        public void Materials_AreSharedPerSourceAndTexture()
        {
            var source = _objects.Track(new Material(Shader.Find("Sprites/Default")) { name = "Source" });
            var otherTexture = _objects.Track(new Texture2D(4, 4) { name = "Other" });

            var first = RawSpriteResources.AcquireMaterial(source, _sprite.texture)!;
            var second = RawSpriteResources.AcquireMaterial(source, _sprite.texture)!;
            var other = RawSpriteResources.AcquireMaterial(source, otherTexture)!;

            Assert.That(second, Is.SameAs(first));
            Assert.That(other, Is.Not.SameAs(first));
            Assert.That(first.Material.GetTexture("_MainTex"), Is.SameAs(_sprite.texture));
            Assert.That(first.Material.name, Does.StartWith("Source ("));
            Assert.That(RawSpriteResources.AcquireMaterial(null, _sprite.texture), Is.Null);
            Assert.That(RawSpriteResources.AcquireMaterial(source, null), Is.Null);

            var material = first.Material;
            RawSpriteResources.Release(first);
            RawSpriteResources.Release(second);
            RawSpriteResources.Release(other);
            Assert.That(material == null, Is.True);
        }

        /// <summary>Acquires and tracks a mesh of the test sprite.</summary>
        /// <param name="mode">The draw mode.</param>
        /// <param name="size">The draw size.</param>
        /// <param name="tileMode">The tile mode.</param>
        /// <returns>The mesh entry.</returns>
        private RawSpriteResources.MeshEntry Acquire(SpriteDrawMode mode, Vector2 size, SpriteTileMode tileMode = SpriteTileMode.Continuous)
        {
            return Track(RawSpriteResources.AcquireMesh(_sprite, false, false, mode, size, tileMode, 0.5f)!);
        }

        /// <summary>Records a mesh entry for release after the test.</summary>
        /// <param name="entry">The entry.</param>
        /// <returns>The same entry.</returns>
        private RawSpriteResources.MeshEntry Track(RawSpriteResources.MeshEntry entry)
        {
            _meshes.Add(entry);
            return entry;
        }
    }
}
