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

        /// <summary>Parses the test chart.</summary>
        /// <returns>The parsed chart.</returns>
        private static SimaiChart Parse()
        {
            return SimaiParser.ParseChart("13", "Designer", Fumen.AsSpan());
        }

        /// <summary>Gets the timing points that hold notes.</summary>
        /// <param name="chart">The chart to read.</param>
        /// <returns>The note timing points.</returns>
        private static SimaiTimingPoint[] Timings(SimaiChart chart)
        {
            return chart.NoteTimings.ToArray();
        }

        /// <summary>Checks the parsed test chart so the transformation tests rely on a known layout.</summary>
        [Test]
        public void TestChart_HasExpectedLayout()
        {
            var timings = Timings(Parse());

            Assert.That(timings.Length, Is.EqualTo(6));
            Assert.That(timings.Select(timing => timing.Notes[0].Type), Is.EqualTo(new[]
            {
                SimaiNoteType.Tap, SimaiNoteType.Hold, SimaiNoteType.Slide, SimaiNoteType.Touch, SimaiNoteType.TouchHold, SimaiNoteType.Tap
            }));
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
            var slideTime = before[2].Notes[0].SlideTime;
            var slideStart = before[2].Notes[0].SlideStartTime;
            var bpm = before[0].Bpm;

            var result = chart.Scale(2f);

            var after = Timings(result);
            Assert.That(result, Is.SameAs(chart));
            Assert.That(after[1].Timing, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(after[0].Bpm, Is.EqualTo(bpm * 2f));
            Assert.That(after[1].Notes[0].HoldTime, Is.EqualTo(holdTime / 2).Within(1e-9));
            Assert.That(after[2].Notes[0].SlideTime, Is.EqualTo(slideTime / 2).Within(1e-9));
            Assert.That(after[2].Notes[0].SlideStartTime, Is.EqualTo(slideStart / 2).Within(1e-9));
        }

        /// <summary>Checks that every note becomes a break note, including slides.</summary>
        [Test]
        public void ConvertToBreak_MarksEveryNote()
        {
            var notes = Timings(Parse().ConvertToBreak()).SelectMany(timing => timing.Notes).ToArray();

            Assert.That(notes.All(note => note.IsBreak && note.IsSlideBreak), Is.True);
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
            Assert.That(timings[2].Notes[0].IsSlideNoHead, Is.True);
            Assert.That(timings[2].Notes[1].Type, Is.EqualTo(SimaiNoteType.Touch));
            Assert.That(timings[2].Notes[1].StartPosition, Is.EqualTo(3));
            Assert.That(timings[3].Notes.Single().TouchArea, Is.EqualTo('B'));
            Assert.That(timings[4].Notes.Single().Type, Is.EqualTo(SimaiNoteType.TouchHold));
            Assert.That(timings[5].Notes.All(note => note.Type == SimaiNoteType.Touch), Is.True);
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
            Assert.That(timings[1].Notes.Single().Type, Is.EqualTo(SimaiNoteType.Slide));
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
            Assert.That(note.StartPosition, Is.EqualTo(3));
        }

        /// <summary>Checks that a headless slide is kept by its own index.</summary>
        [Test]
        public void ClampByIndex_KeepsHeadlessSlide()
        {
            var headless = SimaiParser.ParseChart("1", "d", "(120){4}1,3?-5[4:1],E".AsSpan());

            var clamped = headless.Clamp(new Range<long>(1, 1));

            var note = Timings(clamped).Single().Notes.Single();
            Assert.That(note.Type, Is.EqualTo(SimaiNoteType.Slide));
            Assert.That(note.IsSlideNoHead, Is.True);
        }

        /// <summary>Checks that clamping by time keeps only timing points inside the range.</summary>
        [Test]
        public void ClampByTime_KeepsTimingPointsInRange()
        {
            var clamped = Parse().Clamp(new Range<double>(0.4, 1.1));

            var timings = Timings(clamped);
            Assert.That(timings.Select(timing => timing.Timing), Is.EqualTo(new[] { 0.5, 1.0 }).Within(1e-9));
            Assert.That(clamped.CommaTimings.Length, Is.EqualTo(0));
        }

        /// <summary>Checks that an offset moves timing points, comma timings and slide starts.</summary>
        [Test]
        public void AddOffset_ShiftsTimings()
        {
            var chart = Parse();
            var slideStart = chart.NoteTimings[2].Notes[0].SlideStartTime;
            var commaTimings = chart.CommaTimings.ToArray().Select(timing => timing.Timing).ToArray();
            var holdStart = chart.NoteTimings[1].Timing;

            var result = chart.AddOffset(1.5f);

            Assert.That(result, Is.SameAs(chart));
            Assert.That(chart.NoteTimings[1].Timing, Is.EqualTo(holdStart + 1.5).Within(1e-6));
            Assert.That(chart.NoteTimings[2].Notes[0].SlideStartTime, Is.EqualTo(slideStart + 1.5).Within(1e-6));
            var shifted = chart.CommaTimings.ToArray().Select(timing => timing.Timing).ToArray();
            Assert.That(shifted.Length, Is.EqualTo(commaTimings.Length));
            for (var i = 0; i < shifted.Length; i++)
            {
                Assert.That(shifted[i], Is.EqualTo(commaTimings[i] + 1.5).Within(1e-6));
            }
        }
    }
}
