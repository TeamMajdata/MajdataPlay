#nullable enable
using System;

namespace UnityEngine
{
    /// <summary>
    /// Minimal stand-in for <c>UnityEngine.Vector2</c> covering the members used by the layout code.
    /// </summary>
    public struct Vector2
    {
        /// <summary>
        /// The x component.
        /// </summary>
        public float x;

        /// <summary>
        /// The y component.
        /// </summary>
        public float y;

        /// <summary>
        /// Initializes a new vector.
        /// </summary>
        /// <param name="x">The x component.</param>
        /// <param name="y">The y component.</param>
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        /// <summary>
        /// Divides both components by a scalar.
        /// </summary>
        /// <param name="value">The vector to divide.</param>
        /// <param name="divisor">The scalar divisor.</param>
        /// <returns>The divided vector.</returns>
        public static Vector2 operator /(Vector2 value, float divisor)
        {
            return new Vector2(value.x / divisor, value.y / divisor);
        }

        /// <summary>
        /// Formats the vector for diagnostics.
        /// </summary>
        /// <returns>The vector as text.</returns>
        public override string ToString()
        {
            return $"({x}, {y})";
        }
    }

    /// <summary>
    /// Minimal stand-in for <c>UnityEngine.Rect</c> covering the members used by the layout code.
    /// </summary>
    public struct Rect
    {
        /// <summary>
        /// Initializes a new rectangle.
        /// </summary>
        /// <param name="x">The left edge.</param>
        /// <param name="y">The bottom edge.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        public Rect(float x, float y, float width, float height)
        {
            xMin = x;
            yMin = y;
            this.width = width;
            this.height = height;
        }

        /// <summary>
        /// Gets the left edge.
        /// </summary>
        public float xMin { get; }

        /// <summary>
        /// Gets the bottom edge.
        /// </summary>
        public float yMin { get; }

        /// <summary>
        /// Gets the width.
        /// </summary>
        public float width { get; }

        /// <summary>
        /// Gets the height.
        /// </summary>
        public float height { get; }

        /// <summary>
        /// Gets the right edge.
        /// </summary>
        public float xMax => xMin + width;

        /// <summary>
        /// Gets the top edge.
        /// </summary>
        public float yMax => yMin + height;

        /// <summary>
        /// Creates a rectangle from its edges.
        /// </summary>
        /// <param name="xmin">The left edge.</param>
        /// <param name="ymin">The bottom edge.</param>
        /// <param name="xmax">The right edge.</param>
        /// <param name="ymax">The top edge.</param>
        /// <returns>The rectangle.</returns>
        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax)
        {
            return new Rect(xmin, ymin, xmax - xmin, ymax - ymin);
        }

        /// <summary>
        /// Formats the rectangle for diagnostics.
        /// </summary>
        /// <returns>The rectangle as text.</returns>
        public override string ToString()
        {
            return $"(x:{xMin}, y:{yMin}, width:{width}, height:{height})";
        }
    }

    /// <summary>
    /// Minimal stand-in for <c>UnityEngine.Mathf</c> covering the members used by the layout code.
    /// </summary>
    public static class Mathf
    {
        /// <summary>
        /// Returns the smaller of two values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns>The smaller value.</returns>
        public static float Min(float a, float b)
        {
            return MathF.Min(a, b);
        }

        /// <summary>
        /// Returns the larger of two values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns>The larger value.</returns>
        public static float Max(float a, float b)
        {
            return MathF.Max(a, b);
        }

        /// <summary>
        /// Returns the absolute value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The absolute value.</returns>
        public static float Abs(float value)
        {
            return MathF.Abs(value);
        }

        /// <summary>
        /// Linearly interpolates between two values with <paramref name="t"/> clamped to [0, 1], like Unity.
        /// </summary>
        /// <param name="a">The start value.</param>
        /// <param name="b">The end value.</param>
        /// <param name="t">The interpolation factor.</param>
        /// <returns>The interpolated value.</returns>
        public static float Lerp(float a, float b, float t)
        {
            var clamped = MathF.Max(0f, MathF.Min(1f, t));
            return a + ((b - a) * clamped);
        }
    }
}
