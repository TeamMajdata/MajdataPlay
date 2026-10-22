using System;
using System.Collections.Generic;
using MajdataPlay.IO;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the touch area geometry helpers in <see cref="SensorAreaExtensions"/> and <see cref="ButtonZoneExtensions"/>.</summary>
    public class SensorAreaExtensionsTests
    {
        /// <summary>The ring groups whose areas are numbered 1 to 8.</summary>
        private static readonly SensorGroup[] s_ringGroups = { SensorGroup.A, SensorGroup.B, SensorGroup.D, SensorGroup.E };

        /// <summary>Checks the group of every area.</summary>
        [Test]
        public void GetGroup_ReturnsTheGroupOfEveryArea()
        {
            for (var i = 0; i <= (int)SensorArea.E8; i++)
            {
                var area = (SensorArea)i;
                var expected = area.ToString()[0] switch
                {
                    'A' => SensorGroup.A,
                    'B' => SensorGroup.B,
                    'C' => SensorGroup.C,
                    'D' => SensorGroup.D,
                    _ => SensorGroup.E,
                };
                Assert.That(area.GetGroup(), Is.EqualTo(expected), area.ToString());
            }
        }

        /// <summary>Checks the one-based index of every area inside its group.</summary>
        [Test]
        public void GetIndex_ReturnsTheNumberInTheAreaName()
        {
            for (var i = 0; i <= (int)SensorArea.E8; i++)
            {
                var area = (SensorArea)i;
                var name = area.ToString();
                var expected = name.Length == 1 ? 1 : name[1] - '0';
                Assert.That(area.GetIndex(), Is.EqualTo(expected), name);
            }
        }

        /// <summary>Checks that values beyond E8 are rejected by the group, index and diff helpers.</summary>
        [Test]
        public void InvalidArea_Throws()
        {
            var invalid = (SensorArea)((int)SensorArea.E8 + 1);

            Assert.Throws<InvalidOperationException>(() => invalid.GetGroup());
            Assert.Throws<InvalidOperationException>(() => invalid.GetIndex());
            Assert.Throws<InvalidOperationException>(() => invalid.Diff(1));
        }

        /// <summary>Checks that diff rotates inside each ring group, wraps around and accepts negative steps.</summary>
        [Test]
        public void Diff_RotatesInsideTheGroup()
        {
            foreach (var group in s_ringGroups)
            {
                for (var index = 1; index <= 8; index++)
                {
                    for (var diff = -10; diff <= 10; diff++)
                    {
                        var expectedIndex = Mod8(index - 1 + diff) + 1;
                        var actual = Area(group, index).Diff(diff);
                        Assert.That(actual, Is.EqualTo(Area(group, expectedIndex)), $"{group}{index} {diff}");
                    }
                }
            }
        }

        /// <summary>Checks that the center area never moves.</summary>
        [Test]
        public void Diff_CenterStaysInPlace()
        {
            Assert.That(SensorArea.C.Diff(3), Is.EqualTo(SensorArea.C));
            Assert.That(SensorArea.C.Diff(8), Is.EqualTo(SensorArea.C));
        }

        /// <summary>Checks collinearity between every pair of areas.</summary>
        [Test]
        public void IsCollinearWith_MatchesTheSameOrOppositeAxis()
        {
            foreach (var source in AllAreas())
            {
                foreach (var target in AllAreas())
                {
                    bool expected;
                    if (source == SensorArea.C || target == SensorArea.C)
                    {
                        expected = true;
                    }
                    else if (IsOuterOrInnerRing(source) != IsOuterOrInnerRing(target))
                    {
                        expected = false;
                    }
                    else
                    {
                        var difference = Math.Abs(source.GetIndex() - target.GetIndex());
                        expected = difference == 0 || difference == 4;
                    }
                    Assert.That(source.IsCollinearWith(target), Is.EqualTo(expected), $"{source} {target}");
                }
            }
        }

        /// <summary>Checks that mirroring across the axis of another area matches the reflection of their angles.</summary>
        [Test]
        public void Mirror_ReflectsAcrossTheBaseLine()
        {
            foreach (var source in AllAreas())
            {
                foreach (var baseLine in AllAreas())
                {
                    var actual = source.Mirror(baseLine);
                    if (source == SensorArea.C || source.IsCollinearWith(baseLine))
                    {
                        Assert.That(actual, Is.EqualTo(source), $"{source} {baseLine}");
                        continue;
                    }
                    var expectedAngle = Mod16((2 * HalfStepAngle(baseLine)) - HalfStepAngle(source));
                    Assert.That(actual.GetGroup() == source.GetGroup(), Is.True, $"{source} {baseLine} keeps group");
                    Assert.That(HalfStepAngle(actual), Is.EqualTo(expectedAngle), $"{source} {baseLine}");
                }
            }
        }

        /// <summary>Checks that mirroring the same base line twice restores the area.</summary>
        [Test]
        public void Mirror_IsAnInvolution()
        {
            foreach (var source in AllAreas())
            {
                foreach (var baseLine in AllAreas())
                {
                    Assert.That(source.Mirror(baseLine).Mirror(baseLine), Is.EqualTo(source), $"{source} {baseLine}");
                }
            }
        }

        /// <summary>Checks the left and right relations, which are exclusive for non-collinear areas.</summary>
        [Test]
        public void IsLeftOfAndIsRightOf_AreExclusiveAndFalseOnTheAxis()
        {
            foreach (var source in AllAreas())
            {
                foreach (var target in AllAreas())
                {
                    if (source == SensorArea.C || target == SensorArea.C)
                    {
                        continue;
                    }
                    var left = source.IsLeftOf(target);
                    var right = source.IsRightOf(target);
                    if (source.IsCollinearWith(target))
                    {
                        Assert.That(left || right, Is.False, $"{source} {target}");
                    }
                    else
                    {
                        Assert.That(left, Is.Not.EqualTo(right), $"{source} {target}");
                    }
                }
            }
        }

        /// <summary>Checks representative left and right relations against the cabinet layout.</summary>
        /// <param name="source">The area being classified.</param>
        /// <param name="target">The reference area.</param>
        /// <param name="expectedLeft">Whether the source lies on the left of the reference axis.</param>
        [TestCase(SensorArea.A8, SensorArea.A1, true)]
        [TestCase(SensorArea.A2, SensorArea.A1, false)]
        [TestCase(SensorArea.B6, SensorArea.A1, true)]
        [TestCase(SensorArea.D2, SensorArea.A1, false)]
        [TestCase(SensorArea.D1, SensorArea.A1, true)]
        [TestCase(SensorArea.A1, SensorArea.D1, false)]
        [TestCase(SensorArea.A8, SensorArea.D1, true)]
        [TestCase(SensorArea.E3, SensorArea.D1, false)]
        public void IsLeftOf_ClassifiesRepresentativeAreas(SensorArea source, SensorArea target, bool expectedLeft)
        {
            Assert.That(source.IsLeftOf(target), Is.EqualTo(expectedLeft));
        }

        /// <summary>Checks that the center area cannot be classified as left or right.</summary>
        [Test]
        public void LeftRight_WithCenter_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => SensorArea.C.IsLeftOf(SensorArea.A1));
            Assert.Throws<InvalidOperationException>(() => SensorArea.A1.IsLeftOf(SensorArea.C));
            Assert.Throws<InvalidOperationException>(() => SensorArea.C.IsRightOf(SensorArea.A1));
            Assert.Throws<InvalidOperationException>(() => SensorArea.A1.IsRightOf(SensorArea.C));
        }

        /// <summary>Checks that the outer ring areas convert to button zones and back.</summary>
        [Test]
        public void ToButtonZoneAndBack_RoundTripsTheOuterRing()
        {
            for (var i = 0; i < 8; i++)
            {
                var area = (SensorArea)i;
                var zone = area.ToButtonZone();
                Assert.That(zone, Is.EqualTo((ButtonZone)i));
                Assert.That(zone.ToSensorArea(), Is.EqualTo(area));
            }
        }

        /// <summary>Checks that only A areas convert to button zones.</summary>
        [Test]
        public void ToButtonZone_NonOuterArea_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SensorArea.B1.ToButtonZone());
            Assert.Throws<ArgumentOutOfRangeException>(() => ((SensorArea)(-1)).ToButtonZone());
        }

        /// <summary>Checks the conversion of function buttons, which reuse the raw index, and the range validation.</summary>
        [Test]
        public void ToSensorArea_FunctionButtonsUseRawIndexAndInvalidZonesThrow()
        {
            Assert.That(ButtonZone.Test.ToSensorArea(), Is.EqualTo((SensorArea)(int)ButtonZone.Test));
            Assert.That(ButtonZone.P2.ToSensorArea(), Is.EqualTo((SensorArea)(int)ButtonZone.P2));
            Assert.Throws<ArgumentOutOfRangeException>(() => ((ButtonZone)12).ToSensorArea());
            Assert.Throws<ArgumentOutOfRangeException>(() => ((ButtonZone)(-1)).ToSensorArea());
        }

        /// <summary>Enumerates every valid area.</summary>
        /// <returns>The areas A1 to E8.</returns>
        private static IEnumerable<SensorArea> AllAreas()
        {
            for (var i = 0; i <= (int)SensorArea.E8; i++)
            {
                yield return (SensorArea)i;
            }
        }

        /// <summary>Builds an area from its group and one-based index.</summary>
        /// <param name="group">A ring group.</param>
        /// <param name="index">The index from 1 to 8.</param>
        /// <returns>The area.</returns>
        private static SensorArea Area(SensorGroup group, int index)
        {
            return (SensorArea)Enum.Parse(typeof(SensorArea), $"{group}{index}");
        }

        /// <summary>Gets whether an area lies on the A/B axes rather than the D/E axes.</summary>
        /// <param name="area">A non-center area.</param>
        /// <returns>True for A and B areas.</returns>
        private static bool IsOuterOrInnerRing(SensorArea area)
        {
            return area.GetGroup() is SensorGroup.A or SensorGroup.B;
        }

        /// <summary>Gets the clockwise angle of an area in steps of 22.5 degrees from the top.</summary>
        /// <param name="area">A non-center area.</param>
        /// <returns>A value from 0 to 15; A and B areas are odd and D and E areas are even.</returns>
        private static int HalfStepAngle(SensorArea area)
        {
            var index = area.GetIndex();
            return IsOuterOrInnerRing(area) ? (2 * index) - 1 : 2 * (index - 1);
        }

        /// <summary>Computes a non-negative remainder modulo 8.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The remainder from 0 to 7.</returns>
        private static int Mod8(int value)
        {
            return ((value % 8) + 8) % 8;
        }

        /// <summary>Computes a non-negative remainder modulo 16.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The remainder from 0 to 15.</returns>
        private static int Mod16(int value)
        {
            return ((value % 16) + 16) % 16;
        }
    }
}
