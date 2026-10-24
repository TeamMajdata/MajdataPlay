using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide;
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
        /// <para>普通 slide 用这个数组更新引导星星时，要用<c>ExtendSlideHelper.IsStarPose</c>跳过条件显示的最后一个箭头，Wifi 就别用这个了</para>
        /// </summary>
        public readonly SlidePose[] ArrowPoses;

        /// <summary>slideOK 的位置与方向，以图片中点为锚点。
        /// 请注意素材的尺寸：直线和圆弧形 slideOK 为 410x140，Wifi 为 668x200</summary>
        public readonly SlidePose OkPose;
        /// <summary>slideOK 的种类，告诉你该用哪张素材</summary>
        public readonly SlideOkType OkType;


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
        {
            SlideConst = slideConst;
            SlideLength = slideLength;
            JudgeAreaQueue = judgeAreaQueue;
            ArrowPoses = arrowPoses;
            OkPose = okPose;
            ConditionalLastArrow = conditionalLastArrow;
            OkType = okType;
            Flag = flag;
        }
    }
}
