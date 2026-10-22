using System.Collections.Generic;
using MajdataPlay.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>Builds judge statistics for result screen tests.</summary>
    public static class JudgeRecords
    {
        /// <summary>Creates judge counts where every grade not listed is zero.</summary>
        /// <param name="counts">Pairs of grade and count.</param>
        /// <returns>The judge info.</returns>
        public static JudgeInfo Info(params (JudgeGrade Grade, int Count)[] counts)
        {
            var dictionary = new Dictionary<JudgeGrade, int>();
            foreach (var grade in (JudgeGrade[])System.Enum.GetValues(typeof(JudgeGrade)))
            {
                dictionary[grade] = 0;
            }
            foreach (var (grade, count) in counts)
            {
                dictionary[grade] = count;
            }
            return new JudgeInfo(dictionary);
        }

        /// <summary>Creates a judge detail from per-note-type judge counts.</summary>
        /// <param name="tap">The tap counts.</param>
        /// <param name="hold">The hold counts.</param>
        /// <param name="slide">The slide counts.</param>
        /// <param name="touch">The touch counts.</param>
        /// <param name="breakInfo">The break counts.</param>
        /// <returns>The judge detail.</returns>
        public static JudgeDetail Detail(JudgeInfo? tap = null, JudgeInfo? hold = null, JudgeInfo? slide = null, JudgeInfo? touch = null, JudgeInfo? breakInfo = null)
        {
            return new JudgeDetail(new Dictionary<ScoreNoteType, JudgeInfo>
            {
                { ScoreNoteType.Tap, tap ?? Info() },
                { ScoreNoteType.Hold, hold ?? Info() },
                { ScoreNoteType.Slide, slide ?? Info() },
                { ScoreNoteType.Touch, touch ?? Info() },
                { ScoreNoteType.Break, breakInfo ?? Info() },
            });
        }
    }
}
