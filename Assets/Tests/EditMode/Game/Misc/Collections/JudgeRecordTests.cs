using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Collections
{
    /// <summary>Covers the judge count containers <see cref="JudgeInfo"/>, <see cref="JudgeDetail"/> and <see cref="UnpackJudgeInfo"/>.</summary>
    public class JudgeRecordTests
    {
        /// <summary>Creates a judge record with every grade set to zero except the given counts.</summary>
        /// <param name="counts">The grade counts to set.</param>
        /// <returns>The judge record.</returns>
        internal static JudgeInfo CreateInfo(params (JudgeGrade Grade, int Count)[] counts)
        {
            var dictionary = new Dictionary<JudgeGrade, int>();
            foreach (JudgeGrade grade in Enum.GetValues(typeof(JudgeGrade)))
            {
                dictionary[grade] = 0;
            }
            foreach (var (grade, count) in counts)
            {
                dictionary[grade] = count;
            }
            return new JudgeInfo(dictionary);
        }

        /// <summary>Checks that the empty record holds every grade with a zero count.</summary>
        [Test]
        public void JudgeInfoEmpty_HasEveryGradeAtZero()
        {
            var empty = JudgeInfo.Empty;

            Assert.That(empty.Count, Is.EqualTo(15));
            Assert.That(empty.Values.All(value => value == 0), Is.True);
            Assert.That(empty.Keys, Is.EquivalentTo(Enum.GetValues(typeof(JudgeGrade))));
            Assert.That(empty.ContainsKey(JudgeGrade.Perfect), Is.True);
            Assert.That(empty.TryGetValue(JudgeGrade.Miss, out var miss), Is.True);
            Assert.That(miss, Is.EqualTo(0));
        }

        /// <summary>Checks that the dictionary view enumerates the source entries through both interfaces.</summary>
        [Test]
        public void JudgeInfo_EnumeratesEntries()
        {
            var info = CreateInfo((JudgeGrade.Perfect, 3));
            var nonGeneric = 0;

            foreach (var item in (IEnumerable)info)
            {
                Assert.That(item, Is.InstanceOf<KeyValuePair<JudgeGrade, int>>());
                nonGeneric++;
            }

            Assert.That(nonGeneric, Is.EqualTo(15));
            Assert.That(info.Single(pair => pair.Key == JudgeGrade.Perfect).Value, Is.EqualTo(3));
            Assert.That(info[JudgeGrade.Perfect], Is.EqualTo(3));
        }

        /// <summary>Checks that adding two records sums every grade.</summary>
        [Test]
        public void JudgeInfoAddition_SumsGrades()
        {
            var left = CreateInfo((JudgeGrade.Perfect, 3), (JudgeGrade.Miss, 1));
            var right = CreateInfo((JudgeGrade.Perfect, 2), (JudgeGrade.FastGood, 4));

            var sum = left + right;

            Assert.That(sum[JudgeGrade.Perfect], Is.EqualTo(5));
            Assert.That(sum[JudgeGrade.Miss], Is.EqualTo(1));
            Assert.That(sum[JudgeGrade.FastGood], Is.EqualTo(4));
            Assert.That(sum.Count, Is.EqualTo(15));
        }

        /// <summary>Checks that a judge detail totals its note type records.</summary>
        [Test]
        public void JudgeDetail_TotalsRecords()
        {
            var tap = CreateInfo((JudgeGrade.Perfect, 10));
            var hold = CreateInfo((JudgeGrade.LateGreat, 2));
            var detail = new JudgeDetail(new Dictionary<ScoreNoteType, JudgeInfo>
            {
                { ScoreNoteType.Tap, tap },
                { ScoreNoteType.Hold, hold },
            });

            Assert.That(detail.Count, Is.EqualTo(2));
            Assert.That(detail[ScoreNoteType.Tap], Is.SameAs(tap));
            Assert.That(detail.TotalJudgeInfo[JudgeGrade.Perfect], Is.EqualTo(10));
            Assert.That(detail.TotalJudgeInfo[JudgeGrade.LateGreat], Is.EqualTo(2));
            Assert.That(detail.Keys, Is.EquivalentTo(new[] { ScoreNoteType.Tap, ScoreNoteType.Hold }));
            Assert.That(detail.Values.Count(), Is.EqualTo(2));
            Assert.That(detail.ContainsKey(ScoreNoteType.Break), Is.False);
            Assert.That(detail.TryGetValue(ScoreNoteType.Hold, out var found), Is.True);
            Assert.That(found, Is.SameAs(hold));
            Assert.That(detail.Count(), Is.EqualTo(2));
            var nonGeneric = 0;
            foreach (var item in (IEnumerable)detail)
            {
                nonGeneric++;
            }
            Assert.That(nonGeneric, Is.EqualTo(2));
        }

        /// <summary>Checks that the empty detail contains every note type with empty records.</summary>
        [Test]
        public void JudgeDetailEmpty_HasEveryNoteType()
        {
            var empty = JudgeDetail.Empty;

            Assert.That(empty.Count, Is.EqualTo(5));
            Assert.That(empty.TotalJudgeInfo.Values.Sum(), Is.EqualTo(0));
            Assert.That(empty[ScoreNoteType.Touch], Is.SameAs(JudgeInfo.Empty));
        }

        /// <summary>Checks that unpacking groups the grades and separates fast and late judgements.</summary>
        [Test]
        public void UnpackJudgeRecord_GroupsGrades()
        {
            var info = CreateInfo(
                (JudgeGrade.Perfect, 10),
                (JudgeGrade.FastPerfect2nd, 1),
                (JudgeGrade.LatePerfect3rd, 2),
                (JudgeGrade.FastGreat, 3),
                (JudgeGrade.LateGreat2nd, 4),
                (JudgeGrade.FastGood, 5),
                (JudgeGrade.LateGood, 6),
                (JudgeGrade.Miss, 7),
                (JudgeGrade.TooFast, 8));

            var unpacked = JudgeDetail.UnpackJudgeRecord(info);

            Assert.That(unpacked.CriticalPerfect, Is.EqualTo(10));
            Assert.That(unpacked.Perfect, Is.EqualTo(3));
            Assert.That(unpacked.Great, Is.EqualTo(7));
            Assert.That(unpacked.Good, Is.EqualTo(11));
            Assert.That(unpacked.Miss, Is.EqualTo(15));
            Assert.That(unpacked.Fast, Is.EqualTo(9));
            Assert.That(unpacked.Late, Is.EqualTo(12));
            Assert.That(unpacked.All, Is.EqualTo(31));
            Assert.That(unpacked.IsFullCombo, Is.False);
        }

        /// <summary>Checks the combo classifications derived from unpacked judgements.</summary>
        [Test]
        public void UnpackJudgeInfo_ClassifiesCombos()
        {
            var empty = JudgeDetail.UnpackJudgeRecord(JudgeInfo.Empty);
            var theoretical = JudgeDetail.UnpackJudgeRecord(CreateInfo((JudgeGrade.Perfect, 5)));
            var allPerfect = JudgeDetail.UnpackJudgeRecord(CreateInfo((JudgeGrade.Perfect, 5), (JudgeGrade.LatePerfect2nd, 1)));
            var fullComboPlus = JudgeDetail.UnpackJudgeRecord(CreateInfo((JudgeGrade.Perfect, 5), (JudgeGrade.FastGreat3rd, 1)));
            var fullCombo = JudgeDetail.UnpackJudgeRecord(CreateInfo((JudgeGrade.Perfect, 5), (JudgeGrade.FastGood, 1)));

            Assert.That(empty.IsFullCombo, Is.False);
            Assert.That(theoretical.IsTheoretical, Is.True);
            Assert.That(allPerfect.IsAllPerfect, Is.True);
            Assert.That(allPerfect.IsTheoretical, Is.False);
            Assert.That(fullComboPlus.IsFullComboPlus, Is.True);
            Assert.That(fullComboPlus.IsAllPerfect, Is.False);
            Assert.That(fullCombo.IsFullCombo, Is.True);
            Assert.That(fullCombo.IsFullComboPlus, Is.False);
        }
    }
}
