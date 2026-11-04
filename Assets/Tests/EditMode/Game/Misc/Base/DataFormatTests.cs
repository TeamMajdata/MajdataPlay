using System;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Tests.Game.Misc.Support;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the plain data formats of the game; the models of the online service are covered by <c>NetModelTests</c>.</summary>
    public class DataFormatTests
    {
        /// <summary>Checks the default course settings and damage table.</summary>
        [Test]
        public void DanInfo_HasDefaultCourseRules()
        {
            var info = new DanInfo();

            Assert.That(info.StartHP, Is.EqualTo(50));
            Assert.That(info.RestoreHP, Is.EqualTo(10));
            Assert.That(info.IsPlayList, Is.False);
            Assert.That(info.IsForceGameover, Is.False);
            Assert.That(info.Mods, Is.Empty);
            Assert.That(info.Damages[JudgeGrade.Miss], Is.EqualTo(-5));
            Assert.That(info.Damages[JudgeGrade.LateGood], Is.EqualTo(-3));
            Assert.That(info.Damages[JudgeGrade.FastGreat], Is.EqualTo(-2));
            Assert.That(info.Damages[JudgeGrade.Perfect], Is.EqualTo(0));
            Assert.That(info.Damages.Count, Is.EqualTo(15));
        }

        /// <summary>Checks that a course definition is read from the playlist JSON format.</summary>
        [Test]
        public void DanInfo_DeserializesPlaylistJson()
        {
            var info = Serializer.Json.Deserialize<DanInfo>("{\"Name\":\"Course\",\"SongHashs\":[\"a\",\"b\"],\"IsPlayList\":true,\"StartHP\":30}");

            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Name, Is.EqualTo("Course"));
            Assert.That(info.SongHashs, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(info.IsPlayList, Is.True);
            Assert.That(info.StartHP, Is.EqualTo(30));
        }

        /// <summary>Checks that the interface reports online songs from their storage location.</summary>
        [Test]
        public void SongDetailIsOnline_FollowsLocation()
        {
            ISongDetail local = new FakeSongDetail("a", location: ChartStorageLocation.Local);
            ISongDetail online = new FakeSongDetail("b", location: ChartStorageLocation.Online);

            Assert.That(local.IsOnline, Is.False);
            Assert.That(online.IsOnline, Is.True);
        }

        /// <summary>Checks that the chart scan progress and analysis results keep their initialized values.</summary>
        [Test]
        public void ProgressAndAnalysisValues_KeepInitializedValues()
        {
            var progress = new ChartScanProgress { StorageType = ChartStorageLocation.Online, Message = "scan" };
            var analysis = new MaidataAnalyzeResult { Length = TimeSpan.FromSeconds(2), MaxBPM = 200, MinBPM = 100 };

            Assert.That(progress.StorageType, Is.EqualTo(ChartStorageLocation.Online));
            Assert.That(progress.Message, Is.EqualTo("scan"));
            Assert.That(analysis.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(analysis.MaxBPM, Is.EqualTo(200f));
            Assert.That(analysis.MinBPM, Is.EqualTo(100f));
        }
    }
}
