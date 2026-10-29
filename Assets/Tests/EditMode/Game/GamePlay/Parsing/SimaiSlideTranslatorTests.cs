using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>
    /// Checks the translation of simai slides into slide code by <see cref="SimaiSlideTranslator"/>, and that the
    /// translated shapes keep the predefined judge tables and slide results of the former slide prefabs.
    /// </summary>
    public class SimaiSlideTranslatorTests
    {
        /// <summary>The number of simai slide segments between two keys that the slide rules accept.</summary>
        private const int ValidSegmentCount = 608;

        /// <summary>The private helper type nested in <see cref="NoteLoader"/>.</summary>
        private static readonly Type s_helper = typeof(NoteLoader).GetNestedType("NoteCreateHelper", BindingFlags.NonPublic)!;

        /// <summary>The simai shapes written between a start key and an end key.</summary>
        private static readonly string[] s_shapes = { "-", ">", "<", "^", "v", "p", "q", "pp", "qq", "s", "z" };

        /// <summary>Builds the judge area lookup table used by the slide code algorithm.</summary>
        [SetUp]
        public void SetUp()
        {
            SlideDataBuilder.InitializeSlideAreaLookup();
        }

        /// <summary>Checks the slide code of every shape family, including the examples of the slide code documentation.</summary>
        /// <param name="segment">The simai segment.</param>
        /// <param name="expected">The expected slide code.</param>
        [TestCase("1-3", "1K3")]
        [TestCase("8-6", "8K6")]
        [TestCase("1>3", "1Q9K3")]
        [TestCase("1>1", "1Q9K1")]
        [TestCase("3>5", "3P9K5")]
        [TestCase("4<2", "4Q9K2")]
        [TestCase("1<7", "1P9K7")]
        [TestCase("1^3", "1Q9K3")]
        [TestCase("1^7", "1P9K7")]
        [TestCase("1v3", "1CK3")]
        [TestCase("1v1", "1CK1")]
        [TestCase("1p4", "1P0K4")]
        [TestCase("1q4", "1Q0K4")]
        [TestCase("1pp6", "1P3K6")]
        [TestCase("1pp4", "1P3P3K4")]
        [TestCase("1pp5", "1P3P3K5")]
        [TestCase("5pp4", "5P7K4")]
        [TestCase("3qq4", "3Q2K4")]
        [TestCase("1qq4", "1Q8K4")]
        [TestCase("1qq6", "1Q8Q8K6")]
        [TestCase("1s5", "1B7B3K5")]
        [TestCase("1z5", "1B3B7K5")]
        [TestCase("1V72", "1A7K2")]
        [TestCase("1V35", "1A3K5")]
        public void Translate_WritesTheSlideCodeOfEveryShape(string segment, string expected)
        {
            Assert.That(SimaiSlideTranslator.Translate(segment), Is.EqualTo(expected));
        }

        /// <summary>Checks that shapes that cannot connect their keys are rejected with the simai error messages.</summary>
        /// <param name="segment">The invalid segment.</param>
        [TestCase("1-2")]
        [TestCase("1-8")]
        [TestCase("1^5")]
        [TestCase("1^1")]
        [TestCase("1v5")]
        [TestCase("1s4")]
        [TestCase("1z3")]
        [TestCase("1V76")]
        [TestCase("1V34")]
        [TestCase("1V45")]
        public void Translate_RejectsImpossibleShapes(string segment)
        {
            var exception = Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.Translate(segment));

            Assert.That(exception!.Message, Does.Contain("エラー"));
        }

        /// <summary>Checks that malformed segments and unknown shapes are rejected.</summary>
        /// <param name="segment">The malformed segment.</param>
        [TestCase("1x3")]
        [TestCase("1-")]
        [TestCase("-3")]
        [TestCase("13")]
        [TestCase("1-9")]
        [TestCase("1-34")]
        [TestCase("1V3")]
        [TestCase("1-3[8:1]")]
        public void Translate_RejectsMalformedSegments(string segment)
        {
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.Translate(segment));
        }

        /// <summary>Checks that wifi slides, which slide code cannot express, and missing segments are rejected.</summary>
        [Test]
        public void Translate_RejectsWifiAndNull()
        {
            Assert.Throws<NotSupportedException>(() => SimaiSlideTranslator.Translate("1w5"));
            Assert.Throws<ArgumentNullException>(() => SimaiSlideTranslator.Translate(null!));
        }

        /// <summary>Checks that wifi slides to the opposite key are accepted.</summary>
        /// <param name="segment">The wifi segment.</param>
        [TestCase("1w5")]
        [TestCase("5w1")]
        [TestCase("3w7")]
        public void ValidateWifi_AcceptsTheOppositeKey(string segment)
        {
            Assert.DoesNotThrow(() => SimaiSlideTranslator.ValidateWifi(segment));
        }

        /// <summary>Checks that wifi slides to another key and other segments are rejected.</summary>
        [Test]
        public void ValidateWifi_RejectsOtherSegments()
        {
            var wrongEnd = Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.ValidateWifi("1w4"));
            Assert.That(wrongEnd!.Message, Does.Contain("エラー"));
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.ValidateWifi("1-5"));
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.ValidateWifi("1w"));
            Assert.Throws<ArgumentNullException>(() => SimaiSlideTranslator.ValidateWifi(null!));
        }

        /// <summary>
        /// Checks that the segments of a connection slide are joined into one slide code. Each segment keeps its own
        /// meaning: in 1&gt;4&gt;7 the second &gt; starts on the lower half and therefore turns counter-clockwise.
        /// </summary>
        /// <param name="segments">The segments, separated by commas.</param>
        /// <param name="expected">The expected slide code.</param>
        [TestCase("1-3", "1K3")]
        [TestCase("1-4,4q7", "1A4Q0K7")]
        [TestCase("1>4,4>7", "1Q9A4P9K7")]
        [TestCase("1pp6,6-2", "1P3A6K2")]
        [TestCase("1V35,5-7", "1A3A5K7")]
        [TestCase("1-3,3v1", "1A3CK1")]
        public void TranslateChain_JoinsTheSegments(string segments, string expected)
        {
            Assert.That(SimaiSlideTranslator.TranslateChain(segments.Split(',')), Is.EqualTo(expected));
        }

        /// <summary>Checks that empty, broken and wifi chains are rejected.</summary>
        [Test]
        public void TranslateChain_RejectsBrokenChains()
        {
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.TranslateChain(Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.TranslateChain(new[] { "1-4", "5-7" }));
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.TranslateChain(new[] { "1-4", "4-5" }));
            Assert.Throws<NotSupportedException>(() => SimaiSlideTranslator.TranslateChain(new[] { "1-4", "4w8" }));
            Assert.Throws<ArgumentNullException>(() => SimaiSlideTranslator.TranslateChain(null!));
        }

        /// <summary>Checks that a segment written as slide code is returned without shorthand.</summary>
        /// <param name="segment">The slide code segment.</param>
        /// <param name="expected">The expected slide code.</param>
        [TestCase("3Q9K4", "3Q9K4")]
        [TestCase("1P33K4", "1P3P3K4")]
        [TestCase("2K5", "2K5")]
        [TestCase("1A357K1", "1A3A5A7K1")]
        [TestCase("4CK8", "4CK8")]
        public void Translate_NormalizesSlideCodeSegments(string segment, string expected)
        {
            Assert.That(SimaiSlideTranslator.Translate(segment), Is.EqualTo(expected));
        }

        /// <summary>Checks that malformed slide code segments are rejected.</summary>
        /// <param name="segment">The malformed segment.</param>
        [TestCase("3Q9")]
        [TestCase("3Q9K")]
        [TestCase("3Q9K4^5")]
        [TestCase("3Q9K9")]
        [TestCase("9Q9K4")]
        [TestCase("3Q^9K4")]
        [TestCase("1C3K5")]
        public void Translate_RejectsMalformedSlideCodeSegments(string segment)
        {
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.Translate(segment));
        }

        /// <summary>
        /// Checks that simai segments and slide code segments chain into one slide code, in either order, as in
        /// <c>1^2^3Q9K4[1:100]</c> and <c>1Q9K3-7[1:1]</c>.
        /// </summary>
        /// <param name="segments">The segments, separated by commas.</param>
        /// <param name="expected">The expected slide code.</param>
        [TestCase("1^2,2^3,3Q9K4", "1Q9A2Q9A3Q9K4")]
        [TestCase("1Q9K3,3-7", "1Q9A3K7")]
        [TestCase("1-4,4A6K8", "1A4A6K8")]
        [TestCase("1Q9K3,3Q9K5", "1Q9A3Q9K5")]
        [TestCase("1A357K1,1>1", "1A3A5A7A1Q9K1")]
        public void TranslateChain_JoinsSlideCodeSegments(string segments, string expected)
        {
            var code = SimaiSlideTranslator.TranslateChain(segments.Split(','));

            Assert.That(code, Is.EqualTo(expected));
            Assert.DoesNotThrow(() => SlideCodeParser.Parse(code));
        }

        /// <summary>Checks which characters start a slide code segment.</summary>
        /// <param name="c">The character.</param>
        /// <param name="expected">Whether it is a slide code command.</param>
        [TestCase('A', true)]
        [TestCase('B', true)]
        [TestCase('C', true)]
        [TestCase('P', true)]
        [TestCase('Q', true)]
        [TestCase('K', true)]
        [TestCase('V', false)]
        [TestCase('p', false)]
        [TestCase('q', false)]
        [TestCase('-', false)]
        [TestCase('^', false)]
        [TestCase('3', false)]
        public void IsSlideCodeCommand_AcceptsOnlySlideCodeCommands(char c, bool expected)
        {
            Assert.That(SimaiSlideTranslator.IsSlideCodeCommand(c), Is.EqualTo(expected));
        }

        /// <summary>Checks that a slide code segment ends right after the end key that follows K.</summary>
        /// <param name="text">The slide note text.</param>
        /// <param name="commandIndex">The index of the first command.</param>
        /// <param name="expected">The index after the end key.</param>
        [TestCase("1^2^3Q9K4[1:100]", 5, 9)]
        [TestCase("1P3K6[8:1]", 1, 5)]
        [TestCase("1Q9K3-7[1:1]", 1, 5)]
        [TestCase("1A357K1>1[1:1]", 1, 7)]
        [TestCase("1Q9K3Q9K5[1:1]", 5, 9)]
        public void FindSlideCodeEnd_StopsAfterTheEndKey(string text, int commandIndex, int expected)
        {
            Assert.That(SimaiSlideTranslator.FindSlideCodeEnd(text, commandIndex), Is.EqualTo(expected));
        }

        /// <summary>Checks that slide code segments without a command, an end flag or an end key are rejected.</summary>
        /// <param name="text">The slide note text.</param>
        /// <param name="commandIndex">The index where the commands should start.</param>
        [TestCase("1^2[1:1]", 1)]
        [TestCase("1Q9", 1)]
        [TestCase("1Q9K", 1)]
        [TestCase("1Q9K[1:1]", 1)]
        [TestCase("1Q^9K4", 1)]
        [TestCase("1Q9K4", 5)]
        [TestCase("1Q9K4", -1)]
        public void FindSlideCodeEnd_RejectsMalformedSlideCode(string text, int commandIndex)
        {
            Assert.Throws<ArgumentException>(() => SimaiSlideTranslator.FindSlideCodeEnd(text, commandIndex));
        }

        /// <summary>
        /// Checks which slide notes are one slide code, built directly, and which are split into segments, including
        /// connection slides that contain slide code segments.
        /// </summary>
        /// <param name="text">The slide note text.</param>
        /// <param name="expected">Whether the note is one slide code.</param>
        [TestCase("1P3K6[8:1]", true)]
        [TestCase("1P3K6", true)]
        [TestCase("1P33K4[8:1]", true)]
        [TestCase("1P3K", true)]
        [TestCase("1^2^3Q9K4[1:100]", false)]
        [TestCase("1Q9K3-7[1:1]", false)]
        [TestCase("1Q9K3Q9K5[1:1]", false)]
        [TestCase("1-4[8:1]", false)]
        [TestCase("1V35[8:1]", false)]
        public void IsSingleSlideCode_SeparatesSlideCodeFromConnectionSlides(string text, bool expected)
        {
            Assert.That(SimaiSlideTranslator.IsSingleSlideCode(text), Is.EqualTo(expected));
        }

        /// <summary>Checks that the slide code helpers reject missing text.</summary>
        [Test]
        public void SlideCodeHelpers_RejectNull()
        {
            Assert.Throws<ArgumentNullException>(() => SimaiSlideTranslator.FindSlideCodeEnd(null!, 1));
            Assert.Throws<ArgumentNullException>(() => SimaiSlideTranslator.IsSingleSlideCode(null!));
        }

        /// <summary>
        /// Checks that a connection slide whose path is an official shape uses that predefined table: 1-3-5 draws the
        /// same path as 1V35.
        /// </summary>
        [Test]
        public void TranslateChain_OfficialPathUsesThePredefinedTable()
        {
            var code = SimaiSlideTranslator.TranslateChain(new[] { "1-3", "3-5" });

            Assert.That(SlideTables.TryMatchSlideCode(code, out var tableName, out var isMirrored, out _), Is.True);
            Assert.That(tableName, Is.EqualTo("L5"));
            Assert.That(isMirrored, Is.True);
        }

        /// <summary>
        /// Checks that the translated shape finds the predefined table of the former slide prefab, mirrored like the
        /// prefab and rotated to the start key.
        /// </summary>
        /// <param name="segment">The simai segment.</param>
        /// <param name="expectedTable">The table name of the former prefab.</param>
        /// <param name="expectedMirrored">Whether the former prefab was mirrored.</param>
        [TestCase("1-3", "line3", false)]
        [TestCase("1-7", "line7", false)]
        [TestCase("8-6", "line7", false)]
        [TestCase("1>3", "circle3", false)]
        [TestCase("1>1", "circle1", false)]
        [TestCase("3>5", "circle7", true)]
        [TestCase("4<2", "circle7", false)]
        [TestCase("1<7", "circle3", true)]
        [TestCase("1^3", "circle3", false)]
        [TestCase("1^7", "circle3", true)]
        [TestCase("1v3", "v3", false)]
        [TestCase("1v1", "v1", false)]
        [TestCase("1pp4", "ppqq4", false)]
        [TestCase("1qq4", "ppqq6", true)]
        [TestCase("1p4", "pq4", false)]
        [TestCase("1q4", "pq6", true)]
        [TestCase("1s5", "s", false)]
        [TestCase("1z5", "s", true)]
        [TestCase("1V75", "L5", false)]
        [TestCase("1V72", "L2", false)]
        [TestCase("1V35", "L5", true)]
        public void Translate_MatchesTheFormerSlidePrefab(string segment, string expectedTable, bool expectedMirrored)
        {
            var code = SimaiSlideTranslator.Translate(segment);

            Assert.That(SlideTables.TryMatchSlideCode(code, out var tableName, out var isMirrored, out var rotation), Is.True, code);
            Assert.That(tableName, Is.EqualTo(expectedTable));
            Assert.That(isMirrored, Is.EqualTo(expectedMirrored));
            Assert.That(rotation, Is.EqualTo(segment[0] - '1'));
        }

        /// <summary>
        /// Checks every valid simai segment from every key: it uses a predefined table, and the judge areas that the
        /// slide code algorithm finds on its path, OR areas included, are the areas of that table.
        /// </summary>
        [Test]
        public void Translate_EverySimaiSlideUsesItsPredefinedTable()
        {
            var count = 0;
            foreach (var segment in EnumerateSegments())
            {
                if (!TryTranslate(segment, out var code))
                {
                    continue;
                }
                count++;
                Assert.That(SlideTables.TryMatchSlideCode(code, out var tableName, out _, out var rotation), Is.True, $"{segment} = {code}");
                Assert.That(rotation, Is.EqualTo(segment[0] - '1'), segment);

                using var table = SlideTables.FindTableBySlideCode(code)!;
                var generated = SlideDataBuilder.BuildSlideAreas(SlideCodeParser.Parse(code)).Select(DescribeArea);
                var predefined = table.JudgeQueue.ToArray().Select(x => DescribeArea(x.IncludedAreas.ToArray()));
                Assert.That(generated, Is.EqualTo(predefined), $"{segment} = {code} ({tableName})");
            }
            Assert.That(count, Is.EqualTo(ValidSegmentCount));
        }

        /// <summary>
        /// Checks every valid simai segment from every key: the slide result of the translated slide faces the same way
        /// as the former slide prefab chose from the simai text.
        /// </summary>
        [Test]
        public void Translate_KeepsTheSlideResultFacing()
        {
            foreach (var segment in EnumerateSegments())
            {
                if (!TryTranslate(segment, out var code))
                {
                    continue;
                }
                var metadata = ExtendSlideHelper.CreateSlideEntry(SlideCodeParser.Parse(code));
                var isJustR = metadata.OkType == SlideOkType.StraightR || metadata.OkType == SlideOkType.CircleR;
                var arguments = new object?[] { segment, 0 };
                var expected = (bool)PrivateMember.InvokeStatic(s_helper, "DetectJustType", arguments)!;

                Assert.That(isJustR, Is.EqualTo(expected), $"{segment} = {code}");
            }
        }

        /// <summary>Enumerates every simai segment between two keys, valid or not.</summary>
        /// <returns>The segments, for example 1-3 or 1V35.</returns>
        private static IEnumerable<string> EnumerateSegments()
        {
            for (var start = 1; start <= 8; start++)
            {
                for (var end = 1; end <= 8; end++)
                {
                    foreach (var shape in s_shapes)
                    {
                        yield return $"{start}{shape}{end}";
                    }
                    for (var turn = 1; turn <= 8; turn++)
                    {
                        yield return $"{start}V{turn}{end}";
                    }
                }
            }
        }

        /// <summary>Translates a segment, treating rejected shapes as absent.</summary>
        /// <param name="segment">The simai segment.</param>
        /// <param name="code">Receives the slide code, or an empty string when the shape is rejected.</param>
        /// <returns><see langword="true"/> when the segment is a valid slide.</returns>
        private static bool TryTranslate(string segment, out string code)
        {
            try
            {
                code = SimaiSlideTranslator.Translate(segment);
                return true;
            }
            catch (ArgumentException)
            {
                code = string.Empty;
                return false;
            }
        }

        /// <summary>Formats the sensors of a generated judge area independently of their order.</summary>
        /// <param name="area">The generated judge area.</param>
        /// <returns>The sensor names in enum order, joined with <c>|</c>.</returns>
        private static string DescribeArea(SlideAreaRawData area)
        {
            if (area.SensorB == -1)
            {
                return DescribeArea(new[] { (SensorArea)area.SensorA });
            }
            return DescribeArea(new[] { (SensorArea)area.SensorA, (SensorArea)area.SensorB });
        }

        /// <summary>Formats the sensors of a judge area independently of their order.</summary>
        /// <param name="sensors">The sensors that satisfy the area.</param>
        /// <returns>The sensor names in enum order, joined with <c>|</c>.</returns>
        private static string DescribeArea(SensorArea[] sensors)
        {
            return string.Join("|", sensors.OrderBy(x => x).Select(x => x.ToString()));
        }
    }
}
