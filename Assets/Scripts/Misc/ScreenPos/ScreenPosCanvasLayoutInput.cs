#nullable enable
using System;
using UnityEngine;

namespace MajdataPlay
{
    /// <summary>
    /// Captures every value that affects <see cref="ScreenPosCanvasLayout"/>, so the layout is only recalculated when one of them changes.
    /// </summary>
    public readonly struct ScreenPosCanvasLayoutInput : IEquatable<ScreenPosCanvasLayoutInput>
    {
        /// <summary>
        /// Gets the screen width in pixels.
        /// </summary>
        public int ScreenWidth { get; }

        /// <summary>
        /// Gets the screen height in pixels.
        /// </summary>
        public int ScreenHeight { get; }

        /// <summary>
        /// Gets the unmodified reference resolution of the canvas scaler.
        /// </summary>
        public Vector2 BaseReferenceResolution { get; }

        /// <summary>
        /// Gets a value indicating whether the user-defined display transform is enabled.
        /// </summary>
        public bool IsTransformEnabled { get; }

        /// <summary>
        /// Gets the vertical offset of the main display, in setting units.
        /// </summary>
        public float MainScreenOffset { get; }

        /// <summary>
        /// Gets the scale of the main display.
        /// </summary>
        public float MainScreenScale { get; }

        /// <summary>
        /// Gets the vertical offset of the sub display, in setting units.
        /// </summary>
        public float SubDisplayOffset { get; }

        /// <summary>
        /// Gets the scale of the sub display.
        /// </summary>
        public float SubDisplayScale { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScreenPosCanvasLayoutInput"/> struct.
        /// </summary>
        /// <param name="screenWidth">The screen width in pixels.</param>
        /// <param name="screenHeight">The screen height in pixels.</param>
        /// <param name="baseReferenceResolution">The unmodified reference resolution of the canvas scaler.</param>
        /// <param name="isTransformEnabled">Whether the user-defined display transform is enabled.</param>
        /// <param name="mainScreenOffset">The vertical offset of the main display, in setting units.</param>
        /// <param name="mainScreenScale">The scale of the main display.</param>
        /// <param name="subDisplayOffset">The vertical offset of the sub display, in setting units.</param>
        /// <param name="subDisplayScale">The scale of the sub display.</param>
        public ScreenPosCanvasLayoutInput(int screenWidth,
                                          int screenHeight,
                                          Vector2 baseReferenceResolution,
                                          bool isTransformEnabled,
                                          float mainScreenOffset,
                                          float mainScreenScale,
                                          float subDisplayOffset,
                                          float subDisplayScale)
        {
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            BaseReferenceResolution = baseReferenceResolution;
            IsTransformEnabled = isTransformEnabled;
            MainScreenOffset = mainScreenOffset;
            MainScreenScale = mainScreenScale;
            SubDisplayOffset = subDisplayOffset;
            SubDisplayScale = subDisplayScale;
        }

        /// <summary>
        /// Determines whether this input is identical to another input.
        /// </summary>
        /// <param name="other">The input to compare with.</param>
        /// <returns><see langword="true"/> if every value is exactly equal; otherwise, <see langword="false"/>.</returns>
        public bool Equals(ScreenPosCanvasLayoutInput other)
        {
            return ScreenWidth == other.ScreenWidth &&
                   ScreenHeight == other.ScreenHeight &&
                   BaseReferenceResolution.x.Equals(other.BaseReferenceResolution.x) &&
                   BaseReferenceResolution.y.Equals(other.BaseReferenceResolution.y) &&
                   IsTransformEnabled == other.IsTransformEnabled &&
                   MainScreenOffset.Equals(other.MainScreenOffset) &&
                   MainScreenScale.Equals(other.MainScreenScale) &&
                   SubDisplayOffset.Equals(other.SubDisplayOffset) &&
                   SubDisplayScale.Equals(other.SubDisplayScale);
        }

        /// <summary>
        /// Determines whether this input is identical to another object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="ScreenPosCanvasLayoutInput"/>; otherwise, <see langword="false"/>.</returns>
        public override bool Equals(object? obj)
        {
            return obj is ScreenPosCanvasLayoutInput other && Equals(other);
        }

        /// <summary>
        /// Gets a hash code for this input.
        /// </summary>
        /// <returns>A hash code combining every value of this input.</returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ScreenWidth);
            hash.Add(ScreenHeight);
            hash.Add(BaseReferenceResolution.x);
            hash.Add(BaseReferenceResolution.y);
            hash.Add(IsTransformEnabled);
            hash.Add(MainScreenOffset);
            hash.Add(MainScreenScale);
            hash.Add(SubDisplayOffset);
            hash.Add(SubDisplayScale);
            return hash.ToHashCode();
        }
    }
}
