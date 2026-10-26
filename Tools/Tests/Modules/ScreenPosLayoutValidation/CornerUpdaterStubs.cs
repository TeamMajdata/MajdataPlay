#nullable enable
using System;

namespace UnityEngine
{
    /// <summary>Provides the single component lookup used by the corner updater.</summary>
    public class MonoBehaviour
    {
        /// <summary>The rectangle returned by a component lookup.</summary>
        public RectTransform Rectangle { get; set; } = new RectTransform();

        /// <summary>Returns the configured rectangle.</summary>
        /// <typeparam name="T">The requested component type.</typeparam>
        /// <returns>The configured component.</returns>
        public T GetComponent<T>() where T : class
        {
            return (T)(object)Rectangle;
        }
    }

    /// <summary>Records the requested Unity lifecycle execution order.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : Attribute
    {
        /// <summary>The configured execution order.</summary>
        public int Order { get; }

        /// <summary>Stores an execution order.</summary>
        /// <param name="order">The configured execution order.</param>
        public DefaultExecutionOrder(int order)
        {
            Order = order;
        }
    }

    /// <summary>Marks serialized fields without running Unity serialization.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    /// <summary>Provides the world-space coordinates used by the corner updater.</summary>
    public struct Vector3
    {
        /// <summary>The horizontal coordinate.</summary>
        public float x;
        /// <summary>The vertical coordinate.</summary>
        public float y;
    }

    /// <summary>Provides the four bounds published to the input manager.</summary>
    public struct Vector4
    {
        /// <summary>The left edge.</summary>
        public float x;
        /// <summary>The top edge.</summary>
        public float y;
        /// <summary>The right edge.</summary>
        public float z;
        /// <summary>The bottom edge.</summary>
        public float w;
    }

    /// <summary>Exposes changing screen dimensions to the production dirty check.</summary>
    public static class Screen
    {
        /// <summary>The current output width.</summary>
        public static int width;
        /// <summary>The current output height.</summary>
        public static int height;
    }

    /// <summary>Copies deterministic corners while counting native-style reads.</summary>
    public sealed class RectTransform
    {
        /// <summary>The current world-space rectangle.</summary>
        public Rect Bounds { get; set; }

        /// <summary>The number of calls to read its world-space corners.</summary>
        public int Reads { get; private set; }

        /// <summary>Copies the bottom-left, top-left, top-right and bottom-right corners.</summary>
        /// <param name="corners">The preallocated output buffer.</param>
        public void GetWorldCorners(Vector3[] corners)
        {
            Reads++;
            corners[0] = new Vector3 { x = Bounds.xMin, y = Bounds.yMin };
            corners[1] = new Vector3 { x = Bounds.xMin, y = Bounds.yMax };
            corners[2] = new Vector3 { x = Bounds.xMax, y = Bounds.yMax };
            corners[3] = new Vector3 { x = Bounds.xMax, y = Bounds.yMin };
        }
    }
}

namespace MajdataPlay.Editor
{
    /// <summary>Marks diagnostic fields without implementing an editor drawer.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReadOnlyField : Attribute
    {
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Discards debug output without touching Unity logging.</summary>
    public static class MajDebug
    {
        /// <summary>Accepts one published rectangle for diagnostics.</summary>
        /// <param name="value">The published rectangle.</param>
        public static void LogDebug(UnityEngine.Vector4 value)
        {
        }
    }
}

namespace MajdataPlay.IO
{
    /// <summary>Stores the production component's published touch bounds.</summary>
    public static class InputManager
    {
        /// <summary>The current sub-display bounds in world coordinates.</summary>
        public static UnityEngine.Vector4 SubScreenEdge { get; set; }
    }
}

namespace MajdataPlay.Settings
{
    /// <summary>Supplies only the display settings consumed by the production updater.</summary>
    public sealed class DisplayOptions
    {
        /// <summary>Whether user display transforms are enabled.</summary>
        public bool MainScreenTransform { get; set; }
        /// <summary>The main-display offset.</summary>
        public float MainScreenOffset { get; set; } = 1f;
        /// <summary>The main-display scale.</summary>
        public float MainScreenScale { get; set; } = 1f;
        /// <summary>The sub-display offset.</summary>
        public float SubDisplayOffset { get; set; }
        /// <summary>The sub-display scale.</summary>
        public float SubDisplayScale { get; set; } = 1f;
    }

    /// <summary>Exposes display options through the same settings path as production.</summary>
    public sealed class GameSetting
    {
        /// <summary>The current display settings.</summary>
        public DisplayOptions Display { get; } = new DisplayOptions();
    }
}

namespace MajdataPlay
{
    /// <summary>Provides the scene-local settings dependency.</summary>
    public static class MajEnv
    {
        /// <summary>The current settings root.</summary>
        public static Settings.GameSetting? Settings { get; set; }
    }

    /// <summary>Provides the layout singleton used by production.</summary>
    /// <typeparam name="T">The singleton type.</typeparam>
    public static class Majdata<T> where T : class
    {
        /// <summary>The current singleton instance.</summary>
        public static T? Instance { get; set; }
    }

    /// <summary>Exposes shared layout invalidation without reproducing its calculations.</summary>
    public sealed class ScreenPosCanvasLayoutProvider
    {
        /// <summary>Whether the provider has computed its first layout.</summary>
        public bool HasLayout { get; set; }
        /// <summary>The version advanced after the provider changes a layout.</summary>
        public int Version { get; set; }
    }
}
