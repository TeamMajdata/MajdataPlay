using UnityEngine;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>
    /// Scales Android's display buffer independently of the scene render scale.
    /// Camera projection, Canvas layout and input coordinates remain owned by Unity.
    /// </summary>
    public static class MajAndroidDisplayResolution
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Retains the selected reduction while restoring the original buffer.</summary>
        private static int _reducedPercent = 100;
        /// <summary>Records whether the original display-buffer size has been captured.</summary>
        private static bool _hasBaseline;
        /// <summary>Tracks whether display reduction is currently requested.</summary>
        private static bool _reduced;
        /// <summary>Forces a request after a setting or foreground-state change.</summary>
        private static bool _refreshRequested;
        /// <summary>Stores the buffer width before the first scaling request.</summary>
        private static int _baselineWidth;
        /// <summary>Stores the buffer height before the first scaling request.</summary>
        private static int _baselineHeight;
        /// <summary>Caches the screen width used to detect orientation or surface changes.</summary>
        private static int _screenWidth;
        /// <summary>Caches the screen height used to detect orientation or surface changes.</summary>
        private static int _screenHeight;
        /// <summary>Stores the last requested buffer width to avoid repeated API calls.</summary>
        private static int _requestedWidth;
        /// <summary>Stores the last requested buffer height to avoid repeated API calls.</summary>
        private static int _requestedHeight;
#endif

        /// <summary>Gets scaling status from actual display dimensions rather than assuming a request succeeded.</summary>
        public static MajSystemScalingStatus Status
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var display = Display.main;
                if (!_hasBaseline)
                {
                    return _reduced ? MajSystemScalingStatus.Unconfirmed : MajSystemScalingStatus.Disabled;
                }
                var matches = display != null && display.renderingWidth == _requestedWidth
                    && display.renderingHeight == _requestedHeight;
                if (_reduced)
                {
                    return matches && !_refreshRequested
                        ? MajSystemScalingStatus.Active : MajSystemScalingStatus.Unconfirmed;
                }
                return matches ? MajSystemScalingStatus.Disabled : MajSystemScalingStatus.Restoring;
#else
                return MajSystemScalingStatus.Unsupported;
#endif
            }
        }

        /// <summary>Requests a reduced Android display buffer, or restores it at 100 percent.</summary>
        /// <param name="percent">The requested native-display percentage, clamped to 10 through 100.</param>
        public static void Configure(int percent)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            percent = Mathf.Clamp(percent, 10, 100);
            if (percent == 100)
            {
                Restore();
                return;
            }
            if (_reduced && _reducedPercent == percent)
            {
                return;
            }
            _reducedPercent = percent;
            _reduced = true;
            _refreshRequested = true;
            // Startup may precede surface creation; Tick retries the retained request.
            if (!TryCaptureBaseline())
            {
                return;
            }
            ApplyRequestedSize();
#endif
        }

        /// <summary>Restores the buffer dimensions captured before display scaling began.</summary>
        public static void Restore()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_reduced)
            {
                return;
            }
            _reduced = false;
            if (!_hasBaseline)
            {
                _refreshRequested = false;
                return;
            }
            _refreshRequested = true;
            ApplyRequestedSize();
#endif
        }

        /// <summary>Schedules reapplication after foregrounding can recreate the Android display surface.</summary>
        public static void RequestRefresh()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_hasBaseline && _reduced)
            {
                _refreshRequested = true;
            }
#endif
        }

        /// <summary>Applies pending display-size requests after startup, orientation changes or foreground refresh.</summary>
        public static void Tick()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_hasBaseline && (!_reduced || !TryCaptureBaseline()))
            {
                return;
            }
            if (_reduced || _refreshRequested)
            {
                ApplyRequestedSize();
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>Captures the original buffer once, including across asynchronous restore requests.</summary>
        /// <returns>True when valid baseline dimensions are available.</returns>
        private static bool TryCaptureBaseline()
        {
            if (_hasBaseline)
            {
                return true;
            }
            var display = Display.main;
            if (display == null || display.renderingWidth <= 0 || display.renderingHeight <= 0)
            {
                return false;
            }
            // Keep the original buffer, not an in-flight reduced or restored surface.
            _baselineWidth = display.renderingWidth;
            _baselineHeight = display.renderingHeight;
            _hasBaseline = true;
            return true;
        }

        /// <summary>Applies a changed size once without compounding an earlier scaling request.</summary>
        private static void ApplyRequestedSize()
        {
            var screenWidth = Screen.width;
            var screenHeight = Screen.height;
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                return;
            }
            var refresh = _refreshRequested;
            if (!refresh && _screenWidth == screenWidth && _screenHeight == screenHeight)
            {
                return;
            }
            var display = Display.main;
            if (display == null)
            {
                return;
            }
            _refreshRequested = false;
            _screenWidth = screenWidth;
            _screenHeight = screenHeight;
            // Reduce from native dimensions, but restore the preexisting buffer.
            // Reusing one baseline would compound Unity Fixed DPI or system scaling.
            var nativeWidth = display.systemWidth;
            var nativeHeight = display.systemHeight;
            var hasNativeSize = nativeWidth > 0 && nativeHeight > 0;
            var width = _reduced && hasNativeSize ? nativeWidth : _baselineWidth;
            var height = _reduced && hasNativeSize ? nativeHeight : _baselineHeight;
            if ((width > height) != (screenWidth > screenHeight))
            {
                var previousWidth = width;
                width = height;
                height = previousWidth;
            }
            var percent = _reduced ? _reducedPercent : 100;
            width = Mathf.Max(1, width * percent / 100);
            height = Mathf.Max(1, height * percent / 100);
            var sameRequest = width == _requestedWidth && height == _requestedHeight;
            if (sameRequest && (!refresh || (display.renderingWidth == width && display.renderingHeight == height)))
            {
                return;
            }
            _requestedWidth = width;
            _requestedHeight = height;
            try
            {
                display.SetRenderingResolution(width, height);
            }
            catch (System.Exception exception) when (exception is UnityException
                || exception is System.ArgumentException || exception is System.NotSupportedException)
            {
                Debug.LogWarning($"MRP: display scaling request {percent}% was rejected; retaining actual display dimensions. {exception.Message}");
            }
        }
#endif

        /// <summary>Gets the actual display-buffer size for rendering without changing layout or touch coordinates.</summary>
        /// <param name="width">Receives the positive rendering-buffer width.</param>
        /// <param name="height">Receives the positive rendering-buffer height.</param>
        internal static void GetRenderingSize(out int width, out int height)
        {
            width = Mathf.Max(1, Screen.width);
            height = Mathf.Max(1, Screen.height);
#if UNITY_ANDROID && !UNITY_EDITOR
            var display = Display.main;
            if (display != null && display.renderingWidth > 0 && display.renderingHeight > 0)
            {
                width = display.renderingWidth;
                height = display.renderingHeight;
            }
#endif
        }
    }
}
