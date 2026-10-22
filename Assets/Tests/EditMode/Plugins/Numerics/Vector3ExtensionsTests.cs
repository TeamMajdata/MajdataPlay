using MajdataPlay.Numerics;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Numerics
{
    /// <summary>Covers the point projection helpers in <see cref="Vector3Extensions"/>.</summary>
    public class Vector3ExtensionsTests
    {
        /// <summary>The tolerance used to compare vector components.</summary>
        private const float Tolerance = 1e-4f;

        /// <summary>Asserts that two vectors are equal within <see cref="Tolerance"/>.</summary>
        /// <param name="actual">The computed vector.</param>
        /// <param name="expected">The expected vector.</param>
        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Tolerance));
        }

        /// <summary>Checks that a positive distance moves the point away from the world origin along its direction.</summary>
        [Test]
        public void GetPoint_PositiveDistance_ExtendsAwayFromOrigin()
        {
            var point = new Vector3(3, 4, 0).GetPoint(5);

            AssertVector(point, new Vector3(6, 8, 0));
        }

        /// <summary>Checks that a negative distance moves the point towards the world origin.</summary>
        [Test]
        public void GetPoint_NegativeDistance_MovesTowardsOrigin()
        {
            var point = new Vector3(0, 0, 10).GetPoint(-4);

            AssertVector(point, new Vector3(0, 0, 6));
        }

        /// <summary>Checks the origin overload with the world origin, where it matches the single-argument overload.</summary>
        [Test]
        public void GetPointWithOrigin_AtWorldOrigin_MatchesSimpleOverload()
        {
            var source = new Vector3(3, 4, 0);

            AssertVector(source.GetPoint(Vector3.zero, 5), source.GetPoint(5));
        }

        /// <summary>
        /// Checks that the origin overload returns the documented point: on the line through the origin and the source,
        /// the given distance beyond the source.
        /// </summary>
        [Test]
        public void GetPointWithOrigin_ReturnsPointOnLineThroughOrigin()
        {
            var point = new Vector3(4, 5, 0).GetPoint(new Vector3(1, 1, 0), 5);

            AssertVector(point, new Vector3(7, 9, 0));
        }
    }
}
