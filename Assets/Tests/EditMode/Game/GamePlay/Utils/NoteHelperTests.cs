using System;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Utils;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Utils
{
    /// <summary>Checks the sensor, position and slide OK helpers of <see cref="NoteHelper"/>.</summary>
    public class NoteHelperTests
    {
        /// <summary>The tolerance used for positions.</summary>
        private const float Tolerance = 1e-4f;

        /// <summary>Checks the mapping of touch area letters and indexes to sensors, including the fallback.</summary>
        [Test]
        public void GetSensor_MapsAreaLettersAndIndexes()
        {
            Assert.That(NoteHelper.GetSensor('A', 1), Is.EqualTo(SensorArea.A1));
            Assert.That(NoteHelper.GetSensor('A', 8), Is.EqualTo(SensorArea.A8));
            Assert.That(NoteHelper.GetSensor('B', 3), Is.EqualTo(SensorArea.B3));
            Assert.That(NoteHelper.GetSensor('C', 7), Is.EqualTo(SensorArea.C));
            Assert.That(NoteHelper.GetSensor('D', 2), Is.EqualTo(SensorArea.D2));
            Assert.That(NoteHelper.GetSensor('E', 8), Is.EqualTo(SensorArea.E8));
            Assert.That(NoteHelper.GetSensor(' ', 5), Is.EqualTo(SensorArea.A1));
        }

        /// <summary>Checks the distance of every sensor group from the centre.</summary>
        [Test]
        public void GetTouchAreaDistance_ReturnsTheRingDistance()
        {
            Assert.That(NoteHelper.GetTouchAreaDistance(SensorGroup.A), Is.EqualTo(4.0f));
            Assert.That(NoteHelper.GetTouchAreaDistance(SensorGroup.B), Is.EqualTo(2.2f));
            Assert.That(NoteHelper.GetTouchAreaDistance(SensorGroup.C), Is.EqualTo(0f));
            Assert.That(NoteHelper.GetTouchAreaDistance(SensorGroup.D), Is.EqualTo(4.1f));
            Assert.That(NoteHelper.GetTouchAreaDistance(SensorGroup.E), Is.EqualTo(3.1f));
        }

        /// <summary>Checks the touch positions of every group, their distance and their angle.</summary>
        [Test]
        public void GetTouchAreaPosition_PlacesEveryGroupOnItsRing()
        {
            AssertPosition(NoteHelper.GetTouchAreaPosition(SensorArea.C), Vector3.zero);
            AssertPosition(NoteHelper.GetTouchAreaPosition(SensorArea.A1), Polar(4.0f, (-1 * Mathf.PI / 4) + (Mathf.PI * 5 / 8)));
            AssertPosition(NoteHelper.GetTouchAreaPosition(SensorArea.B3), Polar(2.2f, (-3 * Mathf.PI / 4) + (Mathf.PI * 5 / 8)));
            AssertPosition(NoteHelper.GetTouchAreaPosition(SensorArea.D1), Polar(4.1f, (-1 * Mathf.PI / 4) + (Mathf.PI * 6 / 8)));
            AssertPosition(NoteHelper.GetTouchAreaPosition(SensorArea.E5), Polar(3.1f, (-5 * Mathf.PI / 4) + (Mathf.PI * 6 / 8)));
        }

        /// <summary>Checks that the letter overload matches the sensor overload and rejects unknown letters.</summary>
        [Test]
        public void GetTouchAreaPosition_LetterOverloadMatchesSensorOverload()
        {
            Assert.That(NoteHelper.GetTouchAreaPosition(2, 'A'), Is.EqualTo(NoteHelper.GetTouchAreaPosition(SensorArea.A2)));
            Assert.That(NoteHelper.GetTouchAreaPosition(4, 'B'), Is.EqualTo(NoteHelper.GetTouchAreaPosition(SensorArea.B4)));
            Assert.That(NoteHelper.GetTouchAreaPosition(1, 'C'), Is.EqualTo(Vector3.zero));
            Assert.That(NoteHelper.GetTouchAreaPosition(6, 'D'), Is.EqualTo(NoteHelper.GetTouchAreaPosition(SensorArea.D6)));
            Assert.That(NoteHelper.GetTouchAreaPosition(8, 'E'), Is.EqualTo(NoteHelper.GetTouchAreaPosition(SensorArea.E8)));
            Assert.Throws<ArgumentOutOfRangeException>(() => NoteHelper.GetTouchAreaPosition(1, 'F'));
        }

        /// <summary>Checks that touches face away from the centre and the centre touch is not rotated.</summary>
        [Test]
        public void GetTouchRoation_FacesAwayFromTheCentre()
        {
            Assert.That(NoteHelper.GetTouchRoation(Vector3.zero, SensorArea.C), Is.EqualTo(Quaternion.identity));

            var up = NoteHelper.GetTouchRoation(new Vector3(0, 2, 0), SensorArea.A1);
            var right = NoteHelper.GetTouchRoation(new Vector3(2, 0, 0), SensorArea.A3);

            Assert.That(Quaternion.Angle(up, Quaternion.Euler(0, 0, 0)), Is.EqualTo(0f).Within(0.01f));
            Assert.That(Quaternion.Angle(right, Quaternion.Euler(0, 0, -90)), Is.EqualTo(0f).Within(0.01f));
        }

        /// <summary>Checks the tap lane positions of the integer, float and sensor overloads.</summary>
        [Test]
        public void GetTapPosition_PlacesLanesAroundTheRing()
        {
            AssertPosition(NoteHelper.GetTapPosition(1, 4.8f), Polar(4.8f, 3 * 0.125f * Mathf.PI));
            AssertPosition(NoteHelper.GetTapPosition(5, 2f), Polar(2f, -5 * 0.125f * Mathf.PI));
            AssertPosition(NoteHelper.GetTapPosition(1.5f, 1f), Polar(1f, 2 * 0.125f * Mathf.PI));
            Assert.That(NoteHelper.GetTapPosition(SensorArea.A3, 3f), Is.EqualTo(NoteHelper.GetTapPosition(3, 3f)));
            Assert.That(NoteHelper.GetTapPosition(SensorArea.D1, 3f), Is.EqualTo(NoteHelper.GetTapPosition(0.5f, 3f)));
        }

        /// <summary>Checks that only A and D sensors have a tap position.</summary>
        /// <param name="area">A sensor outside the A and D groups.</param>
        [TestCase(SensorArea.B1)]
        [TestCase(SensorArea.C)]
        [TestCase(SensorArea.E4)]
        public void GetTapPosition_RejectsInnerSensors(SensorArea area)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NoteHelper.GetTapPosition(area, 1f));
        }

        /// <summary>Checks the slide OK shape chosen for each slide prefab name.</summary>
        [Test]
        public void GetSlideOKShapeFromSlideType_DistinguishesCurvesWifiAndStraights()
        {
            for (var i = 1; i <= 8; i++)
            {
                Assert.That(NoteHelper.GetSlideOKShapeFromSlideType($"circle{i}"), Is.EqualTo(SlideOKShape.Curv));
            }
            Assert.That(NoteHelper.GetSlideOKShapeFromSlideType("wifi"), Is.EqualTo(SlideOKShape.Wifi));
            Assert.That(NoteHelper.GetSlideOKShapeFromSlideType("line3"), Is.EqualTo(SlideOKShape.Str));
            Assert.That(NoteHelper.GetSlideOKShapeFromSlideType("ppqq1"), Is.EqualTo(SlideOKShape.Str));
            Assert.That(NoteHelper.GetSlideOKShapeFromSlideType(string.Empty), Is.EqualTo(SlideOKShape.Str));
        }

        /// <summary>Creates a position from a distance and an angle.</summary>
        /// <param name="distance">The distance from the centre.</param>
        /// <param name="angle">The angle in radians.</param>
        /// <returns>The position.</returns>
        private static Vector3 Polar(float distance, float angle)
        {
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }

        /// <summary>Asserts that two positions are equal within <see cref="Tolerance"/>.</summary>
        /// <param name="actual">The actual position.</param>
        /// <param name="expected">The expected position.</param>
        private static void AssertPosition(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance), $"x of {actual}");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance), $"y of {actual}");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Tolerance), $"z of {actual}");
        }
    }
}
