using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Utils;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal class TapEffectDisplayer : MonoBehaviour
    {
        /// <summary>
        /// Gets the world position of the judgment effect.
        /// </summary>
        public Vector3 Position => effectParent.transform.position;
        /// <summary>
        /// Gets the local position or updates its X and Y coordinates and the text layout while preserving sorting depth.
        /// </summary>
        public Vector3 LocalPosition
        {
            get => effectParent.transform.localPosition;
            set
            {
                NoteHelper.Sorting.SetLocalPosition(effectParent.transform, value);
                //perfectDisplayer.transform.localPosition = value;
                //greatDisplayer.transform.localPosition = value;
                //goodDisplayer.transform.localPosition = value;
                var effectPosition = value;
                effectPosition.z = 0f;
                var textDistance = 1f * (2 - DistanceRatio);
                var fastLateDistance = textDistance + 0.66f;

                Vector3 textPosition;
                Vector3 fastLatePosition;
                if (effectPosition.magnitude == 0)
                {
                    textPosition = effectPosition.GetPoint(new Vector3(0, -0.01f, 0), -textDistance);
                    fastLatePosition = effectPosition.GetPoint(new Vector3(0, -0.01f, 0), -fastLateDistance);
                }
                else
                {
                    textPosition = effectPosition.GetPoint(-textDistance);
                    fastLatePosition = effectPosition.GetPoint(-fastLateDistance);
                }

                JudgeTextDisplayer.LocalPosition = textPosition;
                _fastLateDisplayerA.LocalPosition = fastLatePosition;
            }
        }
        /// <summary>
        /// 用于调整Text、Fast/Late的显示位置
        /// </summary>
        public float DistanceRatio { get; set; } = 1f;

        [SerializeField]
        GameObject effectParent;
        [SerializeField]
        TapPerfectEffectDisplayer _perfectDisplayer;
        [SerializeField]
        TapGreatEffectDisplayer _greatDisplayer;
        [SerializeField]
        TapGoodEffectDisplayer _goodDisplayer;

        [SerializeField]
        [FormerlySerializedAs("_judgeTextDisplayer")]
        protected JudgeTextDisplayer JudgeTextDisplayer;
        [SerializeField]
        FastLateDisplayer _fastLateDisplayerA;
        [SerializeField]
        FastLateDisplayer _fastLateDisplayerB;

        bool _isEnabled = false;

        void Start()
        {
            Reset();
            var effectPosition = effectParent.transform.localPosition;
            effectPosition.z = 0f;
            var textDistance = 1f * (2 - DistanceRatio);
            var fastLateDistance = textDistance + 0.66f;

            Vector3 textPosition;
            Vector3 fastLatePosition;
            if (effectPosition.magnitude == 0)
            {
                textPosition = effectPosition.GetPoint(new Vector3(0, -0.01f, 0), -textDistance);
                fastLatePosition = effectPosition.GetPoint(new Vector3(0, -0.01f, 0), -fastLateDistance);
            }
            else
            {
                textPosition = effectPosition.GetPoint(-textDistance);
                fastLatePosition = effectPosition.GetPoint(-fastLateDistance);
            }
            JudgeTextDisplayer.LocalPosition = textPosition;
            _fastLateDisplayerA.LocalPosition = fastLatePosition;
            _fastLateDisplayerB.LocalPosition = textPosition;
            _isEnabled = MajEnv.Settings.Display.OuterJudgeDistance != 0 && DistanceRatio != 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            _perfectDisplayer.OnLateUpdate();
            _greatDisplayer.OnLateUpdate();
            _goodDisplayer.OnLateUpdate();
            JudgeTextDisplayer.OnLateUpdate();
            _fastLateDisplayerA.OnLateUpdate();
            _fastLateDisplayerB.OnLateUpdate();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _perfectDisplayer.Reset();
            _greatDisplayer.Reset();
            _goodDisplayer.Reset();
        }
        /// <summary>
        /// 将Effect、Text和FastLate设置为非活动状态
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetAll()
        {
            Reset();
            JudgeTextDisplayer.Reset();
            _fastLateDisplayerA.Reset();
            _fastLateDisplayerB.Reset();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Play(in NoteJudgeResult judgeResult)
        {
            JudgeTextDisplayer.Reset();
            _fastLateDisplayerA.Reset();
            _fastLateDisplayerB.Reset();
            PlayEffect(judgeResult);
            if (!_isEnabled)
            {
                return;
            }
            if (IsClassCAvailable(judgeResult))
            {
                JudgeTextDisplayer.Play(judgeResult,true);
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
            var isBreak = judgeResult.IsBreak;
            var result = judgeResult.Grade;
            Reset();

            if (isBreak)
            {
                _perfectDisplayer.PlayEffect(judgeResult);
            }
            else
            {
                switch (result)
                {
                    case JudgeGrade.LateGood:
                    case JudgeGrade.FastGood:
                        _goodDisplayer.PlayEffect(judgeResult);
                        break;
                    case JudgeGrade.LateGreat:
                    case JudgeGrade.LateGreat2nd:
                    case JudgeGrade.LateGreat3rd:
                    case JudgeGrade.FastGreat3rd:
                    case JudgeGrade.FastGreat2nd:
                    case JudgeGrade.FastGreat:
                        _greatDisplayer.PlayEffect(judgeResult);
                        break;
                    case JudgeGrade.LatePerfect3rd:
                    case JudgeGrade.FastPerfect3rd:
                    case JudgeGrade.LatePerfect2nd:
                    case JudgeGrade.FastPerfect2nd:
                    case JudgeGrade.Perfect:
                        _perfectDisplayer.PlayEffect(judgeResult);
                        break;
                    default:
                        break;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual bool PlayResult(in NoteJudgeResult judgeResult)
        {
            bool canPlay;
            if (judgeResult.IsMine && judgeResult.Grade is not (JudgeGrade.Miss or JudgeGrade.TooFast))
            {
                return false;
            }
            else if (judgeResult.IsBreak)
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.BreakJudgeType, judgeResult);
            }
            else
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.NoteJudgeType, judgeResult);
            }
            if (!canPlay)
            {
                return false;
            }
            JudgeTextDisplayer.Play(judgeResult);
            return true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool PlayFastLate(in NoteJudgeResult judgeResult, FastLateDisplayer displayer)
        {
            bool canPlay;
            if (judgeResult.IsMine)
            {
                return judgeResult.Grade == JudgeGrade.Miss || judgeResult.Grade == JudgeGrade.TooFast;
            }
            else if (judgeResult.IsBreak)
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.BreakFastLateType, judgeResult);
            }
            else
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.FastLateType, judgeResult);
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
            bool canPlay;
            var isBreak = judgeResult.IsBreak;

            if (judgeResult.IsMissOrTooFast)
            {
                return false;
            }
            if (isBreak)
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.BreakJudgeType, judgeResult);
                canPlay = canPlay && NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.BreakFastLateType, judgeResult);
            }
            else
            {
                canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.NoteJudgeType, judgeResult);
                canPlay = canPlay && NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.FastLateType, judgeResult);
            }
            if (!canPlay)
            {
                return canPlay;
            }
            var skin = MajInstances.SkinManager.GetJudgeTextSkin();
            var isCritical = MajEnv.Settings.Display.DisplayCriticalPerfect;
            var displayBreakScore = MajEnv.Settings.Display.DisplayBreakScore;
            if (isBreak)
            {
                if(displayBreakScore)
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
                                else if(isFast)
                                {
                                    if(isCritical)
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
