using System;
using System.Linq;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide.Segments;
using NUnit.Framework;
using static MajdataPlay.Scenes.Game.Parsing.SlideCodeParser;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Parsing
{
    /// <summary>Checks command tokenizing and path generation of extended slide codes in <see cref="SlideCodeParser"/>.</summary>
    public class SlideCodeParserTests
    {
        /// <summary>The tolerance used for path lengths.</summary>
        private const double Tolerance = 1e-6;

        /// <summary>Checks that digits are recognised and other characters are rejected.</summary>
        [Test]
        public void TryParseDigit_ReturnsTheDigitOrMinusOne()
        {
            Assert.That(TryParseDigit('0'), Is.EqualTo(0));
            Assert.That(TryParseDigit('9'), Is.EqualTo(9));
            Assert.That(TryParseDigit('a'), Is.EqualTo(-1));
            Assert.That(TryParseDigit('/'), Is.EqualTo(-1));
        }

        /// <summary>Checks the command list of a code that uses every command type.</summary>
        [Test]
        public void ParseCommands_TokenizesEveryCommandType()
        {
            var commands = ParseCommands("1CA2B3P0Q9K4");
            var expected = new[]
            {
                (CommandType.NodeA, 1),
                (CommandType.NodeC, 0),
                (CommandType.NodeA, 2),
                (CommandType.NodeB, 3),
                (CommandType.OrbitCcw, 0),
                (CommandType.OrbitCw, 9),
                (CommandType.NodeEnd, 4),
            };

            Assert.That(commands.Select(x => (x.Type, x.Value)), Is.EqualTo(expected));
            Assert.That(CommandChars, Is.EqualTo(new[] { 'A', 'B', 'C', 'P', 'Q', 'K' }));
        }

        /// <summary>Checks the syntax errors reported while tokenizing.</summary>
        /// <param name="code">The invalid code.</param>
        /// <param name="message">A fragment of the expected message.</param>
        [TestCase("1-5K5", "2nd char")]
        [TestCase("15K5", "2nd char")]
        [TestCase("1A5K", "end with 'K'")]
        [TestCase("xA5K5", "invalid char 'x'")]
        [TestCase("1A?K5", "invalid char '?'")]
        [TestCase("1C5K5", "digit should not follow 'C'")]
        public void ParseCommands_RejectsInvalidCodes(string code, string message)
        {
            var exception = Assert.Throws<ArgumentException>(() => ParseCommands(code));

            Assert.That(exception!.Message, Does.Contain(message));
        }

        /// <summary>Checks command equality used to skip repeated nodes.</summary>
        [Test]
        public void Command_IsSameComparesTypeAndValue()
        {
            var a = new Command(CommandType.NodeA, 5);

            Assert.That(Command.IsSame(a, new Command(CommandType.NodeA, 5)), Is.True);
            Assert.That(Command.IsSame(a, new Command(CommandType.NodeEnd, 5)), Is.False);
            Assert.That(Command.IsSame(a, new Command(CommandType.NodeA, 4)), Is.False);
        }

        /// <summary>Checks node positions and the rejection of orbit commands as nodes.</summary>
        [Test]
        public void GetNodePosition_MapsNodeCommands()
        {
            Assert.That(GetNodePosition(new Command(CommandType.NodeA, 3)), Is.EqualTo(SlideGeo.PointGroupA(3)));
            Assert.That(GetNodePosition(new Command(CommandType.NodeEnd, 5)), Is.EqualTo(SlideGeo.PointGroupA(5)));
            Assert.That(GetNodePosition(new Command(CommandType.NodeB, 2)), Is.EqualTo(SlideGeo.PointGroupB(2)));
            Assert.That(GetNodePosition(new Command(CommandType.NodeC, 0)), Is.EqualTo(SlideGeo.PointCenter()));
            Assert.Throws<ArgumentException>(() => GetNodePosition(new Command(CommandType.OrbitCw, 9)));
        }

        /// <summary>Checks a straight slide through the centre and that a repeated node adds no segment.</summary>
        /// <param name="code">A code describing the A1 to A5 straight slide.</param>
        [TestCase("1CK5")]
        [TestCase("1B1K5")]
        public void Parse_StraightSlideThroughTheCentre(string code)
        {
            var path = Parse(code);

            Assert.That(path.Segments.All(x => x is LineSegment), Is.True);
            Assert.That(path.GetPathLength(), Is.EqualTo(9.6).Within(Tolerance));
            Assert.That(path.GetEndShape(), Is.EqualTo(SlideEndShape.Straight));
        }

        /// <summary>Checks that a repeated start node is skipped.</summary>
        [Test]
        public void Parse_SkipsRepeatedNodes()
        {
            var path = Parse("1A1K5");

            Assert.That(path.Segments, Has.Length.EqualTo(1));
            Assert.That(path.GetPathLength(), Is.EqualTo(9.6).Within(Tolerance));
        }

        /// <summary>Checks clockwise and counter-clockwise arcs along the judge circle.</summary>
        [Test]
        public void Parse_JudgeCircleArcs()
        {
            var clockwise = Parse("1Q9K3");
            var counterClockwise = Parse("1P9K3");

            Assert.That(clockwise.Segments.Single(), Is.TypeOf<ArcSegment>());
            Assert.That(clockwise.GetPathLength(), Is.EqualTo(7.539822368615504).Within(Tolerance));
            Assert.That(clockwise.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCW));
            Assert.That(counterClockwise.GetPathLength(), Is.EqualTo(22.619467105846514).Within(Tolerance));
            Assert.That(counterClockwise.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCCW));
        }

        /// <summary>Checks that entering an orbit from outside adds a tangent line, an arc and a final line.</summary>
        [Test]
        public void Parse_CenterOrbitSlide()
        {
            var path = Parse("1P0K5");

            Assert.That(path.Segments.Select(x => x.GetType()), Is.EqualTo(new[] { typeof(LineSegment), typeof(ArcSegment), typeof(LineSegment) }));
            Assert.That(path.GetPathLength(), Is.EqualTo(10.311926063830784).Within(Tolerance));
        }

        /// <summary>Checks that a line arriving on the judge circle is force-aligned before the arc.</summary>
        [Test]
        public void Parse_MarksForceAlignWhenALineJoinsTheJudgeCircle()
        {
            var path = Parse("1CA2Q9K4");

            Assert.That(path.Segments.Select(x => x.ParseMarker), Is.EqualTo(new[] { SlideParseMarker.None, SlideParseMarker.ForceAlign, SlideParseMarker.None }));
            Assert.That(path.GetPathLength(), Is.EqualTo(17.139822368615505).Within(Tolerance));
        }

        /// <summary>Checks that leaving a ppqq orbit for the judge circle adds a smooth-aligned transfer arc.</summary>
        [Test]
        public void Parse_TransfersFromPpqqToTheJudgeCircle()
        {
            var path = Parse("1Q1Q9K3");

            Assert.That(path.Segments, Has.Length.EqualTo(4));
            Assert.That(path.Segments[2].ParseMarker, Is.EqualTo(SlideParseMarker.SmoothAlign));
            Assert.That(path.GetEndShape(), Is.EqualTo(SlideEndShape.CircleCW));
        }

        /// <summary>Checks orbit-to-orbit transfers: from the judge circle, between equal circles and to a smaller circle.</summary>
        [Test]
        public void Parse_TransfersBetweenOrbits()
        {
            Assert.That(Parse("1P9P1K5").Segments, Has.Length.EqualTo(4));
            Assert.That(Parse("1P1P2K5").Segments, Has.Length.EqualTo(5));
            Assert.That(Parse("1P1P0K5").Segments, Has.Length.EqualTo(5));
            Assert.That(Parse("1P0P1K5").Segments, Has.Length.EqualTo(5));
        }

        /// <summary>Checks that repeating the same orbit adds a whole circle.</summary>
        [Test]
        public void Parse_RepeatedOrbitAddsAFullCircle()
        {
            var path = Parse("1Q9Q9K1");

            Assert.That(path.Segments[0], Is.TypeOf<CircleSegment>());
            Assert.That(path.GetPathLength(), Is.EqualTo(60.318578948924035).Within(Tolerance));
        }

        /// <summary>Checks the geometric and ordering errors reported while building the path.</summary>
        /// <param name="code">The invalid code.</param>
        /// <param name="message">A fragment of the expected message.</param>
        [TestCase("1P0Q1K5", "orbit type mismatch")]
        [TestCase("1P0P9K5", "impossible: Orbit0 -> Orbit9")]
        [TestCase("1P9P0K5", "impossible: Orbit9 -> Orbit0")]
        [TestCase("1CP9K5", "impossible: NodeC0 -> Orbit9")]
        [TestCase("1P9B3K3", "impossible: Orbit9 -> NodeB3")]
        [TestCase("1K5A3K5", "'K' should be the last command")]
        [TestCase("1K5P0K5", "'K' should be the last command")]
        public void Parse_RejectsImpossiblePaths(string code, string message)
        {
            var exception = Assert.Throws<ArgumentException>(() => Parse(code));

            Assert.That(exception!.Message, Does.Contain(message));
        }
    }
}
