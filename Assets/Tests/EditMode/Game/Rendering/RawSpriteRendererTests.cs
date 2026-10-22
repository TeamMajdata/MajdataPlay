using System;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>
    /// Verifies the SpriteRenderer-like API of <see cref="RawSpriteRenderer"/> and <see cref="NoteRenderer"/> in Edit Mode,
    /// where the components run because they execute always.
    /// </summary>
    public class RawSpriteRendererTests
    {
        /// <summary>The shader property holding the renderer colour.</summary>
        private static readonly int RendererColorId = Shader.PropertyToID("_RendererColor");

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
            var texture = _objects.Track(new Texture2D(32, 32) { name = "RawSpriteTexture" });
            _sprite = _objects.Track(Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 16f, 0,
                SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4)));
            _material = _objects.Track(new Material(Shader.Find("Sprites/Default")) { name = "RawSpriteMaterial" });
        }

        /// <summary>Destroys the created objects.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks the default state after the component is added.</summary>
        [Test]
        public void Defaults_AreSpriteRendererLike()
        {
            var renderer = Create();

            Assert.That(renderer.Sprite, Is.Null);
            Assert.That(renderer.Color, Is.EqualTo(Color.white));
            Assert.That(renderer.DrawMode, Is.EqualTo(SpriteDrawMode.Simple));
            Assert.That(renderer.Size, Is.EqualTo(Vector2.one));
            Assert.That(renderer.TileMode, Is.EqualTo(SpriteTileMode.Continuous));
            Assert.That(renderer.AdaptiveModeThreshold, Is.EqualTo(0.5f));
            Assert.That(renderer.SortingLayerName, Is.EqualTo("Default"));
            Assert.That(renderer.SortingOrder, Is.EqualTo(0));
            Assert.That(renderer.ShadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.ReceiveShadows, Is.False);
            Assert.That(renderer.MotionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.Object));
            Assert.That(renderer.RendererComponent, Is.SameAs(renderer.GetComponent<MeshRenderer>()));
            Assert.That(renderer.IsVisible, Is.False);
            Assert.That(renderer.Vounds, Is.EqualTo(renderer.GetComponent<MeshRenderer>().bounds));
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.Null);
        }

        /// <summary>Checks that assigning a sprite builds its mesh and a material carrying its texture.</summary>
        [Test]
        public void Sprite_BuildsMeshAndMaterial()
        {
            var renderer = Create();
            renderer.SharedMaterial = _material;

            renderer.Sprite = _sprite;
            renderer.Sprite = _sprite;
            Apply(renderer);

            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var meshRenderer = renderer.GetComponent<MeshRenderer>();
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.EqualTo(_sprite.vertices.Length));
            Assert.That(meshRenderer.sharedMaterial, Is.Not.SameAs(_material));
            Assert.That(meshRenderer.sharedMaterial.GetTexture("_MainTex"), Is.SameAs(_sprite.texture));
            Assert.That(renderer.SharedMaterial, Is.SameAs(_material));
        }

        /// <summary>Checks that renderers with identical settings share one mesh and material.</summary>
        [Test]
        public void IdenticalRenderers_ShareMeshAndMaterial()
        {
            var first = Create(_sprite);
            var second = Create(_sprite);

            Assert.That(second.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(first.GetComponent<MeshFilter>().sharedMesh));
            Assert.That(second.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(first.GetComponent<MeshRenderer>().sharedMaterial));
        }

        /// <summary>Checks that the colour is written to the renderer property block.</summary>
        [Test]
        public void Color_IsWrittenToThePropertyBlock()
        {
            var renderer = Create(_sprite);

            renderer.Color = Color.red;
            renderer.Color = Color.red;
            Apply(renderer);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);

            Assert.That(block.GetColor(RendererColorId), Is.EqualTo(Color.red));
        }

        /// <summary>Checks that flips and draw mode changes rebuild the geometry.</summary>
        [Test]
        public void GeometrySettings_RebuildTheMesh()
        {
            var renderer = Create(_sprite);
            var filter = renderer.GetComponent<MeshFilter>();
            var simple = filter.sharedMesh;
            var simpleX = simple.vertices[0].x;

            renderer.FlipX = true;
            renderer.FlipX = true;
            renderer.FlipY = true;
            renderer.FlipY = true;
            Apply(renderer);
            var flipped = filter.sharedMesh;
            var flippedX = flipped.vertices[0].x;
            renderer.DrawMode = SpriteDrawMode.Sliced;
            renderer.DrawMode = SpriteDrawMode.Sliced;
            renderer.Size = new Vector2(4, 4);
            renderer.Size = new Vector2(4, 4);
            Apply(renderer);
            var sliced = filter.sharedMesh;
            var slicedVertices = sliced.vertexCount;
            renderer.DrawMode = SpriteDrawMode.Tiled;
            renderer.TileMode = SpriteTileMode.Adaptive;
            renderer.TileMode = SpriteTileMode.Adaptive;
            renderer.AdaptiveModeThreshold = 0.25f;
            renderer.AdaptiveModeThreshold = 0.25f;
            Apply(renderer);

            Assert.That(renderer.FlipX && renderer.FlipY, Is.True);
            Assert.That(flipped, Is.Not.SameAs(simple));
            Assert.That(flippedX, Is.EqualTo(-simpleX).Within(1e-5f));
            Assert.That(slicedVertices, Is.EqualTo(36));
            Assert.That(filter.sharedMesh, Is.Not.SameAs(sliced));
            Assert.That(simple == null, Is.True, "meshes no renderer uses are destroyed");
        }

        /// <summary>Checks that invalid sizes and thresholds are sanitised.</summary>
        [Test]
        public void Size_AndThreshold_AreSanitised()
        {
            var renderer = Create();

            renderer.Size = new Vector2(float.NaN, -3f);
            Assert.That(renderer.Size, Is.EqualTo(Vector2.zero));
            renderer.Size = new Vector2(float.PositiveInfinity, 2f);
            Assert.That(renderer.Size, Is.EqualTo(new Vector2(0f, 2f)));
            renderer.AdaptiveModeThreshold = float.NaN;
            Assert.That(renderer.AdaptiveModeThreshold, Is.EqualTo(0.5f));
            renderer.AdaptiveModeThreshold = 3f;
            Assert.That(renderer.AdaptiveModeThreshold, Is.EqualTo(1f));
            renderer.AdaptiveModeThreshold = -1f;
            Assert.That(renderer.AdaptiveModeThreshold, Is.EqualTo(0f));
        }

        /// <summary>Checks that sorting settings are applied to the mesh renderer.</summary>
        [Test]
        public void Sorting_IsAppliedToTheMeshRenderer()
        {
            var renderer = Create(_sprite);
            var meshRenderer = renderer.GetComponent<MeshRenderer>();

            renderer.SortingOrder = 7;
            renderer.SortingOrder = 7;
            renderer.SortingLayerName = null!;
            renderer.SortingLayerName = "Default";
            Apply(renderer);

            Assert.That(meshRenderer.sortingOrder, Is.EqualTo(7));
            Assert.That(renderer.SortingLayerName, Is.EqualTo("Default"));
            Assert.That(renderer.SortingLayerID, Is.EqualTo(SortingLayer.NameToID("Default")));

            renderer.SortingLayerID = SortingLayer.NameToID("Default");
            Assert.That(meshRenderer.sortingLayerID, Is.EqualTo(SortingLayer.NameToID("Default")));
            var other = SortingLayer.layers[SortingLayer.layers.Length - 1];
            renderer.SortingLayerID = other.id;
            Assert.That(meshRenderer.sortingLayerID, Is.EqualTo(other.id));
            Assert.That(renderer.SortingLayerName, Is.EqualTo(other.name));
        }

        /// <summary>Checks that the shadow and motion vector settings are applied immediately.</summary>
        [Test]
        public void RendererSettings_AreAppliedImmediately()
        {
            var renderer = Create();
            var meshRenderer = renderer.GetComponent<MeshRenderer>();

            renderer.ShadowCastingMode = ShadowCastingMode.On;
            renderer.ShadowCastingMode = ShadowCastingMode.On;
            renderer.ReceiveShadows = true;
            renderer.ReceiveShadows = true;
            renderer.MotionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.MotionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            Assert.That(meshRenderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
            Assert.That(meshRenderer.receiveShadows, Is.True);
            Assert.That(meshRenderer.motionVectorGenerationMode, Is.EqualTo(MotionVectorGenerationMode.ForceNoMotion));
        }

        /// <summary>Checks that clearing the shared material falls back to the mesh renderer's material.</summary>
        [Test]
        public void SharedMaterial_FallsBackToTheMeshRenderer()
        {
            var renderer = Create();
            renderer.SharedMaterial = _material;
            renderer.SharedMaterial = _material;

            renderer.SharedMaterial = null!;

            Assert.That(renderer.SharedMaterial, Is.SameAs(renderer.GetComponent<MeshRenderer>().sharedMaterial));
        }

        /// <summary>Checks the property block accessors and their argument validation.</summary>
        [Test]
        public void PropertyBlocks_ValidateArguments()
        {
            var renderer = Create(_sprite);
            var block = new MaterialPropertyBlock();
            block.SetFloat("_Custom", 2f);

            renderer.SetPropertyBlock(block);
            renderer.SetPropertyBlock(block, 0);
            var read = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(read, 0);

            Assert.That(read.GetFloat("_Custom"), Is.EqualTo(2f));
            Assert.Throws<ArgumentNullException>(() => renderer.GetPropertyBlock(null!));
            Assert.Throws<ArgumentNullException>(() => renderer.SetPropertyBlock(null!));
            Assert.Throws<ArgumentNullException>(() => renderer.GetPropertyBlock(null!, 0));
            Assert.Throws<ArgumentNullException>(() => renderer.SetPropertyBlock(null!, 0));
        }

        /// <summary>Checks that changes made behind the property setters, as by the Inspector, are detected.</summary>
        [Test]
        public void SerializedFieldChanges_AreDetected()
        {
            var renderer = Create(_sprite);
            var meshRenderer = renderer.GetComponent<MeshRenderer>();

            PrivateMember.SetField(renderer, "_color", Color.blue);
            PrivateMember.SetField(renderer, "_sortingOrder", 3);
            PrivateMember.SetField(renderer, "_sortingLayerName", SortingLayer.layers[SortingLayer.layers.Length - 1].name);
            PrivateMember.SetField(renderer, "_flipX", true);
            PrivateMember.SetField(renderer, "_sharedMaterial", _material);
            PrivateMember.SetField(renderer, "_sprite", null);
            Apply(renderer);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);

            Assert.That(block.GetColor(RendererColorId), Is.EqualTo(Color.blue));
            Assert.That(meshRenderer.sortingOrder, Is.EqualTo(3));
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.Null);
        }

        /// <summary>Checks that the editor update path, validation hooks and enable state drive the renderer.</summary>
        [Test]
        public void EditorHooksAndEnableState()
        {
            var renderer = Create(_sprite);
            var meshRenderer = renderer.GetComponent<MeshRenderer>();

            renderer.Color = Color.green;
            UnityMessages.Send(renderer, "LateUpdate");
            UnityMessages.Send(renderer, "OnValidate");
            PrivateMember.Invoke(renderer, "DelayedValidate");
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(RendererColorId), Is.EqualTo(Color.green));

            renderer.enabled = false;
            Assert.That(meshRenderer.enabled, Is.False);
            renderer.Color = Color.black;
            Apply(renderer);
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(RendererColorId), Is.EqualTo(Color.green), "disabled renderers are not updated");
            PrivateMember.Invoke(renderer, "DelayedValidate");

            renderer.enabled = true;
            Assert.That(meshRenderer.enabled, Is.True);
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor(RendererColorId), Is.EqualTo(Color.black));
        }

        /// <summary>Checks that a destroyed renderer releases its shared resources and ignores late validation.</summary>
        [Test]
        public void Destroy_ReleasesResources()
        {
            var renderer = Create(_sprite);
            renderer.SharedMaterial = _material;
            Apply(renderer);
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var material = renderer.GetComponent<MeshRenderer>().sharedMaterial;

            UnityEngine.Object.DestroyImmediate(renderer.gameObject);

            Assert.That(mesh == null, Is.True);
            Assert.That(material == null, Is.True);
            Assert.DoesNotThrow(() => PrivateMember.Invoke(renderer, "DelayedValidate"));
        }

        /// <summary>Checks the registration bookkeeping of the shared updater.</summary>
        [Test]
        public void Updater_RegistrationBookkeeping()
        {
            var renderers = new RawSpriteRenderer[20];
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i] = Create();
            }

            RawSpriteRenderer.Updater.Register(renderers[0]);
            RawSpriteRenderer.Updater.Register(null!);
            RawSpriteRenderer.Updater.Unregister(null!);
            RawSpriteRenderer.Updater.Unregister(renderers[3]);
            RawSpriteRenderer.Updater.Unregister(renderers[3]);
            Assert.That(PrivateMember.GetField<int>(renderers[3], "_rawSpriteUpdaterIndex"), Is.EqualTo(-1));
            RawSpriteRenderer.Updater.Register(renderers[3]);
            Assert.That(PrivateMember.GetField<int>(renderers[3], "_rawSpriteUpdaterIndex"), Is.GreaterThanOrEqualTo(0));

            renderers[5].Color = Color.cyan;
            PrivateMember.InvokeStatic(typeof(RawSpriteRenderer.Updater), "UpdateAll");
            var block = new MaterialPropertyBlock();
            renderers[5].GetPropertyBlock(block);
            Assert.That(block.GetColor(RendererColorId), Is.EqualTo(Color.cyan));
        }

        /// <summary>Checks that the updater finds or inserts its marker in a player loop.</summary>
        [Test]
        public void Updater_PlayerLoopInsertion()
        {
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var marker = typeof(RawSpriteRenderer.Updater).GetNestedType("PlayerLoopMarker", System.Reflection.BindingFlags.NonPublic)!;

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(RawSpriteRenderer.Updater), "ContainsMarker", loop, marker), Is.True,
                "registering a renderer installs the updater");
            var empty = new UnityEngine.LowLevel.PlayerLoopSystem();
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(RawSpriteRenderer.Updater), "ContainsMarker", empty, marker), Is.False);
        }

        /// <summary>Checks that the note renderer falls back to the base mesh and material while the editor is not playing.</summary>
        [Test]
        public void NoteRenderer_UsesBaseResourcesOutsidePlayMode()
        {
            var renderer = _objects.Add<NoteRenderer>();
            UnityMessages.Send(renderer, "Awake");
            UnityMessages.Send(renderer, "OnEnable");
            renderer.SharedMaterial = _material;
            renderer.Sprite = _sprite;
            Apply(renderer);
            var simpleMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            renderer.DrawMode = SpriteDrawMode.Sliced;
            renderer.Size = new Vector2(3, 3);
            Apply(renderer);

            Assert.That(simpleMesh, Is.Not.Null);
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(36));
            Assert.That(renderer.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_MainTex"), Is.SameAs(_sprite.texture));
            Assert.That(PrivateMember.GetField<int>(renderer, "_resourceVersion"), Is.EqualTo(NoteSpriteResources.Version));
        }

        /// <summary>Creates a renderer, optionally with a sprite already applied.</summary>
        /// <param name="sprite">The sprite to assign, or null.</param>
        /// <returns>The renderer.</returns>
        private RawSpriteRenderer Create(Sprite? sprite = null)
        {
            var renderer = _objects.Add<RawSpriteRenderer>();
            if (sprite != null)
            {
                renderer.Sprite = sprite;
                Apply(renderer);
            }
            return renderer;
        }

        /// <summary>Runs the pre-late-update pass of a renderer.</summary>
        /// <param name="renderer">The renderer.</param>
        private static void Apply(RawSpriteRenderer renderer)
        {
            PrivateMember.Invoke(renderer, "OnPreLateUpdate");
        }
    }
}
