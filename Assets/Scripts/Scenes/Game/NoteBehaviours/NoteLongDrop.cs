using System;
using System.Runtime.CompilerServices;
using MajdataPlay.Editor;
using MajdataPlay.Extensions;
using MajdataPlay.Numerics;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal abstract class NoteLongDrop : NoteDrop
    {
        protected const float SLIDE_JUDGE_MAXIMUM_ALLOWED_EXT_LENGTH_MSEC = 22 * FRAME_LENGTH_MSEC;
        protected const float SLIDE_JUDGE_SEG_BASE_3RD_PERFECT_MSEC = 14 * FRAME_LENGTH_MSEC;
        protected const float SLIDE_JUDGE_SEG_1ST_GREAT_MSEC = 21 * FRAME_LENGTH_MSEC;
        protected const float SLIDE_JUDGE_SEG_2ND_GREAT_MSEC = 25 * FRAME_LENGTH_MSEC;
        protected const float SLIDE_JUDGE_SEG_3RD_GREAT_MSEC = 29 * FRAME_LENGTH_MSEC;

        protected const float SLIDE_JUDGE_CLASSIC_FAST_SEG_1ST_PERFECT_MSEC = 4 * FRAME_LENGTH_MSEC;  // 4f
        protected const float SLIDE_JUDGE_CLASSIC_FAST_SEG_2ND_PERFECT_MSEC = 8 * FRAME_LENGTH_MSEC;  // 8f
        protected const float SLIDE_JUDGE_CLASSIC_FAST_SEG_3RD_PERFECT_MSEC = 12 * FRAME_LENGTH_MSEC; // 12f
        protected const float SLIDE_JUDGE_CLASSIC_FAST_SEG_1ST_GREAT_MSEC = 16 * FRAME_LENGTH_MSEC;   // 16f
        protected const float SLIDE_JUDGE_CLASSIC_FAST_SEG_2ND_GREAT_MSEC = 20 * FRAME_LENGTH_MSEC;   // 20f
        protected const float SLIDE_JUDGE_CLASSIC_FAST_SEG_3RD_GREAT_MSEC = 24 * FRAME_LENGTH_MSEC;   // 24f

        protected const float SLIDE_JUDGE_CLASSIC_LATE_SEG_1ST_PERFECT_MSEC = 4 * FRAME_LENGTH_MSEC;  // 4f
        protected const float SLIDE_JUDGE_CLASSIC_LATE_SEG_2ND_PERFECT_MSEC = 8 * FRAME_LENGTH_MSEC;  // 8f
        protected const float SLIDE_JUDGE_CLASSIC_LATE_SEG_3RD_PERFECT_MSEC = 12 * FRAME_LENGTH_MSEC; // 12f
        protected const float SLIDE_JUDGE_CLASSIC_LATE_SEG_1ST_GREAT_MSEC = 16 * FRAME_LENGTH_MSEC;   // 16f
        protected const float SLIDE_JUDGE_CLASSIC_LATE_SEG_2ND_GREAT_MSEC = 20 * FRAME_LENGTH_MSEC;   // 20f
        protected const float SLIDE_JUDGE_CLASSIC_LATE_SEG_3RD_GREAT_MSEC = 24 * FRAME_LENGTH_MSEC;   // 24f

        protected const float SLIDE_JUDGE_GOOD_AREA_MSEC = 36 * FRAME_LENGTH_MSEC;               // 36f

        protected const int HOLD_HEAD_STATE_NOT_JUDGED = 0;
        protected const int HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK = 1;
        protected const int HOLD_HEAD_STATE_JUDGED = 2;
        protected const int HOLD_STATE_NONE = -1;
        protected const int HOLD_STATE_RELEASED = 0;
        protected const int HOLD_STATE_PRESSED = 1;

        protected const float HOLD_CLASSIC_END_JUDGE_PERFECT_FAST_AREA_MSEC = 9 * FRAME_LENGTH_MSEC;
        protected const float HOLD_CLASSIC_END_JUDGE_PERFECT_LATE_AREA_MSEC = 14 * FRAME_LENGTH_MSEC;

        protected const float HOLD_HEAD_IGNORE_LENGTH_SEC = 6 * FRAME_LENGTH_SEC;
        protected const float HOLD_TAIL_IGNORE_LENGTH_SEC = 12 * FRAME_LENGTH_SEC;
        protected const float TOUCH_HOLD_HEAD_IGNORE_LENGTH_SEC = 15 * FRAME_LENGTH_SEC;
        protected const float TOUCH_HOLD_TAIL_IGNORE_LENGTH_SEC = 12 * FRAME_LENGTH_SEC;
        protected const float DELUXE_HOLD_RELEASE_IGNORE_TIME_SEC = 2 * FRAME_LENGTH_SEC;
        protected const float CLASSIC_HOLD_ALLOW_OVER_LENGTH_SEC = 20 * FRAME_LENGTH_SEC;


        public float Length
        {
            get => _length;
            set => _length = value;
        }

        [ReadOnlyField]
        [SerializeField]
        protected float PlayerReleaseTimeSec = 0;
        [ReadOnlyField]
        [SerializeField]
        protected float _length = 1f;

        protected readonly static Range<float> DEFAULT_HOLD_BODY_CHECK_RANGE = new Range<float>(float.MinValue, float.MinValue, ContainsType.Closed);
        protected readonly static Range<float> CLASSIC_HOLD_BODY_CHECK_RANGE = new Range<float>(float.MinValue, float.MaxValue, ContainsType.Closed);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual float GetRemainingTime() => MathF.Max(Length - GetTimeSpanToJudgeTiming(), 0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual float GetRemainingTimeWithoutOffset() => MathF.Max(Length - GetTimeSpanToArriveTiming(), 0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected JudgeGrade HoldEndJudge(in JudgeGrade headGrade, in float ingoreTimeSec)
        {
            if (!IsJudged)
            {
                return headGrade;
            }

            var realityHT = (Length - ingoreTimeSec).Clamp(0, Length - 0.3f);
            var percent = ((realityHT - PlayerReleaseTimeSec) / realityHT).Clamp(0, 1);

            if (realityHT <= 0)
            {
                return headGrade;
            }
            //See also: https://www.bilibili.com/opus/694985211225571337/
            var pressGrade = percent switch
            {
                >= 1f => 0,   // >= 100%
                >= 0.67f => 1,// [0.67, 1)
                >= 0.33f => 2,// [0.33, 0.67)
                >= 0.05f => 3,// [0.05, 0.33)
                _ => 4,       // [0, 0.05)
            };

            switch (pressGrade)
            {
                case 0:// >= 100%
                    {
                        switch (headGrade)
                        {
                            case JudgeGrade.LatePerfect3rd:
                            case JudgeGrade.LatePerfect2nd:
                            case JudgeGrade.Perfect:
                            case JudgeGrade.FastPerfect2nd:
                            case JudgeGrade.FastPerfect3rd:
                                return headGrade;
                            case JudgeGrade.LateGood:
                            case JudgeGrade.LateGreat3rd:
                            case JudgeGrade.LateGreat2nd:
                            case JudgeGrade.LateGreat:
                                return JudgeGrade.LateGreat;
                            case JudgeGrade.FastGreat:
                            case JudgeGrade.FastGreat2nd:
                            case JudgeGrade.FastGreat3rd:
                            case JudgeGrade.FastGood:
                                return JudgeGrade.FastGreat;
                            case JudgeGrade.Miss:
                                return JudgeGrade.LateGood;
                            case JudgeGrade.TooFast:
                                return JudgeGrade.FastGood;
                        }
                    }
                    break;
                case 1:// [0.67, 1)
                    {
                        switch (headGrade)
                        {
                            case JudgeGrade.Perfect:
                                if (JudgeDiff >= 0)
                                {
                                    return JudgeGrade.LatePerfect2nd;
                                }
                                else
                                {
                                    return JudgeGrade.FastPerfect2nd;
                                }
                            case JudgeGrade.LatePerfect3rd:
                            case JudgeGrade.LatePerfect2nd:
                            case JudgeGrade.FastPerfect2nd:
                            case JudgeGrade.FastPerfect3rd:
                                return headGrade;
                            case JudgeGrade.LateGood:
                            case JudgeGrade.LateGreat3rd:
                            case JudgeGrade.LateGreat2nd:
                            case JudgeGrade.LateGreat:
                                return JudgeGrade.LateGreat;
                            case JudgeGrade.FastGreat:
                            case JudgeGrade.FastGreat2nd:
                            case JudgeGrade.FastGreat3rd:
                            case JudgeGrade.FastGood:
                                return JudgeGrade.FastGreat;
                            case JudgeGrade.Miss:
                                return JudgeGrade.LateGood;
                            case JudgeGrade.TooFast:
                                return JudgeGrade.FastGood;
                        }
                    }
                    break;
                case 2:// [0.33, 0.67)
                    {
                        switch (headGrade)
                        {
                            case JudgeGrade.Perfect:
                                if (JudgeDiff >= 0)
                                {
                                    return JudgeGrade.LateGreat2nd;
                                }
                                else
                                {
                                    return JudgeGrade.FastGreat2nd;
                                }
                            case JudgeGrade.LateGood:
                            case JudgeGrade.LateGreat3rd:
                            case JudgeGrade.LateGreat2nd:
                            case JudgeGrade.LateGreat:
                            case JudgeGrade.LatePerfect3rd:
                            case JudgeGrade.LatePerfect2nd:
                                return JudgeGrade.LateGreat;
                            case JudgeGrade.FastPerfect2nd:
                            case JudgeGrade.FastPerfect3rd:
                            case JudgeGrade.FastGreat:
                            case JudgeGrade.FastGreat2nd:
                            case JudgeGrade.FastGreat3rd:
                            case JudgeGrade.FastGood:
                                return JudgeGrade.FastGreat;
                            case JudgeGrade.Miss:
                                return JudgeGrade.LateGood;
                            case JudgeGrade.TooFast:
                                return JudgeGrade.FastGood;
                        }
                    }
                    break;
                case 3:// [0.05, 0.33)
                    {
                        switch (headGrade)
                        {
                            case JudgeGrade.Perfect:
                                if (JudgeDiff >= 0)
                                {
                                    return JudgeGrade.LateGood;
                                }
                                else
                                {
                                    return JudgeGrade.FastGood;
                                }
                            case JudgeGrade.Miss:
                            case JudgeGrade.LateGood:
                            case JudgeGrade.LateGreat3rd:
                            case JudgeGrade.LateGreat2nd:
                            case JudgeGrade.LateGreat:
                            case JudgeGrade.LatePerfect3rd:
                            case JudgeGrade.LatePerfect2nd:
                                return JudgeGrade.LateGood;
                            case JudgeGrade.FastPerfect2nd:
                            case JudgeGrade.FastPerfect3rd:
                            case JudgeGrade.FastGreat:
                            case JudgeGrade.FastGreat2nd:
                            case JudgeGrade.FastGreat3rd:
                            case JudgeGrade.FastGood:
                            case JudgeGrade.TooFast:
                                return JudgeGrade.FastGood;
                        }
                    }
                    break;
                case 4:// [0, 0.05)
                    {
                        switch (headGrade)
                        {
                            case JudgeGrade.Perfect:
                                if (JudgeDiff >= 0)
                                {
                                    return JudgeGrade.LateGood;
                                }
                                else
                                {
                                    return JudgeGrade.FastGood;
                                }
                            case JudgeGrade.LateGood:
                            case JudgeGrade.LateGreat3rd:
                            case JudgeGrade.LateGreat2nd:
                            case JudgeGrade.LateGreat:
                            case JudgeGrade.LatePerfect3rd:
                            case JudgeGrade.LatePerfect2nd:
                                return JudgeGrade.LateGood;
                            case JudgeGrade.FastPerfect2nd:
                            case JudgeGrade.FastPerfect3rd:
                            case JudgeGrade.FastGreat:
                            case JudgeGrade.FastGreat2nd:
                            case JudgeGrade.FastGreat3rd:
                            case JudgeGrade.FastGood:
                                return JudgeGrade.FastGood;
                            case JudgeGrade.Miss:
                            case JudgeGrade.TooFast:
                                return headGrade;
                        }
                    }
                    break;
            }

            throw new ArgumentOutOfRangeException();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected JudgeGrade HoldClassicEndJudge(in JudgeGrade headGrade,float offset)
        {
            if (!IsJudged)
            {
                return headGrade;
            }
            else if (headGrade.IsMissOrTooFast())
            {
                return headGrade;
            }

            var releaseTiming = ThisFrameSec - USERSETTING_JUDGE_OFFSET_SEC - offset;
            var diffSec = Timing + Length - releaseTiming;
            var isFast = diffSec > 0;
            var diffMSec = MathF.Abs(diffSec) * 1000;
            var endGrade = JudgeGrade.Miss;

            if (isFast)
            {
                if(diffMSec < HOLD_CLASSIC_END_JUDGE_PERFECT_FAST_AREA_MSEC)
                {
                    endGrade = JudgeGrade.Perfect;
                }
                else
                {
                    endGrade = JudgeGrade.FastGood;
                }
            }
            else
            {
                if (diffMSec < HOLD_CLASSIC_END_JUDGE_PERFECT_LATE_AREA_MSEC)
                {
                    endGrade = JudgeGrade.Perfect;
                }
                else
                {
                    endGrade = JudgeGrade.LateGood;
                }
            }

            var num = Math.Abs(7 - (int)headGrade);
            var endNum = Math.Abs(7 - (int)endGrade);
            if (endNum > num) // 取最差判定
            {
                return endGrade;
            }
            else
            {
                return headGrade;
            }
        }
    }
}