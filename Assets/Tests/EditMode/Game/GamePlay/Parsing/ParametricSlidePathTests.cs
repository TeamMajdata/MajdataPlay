using System;
using System.Numerics;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide.Segments;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>Checks segment lookup, length accounting, rotoreflection and end shape of <see cref="ParametricSlidePath"/>.</summary>
    public class ParametricSlidePathTests
    {
        /// <summary>The tolerance used for floating point geometry.</summary>
        private const double Tolerance = 1e-9;

        /// <summary>Checks that a path needs at least one segment.</summary>
        [Test]
        public void Constructor_RejectsAnEmptySegmentList()
        {
            Assert.Throws<ArgumentException>(() => new ParametricSlidePath(Array.Empty<PathSegment>()));
        }

        /// <summary>Checks the accumulated lengths, fractions and total length of a two-segment path.</summary>
        [Test]
        public void Constructor_AccumulatesLengthsAndFractions()
        {
            var path = CreateLPath();

            Assert.That(path.Segments, Has.Length.EqualTo(2));
            Assert.That(path.AccumulatedLengths, Is.EqualTo(new[] { 1.0, 4.0 }).Within(Tolerance));
            Assert.That(path.Fractions, Is.EqualTo(new[] { 0.25, 1.0 }).Within(Tolerance));
            Assert.That(path.GetPathLength(), Is.EqualTo(4).Within(Tolerance));
        }

        /// <summary>Checks segment lookup at and around the boundaries, including values outside [0, 1].</summary>
        [Test]
        public void GetSegmentAt_FindsTheSegmentAndItsLocalParameter()
        {
            var path = CreateLPath();

            Assert.That(path.GetSegmentAt(-1, out var t0), Is.SameAs(path.Segments[0]));
            Assert.That(t0, Is.EqualTo(0));
            Assert.That(path.GetSegmentAt(2, out var t1), Is.SameAs(path.Segments[1]));
            Assert.That(t1, Is.EqualTo(1));
            Assert.That(path.GetSegmentAt(0.125, out var t2), Is.SameAs(path.Segments[0]));
            Assert.That(t2, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(path.GetSegmentAt(0.25, out var t3), Is.SameAs(path.Segments[0]), "An exact boundary belongs to the earlier segment");
            Assert.That(t3, Is.EqualTo(1).Within(Tolerance));
            Assert.That(path.GetSegmentAt(0.625, out var t4), Is.SameAs(path.Segments[1]));
            Assert.That(t4, Is.EqualTo(0.5).Within(Tolerance));
        }

        /// <summary>Checks uniform interpolation of points and tangents across segments.</summary>
        [Test]
        public void GetPointAndTangentAt_InterpolateUniformlyByLength()
        {
            var path = CreateLPath();

            AssertClose(path.GetPointAt(0), new Complex(0, 0));
            AssertClose(path.GetPointAt(0.25), new Complex(1, 0));
            AssertClose(path.GetPointAt(0.625), new Complex(1, 1.5));
            AssertClose(path.GetPointAt(1), new Complex(1, 3));
            AssertClose(path.GetTangentAt(0.1), new Complex(1, 0));
            AssertClose(path.GetTangentAt(0.9), new Complex(0, 1));
        }

        /// <summary>Checks rotation by 45 degree steps and reflection about the A1-C-A5 axis.</summary>
        [Test]
        public void SetRotoreflection_RotatesAndReflectsPointsAndTangents()
        {
            var path = new ParametricSlidePath(new PathSegment[] { new LineSegment(Complex.Zero, new Complex(1, 0)) });

            Assert.That(path.Reflected, Is.False);
            Assert.That(path.Rotated45CW, Is.EqualTo(0));

            path.SetRotoreflection(false, 2);

            Assert.That(path.Rotated45CW, Is.EqualTo(2));
            AssertClose(path.GetPointAt(1), new Complex(0, -1));
            AssertClose(path.GetTangentAt(0.5), new Complex(0, -1));

            path.SetRotoreflection(true, 0);

            Assert.That(path.Reflected, Is.True);
            var expected = Complex.FromPolarCoordinates(1, 135 / 180.0 * Math.PI);
            AssertClose(path.GetPointAt(1), expected);
        }

        /// <summary>Checks the end shape for straight, counter-clockwise and clockwise endings, with and without reflection.</summary>
        [Test]
        public void GetEndShape_DependsOnTheLastSegmentAndReflection()
        {
            var straight = new ParametricSlidePath(new PathSegment[] { new LineSegment(Complex.Zero, Complex.One) });
            var ccwArc = new ParametricSlidePath(new PathSegment[] { new ArcSegment(new ComplexCircle(Complex.Zero, 1), 0, 1) });
            var cwArc = new ParametricSlidePath(new PathSegment[] { new ArcSegment(new ComplexCircle(Complex.Zero, 1), 1, 0) });
            var ccwCircle = new ParametricSlidePath(new PathSegment[] { new CircleSegment(new ComplexCircle(Complex.Zero, 1), 0, true) });
            var cwCircle = new ParametricSlidePath(new PathSegment[] { new CircleSegment(new ComplexCircle(Complex.Zero, 1), 0, false) });

            Assert.That(straight.GetEndShape(), Is.EqualTo(SlideEndShape.Straight));
            Assert.That(ccwArc.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCCW));
            Assert.That(cwArc.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCW));
            Assert.That(ccwCircle.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCCW));
            Assert.That(cwCircle.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCW));

            ccwArc.SetRotoreflection(true, 0);
            ccwCircle.SetRotoreflection(true, 0);

            Assert.That(ccwArc.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCW));
            Assert.That(ccwCircle.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCW));
        }

        /// <summary>Creates a path going one unit right and then three units up.</summary>
        /// <returns>The path.</returns>
        private static ParametricSlidePath CreateLPath()
        {
            return new ParametricSlidePath(new PathSegment[]
            {
                new LineSegment(new Complex(0, 0), new Complex(1, 0)),
                new LineSegment(new Complex(1, 0), new Complex(1, 3)),
            });
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
