using MajdataPlay.Diagnostics;
using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace MajdataPlay.IO
{
    internal static unsafe partial class InputManager
    {
        public const int TOUCH_ANGLE_SMAPLE_COUNT = ScreenTouchMapper.TOUCH_ANGLE_SMAPLE_COUNT;
        public const float FINGER_RADIUS_SEGMENT_LENGTH = ScreenTouchMapper.FINGER_RADIUS_SEGMENT_LENGTH;

        public static float? Override_TouchSimulationRadius
        {
            get => ScreenTouchMapper.Override_TouchSimulationRadius;
            set => ScreenTouchMapper.Override_TouchSimulationRadius = value;
        }
        public static float? Override_TouchAAreaExtraRadius
        {
            get => ScreenTouchMapper.Override_TouchAAreaExtraRadius;
            set => ScreenTouchMapper.Override_TouchAAreaExtraRadius = value;
        }
        public static float? Override_TouchBAreaExtraRadius
        {
            get => ScreenTouchMapper.Override_TouchBAreaExtraRadius;
            set => ScreenTouchMapper.Override_TouchBAreaExtraRadius = value;
        }
        public static float? Override_TouchCAreaExtraRadius
        {
            get => ScreenTouchMapper.Override_TouchCAreaExtraRadius;
            set => ScreenTouchMapper.Override_TouchCAreaExtraRadius = value;
        }
        public static float? Override_TouchDAreaExtraRadius
        {
            get => ScreenTouchMapper.Override_TouchDAreaExtraRadius;
            set => ScreenTouchMapper.Override_TouchDAreaExtraRadius = value;
        }
        public static float? Override_TouchEAreaExtraRadius
        {
            get => ScreenTouchMapper.Override_TouchEAreaExtraRadius;
            set => ScreenTouchMapper.Override_TouchEAreaExtraRadius = value;
        }
        public static bool UseOuterTouchAsSensor
        {
            get => ScreenTouchMapper.UseOuterTouchAsSensor;
            set => ScreenTouchMapper.UseOuterTouchAsSensor = value;
        }
        public static bool UseGameplayTouchEnhancementFeatures
        {
            get => ScreenTouchMapper.UseGameplayTouchEnhancementFeatures;
            set => ScreenTouchMapper.UseGameplayTouchEnhancementFeatures = value;
        }
        public static float TouchButtonRingEdge
        {
            get => ScreenTouchMapper.TouchButtonRingEdge;
            set => ScreenTouchMapper.TouchButtonRingEdge = value;
        }
        public static float FingerRadius => ScreenTouchMapper.FingerRadius;
        public static ReadOnlySpan<Vector4> UnitCircle => ScreenTouchMapper.UnitCircle;
        public static ReadOnlySpan<ulong> TouchPanelPositionSamples => ScreenTouchMapper.TouchPanelPositionSamples;
        public static Vector4 SubScreenEdge
        {
            get => ScreenTouchMapper.SubScreenEdge;
            set => ScreenTouchMapper.SubScreenEdge = value;
        }

        static void UpdatePointerInput()
        {
            using (UnityProfiler.Create("InputManager.UpdatePointerInput"))
            {
                var screen = GameDeviceManager.ScreenTouchPanel;
                var mouse = GameDeviceManager.MouseTouchPanel;
                ITouchPanelDevice screenPanel = screen;
                ITouchPanelDevice mousePanel = mouse;
                IButtonRingDevice screenButtons = screen;
                IButtonRingDevice mouseButtons = mouse;
                Span<bool> screenStates = stackalloc bool[35];
                Span<bool> mouseStates = stackalloc bool[35];
                Span<bool> hadOn = stackalloc bool[35];
                Span<bool> hadOff = stackalloc bool[35];

                screenPanel.ReadTouchPanel(screenStates, hadOn, hadOff);
                for (var i = 0; i < 34; i++) screenStates[i] |= hadOn[i];
                mousePanel.ReadTouchPanel(mouseStates, hadOn, hadOff);
                var now = MajTimeline.UnscaledTime;
                for (var i = 0; i < 34; i++)
                {
                    var state = screenStates[i] || mouseStates[i] || hadOn[i];
                    _touchPanelInputBuffer.Enqueue(new InputDeviceReport
                    {
                        Index = i,
                        State = state ? SwitchStatus.On : SwitchStatus.Off,
                        Timestamp = now,
                    });
                }

                screenButtons.ReadButtons(screenStates, hadOn, hadOff);
                for (var i = 0; i < 12; i++) screenStates[i] |= hadOn[i];
                mouseButtons.ReadButtons(mouseStates, hadOn, hadOff);
                for (var i = 0; i < 12; i++)
                {
                    var state = screenStates[i] || mouseStates[i] || hadOn[i];
                    _buttonRingInputBuffer.Enqueue(new InputDeviceReport
                    {
                        Index = i,
                        State = state ? SwitchStatus.On : SwitchStatus.Off,
                        Timestamp = now,
                    });
                }

#if UNITY_ANDROID || UNITY_IOS
                for (var i = 0; i < 8; i++)
                    _btnClickedCountInThisFrame[i] += screen.ButtonClickedCount[i] + mouse.ButtonClickedCount[i];
                for (var i = 0; i < 34; i++)
                {
                    var clicked = screen.SensorClickedCount[i] + mouse.SensorClickedCount[i];
                    if (i == 16)
                    {
                        clicked = Mathf.Max(clicked, screen.SensorClickedCount[17] + mouse.SensorClickedCount[17]);
                        _sensorClickedCountInThisFrame[16] += clicked;
                        i++;
                    }
                    else
                    {
                        _sensorClickedCountInThisFrame[i < 16 ? i : i - 1] += clicked;
                    }
                }
#endif
            }
        }
    }
}
