using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Utils;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;

namespace MajdataPlay.Scenes.Game
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public sealed class TouchEffectDisplayer: MonoBehaviour
    {
        public float DistanceRatio { get; set; } = 1f;
        public SensorArea SensorPos { get; set; } = SensorArea.C;

        [SerializeField]
        TouchJudgeEffectDisplayer _judgeEffectDisplayer;

        [SerializeField]
        JudgeTextDisplayer _judgeTextDisplayer;
        [SerializeField]
        FastLateDisplayer _fastLateDisplayerA;
        [SerializeField]
        FastLateDisplayer _fastLateDisplayerB;

        bool _isEnabled = true;

        static readonly int TOUCH_PERFECT_ANIM_HASH = Animator.StringToHash("perfect");
        static readonly int TOUCH_GREAT_ANIM_HASH = Animator.StringToHash("great");
        static readonly int TOUCH_GOOD_ANIM_HASH = Animator.StringToHash("good");
        
        void Start()
        {
            var distance = NoteHelper.GetTouchAreaDistance(SensorPos.GetGroup());
            var rotation = NoteHelper.GetTouchRoation(NoteHelper.GetTouchAreaPosition(SensorPos), SensorPos);
            var textDistance = distance - (0.66f * (2 - DistanceRatio));
            var fastLateDistance = textDistance - 0.56f;
            transform.rotation = rotation;

            var effectPos = _judgeEffectDisplayer.transform.localPosition;
            effectPos.y += distance;
            _judgeEffectDisplayer.transform.localPosition = effectPos;

            var textPos = _judgeTextDisplayer.LocalPosition;
            textPos.y = textDistance;
            _judgeTextDisplayer.LocalPosition = textPos;
            _fastLateDisplayerB.LocalPosition = textPos;

            var fastLatePos = _fastLateDisplayerA.LocalPosition;
            fastLatePos.y = fastLateDistance;
            _fastLateDisplayerA.LocalPosition = fastLatePos;

            _isEnabled = MajEnv.UserSettings.Judge.InnerJudgeDistance != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _judgeEffectDisplayer.SetActive(false);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetAll()
        {
            Reset();
            _judgeTextDisplayer.Reset();
            _fastLateDisplayerA.Reset();
            _fastLateDisplayerB.Reset();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            _judgeEffectDisplayer.OnLateUpdate();
            _judgeTextDisplayer.OnLateUpdate();
            _fastLateDisplayerA.OnLateUpdate();
            _fastLateDisplayerB.OnLateUpdate();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Play(in NoteJudgeResult judgeResult)
        {
            _judgeTextDisplayer.Reset();
            _fastLateDisplayerA.Reset();
            _fastLateDisplayerB.Reset();
            PlayEffect(judgeResult);
            if (!_isEnabled)
            { 
                return; 
            }
            if (IsClassCAvailable(judgeResult))
            {
                _judgeTextDisplayer.Play(judgeResult,true);
            }
            else
            {
                if(PlayResult(judgeResult))
                {
                    PlayFastLate(judgeResult, _fastLateDisplayerA);
                }
                else
                {
                    PlayFastLate(judgeResult, _fastLateDisplayerB);
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void PlayEffect(in NoteJudgeResult judgeResult)
        {
            if (judgeResult.IsMine || judgeResult.IsMissOrTooFast)
            {
                return;
            }
            else
            {
                Reset();
            }
            _judgeEffectDisplayer.PlayEffect(judgeResult);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool PlayResult(in NoteJudgeResult judgeResult)
        {
            bool canPlay;
            if (judgeResult.IsMine && judgeResult.Grade is not (JudgeGrade.Miss or JudgeGrade.TooFast))
            {
                return false;
            }
            else if (judgeResult.IsBreak)
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.BreakJudgeType, judgeResult);
            }
            else
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.TouchJudgeType, judgeResult);
            }
            if (!canPlay)
            {
                return false;
            }
            _judgeTextDisplayer.Play(judgeResult);
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool PlayFastLate(in NoteJudgeResult judgeResult, FastLateDisplayer displayer)
        {
            bool canPlay;
            if (judgeResult.IsMine && judgeResult.Grade is not (JudgeGrade.Miss or JudgeGrade.TooFast))
            {
                return false;
            }
            else if (judgeResult.IsBreak)
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.BreakFastLateType, judgeResult);
            }
            else
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.FastLateType, judgeResult);
            }
            if (!canPlay)
            {
                return false;
            }
            displayer.Play(judgeResult);
            return true;
        }
        static bool IsClassCAvailable(in NoteJudgeResult judgeResult)
        {
            if (judgeResult.IsMissOrTooFast)
            {
                return false;
            }

            var isBreak = judgeResult.IsBreak;
            var canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.TouchJudgeType, judgeResult);
            canPlay = canPlay && NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.UserSettings.Judge.FastLateType, judgeResult);

            if (!canPlay)
            {
                return canPlay;
            }
            var skin = MajInstances.SkinManager.GetJudgeTextSkin();
            var isCritical = MajEnv.UserSettings.Judge.DisplayCriticalPerfect;
            var displayBreakScore = MajEnv.UserSettings.Judge.DisplayBreakScore;
            if (isBreak)
            {
                if (displayBreakScore)
                {
                    switch (judgeResult.Grade)
                    {
                        case JudgeGrade.LateGood:
                            return canPlay && skin.Break_1000.Late is not null;
                        case JudgeGrade.FastGood:
                            return canPlay && skin.Break_1000.Fast is not null;

                        case JudgeGrade.LateGreat3rd:
                            return canPlay && skin.Break_1250.Late is not null;
                        case JudgeGrade.FastGreat3rd:
                            return canPlay && skin.Break_1250.Fast is not null;

                        case JudgeGrade.LateGreat2nd:
                            return canPlay && skin.Break_1500.Late is not null;
                        case JudgeGrade.FastGreat2nd:
                            return canPlay && skin.Break_1500.Fast is not null;

                        case JudgeGrade.LateGreat:
                            return canPlay && skin.Break_2000.Late is not null;
                        case JudgeGrade.FastGreat:
                            return canPlay && skin.Break_2000.Fast is not null;

                        case JudgeGrade.LatePerfect3rd:
                            return canPlay && skin.Break_2500.Late is not null;
                        case JudgeGrade.FastPerfect3rd:
                            return canPlay && skin.Break_2500.Fast is not null;

                        case JudgeGrade.LatePerfect2nd:
                            return canPlay && skin.Break_2550.Late is not null;
                        case JudgeGrade.FastPerfect2nd:
                            return canPlay && skin.Break_2550.Fast is not null;

                        case JudgeGrade.Perfect:
                            return canPlay && skin.Break_2600.Late is not null;
                    }
                }
                else
                {
                    switch (judgeResult.Grade)
                    {
                        case JudgeGrade.LateGood:
                            return canPlay && skin.Good.Late is not null;
                        case JudgeGrade.FastGood:
                            return canPlay && skin.Good.Fast is not null;

                        case JudgeGrade.LateGreat:
                        case JudgeGrade.LateGreat2nd:
                        case JudgeGrade.LateGreat3rd:
                            return canPlay && skin.Great.Late is not null;
                        case JudgeGrade.FastGreat:
                        case JudgeGrade.FastGreat2nd:
                        case JudgeGrade.FastGreat3rd:
                            return canPlay && skin.Great.Fast is not null;
                        case JudgeGrade.LatePerfect2nd:
                        case JudgeGrade.LatePerfect3rd:
                            return canPlay && skin.Perfect.Late is not null;
                        case JudgeGrade.FastPerfect2nd:
                        case JudgeGrade.FastPerfect3rd:
                            return canPlay && skin.Perfect.Fast is not null;
                        case JudgeGrade.Perfect:
                            {
                                var isJust = judgeResult.Diff == 0;
                                var isFast = judgeResult.IsFast;
                                if (isJust)
                                    return false;
                                else if (isFast)
                                {
                                    if (isCritical)
                                        return canPlay && skin.CriticalPerfect.Fast is not null;
                                    else
                                        return canPlay && skin.Perfect.Fast is not null;
                                }
                                else
                                {
                                    if (isCritical)
                                        return canPlay && skin.CriticalPerfect.Late is not null;
                                    else
                                        return canPlay && skin.Perfect.Late is not null;
                                }
                            }
                    }
                }
            }
            else
            {
                switch (judgeResult.Grade)
                {
                    case JudgeGrade.LateGood:
                        return canPlay && skin.Good.Late is not null;
                    case JudgeGrade.FastGood:
                        return canPlay && skin.Good.Fast is not null;

                    case JudgeGrade.LateGreat3rd:
                    case JudgeGrade.LateGreat2nd:
                    case JudgeGrade.LateGreat:
                        return canPlay && skin.Great.Late is not null;
                    case JudgeGrade.FastGreat3rd:
                    case JudgeGrade.FastGreat2nd:
                    case JudgeGrade.FastGreat:
                        return canPlay && skin.Great.Fast is not null;
                    case JudgeGrade.LatePerfect2nd:
                    case JudgeGrade.LatePerfect3rd:
                        return canPlay && skin.Perfect.Late is not null;
                    case JudgeGrade.FastPerfect2nd:
                    case JudgeGrade.FastPerfect3rd:
                        return canPlay && skin.Perfect.Fast is not null;
                    case JudgeGrade.Perfect:
                        {
                            if (judgeResult.Diff == 0)
                                return false;
                            if (judgeResult.IsFast)
                            {
                                if (isCritical)
                                    return canPlay && skin.CriticalPerfect.Fast is not null;
                                else
                                    return canPlay && skin.Perfect.Fast is not null;
                            }
                            else
                            {
                                if (isCritical)
                                    return canPlay && skin.CriticalPerfect.Late is not null;
                                else
                                    return canPlay && skin.Perfect.Late is not null;
                            }
                        }
                }
            }

            return canPlay;
        }
    }
}
