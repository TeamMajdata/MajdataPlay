using MajdataPlay.Buffers;
using MajdataPlay.Editor;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal sealed class ExtendSlideDrop : SingleSlideBase
    {
        [field: ReadOnlyField]
        [field: SerializeField]
        public ExtendSlideMetadata? Metadata { get; set; }
        /// <summary>
        /// Gets or sets the judge table predefined in <see cref="Slide.Utils.SlideTables"/> for the shape of this slide,
        /// already mirrored and rotated to its start key. When <see langword="null"/>, <see cref="Init"/> generates the
        /// table from <see cref="Metadata"/>. The slide owns the table and disposes it when it is destroyed.
        /// </summary>
        public SlideTable? PredefinedTable { get; set; }
        /// <summary>
        /// Gets or sets the slide code the slide was parsed from, after chart rotation, for the slide inspector; empty when
        /// the slide was built from metadata directly.
        /// </summary>
        public string SlideCode { get; set; } = string.Empty;


        protected override void Awake()
        {
            base.Awake();
            var star = Instantiate(SlideStarPrefab, NoteManager.transform.GetChild(3));
            var stars = Stars.Span;
            var starTransforms = StarTransforms.Span;
            var starRenderers = StarRenderers.Span;

            star.SetActive(true);
            stars[0] = star;
            starTransforms[0] = star.transform;
            StarRenderer = star.GetComponent<SpriteRenderer>();
            // SetStarActive hides the star through StarRenderers, as in SlideDrop.
            starRenderers[0] = StarRenderer;

            var slideOK = transform.GetChild(transform.childCount - 1).gameObject; //slideok is the last one
            slideOK.SetActive(true);
            SlideOK = slideOK.GetComponent<SlideOK>();
            SlideOK.IsClassic = IsClassic;
            SlideOK.transform.SetParent(Transform.parent);

            NoteHelper.Sorting.SetPosition(starTransforms[0], Vector3.zero);
            starTransforms[0].localScale = new Vector3(0f, 0f, 1f);
        }
        public override void Init()
        {
            if(Metadata is not ExtendSlideMetadata metadata)
            {
                throw new InvalidOperationException("");
            }
            // Load slide path
            InitArrowAndPath(metadata);

            // Init slide judge area queue
            InitSlideTable(metadata);

            // Init SlideOk
            InitSlideOk(metadata);

            LoadSkin();

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

            var starTransforms = StarTransforms.Span;
            NoteHelper.Sorting.SetPosition(starTransforms[0], StarPositions[0]);
            starTransforms[0].transform.localScale = new Vector3(0f, 0f, 1f);

            SetActive(false);
            SetStarActive(false);
            SetSlideBarAlpha(0f);
            SlideLength = SlideBars.Count + 1;

            State = NoteStatus.Inited;
            DJAutoplayRatio = SlideLength / 14;
        }

        /// <summary>
        /// Uses the predefined table, or generates one, applies the slide skipping setting and computes the judge timing.
        /// </summary>
        /// <param name="metadata">The parsed slide.</param>
        private void InitSlideTable(ExtendSlideMetadata metadata)
        {
            Table = PredefinedTable ?? BuildSlideTable(metadata);
            JudgeQueues[0] = Table.JudgeQueue;
            JudgeQueueLength = Table.JudgeQueue.Length;
            if (!USERSETTING_SLIDE_SKIPPING)
            {
                var judgeQueue = Table.JudgeQueue.Span;
                foreach (ref var judgeArea in judgeQueue)
                {
                    judgeArea.IsSkippable = false;
                }
            }

            var percent = IsClassic ? Table.ClassicConst : Table.Const;
            JudgeTiming = StartTiming + (Length * (1 - percent));
            LastWaitTimeSec = Length * percent;
        }
        /// <summary>Generates the judge table from the judge areas found by the slide code algorithm.</summary>
        /// <param name="metadata">The parsed slide.</param>
        /// <returns>The generated table.</returns>
        private static SlideTable BuildSlideTable(ExtendSlideMetadata metadata)
        {
            using var areas = new PooledList<SlideArea>();
            var parsingAreas = metadata.JudgeAreaQueue;
            var areaQueue = (stackalloc (SensorArea, bool)[2]);
            for (var i = 0; i < parsingAreas.Length; i++)
            {
                var parsingArea = parsingAreas[i];
                var isLast = i == parsingAreas.Length - 1;
                var length = 1;
                areaQueue[0] = (parsingArea.SensorA, isLast);
                if(parsingArea.SensorB is SensorArea sArea)
                {
                    areaQueue[1] = (sArea, isLast);
                    length = 2;
                }
                areas.Add(new SlideArea(areaQueue.Slice(0, length),
                          parsingArea.ArrowProgressPush,
                          parsingArea.ArrowProgressFinish)
                    );
            }
            if (areas.Count <= 3)
            {
                var @as = areas.AsSpan();
                ref var area = ref @as[1];
                if(areas.Count == 2)
                {
                    area = ref @as[0];
                }
                area.IsSkippable = false;
            }

            var rentedBuffer = Pool<SlideArea>.Rent(areas.Count);
            areas.CopyTo(rentedBuffer);

            return new SlideTable(rentedBuffer, areas.Count)
            {
                Const = metadata.SlideConst,
                ClassicConst = metadata.SlideConst,
            };
        }
        private void InitArrowAndPath(ExtendSlideMetadata metadata)
        {
            var arrowPrefab = RuntimeDatabase.Note.Prefab.SlideArrow;
            StartPos = ((int)metadata.JudgeAreaQueue[0].SensorA) + 1;
            EndPos = ((int)metadata.JudgeAreaQueue[metadata.JudgeAreaQueue.Length - 1].SensorA) + 1;

            for (var i = 0; i < metadata.ArrowPoses.Length; i++)
            {
                var arrowInfo = metadata.ArrowPoses[i];
                var pos = new Vector3(arrowInfo.X, arrowInfo.Y);
                var rot = Quaternion.Euler(new Vector3(0, 0, arrowInfo.RotZ) + new Vector3(0f, 0f, 18f));
                var arrowRot = Quaternion.Euler(new Vector3(0, 0, arrowInfo.RotZ));

                StarPositions.Add(pos);
                StarRotations.Add(rot);

                if (i == 0 || 
                    i == metadata.ArrowPoses.Length - 1 ||
                    (i == metadata.ArrowPoses.Length - 2 && metadata.ConditionalLastArrow))
                {
                    continue;
                }
                
                var arrowObj = Instantiate(arrowPrefab, Transform);
                var arrowTransform = arrowObj.transform;
                var arrowRenderer = arrowObj.GetComponent<SpriteRenderer>();
                

                SlideBars.Add(arrowObj);
                SlideBarRenderers.Add(arrowRenderer);
                SlideBarTransforms.Add(arrowTransform);
                arrowTransform.localScale *= USERSETTING_SLIDE_SCALE;
                arrowTransform.position = pos;
                arrowTransform.rotation = arrowRot;
            }
        }
        private void InitSlideOk(ExtendSlideMetadata metadata)
        {
            switch (metadata.OkType)
            {
                case SlideOkType.StraightR:
                case SlideOkType.CircleR:
                    IsJustR = true;
                    break;
            }
            if (SlideOK is null)
            {
                return;
            }
            var pose = metadata.OkPose;
            var rotZ = pose.RotZ;
            NoteHelper.Sorting.SetPosition(SlideOK.transform, new Vector3(pose.X, pose.Y));
            //SlideOK.transform.rotation = Quaternion.Euler(new Vector3(0, 0, pose.RotZ));
            //if (IsJustR)
            //{
            //    rotZ -= 180f;
            //}
            SlideOK.transform.rotation = Quaternion.AngleAxis(rotZ, Vector3.forward);

            switch(metadata.OkType)
            {
                case SlideOkType.CircleL:
                case SlideOkType.CircleR:
                    SlideOK.Shape = SlideOKShape.Curv;
                    break;
                case SlideOkType.StraightL:
                case SlideOkType.StraightR:
                    SlideOK.Shape = SlideOKShape.Str;
                    break;
                case SlideOkType.WifiU:
                case SlideOkType.WifiD:
                    SlideOK.Shape = SlideOKShape.Wifi;
                    break;
            }
        }

        void OnDestroy()
        {
            // 这个消息会遮蔽 NoteDrop.OnDestroy，所以在这里保留它的行为
            Active = false;
            if (Table is not null)
            {
                Table.Dispose();
            }
            else
            {
                PredefinedTable?.Dispose();
            }
            StarPositions.Dispose();
            StarRotations.Dispose();
        }
    }
}
