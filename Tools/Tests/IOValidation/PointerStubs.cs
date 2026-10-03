using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public static implicit operator Vector3(Vector2 value) => new(value.x, value.y, 0);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public sealed class Camera { }
}

namespace UnityEngine.InputSystem.Controls
{
    public sealed class Vector2Control { public Vector2 value { get; set; } }
}

namespace UnityEngine.InputSystem
{
    public sealed class Touchscreen { public static Touchscreen? current { get; set; } }

    public sealed class Mouse
    {
        public static Mouse? current { get; set; }
        public Controls.ButtonControl leftButton { get; } = new();
        public Controls.Vector2Control position { get; } = new();
    }
}

namespace UnityEngine.InputSystem.EnhancedTouch
{
    public struct Touch
    {
        public static IReadOnlyList<Touch> activeTouches { get; set; } = Array.Empty<Touch>();
        public bool valid { get; set; }
        public int touchId { get; set; }
        public Vector2 radius { get; set; }
        public Vector2 screenPosition { get; set; }
        public bool ended { get; set; }
    }
}

namespace MajdataPlay
{
    internal static class SceneSwitcher
    {
        public static UnityEngine.Camera? MainCamera { get; set; } = new();
    }
}

namespace MajdataPlay.Utils { }

namespace MajdataPlay.IO
{
    // Geometry/raycast is outside this protocol harness. Each test assigns the
    // packed result for a symbolic position and exercises production samplers,
    // frame state, per-pointer histories, and click counting without Unity.
    internal static class ScreenTouchMapper
    {
        public static bool IsInitialized { get; set; } = true;
        public static bool UseOuterTouchAsSensor { get; set; }
        public static readonly Dictionary<int, ulong> PositionReports = new();
        public static int SampleCount { get; set; }

        public static void PositionToSensorState(Span<bool> sensors, Span<bool> buttons,
            UnityEngine.Camera camera, UnityEngine.Vector3 position, float radius,
            ref ulong positionData, ref bool sensorOnly)
        {
            SampleCount++;
            PositionReports.TryGetValue((int)position.x, out var report);
            sensorOnly |= (report & (1UL << 63)) != 0;
            var useSensors = UseOuterTouchAsSensor || sensorOnly;
            for (var i = 0; i < 34; i++) sensors[i] |= (report & (1UL << (i + 12))) != 0;
            for (var i = 0; i < 12; i++)
            {
                if (useSensors && i < 8) sensors[i] |= (report & (1UL << i)) != 0;
                else buttons[i] |= (report & (1UL << i)) != 0;
            }
            positionData = report | (useSensors ? 1UL << 63 : 0);
        }
    }
}
