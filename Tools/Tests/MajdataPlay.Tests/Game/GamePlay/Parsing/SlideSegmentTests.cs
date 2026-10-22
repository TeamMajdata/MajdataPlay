using System;
using System.Numerics;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide.Segments;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>Checks the line, arc and full circle path segments.</summary>
    public class SlideSegmentTests
    {
        /// <summary>The tolerance used for floating point geometry.</summary>
        private const double Tolerance = 1e-9;

        /// <summary>Checks interpolation, tangent and length of a straight segment.</summary>
        [Test]
        public void LineSegment_InterpolatesLinearly()
        {
            var segment = new LineSegment(new Complex(0, 0), new Complex(3, 4));

            Assert.That(segment.IsCurve, Is.False);
            Assert.That(segment.GetSegmentLength(), Is.EqualTo(5).Within(Tolerance));
            AssertClose(segment.GetPointAt(0), new Complex(0, 0));
            AssertClose(segment.GetPointAt(0.5), new Complex(1.5, 2));
            AssertClose(segment.GetPointAt(1), new Complex(3, 4));
            AssertClose(segment.GetTangentAt(0.3), new Complex(0.6, 0.8));
            Assert.That(segment.StartPoint, Is.EqualTo(new Complex(0, 0)));
            Assert.That(segment.EndPoint, Is.EqualTo(new Complex(3, 4)));
        }

        /// <summary>Checks a counter-clockwise arc: points, unit tangent perpendicular to the radius and arc length.</summary>
        [Test]
        public void ArcSegment_CounterClockwiseInterpolatesTheAngle()
        {
            var circle = new ComplexCircle(new Complex(1, 1), 2);
            var segment = new ArcSegment(circle, 0, Math.PI / 2);

            Assert.That(segment.IsCurve, Is.True);
            Assert.That(segment.GetSegmentLength(), Is.EqualTo(Math.PI).Within(Tolerance));
            AssertClose(segment.GetPointAt(0), new Complex(3, 1));
            AssertClose(segment.GetPointAt(1), new Complex(1, 3));
            AssertClose(segment.GetTangentAt(0), new Complex(0, 1));
            AssertClose(segment.GetTangentAt(1), new Complex(-1, 0));
            Assert.That(segment.Circle.Radius, Is.EqualTo(2));
            Assert.That(segment.StartRadian, Is.EqualTo(0));
            Assert.That(segment.EndRadian, Is.EqualTo(Math.PI / 2));
        }

        /// <summary>Checks that a clockwise arc reverses the tangent direction and keeps a positive length.</summary>
        [Test]
        public void ArcSegment_ClockwiseReversesTheTangent()
        {
            var segment = new ArcSegment(new ComplexCircle(Complex.Zero, 1), Math.PI / 2, 0);

            Assert.That(segment.GetSegmentLength(), Is.EqualTo(Math.PI / 2).Within(Tolerance));
            AssertClose(segment.GetPointAt(0), new Complex(0, 1));
            AssertClose(segment.GetPointAt(1), new Complex(1, 0));
            AssertClose(segment.GetTangentAt(0), new Complex(1, 0));
            AssertClose(segment.GetTangentAt(1), new Complex(0, -1));
        }

        /// <summary>Checks a full counter-clockwise turn: the start and end coincide and the length is the circumference.</summary>
        [Test]
        public void CircleSegment_CounterClockwiseTurnsOnce()
        {
            var segment = new CircleSegment(new ComplexCircle(Complex.Zero, 2), 0, true);

            Assert.That(segment.IsCurve, Is.True);
            Assert.That(segment.IsCcw, Is.True);
            Assert.That(segment.StartRadian, Is.EqualTo(0));
            Assert.That(segment.GetSegmentLength(), Is.EqualTo(4 * Math.PI).Within(Tolerance));
            AssertClose(segment.GetPointAt(0), new Complex(2, 0));
            AssertClose(segment.GetPointAt(0.25), new Complex(0, 2));
            AssertClose(segment.GetPointAt(1), new Complex(2, 0));
            AssertClose(segment.GetTangentAt(0), new Complex(0, 1));
        }

        /// <summary>Checks a full clockwise turn: a quarter of the way is below the centre and the tangent points down.</summary>
        [Test]
        public void CircleSegment_ClockwiseTurnsTheOtherWay()
        {
            var segment = new CircleSegment(new ComplexCircle(Complex.Zero, 2), 0, false);

            Assert.That(segment.IsCcw, Is.False);
            AssertClose(segment.GetPointAt(0.25), new Complex(0, -2));
            AssertClose(segment.GetTangentAt(0), new Complex(0, -1));
            Assert.That(segment.Circle.Radius, Is.EqualTo(2));
        }

        /// <summary>Checks the default and updated parse marker and arrow spacing of a segment.</summary>
        [Test]
        public void PathSegment_StoresParseMarkerAndArrowDistance()
        {
            var segment = new LineSegment(Complex.Zero, Complex.One);

            Assert.That(segment.ParseMarker, Is.EqualTo(SlideParseMarker.None));
            Assert.That(segment.ArrowDistance, Is.EqualTo(SlideGeo.DefaultDistance));

            segment.SetParseMarker(SlideParseMarker.SmoothAlign);
            segment.SetArrowDistance(0.25);

            Assert.That(segment.ParseMarker, Is.EqualTo(SlideParseMarker.SmoothAlign));
            Assert.That(segment.ArrowDistance, Is.EqualTo(0.25));
        }

        /// <summary>Asserts that two complex numbers are equal within <see cref="Tolerance"/>.</summary>
        /// <param name="actual">The actual value.</param>
        /// <param name="expected">The expected value.</param>
        private static void AssertClose(Complex actual, Complex expected)
        {
            Assert.That(actual.Real, Is.EqualTo(expected.Real).Within(Tolerance), $"Real part of {actual}");
            Assert.That(actual.Imaginary, Is.EqualTo(expected.Imaginary).Within(Tolerance), $"Imaginary part of {actual}");
        }
    }
}
