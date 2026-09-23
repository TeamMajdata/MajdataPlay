using MajdataPlay.Extensions;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using System;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal sealed class SlideDrop : SingleSlideBase, IConnectableSlide, IMajComponent
    {
        public Quaternion FinalStarAngle { get; private set; } = default;


        protected override void Awake()
        {
            base.Awake();
            var star = Instantiate(SlideStarPrefab, NoteManager.transform.GetChild(3));
            var slideTable = SlideTables.FindTableByName(SlideType);

            if (slideTable is null)
            {
                throw new MissingComponentException($"Slide table of \"{SlideType}\" is not found");
            }

            Table = slideTable;
            JudgeQueues[0] = Table.JudgeQueue;
            EndPos = SlideType switch
            {
                "line3" => 3,
                "line4" => 4,
                "line5" => 5,
                "line6" => 6,
                "line7" => 7,
                "circle1" => 2,
                "circle2" => 3,
                "circle3" => 4,
                "circle4" => 5,
                "circle5" => 6,
                "circle6" => 7,
                "circle7" => 8,
                "circle8" => 1,
                "v1" => 1,
                "v2" => 2,
                "v3" => 3,
                "v4" => 4,
                "v6" => 6,
                "v7" => 7,
                "v8" => 8,
                "ppqq1" => 1,
                "ppqq2" => 2,
                "ppqq3" => 3,
                "ppqq4" => 4,
                "ppqq5" => 5,
                "ppqq6" => 6,
                "ppqq7" => 7,
                "ppqq8" => 8,
                "pq1" => 1,
                "pq2" => 2,
                "pq3" => 3,
                "pq4" => 4,
                "pq5" => 5,
                "pq6" => 6,
                "pq7" => 7,
                "pq8" => 8,
                "s" => 5,
                "L2" => 2,
                "L3" => 3,
                "L4" => 4,
                "L5" => 5,
                _ => 1
            };
            var stars = Stars.Span;
            var starTransforms = StarTransforms.Span;
            var starRenderers = StarRenderers.Span;

            star.SetActive(true);
            stars[0] = star;
            starTransforms[0] = star.transform;
            StarRenderer = star.GetComponent<SpriteRenderer>();
            starRenderers[0] = StarRenderer;

            var slideOK = transform.GetChild(transform.childCount - 1).gameObject; //slideok is the last one
            slideOK.SetActive(true);
            SlideOK = slideOK.GetComponent<SlideOK>();
            SlideOK.IsClassic = IsClassic;
            SlideOK.Shape = NoteHelper.GetSlideOKShapeFromSlideType(SlideType);


            for (var i = 0; i < Transform.childCount - 1; i++)
            {
                SlideBars.Add(Transform.GetChild(i).gameObject);
                SlideBarRenderers.Add(SlideBars[i].GetComponent<SpriteRenderer>());
                SlideBarTransforms.Add(SlideBars[i].transform);
                SlideBarTransforms[i].localScale *= USERSETTING_SLIDE_SCALE;
            }
            LoadSlidePath();
            SetActive(false);
            SetStarActive(false);
            SetSlideBarAlpha(0f);
            SlideLength = SlideBars.Count + 1;

            starTransforms[0].position = StarPositions[0];
            starTransforms[0].transform.localScale = new Vector3(0f, 0f, 1f);
        }
        /// <summary>
        /// Slide初始化
        /// </summary>
        public override void Init()
        {
            if (IsInited)
            {
                return;
            }
            if (IsMirror)
            {
                Table.Mirror();
                Transform.localScale = new Vector3(-1f, 1f, 1f);
                Transform.rotation = Quaternion.Euler(0f, 0f, -45f * StartPos);
                SlideOK!.transform.localScale = new Vector3(-1f, 1f, 1f);
            }
            else
            {
                Transform.rotation = Quaternion.Euler(0f, 0f, -45f * (StartPos - 1));
            }

            var diff = Math.Abs(1 - StartPos);
            if (diff != 0)
            {
                Table.Diff(diff);
            }

            LoadSlidePath();
            LoadSkin();
            SlideOK!.transform.SetParent(Transform.parent);
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
            FadeInTiming += fadeInOffset;
            FadeInTiming += Timing;
            FadeInCompletedTiming = Timing - 0.05f;
            // Slide完全淡入时机
            // 正常情况下应为负值；速度过高将忽略淡入
            FadeInDurationTimeSec = (FadeInCompletedTiming - FadeInTiming).Clamp(0, 0.2f);
            FadeInCutoffTiming = FadeInTiming + FadeInDurationTimeSec;
            //var interval = fullFadeInTiming - fadeInTiming;
            //fadeInAnimator = GetComponent<Animator>();
            //Destroy(GetComponent<Animator>());
            //淡入时机与正解帧间隔小于200ms时，加快淡入动画的播放速度
            //fadeInAnimator.speed = 0.2f / interval;
            //fadeInAnimator.SetTrigger("slide");
            var starTransforms = StarTransforms.Span;
            starTransforms[0].position = StarPositions[0];
            starTransforms[0].transform.localScale = new Vector3(0f, 0f, 1f);
            JudgeQueues[0] = Table.JudgeQueue;
            JudgeQueueLength = Table.JudgeQueue.Length;

            InitializeSlideGroup();

            if (ConnectInfo.IsConnSlide && !ConnectInfo.IsGroupPartEnd)
            {
                Destroy(SlideOK);
                SlideOK = null;
            }

            State = NoteStatus.Inited;
            DJAutoplayRatio = SlideLength / 14;
//#if UNITY_EDITOR
//            var obj = Instantiate(_slideBars[0]);
//            Destroy(obj.GetComponent<SpriteRenderer>());
//            _judgeFramePoint = obj.transform;
//            _runtimeSlideConst = IsClassic ? _table.ClassicConst : _table.Const;
//            var indexProcess = (_starPositions.Count - 1) * (1 - _runtimeSlideConst);
//            var index = (int)indexProcess;
//            var pos = indexProcess - index;

//            var a = _starPositions[index + 1];
//            var b = _starPositions[index];
//            var ba = a - b;
//            var newPos = ba * pos + b;

//            _judgeFramePoint.position = newPos;
//#endif
        }
        private void InitializeSlideGroup()
        {
            var judgeQueue = JudgeQueues[0].Span;

            if (ConnectInfo.IsConnSlide && ConnectInfo.IsGroupPartEnd)
            {
                judgeQueue[judgeQueue.Length - 1].SetIsLast();
            }
            else if (ConnectInfo.IsConnSlide)
            {
                judgeQueue[judgeQueue.Length - 1].SetNonLast();
            }

            if (ConnectInfo.IsConnSlide)
            {
                //Length = ConnectInfo.TotalLength / ConnectInfo.TotalSlideLen * SlideLength;
                if (!ConnectInfo.IsGroupPartHead)
                {
                    if (Parent is null)
                    {
                        throw new NullReferenceException();
                    }
                    var parent = Parent.GameObject.GetComponent<SlideDrop>();
                    StartTiming = parent.StartTiming + parent.Length;
                }
            }
            UpdateJudgeQueue();

            if (ConnectInfo.IsGroupPartEnd || !ConnectInfo.IsConnSlide)
            {
                var percent = Table.Const;
                if (IsClassic)
                {
                    percent = Table.ClassicConst;
                }
                JudgeTiming = StartTiming + Length * (1 - percent);
                LastWaitTimeSec = Length * percent;
            }
        }
        private void UpdateJudgeQueue()
        {
            var judgeQueue = JudgeQueues[0].Span;
            if (!USERSETTING_SLIDE_SKIPPING)
            {
                foreach (ref var judgeArea in judgeQueue)
                {
                    judgeArea.IsSkippable = false;
                }
            }
            else
            {
                if (ConnectInfo.IsConnSlide)
                {
                    if (ConnectInfo.TotalJudgeQueueLen < 4)
                    {
                        if (ConnectInfo.IsGroupPartHead)
                        {
                            judgeQueue[0].IsSkippable = true;
                            judgeQueue[1].IsSkippable = false;
                        }
                        else if (ConnectInfo.IsGroupPartEnd)
                        {
                            judgeQueue[0].IsSkippable = false;
                            judgeQueue[1].IsSkippable = true;
                        }
                    }
                    else
                    {
                        foreach (ref var judgeArea in judgeQueue)
                        {
                            judgeArea.IsSkippable = true;
                        }
                    }
                }
            }     
        }

        private void LoadSlidePath()
        {
            StarPositions.Clear();
            StarRotations.Clear();
            if (StartPos == 0)
            {
                StartPos = 1;
            }
            StarPositions.Add(NoteHelper.GetTapPosition(StartPos, 4.8f));
            StarRotations.Add(Quaternion.Euler(SlideBars[0].transform.rotation.normalized.eulerAngles + new Vector3(0f, 0f, 18f)));
            for (var i = 0; i < SlideBars.Count; i++)
            {
                var bar = SlideBars[i];
                StarPositions.Add(bar.transform.position);

                StarRotations.Add(Quaternion.Euler(SlideBars[i].transform.rotation.normalized.eulerAngles + new Vector3(0f, 0f, 18f)));
                if (i == SlideBars.Count - 1)
                {
                    var a = SlideBars[i - 1].transform.rotation.normalized.eulerAngles;
                    var b = bar.transform.rotation.normalized.eulerAngles;
                    var diff = a - b;
                    var newEulerAugle = b - diff;
                    StarRotations.Add(Quaternion.Euler(newEulerAugle + new Vector3(0f, 0f, 18f)));
                }
            }
            var endPos = NoteHelper.GetTapPosition(EndPos, 4.8f);
            StarPositions.Add(endPos);
            FinalStarAngle = StarRotations[StarRotations.Count - 1];
            if (ConnectInfo.IsConnSlide)
            {
                var parent = ConnectInfo.Parent;
                if (parent is not null)
                {
                    StarRotations[0] = parent.FinalStarAngle;
                }
            }
        }

        protected override void LoadSkin()
        {
            base.LoadSkin();
            if (IsJustR)
            {
                if (SlideOK!.Shape == SlideOKShape.Str && IsMirror)
                {
                    SlideOK!.transform.Rotate(new Vector3(0f, 0f, 180f));
                    var angel = SlideOK.transform.rotation.eulerAngles.z * Mathf.Deg2Rad;
                    SlideOK.transform.position += new Vector3(Mathf.Sin(angel) * 0.27f, Mathf.Cos(angel) * -0.27f);
                }
            }
            else
            {
                if (SlideOK!.Shape == SlideOKShape.Str && !IsMirror)
                {
                    SlideOK!.transform.Rotate(new Vector3(0f, 0f, 180f));
                    var angel = SlideOK.transform.rotation.eulerAngles.z * Mathf.Deg2Rad;
                    SlideOK.transform.position += new Vector3(Mathf.Sin(angel) * 0.27f, Mathf.Cos(angel) * -0.27f);
                }
            }
        }

        void OnDestroy()
        {
            Table?.Dispose();
            StarPositions.Dispose();
            StarRotations.Dispose();
        }

        void IConnectableSlide.End()
        {
            End();
        }
    }
}