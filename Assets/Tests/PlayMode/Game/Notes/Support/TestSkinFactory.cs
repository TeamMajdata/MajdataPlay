using System;
using System.Reflection;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Tests;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// Builds a skin whose sprites are all distinct and named after the skin member that holds them, so tests can tell
    /// which sprite a note picked by comparing <see cref="UnityEngine.Object.name"/> with the member name.
    /// </summary>
    internal static class TestSkinFactory
    {
        /// <summary>The size in pixels of the shared texture used by every generated sprite.</summary>
        private const int TextureSize = 4;

        /// <summary>Creates a skin with a distinct, named sprite in every sprite member and array element.</summary>
        /// <param name="objects">The tracker that destroys the generated texture and sprites.</param>
        /// <returns>The generated skin.</returns>
        public static CustomSkin Create(TestObjects objects)
        {
            var texture = objects.Track(new Texture2D(TextureSize, TextureSize));
            var skin = (CustomSkin)Activator.CreateInstance(typeof(CustomSkin),
                                                            BindingFlags.Instance | BindingFlags.NonPublic,
                                                            null,
                                                            new object[] { "TestSkin" },
                                                            null);
            foreach (var property in typeof(CustomSkin).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.PropertyType == typeof(Sprite))
                {
                    property.GetSetMethod(true)!.Invoke(skin, new object[] { CreateSprite(objects, texture, property.Name) });
                }
                else if (property.PropertyType == typeof(Sprite[]))
                {
                    var sprites = (Sprite?[])property.GetValue(skin)!;
                    for (var i = 0; i < sprites.Length; i++)
                    {
                        sprites[i] = CreateSprite(objects, texture, $"{property.Name}[{i}]");
                    }
                }
            }
            return skin;
        }

        /// <summary>Creates a small named sprite.</summary>
        /// <param name="objects">The tracker that destroys the sprite.</param>
        /// <param name="texture">The texture the sprite samples.</param>
        /// <param name="name">The sprite name.</param>
        /// <returns>The tracked sprite.</returns>
        public static Sprite CreateSprite(TestObjects objects, Texture2D texture, string name)
        {
            var sprite = Sprite.Create(texture,
                                       new Rect(0, 0, TextureSize, TextureSize),
                                       new Vector2(0.5f, 0.5f),
                                       100f,
                                       0,
                                       SpriteMeshType.FullRect);
            sprite.name = name;
            return objects.Track(sprite);
        }

        /// <summary>Creates a small named sprite with its own texture.</summary>
        /// <param name="objects">The tracker that destroys the sprite and texture.</param>
        /// <param name="name">The sprite name.</param>
        /// <returns>The tracked sprite.</returns>
        public static Sprite CreateSprite(TestObjects objects, string name)
        {
            var texture = objects.Track(new Texture2D(TextureSize, TextureSize));
            return CreateSprite(objects, texture, name);
        }
    }
}
