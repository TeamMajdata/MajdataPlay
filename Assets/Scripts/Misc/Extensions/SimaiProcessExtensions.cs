using MajdataPlay.Buffers;
using MajdataPlay.Numerics;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Extensions
{
    public static class SimaiProcessExtensions
    {
        public static SimaiChart Scale(this SimaiChart source,float timeScale)
        {
            var timingPoints = source.NoteTimings;
            foreach(var timingPoint in timingPoints)
            {
                timingPoint.Bpm *= timeScale;
                timingPoint.Timing /= timeScale;
                foreach(var note in timingPoint.Notes)
                {
                    note.HoldTime /= timeScale;
                    note.SlideStartTime /= timeScale;
                    note.SlideTime /= timeScale;
                }
            }
            return source;
        }
        /// <summary>
        /// Turns every note of a chart into a break note (the All Break mod). The chart is changed in place.
        /// </summary>
        /// <remarks>
        /// Since MajSimai 3.0 the star head of a slide is a <see cref="SimaiNoteType.Tap"/> of its own, and
        /// <see cref="SimaiNote.IsBreak"/> on a <see cref="SimaiNoteType.Slide"/> marks a break slide path, so setting
        /// <see cref="SimaiNote.IsBreak"/> on every note breaks both the head and the path, as the 2.x
        /// <c>IsBreak</c> and <c>IsSlideBreak</c> did.
        /// </remarks>
        /// <param name="source">The chart to change.</param>
        /// <returns><paramref name="source"/>.</returns>
        public static SimaiChart ConvertToBreak(this SimaiChart source)
        {
            var timingPoints = source.NoteTimings;
            foreach (var timingPoint in timingPoints)
            {
                foreach (var note in timingPoint.Notes)
                {
                    note.IsBreak = true;
                }
            }
            return source;
        }
        public static SimaiChart ConvertToEx(this SimaiChart source)
        {
            var timingPoints = source.NoteTimings;
            foreach (var timingPoint in timingPoints)
            {
                foreach (var note in timingPoint.Notes)
                    note.IsEx = true;
            }
            return source;
        }
        /// <summary>
        /// Turns every button note of a chart into a touch note on the A sensor of its button (the All Touch mod). The
        /// chart is changed in place.
        /// </summary>
        /// <remarks>
        /// A tap, including the star head of a slide, becomes a <see cref="SimaiNoteType.Touch"/> and a hold becomes a
        /// <see cref="SimaiNoteType.TouchHold"/>; both keep <see cref="SimaiNote.IsBreak"/> and <see cref="SimaiNote.IsEx"/>.
        /// Slides are kept and have no head any more: the head that drew them is replaced by a touch without
        /// <see cref="SimaiNote.RelatedNotes"/>, so no note references it. As before MajSimai 3.0, the touch of a head is
        /// placed right after the slide it drew, and the touch notes of a timing point are moved after its other notes.
        /// The group flags (<see cref="SimaiNote.IsEach"/>, <see cref="SimaiNote.IsStar"/> and
        /// <see cref="SimaiNote.IsStarDouble"/>) are computed again for the new notes of each timing point.
        /// </remarks>
        /// <param name="source">The chart to change.</param>
        /// <returns><paramref name="source"/>.</returns>
        public static SimaiChart ConvertToTouch(this SimaiChart source)
        {
            var timingPoints = source.NoteTimings;
            var newNoteList = new List<SimaiNote>();
            var touchNotes = new List<SimaiNote>();
            foreach (var timingPoint in timingPoints)
            {
                var notes = timingPoint.Notes;
                newNoteList.Clear();
                touchNotes.Clear();
                foreach (var note in notes)
                {
                    switch (note.Type)
                    {
                        case SimaiNoteType.Touch:
                        case SimaiNoteType.TouchHold:
                            touchNotes.Add(note);
                            break;
                        case SimaiNoteType.Slide:
                            newNoteList.Add(note);
                            if (note.TryGetSlideHead(notes, out var head))
                            {
                                newNoteList.Add(CreateTouch(head));
                            }
                            break;
                        case SimaiNoteType.Tap:
                            // The head of a slide in this timing point becomes a touch right after that slide.
                            if (!IsHeadOfSlideIn(note, notes))
                            {
                                newNoteList.Add(CreateTouch(note));
                            }
                            break;
                        case SimaiNoteType.Hold:
                            newNoteList.Add(new SimaiNote()
                            {
                                Type = SimaiNoteType.TouchHold,
                                StartPosition = note.StartPosition,
                                HoldTime = note.HoldTime,
                                TouchArea = 'A',
                                IsBreak = note.IsBreak,
                                IsEx = note.IsEx
                            });
                            break;
                    }
                }
                newNoteList.AddRange(touchNotes);
                var newNotes = newNoteList.ToArray();
                RefreshGroupFlags(newNotes);
                timingPoint.Notes = newNotes;
            }
            return source;
        }
        /// <summary>
        /// Creates a chart that only keeps the notes whose combo index is inside a range (practice by combo).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Notes are numbered in chart order, one index per note of <see cref="SimaiTimingPoint.Notes"/>. A slide with a
        /// head therefore takes two indices, its star head <c>i</c> and its first slide <c>i + 1</c>, which directly
        /// follows the head; the other slides of a same-head group and slides without a head take one index each.
        /// </para>
        /// <para>
        /// A note other than a head is kept when its index is in the range. A head is kept as the head of its slides
        /// when its first slide is kept, even if the head index itself is outside the range; its
        /// <see cref="SimaiNote.RelatedNotes"/> then only lists the kept slides. When its first slide is dropped but the
        /// head index is in the range, the head becomes a star-shaped tap (<c>$$</c>) without slides, so the other kept
        /// slides of its group have no head, as in the 2.x note model.
        /// </para>
        /// <para>
        /// The kept notes of a timing point that lost notes are copies, so the relations and the group flags
        /// (<see cref="SimaiNote.IsEach"/>, <see cref="SimaiNote.IsStar"/> and <see cref="SimaiNote.IsStarDouble"/>) of
        /// <paramref name="source"/> are left unchanged; timing points that keep every note share their notes with
        /// <paramref name="source"/>. The new chart has no comma timing points.
        /// </para>
        /// </remarks>
        /// <param name="source">The chart to read.</param>
        /// <param name="noteIndexRange">The range of note indices to keep.</param>
        /// <returns>A new chart with the kept notes and the level, designer and text of <paramref name="source"/>.</returns>
        public static SimaiChart Clamp(this SimaiChart source,Range<long> noteIndexRange)
        {
            using PooledList<SimaiTimingPoint> newTimingList = new();
            var currentIndex = 0L;
            for (var i = 0; i < source.NoteTimings.Length; i++)
            {
                var noteTiming = source.NoteTimings[i];
                var newNotes = ClampNotes(noteTiming.Notes, noteIndexRange, currentIndex);
                currentIndex += noteTiming.Notes.Length;
                if (newNotes.Length != 0)
                {
                    var newTimingPoint = new SimaiTimingPoint(noteTiming.Timing,
                                                              newNotes,
                                                              noteTiming.RawContent,
                                                              noteTiming.RawTextPositionX,
                                                              noteTiming.RawTextPositionY,
                                                              noteTiming.Bpm,
                                                              noteTiming.HSpeed,
                                                              noteTiming.SVeloc,
                                                              noteTiming.RawTextPosition,
                                                              noteTiming.SignatureNumerator,
                                                              noteTiming.SignatureDenominator);
                    newTimingList.Add(newTimingPoint);
                }
            }
            using (var buffer = Pool<SimaiTimingPoint>.Rent(newTimingList.Count))
            {
                newTimingList.CopyTo(buffer);
                return new SimaiChart(source.Level, source.Designer, source.Fumen, buffer.AsSpan(0, newTimingList.Count));
            }
            //source.NoteTimings = newTimingList.ToArray();
        }
        /// <summary>
        /// Creates a chart that only keeps the timing points whose time is inside a range (practice by time).
        /// </summary>
        /// <remarks>
        /// The kept timing points and their notes are shared with <paramref name="source"/>. The new chart has no comma
        /// timing points.
        /// </remarks>
        /// <param name="source">The chart to read.</param>
        /// <param name="timestampRange">The range of times, in seconds from the start of the chart, to keep.</param>
        /// <returns>A new chart with the kept timing points and the level, designer and text of <paramref name="source"/>.</returns>
        public static SimaiChart Clamp(this SimaiChart source, Range<double> timestampRange)
        {
            using PooledList<SimaiTimingPoint> newTimingList = new();
            foreach(var noteTiming in source.NoteTimings)
            {
                if(timestampRange.InRange(noteTiming.Timing))
                {
                    newTimingList.Add(noteTiming);
                }
            }
            //source.NoteTimings = newTimingList.ToArray();
            using (var buffer = Pool<SimaiTimingPoint>.Rent(newTimingList.Count, true))
            {
                newTimingList.CopyTo(buffer);
                return new SimaiChart(source.Level, source.Designer, source.Fumen, buffer.AsSpan(0, newTimingList.Count));
            }
        }
        public static SimaiChart AddOffset(this SimaiChart source, float timingOffsetMSec)
        {
            var noteTimingCount = source.NoteTimings.Length;
            var commaTimingCount = source.CommaTimings.Length;

            Parallel.For(0, noteTimingCount, i =>
            {
                ref readonly var tp = ref source.NoteTimings[i];
                tp.Timing += timingOffsetMSec;
                for (var k = 0; k < tp.Notes.Length; k++)
                {
                    ref var note = ref tp.Notes[k];
                    switch(note.Type)
                    {
                        case SimaiNoteType.Slide:
                            note.SlideStartTime += timingOffsetMSec;
                            break;
                    }
                }
            });
            Parallel.For(0, commaTimingCount, i =>
            {
                ref readonly var tp = ref source.CommaTimings[i];
                tp.Timing += timingOffsetMSec;
            });

            return source;
        }
        /// <summary>
        /// Selects the notes of one timing point that <see cref="Clamp(SimaiChart, Range{long})"/> keeps.
        /// </summary>
        /// <param name="notes">The notes of the timing point.</param>
        /// <param name="noteIndexRange">The range of note indices to keep.</param>
        /// <param name="firstIndex">The note index of the first note of the timing point.</param>
        /// <returns>The kept notes in chart order, or an empty array when no note is kept.</returns>
        private static SimaiNote[] ClampNotes(SimaiNote[] notes, Range<long> noteIndexRange, long firstIndex)
        {
            var keptNotes = new List<SimaiNote>(notes.Length);
            var isChanged = false;
            for (var i = 0; i < notes.Length; i++)
            {
                var note = notes[i];
                if (!note.IsSlideHead())
                {
                    if (noteIndexRange.InRange(firstIndex + i))
                    {
                        keptNotes.Add(note);
                    }
                    else
                    {
                        isChanged = true;
                    }
                    continue;
                }
                var firstSlideIndex = IndexOfReference(notes, note.RelatedNotes[0]);
                if (firstSlideIndex != -1 && noteIndexRange.InRange(firstIndex + firstSlideIndex))
                {
                    keptNotes.Add(note);
                }
                else if (noteIndexRange.InRange(firstIndex + i))
                {
                    keptNotes.Add(CreateFakeStar(note));
                    isChanged = true;
                }
                else
                {
                    isChanged = true;
                }
            }
            if (!isChanged)
            {
                return keptNotes.ToArray();
            }

            return CopyKeptNotes(keptNotes);
        }
        /// <summary>
        /// Copies the kept notes of a timing point that lost notes, so that each kept head only lists the copies of its
        /// kept slides, and computes the group flags of the copies.
        /// </summary>
        /// <param name="keptNotes">The kept notes in chart order.</param>
        /// <returns>The copies in the same order.</returns>
        private static SimaiNote[] CopyKeptNotes(List<SimaiNote> keptNotes)
        {
            var copies = new SimaiNote[keptNotes.Count];
            for (var i = 0; i < copies.Length; i++)
            {
                copies[i] = CopyNote(keptNotes[i]);
            }
            var keptRelatedNotes = new List<SimaiNote>();
            for (var i = 0; i < copies.Length; i++)
            {
                var relatedNotes = keptNotes[i].RelatedNotes;
                if (relatedNotes.Length == 0)
                {
                    continue;
                }
                keptRelatedNotes.Clear();
                foreach (var relatedNote in relatedNotes)
                {
                    var keptIndex = IndexOfReference(keptNotes, relatedNote);
                    if (keptIndex != -1)
                    {
                        keptRelatedNotes.Add(copies[keptIndex]);
                    }
                }
                copies[i].RelatedNotes = keptRelatedNotes.ToArray();
            }
            RefreshGroupFlags(copies);

            return copies;
        }
        /// <summary>
        /// Creates the star-shaped tap that stands for the head of a slide whose first slide was clamped away.
        /// </summary>
        /// <param name="head">The head of the slide.</param>
        /// <returns>A <see cref="SimaiNoteType.Tap"/> with <see cref="SimaiNote.IsForceStar"/> and
        /// <see cref="SimaiNote.IsFakeRotate"/> on the button of <paramref name="head"/>, and without slides.</returns>
        private static SimaiNote CreateFakeStar(SimaiNote head)
        {
            return new SimaiNote()
            {
                Type = SimaiNoteType.Tap,
                IsForceStar = true,
                IsFakeRotate = true,
                IsStar = true,
                IsBreak = head.IsBreak,
                IsEx = head.IsEx,
                RawContent = $"{head.StartPosition}",
                StartPosition = head.StartPosition,
            };
        }
        /// <summary>
        /// Creates the touch note that replaces a tap, or the head of a slide, in <see cref="ConvertToTouch(SimaiChart)"/>.
        /// </summary>
        /// <param name="note">The tap to replace.</param>
        /// <returns>A <see cref="SimaiNoteType.Touch"/> on the A sensor of the button of <paramref name="note"/>.</returns>
        private static SimaiNote CreateTouch(SimaiNote note)
        {
            return new SimaiNote()
            {
                Type = SimaiNoteType.Touch,
                StartPosition = note.StartPosition,
                TouchArea = 'A',
                IsBreak = note.IsBreak,
                IsEx = note.IsEx
            };
        }
        /// <summary>
        /// Copies every value of a note except <see cref="SimaiNote.RelatedNotes"/>, which the copy leaves empty.
        /// </summary>
        /// <param name="note">The note to copy.</param>
        /// <returns>The copy.</returns>
        private static SimaiNote CopyNote(SimaiNote note)
        {
            return new SimaiNote()
            {
                Type = note.Type,
                StartPosition = note.StartPosition,
                TouchArea = note.TouchArea,
                HoldTime = note.HoldTime,
                SlideStartTime = note.SlideStartTime,
                SlideTime = note.SlideTime,
                RawContent = note.RawContent,
                IsBreak = note.IsBreak,
                IsEx = note.IsEx,
                IsFakeRotate = note.IsFakeRotate,
                IsForceStar = note.IsForceStar,
                IsHanabi = note.IsHanabi,
                IsTapHeadSlide = note.IsTapHeadSlide,
                IsMine = note.IsMine,
                UsingSV = note.UsingSV,
                IsEach = note.IsEach,
                IsStar = note.IsStar,
                IsStarDouble = note.IsStarDouble,
            };
        }
        /// <summary>
        /// Gets whether a note is the head of a slide whose first slide is one of the given notes.
        /// </summary>
        /// <param name="note">The note to check.</param>
        /// <param name="notes">The notes of the timing point that holds <paramref name="note"/>.</param>
        /// <returns><see langword="true"/> when <paramref name="note"/> is a head and <paramref name="notes"/> holds its
        /// first slide.</returns>
        private static bool IsHeadOfSlideIn(SimaiNote note, SimaiNote[] notes)
        {
            return note.IsSlideHead() && IndexOfReference(notes, note.RelatedNotes[0]) != -1;
        }
        /// <summary>
        /// Finds a note by reference.
        /// </summary>
        /// <param name="notes">The notes to search.</param>
        /// <param name="target">The note to find.</param>
        /// <returns>The index of <paramref name="target"/> in <paramref name="notes"/>, or -1 when it is not there.</returns>
        private static int IndexOfReference(IReadOnlyList<SimaiNote> notes, SimaiNote target)
        {
            for (var i = 0; i < notes.Count; i++)
            {
                if (ReferenceEquals(notes[i], target))
                {
                    return i;
                }
            }

            return -1;
        }
        /// <summary>
        /// Computes the group flags of the notes of a timing point the way the MajSimai parser does:
        /// <see cref="SimaiNote.IsEach"/>, and <see cref="SimaiNote.IsStar"/> and <see cref="SimaiNote.IsStarDouble"/> of taps.
        /// </summary>
        /// <param name="notes">Every note of the timing point.</param>
        private static void RefreshGroupFlags(SimaiNote[] notes)
        {
            var slideCount = 0;
            foreach (var note in notes)
            {
                if (note.Type == SimaiNoteType.Slide)
                {
                    slideCount++;
                }
            }
            var otherCount = notes.Length - slideCount;
            foreach (var note in notes)
            {
                if (note.Type == SimaiNoteType.Slide)
                {
                    note.IsEach = slideCount > 1;
                    continue;
                }
                note.IsEach = otherCount > 1;
                if (note.Type != SimaiNoteType.Tap)
                {
                    continue;
                }
                var hasSlides = note.RelatedNotes.Length != 0;
                note.IsStar = note.IsForceStar || (hasSlides && !note.IsTapHeadSlide);
                note.IsStarDouble = note.IsStar && hasSlides && CountSameButtonSlides(notes, note.StartPosition) > 1;
            }
        }
        /// <summary>
        /// Counts the slides of a timing point that start from a button and are not mines.
        /// </summary>
        /// <param name="notes">Every note of the timing point.</param>
        /// <param name="startPosition">The button.</param>
        /// <returns>The number of such slides.</returns>
        private static int CountSameButtonSlides(SimaiNote[] notes, int startPosition)
        {
            var count = 0;
            foreach (var note in notes)
            {
                if (note.Type == SimaiNoteType.Slide && !note.IsMine && note.StartPosition == startPosition)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
