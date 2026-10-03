using System;
using MajdataPlay.Diagnostics;
using MajdataPlay.Utils;
using UnityEngine.InputSystem;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MajdataPlay.IO
{
    /// <summary>Samples touchscreen contacts on the Unity main thread.</summary>
    internal sealed class ScreenTouchPanelDevice : ITouchPanelDevice, IButtonRingDevice
    {
        readonly PointerInputState _state = new();
        float _maxTouchRadius = -1f;

        public bool IsConnected { get; private set; }
        public ReadOnlySpan<int> ButtonClickedCount => _state.ButtonClickedCount;
        public ReadOnlySpan<int> SensorClickedCount => _state.SensorClickedCount;

        public void OnPreUpdate()
        {
            _state.BeginFrame();
            try
            {
                IsConnected = Touchscreen.current != null;
                var camera = SceneSwitcher.MainCamera;
                if (!ScreenTouchMapper.IsInitialized)
                {
                    return;
                }
#if UNITY_IOS
                const float platformRadiusAdjust = 78f * 4f;
#else
                const float platformRadiusAdjust = 1f;
#endif
                var touches = Touch.activeTouches;
                for (var i = 0; i < touches.Count; i++)
                {
                    var touch = touches[i];
                    if (!touch.valid) continue;
                    var radius = touch.radius.magnitude;
                    _state.AddPointer(touch.touchId, touch.screenPosition,
                        radius / platformRadiusAdjust, touch.ended, camera);
                    if (radius > _maxTouchRadius)
                    {
                        MajDebug.LogInfo($"Touch radius: {radius}");
                        _maxTouchRadius = radius;
                    }
                }
            }
            finally
            {
                _state.EndFrame();
            }
        }

        public void ReadButtons(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _state.ReadButtons(states, hadOn, hadOff);
        public void ReadTouchPanel(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff) =>
            _state.ReadTouchPanel(states, hadOn, hadOff);
        public bool IsButtonCurrentlyOn(int index) => _state.IsButtonCurrentlyOn(index);
        public bool IsSensorCurrentlyOn(int index) => _state.IsSensorCurrentlyOn(index);
    }
}
