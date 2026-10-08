using MajdataPlay.Settings;
using MajdataPlay.IO;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MajdataPlay.Editor;
using UnityEngine;
using Random = System.Random;
using MajdataPlay.Scenes.View;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Numerics;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal abstract class NoteDrop : MajComponent, IStatefulNote
    {
        public int StartPos
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _startPos;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (value.InRange(1, 8))
                    _startPos = value;
                else
                    throw new ArgumentOutOfRangeException("Start position must be between 1 and 8");
            }
        }
        public float Timing
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _timing;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _timing = value;
        }
        public int SortOrder
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sortOrder;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _sortOrder = value;
        }
        public float Speed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _speed;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _speed = value;
        }
        public bool IsEach
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isEach;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isEach = value;
        }
        public bool IsBreak
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isBreak;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isBreak = value;
        }
        public bool IsEX
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isEX;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isEX = value;
        }
        public bool IsMine
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isMine;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isMine = value;
        }
        public bool IsInited
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State >= NoteStatus.Inited;
        }
        public bool IsEnded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => State == NoteStatus.End;
        }
        public bool IsClassic
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => USERSETTING_SLIDE_JUDGE_MODE == JudgeModeOption.Classic;
        }
        public NoteStatus State
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set => _state = value;
        }
        public float JudgeTimingWithOffset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => JudgeTiming + USERSETTING_JUDGE_OFFSET_SEC;
        }
        public float ThisFrameSec
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _noteController.ThisFrameSec;
        }

        protected bool IsUseButtonRingForTouch
        {
            get; private set;
        }
        protected INoteController NoteController
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _noteController;
        }
        protected Material BreakMaterial
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _breakMaterial;
        }
        protected Material DefaultMaterial
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _defaultMaterial;
        }
        protected Material HoldShineMaterial
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _holdShineMaterial;
        }
        protected GameModInfo ModInfo
        {
            get;
            private set;
        }
        protected bool IsAutoplay
        {
            get => AutoplayMode != AutoplayModeOption.Disable;
        }
        protected bool IsJudged = false;
        protected JudgeGrade AutoplayGrade = JudgeGrade.Perfect;
        protected AutoplayModeOption AutoplayMode = AutoplayModeOption.Disable;
        /// <summary>
        /// The answer frame
        /// </summary>
        protected float JudgeTiming;
        protected float JudgeDiff = -1;
        protected Range<float> JudgableRange = new(float.MinValue, float.MinValue + 1, ContainsType.Closed);
        protected JudgeGrade JudgeResult = JudgeGrade.Miss;

        protected SensorArea SensorPos;

        protected ObjectCounter ObjectCounter;
        protected NoteManager NoteManager;
        protected NoteEffectManager EffectManager;
        protected NoteAudioManager AudioEffMana;
        protected GameSetting Settings;
        protected Random Randomizer;

        protected bool USERSETTING_SLIDE_SKIPPING = false;
        protected float USERSETTING_JUDGE_OFFSET_SEC = 0f;
        protected float USERSETTING_TOUCHPANEL_OFFSET_SEC = 0f;
        protected float USERSETTING_TAP_SCALE = 1;
        protected float USERSETTING_HOLD_SCALE = 1;
        protected float USERSETTING_TOUCH_SCALE = 1;
        protected float USERSETTING_SLIDE_SCALE = 1;
        protected bool USERSETTING_DISPLAY_HOLD_HEAD_JUDGE_RESULT = false;
        protected JudgeModeOption USERSETTING_SLIDE_JUDGE_MODE = JudgeModeOption.Modern;
        protected DJAutoPolicyOption USERSETTING_DJAUTO_POLICY = DJAutoPolicyOption.Strict;

        protected const float FRAME_LENGTH_SEC = MajEnv.FRAME_LENGTH_SEC;
        protected const float FRAME_LENGTH_MSEC = MajEnv.FRAME_LENGTH_MSEC;

        protected const float TAP_JUDGE_SEG_1ST_PERFECT_MSEC = 1 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_2ND_PERFECT_MSEC = 2 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_3RD_PERFECT_MSEC = 3 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_1ST_GREAT_MSEC = 4 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_2ND_GREAT_MSEC = 5 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_SEG_3RD_GREAT_MSEC = 6 * FRAME_LENGTH_MSEC;
        protected const float TAP_JUDGE_GOOD_AREA_MSEC = 9 * FRAME_LENGTH_MSEC;

        protected const float TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC = 9 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_2ND_PERFECT_MSEC = 10.5f * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_3RD_PERFECT_MSEC = 12 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_1ST_GREAT_MSEC = 13 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_2ND_GREAT_MSEC = 14 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_SEG_3RD_GREAT_MSEC = 15 * FRAME_LENGTH_MSEC;
        protected const float TOUCH_JUDGE_GOOD_AREA_MSEC = 18 * FRAME_LENGTH_MSEC;

        protected const float TOUCH_DISPLAY_OFFSET_SEC = 0 * FRAME_LENGTH_SEC;
        protected const float TOUCH_HOLD_DISPLAY_OFFSET_SEC = 0 * FRAME_LENGTH_SEC;

        protected override void Awake()
        {
            base.Awake();

            var gameInfo = Majdata<GameInfo>.Instance;
            Settings = MajEnv.Settings;
            Randomizer = new();
            ObjectCounter = Majdata<ObjectCounter>.Instance!;
            NoteManager = Majdata<NoteManager>.Instance!;
            EffectManager = Majdata<NoteEffectManager>.Instance!;
            AudioEffMana = Majdata<NoteAudioManager>.Instance!;

            USERSETTING_SLIDE_SKIPPING = gameInfo?.ChartSettings.SlideSkipping ?? Settings.Game.SlideSkipping;
            USERSETTING_JUDGE_OFFSET_SEC = ((MajEnv.Settings?.Judge.JudgeOffset ?? 0) + (MajEnv.Settings?.Debug.DisplayOffset ?? 0)) * ((MajEnv.Settings?.Debug.OffsetUnit ?? OffsetUnitOption.Second) == OffsetUnitOption.Second ? 1 : FRAME_LENGTH_SEC);
            USERSETTING_TOUCHPANEL_OFFSET_SEC = (MajEnv.Settings?.Judge.TouchPanelOffset ?? 0) * ((MajEnv.Settings?.Debug.OffsetUnit ?? OffsetUnitOption.Second) == OffsetUnitOption.Second ? 1 : FRAME_LENGTH_SEC);
            USERSETTING_TAP_SCALE = MajEnv.Settings?.Display.TapScale ?? 1;
            USERSETTING_HOLD_SCALE = MajEnv.Settings?.Display.HoldScale ?? 1;
            USERSETTING_TOUCH_SCALE = MajEnv.Settings?.Display.TouchScale ?? 1;
            USERSETTING_SLIDE_SCALE = MajEnv.Settings?.Display.SlideScale ?? 1;
            USERSETTING_DISPLAY_HOLD_HEAD_JUDGE_RESULT = MajEnv.Settings?.Display.DisplayHoldHeadJudgeResult ?? false;
            USERSETTING_SLIDE_JUDGE_MODE = MajEnv.Settings?.Judge.Mode ?? JudgeModeOption.Modern;
            USERSETTING_DJAUTO_POLICY = MajEnv.Settings?.Debug.DJAutoPolicy ?? DJAutoPolicyOption.Strict;

            _noteController = Majdata<INoteController>.Instance!;
            ModInfo = _noteController.ModInfo;
            _breakMaterial = _noteController.BreakMaterial;
            _defaultMaterial = _noteController.DefaultMaterial;
            _holdShineMaterial = _noteController.HoldShineMaterial;
            AutoplayMode = ModInfo.AutoPlay;
            AutoplayGrade = _noteController.AutoplayGrade;
#if UNITY_ANDROID || UNITY_IOS
            IsUseButtonRingForTouch = false;
#else
            IsUseButtonRingForTouch = ModInfo.ButtonRingForTouch;
#endif
        }
        void OnDestroy()
        {
            Active = false;
        }
        protected abstract void LoadSkin();
        protected abstract void PlaySFX();
        protected abstract void PlayJudgeSFX(in NoteJudgeResult judgeResult);
        protected virtual void Judge(float currentSec)
        {
            if (IsJudged)
                return;

            var diffSec = currentSec - JudgeTimingWithOffset;
            var isFast = diffSec < 0;
            JudgeDiff = diffSec * 1000;
            var diffMSec = MathF.Abs(diffSec * 1000);
            var result = diffMSec switch
            {
                <= TAP_JUDGE_SEG_1ST_PERFECT_MSEC => JudgeGrade.Perfect,
                <= TAP_JUDGE_SEG_2ND_PERFECT_MSEC => isFast ? JudgeGrade.FastPerfect2nd : JudgeGrade.LatePerfect2nd,
                <= TAP_JUDGE_SEG_3RD_PERFECT_MSEC => isFast ? JudgeGrade.FastPerfect3rd : JudgeGrade.LatePerfect3rd,
                <= TAP_JUDGE_SEG_1ST_GREAT_MSEC => isFast ? JudgeGrade.FastGreat : JudgeGrade.LateGreat,
                <= TAP_JUDGE_SEG_2ND_GREAT_MSEC => isFast ? JudgeGrade.FastGreat2nd : JudgeGrade.LateGreat2nd,
                <= TAP_JUDGE_SEG_3RD_GREAT_MSEC => isFast ? JudgeGrade.FastGreat3rd : JudgeGrade.LateGreat3rd,
                _ => isFast ? JudgeGrade.FastGood : JudgeGrade.LateGood
            };

            if (IsEX)
            {
                result = JudgeGrade.Perfect;
            }

            ConvertJudgeGrade(ref result);
            JudgeResult = result;
            IsJudged = true;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual void Autoplay()
        {
            if (IsJudged || !IsAutoplay)
                return;
            if (GetTimeSpanToArriveTiming() >= -0.016667f)
            {
                var autoplayGrade = NoteController.AutoplayGrade;
                if (((int)autoplayGrade).InRange(0, 14))
                {
                    JudgeResult = autoplayGrade;
                }
                else
                {
                    JudgeResult = (JudgeGrade)Randomizer.Next(0, 15);
                }
                ConvertJudgeGrade(ref JudgeResult);
                IsJudged = true;
                JudgeDiff = JudgeResult switch
                {
                    < JudgeGrade.Perfect => 1,
                    > JudgeGrade.Perfect => -1,
                    _ => 0
                };
            }
        }

        /// <summary>
        /// Gets the time offset from the current moment to the judgment line.
        /// </summary>
        /// <returns>
        /// If the current moment is behind the judgment line, the result is a positive number.
        /// <para>If the current moment is ahead of the judgment line, the result is a negative number.</para>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual float GetTimeSpanToArriveTiming() => ThisFrameSec - Timing;
        /// <summary>
        /// Gets the time offset from the current moment to the answer frame.
        /// </summary>
        /// <returns>
        /// If the current moment is behind the answer frame, the result is a positive number.
        /// <para>If the current moment is ahead of the answer frame, the result is a negative number.</para>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected float GetTimeSpanToJudgeTiming() => ThisFrameSec - JudgeTimingWithOffset;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected float GetTimeSpanToJudgeTiming(float baseTiming) => baseTiming - JudgeTimingWithOffset;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ConvertJudgeGrade(ref JudgeGrade grade)
        {
            var judgeStyle = ModInfo.JudgeStyle;
            switch (judgeStyle)
            {
                case JudgeStyleOption.MAJI:
                    ConvertToMAJI(ref grade);
                    break;
                case JudgeStyleOption.GACHI:
                    ConvertToGACHI(ref grade);
                    break;
                case JudgeStyleOption.GORI:
                    ConvertToGORI(ref grade);
                    break;
                case JudgeStyleOption.DEFAULT:
                default:
                    return;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ConvertToMAJI(ref JudgeGrade judgeType)
        {
            var isFast = (int)judgeType > 7;
            switch (judgeType)
            {
                case JudgeGrade.LateGreat:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.FastGreat:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat3rd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGood;
                    else
                        judgeType = JudgeGrade.LateGood;
                    break;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGreat;
                    else
                        judgeType = JudgeGrade.LateGreat;
                    break;
                default:
                    if (judgeType > JudgeGrade.Perfect)
                        judgeType = JudgeGrade.TooFast;
                    else if (judgeType < JudgeGrade.Perfect)
                        judgeType = JudgeGrade.Miss;
                    break;
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.Perfect:
                case JudgeGrade.Miss:
                case JudgeGrade.TooFast:
                    return;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ConvertToGACHI(ref JudgeGrade judgeType)
        {
            var isFast = (int)judgeType > 7;
            switch (judgeType)
            {
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGood;
                    else
                        judgeType = JudgeGrade.LateGood;
                    break;
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                    if (isFast)
                        judgeType = JudgeGrade.FastGreat;
                    else
                        judgeType = JudgeGrade.LateGreat;
                    break;
                default:
                    if (judgeType > JudgeGrade.Perfect)
                        judgeType = JudgeGrade.TooFast;
                    else if (judgeType < JudgeGrade.Perfect)
                        judgeType = JudgeGrade.Miss;
                    break;
                case JudgeGrade.Perfect:
                case JudgeGrade.Miss:
                    return;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ConvertToGORI(ref JudgeGrade judgeType)
        {
            switch (judgeType)
            {
                case JudgeGrade.Perfect:
                case JudgeGrade.Miss:
                    return;
                default:
                    if (judgeType > JudgeGrade.Perfect)
                        judgeType = JudgeGrade.TooFast;
                    else if (judgeType < JudgeGrade.Perfect)
                        judgeType = JudgeGrade.Miss;
                    break;
            }
        }
        [ReadOnlyField, SerializeField]
        int _startPos = 1;
        [ReadOnlyField, SerializeField]
        float _timing;
        [ReadOnlyField, SerializeField]
        float _speed = 7;
        [ReadOnlyField, SerializeField]
        int _sortOrder;
        [ReadOnlyField, SerializeField]
        bool _isEach = false;
        [ReadOnlyField, SerializeField]
        bool _isBreak = false;
        [ReadOnlyField, SerializeField]
        bool _isEX = false;
        [ReadOnlyField, SerializeField]
        bool _isMine = false;        
        [ReadOnlyField, SerializeField]
        NoteStatus _state = NoteStatus.Start;

        Material _breakMaterial;
        Material _defaultMaterial;
        Material _holdShineMaterial;

        INoteController _noteController;
    }
}