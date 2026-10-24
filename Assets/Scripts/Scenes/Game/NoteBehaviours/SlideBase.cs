using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Editor;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Serialization;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal abstract class SlideBase : NoteLongDrop
    {
        const int SLIDE_BAR_MODE_SET_ALPHA = 1;
        const int SLIDE_BAR_MODE_SET_MATERIAL = 2;
        const int SLIDE_BAR_MODE_SET_ALPHA_AND_MATERIAL = 3;
        public IConnectableSlide? Parent => ConnectInfo.Parent;
        public ConnSlideInfo ConnectInfo
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set;
        } = new()
        {
            IsGroupPart = false,
            Parent = null
        };
        /// <summary>
        /// If all judgment areas have been completed, return True, otherwise False
        /// </summary>
        public bool IsFinished
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => QueueRemaining == 0;
        }
        /// <summary>
        /// Returns the number of unfinished judgment areas in the judgment queue
        /// </summary>
        public bool IsPendingFinish
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => QueueRemaining == 1;
        }
        /// <summary>
        /// 返回判定队列中未完成判定区的数量
        /// </summary>
        public int QueueRemaining
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Span<int> reamaining = stackalloc int[3];
                var judgeQueues = JudgeQueues.AsSpan();
                for (var i = 0; i < judgeQueues.Length; i++)
                {
                    var queue = judgeQueues[i];
                    reamaining[i] = queue.Length;
                }
                return reamaining.Max();
            }
        }
        /// <summary>
        /// a timing of slide start
        /// </summary>
        public float StartTiming
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _startTiming;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _startTiming = value;
        }
        public float ArriveTiming
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _startTiming + Length;
        }
        public bool IsJustR
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _isJustR;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _isJustR = value;
        }
        public int EndPos
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _endPos;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (value.InRange(1, 8))
                {
                    _endPos = value;
                }
                else
                {
                    throw new ArgumentOutOfRangeException("End position must be between 1 and 8");
                }
            }
        }
        public int Multiple
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _multiple;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _multiple = value;
        }
        public string SlideType
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _slideType;
        }
        public float SlideLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _slideLength;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set => _slideLength = value;
        }
        public bool IsSlideNoHead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set;
        } = false;
        public bool IsSlideNoTrack
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set;
        } = false;
        protected readonly Memory<SlideArea>[] JudgeQueues = new Memory<SlideArea>[3]
        {
            Memory<SlideArea>.Empty,
            Memory<SlideArea>.Empty,
            Memory<SlideArea>.Empty
        }; // 判定队列

        /// <summary>
        /// Slide star prefab
        /// <para>Readonly</para>
        /// </summary>
        [SerializeField]
        [FormerlySerializedAs("_slideStarPrefab")]
        protected GameObject SlideStarPrefab;
        /// <summary>
        /// Arrows
        /// </summary>
        [ReadOnlyField]
        [SerializeField]
        protected readonly PooledList<GameObject> SlideBars = new();
        /// <summary>
        /// Arrow Renderers
        /// </summary>
        [ReadOnlyField]
        [SerializeField]
        protected readonly PooledList<SpriteRenderer> SlideBarRenderers = new();
        protected readonly PooledList<Transform> SlideBarTransforms = new();
        /// <summary>
        /// Slide star
        /// </summary>
        protected readonly Memory<GameObject?> Stars = new GameObject?[3];
        protected readonly Memory<SpriteRenderer?> StarRenderers = new SpriteRenderer?[3];
        protected readonly Memory<Transform?> StarTransforms = new Transform?[3];

        protected SlideOK? SlideOK;

        protected float LastWaitTimeSec;

        [ReadOnlyField, SerializeField]
        protected float FadeInTiming = 0f;
        [ReadOnlyField, SerializeField]
        protected float FadeInCutoffTiming = 0f;
        [ReadOnlyField, SerializeField]
        protected float FadeInCompletedTiming = 0f;
        [ReadOnlyField, SerializeField]
        protected float FadeInDurationTimeSec = 0.2f;
        [ReadOnlyField, SerializeField]
        protected float FadeInMaxAlpha = 0.5f; // 淡入时最大不透明度

        protected int JudgeQueueLength = 0;
        protected int AutoplayLastAreaIndex = 0;
        [ReadOnlyField]
        [SerializeField]
        protected float AutoplayProgress = 0f;
        protected float DJAutoplayProgress = 0;

        protected int LastHiddenSlideBarIndex = 0;
        // Flags
        protected bool IsCheckable = false;
        protected bool IsSoundPlayed = false;
        protected uint SlideBarFadeInFlag = 0;

        [ReadOnlyField, SerializeField]
        private int _multiple = 1;

        private SlideArea?[] _lastTriggerAreas = new SlideArea?[3];

        /// <summary>
        /// The touches DJAuto simulated in its latest run, as centre, radius and pressed sensor bits, recorded only in
        /// the Editor for the slide inspector.
        /// </summary>
        private readonly List<(Vector3 Position, float Radius, ulong PressedSensors)> _djAutoTouches = new();
        /// <summary>The frame of the latest DJAuto run in <see cref="_djAutoTouches"/>, or -1 before the first run.</summary>
        private int _djAutoTouchFrame = -1;

        public abstract void Init();
        protected void MineCheck()
        {
            if (!IsMine || IsJudged)
            {
                return;
            }
            if (ConnectInfo.IsGroupPartEnd || !ConnectInfo.IsConnSlide)
            {
                if (GetTimeSpanToJudgeTiming() >= 0)
                {
                    JudgeResult = JudgeGrade.Perfect;
                    IsJudged = true;
                    HideAllBar();
                }
                else
                {
                    Autoplay();
                }
            }
            else
            {
                if (GetRemainingTimeWithoutOffset() == 0)
                {
                    IsJudged = true;
                    ClearAllJudgeQueue();
                    HideAllBar();
                }
                else
                {
                    Autoplay();
                }
            }            
        }
        void ClearAllJudgeQueue()
        {
            for (var i = 0; i < JudgeQueues.Length; i++)
            {
                ref var queue = ref JudgeQueues[i];
                queue = Memory<SlideArea>.Empty;
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected virtual SensorCheckResult SensorCheck()
        {
            if (AutoplayMode == AutoplayModeOption.Enable || !IsCheckable)
            {
                return default;
            }
            else if (IsEnded || !IsInited)
            {
                return default;
            }
            else if (IsFinished)
            {
                return default;
            }
            Array.Clear(_lastTriggerAreas, 0, _lastTriggerAreas.Length);
            var isAnyAreaTriggered = false;
            for (var x = 0; x < JudgeQueues.Length; x++)
            {
                ref var queueMemory = ref JudgeQueues[x];
                if(queueMemory.IsEmpty)
                {
                    continue;
                }
                
                for (; !queueMemory.IsEmpty;)
                {
                    var queue = queueMemory.Span;
                    ref var first = ref queue[0];
                    ref SlideArea second = ref Unsafe.NullRef<SlideArea>();
                    var fAreas = first.IncludedAreas;

                    if (queue.Length >= 2)
                    {
                        second = ref queue[1];
                    }

                    for (var i = 0; i < fAreas.Length; i++)
                    {
                        var area = fAreas[i];
                        var sensorState = NoteManager.GetSensorStatusInThisFrame(area);
                        first.Check(area, sensorState);
                    }

                    // Check the second area

                    if (!Unsafe.IsNullRef(ref second) && (first.IsSkippable || first.On))
                    {
                        var sAreas = second.IncludedAreas;

                        for (var i = 0; i < sAreas.Length; i++)
                        {
                            var area = sAreas[i];
                            var sensorState = NoteManager.GetSensorStatusInThisFrame(area);
                            second.Check(area, sensorState);
                        }

                        if (second.IsFinished)
                        {
                            queueMemory = queueMemory.Slice(2);
                            _lastTriggerAreas[x] = second;
                            isAnyAreaTriggered = true;
                            continue;
                        }
                        else if (second.On)
                        {
                            queueMemory = queueMemory.Slice(1);
                            _lastTriggerAreas[x] = second;
                            isAnyAreaTriggered = true;
                            continue;
                        }
                    }

                    // Finally check the first area

                    if (first.IsFinished)
                    {
                        queueMemory = queueMemory.Slice(1);
                        _lastTriggerAreas[x] = first;
                        isAnyAreaTriggered = true;
                    }
                    else if (first.On)
                    {
                        _lastTriggerAreas[x] = first;
                        isAnyAreaTriggered = true;
                    }
                    break;
                }
            }
            return new()
            {
                IsAnyAreaTriggered = isAnyAreaTriggered,
                LastTriggerAreas = _lastTriggerAreas
            };
        }

        [Il2CppSetOption(Option.NullChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected sealed override void Judge(float currentSec)
        {
            if (!ConnectInfo.IsGroupPartEnd && ConnectInfo.IsConnSlide)
            {
                return;
            }
            else if (IsJudged)
            {
                return;
            }
            var stayTimeMSec = LastWaitTimeSec * 1000; // 停留时间

            // By Minepig
            var diffSec = currentSec - JudgeTimingWithOffset;
            var isFast = diffSec < 0;
            var diffMSec = MathF.Abs(diffSec) * 1000;
            JudgeDiff = diffSec * 1000;
            // input latency simulation
            //var ext = MathF.Max(0.05f, MathF.Min(stayTime / 4, 0.36666667f));
            var ext = MathF.Min(stayTimeMSec / 4, SLIDE_JUDGE_MAXIMUM_ALLOWED_EXT_LENGTH_MSEC);
            var JUDGE_SEG_3RD_PERFECT_MSEC = SLIDE_JUDGE_SEG_BASE_3RD_PERFECT_MSEC + ext;
            var JUDGE_SEG_1ST_PERFECT_MSEC = JUDGE_SEG_3RD_PERFECT_MSEC * 0.333333f;
            var JUDGE_SEG_2ND_PERFECT_MSEC = JUDGE_SEG_3RD_PERFECT_MSEC * 0.666666f;

            var result = diffMSec switch
            {
                _ when diffMSec <= JUDGE_SEG_1ST_PERFECT_MSEC => JudgeGrade.Perfect,
                _ when diffMSec <= JUDGE_SEG_2ND_PERFECT_MSEC => isFast ? JudgeGrade.FastPerfect2nd : JudgeGrade.LatePerfect2nd,
                _ when diffMSec <= JUDGE_SEG_3RD_PERFECT_MSEC => isFast ? JudgeGrade.FastPerfect3rd : JudgeGrade.LatePerfect3rd,
                <= SLIDE_JUDGE_SEG_1ST_GREAT_MSEC => isFast ? JudgeGrade.FastGreat : JudgeGrade.LateGreat,
                <= SLIDE_JUDGE_SEG_2ND_GREAT_MSEC => isFast ? JudgeGrade.FastGreat2nd : JudgeGrade.LateGreat2nd,
                <= SLIDE_JUDGE_SEG_3RD_GREAT_MSEC => isFast ? JudgeGrade.FastGreat3rd : JudgeGrade.LateGreat3rd,
                _ => isFast ? JudgeGrade.FastGood : JudgeGrade.LateGood
            };

            //MajDebug.Log($"Slide diff : {MathF.Round(diffMSec, 2)} ms");
            ConvertJudgeGrade(ref result);
            JudgeResult = result;
            IsJudged = true;

            var remainingStartTime = ThisFrameSec - ConnectInfo.StartTiming;
            if (remainingStartTime < 0)
            {
                LastWaitTimeSec = MathF.Abs(remainingStartTime) / 2;
            }
            else if (diffMSec >= SLIDE_JUDGE_GOOD_AREA_MSEC && !isFast)
            {
                LastWaitTimeSec = 0.05f;
            }
            if (IsMine)
            {
                if (JudgeResult >= JudgeGrade.Perfect)
                {
                    JudgeResult = JudgeGrade.TooFast;
                }
                else
                {
                    JudgeResult = JudgeGrade.Miss;
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void JudgeClassic(float currentSec)
        {
            if (!ConnectInfo.IsGroupPartEnd && ConnectInfo.IsConnSlide)
            {
                return;
            }
            else if (IsJudged)
            {
                return;
            }
            var diffSec = currentSec - JudgeTimingWithOffset;
            var isFast = diffSec < 0;
            JudgeDiff = diffSec * 1000;
            var diffMSec = MathF.Abs(diffSec) * 1000;
            var judge = JudgeGrade.Miss;
            if (isFast)
            {
                judge = diffMSec switch
                {
                    <= SLIDE_JUDGE_CLASSIC_FAST_SEG_1ST_PERFECT_MSEC => JudgeGrade.Perfect,
                    <= SLIDE_JUDGE_CLASSIC_FAST_SEG_2ND_PERFECT_MSEC => JudgeGrade.FastPerfect2nd,
                    <= SLIDE_JUDGE_CLASSIC_FAST_SEG_3RD_PERFECT_MSEC => JudgeGrade.FastPerfect3rd,
                    <= SLIDE_JUDGE_CLASSIC_FAST_SEG_1ST_GREAT_MSEC => JudgeGrade.FastGreat,
                    <= SLIDE_JUDGE_CLASSIC_FAST_SEG_2ND_GREAT_MSEC => JudgeGrade.FastGreat2nd,
                    <= SLIDE_JUDGE_CLASSIC_FAST_SEG_3RD_GREAT_MSEC => JudgeGrade.FastGreat3rd,
                    _ => JudgeGrade.FastGood
                };
            }
            else
            {
                judge = diffMSec switch
                {
                    <= SLIDE_JUDGE_CLASSIC_LATE_SEG_1ST_PERFECT_MSEC => JudgeGrade.Perfect,
                    <= SLIDE_JUDGE_CLASSIC_LATE_SEG_2ND_PERFECT_MSEC => JudgeGrade.LatePerfect2nd,
                    <= SLIDE_JUDGE_CLASSIC_LATE_SEG_3RD_PERFECT_MSEC => JudgeGrade.LatePerfect3rd,
                    <= SLIDE_JUDGE_CLASSIC_LATE_SEG_1ST_GREAT_MSEC => JudgeGrade.LateGreat,
                    <= SLIDE_JUDGE_CLASSIC_LATE_SEG_2ND_GREAT_MSEC => JudgeGrade.LateGreat2nd,
                    <= SLIDE_JUDGE_CLASSIC_LATE_SEG_3RD_GREAT_MSEC => JudgeGrade.LateGreat3rd,
                    _ => JudgeGrade.LateGood
                };
            }

            //MajDebug.Log($"Slide diff : {MathF.Round(diffMSec, 2)} ms");
            JudgeResult = judge;
            IsJudged = true;

            var remainingStartTime = ThisFrameSec - ConnectInfo.StartTiming;
            if (remainingStartTime < 0)
            {
                LastWaitTimeSec = MathF.Abs(remainingStartTime) / 2;
            }
            if (IsMine)
            {
                if (JudgeResult >= JudgeGrade.Perfect)
                {
                    JudgeResult = JudgeGrade.TooFast;
                }
                else
                {
                    JudgeResult = JudgeGrade.Miss;
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void HideBar(int endIndex)
        {
            endIndex = endIndex - 1;       
            endIndex = Math.Min(endIndex, SlideBars.Count - 1);
            if (endIndex < LastHiddenSlideBarIndex)
            {
                return;
            }
            ref var slideBarRenderersRef = ref MemoryMarshal.GetReference(SlideBarRenderers.AsSpan());
            for (; LastHiddenSlideBarIndex <= endIndex; LastHiddenSlideBarIndex++)
            {
                ref var slideBarRenderer = ref Unsafe.Add(ref slideBarRenderersRef, LastHiddenSlideBarIndex);
                //_slideBarRenderers[i].forceRenderingOff = true;
                slideBarRenderer.enabled = false;
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [MemberNotNullWhen(true, "SlideOK")]
        protected bool PlaySlideOK(in NoteJudgeResult result)
        {
            if (SlideOK is null)
            {
                return false;
            }

            bool canPlay;
            canPlay = NoteEffectManager.CheckJudgeDisplaySetting(MajEnv.Settings.Display.SlideJudgeType, result);

            return canPlay;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void HideAllBar() => HideBar(int.MaxValue);
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SetSlideBarAlpha(float alpha)
        {
            SetSlideBarRender(alpha, null, SLIDE_BAR_MODE_SET_ALPHA);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SetSlideBarMaterial(Material material)
        {
            SetSlideBarRender(0, material, SLIDE_BAR_MODE_SET_MATERIAL);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SetSlideBarAlphaAndMaterial(float alpha, Material material)
        {
            SetSlideBarRender(alpha, material, SLIDE_BAR_MODE_SET_ALPHA_AND_MATERIAL);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetSlideBarRender(float alpha, Material? material, int setMode)
        {
            ref var rendererRef = ref MemoryMarshal.GetReference(SlideBarRenderers.AsSpan());
            var count = SlideBars.Count;
            var newColor = new Color(1f, 1f, 1f, alpha);
            for (var i = 0; i < count; i++)
            {
                ref var sr = ref Unsafe.Add(ref rendererRef, i);
                switch(setMode)
                {
                    case SLIDE_BAR_MODE_SET_ALPHA:
                        if (alpha <= 0f)
                        {
                            sr.enabled = false;
                        }
                        else
                        {
                            sr.enabled = true;
                            sr.color = newColor;
                        }
                        break;
                    case SLIDE_BAR_MODE_SET_MATERIAL:
                        sr.sharedMaterial = material;
                        break;
                    case SLIDE_BAR_MODE_SET_ALPHA_AND_MATERIAL:
                        if (alpha <= 0f)
                        {
                            sr.enabled = false;
                        }
                        else
                        {
                            sr.enabled = true;
                            sr.color = newColor;
                        }
                        sr.sharedMaterial = material;
                        break;
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sealed override void SetActive(bool state)
        {
            base.SetActive(state);
            ref var slideBarRenderersRef = ref MemoryMarshal.GetReference(SlideBarRenderers.AsSpan());
            var count = SlideBarRenderers.Count;
            for (var i = 0; i < count; i++)
            {
                ref var slideBarRenderer = ref Unsafe.Add(ref slideBarRenderersRef, i);
                slideBarRenderer.enabled = state;
            }
            SetStarActive(state);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SetStarActive(bool state)
        {
            var starRenderers = StarRenderers.Span;
            for (var i = 0; i < starRenderers.Length; i++)
            {
                var starRenderer = starRenderers[i];
                if (starRenderer is null)
                {
                    continue;
                }
                starRenderer.enabled = state;
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected sealed override void PlaySFX()
        {
            if (!IsSoundPlayed)
            {
                AudioEffMana.PlaySlideSound(IsBreak);
                IsSoundPlayed = true;
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected sealed override void PlayJudgeSFX(in NoteJudgeResult judgeResult)
        {
            if (judgeResult.IsBreak && judgeResult.Grade == JudgeGrade.Perfect)
            {
                AudioEffMana.PlayBreakSlideEndSound();
            }
        }
        protected virtual void TooLateJudge()
        {
            if (IsJudged)
            {
                End();
                return;
            }
            else if (QueueRemaining == 1)
            {
                JudgeResult = JudgeGrade.LateGood;
            }
            else
            {
                JudgeResult = JudgeGrade.Miss;
            }
            ConvertJudgeGrade(ref JudgeResult);
            IsJudged = true;
            End();
        }
        protected virtual void End()
        {
            if (IsEnded)
            {
                return;
            }
            State = NoteStatus.End;
            if (Parent is not null && !Parent.IsEnded)
            {
                Parent.End();
            }

            SetActive(false);

            if (!ConnectInfo.IsConnSlide || ConnectInfo.IsGroupPartEnd)
            {
                ConvertJudgeGrade(ref JudgeResult);
                if (!ModInfo.SubdivideSlideJudgeGrade)
                {
                    JudgeGradeCorrection(ref JudgeResult);
                }
                var result = new NoteJudgeResult()
                {
                    Grade = JudgeResult,
                    Diff = JudgeDiff,
                    IsEX = IsEX,
                    IsBreak = IsBreak
                };
                // 只有组内最后一个Slide完成 才会显示判定条并增加总数
                ObjectCounter.ReportResult(this, result, Multiple);
                if (PlaySlideOK(result))
                {
                    SlideOK.PlayResult(result);
                }

                PlayJudgeSFX(result);
            }
        }
        /// <summary>
        /// Connection Slide
        /// <para>强制完成该Slide</para>
        /// </summary>
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ForceFinish()
        {
            if (!ConnectInfo.IsConnSlide || ConnectInfo.IsGroupPartEnd)
            { 
                return; 
            }
            HideAllBar();
            var emptyQueue = Memory<SlideArea>.Empty;
            for (var i = 0; i < 3; i++)
            { 
                JudgeQueues[i] = emptyQueue; 
            }
        }
        /// <summary>
        /// Captures the judge queues and the protected judgment state of the slide for the slide inspector.
        /// </summary>
        /// <returns>A new snapshot; capturing allocates, so only editor tools call this.</returns>
        internal SlideDebugInfo CaptureDebugInfo()
        {
            var queues = new List<SlideDebugInfo.QueueInfo>(JudgeQueues.Length);
            for (var i = 0; i < JudgeQueues.Length; i++)
            {
                var remaining = JudgeQueues[i];
                var initial = GetInitialJudgeQueue(i);
                if (remaining.Length > initial.Length)
                {
                    // 判定队列被直接替换过，只能把剩余的部分当作完整的队列
                    initial = remaining;
                }
                if (initial.IsEmpty)
                {
                    continue;
                }
                // 判定队列只会从头部切掉已经通过的判定区，所以剩余部分总是完整队列的后缀
                queues.Add(new SlideDebugInfo.QueueInfo(GetJudgeQueueName(i),
                                                        CaptureAreas(initial.Span),
                                                        initial.Length - remaining.Length));
            }
            var arrowPositions = new Vector3[SlideBarTransforms.Count];
            for (var i = 0; i < arrowPositions.Length; i++)
            {
                var arrowTransform = SlideBarTransforms[i];
                if (arrowTransform != null)
                {
                    arrowPositions[i] = arrowTransform.position;
                }
            }
            var (tableName, tableConst, tableClassicConst) = GetJudgeTableInfo();
            // 星星的移动不含判定偏移，所以用不含偏移的判定时机求正解帧时星星的位置
            var hasJudgeTiming = !ConnectInfo.IsConnSlide || ConnectInfo.IsGroupPartEnd;
            var judgeFrameProgress = Length <= 0f ? 1f : Mathf.Clamp01((JudgeTiming - StartTiming) / Length);

            return new SlideDebugInfo()
            {
                TableName = tableName,
                TableConst = tableConst,
                TableClassicConst = tableClassicConst,
                IsJudged = IsJudged,
                JudgeResult = JudgeResult,
                JudgeDiffMSec = JudgeDiff,
                LastWaitTimeSec = LastWaitTimeSec,
                HasJudgeTiming = hasJudgeTiming,
                JudgeFrameProgress = judgeFrameProgress,
                JudgeFrameStarPositions = hasJudgeTiming ? GetStarPositionsAt(judgeFrameProgress) : Array.Empty<Vector3>(),
                IsCheckable = IsCheckable,
                ArrowPositions = arrowPositions,
                HiddenArrowCount = LastHiddenSlideBarIndex,
                Queues = queues.ToArray(),
                AutoplayMode = AutoplayMode,
                IsDJAutoEnabled = AutoplayMode is AutoplayModeOption.DJAuto_TouchPanel_First or AutoplayModeOption.DJAuto_ButtonRing_First,
                DJAutoplayProgress = DJAutoplayProgress,
                DJAutoTouchFrame = _djAutoTouchFrame,
                DJAutoTouches = CaptureDJAutoTouches()
            };
        }
        /// <summary>Gets a judge queue as it was built, before the player passed any of its areas.</summary>
        /// <param name="index">The index of the queue in <see cref="JudgeQueues"/>.</param>
        /// <returns>The whole queue; the base implementation only knows the areas that are still waiting.</returns>
        private protected virtual Memory<SlideArea> GetInitialJudgeQueue(int index)
        {
            return JudgeQueues[index];
        }
        /// <summary>Gets the name the slide inspector shows for a judge queue.</summary>
        /// <param name="index">The index of the queue in <see cref="JudgeQueues"/>.</param>
        /// <returns>The queue name.</returns>
        private protected virtual string GetJudgeQueueName(int index)
        {
            return index == 0 ? "Judge queue" : $"Judge queue {index}";
        }
        /// <summary>Gets the name and the judgment constants of the judge table, for the slide inspector.</summary>
        /// <returns>The table name, empty when unknown or generated, and the modern and classic constants.</returns>
        private protected virtual (string Name, float Const, float ClassicConst) GetJudgeTableInfo()
        {
            return (string.Empty, 0f, 0f);
        }
        /// <summary>Gets where the stars are once they have moved a share of the path, for the slide inspector.</summary>
        /// <param name="progress">How far along the path, from 0 to 1.</param>
        /// <returns>The world position of each star; the base implementation knows no path and returns an empty array.</returns>
        private protected virtual Vector3[] GetStarPositionsAt(float progress)
        {
            return Array.Empty<Vector3>();
        }
        /// <summary>Captures the areas of a judge queue together with the state of their sensors in this frame.</summary>
        /// <param name="areas">The areas of the queue as it was built.</param>
        /// <returns>The captured areas, in judgment order.</returns>
        private SlideDebugInfo.AreaInfo[] CaptureAreas(ReadOnlySpan<SlideArea> areas)
        {
            var result = new SlideDebugInfo.AreaInfo[areas.Length];
            for (var i = 0; i < areas.Length; i++)
            {
                var area = areas[i];
                var sensors = area.IncludedAreas.ToArray();
                var statuses = NoteManager is null ? Array.Empty<SwitchStatus>() : new SwitchStatus[sensors.Length];
                for (var j = 0; j < statuses.Length; j++)
                {
                    statuses[j] = NoteManager!.GetSensorStatusInThisFrame(sensors[j]);
                }
                result[i] = new SlideDebugInfo.AreaInfo()
                {
                    Sensors = sensors,
                    SensorStatuses = statuses,
                    Policy = area.Policy,
                    IsSkippable = area.IsSkippable,
                    IsOn = area.On,
                    IsFinished = area.IsFinished,
                    ArrowProgressWhenOn = area.ArrowProgressWhenOn,
                    ArrowProgressWhenFinished = area.ArrowProgressWhenFinished
                };
            }
            return result;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void DestroyStars()
        {
            if (Stars.IsEmpty)
            {
                return;
            }
            SetStarActive(false);
            var stars = Stars.Span;
            var starTransforms = StarTransforms.Span;
            var starRenderers = StarRenderers.Span;
            for (var i = 0; i < 3; i++)
            {
                stars[i] = null;
                starTransforms[i] = null;
                starRenderers[i] = null;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SlideBarFadeIn()
        {
            if (SlideBarFadeInFlag == 1 || IsSlideNoTrack || !IsInited || IsEnded)
            {
                return;
            }
            var thisFrameSec = ThisFrameSec;
            if(thisFrameSec < FadeInTiming)
            {
                return;
            }

            if (thisFrameSec > FadeInCompletedTiming)
            {
                SlideBarFadeInFlag = 1;
                var breakMaterial = BreakMaterial;
                if (IsBreak && breakMaterial is not null)
                {
                    SetSlideBarRender(1f, breakMaterial, SLIDE_BAR_MODE_SET_ALPHA_AND_MATERIAL);
                }
                else
                {
                    SetSlideBarRender(1f, null, SLIDE_BAR_MODE_SET_ALPHA);
                }
            }
            else if (thisFrameSec > FadeInCutoffTiming)
            {
                SetSlideBarRender(FadeInMaxAlpha, null, SLIDE_BAR_MODE_SET_ALPHA);
            }
            else
            {
                var diff = FadeInCutoffTiming - thisFrameSec;
                var alpha = 1 - diff / FadeInDurationTimeSec;

                alpha *= FadeInMaxAlpha;
                SetSlideBarRender(alpha, null, SLIDE_BAR_MODE_SET_ALPHA);
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SlideDJAutoSimulateSensorPress(Vector3 cubeRay, float rad)
        {
            var positionSamples = InputManager.TouchPanelPositionSamples;
            if (positionSamples.IsEmpty)
            {
                // 触摸位置采样表尚未生成，没有可以读取的传感器
                RecordDJAutoTouch(cubeRay, rad, 0UL);
                return;
            }
            var p = 0UL;
            var pressedSensors = 0UL;
            unsafe
            {
                fixed (ulong* samplePtr = &positionSamples.GetPinnableReference())
                fixed (Vector4* circleSamplesPtr = &InputManager.UnitCircle.GetPinnableReference())
                {
                    MobileTouchPanelHelper.PositionHandle(cubeRay,
                        rad,
                        0,
                        0,
                        0,
                        0,
                        0,
                        InputManager.FINGER_RADIUS_SEGMENT_LENGTH,
                        InputManager.TOUCH_ANGLE_SMAPLE_COUNT,
                        samplePtr,
                        circleSamplesPtr,
                        ref p);
                }
                for (var i = 0; i < (int)SensorArea.C; i++)
                {
                    if ((p & (1UL << (i + 12))) != 0)
                    {
                        NoteManager.SimulateSensorPress((SensorArea)i);
                        pressedSensors |= 1UL << i;
                    }
                }
                if ((p & (0b11 << (16 + 12))) != 0)
                {
                    NoteManager.SimulateSensorPress(SensorArea.C);
                    pressedSensors |= 1UL << (int)SensorArea.C;
                }
                for (var i = 18; i < 34; i++)
                {
                    if ((p & (1UL << (i + 12))) != 0)
                    {
                        NoteManager.SimulateSensorPress((SensorArea)(i - 1));
                        pressedSensors |= 1UL << (i - 1);
                    }
                }
            }
            RecordDJAutoTouch(cubeRay, rad, pressedSensors);
        }
        /// <summary>
        /// Starts a new DJAuto run in the touch record of the slide inspector, dropping the touches of the previous run.
        /// Only the Editor records touches.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private protected void BeginDJAutoTouchRecord()
        {
            _djAutoTouches.Clear();
            _djAutoTouchFrame = Time.frameCount;
        }
        /// <summary>Adds a touch simulated by DJAuto to the touch record of the slide inspector. Only the Editor records touches.</summary>
        /// <param name="position">The world position of the touch centre.</param>
        /// <param name="radius">The touch radius passed to the touch panel sampling.</param>
        /// <param name="pressedSensors">The pressed sensors, one bit for each <see cref="SensorArea"/> value.</param>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void RecordDJAutoTouch(Vector3 position, float radius, ulong pressedSensors)
        {
            _djAutoTouches.Add((position, radius, pressedSensors));
        }
        /// <summary>Captures the touches DJAuto simulated in its latest run.</summary>
        /// <returns>The touches in the order DJAuto pressed them.</returns>
        private SlideDebugInfo.DJAutoTouchInfo[] CaptureDJAutoTouches()
        {
            var result = new SlideDebugInfo.DJAutoTouchInfo[_djAutoTouches.Count];
            for (var i = 0; i < result.Length; i++)
            {
                var (position, radius, pressedSensors) = _djAutoTouches[i];
                var sensors = new List<SensorArea>();
                for (var sensor = SensorArea.A1; sensor <= SensorArea.E8; sensor++)
                {
                    if ((pressedSensors & (1UL << (int)sensor)) != 0)
                    {
                        sensors.Add(sensor);
                    }
                }
                // 采样按 FINGER_RADIUS_SEGMENT_LENGTH 的整数倍向外取圆环，最外圈会超出给定半径
                var ringCount = Mathf.Ceil(radius / InputManager.FINGER_RADIUS_SEGMENT_LENGTH);
                result[i] = new SlideDebugInfo.DJAutoTouchInfo()
                {
                    Position = position,
                    Radius = radius,
                    SampledRadius = ringCount * InputManager.FINGER_RADIUS_SEGMENT_LENGTH,
                    Sensors = sensors.ToArray()
                };
            }
            return result;
        }
        protected void JudgeGradeCorrection(ref JudgeGrade result)
        {
            switch (result)
            {
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.FastPerfect3rd:
                    result = JudgeGrade.Perfect;
                    break;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected sealed override float GetRemainingTime() => GetRemainingTimeWithoutOffset();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected sealed override float GetRemainingTimeWithoutOffset() => MathF.Max(ArriveTiming - ThisFrameSec, 0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected sealed override float GetTimeSpanToArriveTiming() => ThisFrameSec - ArriveTiming;

        [ReadOnlyField, SerializeField]
        float _startTiming;
        [ReadOnlyField, SerializeField]
        bool _isJustR = false;
        [ReadOnlyField, SerializeField]
        int _endPos = 1;
        [SerializeField]
        string _slideType = string.Empty;
        [ReadOnlyField, SerializeField]
        float _slideLength = 0f;

        protected readonly ref struct SensorCheckResult
        {
            public bool IsAnyAreaTriggered { get; init; }
            public ReadOnlySpan<SlideArea?> LastTriggerAreas { get; init; }
        }
    }
}
