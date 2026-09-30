using MajdataPlay.Buffers;
using MajdataPlay.Numerics;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public static SimaiChart ConvertToBreak(this SimaiChart source)
        {
            var timingPoints = source.NoteTimings;
            foreach (var timingPoint in timingPoints)
            {
                foreach (var note in timingPoint.Notes)
                {
                    note.IsBreak = true;
                    note.IsSlideBreak = true;
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
        public static SimaiChart ConvertToTouch(this SimaiChart source)
        {
            var timingPoints = source.NoteTimings;
            foreach (var timingPoint in timingPoints)
            {
                var notes = timingPoint.Notes;
                var touchNotes = notes.Where(x => x.Type is SimaiNoteType.Touch or SimaiNoteType.TouchHold);
                var newNoteList = new List<SimaiNote>();
                var noteCount = notes.Count();
                for (var i = 0; i < noteCount; i++)
                {
                    var note = notes[i];
                    switch(note.Type)
                    {
                        case SimaiNoteType.Touch:
                        case SimaiNoteType.TouchHold:
                            continue;
                        case SimaiNoteType.Slide:
                            {
                                if (note.IsSlideNoHead)
                                {
                                    newNoteList.Add(note);
                                    continue;
                                }
                                note.IsSlideNoHead = true;
                                var startKey = note.StartPosition;
                                newNoteList.Add(note);
                                newNoteList.Add(new SimaiNote()
                                {
                                    Type = SimaiNoteType.Touch,
                                    StartPosition = startKey,
                                    TouchArea = 'A',
                                    IsBreak = note.IsBreak,
                                    IsEx = note.IsEx
                                });
                            }
                            break;
                        case SimaiNoteType.Tap:
                            {
                                var startKey = note.StartPosition;
                                newNoteList.Add(new SimaiNote()
                                {
                                    Type = SimaiNoteType.Touch,
                                    StartPosition = startKey,
                                    TouchArea = 'A',
                                    IsBreak = note.IsBreak,
                                    IsEx = note.IsEx
                                });
                            }
                            break;
                        case SimaiNoteType.Hold:
                            {
                                var startKey = note.StartPosition;
                                newNoteList.Add(new SimaiNote()
                                {
                                    Type = SimaiNoteType.TouchHold,
                                    StartPosition = startKey,
                                    HoldTime = note.HoldTime,
                                    TouchArea = 'A',
                                    IsBreak = note.IsBreak,
                                    IsEx = note.IsEx
                                });
                            }
                            break;
                    }
                }
                foreach (var touch in touchNotes)
                    newNoteList.Add(touch);
                timingPoint.Notes = newNoteList.ToArray();
            }
            return source;
        }
        public static SimaiChart Clamp(this SimaiChart source,Range<long> noteIndexRange)
        {
            using PooledList<SimaiTimingPoint> newTimingList = new();
            var currentIndex = 0;
            for (var i = 0; i < source.NoteTimings.Length; i++)
            {
                var noteTiming = source.NoteTimings[i];
                List<SimaiNote> newNoteList = new();
                for (var j = 0; j < noteTiming.Notes.Length; j++)
                {
                    var note = noteTiming.Notes[j];
                    switch (note.Type)
                    {
                        case SimaiNoteType.Tap:
                        case SimaiNoteType.Hold:
                        case SimaiNoteType.Touch:
                        case SimaiNoteType.TouchHold:
                            if (noteIndexRange.InRange(currentIndex))
                            {
                                newNoteList.Add(note);
                            }
                            break;
                        case SimaiNoteType.Slide:
                            if (note.IsSlideNoHead)
                            {
                                if (noteIndexRange.InRange(currentIndex))
                                {
                                    newNoteList.Add(note);
                                }
                            }
                            else
                            {
                                if (noteIndexRange.InRange(currentIndex + 1))
                                {
                                    newNoteList.Add(note);
                                    currentIndex++;
                                }
                                else if (noteIndexRange.InRange(currentIndex))
                                {
                                    newNoteList.Add(new()
                                    {
                                        Type = SimaiNoteType.Tap,
                                        IsForceStar = true,
                                        IsFakeRotate = true,
                                        IsBreak = note.IsBreak,
                                        IsEx = note.IsEx,
                                        RawContent = $"{note.StartPosition}",
                                        StartPosition = note.StartPosition,
                                    });
                                }
                            }
                            break;
                    }
                    currentIndex++;
                }
                if (newNoteList.Count != 0) 
                {
                    var newTimingPoint = new SimaiTimingPoint(noteTiming.Timing,
                                                              newNoteList.ToArray(),
                                                              noteTiming.RawContent,
                                                              noteTiming.RawTextPositionX,
                                                              noteTiming.RawTextPositionY,
                                                              noteTiming.Bpm, 
                                                              noteTiming.HSpeed);
                    newTimingList.Add(newTimingPoint);
                }
            }
            using (var buffer = Pool<SimaiTimingPoint>.Rent(newTimingList.Count))
            {
                newTimingList.CopyTo(buffer);
                return new SimaiChart(source.Level, source.Designer, source.Fumen, buffer.AsSpan(0, newTimingList.Count), null);
            }
            //source.NoteTimings = newTimingList.ToArray();
        }
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
                return new SimaiChart(source.Level, source.Designer, source.Fumen, buffer.AsSpan(0, newTimingList.Count), null);
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
    }
}