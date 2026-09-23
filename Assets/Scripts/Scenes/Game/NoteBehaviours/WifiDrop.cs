using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal sealed class WifiDrop : SlideBase, IMajComponent
    {
        readonly Vector3[] _starEndPositions = new Vector3[3];

        Vector3[] _starStartPositions = new Vector3[3];

        WifiTable _wifiTable = SlideTables.GetWifiTable(1);

        protected override void Awake()
        {
            base.Awake();
            EndPos = 5;
            var stars = Stars.Span;
            var starTransforms = StarTransforms.Span;
            var starRenderers = StarRenderers.Span;

            var slideParent = NoteManager.transform.GetChild(3);
            var centerStar = Instantiate(SlideStarPrefab, slideParent);
            var leftStar = Instantiate(SlideStarPrefab, slideParent);
            var rightStar = Instantiate(SlideStarPrefab, slideParent);

            var sensorPos = (SensorArea)(EndPos - 1);
            var rIndex = sensorPos.Diff(-1).GetIndex();
            var lIndex = sensorPos.Diff(1).GetIndex();

            rightStar.SetActive(true);
            centerStar.SetActive(true);
            leftStar.SetActive(true);
            stars[0] = rightStar;
            stars[1] = centerStar;
            stars[2] = leftStar;
            starRenderers[0] = stars[0]!.GetComponent<SpriteRenderer>();
            starRenderers[1] = stars[1]!.GetComponent<SpriteRenderer>();
            starRenderers[2] = stars[2]!.GetComponent<SpriteRenderer>();
            JudgeQueues[0] = _wifiTable.Left;
            JudgeQueues[1] = _wifiTable.Center;
            JudgeQueues[2] = _wifiTable.Right;
            _starEndPositions[0] = NoteHelper.GetTapPosition(rIndex, 4.8f);// R
            _starEndPositions[1] = NoteHelper.GetTapPosition(EndPos, 4.8f);// Center
            _starEndPositions[2] = NoteHelper.GetTapPosition(lIndex, 4.8f); // L

            if (IsClassic)
            {
                _starStartPositions[0] = NoteHelper.GetTapPosition(StartPos + 0.11f, 4.55f);
                _starStartPositions[1] = NoteHelper.GetTapPosition(StartPos, 4.8f);
                _starStartPositions[2] = NoteHelper.GetTapPosition(StartPos - 0.13f, 4.55f);

                starRenderers[0].sortingOrder = -1;
                starRenderers[2].sortingOrder = -1;
            }
            else
            {
                _starStartPositions[0] = NoteHelper.GetTapPosition(StartPos, 4.8f);
                _starStartPositions[1] = NoteHelper.GetTapPosition(StartPos, 4.8f);
                _starStartPositions[2] = NoteHelper.GetTapPosition(StartPos, 4.8f);
            }

            var slideOK = transform.GetChild(transform.childCount - 1).gameObject; //slideok is the last one
            slideOK.SetActive(true);
            SlideOK = slideOK.GetComponent<SlideOK>();
            SlideOK.IsClassic = IsClassic;
            SlideOK.Shape = NoteHelper.GetSlideOKShapeFromSlideType("wifi");

            //Transform.rotation = Quaternion.Euler(0f, 0f, -45f * (StartPos - 1));

            for (var i = 0; i < Transform.childCount - 1; i++)
            {
                SlideBars.Add(Transform.GetChild(i).gameObject);
                SlideBarTransforms.Add(SlideBars[i].transform);
                SlideBarRenderers.Add(SlideBars[i].GetComponent<SpriteRenderer>());
            }

            SetActive(false);
            SetStarActive(false);
            SetSlideBarAlpha(0f);

            for (var i = 0; i < Stars.Length; i++)
            {
                var star = stars[i];
                if (star is null)
                {
                    continue;
                }
                starTransforms[i] = star.transform;
                star.transform.position = _starStartPositions[i];
                star.transform.localScale = new Vector3(0f, 0f, 1f);
            }
            SlideLength = 20;
        }
        public override void Init()
        {
            if (State >= NoteStatus.Inited)
            {
                return;
            }
            
            _wifiTable.Dispose();
            _wifiTable = SlideTables.GetWifiTable(StartPos);
            var wifiConst = _wifiTable.Const;
            JudgeQueueLength = 4;

            JudgeQueues[0] = _wifiTable.Left;
            JudgeQueues[1] = _wifiTable.Center;
            JudgeQueues[2] = _wifiTable.Right;

            JudgeTiming = StartTiming + Length * (1 - wifiConst);
            LastWaitTimeSec = Length * wifiConst;

            // 计算Slide淡入时机
            // 在8.0速时应当提前300ms显示Slide
            FadeInTiming = -3.926913f / Speed;
            var fadeInOffset = 0f;
            if (Settings.Debug.OffsetUnit == OffsetUnitOption.Second)
            {
                fadeInOffset = Settings.Game.SlideFadeInOffset;
            }
            else
            {
                fadeInOffset = Settings.Game.SlideFadeInOffset * MajEnv.FRAME_LENGTH_SEC;
            }
            FadeInMaxAlpha = 1f;
            FadeInTiming += fadeInOffset;
            FadeInTiming += Timing;
            FadeInCompletedTiming = Timing - 0.05f;
            // Slide完全淡入时机
            // 正常情况下应为负值；速度过高将忽略淡入
            FadeInDurationTimeSec = (FadeInCompletedTiming - FadeInTiming).Clamp(0, 0.2f);
            FadeInCutoffTiming = FadeInTiming + FadeInDurationTimeSec;
            //淡入时机与正解帧间隔小于200ms时，加快淡入动画的播放速度
            //fadeInAnimator.speed = 0.2f / interval;
            //fadeInAnimator.SetTrigger("wifi");
            var sensorPos = (SensorArea)(EndPos - 1);
            var rIndex = sensorPos.Diff(-1).GetIndex();
            var lIndex = sensorPos.Diff(1).GetIndex();
            _starEndPositions[0] = NoteHelper.GetTapPosition(rIndex, 4.8f);// R
            _starEndPositions[1] = NoteHelper.GetTapPosition(EndPos, 4.8f);// Center
            _starEndPositions[2] = NoteHelper.GetTapPosition(lIndex, 4.8f); // L

            if (IsClassic)
            {
                _starStartPositions[0] = NoteHelper.GetTapPosition(StartPos + 0.11f, 4.55f);
                _starStartPositions[1] = NoteHelper.GetTapPosition(StartPos, 4.8f);
                _starStartPositions[2] = NoteHelper.GetTapPosition(StartPos - 0.13f, 4.55f);
            }
            else
            {
                _starStartPositions[0] = NoteHelper.GetTapPosition(StartPos, 4.8f);
                _starStartPositions[1] = NoteHelper.GetTapPosition(StartPos, 4.8f);
                _starStartPositions[2] = NoteHelper.GetTapPosition(StartPos, 4.8f);
            }

            Transform.rotation = Quaternion.Euler(0f, 0f, -45f * (StartPos - 1));

            LoadSkin();
            SlideOK!.transform.SetParent(transform.parent);
            var stars = Stars.Span;
            var starTransforms = StarTransforms.Span;
            for (var i = 0; i < stars.Length; i++)
            {
                var star = stars[i];
                if (star is null)
                {
                    continue;
                }
                starTransforms[i] = star.transform;
                star.transform.position = _starStartPositions[i];
                star.transform.localScale = new Vector3(0f, 0f, 1f);
            }

            if(!USERSETTING_SLIDE_SKIPPING)
            {
                for (var i = 0; i < JudgeQueues.Length; i++)
                {
                    var queueMemory = JudgeQueues[i];
                    var queue = queueMemory.Span;
                    for (var j = 0; j < queue.Length; j++)
                    {
                        ref var area = ref queue[j];
                        area.IsSkippable = false;
                    }
                }
            }

            State = NoteStatus.Inited;
        }
        protected override SensorCheckResult SensorCheck()
        {
            var result = base.SensorCheck();
            if (result.IsAnyAreaTriggered)
            {
                HideBar(GetIndex());
                PlaySFX();
            }
            return result;
        }

        void SlideCheck()
        {
            var thisFrameSec = ThisFrameSec;
            var startTiming = thisFrameSec - Timing;
            var tooLateTiming = StartTiming + Length + SLIDE_JUDGE_GOOD_AREA_MSEC / 1000 + MathF.Min(USERSETTING_JUDGE_OFFSET_SEC, 0);
            var isTooLate = thisFrameSec - tooLateTiming > 0;

            if (startTiming >= -0.05f)
            {
                IsCheckable = true;
            }

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
        int GetIndex()
        {
            if (JudgeQueues.IsEmpty())
            {
                return int.MaxValue;
            }
            else if (IsClassic)
            {
                var isRemainingOne = JudgeQueues.All(x => x.Length <= 1);
                if (isRemainingOne)
                {
                    return 8;
                }
            }
            else if (JudgeQueues[1].IsEmpty)
            {
                if (JudgeQueues[0].Length <= 1 && JudgeQueues[2].Length <= 1)
                {
                    return 9;
                }
            }
            var maxRemaing = 0;
            var index2Hide = int.MaxValue;
            for (var i = 0; i < JudgeQueues.Length; i++)
            {
                var queue = JudgeQueues[i];
                if(queue.Length > maxRemaing)
                {
                    maxRemaing = queue.Length;
                    index2Hide = queue.Span[0].ArrowProgressWhenFinished;
                }
            }

            return index2Hide;
        }
        [OnPreUpdate]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("WifiDrop.OnPreUpdate"))
            {
                SlideBarFadeIn();
                SlideCheck();
            }
        }
        [OnUpdate]
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("WifiDrop.OnUpdate"))
            {
                AutoplayUpdate();
                SensorCheck();
                MineCheck();

                var stars = Stars.Span;
                var starTransforms = StarTransforms.Span;
                var starRenderers = StarRenderers.Span;
                switch (State)
                {
                    case NoteStatus.Inited:
                        SetStarActive(false);
                        if (ThisFrameSec - Timing > 0)
                        {
                            SetStarActive(true);
                            for (var i = 0; i < stars.Length; i++)
                            {
                                var starTransform = starTransforms[i];

                                starTransform.position = _starStartPositions[i];
                            }
                            State = NoteStatus.Scaling;
                            goto case NoteStatus.Scaling;
                        }
                        break;
                    case NoteStatus.Scaling:
                        var timing = ThisFrameSec - StartTiming;
                        if (timing > 0f)
                        {
                            for (var i = 0; i < stars.Length; i++)
                            {
                                var starTransform = starTransforms[i];

                                starRenderers[i].color = new Color(1, 1, 1, 1);
                                if (!IsSlideNoHead)
                                {
                                    starTransform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                                }
                            }
                            State = NoteStatus.Running;
                            goto case NoteStatus.Running;
                        }
                        else if (IsSlideNoHead)
                        {
                            return;
                        }
                        var alpha = (1f - -timing / (StartTiming - Timing)).Clamp(0, 1);

                        for (var i = 0; i < stars.Length; i++)
                        {
                            var starTransform = starTransforms[i];

                            starRenderers[i].color = new Color(1, 1, 1, alpha);
                            if (IsClassic)
                            {
                                var scale = 1 + alpha / 2;
                                starTransform.localScale = new Vector3(scale, scale, scale);
                            }
                            else
                            {
                                starTransform.localScale = new Vector3(alpha + 0.5f, alpha + 0.5f, alpha + 0.5f);
                            }
                        }
                        break;
                    case NoteStatus.Running:
                        var remaingTimeWithoutOffset = GetRemainingTimeWithoutOffset();
                        if (remaingTimeWithoutOffset == 0)
                        {
                            for (var i = 0; i < stars.Length; i++)
                            {
                                var starTransform = starTransforms[i];
                                starTransform.position = _starEndPositions[i];
                            }
                            State = NoteStatus.Arrived;
                            goto case NoteStatus.Arrived;
                        }
                        var process = (Length - remaingTimeWithoutOffset) / Length;

                        for (var i = 0; i < stars.Length; i++)
                        {
                            var starTransform = starTransforms[i];
                            var a = _starStartPositions[i];
                            var b = _starEndPositions[i];
                            var newPos = Vector3.Lerp(a, b, process);

                            starTransform.position = newPos; //TODO add some runhua
                        }
                        break;
                    case NoteStatus.Arrived:
                        break;
                }
            }
        }
        void AutoplayUpdate()
        {
            if (!IsAutoplay)
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
                case AutoplayModeOption.DJAuto_ButtonRing_First:
                case AutoplayModeOption.DJAuto_TouchPanel_First:
                    DJAutoplay();
                    break;
            }
        }
        protected override void Autoplay()
        {
            var process = ((Length - GetRemainingTimeWithoutOffset()) / Length).Clamp(0, 1);
            var queues = JudgeQueues.AsSpan();
            if (QueueRemaining == 0)
            {
                return;
            }
            else if (process >= 1)
            {
                AutoplayProgress = 1;
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
            else if (process > 0)
            {
                PlaySFX();
            }
            AutoplayProgress = process;
            var areaIndex = (int)(process * JudgeQueueLength) - 1;
            if (areaIndex < 0)
            {
                return;
            }
            var lastAreaIndex = AutoplayLastAreaIndex;
            if (lastAreaIndex != areaIndex)
            {
                AutoplayLastAreaIndex = areaIndex;
                var remaining = JudgeQueueLength - areaIndex;
                for(var i = 0; i < queues.Length; i++)
                {
                    ref var queueMemory = ref queues[i];
                    var indexDelta = queueMemory.Length - remaining;
                    if (indexDelta > 0)
                    {
                        queueMemory = queueMemory.Slice(indexDelta);
                    }
                }                
            }
            if(process > 0.7)
            {
                HideBar(9);
            }
            else if (process > 0.4)
            {
                HideBar(5);
            }
            else if (process > 0.15)
            {
                HideBar(2);
            }
        }
        void DJAutoplay()
        {
            if (IsFinished)
            {
                return;
            }
            var currentProgress = ((Length - GetRemainingTimeWithoutOffset()) / Length).Clamp(0, 1);
            var startPos = NoteHelper.GetTapPosition(StartPos, 4.8f);
            var step = (currentProgress - DJAutoplayProgress) / 4;
            for (; ; DJAutoplayProgress += step)
            {
                for (var j = 0; j < 3; j++)
                {
                    float rad;
                    if(j == 1)
                    {
                        rad = 0.8f;
                    }
                    else
                    {
                        rad = 0.15f;
                    }
                    var pos = (_starEndPositions[j] - startPos) * DJAutoplayProgress.Clamp(0, 1) + startPos;
                    SlideDJAutoSimulateSensorPress(pos, rad);
                }
                if(DJAutoplayProgress >= currentProgress)
                {
                    break;
                }
            }
            DJAutoplayProgress = DJAutoplayProgress.Clamp(0, currentProgress);
        }

        protected override void LoadSkin()
        {
            var barRenderers = SlideBarRenderers;
            var skin = MajInstances.SkinManager.GetWifiSkin();
            var starRenderers = StarRenderers.Span;
            var barSprites = skin.Normal;
            var starSprite = skin.Star.Normal;
            Material? breakMaterial = null;

            if (IsMine)
            {
                barSprites = skin.Mine;
                starSprite = skin.Star.Mine;
                if (IsBreak)
                {
                    barSprites = skin.BreakMine;
                    starSprite = skin.Star.BreakMine;
                    breakMaterial = BreakMaterial;
                }
            }
            else
            {
                if (IsEach)
                {
                    barSprites = skin.Each;
                    starSprite = skin.Star.Each;
                }
                if (IsBreak)
                {
                    barSprites = skin.Break;
                    starSprite = skin.Star.Break;
                    breakMaterial = BreakMaterial;
                }
            }
            foreach (var (i, renderer) in barRenderers.WithIndex())
            {
                renderer.color = new Color(1f, 1f, 1f, 0f);
                renderer.sortingOrder = SortOrder--;
                renderer.sortingLayerName = "Slides";

                renderer.sprite = barSprites[i];
            }
            foreach (var (i, star) in Stars.Span.WithIndex())
            {
                var starRenderer = starRenderers[i];
                starRenderer.sprite = starSprite;
                if (breakMaterial is not null)
                {
                    starRenderer.sharedMaterial = breakMaterial;
                }
                star!.transform.rotation = Quaternion.Euler(0, 0, -22.5f * (8 + i + 2 * (StartPos - 1)));
            }


            if (IsJustR)
            {
                SlideOK!.SetR();
            }
            else
            {
                SlideOK!.SetL();
                SlideOK!.transform.Rotate(new Vector3(0f, 0f, 180f));
            }
        }
        void OnDestroy()
        {
            _wifiTable.Dispose();
        }
    }
}