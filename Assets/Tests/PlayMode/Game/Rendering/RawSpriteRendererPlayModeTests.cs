using System;
using System.Collections;
using System.Reflection;
using MajdataPlay.Drawing;
using MajdataPlay.Rendering;
using MajdataPlay.Scenes.Game.Notes.Skins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering.PlayMode
{
    /// <summary>
    /// Verifies that the player loop updater drives <see cref="RawSpriteRenderer"/> in Play Mode and that
    /// <see cref="NoteRenderer"/> borrows the preloaded note resources while playing.
    /// </summary>
    public class RawSpriteRendererPlayModeTests
    {
        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>A bordered sprite.</summary>
        private Sprite _sprite = null!;

        /// <summary>A source material.</summary>
        private Material _material = null!;

        /// <summary>Creates the sprite and material and clears the note caches.</summary>
        [SetUp]
        public void SetUp()
        {
            PrivateMember.InvokeStatic(typeof(NoteSpriteResources), "ResetStatics");
            _objects = new TestObjects();
            var texture = _objects.Track(new Texture2D(32, 32) { name = "PlayModeSpriteTexture" });
            _sprite = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 16f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4)));
            _material = _objects.Track(new Material(Shader.Find("Sprites/Default")) { name = "PlayModeMaterial" });
        }

        /// <summary>Destroys the objects and clears the note caches.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            PrivateMember.InvokeStatic(typeof(NoteSpriteResources), "ResetStatics");
        }

        /// <summary>Checks that property changes are applied by the updater on the next frame.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Updater_AppliesChangesEachFrame()
        {
            var renderer = _objects.Add<RawSpriteRenderer>();
            renderer.SharedMaterial = _material;

            renderer.Sprite = _sprite;
            renderer.Color = Color.yellow;
            renderer.SortingOrder = 4;
            yield return null;
            yield return null;

            var filter = renderer.GetComponent<MeshFilter>();
            var meshRenderer = renderer.GetComponent<MeshRenderer>();
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Assert.That(meshRenderer.sortingOrder, Is.EqualTo(4));
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor("_RendererColor"), Is.EqualTo(Color.yellow));

            renderer.DrawMode = SpriteDrawMode.Sliced;
            renderer.Size = new Vector2(3, 3);
            yield return null;
            Assert.That(filter.sharedMesh.vertexCount, Is.EqualTo(36));

            var mesh = filter.sharedMesh;
            UnityEngine.Object.Destroy(renderer.gameObject);
            yield return null;
            yield return null;
            Assert.That(mesh == null, Is.True, "destroying the last user releases the mesh");
        }

        /// <summary>Checks that a playing note renderer uses the preloaded Simple mesh and material and falls back for Sliced meshes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NoteRenderer_UsesPreloadedResources()
        {
            var skin = (CustomSkin)Activator.CreateInstance(typeof(CustomSkin), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "PlayModeSkin" }, null)!;
            PrivateMember.SetProperty(skin, nameof(CustomSkin.IsLoaded), true);
            NoteSpriteResources.PreloadMeshes(skin, new[] { _sprite });
            NoteSpriteResources.PreloadMaterials(skin, _material);
            var renderer = _objects.Add<NoteRenderer>();
            renderer.SharedMaterial = _material;
            renderer.Sprite = _sprite;
            yield return null;
            yield return null;

            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(NoteSpriteResources.GetMesh(_sprite)));
            Assert.That(renderer.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(NoteSpriteResources.GetMaterial(_material, _sprite.texture)));

            renderer.DrawMode = SpriteDrawMode.Sliced;
            renderer.Size = new Vector2(2, 2);
            yield return null;
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs(NoteSpriteResources.GetMesh(_sprite)));

            NoteSpriteResources.Unload(skin);
            yield return null;
            Assert.That(PrivateMember.GetField<int>(renderer, "_resourceVersion"), Is.EqualTo(NoteSpriteResources.Version));
        }
    }
}
