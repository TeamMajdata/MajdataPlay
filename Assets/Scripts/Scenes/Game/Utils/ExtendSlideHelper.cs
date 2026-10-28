using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MajdataPlay.Scenes.Game.Utils
{
    internal static class ExtendSlideHelper
    {
        /// <summary>需要贴图尺寸 410x140</summary>
        public const double CircleOkRadius = SlideGeo.MainRadius * 462.0 / 480.0;
        /// <summary>需要贴图尺寸 410x140</summary>
        public const double StraightOkDistance = SlideGeo.MainRadius * 205.0 / 480.0;


        public static SlidePose CalcArrowPose(SlideArrowRawData rawData)
        {
            var x = (float)rawData.Point.Real;
            var y = (float)rawData.Point.Imaginary;
            var phase = rawData.Direction.Phase;  // -pi ~ pi

            // rawData 中 phase 是弧度，0 度是箭头朝正右，逆时针为正
            // View 中 rotZ 是角度，0 度是箭头朝正左，逆时针为正
            var rotZ = (float)(180 + phase * 180 / Math.PI);    // should be 0 ~ 360

            return new SlidePose(x, y, rotZ, (float)rawData.PathLength);
        }

        /// <summary>
        /// 获取圆弧 slide 的 slideOK 姿势
        /// </summary>
        /// <param name="endButton">1-based idx，1~8</param>
        /// <param name="isCcw">true 表示是逆时针 slide</param>
        public static SlidePose CalcCircleOkPose(int endButton, bool isCcw)
        {
            if (isCcw)
            {
                var rotZ = (float)(360 - 45 * endButton);
                var pos = Complex.FromPolarCoordinates(CircleOkRadius, Math.PI * (2 - endButton) / 4.0);
                return new SlidePose((float)pos.Real, (float)pos.Imaginary, rotZ, 0);
            }
            else
            {
                var rotZ = (float)(405 - 45 * endButton);
                var pos = Complex.FromPolarCoordinates(CircleOkRadius, Math.PI * (3 - endButton) / 4.0);
                return new SlidePose((float)pos.Real, (float)pos.Imaginary, rotZ, 0);
            }
        }

        /// <summary>
        /// 获取直线 slide 的 slideOK 姿势
        /// </summary>
        /// <param name="finalArrow">直接把生成的最后一个箭头数据代进来</param>
        /// <param name="isLeft">true 表示是朝左的 Ok</param>
        public static SlidePose CalcStraightOkPose(SlideArrowRawData finalArrow, bool isLeft)
        {
            var pos = finalArrow.Point - finalArrow.Direction * StraightOkDistance;
            var rotZ = (float)(finalArrow.Direction.Phase * 180.0 / Math.PI);    // should be -180 ~ 180
            if (isLeft)
            {
                rotZ += 180f;
            }
            return new SlidePose((float)pos.Real, (float)pos.Imaginary, rotZ, 0);
        }

        /// <summary>
        /// 使用指定的 slide 路径打表
        /// </summary>
        public static ExtendSlideMetadata CreateSlideEntry(ParametricSlidePath slidePath, SlideFlag flag = SlideFlag.None)
        {
            var arrowData = SlideDataBuilder.BuildArrowData(slidePath);
            var areaData = SlideDataBuilder.BuildSlideAreas(slidePath);
            return CreateSlideEntry(arrowData, areaData, slidePath.GetEndShape(), flag);
        }

        /// <summary>
        /// Creates the metadata of a simai connection slide, such as <c>1-4q7</c>, in the given arrow style.
        /// </summary>
        /// <param name="chainPath">
        /// The path of the whole chain, parsed from the slide code that joins the segments (see
        /// <see cref="SimaiSlideTranslator.TranslateChain"/>). The judge areas always follow this path.
        /// </param>
        /// <param name="segmentPaths">
        /// The paths of the segments, each parsed from its own slide code and rotated like <paramref name="chainPath"/>;
        /// together they draw <paramref name="chainPath"/>.
        /// </param>
        /// <param name="style">How the arrows are placed along the segments.</param>
        /// <returns>The metadata of the slide.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="segmentPaths"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="segmentPaths"/> is empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="style"/> is not a defined style.</exception>
        public static ExtendSlideMetadata CreateConnSlideEntry(ParametricSlidePath chainPath,
                                                               IReadOnlyList<ParametricSlidePath> segmentPaths,
                                                               SlidePathStyle style)
        {
            bool isArcJointFilled;
            switch (style)
            {
                case SlidePathStyle.Classic:
                    isArcJointFilled = false;
                    break;
                case SlidePathStyle.Medium:
                    isArcJointFilled = true;
                    break;
                case SlidePathStyle.Perfect:
                    return CreateSlideEntry(chainPath);
                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown slide path style");
            }

            var arrowData = SlideDataBuilder.BuildSegmentedArrowData(segmentPaths, isArcJointFilled, out var starData);
            // 各段拼起来就是整条路径，所以箭头的路径长度可以直接对上整条路径的判定区
            var areaData = SlideDataBuilder.BuildSlideAreas(chainPath);
            return CreateSlideEntry(arrowData, starData, areaData, chainPath.GetEndShape());
        }

        /// <summary>
        /// 使用指定的 slide 路径打表，如果需要对原始参数做调整的话用这个
        /// </summary>
        public static ExtendSlideMetadata CreateSlideEntry(
            SlideArrowRawData[] arrowRawData,
            SlideAreaRawData[] areaRawData,
            SlideEndShape endShape,
            SlideFlag flag = SlideFlag.None)
        {
            // 引导星星经过除条件箭头以外的所有箭头
            var conditionalLastArrow = SlideDataBuilder.IsLastArrowConditional(arrowRawData[^2], arrowRawData[^1]);
            var starRawData = new List<SlideArrowRawData>(arrowRawData.Length);
            for (var i = 0; i < arrowRawData.Length; i++)
            {
                if (IsStarPose(i, arrowRawData.Length, conditionalLastArrow))
                {
                    starRawData.Add(arrowRawData[i]);
                }
            }
            return CreateSlideEntry(arrowRawData, starRawData.ToArray(), areaRawData, endShape, flag);
        }

        /// <summary>
        /// Creates the metadata of a slide whose guide star poses are given separately from its arrows, such as a
        /// connection slide whose segment joints have no arrow.
        /// </summary>
        /// <param name="arrowRawData">The path start, the arrows and the path end, as built by <see cref="SlideDataBuilder"/>.</param>
        /// <param name="starRawData">The poses the guide star passes through, from the path start to the path end.</param>
        /// <param name="areaRawData">The judge areas along the same path.</param>
        /// <param name="endShape">The shape of the path end, which selects the slide result.</param>
        /// <param name="flag">The special shape flag.</param>
        /// <returns>The metadata of the slide.</returns>
        public static ExtendSlideMetadata CreateSlideEntry(
            SlideArrowRawData[] arrowRawData,
            SlideArrowRawData[] starRawData,
            SlideAreaRawData[] areaRawData,
            SlideEndShape endShape,
            SlideFlag flag = SlideFlag.None)
        {
            // ========== ========== ========== ========== ========== ========== ==========
            // 整理箭头数据

            var arrowPoseList = new List<SlidePose>();

            for (var i = 0; i < arrowRawData.Length; i++)
            {
                arrowPoseList.Add(CalcArrowPose(arrowRawData[i]));
            }

            var arrowCount = arrowPoseList.Count;

            var starPoses = new SlidePose[starRawData.Length];
            for (var i = 0; i < starRawData.Length; i++)
            {
                starPoses[i] = CalcArrowPose(starRawData[i]);
            }

            // 如果最后一个箭头距离终点太近，就只在 conn-slide 里显示
            var conditionalLastArrow = SlideDataBuilder.IsLastArrowConditional(arrowRawData[^2], arrowRawData[^1]);

            // ========== ========== ========== ========== ========== ========== ==========
            // 整理判定区数据

            var areaList = new List<ParsingSlideArea>();

            var arrowIdx = 1;
            var lastLength = 0.0;
            var firstAreaMinIdx = 2;    // 第一个区至少删两个箭头
            var finalAreaMaxIdx = conditionalLastArrow ? arrowCount - 7 : arrowCount - 6;  // 最后一个区至少留四个箭头
            SensorArea sensorA;
            SensorArea? sensorB;
            for (var i = 0; i <= areaRawData.Length - 2; i++) // 最后一个判定段要特殊处理
            {
                var targetLength = lastLength + 0.33 * (areaRawData[i].LengthAfterPush - lastLength);
                while (arrowRawData[arrowIdx].PathLength <= targetLength) arrowIdx++;
                lastLength = areaRawData[i].LengthAfterPush;
                var push = Math.Max(arrowIdx - 1, firstAreaMinIdx);   // 扣掉本来就不显示的路径起点

                targetLength = lastLength + 0.33 * (areaRawData[i].LengthAfterFinish - lastLength);
                while (arrowRawData[arrowIdx].PathLength <= targetLength) arrowIdx++;
                var finish = Math.Min(arrowIdx - 1, finalAreaMaxIdx);   // 扣掉本来就不显示的路径起点
                lastLength = areaRawData[i].LengthAfterFinish;

                sensorA = (SensorArea)areaRawData[i].SensorA;
                sensorB = areaRawData[i].SensorB == -1 ? null : (SensorArea)areaRawData[i].SensorB;
                areaList.Add(new ParsingSlideArea(push, finish, sensorA, sensorB));
            }

            sensorA = (SensorArea)areaRawData[^1].SensorA;
            sensorB = areaRawData[^1].SensorB == -1 ? null : (SensorArea)areaRawData[^1].SensorB;
            areaList.Add(new ParsingSlideArea(arrowCount, arrowCount, sensorA, sensorB));

            var slideLength = arrowRawData[^1].PathLength;
            var slideConst = CalcSlideConst(starRawData, areaRawData[^2].LengthAfterFinish);

            // ========== ========== ========== ========== ========== ========== ==========
            // 生成 slideOk

            var endButton = areaRawData[^1].SensorA + 1;   // 直接从判定队列里抠出最后一个区的键位
            SlideOkType okType;
            SlidePose okPose;

            switch (endShape)
            {
                case SlideEndShape.CircleCCW:
                    {
                        okType = SlideOkType.CircleL;
                        okPose = CalcCircleOkPose(endButton, true);
                        break;
                    }
                case SlideEndShape.CircleCW:
                    {
                        okType = SlideOkType.CircleR;
                        okPose = CalcCircleOkPose(endButton, false);
                        break;
                    }
                case SlideEndShape.Straight:
                default:
                    {
                        var isLeft = (endButton > 4);
                        okType = isLeft ? SlideOkType.StraightL : SlideOkType.StraightR;
                        okPose = CalcStraightOkPose(arrowRawData[^1], isLeft);
                        break;
                    }
            }

            return new ExtendSlideMetadata(
                areaList.ToArray(), slideConst, (float)slideLength,
                arrowPoseList.ToArray(), starPoses, conditionalLastArrow,
                okPose, okType, flag);
        }

        /// <summary>
        /// Checks whether the guide star of a slide built from a single path passes through a pose of
        /// <see cref="ExtendSlideMetadata.ArrowPoses"/>. The star skips the conditional last arrow, like the star of a
        /// prefab slide, whose prefab has no such arrow.
        /// </summary>
        /// <param name="poseIndex">The index of the pose, where 0 is the path start.</param>
        /// <param name="poseCount">The number of poses, including the path start and end.</param>
        /// <param name="conditionalLastArrow">Whether the last arrow is too close to the path end to be shown.</param>
        /// <returns><see langword="true"/> when the star passes through the pose.</returns>
        public static bool IsStarPose(int poseIndex, int poseCount, bool conditionalLastArrow)
        {
            return !conditionalLastArrow || poseIndex == 0 || poseIndex != poseCount - 2;
        }

        /// <summary>
        /// Converts the path length at which the star reaches the judge frame into the slide constant. The star does not
        /// move at a constant speed: it passes the same number of star poses per second, and the poses are not evenly
        /// spaced (the gap before the path end is usually shorter or longer than the arrow distance), so the length is
        /// first converted into a fractional pose index.
        /// </summary>
        /// <param name="starRawData">The poses the star passes through, including the path start and end.</param>
        /// <param name="judgeLength">The path length at which the star reaches the judge frame.</param>
        /// <returns>The share of the star movement left after the judge frame, from 0 to 1.</returns>
        private static float CalcSlideConst(SlideArrowRawData[] starRawData, double judgeLength)
        {
            // 找到正解帧所在的那一段：starRawData[next - 1] ~ starRawData[next]
            var next = 1;
            while (next < starRawData.Length - 1 && starRawData[next].PathLength < judgeLength)
            {
                next++;
            }
            var segmentStart = starRawData[next - 1].PathLength;
            var segmentLength = starRawData[next].PathLength - segmentStart;
            var segmentProgress = segmentLength > 0 ? Math.Clamp((judgeLength - segmentStart) / segmentLength, 0.0, 1.0) : 1.0;
            var starIndex = next - 1 + segmentProgress;

            return (float)(1.0 - (starIndex / (starRawData.Length - 1)));
        }
    }
}
