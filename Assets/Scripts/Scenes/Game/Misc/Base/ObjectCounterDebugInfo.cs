using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;

#nullable enable
namespace MajdataPlay.Scenes.Game
{
    /// <summary>
    /// The state of an <see cref="ObjectCounter"/> captured for the object counter inspector: the note sums, the judge
    /// statistics, the scores and achievement rates, together with the combos, the DX score, the timing samples, the
    /// per-grade counts and the display options the counter keeps private. Capturing allocates, so only editor tools
    /// capture it.
    /// </summary>
    internal sealed class ObjectCounterDebugInfo
    {
        /// <summary>Gets the number of taps in the chart, including slide heads that are not breaks.</summary>
        public int TapSum { get; init; }
        /// <summary>Gets the number of holds and touch holds in the chart.</summary>
        public int HoldSum { get; init; }
        /// <summary>Gets the number of slide tracks in the chart that are not breaks.</summary>
        public int SlideSum { get; init; }
        /// <summary>Gets the number of touches in the chart.</summary>
        public int TouchSum { get; init; }
        /// <summary>Gets the number of breaks in the chart, including break slide heads and break slide tracks.</summary>
        public int BreakSum { get; init; }
        /// <summary>Gets the number of notes in the chart.</summary>
        public int NoteSum { get; init; }
        /// <summary>Gets whether every note of the chart has been judged.</summary>
        public bool AllFinished { get; init; }

        /// <summary>Gets the judgment counts of every note kind and the fast and late counts.</summary>
        public ObjectCounter.NoteJudgeStatistics JudgeStats { get; init; }
        /// <summary>Gets the achievement rates in percent.</summary>
        public ObjectCounter.AccurateStatistics AccurateStats { get; init; }

        /// <summary>Gets the base score of the whole chart.</summary>
        public long TotalNoteBaseScore { get; init; }
        /// <summary>Gets the break extra score of the whole chart.</summary>
        public long TotalNoteExtraScore { get; init; }
        /// <summary>Gets the base score earned so far.</summary>
        public long CurrentNoteBaseScore { get; init; }
        /// <summary>Gets the break extra score earned so far.</summary>
        public long CurrentNoteExtraScore { get; init; }
        /// <summary>Gets the break extra score earned so far under the classic rules.</summary>
        public long CurrentNoteExtraScoreClassic { get; init; }
        /// <summary>Gets the base score lost so far.</summary>
        public long LostNoteBaseScore { get; init; }
        /// <summary>Gets the break extra score lost so far.</summary>
        public long LostNoteExtraScore { get; init; }
        /// <summary>Gets the break extra score lost so far under the classic rules.</summary>
        public long LostNoteExtraScoreClassic { get; init; }

        /// <summary>Gets the combo, which only a miss or a too fast judgment breaks.</summary>
        public long Combo { get; init; }
        /// <summary>Gets the perfect combo, which a great or worse judgment breaks.</summary>
        public long PerfectCombo { get; init; }
        /// <summary>Gets the critical perfect combo, which any judgment below critical perfect breaks.</summary>
        public long CriticalPerfectCombo { get; init; }
        /// <summary>Gets the DX score of the whole chart, three for each note.</summary>
        public long TotalDXScore { get; init; }
        /// <summary>Gets the DX score lost so far, as zero or a negative number.</summary>
        public long LostDXScore { get; init; }

        /// <summary>Gets the judge difference in milliseconds of the latest note that is not a slide; negative is fast.</summary>
        public float LastJudgeDiffMSec { get; init; }
        /// <summary>Gets the timer that fades the judge difference out of the background information; it starts at 3.</summary>
        public float DiffDisplayTimer { get; init; }
        /// <summary>Gets the judge differences in milliseconds of the judged taps and holds, in judgment order.</summary>
        public float[] NoteJudgeDiffsMSec { get; init; } = Array.Empty<float>();
        /// <summary>Gets whether the judge timing gauge is shown.</summary>
        public bool IsJudgeTimingGaugeEnabled { get; init; }
        /// <summary>Gets the average judge difference in milliseconds that places the cursor of the judge timing gauge.</summary>
        public float JudgeTimingGaugeAverageMSec { get; init; }
        /// <summary>Gets how many scales the judge timing gauge has, which is how many latest samples it averages.</summary>
        public int JudgeTimingGaugeScaleCount { get; init; }

        /// <summary>Gets which judgments of notes that are not breaks count as fast or late.</summary>
        public JudgeDisplayOption NoteFastLateDisplayOption { get; init; }
        /// <summary>Gets which judgments of breaks count as fast or late.</summary>
        public JudgeDisplayOption BreakFastLateDisplayOption { get; init; }
        /// <summary>Gets what the centre background information shows.</summary>
        public BGInfoOption CenterInfoOption { get; init; }
        /// <summary>Gets what the secondary background information shows; none while the judge timing gauge is shown.</summary>
        public BGInfoOption SecondaryInfoOption { get; init; }
        /// <summary>Gets what the background information of the sub screen shows.</summary>
        public BGInfoOption SubScreenInfoOption { get; init; }
        /// <summary>Gets what the top information shows.</summary>
        public TopInfoDisplayOption TopInfoOption { get; init; }

        /// <summary>Gets whether dan life replaces the centre background information.</summary>
        public bool IsDanLifeEnabled { get; init; }
        /// <summary>Gets the remaining dan life.</summary>
        public int CurrentHP { get; init; }
        /// <summary>Gets the maximum dan life.</summary>
        public int MaxHP { get; init; }

        /// <summary>Gets the judgment count of taps that are not breaks for each grade, indexed by <see cref="JudgeGrade"/>.</summary>
        public int[] TapGradeCounts { get; init; } = Array.Empty<int>();
        /// <summary>Gets the judgment count of holds that are not breaks for each grade, indexed by <see cref="JudgeGrade"/>.</summary>
        public int[] HoldGradeCounts { get; init; } = Array.Empty<int>();
        /// <summary>Gets the judgment count of touch holds that are not breaks for each grade, indexed by <see cref="JudgeGrade"/>.</summary>
        public int[] TouchHoldGradeCounts { get; init; } = Array.Empty<int>();
        /// <summary>Gets the judgment count of touches that are not breaks for each grade, indexed by <see cref="JudgeGrade"/>.</summary>
        public int[] TouchGradeCounts { get; init; } = Array.Empty<int>();
        /// <summary>Gets the judgment count of slides that are not breaks for each grade, indexed by <see cref="JudgeGrade"/>.</summary>
        public int[] SlideGradeCounts { get; init; } = Array.Empty<int>();
        /// <summary>Gets the judgment count of breaks of every kind for each grade, indexed by <see cref="JudgeGrade"/>.</summary>
        public int[] BreakGradeCounts { get; init; } = Array.Empty<int>();
        /// <summary>Gets the judgment count of every note for each grade, indexed by <see cref="JudgeGrade"/>.</summary>
        public int[] TotalGradeCounts { get; init; } = Array.Empty<int>();

        /// <summary>Gets the DX score so far: the DX score of the whole chart less the DX score lost so far.</summary>
        public long DXScore => TotalDXScore + LostDXScore;

        /// <summary>
        /// Cross-checks the counters with each other: no note kind may be judged more often than the chart has notes of
        /// it, and the judged count of each note kind and of all notes must agree with its critical to miss counts and with
        /// its per-grade counts. A mismatch points to a note that reported an unknown kind or a counter that was updated
        /// in one place but not the other.
        /// </summary>
        /// <returns>One message for each mismatch, or an empty list when the counters agree.</returns>
        public List<string> FindInconsistencies()
        {
            var problems = new List<string>();
            var stats = JudgeStats;
            CheckKind(problems, "Tap", TapSum, stats.TapJudgedCount,
                      stats.TapCriticalCount + stats.TapPerfectCount + stats.TapGreatCount + stats.TapGoodCount + stats.TapMissCount,
                      Sum(TapGradeCounts));
            CheckKind(problems, "Hold", HoldSum, stats.HoldJudgedCount,
                      stats.HoldCriticalCount + stats.HoldPerfectCount + stats.HoldGreatCount + stats.HoldGoodCount + stats.HoldMissCount,
                      Sum(HoldGradeCounts) + Sum(TouchHoldGradeCounts));
            CheckKind(problems, "Slide", SlideSum, stats.SlideJudgedCount,
                      stats.SlideCriticalCount + stats.SlidePerfectCount + stats.SlideGreatCount + stats.SlideGoodCount + stats.SlideMissCount,
                      Sum(SlideGradeCounts));
            CheckKind(problems, "Touch", TouchSum, stats.TouchJudgedCount,
                      stats.TouchCriticalCount + stats.TouchPerfectCount + stats.TouchGreatCount + stats.TouchGoodCount + stats.TouchMissCount,
                      Sum(TouchGradeCounts));
            CheckKind(problems, "Break", BreakSum, stats.BreakJudgedCount,
                      stats.BreakCriticalCount + stats.BreakPerfectCount + stats.BreakGreatCount + stats.BreakGoodCount + stats.BreakMissCount,
                      Sum(BreakGradeCounts));
            CheckKind(problems, "Total", NoteSum, stats.TotalJudgedCount,
                      stats.TotalCriticalCount + stats.TotalPerfectCount + stats.TotalGreatCount + stats.TotalGoodCount + stats.TotalMissCount,
                      Sum(TotalGradeCounts));

            var kindJudgedCount = stats.TapJudgedCount + stats.HoldJudgedCount + stats.SlideJudgedCount + stats.TouchJudgedCount + stats.BreakJudgedCount;
            if (kindJudgedCount != stats.TotalJudgedCount)
            {
                problems.Add($"Total: {stats.TotalJudgedCount} judged, but the note kinds add up to {kindJudgedCount}.");
            }
            return problems;
        }

        /// <summary>Cross-checks the counters of one note kind.</summary>
        /// <param name="problems">Receives a message for each mismatch.</param>
        /// <param name="name">The name of the note kind.</param>
        /// <param name="noteSum">How many notes of the kind the chart has.</param>
        /// <param name="judgedCount">How many notes of the kind have been judged.</param>
        /// <param name="columnSum">The sum of the critical, perfect, great, good and miss counts of the kind.</param>
        /// <param name="gradeSum">The sum of the per-grade counts of the kind.</param>
        private static void CheckKind(List<string> problems, string name, long noteSum, long judgedCount, long columnSum, long gradeSum)
        {
            if (judgedCount > noteSum)
            {
                problems.Add($"{name}: {judgedCount} judged, but the chart has {noteSum}.");
            }
            if (columnSum != judgedCount)
            {
                problems.Add($"{name}: {judgedCount} judged, but the critical to miss counts add up to {columnSum}.");
            }
            if (gradeSum != judgedCount)
            {
                problems.Add($"{name}: {judgedCount} judged, but the per-grade counts add up to {gradeSum}.");
            }
        }

        /// <summary>Adds up per-grade counts.</summary>
        /// <param name="counts">The per-grade counts.</param>
        /// <returns>The sum.</returns>
        private static long Sum(int[] counts)
        {
            var sum = 0L;
            foreach (var count in counts)
            {
                sum += count;
            }
            return sum;
        }
    }
}
