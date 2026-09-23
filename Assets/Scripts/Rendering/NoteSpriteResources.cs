using System;
using System.Collections.Generic;
using MajdataPlay.Drawing;
using MajdataPlay.Scenes.Game.Notes.Skins;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Main-thread-only skin resource ownership. Preload during loading, borrow
    /// during gameplay, and release only after the skin's renderers stop using it.
    /// </summary>
    public static class NoteSpriteResources
    {
        internal static int Version { get; private set; }

        sealed class SkinResources
        {
            internal readonly HashSet<Sprite> Sprites = new();
            internal readonly HashSet<Texture> Textures = new();
            internal readonly HashSet<(Material, Texture)> Materials = new();
        }

        static readonly Dictionary<CustomSkin, SkinResources> Skins = new();
        static readonly Dictionary<Sprite, (RawSpriteResources.MeshEntry Entry, int Owners)> Meshes = new();
        static readonly Dictionary<(Material, Texture), (RawSpriteResources.MaterialEntry Entry, int Owners)> Materials = new();

        internal static void PreloadMeshes(CustomSkin skin, IEnumerable<Sprite> sprites)
        {
            if (Skins.ContainsKey(skin)) return;
            var resources = new SkinResources();
            Skins.Add(skin, resources);
            try
            {
                foreach (var sprite in sprites)
                    PreloadMesh(resources, sprite);
                // Unassigned optional slots and CustomSkin.Empty use this sprite.
                PreloadMesh(resources, SpriteLoader.EmptySprite);
            }
            catch
            {
                Unload(skin);
                throw;
            }
        }

        static void PreloadMesh(SkinResources resources, Sprite? sprite)
        {
            if (sprite == null || resources.Sprites.Contains(sprite)) return;
            if (Meshes.TryGetValue(sprite, out var cached))
            {
                Meshes[sprite] = (cached.Entry, cached.Owners + 1);
            }
            else
            {
                var entry = RawSpriteResources.AcquireMesh(sprite, false, false)!;
                Meshes.Add(sprite, (entry, 1));
            }
            resources.Sprites.Add(sprite);
            Version++;
            resources.Textures.Add(sprite.texture);
        }

        /// <summary>
        /// Prepares every source-material/skin-texture pair. Repeated calls are
        /// idempotent; additional source materials must be supplied before play.
        /// </summary>
        public static void PreloadMaterials(CustomSkin skin, params Material?[] sources)
        {
            if (!skin.IsLoaded)
                throw new InvalidOperationException("Load the skin before preloading note materials.");
            if (!Skins.TryGetValue(skin, out var resources))
            {
                if (skin != CustomSkin.Empty)
                    throw new InvalidOperationException("The skin meshes have not been preloaded.");
                PreloadMeshes(skin, Array.Empty<Sprite>());
                resources = Skins[skin];
            }

            foreach (var source in sources)
            {
                if (source == null) continue;
                foreach (var texture in resources.Textures)
                {
                    var key = (source, texture);
                    if (resources.Materials.Contains(key)) continue;
                    if (Materials.TryGetValue(key, out var cached))
                    {
                        Materials[key] = (cached.Entry, cached.Owners + 1);
                    }
                    else
                    {
                        var entry = RawSpriteResources.AcquireMaterial(source, texture)!;
                        Materials.Add(key, (entry, 1));
                    }
                    resources.Materials.Add(key);
                    Version++;
                }
            }
        }

        // Lookup misses never fall back to allocating GPU resources.
        public static Mesh? GetMesh(Sprite? sprite)
        {
            return sprite != null && Meshes.TryGetValue(sprite, out var cached)
                ? cached.Entry.Mesh : null;
        }

        public static Material? GetMaterial(Material? source, Texture? texture)
        {
            return source != null && texture != null &&
                Materials.TryGetValue((source, texture), out var cached)
                ? cached.Entry.Material : null;
        }

        internal static void Unload(CustomSkin skin)
        {
            if (!Skins.TryGetValue(skin, out var resources)) return;
            Skins.Remove(skin);
            Version++;
            foreach (var key in resources.Materials)
            {
                var cached = Materials[key];
                if (cached.Owners > 1)
                    Materials[key] = (cached.Entry, cached.Owners - 1);
                else
                {
                    Materials.Remove(key);
                    RawSpriteResources.Release(cached.Entry);
                }
            }
            foreach (var sprite in resources.Sprites)
            {
                var cached = Meshes[sprite];
                if (cached.Owners > 1)
                    Meshes[sprite] = (cached.Entry, cached.Owners - 1);
                else
                {
                    Meshes.Remove(sprite);
                    RawSpriteResources.Release(cached.Entry);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            foreach (var cached in Materials.Values)
                RawSpriteResources.Release(cached.Entry);
            foreach (var cached in Meshes.Values)
                RawSpriteResources.Release(cached.Entry);
            Materials.Clear();
            Meshes.Clear();
            Skins.Clear();
            Version++;
        }
    }
}
