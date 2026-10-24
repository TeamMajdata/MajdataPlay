using System;
using MajdataPlay.Scenes.Game.Parsing;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>Checks the formatting, rotation and mirroring of slide codes by <see cref="SlideCodeShape"/>.</summary>
    public class SlideCodeShapeTests
    {
        /// <summary>The tolerance used for path positions.</summary>
        private const double Tolerance = 1e-6;

        /// <summary>
        /// Slide codes that use every command, every kind of circle and every orbit transfer, including the examples of
        /// the slide code documentation.
        /// </summary>
        private static readonly string[] s_shapes =
        {
            "1K3", "1Q9K3", "1P9K7", "1CK1", "1P3K6", "1P3P3K4", "1Q8K4", "1P0K6", "1Q0K4", "1B7B3K5", "1A7K2",
            "1P5K7", "1B46K1", "6Q0380K4", "8B63K1", "4Q53175K5", "5P98CQ49K1", "4Q37K5", "8Q79K3", "1Q892K8",
            "4P2B8P7K4", "1Q2CP6K5", "1P0P0P0K8", "2P4K7", "3Q1K6"
        };

        /// <summary>Checks that formatting parsed commands writes every command letter and expands shorthand.</summary>
        /// <param name="code">The slide code to parse.</param>
        /// <param name="expected">The expected slide code.</param>
        [TestCase("1P33K4", "1P3P3K4")]
        [TestCase("1A357K1", "1A3A5A7K1")]
        [TestCase("5Q9A1P98CQ49K5", "5Q9A1P9P8CQ4Q9K5")]
        [TestCase("1CK5", "1CK5")]
        public void Format_ExpandsShorthand(string code, string expected)
        {
            Assert.That(SlideCodeShape.Format(SlideCodeParser.ParseCommands(code)), Is.EqualTo(expected));
        }

        /// <summary>Checks that formatting needs at least the start key.</summary>
        [Test]
        public void Format_RejectsEmptyCommands()
        {
            Assert.Throws<ArgumentException>(() => SlideCodeShape.Format(Array.Empty<SlideCodeParser.Command>()));
        }

        /// <summary>Checks that rotation moves keys and side circles but keeps the centre and the two central circles.</summary>
        /// <param name="code">The slide code.</param>
        /// <param name="diff">The number of keys to rotate by.</param>
        /// <param name="expected">The expected slide code.</param>
        [TestCase("1K4", 2, "3K6")]
        [TestCase("3K6", -2, "1K4")]
        [TestCase("1K4", 10, "3K6")]
        [TestCase("1K4", -7, "2K5")]
        [TestCase("1P3K6", 4, "5P7K2")]
        [TestCase("1Q9K3", 2, "3Q9K5")]
        [TestCase("1Q0K4", 4, "5Q0K8")]
        [TestCase("1CK5", 1, "2CK6")]
        [TestCase("1B7B3K5", 3, "4B2B6K8")]
        public void Rotate_MovesKeysAndSideCircles(string code, int diff, string expected)
        {
            Assert.That(SlideCodeShape.Rotate(code, diff), Is.EqualTo(expected));
        }

        /// <summary>Checks that mirroring about the key 1 axis maps keys and side circles and swaps the orbit direction.</summary>
        /// <param name="code">The slide code.</param>
        /// <param name="expected">The expected slide code.</param>
        [TestCase("1P9K3", "1Q9K7")]
        [TestCase("1P3K6", "1Q8K4")]
        [TestCase("1P1K2", "1Q2K8")]
        [TestCase("1B7B3K5", "1B3B7K5")]
        [TestCase("1Q0K4", "1P0K6")]
        [TestCase("1CK3", "1CK7")]
        [TestCase("3K6", "7K4")]
        public void Mirror_MapsKeysCirclesAndDirections(string code, string expected)
        {
            Assert.That(SlideCodeShape.Mirror(code), Is.EqualTo(expected));
            Assert.That(SlideCodeShape.Mirror(expected), Is.EqualTo(code), "Mirroring twice restores the code");
        }

        /// <summary>Checks that normalizing rotates a code to key 1 and reports the rotation back to its start key.</summary>
        /// <param name="code">The slide code.</param>
        /// <param name="expected">The expected slide code starting at key 1.</param>
        /// <param name="expectedRotation">The expected rotation.</param>
        [TestCase("3K6", "1K4", 2)]
        [TestCase("1P3K6", "1P3K6", 0)]
        [TestCase("8Q7K5", "1Q8K6", 7)]
        [TestCase("5Q0K8", "1Q0K4", 4)]
        public void Normalize_RotatesToKeyOne(string code, string expected, int expectedRotation)
        {
            var normalized = SlideCodeShape.Normalize(code, out var rotation);

            Assert.That(normalized, Is.EqualTo(expected));
            Assert.That(rotation, Is.EqualTo(expectedRotation));
            Assert.That(SlideCodeShape.Rotate(normalized, rotation), Is.EqualTo(SlideCodeShape.Rotate(code, 0)));
        }

        /// <summary>
        /// Checks that rotating and mirroring the commands moves the parsed path exactly like rotating and reflecting
        /// the path itself, for every rotation, with and without mirroring.
        /// </summary>
        /// <param name="code">The slide code.</param>
        [Test]
        public void RotateAndMirror_MoveThePathLikeARotoreflection([ValueSource(nameof(s_shapes))] string code)
        {
            var length = SlideCodeParser.Parse(code).GetPathLength();
            foreach (var isMirrored in new[] { false, true })
            {
                for (var rotation = 0; rotation < 8; rotation++)
                {
                    var transformedCode = SlideCodeShape.Rotate(isMirrored ? SlideCodeShape.Mirror(code) : code, rotation);
                    var transformed = SlideCodeParser.Parse(transformedCode);
                    var expected = SlideCodeParser.Parse(code);
                    expected.SetRotoreflection(isMirrored, rotation);

                    Assert.That(transformed.GetPathLength(), Is.EqualTo(length).Within(Tolerance), transformedCode);
                    for (var i = 0; i <= 40; i++)
                    {
                        var t = i / 40.0;
                        var distance = (transformed.GetPointAt(t) - expected.GetPointAt(t)).Magnitude;
                        Assert.That(distance, Is.LessThan(Tolerance), $"{code} -> {transformedCode} at t = {t}");
                    }
                }
            }
        }
    }
}
