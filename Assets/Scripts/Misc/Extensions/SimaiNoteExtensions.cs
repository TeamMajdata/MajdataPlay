using MajSimai;
using System;
using System.Diagnostics.CodeAnalysis;

#nullable enable
namespace MajdataPlay.Extensions
{
    /// <summary>
    /// Reads the slide head relations of the MajSimai 3.x note model.
    /// </summary>
    /// <remarks>
    /// Since MajSimai 3.0 a slide with a head, such as <c>1-5[8:1]</c>, is a <see cref="SimaiNoteType.Tap"/> head
    /// followed by a <see cref="SimaiNoteType.Slide"/> path; the head lists its slides in
    /// <see cref="SimaiNote.RelatedNotes"/>. Only the first related slide of a head draws that head, which matches the
    /// 2.x model where the other parts of a same-head group (<c>1-3[8:1]*-5[8:1]</c>) were slides without a head.
    /// </remarks>
    public static class SimaiNoteExtensions
    {
        /// <summary>
        /// Gets whether a note is the star (or <c>@</c> tap) head of one or more slides.
        /// </summary>
        /// <param name="note">The note to check.</param>
        /// <returns><see langword="true"/> when the note is a <see cref="SimaiNoteType.Tap"/> with related slides.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="note"/> is <see langword="null"/>.</exception>
        public static bool IsSlideHead(this SimaiNote note)
        {
            if (note is null)
            {
                throw new ArgumentNullException(nameof(note));
            }

            return note.Type == SimaiNoteType.Tap && note.RelatedNotes.Length != 0;
        }

        /// <summary>
        /// Finds the head that a slide draws, that is the head whose first related note is the slide.
        /// </summary>
        /// <param name="slide">The slide to look up.</param>
        /// <param name="notes">The notes of the timing point that holds the slide.</param>
        /// <param name="head">When this method returns <see langword="true"/>, the head of the slide; otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> when the slide draws a head; <see langword="false"/> for a slide without a head,
        /// a later part of a same-head group, or a note that is not a slide.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="slide"/> is <see langword="null"/>.</exception>
        public static bool TryGetSlideHead(this SimaiNote slide,
                                           ReadOnlySpan<SimaiNote> notes,
                                           [NotNullWhen(true)] out SimaiNote? head)
        {
            if (slide is null)
            {
                throw new ArgumentNullException(nameof(slide));
            }

            head = null;
            if (slide.Type != SimaiNoteType.Slide)
            {
                return false;
            }
            foreach (var note in notes)
            {
                if (note is null || !note.IsSlideHead())
                {
                    continue;
                }
                if (ReferenceEquals(note.RelatedNotes[0], slide))
                {
                    head = note;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets whether a slide is drawn without a head of its own (the 2.x <c>IsSlideNoHead</c>).
        /// </summary>
        /// <param name="slide">The slide to check.</param>
        /// <param name="notes">The notes of the timing point that holds the slide.</param>
        /// <returns><see langword="true"/> when <paramref name="slide"/> is a <see cref="SimaiNoteType.Slide"/> that does not
        /// draw a head; <see langword="false"/> for a slide that draws a head or a note that is not a slide.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="slide"/> is <see langword="null"/>.</exception>
        public static bool IsHeadlessSlide(this SimaiNote slide, ReadOnlySpan<SimaiNote> notes)
        {
            if (slide is null)
            {
                throw new ArgumentNullException(nameof(slide));
            }

            return slide.Type == SimaiNoteType.Slide && !slide.TryGetSlideHead(notes, out _);
        }
    }
}
