using UnityEngine;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Borrows preloaded skin resources for Simple mode. Flips are always ignored;
    /// Sliced/Tiled meshes use the base renderer's reference-counted cache.
    /// </summary>
    [AddComponentMenu("Rendering/Note Renderer")]
    public sealed class NoteRenderer : RawSpriteRenderer
    {
        int _resourceVersion = -1;

        protected override void OnPreLateUpdate()
        {
            if (_resourceVersion != NoteSpriteResources.Version)
            {
                _resourceVersion = NoteSpriteResources.Version;
                MarkAllDirty();
            }
            base.OnPreLateUpdate();
        }

        protected override Mesh? AcquireMesh(Sprite? sprite, bool flipX, bool flipY)
        {
            if (DrawMode != SpriteDrawMode.Simple)
                return base.AcquireMesh(sprite, false, false);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return base.AcquireMesh(sprite, false, false);
#endif
            return NoteSpriteResources.GetMesh(sprite);
        }

        protected override Material AcquireMaterial(Material? source, Texture? texture)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return base.AcquireMaterial(source, texture);
#endif
            return NoteSpriteResources.GetMaterial(source, texture)!;
        }
    }
}
