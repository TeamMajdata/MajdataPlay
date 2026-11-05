using System;
using MajdataPlay.Extensions;
using MajSimai;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Extensions
{
    /// <summary>Covers the slide head lookups of <see cref="SimaiNoteExtensions"/> on charts parsed by MajSimai 3.x.</summary>
    public class SimaiNoteExtensionsTests
    {
        /// <summary>Parses a chart and returns the notes of its first timing point.</summary>
        /// <param name="fumen">The chart text.</param>
        /// <returns>The notes of the first note timing point.</returns>
        private static SimaiNote[] FirstNotes(string fumen)
        {
            var chart = SimaiParser.ParseChart(fumen.AsSpan());
            return chart.NoteTimings[0].Notes;
        }

        /// <summary>Checks that a slide with a star head is a head followed by a slide that draws it.</summary>
        [Test]
        public void HeadedSlide_SlideDrawsItsHead()
        {
            var notes = FirstNotes("(120){4}1-5[8:1],");

            Assert.That(notes.Length, Is.EqualTo(2));
            Assert.That(notes[0].IsSlideHead(), Is.True);
            Assert.That(notes[1].TryGetSlideHead(notes, out var head), Is.True);
            Assert.That(head, Is.SameAs(notes[0]));
            Assert.That(notes[1].IsHeadlessSlide(notes), Is.False);
        }

        /// <summary>Checks that slides written without a head have no head.</summary>
        [TestCase("(120){4}1?-5[8:1],")]
        [TestCase("(120){4}1!-5[8:1],")]
        public void SlideWithoutHead_IsHeadless(string fumen)
        {
            var notes = FirstNotes(fumen);

            Assert.That(notes.Length, Is.EqualTo(1));
            Assert.That(notes[0].Type, Is.EqualTo(SimaiNoteType.Slide));
            Assert.That(notes[0].TryGetSlideHead(notes, out var head), Is.False);
            Assert.That(head, Is.Null);
            Assert.That(notes[0].IsHeadlessSlide(notes), Is.True);
        }

        /// <summary>Checks that only the first part of a same-head group draws the head, as in the 2.x model.</summary>
        [Test]
        public void SameHeadGroup_OnlyFirstSlideDrawsHead()
        {
            var notes = FirstNotes("(120){4}1-3[8:1]*-5[8:1],");
            var head = Array.Find(notes, note => note.IsSlideHead());
            var slides = Array.FindAll(notes, note => note.Type == SimaiNoteType.Slide);

            Assert.That(head, Is.Not.Null);
            Assert.That(slides.Length, Is.EqualTo(2));
            Assert.That(slides[0].IsHeadlessSlide(notes), Is.False);
            Assert.That(slides[1].IsHeadlessSlide(notes), Is.True);
        }

        /// <summary>Checks that notes other than slides are never headless slides and that plain taps are not heads.</summary>
        [Test]
        public void NonSlideNotes_AreNotHeadlessSlides()
        {
            var notes = FirstNotes("(120){4}1/2h[4:1],");

            foreach (var note in notes)
            {
                Assert.That(note.IsSlideHead(), Is.False);
                Assert.That(note.IsHeadlessSlide(notes), Is.False);
                Assert.That(note.TryGetSlideHead(notes, out _), Is.False);
            }
        }

        /// <summary>Checks that the lookups reject a null note.</summary>
        [Test]
        public void NullNote_Throws()
        {
            SimaiNote? note = null;

            Assert.Throws<ArgumentNullException>(() => note!.IsSlideHead());
            Assert.Throws<ArgumentNullException>(() => note!.TryGetSlideHead(Array.Empty<SimaiNote>(), out _));
            Assert.Throws<ArgumentNullException>(() => note!.IsHeadlessSlide(Array.Empty<SimaiNote>()));
        }
    }
}
