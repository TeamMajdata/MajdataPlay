using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using System;
using UnityEditor;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Editor
{
    using Editor = UnityEditor.Editor;

    /// <summary>
    /// Inspector of the object counter. In Play Mode it adds, below the serialized fields, the judgment progress with a
    /// cross-check of the counters, the judgments of every note kind, the achievement rates and scores, the combos and
    /// the DX score, the judge timing samples, the counts of every judge grade and the display options the counter read
    /// from the settings.
    /// </summary>
    [CustomEditor(typeof(ObjectCounter))]
    internal sealed class ObjectCounterInspector : Editor
    {
        /// <summary>The prefix of the session keys that remember the open sections.</summary>
        private const string SessionKeyPrefix = "MajdataPlay.ObjectCounterInspector.";

        /// <summary>How many of the latest judge differences the timing section lists.</summary>
        private const int LatestDiffCount = 8;

        /// <summary>The width of the first column of the tables.</summary>
        private const float HeaderColumnWidth = 92f;

        /// <summary>The width of the number columns of the tables.</summary>
        private const float NumberColumnWidth = 36f;

        /// <summary>The width of the wide number columns of the tables.</summary>
        private const float WideNumberColumnWidth = 72f;

        /// <summary>The judge grades in the order the grade table lists them, from fast to late.</summary>
        private static readonly JudgeGrade[] s_gradesFastToLate =
        {
            JudgeGrade.TooFast,
            JudgeGrade.FastGood,
            JudgeGrade.FastGreat3rd,
            JudgeGrade.FastGreat2nd,
            JudgeGrade.FastGreat,
            JudgeGrade.FastPerfect3rd,
            JudgeGrade.FastPerfect2nd,
            JudgeGrade.Perfect,
            JudgeGrade.LatePerfect2nd,
            JudgeGrade.LatePerfect3rd,
            JudgeGrade.LateGreat,
            JudgeGrade.LateGreat2nd,
            JudgeGrade.LateGreat3rd,
            JudgeGrade.LateGood,
            JudgeGrade.Miss
        };

        /// <summary>The left-aligned label style that understands rich text, created on first use because it needs the editor skin.</summary>
        private static GUIStyle? s_richLabel;

        /// <summary>The right-aligned label style that understands rich text, created on first use because it needs the editor skin.</summary>
        private static GUIStyle? s_richNumberLabel;

        /// <summary>Repaints every frame in Play Mode so the inspector follows the counter.</summary>
        /// <returns><see langword="true"/> while the editor is playing.</returns>
        public override bool RequiresConstantRepaint()
        {
            return EditorApplication.isPlaying;
        }

        /// <summary>Draws the serialized fields and, in Play Mode, the counters of the chart being played.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Counter Debug", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect the judgments, scores and combos counted for the chart.", MessageType.Info);
                return;
            }
            var info = ((ObjectCounter)target).CaptureDebugInfo();

            DrawOverview(info);
            DrawJudgments(info);
            DrawScores(info);
            DrawCombo(info);
            DrawTiming(info);
            DrawGrades(info);
            DrawDisplayOptions(info);
        }

        /// <summary>Draws how many notes have been judged, the dan life and the cross-check of the counters.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawOverview(ObjectCounterDebugInfo info)
        {
            if (!Foldout("Overview", "Overview"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                var judged = info.JudgeStats.TotalJudgedCount;
                var progress = info.NoteSum == 0 ? 0f : Mathf.Clamp01((float)judged / info.NoteSum);
                EditorGUI.ProgressBar(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect()), progress, $"Notes judged: {judged} / {info.NoteSum}");
                EditorGUILayout.LabelField("All finished", info.AllFinished ? "yes" : "no");
                if (info.IsDanLifeEnabled)
                {
                    EditorGUILayout.LabelField("Dan life", $"{info.CurrentHP} / {info.MaxHP}");
                }
                var problems = info.FindInconsistencies();
                if (problems.Count == 0)
                {
                    EditorGUILayout.LabelField("Consistency", "the counters agree");
                }
                else
                {
                    EditorGUILayout.HelpBox("The counters disagree:\n" + string.Join("\n", problems), MessageType.Warning);
                }
            }
        }

        /// <summary>Draws the judged count and the critical to miss counts of every note kind, with the fast and late counts.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawJudgments(ObjectCounterDebugInfo info)
        {
            if (!Foldout("Judgments", "Judgments"))
            {
                return;
            }
            var stats = info.JudgeStats;
            DrawJudgmentRow("<b>Kind</b>", "<b>Judged</b>", "<b>CP</b>", "<b>P</b>", "<b>GR</b>", "<b>GD</b>", "<b>Miss</b>");
            DrawJudgmentRow("Tap", stats.TapJudgedCount, info.TapSum,
                            stats.TapCriticalCount, stats.TapPerfectCount, stats.TapGreatCount, stats.TapGoodCount, stats.TapMissCount);
            DrawJudgmentRow("Hold", stats.HoldJudgedCount, info.HoldSum,
                            stats.HoldCriticalCount, stats.HoldPerfectCount, stats.HoldGreatCount, stats.HoldGoodCount, stats.HoldMissCount);
            DrawJudgmentRow("Slide", stats.SlideJudgedCount, info.SlideSum,
                            stats.SlideCriticalCount, stats.SlidePerfectCount, stats.SlideGreatCount, stats.SlideGoodCount, stats.SlideMissCount);
            DrawJudgmentRow("Touch", stats.TouchJudgedCount, info.TouchSum,
                            stats.TouchCriticalCount, stats.TouchPerfectCount, stats.TouchGreatCount, stats.TouchGoodCount, stats.TouchMissCount);
            DrawJudgmentRow("Break", stats.BreakJudgedCount, info.BreakSum,
                            stats.BreakCriticalCount, stats.BreakPerfectCount, stats.BreakGreatCount, stats.BreakGoodCount, stats.BreakMissCount);
            DrawJudgmentRow("<b>Total</b>", stats.TotalJudgedCount, info.NoteSum,
                            stats.TotalCriticalCount, stats.TotalPerfectCount, stats.TotalGreatCount, stats.TotalGoodCount, stats.TotalMissCount);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Fast / Late", $"{stats.TotalFastCount} / {stats.TotalLateCount}");
            }
        }

        /// <summary>Draws one note kind of the judgment table.</summary>
        /// <param name="kind">The note kind.</param>
        /// <param name="judged">How many notes of the kind have been judged.</param>
        /// <param name="sum">How many notes of the kind the chart has.</param>
        /// <param name="critical">The critical perfect count.</param>
        /// <param name="perfect">The perfect count.</param>
        /// <param name="great">The great count.</param>
        /// <param name="good">The good count.</param>
        /// <param name="miss">The miss count, including too fast judgments.</param>
        private static void DrawJudgmentRow(string kind, long judged, int sum, long critical, long perfect, long great, long good, long miss)
        {
            var judgedCell = judged > sum ? Colored($"{judged} / {sum}", GetWarningColor()) : $"{judged} / {sum}";
            DrawJudgmentRow(kind, judgedCell, FormatCount(critical), FormatCount(perfect), FormatCount(great), FormatCount(good), FormatCount(miss));
        }

        /// <summary>Draws one row of the judgment table.</summary>
        /// <param name="kind">The note kind.</param>
        /// <param name="judged">The judged count and the note count.</param>
        /// <param name="critical">The critical perfect count.</param>
        /// <param name="perfect">The perfect count.</param>
        /// <param name="great">The great count.</param>
        /// <param name="good">The good count.</param>
        /// <param name="miss">The miss count.</param>
        private static void DrawJudgmentRow(string kind, string judged, string critical, string perfect, string great, string good, string miss)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                BeginRow(kind);
                GUILayout.Label(judged, GetRichNumberLabel(), GUILayout.Width(WideNumberColumnWidth));
                DrawNumberCell(critical);
                DrawNumberCell(perfect);
                DrawNumberCell(great);
                DrawNumberCell(good);
                DrawNumberCell(miss);
            }
        }

        /// <summary>Draws the achievement rates and the base, extra and classic extra scores.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawScores(ObjectCounterDebugInfo info)
        {
            if (!Foldout("Scores", "Achievement and scores"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                var acc = info.AccurateStats;
                EditorGUILayout.LabelField("Achievement (+)", $"{acc.Achievement_C:0.0000}%");
                EditorGUILayout.LabelField("Achievement 101 (-)", $"{acc.Achievement_A:0.0000}%");
                EditorGUILayout.LabelField("Achievement 100 (-)", $"{acc.Achievement_B:0.0000}%");
                EditorGUILayout.LabelField("Classic (+)", $"{acc.ClassicAchievement_A:0.00}%");
                EditorGUILayout.LabelField("Classic (-)", $"{acc.ClassicAchievement_B:0.00}%");
            }
            DrawScoreRow("<b>Score</b>", "<b>Current</b>", "<b>Lost</b>", "<b>Total</b>");
            DrawScoreRow("Base", info.CurrentNoteBaseScore.ToString(), info.LostNoteBaseScore.ToString(), info.TotalNoteBaseScore.ToString());
            DrawScoreRow("Break extra", info.CurrentNoteExtraScore.ToString(), info.LostNoteExtraScore.ToString(), info.TotalNoteExtraScore.ToString());
            DrawScoreRow("Classic extra", info.CurrentNoteExtraScoreClassic.ToString(), info.LostNoteExtraScoreClassic.ToString(), info.TotalNoteExtraScore.ToString());
        }

        /// <summary>Draws one row of the score table.</summary>
        /// <param name="name">The score name.</param>
        /// <param name="current">The score earned so far.</param>
        /// <param name="lost">The score lost so far.</param>
        /// <param name="total">The score of the whole chart.</param>
        private static void DrawScoreRow(string name, string current, string lost, string total)
        {
            var style = GetRichNumberLabel();
            using (new EditorGUILayout.HorizontalScope())
            {
                BeginRow(name);
                GUILayout.Label(current, style, GUILayout.Width(WideNumberColumnWidth));
                GUILayout.Label(lost, style, GUILayout.Width(WideNumberColumnWidth));
                GUILayout.Label(total, style, GUILayout.Width(WideNumberColumnWidth));
            }
        }

        /// <summary>Draws the combos and the DX score with its star rank.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawCombo(ObjectCounterDebugInfo info)
        {
            if (!Foldout("Combo", "Combo and DX score"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Combo", info.Combo.ToString());
                EditorGUILayout.LabelField("Perfect combo", info.PerfectCombo.ToString());
                EditorGUILayout.LabelField("Critical perfect combo", info.CriticalPerfectCombo.ToString());
                EditorGUILayout.LabelField("DX score", $"{info.DXScore} / {info.TotalDXScore} (lost {-info.LostDXScore})");
                var rank = info.TotalDXScore == 0 ? 0 : new DXScoreRank(info.DXScore, info.TotalDXScore).Rank;
                EditorGUILayout.LabelField("DX stars", rank.ToString());
            }
        }

        /// <summary>Draws the latest judge difference, the statistics of the timing samples and the judge timing gauge.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawTiming(ObjectCounterDebugInfo info)
        {
            if (!Foldout("Timing", "Timing"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Last difference", FormatDiff(info.LastJudgeDiffMSec));
                EditorGUILayout.LabelField("Difference fade timer", $"{info.DiffDisplayTimer:0.00}");
                var diffs = info.NoteJudgeDiffsMSec;
                EditorGUILayout.LabelField("Tap and hold samples", diffs.Length.ToString());
                if (diffs.Length > 0)
                {
                    GetDiffStatistics(diffs, out var mean, out var standardDeviation, out var min, out var max);
                    EditorGUILayout.LabelField("Mean", FormatDiff(mean));
                    EditorGUILayout.LabelField("Standard deviation", $"{standardDeviation:0.00} ms");
                    EditorGUILayout.LabelField("Range", $"{FormatDiff(min)} to {FormatDiff(max)}");
                    EditorGUILayout.LabelField("Latest", FormatLatestDiffs(diffs));
                }
                var gauge = info.IsJudgeTimingGaugeEnabled
                    ? $"{FormatDiff(info.JudgeTimingGaugeAverageMSec)} (average of the latest {info.JudgeTimingGaugeScaleCount})"
                    : "disabled";
                EditorGUILayout.LabelField("Timing gauge", gauge);
                EditorGUILayout.LabelField("Fast/late of notes", info.NoteFastLateDisplayOption.ToString());
                EditorGUILayout.LabelField("Fast/late of breaks", info.BreakFastLateDisplayOption.ToString());
            }
        }

        /// <summary>Draws the judgment counts of every judge grade for every note kind.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawGrades(ObjectCounterDebugInfo info)
        {
            if (!Foldout("Grades", "Judge grades", false))
            {
                return;
            }
            DrawGradeRow("<b>Grade</b>", "<b>Tap</b>", "<b>Hold</b>", "<b>THold</b>", "<b>Touch</b>", "<b>Slide</b>", "<b>Break</b>", "<b>Total</b>");
            foreach (var grade in s_gradesFastToLate)
            {
                var index = (int)grade;
                DrawGradeRow(grade.ToString(),
                             FormatGradeCount(info.TapGradeCounts, index),
                             FormatGradeCount(info.HoldGradeCounts, index),
                             FormatGradeCount(info.TouchHoldGradeCounts, index),
                             FormatGradeCount(info.TouchGradeCounts, index),
                             FormatGradeCount(info.SlideGradeCounts, index),
                             FormatGradeCount(info.BreakGradeCounts, index),
                             FormatGradeCount(info.TotalGradeCounts, index));
            }
        }

        /// <summary>Draws one row of the judge grade table.</summary>
        /// <param name="grade">The judge grade.</param>
        /// <param name="tap">The count of taps.</param>
        /// <param name="hold">The count of holds.</param>
        /// <param name="touchHold">The count of touch holds.</param>
        /// <param name="touch">The count of touches.</param>
        /// <param name="slide">The count of slides.</param>
        /// <param name="breakCount">The count of breaks.</param>
        /// <param name="total">The count of all notes.</param>
        private static void DrawGradeRow(string grade, string tap, string hold, string touchHold, string touch, string slide, string breakCount, string total)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                BeginRow(grade);
                DrawNumberCell(tap);
                DrawNumberCell(hold);
                DrawNumberCell(touchHold);
                DrawNumberCell(touch);
                DrawNumberCell(slide);
                DrawNumberCell(breakCount);
                DrawNumberCell(total);
            }
        }

        /// <summary>Draws what the background information, the sub screen and the top information show.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawDisplayOptions(ObjectCounterDebugInfo info)
        {
            if (!Foldout("Display", "Display options", false))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                var center = info.IsDanLifeEnabled ? "dan life" : info.CenterInfoOption.ToString();
                EditorGUILayout.LabelField("Centre BG info", center);
                EditorGUILayout.LabelField("Secondary BG info", info.IsJudgeTimingGaugeEnabled ? "None (timing gauge shown)" : info.SecondaryInfoOption.ToString());
                EditorGUILayout.LabelField("Sub screen BG info", info.SubScreenInfoOption.ToString());
                EditorGUILayout.LabelField("Top info", info.TopInfoOption.ToString());
            }
        }

        /// <summary>
        /// Computes the mean, the population standard deviation and the range of the judge differences.
        /// </summary>
        /// <param name="diffs">The judge differences in milliseconds; must not be empty.</param>
        /// <param name="mean">Receives the mean.</param>
        /// <param name="standardDeviation">Receives the population standard deviation.</param>
        /// <param name="min">Receives the smallest difference, which is the fastest one.</param>
        /// <param name="max">Receives the largest difference, which is the latest one.</param>
        internal static void GetDiffStatistics(float[] diffs, out float mean, out float standardDeviation, out float min, out float max)
        {
            var sum = 0d;
            min = float.MaxValue;
            max = float.MinValue;
            foreach (var diff in diffs)
            {
                sum += diff;
                min = Math.Min(min, diff);
                max = Math.Max(max, diff);
            }
            var average = sum / diffs.Length;
            var squareSum = 0d;
            foreach (var diff in diffs)
            {
                var deviation = diff - average;
                squareSum += deviation * deviation;
            }
            mean = (float)average;
            standardDeviation = (float)Math.Sqrt(squareSum / diffs.Length);
        }

        /// <summary>Formats the latest judge differences, the newest last.</summary>
        /// <param name="diffs">The judge differences in milliseconds, in judgment order.</param>
        /// <returns>Up to <see cref="LatestDiffCount"/> differences joined by commas.</returns>
        internal static string FormatLatestDiffs(float[] diffs)
        {
            var count = Math.Min(LatestDiffCount, diffs.Length);
            var cells = new string[count];
            for (var i = 0; i < count; i++)
            {
                cells[i] = diffs[diffs.Length - count + i].ToString("+0.0;-0.0;0.0");
            }
            return string.Join(", ", cells);
        }

        /// <summary>Formats a judge difference.</summary>
        /// <param name="diffMSec">The difference in milliseconds; negative is fast.</param>
        /// <returns>The signed difference with whether it is fast or late.</returns>
        internal static string FormatDiff(float diffMSec)
        {
            var direction = diffMSec < 0f ? " fast" : diffMSec > 0f ? " late" : string.Empty;
            return $"{diffMSec:+0.00;-0.00;0.00} ms{direction}";
        }

        /// <summary>Formats a count of the judge grade table, greying out zeros so the judged grades stand out.</summary>
        /// <param name="counts">The per-grade counts of a note kind.</param>
        /// <param name="index">The index of the judge grade.</param>
        /// <returns>The count as rich text.</returns>
        private static string FormatGradeCount(int[] counts, int index)
        {
            return FormatCount(index < counts.Length ? counts[index] : 0);
        }

        /// <summary>Formats a count, greying out zeros so the other counts stand out.</summary>
        /// <param name="count">The count.</param>
        /// <returns>The count as rich text.</returns>
        private static string FormatCount(long count)
        {
            return count == 0 ? Colored("0", EditorGUIUtility.isProSkin ? "#6F6F6F" : "#9C9C9C") : count.ToString();
        }

        /// <summary>Indents a table row and draws its first column.</summary>
        /// <param name="header">The text of the first column.</param>
        private static void BeginRow(string header)
        {
            GUILayout.Space(EditorGUI.indentLevel * 15f + 15f);
            GUILayout.Label(header, GetRichLabel(), GUILayout.Width(HeaderColumnWidth));
        }

        /// <summary>Draws a right-aligned number column of a table row.</summary>
        /// <param name="text">The text of the column.</param>
        private static void DrawNumberCell(string text)
        {
            GUILayout.Label(text, GetRichNumberLabel(), GUILayout.Width(NumberColumnWidth));
        }

        /// <summary>Draws a section header that remembers whether it is open for this editor session.</summary>
        /// <param name="key">The key of the section.</param>
        /// <param name="title">The section title.</param>
        /// <param name="isOpenByDefault">Whether the section is open the first time.</param>
        /// <returns>Whether the section is open.</returns>
        private static bool Foldout(string key, string title, bool isOpenByDefault = true)
        {
            var sessionKey = SessionKeyPrefix + key;
            var isOpen = SessionState.GetBool(sessionKey, isOpenByDefault);
            var newIsOpen = EditorGUILayout.Foldout(isOpen, title, true, EditorStyles.foldoutHeader);
            if (newIsOpen != isOpen)
            {
                SessionState.SetBool(sessionKey, newIsOpen);
            }
            return newIsOpen;
        }

        /// <summary>Gets the colour that marks a count the chart cannot have.</summary>
        /// <returns>The colour as a rich text colour value.</returns>
        private static string GetWarningColor()
        {
            return EditorGUIUtility.isProSkin ? "#FF8A65" : "#C62828";
        }

        /// <summary>Wraps text in a rich text colour tag.</summary>
        /// <param name="text">The text.</param>
        /// <param name="color">The colour, for example #808080.</param>
        /// <returns>The coloured text.</returns>
        private static string Colored(string text, string color)
        {
            return $"<color={color}>{text}</color>";
        }

        /// <summary>Gets the left-aligned label style that understands rich text.</summary>
        /// <returns>The style.</returns>
        private static GUIStyle GetRichLabel()
        {
            s_richLabel ??= new GUIStyle(EditorStyles.label) { richText = true };
            return s_richLabel;
        }

        /// <summary>Gets the right-aligned label style that understands rich text.</summary>
        /// <returns>The style.</returns>
        private static GUIStyle GetRichNumberLabel()
        {
            s_richNumberLabel ??= new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleRight };
            return s_richNumberLabel;
        }
    }
}
