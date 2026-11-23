using System.Collections;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering.PlayMode
{
    /// <summary>
    /// Verifies that the player loop updater drives <see cref="RawSpriteRenderer"/> in Play Mode.
    /// </summary>
    public class RawSpriteRendererPlayModeTests
    {
        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>A bordered sprite.</summary>
        private Sprite _sprite = null!;

        /// <summary>A source material.</summary>
        private Material _material = null!;

        /// <summary>Creates the sprite and material.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            var texture = _objects.Track(new Texture2D(32, 32) { name = "PlayModeSpriteTexture" });
            _sprite = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 16f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4)));
            _material = _objects.Track(new Material(Shader.Find("Sprites/Default")) { name = "PlayModeMaterial" });
        }

        /// <summary>Destroys the objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
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
    }
}
