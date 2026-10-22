using System.Collections;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ResultScene
{
    /// <summary>Covers the per-note-type judge table of the result sub display.</summary>
    public class ResultsSubDisplayManagerTests : SceneTestBase
    {
        /// <summary>The note type prefixes of the table rows.</summary>
        private static readonly string[] s_rows = { "Note", "Hold", "Slide", "Touch", "Break" };

        /// <summary>The judge columns of the table.</summary>
        private static readonly string[] s_columns = { "Critical", "Perfect", "Great", "Good", "Miss" };

        /// <summary>The manager under test.</summary>
        private ResultsSubDisplayManager _manager = null!;

        /// <summary>Creates the manager with one text per table cell.</summary>
        [SetUp]
        public void SetUp()
        {
            _manager = Objects.Add<ResultsSubDisplayManager>();
            foreach (var row in s_rows)
            {
                foreach (var column in s_columns)
                {
                    typeof(ResultsSubDisplayManager).GetField($"{row}_{column}")!.SetValue(_manager, CreateText($"{row}_{column}"));
                }
            }
        }

        /// <summary>Gets the text of one table cell.</summary>
        /// <param name="row">The note type prefix.</param>
        /// <param name="column">The judge column.</param>
        /// <returns>The cell text.</returns>
        private string Cell(string row, string column)
        {
            return ((TextMeshProUGUI)typeof(ResultsSubDisplayManager).GetField($"{row}_{column}")!.GetValue(_manager)).text;
        }

        /// <summary>Checks that each note type's grades are grouped into the five judge columns.</summary>
        [Test]
        public void SetJudgeDetail_GroupsGradesPerNoteType()
        {
            var detail = JudgeRecords.Detail(
                tap: JudgeRecords.Info((JudgeGrade.Perfect, 10), (JudgeGrade.FastPerfect2nd, 2), (JudgeGrade.LatePerfect3rd, 1), (JudgeGrade.FastGreat, 3), (JudgeGrade.LateGreat3rd, 1), (JudgeGrade.FastGood, 4), (JudgeGrade.Miss, 5), (JudgeGrade.TooFast, 1)),
                hold: JudgeRecords.Info((JudgeGrade.Perfect, 7), (JudgeGrade.LateGood, 2)),
                slide: JudgeRecords.Info((JudgeGrade.LateGreat2nd, 6)),
                touch: JudgeRecords.Info((JudgeGrade.FastGreat3rd, 8), (JudgeGrade.Miss, 1)));

            _manager.SetJudgeDetail(detail);

            Assert.That(Cell("Note", "Critical"), Is.EqualTo("10"));
            Assert.That(Cell("Note", "Perfect"), Is.EqualTo("3"));
            Assert.That(Cell("Note", "Great"), Is.EqualTo("4"));
            Assert.That(Cell("Note", "Good"), Is.EqualTo("4"));
            Assert.That(Cell("Note", "Miss"), Is.EqualTo("6"));
            Assert.That(Cell("Hold", "Critical"), Is.EqualTo("7"));
            Assert.That(Cell("Hold", "Good"), Is.EqualTo("2"));
            Assert.That(Cell("Slide", "Great"), Is.EqualTo("6"));
            Assert.That(Cell("Touch", "Great"), Is.EqualTo("8"));
            Assert.That(Cell("Touch", "Miss"), Is.EqualTo("1"));
            Assert.That(Cell("Break", "Perfect"), Is.EqualTo("0"));
        }

        /// <summary>Checks that break perfects are split into 2550 and 2500 point counts.</summary>
        [Test]
        public void SetJudgeDetail_SplitsBreakPerfectsByScore()
        {
            var detail = JudgeRecords.Detail(breakInfo: JudgeRecords.Info(
                (JudgeGrade.Perfect, 9),
                (JudgeGrade.FastPerfect2nd, 2),
                (JudgeGrade.LatePerfect2nd, 1),
                (JudgeGrade.FastPerfect3rd, 4),
                (JudgeGrade.LatePerfect3rd, 0),
                (JudgeGrade.LateGreat, 3)));

            _manager.SetJudgeDetail(detail);

            Assert.That(Cell("Break", "Critical"), Is.EqualTo("9"));
            Assert.That(Cell("Break", "Perfect"), Is.EqualTo("3+4"));
            Assert.That(Cell("Break", "Great"), Is.EqualTo("3"));
        }

        /// <summary>Checks that the empty lifecycle messages run without changing the table.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Lifecycle_DoesNotChangeTable()
        {
            _manager.SetJudgeDetail(JudgeRecords.Detail());
            yield return null;
            yield return null;

            Assert.That(Cell("Note", "Critical"), Is.EqualTo("0"));
            Assert.That(Cell("Break", "Perfect"), Is.EqualTo("0"));
        }
    }
}
