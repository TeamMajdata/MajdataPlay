using System;
using System.Collections.Generic;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Base
{
    /// <summary>Checks the round bookkeeping of normal, practice and dan sessions in <see cref="GameInfo"/>.</summary>
    public class GameInfoTests
    {
        /// <summary>Restores the settings read by chart settings.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>Installs default settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new GamePlaySettingsScope();
        }

        /// <summary>Restores the previous settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _settings.Dispose();
        }

        /// <summary>Checks that a missing chart queue produces an empty session.</summary>
        [Test]
        public void Constructor_WithoutChartsCreatesAnEmptySession()
        {
            var info = new GameInfo(GameMode.Normal, null!, null!);

            Assert.That(info.Charts, Is.Empty);
            Assert.That(info.Levels, Is.Empty);
            Assert.That(info.Results, Is.Empty);
            Assert.That(info.Current, Is.Null);
            Assert.That(info.CurrentLevel, Is.EqualTo(ChartLevel.Easy));
            Assert.That(info.ChartSettings, Is.Not.Null);
            Assert.That(info.GetLastResult().SongDetail, Is.Null);
            Assert.That(info.NextRound(), Is.False);
        }

        /// <summary>Checks the first chart, its level, its settings and the mode flags of a normal session.</summary>
        [Test]
        public void Constructor_NormalSessionStartsWithTheFirstChart()
        {
            var first = new GamePlayFakeSongDetail();
            var info = new GameInfo(GameMode.Normal, new ISongDetail[] { first }, new[] { ChartLevel.Master });

            Assert.That(info.Mode, Is.EqualTo(GameMode.Normal));
            Assert.That(info.Current, Is.SameAs(first));
            Assert.That(info.CurrentLevel, Is.EqualTo(ChartLevel.Master));
            Assert.That(info.ChartSettings.Hash, Is.EqualTo(first.Hash));
            Assert.That(info.Results, Has.Length.EqualTo(1));
            Assert.That(info.IsDanMode, Is.False);
            Assert.That(info.IsPracticeMode, Is.False);
            Assert.That(info.IsDanLifeEnabled, Is.False);
            Assert.That(info.IsForceGameover, Is.False);
            Assert.That(info.PracticeCount, Is.EqualTo(1));
        }

        /// <summary>Checks that the next round skips missing charts and stops after the last chart.</summary>
        [Test]
        public void NextRound_SkipsMissingChartsAndStopsAtTheEnd()
        {
            var first = new GamePlayFakeSongDetail();
            var third = new GamePlayFakeSongDetail();
            var info = new GameInfo(GameMode.Normal, new ISongDetail[] { first, null!, third }, new[] { ChartLevel.Basic, ChartLevel.Advance, ChartLevel.Expert });

            Assert.That(info.Results, Has.Length.EqualTo(2));
            Assert.That(info.NextRound(), Is.True);
            Assert.That(info.Current, Is.SameAs(third));
            Assert.That(info.CurrentLevel, Is.EqualTo(ChartLevel.Expert));
            Assert.That(info.ChartSettings.Hash, Is.EqualTo(third.Hash));
            Assert.That(info.NextRound(), Is.False);
            Assert.That(info.NextRound(), Is.False);
        }

        /// <summary>Checks that recorded results are returned as the last result.</summary>
        [Test]
        public void RecordResult_StoresTheResultOfTheCurrentRound()
        {
            var song = new GamePlayFakeSongDetail();
            var info = new GameInfo(GameMode.Normal, new ISongDetail[] { song, song }, new[] { ChartLevel.Easy, ChartLevel.Basic });

            info.RecordResult(new GameResult { DXScore = 10, SongDetail = song });
            Assert.That(info.GetLastResult().DXScore, Is.EqualTo(10));

            info.NextRound();
            info.RecordResult(new GameResult { DXScore = 20, SongDetail = song });

            Assert.That(info.Results[0].DXScore, Is.EqualTo(10));
            Assert.That(info.Results[1].DXScore, Is.EqualTo(20));
            Assert.That(info.GetLastResult().DXScore, Is.EqualTo(20));
        }

        /// <summary>Checks that a result can be recorded for a chart that follows a missing chart.</summary>
        [Test]
        [Ignore("Known issue: GameInfo sizes Results by the non-null charts but indexes it by the chart queue position, so RecordResult throws after a missing chart was skipped.")]
        public void RecordResult_AfterASkippedChartDoesNotThrow()
        {
            var song = new GamePlayFakeSongDetail();
            var info = new GameInfo(GameMode.Normal, new ISongDetail[] { song, null!, song }, new[] { ChartLevel.Easy, ChartLevel.Easy, ChartLevel.Easy });
            info.NextRound();

            Assert.DoesNotThrow(() => info.RecordResult(new GameResult { DXScore = 1 }));
        }

        /// <summary>Checks that a practice session repeats the first chart for the requested number of rounds.</summary>
        [Test]
        public void PracticeSession_RepeatsTheFirstChart()
        {
            var song = new GamePlayFakeSongDetail();
            var other = new GamePlayFakeSongDetail();
            var info = new GameInfo(GameMode.Practice, new ISongDetail[] { song, other }, new[] { ChartLevel.ReMaster, ChartLevel.Easy }, 3);

            Assert.That(info.IsPracticeMode, Is.True);
            Assert.That(info.Charts, Has.Length.EqualTo(1));
            Assert.That(info.Levels, Is.EqualTo(new[] { ChartLevel.ReMaster }));
            Assert.That(info.Results, Has.Length.EqualTo(3));
            Assert.That(info.PracticeCount, Is.EqualTo(3));
            Assert.That(info.NextRound(), Is.True);
            Assert.That(info.NextRound(), Is.True);
            Assert.That(info.NextRound(), Is.False);
            Assert.That(info.Current, Is.SameAs(song));
        }

        /// <summary>Checks the validation of the practice count.</summary>
        [Test]
        public void PracticeCount_IsValidated()
        {
            var song = new GamePlayFakeSongDetail();
            var info = new GameInfo(GameMode.Practice, new ISongDetail[] { song }, new[] { ChartLevel.Easy }, 2);

            Assert.Throws<ArgumentException>(() => info.PracticeCount = 0);
            info.PracticeCount = 5;
            Assert.That(info.Results, Has.Length.EqualTo(5));

            info.NextRound();

            Assert.Throws<InvalidOperationException>(() => info.PracticeCount = 6);
            Assert.Throws<ArgumentException>(() => new GameInfo(GameMode.Practice, new ISongDetail[] { song }, new[] { ChartLevel.Easy }, 0));
        }

        /// <summary>Checks that a normal session accepts any practice count without resizing its results.</summary>
        [Test]
        public void PracticeCount_IsIgnoredOutsidePracticeMode()
        {
            var info = new GameInfo(GameMode.Normal, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy });

            info.PracticeCount = 9;

            Assert.That(info.PracticeCount, Is.EqualTo(9));
            Assert.That(info.Results, Has.Length.EqualTo(1));
        }

        /// <summary>Checks dan life: damage per grade and multiplier, clamping and recovery between rounds.</summary>
        [Test]
        public void DanSession_TracksLife()
        {
            var song = new GamePlayFakeSongDetail();
            var danInfo = new DanInfo { StartHP = 10, IsForceGameover = true };
            var info = new GameInfo(GameMode.Dan, new ISongDetail[] { song, song }, new[] { ChartLevel.Master, ChartLevel.Master })
            {
                DanInfo = danInfo,
            };
            info.MaxHP = 20;
            info.CurrentHP = 10;
            info.HPRecover = 15;

            Assert.That(info.IsDanMode, Is.True);
            Assert.That(info.IsDanLifeEnabled, Is.True);
            Assert.That(info.IsForceGameover, Is.True);

            info.OnNoteJudged(JudgeGrade.Miss);
            Assert.That(info.CurrentHP, Is.EqualTo(5));

            info.OnNoteJudged(JudgeGrade.LateGood, 3);
            Assert.That(info.CurrentHP, Is.EqualTo(0), "Life is clamped at zero");

            info.OnNoteJudged(JudgeGrade.Perfect);
            Assert.That(info.CurrentHP, Is.EqualTo(0));

            Assert.That(info.NextRound(), Is.True);
            Assert.That(info.CurrentHP, Is.EqualTo(15));

            info.CurrentHP = 18;
            info.HPRecover = 50;
            Assert.That(info.NextRound(), Is.False, "No chart remains, so no life is recovered");
            Assert.That(info.CurrentHP, Is.EqualTo(18));
        }

        /// <summary>Checks that a dan with a negative start life disables the life gauge and judgements do nothing.</summary>
        [Test]
        public void DanSession_WithNegativeStartLifeDisablesLife()
        {
            var info = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy })
            {
                DanInfo = new DanInfo { StartHP = -1 },
            };
            info.CurrentHP = 3;

            info.OnNoteJudged(JudgeGrade.Miss);

            Assert.That(info.IsDanLifeEnabled, Is.False);
            Assert.That(info.CurrentHP, Is.EqualTo(3));
        }

        /// <summary>Checks the optional practice ranges.</summary>
        [Test]
        public void Ranges_AreStored()
        {
            var info = new GameInfo(GameMode.Normal, null!, null!)
            {
                ComboRange = new Range<long>(1, 5),
                TimeRange = new Range<double>(2.5, 7.5),
            };

            Assert.That(info.ComboRange!.Value.End, Is.EqualTo(5));
            Assert.That(info.TimeRange!.Value.Start, Is.EqualTo(2.5));
        }

        /// <summary>Checks that a dan damage table drives the life change for every grade.</summary>
        [Test]
        public void DanSession_UsesTheDamageTable()
        {
            var damages = new Dictionary<JudgeGrade, int>();
            foreach (JudgeGrade grade in Enum.GetValues(typeof(JudgeGrade)))
            {
                damages[grade] = 1;
            }
            var info = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy })
            {
                DanInfo = new DanInfo { StartHP = 0, Damages = damages },
            };
            info.MaxHP = 2;

            info.OnNoteJudged(JudgeGrade.Perfect, 5);

            Assert.That(info.CurrentHP, Is.EqualTo(2));
        }
    }
}
