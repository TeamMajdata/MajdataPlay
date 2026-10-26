using MajdataPlay.Scenes.Game.Parsing.Slide.Segments;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MajdataPlay.Scenes.Game.Parsing.Slide
{
    /// <summary>
    /// 支持链式调用的 slide 路径构造工具
    /// </summary>
    public class SlidePathConstructor
    {
        public readonly List<PathSegment> PathSegments = new();
        public Complex CurrentEndPoint = Complex.Zero;

        /// <summary>
        /// <p>从指定点开始构造 slide 路径</p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public static SlidePathConstructor BeginAt(Complex point)
        {
            var obj = new SlidePathConstructor
            {
                CurrentEndPoint = point
            };
            return obj;
        }

        /// <summary>
        /// 从指定按键开始构造 slide 路径
        /// </summary>
        /// <param name="sensorIdx">判定区，应当是 A 区（0~7）</param>
        /// <returns><c>this</c></returns>
        public static SlidePathConstructor BeginAt(int sensorIdx)
        {
            return BeginAt(SlideGeo.GetPoint(sensorIdx));
        }

        /// <summary>
        /// <p>为上一个路径片段添加控制箭头对齐的标志</p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor TrySetLastParseMarker(SlideParseMarker marker)
        {
            if (PathSegments.Count <= 0) return this;
            PathSegments[^1].SetParseMarker(marker);
            return this;
        }

        /// <summary>
        /// <p>添加一条直线前往指定点的路径</p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor LineToPoint(Complex point)
        {
            PathSegments.Add(new LineSegment(CurrentEndPoint, point));
            CurrentEndPoint = point;
            return this;
        }

        /// <summary>
        /// <p>添加一条直线前往指定判定区的路径（仅支持 A、B、C 区且不是 Touch 的位置）</p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor LineToPoint(int sensorIdx)
        {
            return LineToPoint(SlideGeo.GetPoint(sensorIdx));
        }

        /// <summary>
        /// <p>添加一条沿切线前往指定圆周的路径</p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor TangentToCircle(ComplexCircle circle, bool isCcw)
        {
            var inAngle = SlideGeo.CalcTangentAngle(CurrentEndPoint, circle, isCcw);
            var inPoint = Complex.FromPolarCoordinates(circle.Radius, inAngle) + circle.Center;
            return LineToPoint(inPoint);
        }

        /// <summary>
        /// <p>添加一条沿切线前往指定圆周的路径，圆周定义见<c>MajGeo.GetCircle</c></p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor TangentToCircle(int circleIdx, bool isCcw)
        {
            return TangentToCircle(SlideGeo.GetCircle(circleIdx), isCcw);
        }

        /// <summary>
        /// <p>添加一条沿指定圆心绕行到指定辐角的路径</p>
        /// <p>P.S. 目标辐角是绝对坐标，以正右方向为 0</p>
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="endRad">目标辐角，应在 -pi ~ pi</param>
        /// <param name="isCcw">true 为逆时针绕行</param>
        /// <param name="skipIfZero">true 表示当目标辐角和当前位置相距过小时不进行操作，false 会多绕一圈</param>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor ArcToAngle(Complex center, double endRad, bool isCcw, bool skipIfZero)
        {
            var diff = CurrentEndPoint - center;
            var circle = new ComplexCircle(center, diff.Magnitude);
            var startRad = diff.Phase;
            // startAngle and endAngle in range [-PI, PI]
            // 同一个方向可能分别落在 PI 和 -PI 两侧（例如 5q8、8p5 的切点恰好在正左方），所以要按 2PI 取模判断是否重合
            var isSameAngle = Math.Abs(Math.IEEERemainder(endRad - startRad, 2 * Math.PI)) < SlideGeo.EpsilonRad;
            if (isSameAngle)
            {
                if (skipIfZero)
                {
                    return this;
                }
                endRad = isCcw ? startRad + 2 * Math.PI : startRad - 2 * Math.PI;
            }
            else if (isCcw)
            {
                if (startRad > endRad)
                {
                    startRad -= 2 * Math.PI;
                }
            }
            else
            {
                if (startRad < endRad)
                {
                    startRad += 2 * Math.PI;
                }
            }

            var seg = new ArcSegment(circle, startRad, endRad);
            PathSegments.Add(seg);
            CurrentEndPoint = seg.GetPointAt(1f);
            return this;
        }

        /// <summary>
        /// <p>添加一条沿指定圆心绕行到指定辐角的路径</p>
        /// <p>P.S. 目标辐角是绝对坐标，以正右方向为 0</p>
        /// </summary>
        /// <param name="circleIdx">相关圆，只会用圆心，见<c>MajGeo.GetCircle</c></param>
        /// <param name="endRad">目标辐角，应在 -pi ~ pi</param>
        /// <param name="isCcw">true 为逆时针绕行</param>
        /// <param name="skipIfZero">true 表示当目标辐角和当前位置相等时不进行操作，false 会多绕一圈</param>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor ArcToAngle(int circleIdx, double endRad, bool isCcw, bool skipIfZero)
        {
            return ArcToAngle(SlideGeo.GetCircle(circleIdx).Center, endRad, isCcw, skipIfZero);
        }

        /// <summary>
        /// <p>添加一条沿指定圆心绕行的圆弧，直到恰好达到前往指定目标点的切点</p>
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="target">目标点</param>
        /// <param name="isCcw">true 为逆时针绕行</param>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor ArcToTangentTowards(Complex target, Complex center, bool isCcw)
        {
            var diff = CurrentEndPoint - center;
            var endRad =
                SlideGeo.CalcTangentAngle(target, new ComplexCircle(center, diff.Magnitude), !isCcw);
            return ArcToAngle(center, endRad, isCcw, false);
        }

        /// <summary>
        /// <p>添加一条沿指定圆心绕行的圆弧，直到恰好达到前往指定判定区的切点</p>
        /// </summary>
        /// <param name="circleIdx">相关圆，只会用圆心，见<c>MajGeo.GetCircle</c></param>
        /// <param name="sensorIdx">目标判定区，仅支持 A、B、C 区且不是 Touch 的位置</param>
        /// <param name="isCcw">true 为逆时针绕行</param>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor ArcToTangentTowards(int sensorIdx, int circleIdx, bool isCcw)
        {
            return ArcToTangentTowards(SlideGeo.GetPoint(sensorIdx), SlideGeo.GetCircle(circleIdx).Center, isCcw);
        }

        /// <summary>
        /// <p>添加一条沿指定圆心绕行一整圈的路径</p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor FullCircle(Complex center, bool isCcw)
        {
            var diff = CurrentEndPoint - center;
            var circle = new ComplexCircle(center, diff.Magnitude);
            PathSegments.Add(new CircleSegment(circle, diff.Phase, isCcw));
            // CurrentEndPoint not changed
            return this;
        }

        /// <summary>
        /// <p>添加一条沿指定圆的圆心绕行一整圈的路径，忽略半径</p>
        /// </summary>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor FullCircle(int circleIdx, bool isCcw)
        {
            return FullCircle(SlideGeo.GetCircle(circleIdx).Center, isCcw);
        }

        /// <summary>
        /// <p>添加一条沿外公切线前往指定圆周的直线路径</p>
        /// <p>首先圆弧至外公切线切点，然后走公切线进入目标圆周</p>
        /// </summary>
        /// <param name="currentCenter">当前圆的圆心</param>
        /// <param name="targetCircle">目标圆</param>
        /// <param name="isCcw">true 为逆时针绕行</param>
        /// <returns><c>this</c></returns>
        public SlidePathConstructor ExternTangentTransfer(Complex currentCenter, ComplexCircle targetCircle,
            bool isCcw)
        {
            var diff = CurrentEndPoint - currentCenter;
            double endRad;
            if (Math.Abs(diff.Magnitude - targetCircle.Radius) < SlideGeo.Epsilon)
            {
                // 两个圆半径差不多相等
                var vector = targetCircle.Center - currentCenter;
                vector *= isCcw ? -Complex.ImaginaryOne : Complex.ImaginaryOne;
                endRad = vector.Phase;
            }
            else if (targetCircle.Radius > diff.Magnitude)
            {
                // 目标圆更大
                var helperCircle = new ComplexCircle(targetCircle.Center, targetCircle.Radius - diff.Magnitude);
                endRad = SlideGeo.CalcTangentAngle(currentCenter, helperCircle, isCcw);
            }
            else
            {
                var helperCircle = new ComplexCircle(currentCenter, diff.Magnitude - targetCircle.Radius);
                endRad = SlideGeo.CalcTangentAngle(targetCircle.Center, helperCircle, !isCcw);
            }
            ArcToAngle(currentCenter, endRad, isCcw, false);
            var inPoint = Complex.FromPolarCoordinates(targetCircle.Radius, endRad) + targetCircle.Center;
            LineToPoint(inPoint);
            return this;
        }

        /// <summary>
        /// <p>结束 slide 路径，生成一个参数化 slide 路径对象</p>
        /// </summary>
        public ParametricSlidePath GeneratePath()
        {
            return new ParametricSlidePath(PathSegments);
        }
    }
}
