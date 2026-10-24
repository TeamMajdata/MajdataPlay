using System;
using System.Collections.Generic;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>
    /// Checks that <see cref="SimaiMirror"/> moves slides written as slide code together with the rest of the chart, and
    /// that it still mirrors plain simai notes character by character.
    /// </summary>
    /// <remarks>
    /// Every mirror option moves the keys like a rotoreflection: a mirror about the line through key 1, the centre and
    /// key 5 followed by a clockwise rotation. A mirrored slide code must draw the original path moved by that
    /// rotoreflection, which <see cref="ParametricSlidePath.SetRotoreflection"/> computes from the path itself.
    /// </remarks>
    public class SimaiMirrorSlideCodeTests
    {
        /// <summary>The tolerance used for path positions and lengths.</summary>
        private const double Tolerance = 1e-6;

        /// <summary>The duration written after the slide codes of these tests.</summary>
        private const string Duration = "[8:1]";

        /// <summary>A chart of plain simai notes that uses every slide shape, touch, note flag and separator.</summary>
        private const string SimaiChart =
            "(150){8}1-5[4:1],2h[2:1],3>7[8:1],4<8[8:1]/5^1[8:1],6v2[8:1],7V13[8:1],8p4[8:1],\n" +
            "1q5[8:1],2pp6[8:1],3qq7[8:1],4s8[8:1],5z1[8:1],6w2[8:1],1-4-7[4:1]*-6[8:1],B1/C,D3,E2h[4:1],Ch[2:1]f,\n" +
            "1bx,2$,3$$,4@-8[8:1],5?-1[8:1],6!-2[8:1],7m,8-4b[8:1],12,<HS*0.5>3,4`5,(120){16}6-2[160#8:3],7-3[3##1.5],E";

        /// <summary><see cref="SimaiChart"/> mirrored left-right by the key maps.</summary>
        private const string SimaiChartLeftRight =
            "(150){8}8-4[4:1],7h[2:1],6<2[8:1],5>1[8:1]/4^8[8:1],3v7[8:1],2V86[8:1],1q5[8:1],\n" +
            "8p4[8:1],7qq3[8:1],6pp2[8:1],5z1[8:1],4s8[8:1],3w7[8:1],8-5-2[4:1]*-3[8:1],B8/C,D7,E8h[4:1],Ch[2:1]f,\n" +
            "8bx,7$,6$$,5@-1[8:1],4?-8[8:1],3!-7[8:1],2m,1-5b[8:1],87,<HS*0.5>6,5`4,(120){16}3-7[160#8:3],2-6[3##1.5],E";

        /// <summary><see cref="SimaiChart"/> mirrored upside down by the key maps.</summary>
        private const string SimaiChartUpsideDown =
            "(150){8}4-8[4:1],3h[2:1],2>6[8:1],1<5[8:1]/8^4[8:1],7v3[8:1],6V42[8:1],5q1[8:1],\n" +
            "4p8[8:1],3qq7[8:1],2pp6[8:1],1z5[8:1],8s4[8:1],7w3[8:1],4-1-6[4:1]*-7[8:1],B4/C,D3,E4h[4:1],Ch[2:1]f,\n" +
            "4bx,3$,2$$,1@-5[8:1],8?-4[8:1],7!-3[8:1],6m,5-1b[8:1],43,<HS*0.5>2,1`8,(120){16}7-3[160#8:3],6-2[3##1.5],E";

        /// <summary>
        /// Slide codes that use every command, every kind of circle and every orbit transfer, including the examples of
        /// the slide code documentation such as <c>5Q9A1P98CQ49K5</c> and its shorthand examples.
        /// </summary>
        private static readonly string[] s_slideCodes =
        {
            "1K3", "1Q9K3", "1P9K7", "1CK1", "1P3K6", "1P3P3K4", "1Q8K4", "1P0K6", "1Q0K4", "1B7B3K5", "1A7K2",
            "1P5K7", "1B46K1", "6Q0380K4", "8B63K1", "4Q53175K5", "5P98CQ49K1", "4Q37K5", "8Q79K3", "1Q892K8",
            "4P2B8P7K4", "1Q2CP6K5", "1P0P0P0K8", "2P4K7", "3Q1K6", "5Q9A1P98CQ49K5", "1P00000K8", "1A357K1"
        };

        /// <summary>The chart snippets of the configuration examples in the slide code documentation.</summary>
        private static readonly string[] s_documentedCharts =
        {
            "8B7B3K4[8:1]*B1B5K4[8:1]",
            "8P1CQ5K4[8:1]*Q8CP4K4[8:1]",
            "8Q8CP4K4[8:1]/1P2CQ6K5[8:1]",
            "6Q919K3b[1:1]",
            "7Q7CP6K6[1:1]/2P3CQ4K3[1:1]/8Q7CP5K5[1:1]/1P3CQ5K4[1:1]",
            "1P13CQ75K5[1:2]"
        };

        /// <summary>
        /// Checks that the rotoreflection used as the oracle for each option moves every key to the key that the simai key
        /// map of the option gives a tap on it; the left-right mirror, for example, moves key 1 to key 8.
        /// </summary>
        /// <param name="option">The mirror option.</param>
        [Test]
        public void Rotoreflection_MovesEveryKeyLikeTheKeyMap([Values] MirrorOption option)
        {
            var (isMirrored, rotation) = GetRotoreflection(option);
            for (var key = 1; key <= 8; key++)
            {
                var mappedKey = int.Parse(SimaiMirror.NoteMirrorHandle(key.ToString(), option));
                var path = SlideCodeParser.Parse($"{key}K{(key % 8) + 1}");
                path.SetRotoreflection(isMirrored, rotation);

                var distance = (path.GetPointAt(0) - SlideGeo.PointGroupA(mappedKey)).Magnitude;
                Assert.That(distance, Is.LessThan(Tolerance), $"{option} maps key {key} to key {mappedKey}");
            }
        }

        /// <summary>
        /// Checks that mirroring a slide code note keeps its duration and makes its code draw the original path moved by
        /// the rotoreflection of the option.
        /// </summary>
        /// <param name="option">The mirror option.</param>
        /// <param name="code">The slide code.</param>
        [Test]
        public void SlideCode_DrawsTheMovedPath([Values] MirrorOption option, [ValueSource(nameof(s_slideCodes))] string code)
        {
            var mirrored = SimaiMirror.NoteMirrorHandle(code + Duration, option);

            Assert.That(mirrored, Does.EndWith(Duration));
            AssertDrawsMovedPath(mirrored.Substring(0, mirrored.Length - Duration.Length), code, option);
        }

        /// <summary>
        /// Checks that a slide code joined to a simai slide by <c>*</c>, which is written without its start key and starts
        /// at the shared star head, draws the moved path while the simai slide is mirrored as on its own.
        /// </summary>
        /// <param name="option">The mirror option.</param>
        /// <param name="code">The slide code.</param>
        [Test]
        public void SameHeadSlideCode_DrawsTheMovedPath([Values] MirrorOption option, [ValueSource(nameof(s_slideCodes))] string code)
        {
            var startKey = code[0] - '0';
            var simaiSlide = $"{startKey}-{((startKey + 3) % 8) + 1}{Duration}";

            var slides = SimaiMirror.NoteMirrorHandle($"{simaiSlide}*{code.Substring(1)}{Duration}", option).Split('*');

            Assert.That(slides, Has.Length.EqualTo(2));
            Assert.That(slides[0], Is.EqualTo(SimaiMirror.NoteMirrorHandle(simaiSlide, option)));
            Assert.That(slides[1], Does.EndWith(Duration));
            var mirroredCode = slides[0][0] + slides[1].Substring(0, slides[1].Length - Duration.Length);
            AssertDrawsMovedPath(mirroredCode, code, option);
        }

        /// <summary>
        /// Checks that every slide of the chart snippets in the slide code documentation, including same-head slides and
        /// each groups, draws its original path moved by the rotoreflection of the option, while the duration and the
        /// flags after each code stay as written.
        /// </summary>
        /// <param name="option">The mirror option.</param>
        /// <param name="chart">The chart snippet.</param>
        [Test]
        public void DocumentedChart_DrawsTheMovedPaths([Values] MirrorOption option, [ValueSource(nameof(s_documentedCharts))] string chart)
        {
            var slides = GetSlides(chart);

            var mirroredSlides = GetSlides(SimaiMirror.NoteMirrorHandle(chart, option));

            Assert.That(mirroredSlides, Has.Count.EqualTo(slides.Count));
            for (var i = 0; i < slides.Count; i++)
            {
                Assert.That(mirroredSlides[i].Tail, Is.EqualTo(slides[i].Tail), slides[i].Code);
                AssertDrawsMovedPath(mirroredSlides[i].Code, slides[i].Code, option);
            }
        }

        /// <summary>
        /// Checks that mirroring a slide code changes only its keys, circles and orbit directions: shorthand, note flags,
        /// durations and whitespace keep their places.
        /// </summary>
        /// <param name="source">The slide code note.</param>
        /// <param name="option">The mirror option.</param>
        /// <param name="expected">The expected mirrored note.</param>
        [TestCase("1P3K6[8:1]", MirrorOption.Off, "1P3K6[8:1]")]
        [TestCase("1P3K6[8:1]", MirrorOption.LRMirror, "8Q7K3[8:1]")]
        [TestCase("1P3K6[8:1]", MirrorOption.UDMirror, "4Q3K7[8:1]")]
        [TestCase("1P33K4[8:1]", MirrorOption.LRMirror, "8Q77K5[8:1]")]
        [TestCase("1P33K4[160#8:3]", MirrorOption.UDMirror, "4Q33K1[160#8:3]")]
        [TestCase("1bP3K6[8:1]", MirrorOption.LRMirror, "8bQ7K3[8:1]")]
        [TestCase("1bP3K6[8:1]", MirrorOption.UDMirror, "4bQ3K7[8:1]")]
        [TestCase("1P3K6b[8:1]", MirrorOption.LRMirror, "8Q7K3b[8:1]")]
        [TestCase("1P3K6[8:1]b", MirrorOption.LRMirror, "8Q7K3[8:1]b")]
        [TestCase("1$P3K6[160#8:3]", MirrorOption.LRMirror, "8$Q7K3[160#8:3]")]
        [TestCase("1@P3K6[3##1.5]", MirrorOption.LRMirror, "8@Q7K3[3##1.5]")]
        [TestCase("1?P3K6[8:1]", MirrorOption.LRMirror, "8?Q7K3[8:1]")]
        [TestCase("1!P3K6[8:1]", MirrorOption.LRMirror, "8!Q7K3[8:1]")]
        [TestCase("1xP3K6m[8:1]", MirrorOption.LRMirror, "8xQ7K3m[8:1]")]
        [TestCase("1P3 K6[8:1]", MirrorOption.LRMirror, "8Q7 K3[8:1]")]
        public void SlideCode_KeepsShorthandFlagsAndDurations(string source, MirrorOption option, string expected)
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(source, option), Is.EqualTo(expected));
        }

        /// <summary>
        /// Checks that slide code next to other notes, in the same chart, as a same-head slide or chained with simai
        /// slides, is moved on its own while the simai notes and segments are mirrored by the key maps.
        /// </summary>
        /// <param name="source">The chart text.</param>
        /// <param name="option">The mirror option.</param>
        /// <param name="expected">The expected mirrored chart text.</param>
        [TestCase("1P3K6[8:1]*Q9K3[8:1]", MirrorOption.LRMirror, "8Q7K3[8:1]*P9K6[8:1]")]
        [TestCase("1P3K6[8:1]*Q9K3[8:1]", MirrorOption.UDMirror, "4Q3K7[8:1]*P9K2[8:1]")]
        [TestCase("1-5[8:1]*P3K6[8:1]", MirrorOption.LRMirror, "8-4[8:1]*Q7K3[8:1]")]
        [TestCase("1P3K6[8:1]*-5[8:1]", MirrorOption.UDMirror, "4Q3K7[8:1]*-8[8:1]")]
        [TestCase("1bA357K1>1[1:1]", MirrorOption.LRMirror, "8bA642K8<8[1:1]")]
        [TestCase("1bA357K1>1[1:1]", MirrorOption.UDMirror, "4bA286K4>4[1:1]")]
        [TestCase("1bxA357K1b[2:1]", MirrorOption.LRMirror, "8bxA642K8b[2:1]")]
        [TestCase("1A357K1[8:1]>1[8:1]", MirrorOption.LRMirror, "8A642K8[8:1]<8[8:1]")]
        [TestCase("1-5A3K7[8:1]", MirrorOption.LRMirror, "8-4A6K2[8:1]")]
        [TestCase("(120){8}1P3K6[8:1],2-6[8:1]/3P0K5[4:1],E", MirrorOption.LRMirror, "(120){8}8Q7K3[8:1],7-3[8:1]/6Q0K4[4:1],E")]
        [TestCase("(120){8}1P3K6[8:1],2-6[8:1]/3P0K5[4:1],E", MirrorOption.UDMirror, "(120){8}4Q3K7[8:1],3-7[8:1]/2Q0K8[4:1],E")]
        public void SlideCodeBesideOtherNotes_IsMirroredOnItsOwn(string source, MirrorOption option, string expected)
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(source, option), Is.EqualTo(expected));
        }

        /// <summary>Checks that mirroring a chart with slide code twice restores it.</summary>
        /// <param name="option">The mirror option applied twice.</param>
        [Test]
        public void MirroringTwice_RestoresSlideCode([Values] MirrorOption option)
        {
            const string Source = "(120){8}1P33K4[8:1],2-6[8:1]*Q9K3[8:1]/5bP98CQ49K1[4:1],1Q2CP6K5[8:1],1bA357K1>1[1:1]";

            var twice = SimaiMirror.NoteMirrorHandle(SimaiMirror.NoteMirrorHandle(Source, option), option);

            Assert.That(twice, Is.EqualTo(Source));
        }

        /// <summary>Checks that charts without slide code are mirrored character by character with the key maps, as before.</summary>
        /// <param name="option">The mirror option.</param>
        /// <param name="expected">The expected mirrored chart.</param>
        [TestCase(MirrorOption.Off, SimaiChart)]
        [TestCase(MirrorOption.LRMirror, SimaiChartLeftRight)]
        [TestCase(MirrorOption.UDMirror, SimaiChartUpsideDown)]
        public void PlainSimai_IsMirroredAsBefore(MirrorOption option, string expected)
        {
            Assert.That(SimaiMirror.NoteMirrorHandle(SimaiChart, option), Is.EqualTo(expected));
        }

        /// <summary>
        /// Gets the rotoreflection, in the form <see cref="ParametricSlidePath.SetRotoreflection"/> takes, that moves the
        /// keys like the simai key maps of a mirror option.
        /// </summary>
        /// <param name="option">The mirror option.</param>
        /// <returns>Whether the keys are mirrored about the key 1 axis, and the clockwise rotation in keys that follows.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an option these tests do not know yet.</exception>
        private static (bool IsMirrored, int Rotation) GetRotoreflection(MirrorOption option)
        {
            switch (option)
            {
                case MirrorOption.Off:
                    return (false, 0);
                case MirrorOption.LRMirror:
                    // 1<->8, 2<->7, 3<->6, 4<->5: the mirror moves key k to 2 - k, turning back by one key gives 9 - k
                    return (true, 7);
                case MirrorOption.UDMirror:
                    // 1<->4, 2<->3, 5<->8, 6<->7: the mirror moves key k to 2 - k, turning on by three keys gives 5 - k
                    return (true, 3);
                default:
                    throw new ArgumentOutOfRangeException(nameof(option), option, "No rotoreflection is known for this option");
            }
        }

        /// <summary>
        /// Splits chart text written with slide code into its slides: the code of each slide, starting at the key of the
        /// star head for a same-head slide after <c>*</c>, and the text written after the code.
        /// </summary>
        /// <param name="chart">The chart text, for example <c>8B7B3K4[8:1]*B1B5K4[8:1]</c>.</param>
        /// <returns>The code and the following text of every slide, in order.</returns>
        private static List<(string Code, string Tail)> GetSlides(string chart)
        {
            var slides = new List<(string Code, string Tail)>();
            foreach (var group in chart.Split('/'))
            {
                var texts = group.Split('*');
                for (var i = 0; i < texts.Length; i++)
                {
                    var text = i == 0 ? texts[i] : group[0] + texts[i];
                    var codeLength = text.IndexOf('K') + 2;
                    slides.Add((text.Substring(0, codeLength), text.Substring(codeLength)));
                }
            }
            return slides;
        }

        /// <summary>
        /// Asserts that a mirrored slide code draws the path of the original code moved by the rotoreflection of the option.
        /// </summary>
        /// <param name="mirroredCode">The slide code produced by the mirror.</param>
        /// <param name="code">The original slide code.</param>
        /// <param name="option">The mirror option.</param>
        private static void AssertDrawsMovedPath(string mirroredCode, string code, MirrorOption option)
        {
            var (isMirrored, rotation) = GetRotoreflection(option);
            var actual = SlideCodeParser.Parse(mirroredCode);
            var expected = SlideCodeParser.Parse(code);
            expected.SetRotoreflection(isMirrored, rotation);

            var message = $"{code} -> {mirroredCode} ({option})";
            Assert.That(actual.GetPathLength(), Is.EqualTo(expected.GetPathLength()).Within(Tolerance), message);
            for (var i = 0; i <= 40; i++)
            {
                var t = i / 40.0;
                var distance = (actual.GetPointAt(t) - expected.GetPointAt(t)).Magnitude;
                Assert.That(distance, Is.LessThan(Tolerance), $"{message} at t = {t}");
            }
        }
    }
}
