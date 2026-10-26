using System;
using MajdataPlay.Utils;
using UnityEngine.InputSystem;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace MajdataPlay.IO
{
    /// <summary>Maps the mouse to sensors and outer buttons when touchscreen input is absent.</summary>
    internal sealed class MouseTouchPanelDevice : ITouchPanelDevice, IButtonRingDevice
    {
        readonly PointerInputState _state = new();

        public bool IsConnected { get; private set; }
        public ReadOnlySpan<int> ButtonClickedCount => _state.ButtonClickedCount;
        public ReadOnlySpan<int> SensorClickedCount => _state.SensorClickedCount;

        public void OnPreUpdate()
        {
            _state.BeginFrame();
            try
            {
#if UNITY_STANDALONE || UNITY_EDITOR
                var mouse = Mouse.current;
                IsConnected = mouse != null;
                var camera = SceneSwitcher.MainCamera;
                if (mouse == null || Touch.activeTouches.Count > 0 ||
                    !ScreenTouchMapper.IsInitialized || camera == null || !mouse.leftButton.isPressed)
                    return;
                _state.AddPointer(1, mouse.position.value, 0, false, camera);
#else
                IsConnected = false;
#endif
            }
            finally
            {
                // Also publishes released states on disconnect or touch-priority suppression.
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
