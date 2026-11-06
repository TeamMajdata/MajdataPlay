using System;
using System.Linq;
using MajdataPlay.Extensions;
using MajdataPlay.Numerics;
using MajSimai;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Extensions
{
    /// <summary>Covers the chart transformations used by mods and practice mode in <see cref="SimaiProcessExtensions"/>.</summary>
    public class SimaiProcessExtensionsTests
    {
        /// <summary>A chart with a tap, a hold, a slide, a touch, a touch hold and an each pair, one per half second.</summary>
        private const string Fumen = "(120){4}1,2h[4:1],3-5[4:1],B1,Ch[4:1],4/5,E";

        /// <summary>
        /// A chart with a tap and a same-head group whose head and first slide are breaks. The note indices are: tap 0,
        /// head 1, first slide 2, second slide 3.
        /// </summary>
        private const string GroupFumen = "(120){4}1,1b-3b[8:1]*-5[8:1],E";

        /// <summary>Parses the test chart.</summary>
        /// <returns>The parsed chart.</returns>
        private static SimaiChart Parse()
        {
            return Parse(Fumen);
        }

        /// <summary>Parses a chart.</summary>
        /// <param name="fumen">The chart text.</param>
        /// <returns>The parsed chart.</returns>
        private static SimaiChart Parse(string fumen)
        {
            return SimaiParser.ParseChart("13", "Designer", fumen.AsSpan());
        }

        /// <summary>Gets the timing points that hold notes.</summary>
        /// <param name="chart">The chart to read.</param>
        /// <returns>The note timing points.</returns>
        private static SimaiTimingPoint[] Timings(SimaiChart chart)
        {
            return chart.NoteTimings.ToArray();
        }

        /// <summary>Checks that no note of a timing point lists a note that is not in the same timing point.</summary>
        /// <param name="notes">The notes of the timing point.</param>
        private static void AssertRelationsStayInside(SimaiNote[] notes)
        {
            foreach (var note in notes)
            {
                foreach (var relatedNote in note.RelatedNotes)
                {
                    Assert.That(notes.Any(other => ReferenceEquals(other, relatedNote)), Is.True, "A related note is missing from its timing point");
                }
            }
        }

        /// <summary>Checks the parsed test chart so the transformation tests rely on a known layout.</summary>
        [Test]
        public void TestChart_HasExpectedLayout()
        {
            var timings = Timings(Parse());

            Assert.That(timings.Length, Is.EqualTo(6));
            Assert.That(timings.Select(timing => timing.Notes[0].Type), Is.EqualTo(new[]
            {
                SimaiNoteType.Tap, SimaiNoteType.Hold, SimaiNoteType.Tap, SimaiNoteType.Touch, SimaiNoteType.TouchHold, SimaiNoteType.Tap
            }));
            Assert.That(timings[2].Notes.Length, Is.EqualTo(2));
            Assert.That(timings[2].Notes[0].IsSlideHead(), Is.True);
            Assert.That(timings[2].Notes[1].Type, Is.EqualTo(SimaiNoteType.Slide));
            Assert.That(timings[2].Notes[0].RelatedNotes, Is.EqualTo(new[] { timings[2].Notes[1] }));
            Assert.That(timings[5].Notes.Length, Is.EqualTo(2));
            Assert.That(timings[1].Timing, Is.EqualTo(0.5).Within(1e-9));
        }

        /// <summary>Checks that scaling speeds up timings, holds and slides.</summary>
        [Test]
        public void Scale_ScalesTimeValues()
        {
            var chart = Parse();
            var before = Timings(chart);
            var holdTime = before[1].Notes[0].HoldTime;
            var slideTime = before[2].Notes[1].SlideTime;
            var slideStart = before[2].Notes[1].SlideStartTime;
            var bpm = before[0].Bpm;

            var result = chart.Scale(2f);

            var after = Timings(result);
            Assert.That(result, Is.SameAs(chart));
            Assert.That(after[1].Timing, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(after[0].Bpm, Is.EqualTo(bpm * 2f));
            Assert.That(after[1].Notes[0].HoldTime, Is.EqualTo(holdTime / 2).Within(1e-9));
            Assert.That(after[2].Notes[1].SlideTime, Is.EqualTo(slideTime / 2).Within(1e-9));
            Assert.That(after[2].Notes[1].SlideStartTime, Is.EqualTo(slideStart / 2).Within(1e-9));
        }

        /// <summary>Checks that every note becomes a break note, including slide heads and slide paths.</summary>
        [Test]
        public void ConvertToBreak_MarksEveryNote()
        {
            var timings = Timings(Parse().ConvertToBreak());
            var notes = timings.SelectMany(timing => timing.Notes).ToArray();

            Assert.That(notes.All(note => note.IsBreak), Is.True);
            Assert.That(timings[2].Notes[0].IsSlideHead(), Is.True);
            Assert.That(timings[2].Notes[1].IsBreak, Is.True);
        }

        /// <summary>Checks that converting to breaks also marks the paths of a same-head group and a headless slide.</summary>
        [Test]
        public void ConvertToBreak_MarksEverySlidePath()
        {
            var timings = Timings(Parse("(120){4}1-3[8:1]*-5[8:1],3?-7[8:1],E").ConvertToBreak());

            Assert.That(timings[0].Notes.All(note => note.IsBreak), Is.True);
            Assert.That(timings[1].Notes.Single().IsBreak, Is.True);
        }

        /// <summary>Checks that every note becomes an EX note.</summary>
        [Test]
        public void ConvertToEx_MarksEveryNote()
        {
            var notes = Timings(Parse().ConvertToEx()).SelectMany(timing => timing.Notes).ToArray();

            Assert.That(notes.All(note => note.IsEx), Is.True);
        }

        /// <summary>Checks that taps, holds and slide heads are replaced by touches on the A area.</summary>
        [Test]
        public void ConvertToTouch_ReplacesButtonNotes()
        {
            var timings = Timings(Parse().ConvertToTouch());

            Assert.That(timings[0].Notes.Single().Type, Is.EqualTo(SimaiNoteType.Touch));
            Assert.That(timings[0].Notes.Single().TouchArea, Is.EqualTo('A'));
            Assert.That(timings[0].Notes.Single().StartPosition, Is.EqualTo(1));
            Assert.That(timings[1].Notes.Single().Type, Is.EqualTo(SimaiNoteType.TouchHold));
            Assert.That(timings[1].Notes.Single().HoldTime, Is.GreaterThan(0));
            Assert.That(timings[2].Notes.Length, Is.EqualTo(2));
            Assert.That(timings[2].Notes[0].Type, Is.EqualTo(SimaiNoteType.Slide));
            Assert.That(timings[2].Notes[0].IsHeadlessSlide(timings[2].Notes), Is.True);
            Assert.That(timings[2].Notes[1].Type, Is.EqualTo(SimaiNoteType.Touch));
            Assert.That(timings[2].Notes[1].StartPosition, Is.EqualTo(3));
            Assert.That(timings[2].Notes[1].RelatedNotes, Is.Empty);
            Assert.That(timings[3].Notes.Single().TouchArea, Is.EqualTo('B'));
            Assert.That(timings[4].Notes.Single().Type, Is.EqualTo(SimaiNoteType.TouchHold));
            Assert.That(timings[5].Notes.All(note => note.Type == SimaiNoteType.Touch), Is.True);
            Assert.That(timings[5].Notes.All(note => note.IsEach), Is.True);
        }

        /// <summary>Checks that converting a headless slide keeps it without adding a touch.</summary>
        [Test]
        public void ConvertToTouch_KeepsHeadlessSlides()
        {
            var chart = Parse().ConvertToTouch();

            var twice = Timings(chart.ConvertToTouch());

            Assert.That(twice[2].Notes.Count(note => note.Type == SimaiNoteType.Slide), Is.EqualTo(1));
            Assert.That(twice[2].Notes.Length, Is.EqualTo(2));
        }

        /// <summary>Checks that a slide written without a head stays the only note of its timing point.</summary>
        [Test]
        public void ConvertToTouch_KeepsSlideWrittenWithoutHead()
        {
            var notes = Timings(Parse("(120){4}1?-5[8:1],E").ConvertToTouch()).Single().Notes;

            Assert.That(notes.Single().Type, Is.EqualTo(SimaiNoteType.Slide));
        }

        /// <summary>
        /// Checks that the head of a same-head group becomes one touch right after its first slide, with the break of the
        /// head, and that no note is left that lists slides.
        /// </summary>
        [Test]
        public void ConvertToTouch_ReplacesHeadOfSameHeadGroup()
        {
            var notes = Timings(Parse(GroupFumen).ConvertToTouch())[1].Notes;

            Assert.That(notes.Select(note => note.Type), Is.EqualTo(new[]
            {
                SimaiNoteType.Slide, SimaiNoteType.Touch, SimaiNoteType.Slide
            }));
            Assert.That(notes[1].TouchArea, Is.EqualTo('A'));
            Assert.That(notes[1].StartPosition, Is.EqualTo(1));
            Assert.That(notes[1].IsBreak, Is.True);
            Assert.That(notes[0].IsBreak, Is.True);
            Assert.That(notes[2].IsBreak, Is.False);
            Assert.That(notes.All(note => note.RelatedNotes.Length == 0), Is.True);
            Assert.That(notes[0].IsHeadlessSlide(notes), Is.True);
            Assert.That(notes[2].IsHeadlessSlide(notes), Is.True);
            Assert.That(notes[0].IsEach, Is.True);
            Assert.That(notes[1].IsEach, Is.False);
        }

        /// <summary>Checks that the touch of a head and an each tap are both marked as each.</summary>
        [Test]
        public void ConvertToTouch_RecomputesEachOfNewTouches()
        {
            var notes = Timings(Parse("(120){4}1-5[8:1]/2,E").ConvertToTouch()).Single().Notes;

            Assert.That(notes.Select(note => note.Type), Is.EqualTo(new[]
            {
                SimaiNoteType.Slide, SimaiNoteType.Touch, SimaiNoteType.Touch
            }));
            Assert.That(notes[0].IsEach, Is.False);
            Assert.That(notes[1].IsEach, Is.True);
            Assert.That(notes[2].IsEach, Is.True);
        }

        /// <summary>Checks that clamping by note index keeps the selected notes and the full slide when its body is in range.</summary>
        [Test]
        public void ClampByIndex_KeepsNotesInRange()
        {
            var chart = Parse();

            var clamped = chart.Clamp(new Range<long>(1, 3));

            var timings = Timings(clamped);
            Assert.That(clamped.Level, Is.EqualTo("13"));
            Assert.That(clamped.Designer, Is.EqualTo("Designer"));
            Assert.That(timings.Length, Is.EqualTo(2));
            Assert.That(timings[0].Notes.Single().Type, Is.EqualTo(SimaiNoteType.Hold));
            Assert.That(timings[1].Notes.Select(note => note.Type), Is.EqualTo(new[] { SimaiNoteType.Tap, SimaiNoteType.Slide }));
            Assert.That(timings[1].Notes[1].TryGetSlideHead(timings[1].Notes, out var head), Is.True);
            Assert.That(head, Is.SameAs(timings[1].Notes[0]));
        }

        /// <summary>Checks that a slide whose body is outside the range is reduced to a star-shaped tap.</summary>
        [Test]
        public void ClampByIndex_ReducesSlideToStar()
        {
            var clamped = Parse().Clamp(new Range<long>(2, 2));

            var note = Timings(clamped).Single().Notes.Single();
            Assert.That(note.Type, Is.EqualTo(SimaiNoteType.Tap));
            Assert.That(note.IsForceStar, Is.True);
            Assert.That(note.IsFakeRotate, Is.True);
            Assert.That(note.IsStar, Is.True);
            Assert.That(note.RelatedNotes, Is.Empty);
            Assert.That(note.StartPosition, Is.EqualTo(3));
            Assert.That(note.RawContent, Is.EqualTo("3"));
        }

        /// <summary>Checks that a range starting at the slide of a headed slide keeps its head as well.</summary>
        [Test]
        public void ClampByIndex_KeepsHeadWhenOnlySlideIndexIsInRange()
        {
            var timings = Timings(Parse().Clamp(new Range<long>(3, 4)));

            Assert.That(timings.Length, Is.EqualTo(2));
            Assert.That(timings[0].Notes.Select(note => note.Type), Is.EqualTo(new[] { SimaiNoteType.Tap, SimaiNoteType.Slide }));
            Assert.That(timings[0].Notes[0].IsForceStar, Is.False);
            Assert.That(timings[0].Notes[0].RelatedNotes, Is.EqualTo(new[] { timings[0].Notes[1] }));
            Assert.That(timings[1].Notes.Single().Type, Is.EqualTo(SimaiNoteType.Touch));
        }

        /// <summary>Checks that a headed slide always takes two indices, also when it is before the range.</summary>
        [Test]
        public void ClampByIndex_CountsHeadedSlideBeforeRangeAsTwoNotes()
        {
            var note = Timings(Parse("(120){4}1-5[8:1],2,3,E").Clamp(new Range<long>(2, 2))).Single().Notes.Single();

            Assert.That(note.Type, Is.EqualTo(SimaiNoteType.Tap));
            Assert.That(note.StartPosition, Is.EqualTo(2));
        }

        /// <summary>Checks that a headless slide is kept by its own index.</summary>
        [Test]
        public void ClampByIndex_KeepsHeadlessSlide()
        {
            var headless = SimaiParser.ParseChart("1", "d", "(120){4}1,3?-5[4:1],E".AsSpan());

            var clamped = headless.Clamp(new Range<long>(1, 1));

            var notes = Timings(clamped).Single().Notes;
            Assert.That(notes.Single().Type, Is.EqualTo(SimaiNoteType.Slide));
            Assert.That(notes.Single().IsHeadlessSlide(notes), Is.True);
        }

        /// <summary>Checks that a same-head group fully in range keeps its head with both slides.</summary>
        [Test]
        public void ClampByIndex_KeepsWholeSameHeadGroup()
        {
            var notes = Timings(Parse(GroupFumen).Clamp(new Range<long>(1, 3))).Single().Notes;

            Assert.That(notes.Length, Is.EqualTo(3));
            Assert.That(notes[0].RelatedNotes, Is.EqualTo(new[] { notes[1], notes[2] }));
            Assert.That(notes[0].IsStarDouble, Is.True);
        }

        /// <summary>
        /// Checks that a head whose first slide is kept only lists the kept slides, keeps its flags, and that the source
        /// chart is left unchanged.
        /// </summary>
        [Test]
        public void ClampByIndex_DropsClampedSlidesFromHead()
        {
            var chart = Parse(GroupFumen);

            var notes = Timings(chart.Clamp(new Range<long>(0, 2)))[1].Notes;

            Assert.That(notes.Select(note => note.Type), Is.EqualTo(new[] { SimaiNoteType.Tap, SimaiNoteType.Slide }));
            Assert.That(notes[0].RelatedNotes, Is.EqualTo(new[] { notes[1] }));
            Assert.That(notes[0].IsBreak, Is.True);
            Assert.That(notes[0].IsStar, Is.True);
            Assert.That(notes[0].IsStarDouble, Is.False);
            Assert.That(notes[1].IsBreak, Is.True);
            Assert.That(notes[1].IsEach, Is.False);
            Assert.That(notes[1].RawContent, Is.EqualTo("1-3[8:1]"));
            Assert.That(notes[1].SlideTime, Is.EqualTo(chart.NoteTimings[1].Notes[1].SlideTime));
            AssertRelationsStayInside(notes);
            var sourceNotes = chart.NoteTimings[1].Notes;
            Assert.That(sourceNotes[0].RelatedNotes, Is.EqualTo(new[] { sourceNotes[1], sourceNotes[2] }));
            Assert.That(sourceNotes[0].IsStarDouble, Is.True);
            Assert.That(sourceNotes[1].IsEach, Is.True);
        }

        /// <summary>Checks that a head whose first slide is clamped away becomes a star-shaped tap without slides.</summary>
        [Test]
        public void ClampByIndex_ReducesGroupHeadToStar()
        {
            var timings = Timings(Parse(GroupFumen).Clamp(new Range<long>(0, 1)));

            var note = timings[1].Notes.Single();
            Assert.That(note.Type, Is.EqualTo(SimaiNoteType.Tap));
            Assert.That(note.IsForceStar, Is.True);
            Assert.That(note.IsFakeRotate, Is.True);
            Assert.That(note.IsBreak, Is.True);
            Assert.That(note.RelatedNotes, Is.Empty);
        }

        /// <summary>Checks that a later slide of a group kept without its head becomes a slide without a head.</summary>
        [Test]
        public void ClampByIndex_KeepsLaterGroupSlideWithoutHead()
        {
            var notes = Timings(Parse(GroupFumen).Clamp(new Range<long>(3, 3))).Single().Notes;

            Assert.That(notes.Single().Type, Is.EqualTo(SimaiNoteType.Slide));
            Assert.That(notes.Single().RawContent, Is.EqualTo("1-5[8:1]"));
            Assert.That(notes.Single().IsHeadlessSlide(notes), Is.True);
        }

        /// <summary>Checks that clamping by note index copies every value of the kept timing points.</summary>
        [Test]
        public void ClampByIndex_CopiesTimingPointValues()
        {
            var chart = Parse("(120){4}<HS*2.0>1,<SV*1.5>2-6[8:1],||s3/4\n3,E");

            var timings = Timings(chart.Clamp(new Range<long>(0, 3)));

            Assert.That(timings.Length, Is.EqualTo(chart.NoteTimings.Length));
            for (var i = 0; i < timings.Length; i++)
            {
                var expected = chart.NoteTimings[i];
                var actual = timings[i];
                Assert.That(actual, Is.Not.SameAs(expected));
                Assert.That(actual.Timing, Is.EqualTo(expected.Timing));
                Assert.That(actual.RawContent, Is.EqualTo(expected.RawContent));
                Assert.That(actual.RawTextPositionX, Is.EqualTo(expected.RawTextPositionX));
                Assert.That(actual.RawTextPositionY, Is.EqualTo(expected.RawTextPositionY));
                Assert.That(actual.RawTextPosition, Is.EqualTo(expected.RawTextPosition));
                Assert.That(actual.Bpm, Is.EqualTo(expected.Bpm));
                Assert.That(actual.HSpeed, Is.EqualTo(expected.HSpeed));
                Assert.That(actual.SVeloc, Is.EqualTo(expected.SVeloc));
                Assert.That(actual.SignatureNumerator, Is.EqualTo(expected.SignatureNumerator));
                Assert.That(actual.SignatureDenominator, Is.EqualTo(expected.SignatureDenominator));
            }
        }

        /// <summary>Checks that a clamped chart keeps the chart text and has no comma timing points.</summary>
        [Test]
        public void ClampByIndex_KeepsFumenWithoutCommaTimings()
        {
            var clamped = Parse().Clamp(new Range<long>(0, 0));

            Assert.That(clamped.Fumen, Is.EqualTo(Fumen));
            Assert.That(clamped.CommaTimings.Length, Is.EqualTo(0));
        }

        /// <summary>Checks that clamping by time keeps only timing points inside the range.</summary>
        [Test]
        public void ClampByTime_KeepsTimingPointsInRange()
        {
            var clamped = Parse().Clamp(new Range<double>(0.4, 1.1));

            var timings = Timings(clamped);
            Assert.That(timings.Select(timing => timing.Timing), Is.EqualTo(new[] { 0.5, 1.0 }).Within(1e-9));
            Assert.That(timings[1].Notes.Select(note => note.Type), Is.EqualTo(new[] { SimaiNoteType.Tap, SimaiNoteType.Slide }));
            Assert.That(clamped.Fumen, Is.EqualTo(Fumen));
            Assert.That(clamped.CommaTimings.Length, Is.EqualTo(0));
        }

        /// <summary>Checks that an offset moves timing points, comma timings and slide starts.</summary>
        [Test]
        public void AddOffset_ShiftsTimings()
        {
            var chart = Parse();
            var slideStart = chart.NoteTimings[2].Notes[1].SlideStartTime;
            var commaTimings = chart.CommaTimings.ToArray().Select(timing => timing.Timing).ToArray();
            var holdStart = chart.NoteTimings[1].Timing;

            var result = chart.AddOffset(1.5f);

            Assert.That(result, Is.SameAs(chart));
            Assert.That(chart.NoteTimings[1].Timing, Is.EqualTo(holdStart + 1.5).Within(1e-6));
            Assert.That(chart.NoteTimings[2].Notes[1].SlideStartTime, Is.EqualTo(slideStart + 1.5).Within(1e-6));
            var shifted = chart.CommaTimings.ToArray().Select(timing => timing.Timing).ToArray();
            Assert.That(shifted.Length, Is.EqualTo(commaTimings.Length));
            Assert.That(shifted.Length, Is.GreaterThan(0));
            for (var i = 0; i < shifted.Length; i++)
            {
                Assert.That(shifted[i], Is.EqualTo(commaTimings[i] + 1.5).Within(1e-6));
            }
        }
    }
}
