using System;
using System.Numerics;
using MajdataPlay.Scenes.Game.Parsing;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>Checks the slide geometry model in <see cref="SlideGeo"/>.</summary>
    public class SlideGeoTests
    {
        /// <summary>The tolerance used for floating point geometry.</summary>
        private const double Tolerance = 1e-9;

        /// <summary>Checks the derived radii and distances against their closed forms.</summary>
        [Test]
        public void DerivedRadii_MatchTheirDefinitions()
        {
            Assert.That(SlideGeo.MainRadius, Is.EqualTo(4.8));
            Assert.That(SlideGeo.CenterRadius, Is.EqualTo(4.8 * Math.Cos(Math.PI * 3 / 8)).Within(Tolerance));
            Assert.That(SlideGeo.GroupBRadius, Is.EqualTo(SlideGeo.CenterRadius / Math.Cos(Math.PI / 8)).Within(Tolerance));
            Assert.That(SlideGeo.PPQQRadius, Is.EqualTo(2.2173108780270883).Within(Tolerance));
            Assert.That(SlideGeo.TransferRadius, Is.EqualTo(3.9358927230842613).Within(Tolerance));
            Assert.That(SlideGeo.DefaultDistance, Is.EqualTo(4.8 * Math.PI / 32).Within(Tolerance));
            Assert.That(SlideGeo.EdgeTransferRadian, Is.EqualTo(Math.PI / 4).Within(Tolerance));
        }

        /// <summary>Checks that A points lie on the judge circle, one eighth of a turn apart, starting at 67.5 degrees.</summary>
        [Test]
        public void PointGroupA_LiesOnTheJudgeCircleClockwise()
        {
            for (var i = 1; i <= 8; i++)
            {
                var point = SlideGeo.PointGroupA(i);
                Assert.That(point.Magnitude, Is.EqualTo(SlideGeo.MainRadius).Within(Tolerance), $"A{i} radius");
                var expected = Complex.FromPolarCoordinates(SlideGeo.MainRadius, Math.PI * (5.0 / 8.0 - i / 4.0));
                AssertClose(point, expected, $"A{i} position");
            }
            Assert.That(SlideGeo.PointGroupA(1).Phase, Is.EqualTo(Math.PI * 3 / 8).Within(Tolerance));
        }

        /// <summary>Checks that B points lie on the B radius in the same directions as the A points.</summary>
        [Test]
        public void PointGroupB_SharesTheDirectionOfGroupA()
        {
            for (var i = 1; i <= 8; i++)
            {
                var a = SlideGeo.PointGroupA(i);
                var b = SlideGeo.PointGroupB(i);
                Assert.That(b.Magnitude, Is.EqualTo(SlideGeo.GroupBRadius).Within(Tolerance));
                Assert.That(b.Phase, Is.EqualTo(a.Phase).Within(Tolerance));
            }
        }

        /// <summary>Checks the sensor index mapping of <see cref="SlideGeo.GetPoint"/>, including out-of-range indexes.</summary>
        [Test]
        public void GetPoint_MapsSensorIndexesToNodes()
        {
            for (var i = 0; i < 8; i++)
            {
                Assert.That(SlideGeo.GetPoint(i), Is.EqualTo(SlideGeo.PointGroupA(i + 1)));
                Assert.That(SlideGeo.GetPoint(i + 8), Is.EqualTo(SlideGeo.PointGroupB(i + 1)));
            }
            Assert.That(SlideGeo.GetPoint(16), Is.EqualTo(Complex.Zero));
            Assert.That(SlideGeo.PointCenter(), Is.EqualTo(Complex.Zero));
            Assert.That(SlideGeo.GetPoint(17), Is.EqualTo(Complex.Zero));
            Assert.That(SlideGeo.GetPoint(-1), Is.EqualTo(Complex.Zero));
        }

        /// <summary>Checks the centre, outer and ppqq circles returned by <see cref="SlideGeo.GetCircle"/>.</summary>
        [Test]
        public void GetCircle_ReturnsCenterOuterAndPpqqCircles()
        {
            var center = SlideGeo.GetCircle(0);
            Assert.That(center.Center, Is.EqualTo(Complex.Zero));
            Assert.That(center.Radius, Is.EqualTo(SlideGeo.CenterRadius));

            var outer = SlideGeo.GetCircle(9);
            Assert.That(outer.Center, Is.EqualTo(Complex.Zero));
            Assert.That(outer.Radius, Is.EqualTo(SlideGeo.MainRadius));

            for (var i = 1; i <= 8; i++)
            {
                var ppqq = SlideGeo.GetCircle(i);
                Assert.That(ppqq.Radius, Is.EqualTo(SlideGeo.PPQQRadius).Within(Tolerance));
                Assert.That(ppqq.Center.Magnitude, Is.EqualTo(SlideGeo.PPQQRadius).Within(Tolerance), "Every ppqq circle passes through C");
                var expected = Complex.FromPolarCoordinates(SlideGeo.PPQQRadius, Math.PI * (3.0 / 4.0 - i / 4.0));
                AssertClose(ppqq.Center, expected, $"ppqq circle {i} centre");
            }
        }

        /// <summary>Checks that the tangent angle produces a point where the line from the source touches the circle.</summary>
        [Test]
        public void CalcTangentAngle_ProducesTangentPointsOnBothSides()
        {
            var point = SlideGeo.PointGroupA(1);
            var circle = SlideGeo.GetCircle(0);
            foreach (var isCcw in new[] { true, false })
            {
                var angle = SlideGeo.CalcTangentAngle(point, circle, isCcw);
                var touch = circle.Center + Complex.FromPolarCoordinates(circle.Radius, angle);
                var radius = touch - circle.Center;
                var line = point - touch;
                var dot = (radius.Real * line.Real) + (radius.Imaginary * line.Imaginary);
                Assert.That(dot, Is.EqualTo(0).Within(1e-9), $"Tangent for isCcw={isCcw}");
                Assert.That(angle, Is.InRange(-Math.PI, Math.PI));
            }
            var ccw = SlideGeo.CalcTangentAngle(point, circle, true);
            var cw = SlideGeo.CalcTangentAngle(point, circle, false);
            Assert.That(ccw, Is.Not.EqualTo(cw).Within(1e-6));
        }

        /// <summary>Checks that every transfer circle touches the ppqq circle and ends on the judge circle.</summary>
        /// <param name="isCcw">The travel direction.</param>
        [TestCase(true)]
        [TestCase(false)]
        public void TransferOutData_ConnectsPpqqCirclesToTheJudgeCircle(bool isCcw)
        {
            for (var i = 1; i <= 8; i++)
            {
                var (circle, start, end) = SlideGeo.TransferOutData(i, isCcw);
                var ppqq = SlideGeo.GetCircle(i);
                Assert.That(circle.Radius, Is.EqualTo(SlideGeo.TransferRadius).Within(Tolerance));
                Assert.That(circle.Center.Magnitude + circle.Radius, Is.EqualTo(SlideGeo.MainRadius).Within(1e-9), "Internally tangent to the judge circle");
                var leave = ppqq.Center + Complex.FromPolarCoordinates(ppqq.Radius, start);
                Assert.That((leave - circle.Center).Magnitude, Is.EqualTo(circle.Radius).Within(1e-9), "Leaves the ppqq circle on the transfer circle");
                var arrive = circle.Center + Complex.FromPolarCoordinates(circle.Radius, end);
                Assert.That(arrive.Magnitude, Is.EqualTo(SlideGeo.MainRadius).Within(1e-9), "Arrives on the judge circle");
                Assert.That(start, Is.InRange(-Math.PI, Math.PI));
                Assert.That(end, Is.InRange(-Math.PI, Math.PI));
            }
        }

        /// <summary>Checks that the complex circle value type keeps its centre and radius.</summary>
        [Test]
        public void ComplexCircle_StoresCenterAndRadius()
        {
            var circle = new ComplexCircle(new Complex(1, 2), 3);

            Assert.That(circle.Center, Is.EqualTo(new Complex(1, 2)));
            Assert.That(circle.Radius, Is.EqualTo(3));
        }

        /// <summary>Asserts that two complex numbers are equal within <see cref="Tolerance"/>.</summary>
        /// <param name="actual">The actual value.</param>
        /// <param name="expected">The expected value.</param>
        /// <param name="message">The assertion message.</param>
        private static void AssertClose(Complex actual, Complex expected, string message)
        {
            Assert.That(actual.Real, Is.EqualTo(expected.Real).Within(Tolerance), message);
            Assert.That(actual.Imaginary, Is.EqualTo(expected.Imaginary).Within(Tolerance), message);
        }
    }
}
