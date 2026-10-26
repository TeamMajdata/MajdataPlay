#nullable enable
using MajdataPlay.Diagnostics;
using MajdataPlay.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace MajdataPlay
{
    /// <summary>
    /// Calculates the shared <see cref="ScreenPosCanvasLayout"/> from the screen size and the display settings.
    /// </summary>
    /// <remarks>
    /// This component lives on the persistent <c>GlobalCanvas</c>. Every canvas, including <c>GlobalCanvas</c> itself,
    /// applies the result through <see cref="ScreenPosCanvasLayoutApplier"/>.
    /// </remarks>
    [DefaultExecutionOrder(140)]
    public sealed class ScreenPosCanvasLayoutProvider : MonoBehaviour
    {
        /// <summary>
        /// The reference resolution used when <c>GlobalCanvas</c> has no <see cref="CanvasScaler"/>.
        /// </summary>
        private static readonly Vector2 s_defaultReferenceResolution = new Vector2(1080f, 1920f);

        /// <summary>
        /// Gets a value indicating whether <see cref="Layout"/> has been calculated at least once.
        /// </summary>
        public bool HasLayout { get; private set; }

        /// <summary>
        /// Gets a number that changes every time <see cref="Layout"/> is recalculated.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// Gets the most recently calculated layout. Only valid while <see cref="HasLayout"/> is <see langword="true"/>.
        /// </summary>
        public ScreenPosCanvasLayout Layout { get; private set; }

        /// <summary>
        /// The reference resolution of <c>GlobalCanvas</c> before any display scale is applied.
        /// </summary>
        private Vector2 _baseReferenceResolution;

        /// <summary>
        /// The input used for the current <see cref="Layout"/>.
        /// </summary>
        private ScreenPosCanvasLayoutInput _lastInput;

        private void Awake()
        {
            Majdata<ScreenPosCanvasLayoutProvider>.SetAsSingleton(this);
            // Read before any applier changes the scaler; appliers only write in LateUpdate.
            var canvasScaler = GetComponent<CanvasScaler>();
            _baseReferenceResolution = canvasScaler != null ? canvasScaler.referenceResolution : s_defaultReferenceResolution;
        }

        private void LateUpdate()
        {
            var displayOptions = MajEnv.Settings?.Display;
            if (displayOptions is null)
            {
                return;
            }
            var screenWidth = Screen.width;
            var screenHeight = Screen.height;
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }

            var input = CreateInput(screenWidth, screenHeight, displayOptions);
            if (HasLayout && input.Equals(_lastInput))
            {
                return;
            }

            var layout = ScreenPosCanvasLayout.Calculate(input);
            _lastInput = input;
            Layout = layout;
            HasLayout = true;
            Version++;
            displayOptions.MainScreenCachedScreenCenterY = layout.ScreenCenterY;
            MajDebug.LogDebug(nameof(ScreenPosCanvasLayoutProvider), $"Layout v{Version}: screen {screenWidth}x{screenHeight}, reference resolution {layout.ReferenceResolution}");
        }

        /// <summary>
        /// Creates the layout input for the current frame.
        /// </summary>
        /// <param name="screenWidth">The screen width in pixels.</param>
        /// <param name="screenHeight">The screen height in pixels.</param>
        /// <param name="displayOptions">The display settings to read.</param>
        /// <returns>The layout input.</returns>
        private ScreenPosCanvasLayoutInput CreateInput(int screenWidth, int screenHeight, DisplayOptions displayOptions)
        {
            return new ScreenPosCanvasLayoutInput(
                screenWidth,
                screenHeight,
                _baseReferenceResolution,
                displayOptions.MainScreenTransform,
                displayOptions.MainScreenOffset,
                displayOptions.MainScreenScale,
                displayOptions.SubDisplayOffset,
                displayOptions.SubDisplayScale);
        }
    }
}
