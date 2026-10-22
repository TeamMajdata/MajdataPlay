using System;
using System.Numerics;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide.Segments;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>Checks the chained path building operations of <see cref="SlidePathConstructor"/>.</summary>
    public class SlidePathConstructorTests
    {
        /// <summary>The tolerance used for floating point geometry.</summary>
        private const double Tolerance = 1e-9;

        /// <summary>Checks that building starts at a point or at a sensor node without any segment.</summary>
        [Test]
        public void BeginAt_StartsAtThePointWithoutSegments()
        {
            var byPoint = SlidePathConstructor.BeginAt(new Complex(1, 2));
            var bySensor = SlidePathConstructor.BeginAt(3);

            Assert.That(byPoint.CurrentEndPoint, Is.EqualTo(new Complex(1, 2)));
            Assert.That(byPoint.PathSegments, Is.Empty);
            Assert.That(bySensor.CurrentEndPoint, Is.EqualTo(SlideGeo.PointGroupA(4)));
        }

        /// <summary>Checks that straight lines are appended and move the current end point.</summary>
        [Test]
        public void LineToPoint_AppendsLineSegments()
        {
            var constructor = SlidePathConstructor.BeginAt(0)
                .LineToPoint(16)
                .LineToPoint(new Complex(1, 1));

            Assert.That(constructor.PathSegments, Has.Count.EqualTo(2));
            Assert.That(constructor.PathSegments[0], Is.TypeOf<LineSegment>());
            Assert.That(((LineSegment)constructor.PathSegments[0]).EndPoint, Is.EqualTo(Complex.Zero));
            Assert.That(constructor.CurrentEndPoint, Is.EqualTo(new Complex(1, 1)));
        }

        /// <summary>Checks that the parse marker is applied to the last segment only and ignored on an empty path.</summary>
        [Test]
        public void TrySetLastParseMarker_MarksTheLastSegment()
        {
            var empty = SlidePathConstructor.BeginAt(0);
            Assert.That(empty.TrySetLastParseMarker(SlideParseMarker.ForceAlign), Is.SameAs(empty));
            Assert.That(empty.PathSegments, Is.Empty);

            var constructor = SlidePathConstructor.BeginAt(0).LineToPoint(16).LineToPoint(4);
            constructor.TrySetLastParseMarker(SlideParseMarker.ForceAlign);

            Assert.That(constructor.PathSegments[0].ParseMarker, Is.EqualTo(SlideParseMarker.None));
            Assert.That(constructor.PathSegments[1].ParseMarker, Is.EqualTo(SlideParseMarker.ForceAlign));
        }

        /// <summary>Checks that a tangent approach ends on the target circle.</summary>
        [Test]
        public void TangentToCircle_EndsOnTheCircle()
        {
            var constructor = SlidePathConstructor.BeginAt(0).TangentToCircle(0, true);
            var circle = SlideGeo.GetCircle(0);

            Assert.That(constructor.PathSegments, Has.Count.EqualTo(1));
            Assert.That((constructor.CurrentEndPoint - circle.Center).Magnitude, Is.EqualTo(circle.Radius).Within(Tolerance));
        }

        /// <summary>Checks counter-clockwise and clockwise arcs to an absolute angle.</summary>
        [Test]
        public void ArcToAngle_TravelsInTheRequestedDirection()
        {
            var ccw = SlidePathConstructor.BeginAt(new Complex(1, 0)).ArcToAngle(Complex.Zero, -Math.PI / 2, true, false);
            var ccwArc = (ArcSegment)ccw.PathSegments[0];
            Assert.That(ccwArc.EndRadian - ccwArc.StartRadian, Is.EqualTo(Math.PI * 1.5).Within(Tolerance));
            AssertClose(ccw.CurrentEndPoint, new Complex(0, -1));

            var cw = SlidePathConstructor.BeginAt(new Complex(1, 0)).ArcToAngle(Complex.Zero, Math.PI / 2, false, false);
            var cwArc = (ArcSegment)cw.PathSegments[0];
            Assert.That(cwArc.StartRadian - cwArc.EndRadian, Is.EqualTo(Math.PI * 1.5).Within(Tolerance));
            AssertClose(cw.CurrentEndPoint, new Complex(0, 1));
        }

        /// <summary>Checks that a zero-length arc is skipped on request and otherwise turns a whole circle.</summary>
        /// <param name="isCcw">The travel direction.</param>
        [TestCase(true)]
        [TestCase(false)]
        public void ArcToAngle_HandlesZeroLengthArcs(bool isCcw)
        {
            var skipped = SlidePathConstructor.BeginAt(new Complex(1, 0)).ArcToAngle(Complex.Zero, 0, isCcw, true);
            Assert.That(skipped.PathSegments, Is.Empty);

            var full = SlidePathConstructor.BeginAt(new Complex(1, 0)).ArcToAngle(Complex.Zero, 0, isCcw, false);
            Assert.That(full.PathSegments, Has.Count.EqualTo(1));
            Assert.That(full.PathSegments[0].GetSegmentLength(), Is.EqualTo(2 * Math.PI).Within(Tolerance));
            AssertClose(full.CurrentEndPoint, new Complex(1, 0));
        }

        /// <summary>Checks the circle index overloads of the arc and full circle operations.</summary>
        [Test]
        public void CircleIndexOverloads_UseTheCircleCentre()
        {
            var arc = SlidePathConstructor.BeginAt(0).ArcToAngle(9, SlideGeo.PointGroupA(3).Phase, false, false);
            Assert.That(arc.PathSegments[0].GetSegmentLength(), Is.EqualTo(SlideGeo.MainRadius * Math.PI / 2).Within(1e-6));

            var circle = SlidePathConstructor.BeginAt(0).FullCircle(9, true);
            Assert.That(circle.PathSegments[0], Is.TypeOf<CircleSegment>());
            Assert.That(circle.CurrentEndPoint, Is.EqualTo(SlideGeo.PointGroupA(1)), "A full circle keeps the end point");
            Assert.That(circle.PathSegments[0].GetSegmentLength(), Is.EqualTo(2 * Math.PI * SlideGeo.MainRadius).Within(1e-6));
        }

        /// <summary>Checks that an arc towards a target ends where the remaining line to the target is tangent.</summary>
        [Test]
        public void ArcToTangentTowards_StopsAtTheTangentPoint()
        {
            var constructor = SlidePathConstructor.BeginAt(0)
                .TangentToCircle(0, true)
                .ArcToTangentTowards(4, 0, true);
            var touch = constructor.CurrentEndPoint;
            var target = SlideGeo.GetPoint(4);
            var radius = touch;
            var line = target - touch;
            var dot = (radius.Real * line.Real) + (radius.Imaginary * line.Imaginary);

            Assert.That(constructor.PathSegments, Has.Count.EqualTo(2));
            Assert.That(constructor.PathSegments[1], Is.TypeOf<ArcSegment>());
            Assert.That(dot, Is.EqualTo(0).Within(1e-6));
        }

        /// <summary>Checks the three radius cases of the external tangent transfer between circles.</summary>
        [Test]
        public void ExternTangentTransfer_EndsOnTheTargetCircleForEveryRadiusCase()
        {
            var equal = SlidePathConstructor.BeginAt(SlideGeo.GetCircle(1).Center + SlideGeo.PPQQRadius)
                .ExternTangentTransfer(SlideGeo.GetCircle(1).Center, SlideGeo.GetCircle(2), true);
            AssertOnCircle(equal.CurrentEndPoint, SlideGeo.GetCircle(2));
            Assert.That(equal.PathSegments, Has.Count.EqualTo(2));

            var larger = SlidePathConstructor.BeginAt(new Complex(SlideGeo.CenterRadius, 0))
                .ExternTangentTransfer(Complex.Zero, SlideGeo.GetCircle(1), true);
            AssertOnCircle(larger.CurrentEndPoint, SlideGeo.GetCircle(1));

            var smaller = SlidePathConstructor.BeginAt(SlideGeo.GetCircle(1).Center + SlideGeo.PPQQRadius)
                .ExternTangentTransfer(SlideGeo.GetCircle(1).Center, SlideGeo.GetCircle(0), false);
            AssertOnCircle(smaller.CurrentEndPoint, SlideGeo.GetCircle(0));
        }

        /// <summary>Checks that the generated path contains every appended segment.</summary>
        [Test]
        public void GeneratePath_WrapsTheSegments()
        {
            var path = SlidePathConstructor.BeginAt(0).LineToPoint(16).LineToPoint(4).GeneratePath();

            Assert.That(path.Segments, Has.Length.EqualTo(2));
            Assert.That(path.GetPathLength(), Is.EqualTo(2 * SlideGeo.MainRadius).Within(Tolerance));
        }

        /// <summary>Checks that a point lies on a circle.</summary>
        /// <param name="point">The point.</param>
        /// <param name="circle">The circle.</param>
        private static void AssertOnCircle(Complex point, ComplexCircle circle)
        {
            Assert.That((point - circle.Center).Magnitude, Is.EqualTo(circle.Radius).Within(1e-6));
        }

        /// <summary>Asserts that two complex numbers are equal within <see cref="Tolerance"/>.</summary>
        /// <param name="actual">The actual value.</param>
        /// <param name="expected">The expected value.</param>
        private static void AssertClose(Complex actual, Complex expected)
        {
            Assert.That(actual.Real, Is.EqualTo(expected.Real).Within(1e-6), $"Real part of {actual}");
            Assert.That(actual.Imaginary, Is.EqualTo(expected.Imaginary).Within(1e-6), $"Imaginary part of {actual}");
        }
    }
}
