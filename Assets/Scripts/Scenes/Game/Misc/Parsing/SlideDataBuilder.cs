using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide.Segments;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MajdataPlay.Scenes.Game.Parsing
{
    /// <summary>
    /// <p>用于生成 slide 相关 metadata 的工具类</p>
    /// <p>目前包含箭头数据和判定区数据</p>
    /// </summary>
    public static class SlideDataBuilder
    {
        /// <summary>
        /// <p>为指定 slide 路径生成箭头的坐标与朝向数据</p>
        /// <p>
        /// 注意返回的数组比实际箭头数量多 2~3 个，因为路径起点与终点也被包含进去了，而起点和终点不放置箭头。
        /// 此外官机的实践是：如果最后一个箭头距离路径终点的距离不足 23.556 px 则只在 conn-slide 中显示，
        /// 因此拿到返回值以后还需要进行简单的后处理
        /// </p>
        /// <p>请使用<c>MajGeo.DefaultDistance / 2</c>代替 23.556</p>
        /// </summary>
        public static SlideArrowRawData[] BuildArrowData(ParametricSlidePath path)
        {
            var result = new List<SlideArrowRawData>();
            var totalLength = path.GetPathLength();
            var totalSegCount = path.Segments.Length;

            var currentLength = 0.0;
            var segIdx = 0;

            while (currentLength < totalLength)
            {
                var t = currentLength / totalLength;
                var pt = path.GetPointAt(t);
                var tg = path.GetTangentAt(t);

                if (path.GetSegmentAt(t, out _).IsCurve && currentLength > 0)
                {
                    // 在官机中，圆弧形 slide 箭头的朝向是“上个箭头 → 当前箭头”的延长线方向
                    // （严格来说前 4 ~ 6 个箭头有一个收敛过程，但我不想复刻这个）

                    var lastPoint = result[^1].Point;
                    tg = pt - lastPoint;
                    tg /= tg.Magnitude;
                }

                result.Add(new SlideArrowRawData(pt, tg, currentLength));

                // 计算下一个箭头放在哪里
                var nextLength = currentLength + path.Segments[segIdx].ArrowDistance;

                if (segIdx < totalSegCount - 1 && nextLength >= path.AccumulatedLengths[segIdx])
                {
                    // 即将切换 Segment
                    if (path.Segments[segIdx + 1].ParseMarker == SlideParseMarker.SmoothAlign)
                    {
                        // SmoothAlign 标志的意思是，调节本段箭头间距使得结束时箭头位置恰好在本段终点
                        // P.S. 这种情况出现在 ppqq 圈进入判定线大圆，可以把转移轨道的箭头间距微调一下，让大圆的箭头对齐
                        var delta = path.AccumulatedLengths[segIdx + 1] - currentLength;
                        var n = Math.Round(delta / SlideGeo.DefaultDistance);
                        path.Segments[segIdx + 1].SetArrowDistance(delta / n);
                        nextLength = currentLength + delta / n;
                    }

                    if (path.Segments[segIdx].ParseMarker == SlideParseMarker.ForceAlign)
                    {
                        // ForceAlign 标志的意思是，结束时把当前的位置强制对齐到本段的终点
                        // 于是下一个箭头应该出现在转折点之后一个间隔处
                        // P.S. 这种情况一般是出现在一条直线连接到判定线大圆，这个处理是为了让大圆的箭头对齐
                        nextLength = path.AccumulatedLengths[segIdx] + path.Segments[segIdx + 1].ArrowDistance;
                    }

                    segIdx++;
                }

                currentLength = nextLength;
            }

            // 把路径终点补上
            result.Add(new SlideArrowRawData(path.GetPointAt(1.0), path.GetTangentAt(1.0), totalLength));

            return result.ToArray();
        }

        /// <summary>
        /// The largest distance from the path end at which <see cref="BuildArrowData"/> may place an arrow through rounding,
        /// for example on <c>1Q9K2</c> but not on <c>2Q9K3</c>. Such an arrow is the path end itself.
        /// </summary>
        private const double EndArrowTolerance = SlideGeo.Epsilon;

        /// <summary>
        /// Checks whether the last arrow of a path is so close to the path end that, as in the official game, it is only
        /// shown when the path is followed by another segment of a connection slide.
        /// </summary>
        /// <param name="lastArrow">The last arrow before the path end.</param>
        /// <param name="pathEnd">The path end.</param>
        /// <returns><see langword="true"/> when the arrow is at most half the default arrow distance from the path end.</returns>
        public static bool IsLastArrowConditional(SlideArrowRawData lastArrow, SlideArrowRawData pathEnd)
        {
            return pathEnd.PathLength - lastArrow.PathLength <= SlideGeo.DefaultDistance / 2.0;
        }

        /// <summary>
        /// Builds the arrows of a simai connection slide segment by segment: every segment gets the arrows of
        /// <see cref="BuildArrowData"/> for its own path, so it overlaps the standalone slide of the same shape exactly.
        /// The joints between segments are passed by the guide star but get no arrow, unless
        /// <paramref name="isArcJointFilled"/> fills the gap between two arcs of the judgment circle. As in the official
        /// game, the conditional last arrow of a segment (see <see cref="IsLastArrowConditional"/>) is shown when another
        /// segment follows.
        /// </summary>
        /// <param name="segmentPaths">The paths of the segments in order; each one starts where the one before it ends.</param>
        /// <param name="isArcJointFilled">
        /// Whether an arrow is placed where an arc of the judgment circle meets another one; otherwise the joint is left
        /// as a gap of two arrow distances.
        /// </param>
        /// <param name="starData">
        /// Receives the poses the guide star passes through, in path order: the path start, the arrows except a
        /// conditional last arrow of the final segment, the joints between segments and the path end.
        /// </param>
        /// <returns>
        /// The arrows in the layout of <see cref="BuildArrowData"/>: the path start, the arrows and the path end, with
        /// path lengths measured from the start of the first segment.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="segmentPaths"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="segmentPaths"/> is empty.</exception>
        public static SlideArrowRawData[] BuildSegmentedArrowData(IReadOnlyList<ParametricSlidePath> segmentPaths,
                                                                  bool isArcJointFilled,
                                                                  out SlideArrowRawData[] starData)
        {
            if (segmentPaths is null)
            {
                throw new ArgumentNullException(nameof(segmentPaths));
            }
            if (segmentPaths.Count == 0)
            {
                throw new ArgumentException("A slide needs at least one segment", nameof(segmentPaths));
            }

            var arrows = new List<SlideArrowRawData>();
            var stars = new List<SlideArrowRawData>();
            var startLength = 0.0;
            for (var i = 0; i < segmentPaths.Count; i++)
            {
                var segmentArrows = BuildArrowData(segmentPaths[i]);
                var endIndex = segmentArrows.Length - 1;
                var lastArrowIndex = endIndex - 1;
                if (lastArrowIndex > 0 &&
                    segmentArrows[endIndex].PathLength - segmentArrows[lastArrowIndex].PathLength < EndArrowTolerance)
                {
                    // 舍入误差在终点上多放的箭头就是终点本身，不算箭头，否则它会在连接点上补出一个箭头
                    lastArrowIndex--;
                }
                var isFinalSegment = i == segmentPaths.Count - 1;
                // 只有最后一段的条件箭头不显示（由 ExtendSlideHelper 判断），引导星星也不经过它
                var isLastArrowConditional = isFinalSegment &&
                                             lastArrowIndex > 0 &&
                                             IsLastArrowConditional(segmentArrows[lastArrowIndex], segmentArrows[endIndex]);

                if (i == 0)
                {
                    arrows.Add(segmentArrows[0]);
                    stars.Add(segmentArrows[0]);
                }
                for (var j = 1; j <= lastArrowIndex; j++)
                {
                    var arrow = MoveAlongChain(segmentArrows[j], startLength);
                    arrows.Add(arrow);
                    if (!isLastArrowConditional || j != lastArrowIndex)
                    {
                        stars.Add(arrow);
                    }
                }

                var segmentEnd = MoveAlongChain(segmentArrows[endIndex], startLength);
                if (isFinalSegment)
                {
                    arrows.Add(segmentEnd);
                    stars.Add(segmentEnd);
                }
                else if (isArcJointFilled && IsArcJoint(segmentPaths[i], segmentPaths[i + 1]))
                {
                    // 圆弧上的箭头朝向是“上个箭头 → 当前箭头”，补上的箭头也一样
                    var direction = segmentEnd.Point - arrows[^1].Point;
                    direction /= direction.Magnitude;
                    var jointArrow = new SlideArrowRawData(segmentEnd.Point, direction, segmentEnd.PathLength);
                    arrows.Add(jointArrow);
                    stars.Add(jointArrow);
                }
                else
                {
                    stars.Add(segmentEnd);
                }
                startLength += segmentPaths[i].GetPathLength();
            }

            starData = stars.ToArray();
            return arrows.ToArray();
        }

        /// <summary>Moves an arrow of a segment to its place along a connection slide.</summary>
        /// <param name="arrow">The arrow, with its path length measured from the start of its segment.</param>
        /// <param name="segmentStartLength">The path length of the connection slide at the start of the segment.</param>
        /// <returns>The arrow with its path length measured from the start of the connection slide.</returns>
        private static SlideArrowRawData MoveAlongChain(SlideArrowRawData arrow, double segmentStartLength)
        {
            return new SlideArrowRawData(arrow.Point, arrow.Direction, segmentStartLength + arrow.PathLength);
        }

        /// <summary>Checks whether two consecutive segments of a connection slide meet as two arcs of the judgment circle.</summary>
        /// <param name="before">The earlier segment.</param>
        /// <param name="after">The segment that starts where <paramref name="before"/> ends.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="before"/> ends and <paramref name="after"/> starts along the
        /// judgment circle, for example at key 2 of <c>1^2^3</c>.
        /// </returns>
        private static bool IsArcJoint(ParametricSlidePath before, ParametricSlidePath after)
        {
            return IsJudgmentCircleArc(before.Segments[^1]) && IsJudgmentCircleArc(after.Segments[0]);
        }

        /// <summary>Checks whether a path segment runs along the judgment circle.</summary>
        /// <param name="segment">The path segment.</param>
        /// <returns><see langword="true"/> for an arc or a full turn of the judgment circle.</returns>
        private static bool IsJudgmentCircleArc(PathSegment segment)
        {
            ComplexCircle circle;
            switch (segment)
            {
                case ArcSegment arc:
                    circle = arc.Circle;
                    break;
                case CircleSegment fullCircle:
                    circle = fullCircle.Circle;
                    break;
                default:
                    return false;
            }
            var judgmentCircle = SlideGeo.GetCircle(9);
            return (circle.Center - judgmentCircle.Center).Magnitude < SlideGeo.Epsilon &&
                   Math.Abs(circle.Radius - judgmentCircle.Radius) < SlideGeo.Epsilon;
        }

        /// <summary>
        /// <p>key 是两个 5 bit 整数拼起来，表示高 5 位的判定区前往低 5 位的判定区</p>
        /// <p>5 bit 整数与判定区的对应符合<c>SensorType</c>的定义，可取范围 0~16</p>
        /// <p>value 记录这一段路径如何被各个判定段切割</p>
        /// <p><c>LengthAfterPush</c>是“判定区出点”的位置</p>
        /// <p><c>LengthAfterFinish</c>是“下个判定区入点”的位置</p>
        /// <p>这个字典只能应付由 slidecode 生成的 slide 形状，无法处理过于超前的形状</p>
        /// </summary>
        public static readonly Dictionary<int, SlideAreaRawData[]> SlideAreaLookup = new();

        /// <summary>
        /// 初始化判定区探测算法，给<c>HitAreasLookup</c>打表
        /// </summary>
        public static void InitializeSlideAreaLookup()
        {
            SlideAreaLookup.Clear();
            for (var i = 0; i < 8; i++)
            {
                for (var j = 0; j < 8; j++)
                {
                    AddSlideAreaLookupEntries(i, j);
                }
            }
        }

        private static void AddSlideAreaLookupEntries(int i, int j)
        {
            // 这里的各种 magic number 都是实测的数据，不要动
            var diff = (j - i) & 7; // 其实就是 % 8 ... 某种对负数的兼容性
            int tmp, tmp2;

            // Ai -> Aj
            var key = (i << 5) | j;
            switch (diff)
            {
                // 只需要考虑 1/2/6/7
                // 相隔 3/4/5 个键位的 A 区不可能直接到达而不接触其他判定区
                case 1:
                case 7:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.32, 0.68, i),
                        new SlideAreaRawData(1.00, 1.00, j)
                    };
                        break;
                    }
                case 2:
                case 6:
                    {
                        tmp = (diff == 2) ? (i + 1) & 7 : (i - 1) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.20, 0.38, i),
                        new SlideAreaRawData(0.62, 0.80, tmp, tmp | 8),
                        new SlideAreaRawData(1.00, 1.00, j)
                    };
                        break;
                    }
            }

            // Bi -> Bj
            key = ((i | 8) << 5) | (j | 8);
            switch (diff)
            {
                // 1~7 中除了 4 都有可能
                // 相隔 4 个键位的 B 区之间必然经过 C 区
                case 1:
                case 7:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.35, 0.65, i | 8),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        break;
                    }
                case 2:
                case 6:
                    {
                        tmp = (diff == 2) ? (i + 1) & 7 : (i - 1) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.22, 0.40, i | 8),
                        new SlideAreaRawData(0.60, 0.78, tmp | 8, 16),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        break;
                    }
                case 3:
                case 5:
                    {
                        tmp = (diff == 3) ? (i + 1) & 7 : (i - 1) & 7;
                        tmp2 = (diff == 3) ? (i + 2) & 7 : (i - 2) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.15, 0.28, i | 8),
                        new SlideAreaRawData(0.43, 0.57, tmp | 8, 16),
                        new SlideAreaRawData(0.72, 0.85, tmp2 | 8, 16),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        break;
                    }
            }

            // Ai <-> Bj
            key = (i << 5) | (j | 8);
            var key2 = ((j | 8) << 5) | i;
            switch (diff)
            {
                // 2/4/6 是不可能的，剩下 0/1/3/5/7
                case 0:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.50, 0.80, i),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        SlideAreaLookup[key2] = new[]
                        {
                        new SlideAreaRawData(0.20, 0.50, j | 8),
                        new SlideAreaRawData(1.00, 1.00, i)
                    };
                        break;
                    }
                case 1:
                case 7:
                    {
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.45, 0.77, i),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        SlideAreaLookup[key2] =
                            new[]
                            {
                            new SlideAreaRawData(0.23, 0.55, j | 8),
                            new SlideAreaRawData(1.00, 1.00, i)
                            };
                        break;
                    }
                case 3:
                case 5:
                    {
                        tmp = (diff == 3) ? (i + 1) & 7 : (i - 1) & 7;
                        tmp2 = (diff == 3) ? (i + 2) & 7 : (i - 2) & 7;
                        SlideAreaLookup[key] = new[]
                        {
                        new SlideAreaRawData(0.20, 0.34, i),
                        new SlideAreaRawData(0.54, 0.67, i | 8, tmp | 8),
                        new SlideAreaRawData(0.80, 0.90, tmp2 | 8, 16),
                        new SlideAreaRawData(1.00, 1.00, j | 8)
                    };
                        SlideAreaLookup[key2] = new[]
                        {
                        new SlideAreaRawData(0.10, 0.20, j | 8),
                        new SlideAreaRawData(0.33, 0.46, tmp2 | 8, 16),
                        new SlideAreaRawData(0.66, 0.80, i | 8, tmp | 8),
                        new SlideAreaRawData(1.00, 1.00, i)
                    };
                        break;
                    }
            }

            // C <-> Bj
            // C 区不可能绕过 B 区直接去 A 区
            key = (16 << 5) | (j | 8);
            key2 = ((j | 8) << 5) | 16;
            SlideAreaLookup[key] = new[]
            {
                new SlideAreaRawData(0.40, 0.70, 16),
                new SlideAreaRawData(1.00, 1.00, j | 8)
            };
            SlideAreaLookup[key2] = new[]
            {
                new SlideAreaRawData(0.30, 0.60, j | 8),
                new SlideAreaRawData(1.00, 1.00, 16)
            };
        }

        // 判定区探测算法的参数，不要动
        public const double HitAreaCalcStep = SlideGeo.MainRadius / 48.0;

        public const double HitAreaARadius = SlideGeo.MainRadius * 80.0 / 480.0;
        public const double HitAreaADistance = SlideGeo.MainRadius * 440.0 / 480.0;
        public const double HitAreaBRadius = SlideGeo.MainRadius * 45.0 / 480.0;
        public const double HitAreaBDistance = SlideGeo.MainRadius * 210.0 / 480.0;
        public const double HitAreaCRadius = SlideGeo.MainRadius * 55.0 / 480.0;

        public const double LastDistanceCircle = SlideGeo.MainRadius * 175.0 / 480.0;
        public const double LastDistanceShort = SlideGeo.MainRadius * 130.0 / 480.0;
        public const double LastDistanceLong = SlideGeo.MainRadius * 159.0 / 480.0;

        /// <summary>
        /// 计算指定 slide 路径的判定区序列
        /// </summary>
        public static SlideAreaRawData[] BuildSlideAreas(ParametricSlidePath path)
        {
            // 第一步，计算 slide 路径上“最接近每个判定区的点”
            // 不考虑 OR 判定区，只看恰好经过一个判定区的情况

            var nodeList = new List<(int, double)>(); // 保存判定区以及最接近它的点（用经过的路径长度表示）
            var totalLength = path.GetPathLength();
            var count = (int)Math.Round(totalLength / HitAreaCalcStep);

            int? lastNode = null;
            var enterLength = 0.0;

            for (var i = 0; i < count; i++)
            {
                // 计算现在的位置
                var t = (double)i / count;
                var pt = path.GetPointAt(t);
                int? node = null;

                // 检查是否落在某个判定区的中心领域内
                if (pt.Magnitude < HitAreaCRadius)
                {
                    node = 16;
                }
                else
                    for (var j = 0; j < 8; j++)
                    {
                        var phi = Math.PI * (3.0 / 8.0 - j / 4.0);
                        if ((pt - Complex.FromPolarCoordinates(HitAreaADistance, phi)).Magnitude < HitAreaARadius)
                        {
                            node = j;
                            break;
                        }

                        if ((pt - Complex.FromPolarCoordinates(HitAreaBDistance, phi)).Magnitude < HitAreaBRadius)
                        {
                            node = j | 8;
                            break;
                        }
                    }
                // node 可能为 null，也可能为 0 ~ 16 中的某个

                if (lastNode != node)
                {
                    // 进入或离开了某个判定区的中心领域
                    var length = t * totalLength;
                    if (lastNode == null)
                    {
                        // 进入的情况，记录路径进入时的长度
                        enterLength = length;
                    }
                    else
                    {
                        // 离开的情况，则认为进入和离开位置的中点是“最接近该判定区的点”
                        nodeList.Add((lastNode.Value, (length + enterLength) / 2.0));

                        if (node != null)
                        {
                            // 正好又进入了新一个判定区的领域
                            enterLength = length;
                        }
                    }
                }

                lastNode = node;
            }

            // 补上最后一个区，此时必然位于某个 A 区领域内，所以 lastNode 必不为 null
            nodeList.Add((lastNode!.Value, totalLength));
            // 把第一个判定区的“最接近点”设为 slide 起点
            // 因为按照上述算法，如果不这么做，“最接近点”将会是 起点~离开第一个区 这段路径的中点
            nodeList[0] = (nodeList[0].Item1, 0.0);

            // ========== ========== ========== ========== ========== ========== ==========
            // 第二步，生成判定区列表，以及相应的 push 和 finish
            // OR 判定区在这一步根据预先打好的表生成

            // ReSharper disable once UseObjectOrCollectionInitializer
            var result = new List<SlideAreaRawData>();
            result.Add(new SlideAreaRawData(0.0, 0.0, nodeList[0].Item1));

            for (var i = 1; i < nodeList.Count; i++)
            {
                // 生成查表的 key：高 5 位是上一判定区，低 5 位是下一判定区
                var key = (nodeList[i - 1].Item1 << 5) | nodeList[i].Item1;
                // 这是“最接近”两判定区的点之间的距离
                var lastLength = nodeList[i - 1].Item2;
                var segmentLength = nodeList[i].Item2 - lastLength;

                var data = SlideAreaLookup[key];
                var area = result[^1];

                // 按照预先打表的性质，此时上一个区的 push 和 finish 应该都是“判定区中心”
                // 那么这里再给 push 加上“中心到出点”，finish 加上“中心到下一个入点”
                result[^1] = new SlideAreaRawData(
                    lastLength + segmentLength * data[0].LengthAfterPush,
                    lastLength + segmentLength * data[0].LengthAfterFinish,
                    area.SensorA, area.SensorB
                );

                // 根据预先打好的表生成判定区
                for (var j = 1; j < data.Length; j++)
                {
                    result.Add(new SlideAreaRawData(
                        lastLength + segmentLength * data[j].LengthAfterPush,
                        lastLength + segmentLength * data[j].LengthAfterFinish,
                        data[j].SensorA, data[j].SensorB
                    ));
                }
            }

            // ========== ========== ========== ========== ========== ========== ==========
            // 第三步，修正最后一个区的入点，使之差不多吻合官机的尾判时机

            double lastDistance;

            if (path.Segments[^1] is LineSegment)
            {
                // 最后一个区是直线进入
                // 如果来自 A 区就用短的 LastDistance，例如 A3->A1 / A2->A1 的情况
                lastDistance = nodeList[^2].Item1 <= 7 ? LastDistanceShort : LastDistanceLong;
            }
            else
            {
                // 最后一个区是圆弧进入
                lastDistance = LastDistanceCircle;
            }

            // 然后修正倒数第二区和最后一区的 push 和 finish
            // ReSharper disable once InconsistentNaming
            var last2ndArea = result[^2];
            var lastArea = result[^1];
            result[^2] = new SlideAreaRawData(last2ndArea.LengthAfterPush, totalLength - lastDistance,
                last2ndArea.SensorA, last2ndArea.SensorB);
            result[^1] = new SlideAreaRawData(totalLength, totalLength, lastArea.SensorA, lastArea.SensorB);

            return result.ToArray();
        }
    }
}
