using System;
using MajdataPlay.Collections;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the score value types <see cref="Accurate"/>, <see cref="DXScoreRank"/>, <see cref="MaiScore"/> and <see cref="SongScores"/>.</summary>
    public class ScoreValueTests
    {
        /// <summary>Checks that Update keeps the higher DX accuracy.</summary>
        [Test]
        public void AccurateUpdate_KeepsHigherDx()
        {
            var old = new Accurate { DX = 100.5, Classic = 99 };

            var lower = old.Update(new Accurate { DX = 90, Classic = 99 });
            var higher = old.Update(new Accurate { DX = 101, Classic = 99 });

            Assert.That(lower.DX, Is.EqualTo(100.5));
            Assert.That(higher.DX, Is.EqualTo(101));
        }

        /// <summary>Checks that Update takes a higher classic accuracy.</summary>
        [Test]
        public void AccurateUpdate_TakesHigherClassic()
        {
            var old = new Accurate { DX = 50, Classic = 80 };

            var updated = old.Update(new Accurate { DX = 50, Classic = 95 });

            Assert.That(updated.Classic, Is.EqualTo(95));
        }

        /// <summary>Checks that Update keeps the old classic accuracy when the new record is lower.</summary>
        [Test]
        public void AccurateUpdate_KeepsHigherClassic()
        {
            var old = new Accurate { DX = 50, Classic = 100 };

            var updated = old.Update(new Accurate { DX = 60, Classic = 70 });

            Assert.That(updated.Classic, Is.EqualTo(100));
        }

        /// <summary>Checks the comparison operators of <see cref="Accurate"/>.</summary>
        [Test]
        public void AccurateOperators_CompareBothComponents()
        {
            var a = new Accurate { DX = 100, Classic = 90 };
            var b = new Accurate { DX = 100, Classic = 90 };
            var betterDx = new Accurate { DX = 101, Classic = 80 };
            var betterClassic = new Accurate { DX = 90, Classic = 95 };

            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
            Assert.That(a == betterDx, Is.False);
            Assert.That(a != betterDx, Is.True);
            Assert.That(betterDx > a, Is.True);
            Assert.That(betterClassic > a, Is.True);
            Assert.That(a > b, Is.False);
            Assert.That(a < b, Is.True);
            Assert.That(betterDx < a, Is.False);
        }

        /// <summary>Checks the DX score rank, upper bound and lower bound for each rank band.</summary>
        /// <param name="dxScore">The achieved DX score.</param>
        /// <param name="expectedRank">The expected star rank.</param>
        /// <param name="expectedUpper">The expected upper bound of the band.</param>
        /// <param name="expectedLower">The expected lower bound of the band.</param>
        [TestCase(980, 5, 1000, 970)]
        [TestCase(1000, 5, 1000, 970)]
        [TestCase(970, 4, 970, 950)]
        [TestCase(960, 4, 970, 950)]
        [TestCase(940, 3, 950, 930)]
        [TestCase(920, 2, 930, 900)]
        [TestCase(870, 1, 900, 850)]
        [TestCase(850, 0, 850, 0)]
        [TestCase(0, 0, 850, 0)]
        public void DXScoreRank_ComputesBand(long dxScore, int expectedRank, long expectedUpper, long expectedLower)
        {
            var rank = new DXScoreRank(dxScore, 1000);

            Assert.That(rank.Rank, Is.EqualTo(expectedRank));
            Assert.That(rank.Upper, Is.EqualTo(expectedUpper));
            Assert.That(rank.Lower, Is.EqualTo(expectedLower));
        }

        /// <summary>Checks that a score record is created from a game result.</summary>
        [Test]
        public void MaiScoreCreateFromResult_CopiesResult()
        {
            var song = new FakeSongDetail("hash-a");
            var result = new GameResult
            {
                Acc = new Accurate { DX = 100, Classic = 99 },
                SongDetail = song,
                JudgeRecord = JudgeDetail.Empty,
                Level = ChartLevel.Master,
                Fast = 3,
                Late = 4,
                DXScore = 900,
                TotalDXScore = 1000,
                ComboState = ComboState.FC
            };
            var before = DateTime.Now;

            var score = MaiScore.CreateFromResult(in result, ChartLevel.Expert);

            Assert.That(score.ChartLevel, Is.EqualTo(ChartLevel.Expert));
            Assert.That(score.Hash, Is.EqualTo("hash-a"));
            Assert.That(score.PlayCount, Is.EqualTo(0));
            Assert.That(score.Acc == result.Acc, Is.True);
            Assert.That(score.DXScore, Is.EqualTo(900));
            Assert.That(score.TotalDXScore, Is.EqualTo(1000));
            Assert.That(score.JudgeDetail, Is.SameAs(JudgeDetail.Empty));
            Assert.That(score.Fast, Is.EqualTo(3));
            Assert.That(score.Late, Is.EqualTo(4));
            Assert.That(score.ComboState, Is.EqualTo(ComboState.FC));
            Assert.That(score.Timestamp, Is.GreaterThanOrEqualTo(before));
        }

        /// <summary>Checks the default values of a new score record.</summary>
        [Test]
        public void MaiScore_HasNeutralDefaults()
        {
            var score = new MaiScore();

            Assert.That(score.ChartLevel, Is.EqualTo(ChartLevel.Easy));
            Assert.That(score.Hash, Is.Null);
            Assert.That(score.PlayCount, Is.EqualTo(0));
            Assert.That(score.JudgeDetail, Is.Null);
            Assert.That(score.Timestamp, Is.EqualTo(DateTime.MinValue));
            Assert.That(score.ComboState, Is.EqualTo(ComboState.None));
        }

        /// <summary>Checks that song score buckets are created for every level with the requested hash.</summary>
        [Test]
        public void SongScoresCreate_CreatesEveryLevel()
        {
            var scores = SongScores.Create(new FakeSongDetail("hash-b"));

            var all = new[] { scores.Easy, scores.Basic, scores.Advance, scores.Expert, scores.Master, scores.ReMaster, scores.UTAGE };
            for (var i = 0; i < all.Length; i++)
            {
                Assert.That(all[i].Hash, Is.EqualTo("hash-b"));
                Assert.That(all[i].ChartLevel, Is.EqualTo((ChartLevel)i));
                Assert.That(all[i].PlayCount, Is.EqualTo(0));
            }
        }

        /// <summary>Checks that song score buckets require a hash.</summary>
        [Test]
        public void SongScoresCreate_EmptyHash_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => SongScores.Create(string.Empty));
            Assert.Throws<ArgumentNullException>(() => SongScores.Create((string)null!));
        }
    }
}
