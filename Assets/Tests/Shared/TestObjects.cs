using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests
{
    /// <summary>
    /// Creates Unity objects for a test and destroys all of them when disposed, so tests never leak objects into the
    /// active scene.
    /// </summary>
    public sealed class TestObjects : IDisposable
    {
        /// <summary>The tracked objects in creation order.</summary>
        private readonly List<Object> _objects = new();

        /// <summary>Creates an empty, tracked game object.</summary>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">An optional parent transform.</param>
        /// <returns>The new game object.</returns>
        public GameObject Create(string name = "Test", Transform? parent = null)
        {
            var gameObject = new GameObject(name);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }
            _objects.Add(gameObject);
            return gameObject;
        }

        /// <summary>Creates a tracked game object with one component of the requested type.</summary>
        /// <typeparam name="T">The component type.</typeparam>
        /// <param name="name">The game object name; the component type name when null.</param>
        /// <param name="parent">An optional parent transform.</param>
        /// <returns>The added component.</returns>
        public T Add<T>(string? name = null, Transform? parent = null) where T : Component
        {
            return Create(name ?? typeof(T).Name, parent).AddComponent<T>();
        }

        /// <summary>Tracks an object created elsewhere so it is destroyed with the others.</summary>
        /// <typeparam name="T">The object type.</typeparam>
        /// <param name="value">The object to track.</param>
        /// <returns>The same object.</returns>
        public T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }

        /// <summary>Destroys every tracked object that still exists, newest first.</summary>
        public void Dispose()
        {
            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                var value = _objects[i];
                if (value != null)
                {
                    Object.DestroyImmediate(value);
                }
            }
            _objects.Clear();
        }
    }
}
