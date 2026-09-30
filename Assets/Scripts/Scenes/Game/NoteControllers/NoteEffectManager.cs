using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public class NoteEffectManager : MonoBehaviour
    {
        NoteEffectPool _effectPool;
        private FireworkDisplayer _fireworkDisplayer;

        public Color buttonGoodColor = Color.green;
        public Color buttonGreatColor = Color.red;
        public Color buttonPerfectColor = Color.yellow;

        Dictionary<SensorArea, TimeSpan> _lastTriggerTimes = new();

        readonly GameSetting _setting = MajEnv.Settings;
        Range<int> _touchFeedbackLevel = new Range<int>(0, 0, ContainsType.Open);

        readonly static int FIREWORK_ANIM_HASH = Animator.StringToHash("Fire");

        void Awake()
        {
            Majdata<NoteEffectManager>.Instance = this;

            if (_setting.Display.TouchFeedback != TouchFeedbackLevel.Disable)
            {
                InputManager.BindAnyArea(OnAnyAreaClick);
                switch (_setting.Display.TouchFeedback)
                {
                    case TouchFeedbackLevel.All:
                        _touchFeedbackLevel = new Range<int>(0, 32, ContainsType.Closed);
                        break;
                    case TouchFeedbackLevel.Outer_Only:
                        _touchFeedbackLevel = new Range<int>(0, 7, ContainsType.Closed);
                        break;
                    case TouchFeedbackLevel.Inner_Only:
                        _touchFeedbackLevel = new Range<int>(8, 32, ContainsType.Closed);
                        break;
                }
            }


            for (var i = 0; i < 33; i++)
            {
                var pos = (SensorArea)i;
                _lastTriggerTimes[pos] = TimeSpan.Zero;
            }
        }
        void OnDestroy()
        {
            Majdata<NoteEffectManager>.Free();
            InputManager.UnbindAnyArea(OnAnyAreaClick);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void OnAnyAreaClick(object? sender, InputEventArgs args)
        {
            var pos = args.IsButton?(SensorArea)args.BZone: args.SArea;
            if(args.IsButton && args.BZone >ButtonZone.A8)
            {
                return;
            }
            if (!args.IsDown)
            {
                return;
            }
            else if (pos > SensorArea.E8)
            {
                return;
            }
            else if (pos.GetGroup() == SensorGroup.D)
            {
                return;
            }
            else if (!_touchFeedbackLevel.InRange((int)pos))
            {
                return;
            }

            var now = MajTimeline.Time;
            var lastTriggerTime = _lastTriggerTimes[pos];

            if ((now - lastTriggerTime).TotalMilliseconds < 416.6675f)
            {
                return;
            }

            _effectPool.PlayFeedbackEffect(pos);
        }
        void Start()
        {
            _fireworkDisplayer = Majdata<FireworkDisplayer>.Instance!;
            _effectPool = Majdata<NoteEffectPool>.Instance!;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayFireworkEffect(in Vector3 position)
        {
            _fireworkDisplayer.Play(position);
        }
        /// <summary>
        /// Tap, Hold, Star
        /// </summary>
        /// <param name="position"></param>
        /// <param name="isBreak"></param>
        /// <param name="judge"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTapJudgeResult(int position, in NoteJudgeResult judgeResult)
        {
            var pos = (SensorArea)(position - 1);
            CabinetLed.SetButtonLightWithTimeout(GetColor(judgeResult.Grade), position - 1);

            if (!judgeResult.IsMissOrTooFast)
            {
                _lastTriggerTimes[pos] = MajTimeline.Time;
                _effectPool.ResetFeedbackEffect(pos);
            }
            _effectPool.PlayTapJudgeResult(judgeResult, position);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayHoldEffect(int keyIndex, in JudgeGrade judgeType)
        {
            CabinetLed.SetButtonLight(GetColor(judgeType), keyIndex - 1);
            _effectPool.PlayHoldEffect(judgeType, keyIndex);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayHoldEffect(SensorArea sensorPos, in JudgeGrade judgeType)
        {
            _effectPool.PlayHoldEffect(judgeType, sensorPos);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetHoldEffect(int keyIndex)
        {
            CabinetLed.SetButtonLight(Color.white, keyIndex - 1);
            _effectPool.ResetHoldEffect(keyIndex);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetHoldEffect(SensorArea sensorPos)
        {
            _effectPool.ResetHoldEffect(sensorPos);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTouchJudgeResult(SensorArea sensorPos, in NoteJudgeResult judgeResult)
        {
            if (!judgeResult.IsMissOrTooFast)
            {
                _lastTriggerTimes[sensorPos] = MajTimeline.Time;
                _effectPool.ResetFeedbackEffect(sensorPos);
            }
            _effectPool.PlayTouchJudgeResult(judgeResult, sensorPos);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTouchHoldJudgeResult(SensorArea sensorPos, in NoteJudgeResult judgeResult)
        {
            _effectPool.PlayTouchHoldJudgeResult(judgeResult, sensorPos);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckJudgeDisplaySetting(in JudgeDisplayOption setting, in NoteJudgeResult judgeResult)
        {
            var result = judgeResult.Grade;
            var resultValue = (int)result;
            var absValue = Math.Abs(7 - resultValue);

            return setting switch
            {
                JudgeDisplayOption.All => true,
                JudgeDisplayOption.BelowCP => resultValue != 7,
                JudgeDisplayOption.BelowP => absValue > 2,
                JudgeDisplayOption.BelowGR => absValue > 5,
                JudgeDisplayOption.MissOnly => judgeResult.IsMissOrTooFast,
                _ => false
            };
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetEffect(int position)
        {
            _effectPool.Reset(position);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color GetColor(JudgeGrade judgeType)
        {
            switch (judgeType)
            {
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                    return buttonGoodColor;
                case JudgeGrade.LateGreat:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.FastGreat3rd:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat:
                    return buttonGreatColor;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.Perfect:
                    return buttonPerfectColor;
                default:
                    return Color.white;
            }
        }
    }
}