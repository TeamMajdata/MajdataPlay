using MajdataPlay.Numerics;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Numerics
{
    /// <summary>Covers the point and circle hit tests in <see cref="PolygonRaycasting"/>.</summary>
    public class PolygonRaycastingTests
    {
        /// <summary>A two by two square centred on the origin.</summary>
        private static readonly Vector2[] s_square =
        {
            new Vector2(-1, -1),
            new Vector2(1, -1),
            new Vector2(1, 1),
            new Vector2(-1, 1),
        };

        /// <summary>A concave L-shaped polygon whose notch is the upper right quadrant.</summary>
        private static readonly Vector2[] s_lShape =
        {
            new Vector2(0, 0),
            new Vector2(2, 0),
            new Vector2(2, 1),
            new Vector2(1, 1),
            new Vector2(1, 2),
            new Vector2(0, 2),
        };

        /// <summary>Checks the even-odd test for points inside, outside and in the notch of polygons.</summary>
        [Test]
        public void InPointInInternal_ClassifiesPoints()
        {
            Assert.That(PolygonRaycasting.InPointInInternal(s_square, Vector2.zero), Is.True);
            Assert.That(PolygonRaycasting.InPointInInternal(s_square, new Vector2(0.9f, -0.9f)), Is.True);
            Assert.That(PolygonRaycasting.InPointInInternal(s_square, new Vector2(2, 0)), Is.False);
            Assert.That(PolygonRaycasting.InPointInInternal(s_square, new Vector2(0, -3)), Is.False);
            Assert.That(PolygonRaycasting.InPointInInternal(s_lShape, new Vector2(0.5f, 1.5f)), Is.True);
            Assert.That(PolygonRaycasting.InPointInInternal(s_lShape, new Vector2(1.5f, 1.5f)), Is.False);
        }

        /// <summary>Checks that a vertex strictly closer than the radius is detected.</summary>
        [Test]
        public void IsVertDistance_DetectsNearbyVertex()
        {
            Assert.That(PolygonRaycasting.IsVertDistance(s_square, new Vector2(1.2f, 1.2f), 0.5f), Is.True);
            Assert.That(PolygonRaycasting.IsVertDistance(s_square, new Vector2(0, 0), 1f), Is.False);
            Assert.That(PolygonRaycasting.IsVertDistance(s_square, new Vector2(2, 1), 1f), Is.False);
        }

        /// <summary>Checks that a circle touching or crossing an edge intersects and a distant one does not.</summary>
        [Test]
        public void IsCircleIntersectingPolygonEdges_DetectsEdgeContact()
        {
            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(s_square, new Vector2(0, 1.5f), 0.5f), Is.True);
            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(s_square, new Vector2(1.4f, 0), 0.5f), Is.True);
            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(s_square, new Vector2(0, 3), 0.5f), Is.False);
            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(s_square, Vector2.zero, 0.5f), Is.False);
        }

        /// <summary>Checks that a degenerate edge between duplicated vertices is measured as a point.</summary>
        [Test]
        public void IsCircleIntersectingPolygonEdges_DegenerateEdge_UsesPointDistance()
        {
            var polygon = new[] { new Vector2(5, 5), new Vector2(5, 5) };

            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(polygon, new Vector2(5, 5.4f), 0.5f), Is.True);
            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(polygon, new Vector2(5, 6), 0.5f), Is.False);
        }

        /// <summary>Checks that projections beyond a segment's ends are clamped to the nearest end point.</summary>
        [Test]
        public void IsCircleIntersectingPolygonEdges_ClampsBeyondSegmentEnds()
        {
            var polygon = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0) };

            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(polygon, new Vector2(1.3f, 0), 0.5f), Is.True);
            Assert.That(PolygonRaycasting.IsCircleIntersectingPolygonEdges(polygon, new Vector2(-0.6f, 0), 0.5f), Is.False);
        }
    }
}
