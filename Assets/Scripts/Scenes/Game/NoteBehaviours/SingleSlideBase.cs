using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Settings;
using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal abstract class SingleSlideBase : SlideBase
    {
        [field: SerializeField]
        [field: ReadOnlyField]
        public bool IsMirror { get; set; }

        protected PooledList<Vector3> StarPositions = new();
        protected PooledList<Quaternion> StarRotations = new();

        protected SpriteRenderer StarRenderer;

        protected SlideTable Table;

        protected float DJAutoplayRatio = 1;

        protected static readonly Quaternion s_Z180Rotation = Quaternion.Euler(0f, 0f, 180f);

        [OnPreUpdate]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("SlideDrop.OnPreUpdate"))
            {
                SlideBarFadeIn();
                SlideCheck();
            }
        }
        [OnUpdate]
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("SlideDrop.OnUpdate"))
            {
                var starTransform = StarTransforms.Span[0];

                AutoplayUpdate();
                SensorCheck();
                MineCheck();

                switch (State)
                {
                    case NoteStatus.Inited:
                        SetStarActive(false);
                        if (ThisFrameSec - Timing > 0)
                        {
                            if (!(ConnectInfo.IsConnSlide && !ConnectInfo.IsGroupPartHead))
                            {
                                SetStarActive(true);
                            }

                            StarRenderer.color = new Color(1, 1, 1, 0);
                            starTransform.localScale = new Vector3(0, 0, 1);
                            starTransform.position = StarPositions[0];
                            ApplyStarRotation(StarRotations[0]);
                            State = NoteStatus.Scaling;
                            goto case NoteStatus.Scaling;
                        }
                        break;
                    case NoteStatus.Scaling:
                        var timing = ThisFrameSec - StartTiming;
                        if (timing > 0f)
                        {
                            StarRenderer.color = new Color(1, 1, 1, 1);
                            if (!IsSlideNoHead)
                            {
                                starTransform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                            }
                            SetStarActive(true);

                            State = NoteStatus.Running;
                            goto case NoteStatus.Running;
                        }
                        if (ConnectInfo.IsConnSlide && !ConnectInfo.IsGroupPartHead)
                        {
                            return;
                        }
                        else if (IsSlideNoHead)
                        {
                            return;
                        }
                        // 只有当它是一个起点Slide（而非Slide Group中的子部分）的时候，才会有开始的星星渐入动画
                        var alpha = (1f - -timing / (StartTiming - Timing)).Clamp(0, 1);

                        StarRenderer.color = new Color(1, 1, 1, alpha);
                        if (IsClassic)
                        {
                            var scale = 1 + alpha / 2;
                            starTransform.localScale = new Vector3(scale, scale, scale);
                        }
                        else
                        {
                            starTransform.localScale = new Vector3(alpha + 0.5f, alpha + 0.5f, alpha + 0.5f);
                        }
                        break;
                    case NoteStatus.Running:
                        var remaingTimeWithoutOffset = GetRemainingTimeWithoutOffset();
                        if (remaingTimeWithoutOffset == 0)
                        {
                            starTransform.position = StarPositions[StarPositions.Count - 1];
                            ApplyStarRotation(StarRotations[StarRotations.Count - 1]);
                            if (ConnectInfo.IsConnSlide && !ConnectInfo.IsGroupPartEnd)
                            {
                                DestroyStars();
                            }
                            State = NoteStatus.Arrived;
                            goto case NoteStatus.Arrived;
                        }
                        var process = ((Length - remaingTimeWithoutOffset) / Length).Clamp(0, 1);
                        var indexProcess = (StarPositions.Count - 1) * process;
                        var index = (int)indexProcess;
                        var pos = indexProcess - index;

                        var a = StarPositions[index];
                        var b = StarPositions[index + 1];
                        var newPosition = Vector3.LerpUnclamped(a, b, pos);
                        var newRotation = Quaternion.SlerpUnclamped(
                            StarRotations[index],
                            StarRotations[index + 1],
                            pos
                        );
                        starTransform.position = newPosition;
                        ApplyStarRotation(newRotation);
                        break;
                    case NoteStatus.Arrived:
                        break;
                }
            }
        }
        protected void SlideCheck()
        {
            var thisFrameSec = ThisFrameSec;
            var startTiming = thisFrameSec - Timing;
            var tooLateTiming = StartTiming + _length + (SLIDE_JUDGE_GOOD_AREA_MSEC / 1000) + MathF.Min(USERSETTING_JUDGE_OFFSET_SEC, 0);
            var isTooLate = thisFrameSec - tooLateTiming > 0;

            if (!IsCheckable)
            {
                if (ConnectInfo.IsGroupPart)
                {
                    if (ConnectInfo.IsGroupPartHead && startTiming >= -0.05f)
                    {
                        IsCheckable = true;
                    }
                    else if (!ConnectInfo.IsGroupPartHead)
                    {
                        IsCheckable = ConnectInfo.ParentFinished || ConnectInfo.ParentPendingFinish;
                    }
                }
                else if (startTiming >= -0.05f)
                {
                    IsCheckable = true;
                }
            }

            var isJudgable = ConnectInfo.IsGroupPartEnd || !ConnectInfo.IsConnSlide;

            if (isJudgable)
            {
                if (!IsJudged)
                {
                    if (IsFinished)
                    {
                        HideAllBar();
                        if (IsClassic)
                        {
                            JudgeClassic(thisFrameSec - USERSETTING_TOUCHPANEL_OFFSET_SEC);
                        }
                        else
                        {
                            Judge(thisFrameSec - USERSETTING_TOUCHPANEL_OFFSET_SEC);
                        }
                        return;
                    }
                    else if (isTooLate)
                    {
                        TooLateJudge();
                    }
                }
                else
                {
                    if (LastWaitTimeSec <= 0)
                    {
                        End();
                    }
                    else
                    {
                        LastWaitTimeSec -= MajTimeline.DeltaTime;
                    }
                }
            }
        }

        /// <summary>
        /// 判定队列检查
        /// </summary>
        protected override SensorCheckResult SensorCheck()
        {
            var result = base.SensorCheck();
            if (result.IsAnyAreaTriggered)
            {
                var canPlaySFX = ConnectInfo.IsGroupPartHead || !ConnectInfo.IsConnSlide;
                if (result.LastTriggerAreas[0] is SlideArea area)
                {
                    if (canPlaySFX && area.On)
                    {
                        PlaySFX();
                    }
                    if(area.IsFinished)
                    {
                        HideBar(area.ArrowProgressWhenFinished);
                    }
                    else if(area.On)
                    {
                        HideBar(area.ArrowProgressWhenOn);
                    }
                }
                if (Parent is not null)
                {
                    if (!ConnectInfo.ParentFinished)
                    {
                        Parent.ForceFinish();
                    }
                }
            }
            return result;
        }
        protected void AutoplayUpdate()
        {
            if (!IsAutoplay || IsMine)
            {
                return;
            }
            switch (State)
            {
                case NoteStatus.Running:
                case NoteStatus.Arrived:
                    break;
                default:
                    return;
            }
            switch (AutoplayMode)
            {
                case AutoplayModeOption.Enable:
                    Autoplay();
                    break;
                case AutoplayModeOption.DJAuto_TouchPanel_First:
                case AutoplayModeOption.DJAuto_ButtonRing_First:
                    DJAutoplay();
                    break;
            }
        }
        protected override void Autoplay()
        {
            var process = ((Length - GetRemainingTimeWithoutOffset()) / Length).Clamp(0, 1);
            ref var queueMemory = ref JudgeQueues[0];
            var canPlaySFX = ConnectInfo.IsGroupPartHead || !ConnectInfo.IsConnSlide;
            if (queueMemory.IsEmpty)
            {
                return;
            }
            else if (process >= 1)
            {
                HideAllBar();
                var autoplayGrade = AutoplayGrade;
                if (((int)autoplayGrade).InRange(0, 14))
                {
                    JudgeResult = autoplayGrade;
                }
                else
                {
                    JudgeResult = (JudgeGrade)Randomizer.Next(0, 15);
                }
                IsJudged = true;
                LastWaitTimeSec = 0;
                JudgeDiff = JudgeResult switch
                {
                    < JudgeGrade.Perfect => 1,
                    > JudgeGrade.Perfect => -1,
                    _ => 0
                };
                return;
            }
            else if (process > 0 && canPlaySFX)
            {
                PlaySFX();
            }
            var areaIndex = (int)(process * JudgeQueueLength);
            if (areaIndex < 0)
            {
                return;
            }
            var lastAreaIndex = AutoplayLastAreaIndex;
            if (lastAreaIndex != areaIndex)
            {
                AutoplayLastAreaIndex = areaIndex;
                var remaining = JudgeQueueLength - areaIndex;
                var indexDelta = queueMemory.Length - remaining;
                if (indexDelta > 0)
                {
                    var queue = queueMemory.Span;
                    queueMemory = queueMemory.Slice(indexDelta);
                    HideBar(queue[0].ArrowProgressWhenFinished);
                }
            }

        }
        protected virtual void DJAutoplay()
        {
            const float DJAUTO_SIMULATE_RAD = 0.3f;
            if (IsFinished)
            {
                return;
            }
            var currentProgress = ((Length - GetRemainingTimeWithoutOffset()) / Length).Clamp(0, 1);
            var step = (currentProgress - DJAutoplayProgress) / (8 * DJAutoplayRatio);
            var delta = 0f;
            for (; ; )
            {
                var cubeRay = GetPositionFromProgress(DJAutoplayProgress);
                SlideDJAutoSimulateSensorPress(cubeRay, DJAUTO_SIMULATE_RAD);

                if (delta > 0.2f ||
                   delta + step > 0.2f ||
                   DJAutoplayProgress >= currentProgress)
                {
                    break;
                }
                delta += step;
                DJAutoplayProgress += step;
            }
            DJAutoplayProgress = DJAutoplayProgress.Clamp(0, currentProgress);
        }

        protected override void LoadSkin()
        {
            var barRenderers = SlideBarRenderers;
            var skin = MajInstances.SkinManager.GetSlideSkin();
            var star = Stars.Span[0]!;
            var barSprite = skin.Normal;
            var starSprite = skin.Star.Normal;
            Material? breakMaterial = null;

            if (IsMine)
            {
                barSprite = skin.Mine;
                starSprite = skin.Star.Mine;
                if (IsBreak)
                {
                    barSprite = skin.BreakMine;
                    starSprite = skin.Star.BreakMine;
                    breakMaterial = BreakMaterial;
                }
            }
            else
            {
                if (IsEach)
                {
                    barSprite = skin.Each;
                    starSprite = skin.Star.Each;
                }
                if (IsBreak)
                {
                    barSprite = skin.Break;
                    starSprite = skin.Star.Break;
                    breakMaterial = BreakMaterial;
                }
            }

            foreach (var renderer in barRenderers)
            {
                renderer.color = new Color(1f, 1f, 1f, 0f);
                renderer.sortingOrder = SortOrder--;
                renderer.sortingLayerName = "Slides";

                renderer.sprite = barSprite;
            }

            var starRenderer = star.GetComponent<SpriteRenderer>();
            starRenderer.sprite = starSprite;
            if (breakMaterial is not null)
            {
                starRenderer.sharedMaterial = breakMaterial;
            }

            if (IsJustR)
            {
                SlideOK!.SetR();
            }
            else
            {
                SlideOK!.SetL();
            }
        }


        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected Vector3 GetPositionFromProgress(float progress)
        {
            progress = progress.Clamp(0, 1);
            if (progress == 1)
            {
                return StarPositions[StarPositions.Count - 1];
            }
            var indexProcess = (StarPositions.Count - 1) * progress;
            var index = (int)indexProcess;
            var pos = indexProcess - index;

            var a = StarPositions[index + 1];
            var b = StarPositions[index];
            var ba = a - b;
            var newPos = (ba * pos) + b;

            return newPos;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ApplyStarRotation(in Quaternion newRotation)
        {
            var star = Stars.Span[0];
            var starTransform = StarTransforms.Span[0];
            if (star is null)
            {
                return;
            }

            if (IsMirror)
            {
                starTransform.rotation = newRotation * s_Z180Rotation;
            }
            else
            {
                starTransform.rotation = newRotation;
            }
        }
    }
}
