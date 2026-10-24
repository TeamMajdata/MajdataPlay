using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using MajSimai;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Loading
{
    /// <summary>
    /// Checks the chart-to-note helpers nested in <see cref="NoteLoader"/>: slide result facing, rotation and mirroring,
    /// each-note detection, random mapping and note folding. Slide shapes are covered by the translator tests.
    /// </summary>
    public class NoteCreateHelperTests
    {
        /// <summary>The private helper type nested in <see cref="NoteLoader"/>.</summary>
        private static readonly Type s_helper = typeof(NoteLoader).GetNestedType("NoteCreateHelper", BindingFlags.NonPublic)!;

        /// <summary>Restores the settings changed by random mode tests.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>Checks a note against the notes of its timing point.</summary>
        /// <param name="origin">The note.</param>
        /// <param name="notes">The notes of the timing point.</param>
        /// <returns>Whether the note is an each note.</returns>
        private delegate bool EachCheck(SimaiNote origin, ReadOnlySpan<SimaiNote> notes);

        /// <summary>Checks a note of an explicit type against the notes of its timing point.</summary>
        /// <param name="origin">The note.</param>
        /// <param name="type">The note type to check as.</param>
        /// <param name="notes">The notes of the timing point.</param>
        /// <returns>Whether the note is an each note.</returns>
        private delegate bool TypedEachCheck(SimaiNote origin, SimaiNoteType type, ReadOnlySpan<SimaiNote> notes);

        /// <summary>Counts slides among the notes of a timing point.</summary>
        /// <param name="origin">The note.</param>
        /// <param name="notes">The notes of the timing point.</param>
        /// <returns>The number of non-mine slides.</returns>
        private delegate int SlideCounter(SimaiNote origin, ReadOnlySpan<SimaiNote> notes);

        /// <summary>Installs default settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new GamePlaySettingsScope();
        }

        /// <summary>Restores the previous settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _settings.Dispose();
        }

        /// <summary>Checks whether the slide OK must be mirrored and the end key reported for every slide family.</summary>
        /// <param name="content">The slide text.</param>
        /// <param name="expectedJust">The expected result.</param>
        /// <param name="expectedEnd">The expected end key.</param>
        [TestCase("1>3", true, 3)]
        [TestCase("3>5", false, 5)]
        [TestCase("1<7", false, 7)]
        [TestCase("4<2", true, 2)]
        [TestCase("1^3", true, 3)]
        [TestCase("1^7", false, 7)]
        [TestCase("1V73", true, 3)]
        [TestCase("1V75", false, 5)]
        [TestCase("5w1", true, 1)]
        [TestCase("1w5", false, 5)]
        [TestCase("1-3", true, 3)]
        [TestCase("1-6", false, 6)]
        [TestCase("1pp4", true, 4)]
        [TestCase("1qq7", false, 7)]
        public void DetectJustType_ReportsMirroringAndTheEndKey(string content, bool expectedJust, int expectedEnd)
        {
            var arguments = new object?[] { content, 0 };

            var result = (bool)PrivateMember.InvokeStatic(s_helper, "DetectJustType", arguments)!;

            Assert.That(result, Is.EqualTo(expectedJust));
            Assert.That(arguments[1], Is.EqualTo(expectedEnd));
        }

        /// <summary>Checks that a degenerate star slide falls through to the default result.</summary>
        [Test]
        public void DetectJustType_OppositeStarFallsThrough()
        {
            var arguments = new object?[] { "1^5", 0 };

            var result = (bool)PrivateMember.InvokeStatic(s_helper, "DetectJustType", arguments)!;

            Assert.That(result, Is.True);
            Assert.That(arguments[1], Is.EqualTo(4));
        }

        /// <summary>Checks the key mirror table and its range check.</summary>
        [Test]
        public void MirrorKeys_MirrorsAboutTheVerticalAxis()
        {
            var expected = new[] { 1, 8, 7, 6, 5, 4, 3, 2 };
            for (var key = 1; key <= 8; key++)
            {
                Assert.That(PrivateMember.InvokeStatic<int>(s_helper, "MirrorKeys", key), Is.EqualTo(expected[key - 1]));
            }
            Assert.Throws<TargetInvocationException>(() => PrivateMember.InvokeStatic(s_helper, "MirrorKeys", 0));
            Assert.Throws<TargetInvocationException>(() => PrivateMember.InvokeStatic(s_helper, "MirrorKeys", 9));
        }

        /// <summary>Checks the right and upper half classification of keys.</summary>
        [Test]
        public void HalfClassification_MatchesTheKeyLayout()
        {
            for (var key = 1; key <= 8; key++)
            {
                Assert.That(PrivateMember.InvokeStatic<bool>(s_helper, "IsRightHalf", key), Is.EqualTo(key <= 4), $"Right half {key}");
                Assert.That(PrivateMember.InvokeStatic<bool>(s_helper, "IsUpperHalf", key), Is.EqualTo(key is 7 or 8 or 1 or 2), $"Upper half {key}");
            }
        }

        /// <summary>Checks key rotation, wrapping in both directions and the range check.</summary>
        [Test]
        public void Rotation_RotatesKeysAndSensors()
        {
            Assert.That(PrivateMember.InvokeStatic<int>(s_helper, "Rotation", 1, 2), Is.EqualTo(3));
            Assert.That(PrivateMember.InvokeStatic<int>(s_helper, "Rotation", 8, 1), Is.EqualTo(1));
            Assert.That(PrivateMember.InvokeStatic<int>(s_helper, "Rotation", 1, -1), Is.EqualTo(8));
            Assert.That(PrivateMember.InvokeStatic<int>(s_helper, "Rotation", 5, 0), Is.EqualTo(5));
            Assert.That(PrivateMember.InvokeStatic<SensorArea>(s_helper, "Rotation", SensorArea.B1, 3), Is.EqualTo(SensorArea.B4));
            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.InvokeStatic(s_helper, "Rotation", 0, 1));
            Assert.That(exception!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
            Assert.Throws<TargetInvocationException>(() => PrivateMember.InvokeStatic(s_helper, "Rotation", 9, 1));
        }

        /// <summary>Checks each detection for taps: mines, no-head slides and lone notes are not each notes.</summary>
        [Test]
        public void IsEachNote_ForTapsIgnoresMinesAndNoHeadSlides()
        {
            var isEach = CreateDelegate<EachCheck>("IsEachNote", typeof(SimaiNote), typeof(ReadOnlySpan<SimaiNote>));
            var tap = Note(SimaiNoteType.Tap, 1);
            var other = Note(SimaiNoteType.Tap, 3);
            var mine = Note(SimaiNoteType.Tap, 5);
            mine.IsMine = true;
            var noHead = Note(SimaiNoteType.Slide, 6);
            noHead.IsSlideNoHead = true;

            Assert.That(isEach(tap, new[] { tap, other }), Is.True);
            Assert.That(isEach(tap, new[] { tap }), Is.False);
            Assert.That(isEach(tap, new[] { tap, mine, noHead }), Is.False);
            Assert.That(isEach(mine, new[] { mine, tap, other }), Is.False);
        }

        /// <summary>Checks each detection for slides and for an unknown note type.</summary>
        [Test]
        public void IsEachNote_ForSlidesCountsNonMineSlides()
        {
            var typedIsEach = CreateDelegate<TypedEachCheck>("IsEachNote", typeof(SimaiNote), typeof(SimaiNoteType), typeof(ReadOnlySpan<SimaiNote>));
            var count = CreateDelegate<SlideCounter>("GetEachSlideCount", typeof(SimaiNote), typeof(ReadOnlySpan<SimaiNote>));
            var slide = Note(SimaiNoteType.Slide, 1);
            var otherSlide = Note(SimaiNoteType.Slide, 5);
            var mineSlide = Note(SimaiNoteType.Slide, 3);
            mineSlide.IsMineSlide = true;
            var tap = Note(SimaiNoteType.Tap, 2);

            Assert.That(count(slide, new[] { slide, otherSlide, mineSlide, tap }), Is.EqualTo(2));
            Assert.That(typedIsEach(slide, SimaiNoteType.Slide, new[] { slide, otherSlide }), Is.True);
            Assert.That(typedIsEach(slide, SimaiNoteType.Slide, new[] { slide, mineSlide, tap }), Is.False);
            Assert.That(typedIsEach(mineSlide, SimaiNoteType.Slide, new[] { mineSlide, slide, otherSlide }), Is.False);
            Assert.That(typedIsEach(slide, SimaiNoteType.Tap, new[] { slide, tap }), Is.True, "A slide head counts as a tap");
            Assert.That(typedIsEach(slide, (SimaiNoteType)99, new[] { slide, otherSlide }), Is.False);
        }

        /// <summary>Checks the double star detection, which needs two non-mine slides from the same key.</summary>
        [Test]
        public void IsStarDouble_NeedsTwoSlidesFromTheSameKey()
        {
            var isDouble = CreateDelegate<EachCheck>("IsStarDouble", typeof(SimaiNote), typeof(ReadOnlySpan<SimaiNote>));
            var slide = Note(SimaiNoteType.Slide, 1);
            var sameKey = Note(SimaiNoteType.Slide, 1);
            var otherKey = Note(SimaiNoteType.Slide, 2);
            var mineSameKey = Note(SimaiNoteType.Slide, 1);
            mineSameKey.IsMineSlide = true;

            Assert.That(isDouble(slide, new[] { slide, sameKey }), Is.True);
            Assert.That(isDouble(slide, new[] { slide, otherKey }), Is.False);
            Assert.That(isDouble(slide, new[] { slide, mineSameKey }), Is.False);
        }

        /// <summary>Checks that the each slides are the non-mine slides of the timing point.</summary>
        [Test]
        public void GetEachSlides_ReturnsTheNonMineSlides()
        {
            var slide = Note(SimaiNoteType.Slide, 1);
            var mineSlide = Note(SimaiNoteType.Slide, 3);
            mineSlide.IsMineSlide = true;
            var tap = Note(SimaiNoteType.Tap, 4);
            var otherSlide = Note(SimaiNoteType.Slide, 5);

            var slides = PrivateMember.InvokeStatic<SimaiNote[]>(s_helper, "GetEachSlides", slide, new[] { slide, mineSlide, tap, otherSlide });

            Assert.That(slides, Is.EqualTo(new[] { slide, otherSlide }));
        }

        /// <summary>Checks that the random mapping tables are permutations that keep every sensor in its group.</summary>
        [Test]
        public void GenerateMappingTable_CreatesGroupPreservingPermutations()
        {
            var (buttons, sensors) = ((IReadOnlyDictionary<int, int>, IReadOnlyDictionary<SensorArea, SensorArea>))PrivateMember.InvokeStatic(s_helper, "GenerateMappingTable")!;

            Assert.That(buttons.Keys.OrderBy(x => x), Is.EqualTo(Enumerable.Range(1, 8)));
            Assert.That(buttons.Values.OrderBy(x => x), Is.EqualTo(Enumerable.Range(1, 8)));
            Assert.That(sensors, Has.Count.EqualTo(33));
            Assert.That(sensors.Values.Distinct().Count(), Is.EqualTo(33));
            foreach (var pair in sensors)
            {
                Assert.That(pair.Value.GetGroup(), Is.EqualTo(pair.Key.GetGroup()), pair.Key.ToString());
            }
            for (var key = 1; key <= 8; key++)
            {
                Assert.That(sensors[(SensorArea)(key - 1)], Is.EqualTo((SensorArea)(buttons[key] - 1)), "A sensors follow the buttons");
            }
            Assert.That(sensors[SensorArea.C], Is.EqualTo(SensorArea.C));
        }

        /// <summary>Checks that positions are only remapped when a random mode is enabled.</summary>
        /// <param name="mode">The random mode.</param>
        /// <param name="expectRemap">Whether the position is remapped.</param>
        [TestCase(RandomModeOption.Disabled, false)]
        [TestCase(RandomModeOption.RANDOM, true)]
        [TestCase(RandomModeOption.S_RANDOM, true)]
        public void SetNewPositionIfRequested_RemapsOnlyInRandomModes(RandomModeOption mode, bool expectRemap)
        {
            _settings.Settings.Game.Random = mode;
            var buttonTable = CreateButtonTable(4);
            var sensorTable = new Dictionary<SensorArea, SensorArea> { { SensorArea.E2, SensorArea.E6 } };
            var key = new object?[] { 2, buttonTable };
            var sensor = new object?[] { SensorArea.E2, sensorTable };
            var slide = new object?[] { 3, 1, buttonTable };

            PrivateMember.InvokeStatic(s_helper, "SetNewPositionIfRequested", key);
            PrivateMember.InvokeStatic(s_helper, "SetNewPositionIfRequested", sensor);
            PrivateMember.InvokeStatic(s_helper, "SetSlideNewPositionIfRequested", slide);

            Assert.That(key[0], Is.EqualTo(expectRemap ? 6 : 2));
            Assert.That(sensor[0], Is.EqualTo(expectRemap ? SensorArea.E6 : SensorArea.E2));
            Assert.That(slide[0], Is.EqualTo(expectRemap ? 7 : 3));
            Assert.That(slide[1], Is.EqualTo(expectRemap ? 5 : 1), "The slide keeps its length after remapping");
        }

        /// <summary>Checks that a clockwise slide keeps its length when remapped.</summary>
        [Test]
        public void SetSlideNewPositionIfRequested_KeepsForwardSlides()
        {
            _settings.Settings.Game.Random = RandomModeOption.RANDOM;
            var slide = new object?[] { 1, 4, CreateButtonTable(2) };

            PrivateMember.InvokeStatic(s_helper, "SetSlideNewPositionIfRequested", slide);

            Assert.That(slide[0], Is.EqualTo(3));
            Assert.That(slide[1], Is.EqualTo(6));
        }

        /// <summary>Checks that identical slides are folded into one note with a count and other notes are kept.</summary>
        [Test]
        public void NoteFolding_FoldsIdenticalSlides()
        {
            Assume.That(PrivateMember.GetStaticField<bool>(typeof(NoteLoader), "USERSETTING_NOTE_FOLDING"), Is.True);
            var tap = Note(SimaiNoteType.Tap, 2);
            var slideA = Slide("1-3[8:1]");
            var slideB = Slide("1-3[8:1]");
            var different = Slide("1-5[8:1]");

            var folded = PrivateMember.InvokeStatic<SimaiNote[]>(s_helper, "NoteFolding", new object?[] { new[] { slideA, tap, slideB, different } });

            Assert.That(folded, Has.Length.EqualTo(3));
            Assert.That(folded[0], Is.SameAs(tap));
            var counts = folded.Skip(1).ToDictionary(x => x.RawContent, x => PrivateMember.GetProperty<int>(x, "Count"));
            Assert.That(counts["1-3[8:1]"], Is.EqualTo(2));
            Assert.That(counts["1-5[8:1]"], Is.EqualTo(1));
            var foldedSlide = folded.Single(x => x.RawContent == "1-3[8:1]");
            Assert.That(foldedSlide, Is.Not.SameAs(slideA));
            Assert.That(foldedSlide.Type, Is.EqualTo(SimaiNoteType.Slide));
            Assert.That(foldedSlide.SlideTime, Is.EqualTo(slideA.SlideTime));
            Assert.That(foldedSlide.SlideStartTime, Is.EqualTo(slideA.SlideStartTime));
        }

        /// <summary>Checks that folding a timing point without slides keeps its notes in order.</summary>
        [Test]
        public void NoteFolding_KeepsNonSlideNotes()
        {
            var first = Note(SimaiNoteType.Tap, 1);
            var second = Note(SimaiNoteType.Touch, 1);

            var folded = PrivateMember.InvokeStatic<SimaiNote[]>(s_helper, "NoteFolding", new object?[] { new[] { first, second } });

            Assert.That(folded, Is.EqualTo(new[] { first, second }));
        }

        /// <summary>
        /// Checks that a slide buffer which grows into a pooled array still holding notes of an earlier timing point folds
        /// only the slides of its own timing point. A 17-touch chord followed by a 12-slide chain used to bring 4 touches
        /// of the chord back as touch notes of the chain.
        /// </summary>
        [Test]
        public void NoteFolding_IgnoresNotesLeftInPooledBuffersByEarlierTimingPoints()
        {
            Assume.That(PrivateMember.GetStaticField<bool>(typeof(NoteLoader), "USERSETTING_NOTE_FOLDING"), Is.True);
            var chord = Enumerable.Range(0, 17)
                                  .Select(i => Note(SimaiNoteType.Touch, (i % 8) + 1))
                                  .ToArray();
            var chain = Enumerable.Range(1, 12)
                                  .Select(i => Slide($"6-3[64:{i}]"))
                                  .ToArray();
            PrivateMember.InvokeStatic<SimaiNote[]>(s_helper, "NoteFolding", new object?[] { chord });

            var folded = PrivateMember.InvokeStatic<SimaiNote[]>(s_helper, "NoteFolding", new object?[] { chain });

            Assert.That(folded.Select(x => x.Type), Is.All.EqualTo(SimaiNoteType.Slide));
            Assert.That(folded.Select(x => x.RawContent), Is.EquivalentTo(chain.Select(x => x.RawContent)));
        }

        /// <summary>Checks the equality of the folding key: identical slides are equal, different slides are not.</summary>
        [Test]
        public void FoldingSimaiNote_ComparesByContent()
        {
            var type = typeof(NoteLoader).GetNestedType("FoldingSimaiNote", BindingFlags.NonPublic)!;
            var a = Activator.CreateInstance(type, Slide("1-3[8:1]"), null)!;
            var b = Activator.CreateInstance(type, Slide("1-3[8:1]"), null)!;
            var c = Activator.CreateInstance(type, Slide("1-4[8:1]"), null)!;
            var equality = type.GetMethod("op_Equality")!;
            var inequality = type.GetMethod("op_Inequality")!;
            var implicitConversion = type.GetMethod("op_Implicit")!;

            Assert.That(equality.Invoke(null, new[] { a, b }), Is.True);
            Assert.That(inequality.Invoke(null, new[] { a, c }), Is.True);
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals(c), Is.False);
            Assert.That(a.Equals("not a note"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            var converted = implicitConversion.Invoke(null, new object[] { Slide("1-3[8:1]") })!;
            Assert.That(converted.Equals(a), Is.True);
            Assert.That(PrivateMember.GetProperty<string>(a, "RawContent"), Is.EqualTo("1-3[8:1]"));
            Assert.That(PrivateMember.GetProperty<SimaiNote>(a, "Origin"), Is.Not.Null);
        }

        /// <summary>Checks that a folding note is valid only when it wraps a simai note, not as a cleared buffer slot.</summary>
        [Test]
        public void FoldingSimaiNote_IsValidOnlyWithAnOrigin()
        {
            var type = typeof(NoteLoader).GetNestedType("FoldingSimaiNote", BindingFlags.NonPublic)!;
            var wrapped = Activator.CreateInstance(type, Slide("1-3[8:1]"), null)!;
            var cleared = Activator.CreateInstance(type)!;

            Assert.That(PrivateMember.GetProperty<bool>(wrapped, "IsValid"), Is.True);
            Assert.That(PrivateMember.GetProperty<bool>(cleared, "IsValid"), Is.False);
        }

        /// <summary>Creates a delegate bound to a static helper method.</summary>
        /// <typeparam name="T">The delegate type.</typeparam>
        /// <param name="name">The method name.</param>
        /// <param name="parameterTypes">The parameter types that select the overload.</param>
        /// <returns>The delegate.</returns>
        private static T CreateDelegate<T>(string name, params Type[] parameterTypes) where T : Delegate
        {
            var method = s_helper.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null)!;
            return (T)method.CreateDelegate(typeof(T));
        }

        /// <summary>Creates a note.</summary>
        /// <param name="type">The note type.</param>
        /// <param name="startPosition">The key.</param>
        /// <returns>The note.</returns>
        private static SimaiNote Note(SimaiNoteType type, int startPosition)
        {
            return new SimaiNote { Type = type, StartPosition = startPosition, RawContent = startPosition.ToString() };
        }

        /// <summary>Creates a slide note.</summary>
        /// <param name="raw">The slide text.</param>
        /// <returns>The slide note.</returns>
        private static SimaiNote Slide(string raw)
        {
            return new SimaiNote
            {
                Type = SimaiNoteType.Slide,
                StartPosition = raw[0] - '0',
                RawContent = raw,
                SlideStartTime = 1.25,
                SlideTime = 0.5,
            };
        }

        /// <summary>Creates a button mapping that rotates every key clockwise.</summary>
        /// <param name="offset">The number of keys to rotate by.</param>
        /// <returns>The mapping table.</returns>
        private static Dictionary<int, int> CreateButtonTable(int offset)
        {
            var table = new Dictionary<int, int>();
            for (var key = 1; key <= 8; key++)
            {
                table[key] = ((key - 1 + offset) % 8) + 1;
            }
            return table;
        }
    }
}
