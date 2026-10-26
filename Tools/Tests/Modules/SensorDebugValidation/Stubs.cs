using System;
using System.Collections.Generic;
using UnityEngine;
#nullable enable

namespace System.Runtime.CompilerServices
{
    /// <summary>Provides the init-only marker normally supplied by the project's PolySharp generator.</summary>
    internal static class IsExternalInit
    {
    }
}

namespace MajdataPlay
{
    /// <summary>Substitutes only the component and singleton lifecycle outside the main game.</summary>
    public class MajComponent : MonoBehaviour
    {
        /// <summary>Gets the real Unity transform used by the production renderer.</summary>
        public Transform Transform => transform;
        protected virtual void Awake()
        {
        }
    }

    /// <summary>Supplies the production lifetime marker without registering global services.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DontDestroyOnLoadAttribute : Attribute
    {
    }

    /// <summary>Accepts singleton registration without retaining test scene objects.</summary>
    /// <typeparam name="T">The component type.</typeparam>
    public static class Majdata<T>
    {
        /// <summary>Accepts the production registration call.</summary>
        /// <param name="value">The component under test.</param>
        public static void SetAsSingleton(T value)
        {
        }
    }

    /// <summary>Exposes controllable settings to the production renderer.</summary>
    public static class MajEnv
    {
        /// <summary>Gets the current test settings.</summary>
        public static TestSettings Settings { get; } = new();
    }

    /// <summary>Groups the real production debug options for this isolated scene.</summary>
    public sealed class TestSettings
    {
        /// <summary>Gets the current sensor settings.</summary>
        public Settings.DebugOptions Debug { get; } = new();
    }
}

namespace MajdataPlay.IO
{
    /// <summary>Provides touch state independently of real device I/O.</summary>
    public static class InputManager
    {
        /// <summary>Gets the thirty-four physical bits and the reserved extra bit.</summary>
        public static bool[] TouchPanelRawData { get; } = new bool[35];

        /// <summary>Gets or sets the secondary display edges in world XY order: left, top, right, bottom.</summary>
        public static Vector4 SubScreenEdge { get; set; }
    }
}

namespace MajdataPlay.Collections
{
    /// <summary>Adapts the real Unity hierarchy to the production enumeration calls.</summary>
    public static class HierarchyExtensions
    {
        /// <summary>Enumerates immediate children.</summary>
        /// <param name="transform">The hierarchy root.</param>
        /// <returns>The immediate child transforms.</returns>
        public static IEnumerable<Transform> ToEnumerable(this Transform transform)
        {
            foreach (Transform child in transform)
            {
                yield return child;
            }
        }

        /// <summary>Attaches the stable hierarchy index to each child.</summary>
        /// <param name="children">The hierarchy sequence.</param>
        /// <returns>Indexed children.</returns>
        public static IEnumerable<(int, Transform)> WithIndex(this IEnumerable<Transform> children)
        {
            var index = 0;
            foreach (var child in children)
            {
                yield return (index++, child);
            }
        }
    }
}

namespace MajdataPlay.Utils
{
    /// <summary>Reserves the production using namespace without replacing Unity objects.</summary>
    internal static class NamespaceMarker
    {
    }
}

namespace MajdataPlay.Settings.OptionEnumerators
{
    /// <summary>Supplies the type referenced by an unrelated timing-offset attribute.</summary>
    internal sealed class GameOffsetEnumerator
    {
    }
}
