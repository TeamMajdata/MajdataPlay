using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Utils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Scenes.Game.Parsing.Slide
{
    internal readonly struct ExtendSlideMetadata
    {
        /// <summary>
        /// The share of the guide star movement left after the judge frame, from 0 to 1. The star passes its poses at a
        /// constant rate, so this is a share of the star poses, not of the path length.
        /// </summary>
        public readonly float SlideConst;
        /// <summary>slide 路径全长，单位取决于<c>MajGeo.MainRadius</c></summary>
        public readonly float SlideLength;
        /// <summary>这个参数为 true 表示最后一个箭头距离终点太近，只在 conn-slide 的非最终段显示</summary>
        public readonly bool ConditionalLastArrow;
        /// <summary>用来标记是不是大V</summary>
        public readonly SlideFlag Flag;
        /// <summary>判定队列，若为 Wifi 则表示中间一支的判定队列</summary>
        public readonly ParsingSlideArea[] JudgeAreaQueue;

        /// <summary>
        /// <para>slide 每个箭头的位置与方向</para>
        /// <para>注意数组中包含了路径起点和终点，真正应该显示的箭头需要去掉这两项
        /// （对<c>ConditionalLastArrow = true</c>的情况还要视情况去掉倒数第二项）</para>
        /// <para>引导星星请使用<see cref="StarPoses"/>，Wifi 就别用这个了</para>
        /// </summary>
        public readonly SlidePose[] ArrowPoses;

        /// <summary>
        /// The poses the guide star passes through, at a constant number per second, from the path start to the path end.
        /// They are the arrow poses without a conditional last arrow, plus the joints between the segments of a
        /// connection slide where no arrow is placed (see <see cref="MajdataPlay.Settings.SlidePathStyle"/>).
        /// </summary>
        public readonly SlidePose[] StarPoses;

        /// <summary>slideOK 的位置与方向，以图片中点为锚点。
        /// 请注意素材的尺寸：直线和圆弧形 slideOK 为 410x140，Wifi 为 668x200</summary>
        public readonly SlidePose OkPose;
        /// <summary>slideOK 的种类，告诉你该用哪张素材</summary>
        public readonly SlideOkType OkType;


        /// <summary>
        /// Initializes the metadata of a slide whose guide star passes through every arrow pose except a conditional last
        /// arrow, which is every slide built from a single path.
        /// </summary>
        /// <param name="judgeAreaQueue">The judge areas in order.</param>
        /// <param name="slideConst">The share of the guide star movement left after the judge frame.</param>
        /// <param name="slideLength">The length of the path.</param>
        /// <param name="arrowPoses">The path start, the arrows and the path end.</param>
        /// <param name="conditionalLastArrow">Whether the last arrow is too close to the path end to be shown.</param>
        /// <param name="okPose">The pose of the slide result.</param>
        /// <param name="okType">The slide result type.</param>
        /// <param name="flag">The special shape flag.</param>
        public ExtendSlideMetadata(
            ParsingSlideArea[] judgeAreaQueue,
            float slideConst,
            float slideLength,
            SlidePose[] arrowPoses,
            bool conditionalLastArrow,
            SlidePose okPose,
            SlideOkType okType,
            SlideFlag flag
            )
            : this(judgeAreaQueue,
                   slideConst,
                   slideLength,
                   arrowPoses,
                   SelectStarPoses(arrowPoses, conditionalLastArrow),
                   conditionalLastArrow,
                   okPose,
                   okType,
                   flag)
        {
        }

        /// <summary>Initializes the metadata of a slide whose guide star poses are given separately from its arrows.</summary>
        /// <param name="judgeAreaQueue">The judge areas in order.</param>
        /// <param name="slideConst">The share of the guide star movement left after the judge frame.</param>
        /// <param name="slideLength">The length of the path.</param>
        /// <param name="arrowPoses">The path start, the arrows and the path end.</param>
        /// <param name="starPoses">The poses the guide star passes through, from the path start to the path end.</param>
        /// <param name="conditionalLastArrow">Whether the last arrow is too close to the path end to be shown.</param>
        /// <param name="okPose">The pose of the slide result.</param>
        /// <param name="okType">The slide result type.</param>
        /// <param name="flag">The special shape flag.</param>
        public ExtendSlideMetadata(
            ParsingSlideArea[] judgeAreaQueue,
            float slideConst,
            float slideLength,
            SlidePose[] arrowPoses,
            SlidePose[] starPoses,
            bool conditionalLastArrow,
            SlidePose okPose,
            SlideOkType okType,
            SlideFlag flag
            )
        {
            SlideConst = slideConst;
            SlideLength = slideLength;
            JudgeAreaQueue = judgeAreaQueue;
            ArrowPoses = arrowPoses;
            StarPoses = starPoses;
            OkPose = okPose;
            ConditionalLastArrow = conditionalLastArrow;
            OkType = okType;
            Flag = flag;
        }

        /// <summary>Selects the arrow poses the guide star passes through.</summary>
        /// <param name="arrowPoses">The path start, the arrows and the path end.</param>
        /// <param name="conditionalLastArrow">Whether the last arrow is too close to the path end to be shown.</param>
        /// <returns>The arrow poses without the conditional last arrow.</returns>
        private static SlidePose[] SelectStarPoses(SlidePose[] arrowPoses, bool conditionalLastArrow)
        {
            var starPoses = new List<SlidePose>(arrowPoses.Length);
            for (var i = 0; i < arrowPoses.Length; i++)
            {
                if (ExtendSlideHelper.IsStarPose(i, arrowPoses.Length, conditionalLastArrow))
                {
                    starPoses.Add(arrowPoses[i]);
                }
            }
            return starPoses.ToArray();
        }
    }
}
