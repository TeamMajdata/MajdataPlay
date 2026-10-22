using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.Drawing;
using MajdataPlay.Rendering;
using MajdataPlay.Scenes.Game.Notes.Skins;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Verifies the per-skin ownership of preloaded note meshes and materials in <see cref="NoteSpriteResources"/>.</summary>
    public class NoteSpriteResourcesTests
    {
        /// <summary>The created textures, sprites and materials.</summary>
        private TestObjects _objects = null!;

        /// <summary>A sprite with its own texture.</summary>
        private Sprite _first = null!;

        /// <summary>A second sprite with another texture.</summary>
        private Sprite _second = null!;

        /// <summary>A source material.</summary>
        private Material _source = null!;

        /// <summary>Creates the sprites and material and clears the caches.</summary>
        [SetUp]
        public void SetUp()
        {
            ResetStatics();
            _objects = new TestObjects();
            _first = CreateSprite("First");
            _second = CreateSprite("Second");
            _source = _objects.Track(new Material(Shader.Find("Sprites/Default")) { name = "NoteSource" });
        }

        /// <summary>Clears the caches and destroys the objects.</summary>
        [TearDown]
        public void TearDown()
        {
            ResetStatics();
            _objects.Dispose();
        }

        /// <summary>Checks that preloading creates meshes for the skin's sprites and the empty sprite.</summary>
        [Test]
        public void PreloadMeshes_CreatesMeshesForSpritesAndEmptySprite()
        {
            var skin = CreateSkin("Preload", true);
            var version = NoteSpriteResources.Version;

            NoteSpriteResources.PreloadMeshes(skin, new[] { _first, _first, null!, _second });

            Assert.That(NoteSpriteResources.GetMesh(_first), Is.Not.Null);
            Assert.That(NoteSpriteResources.GetMesh(_second), Is.Not.Null);
            Assert.That(NoteSpriteResources.GetMesh(SpriteLoader.EmptySprite), Is.Not.Null);
            Assert.That(NoteSpriteResources.GetMesh(null), Is.Null);
            Assert.That(NoteSpriteResources.Version, Is.GreaterThan(version));

            var unchanged = NoteSpriteResources.Version;
            NoteSpriteResources.PreloadMeshes(skin, new[] { _first });
            Assert.That(NoteSpriteResources.Version, Is.EqualTo(unchanged), "preloading a skin twice is ignored");
        }

        /// <summary>Checks that a lookup miss never allocates a mesh.</summary>
        [Test]
        public void GetMesh_UnknownSprite_ReturnsNull()
        {
            Assert.That(NoteSpriteResources.GetMesh(_first), Is.Null);
            Assert.That(NoteSpriteResources.GetMaterial(_source, _first.texture), Is.Null);
            Assert.That(NoteSpriteResources.GetMaterial(null, _first.texture), Is.Null);
            Assert.That(NoteSpriteResources.GetMaterial(_source, null), Is.Null);
        }

        /// <summary>Checks that a mesh shared by two skins survives until both are unloaded.</summary>
        [Test]
        public void Unload_KeepsMeshesUsedByAnotherSkin()
        {
            var a = CreateSkin("A", true);
            var b = CreateSkin("B", true);
            NoteSpriteResources.PreloadMeshes(a, new[] { _first });
            NoteSpriteResources.PreloadMeshes(b, new[] { _first });
            var mesh = NoteSpriteResources.GetMesh(_first);

            NoteSpriteResources.Unload(a);
            Assert.That(NoteSpriteResources.GetMesh(_first), Is.SameAs(mesh));

            NoteSpriteResources.Unload(b);
            Assert.That(NoteSpriteResources.GetMesh(_first), Is.Null);
            Assert.That(mesh == null, Is.True, "the last owner destroys the mesh");
            Assert.DoesNotThrow(() => NoteSpriteResources.Unload(b));
        }

        /// <summary>Checks that materials are prepared for every skin texture, shared between skins and released with them.</summary>
        [Test]
        public void PreloadMaterials_PreparesEveryTexturePair()
        {
            var a = CreateSkin("A", true);
            var b = CreateSkin("B", true);
            NoteSpriteResources.PreloadMeshes(a, new[] { _first });
            NoteSpriteResources.PreloadMeshes(b, new[] { _first, _second });

            NoteSpriteResources.PreloadMaterials(a, _source, null);
            NoteSpriteResources.PreloadMaterials(a, _source);
            NoteSpriteResources.PreloadMaterials(b, _source);
            var material = NoteSpriteResources.GetMaterial(_source, _first.texture);

            Assert.That(material, Is.Not.Null);
            Assert.That(material!.GetTexture("_MainTex"), Is.SameAs(_first.texture));
            Assert.That(NoteSpriteResources.GetMaterial(_source, _second.texture), Is.Not.Null);
            Assert.That(NoteSpriteResources.GetMaterial(_source, SpriteLoader.EmptySprite.texture), Is.Not.Null);

            NoteSpriteResources.Unload(a);
            Assert.That(NoteSpriteResources.GetMaterial(_source, _first.texture), Is.SameAs(material));
            NoteSpriteResources.Unload(b);
            Assert.That(NoteSpriteResources.GetMaterial(_source, _first.texture), Is.Null);
        }

        /// <summary>Checks that materials need a loaded skin whose meshes were preloaded, except for the built-in empty skin.</summary>
        [Test]
        public void PreloadMaterials_RequiresALoadedPreloadedSkin()
        {
            Assert.Throws<InvalidOperationException>(() => NoteSpriteResources.PreloadMaterials(CreateSkin("Unloaded", false), _source));
            Assert.Throws<InvalidOperationException>(() => NoteSpriteResources.PreloadMaterials(CreateSkin("NotPreloaded", true), _source));

            NoteSpriteResources.PreloadMaterials(CustomSkin.Empty, _source);

            Assert.That(NoteSpriteResources.GetMaterial(_source, SpriteLoader.EmptySprite.texture), Is.Not.Null);
            NoteSpriteResources.Unload(CustomSkin.Empty);
        }

        /// <summary>Checks that a failure while enumerating sprites unloads the partially preloaded skin.</summary>
        [Test]
        public void PreloadMeshes_FailingEnumeration_UnloadsTheSkin()
        {
            var skin = CreateSkin("Failing", true);

            Assert.Throws<InvalidOperationException>(() => NoteSpriteResources.PreloadMeshes(skin, FailAfter(_first)));

            Assert.That(NoteSpriteResources.GetMesh(_first), Is.Null);
            Assert.DoesNotThrow(() => NoteSpriteResources.PreloadMeshes(skin, new[] { _first }));
            Assert.That(NoteSpriteResources.GetMesh(_first), Is.Not.Null);
        }

        /// <summary>Clears every cached resource through the play mode reset hook.</summary>
        private static void ResetStatics()
        {
            PrivateMember.InvokeStatic(typeof(NoteSpriteResources), "ResetStatics");
        }

        /// <summary>Creates a skin with only a name, optionally marked as loaded.</summary>
        /// <param name="name">The skin name.</param>
        /// <param name="loaded">Whether the skin reports itself as loaded.</param>
        /// <returns>The skin.</returns>
        private static CustomSkin CreateSkin(string name, bool loaded)
        {
            var skin = (CustomSkin)Activator.CreateInstance(typeof(CustomSkin), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { name }, null)!;
            PrivateMember.SetProperty(skin, nameof(CustomSkin.IsLoaded), loaded);
            return skin;
        }

        /// <summary>Creates a sprite with its own texture.</summary>
        /// <param name="name">The sprite and texture name.</param>
        /// <returns>The sprite.</returns>
        private Sprite CreateSprite(string name)
        {
            var texture = _objects.Track(new Texture2D(8, 8) { name = name });
            var sprite = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f));
            sprite.name = name;
            return sprite;
        }

        /// <summary>Yields one sprite and then fails.</summary>
        /// <param name="sprite">The sprite yielded before the failure.</param>
        /// <returns>The failing sequence.</returns>
        /// <exception cref="InvalidOperationException">Always, after the first element.</exception>
        private static IEnumerable<Sprite> FailAfter(Sprite sprite)
        {
            yield return sprite;
            throw new InvalidOperationException("enumeration failed");
        }
    }
}
