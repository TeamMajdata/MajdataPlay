using System.Collections.Generic;
using MajdataPlay.Collections;
using MajdataPlay.Json;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Json
{
    /// <summary>Covers the JSON converters <see cref="JudgeInfoConverter"/> and <see cref="JudgeDetailConverter"/>.</summary>
    public class JudgeConverterTests
    {
        /// <summary>Creates a judge record with a few non-zero counts.</summary>
        /// <returns>The judge record.</returns>
        private static JudgeInfo CreateInfo()
        {
            var dictionary = new Dictionary<JudgeGrade, int>(JudgeInfo.Empty.Count);
            foreach (var pair in JudgeInfo.Empty)
            {
                dictionary[pair.Key] = 0;
            }
            dictionary[JudgeGrade.Perfect] = 9;
            dictionary[JudgeGrade.FastGreat2nd] = 2;
            dictionary[JudgeGrade.Miss] = 1;
            return new JudgeInfo(dictionary);
        }

        /// <summary>Checks that a judge record is written as an object keyed by grade names and read back unchanged.</summary>
        [Test]
        public void JudgeInfo_RoundTrips()
        {
            var info = CreateInfo();

            var json = JsonConvert.SerializeObject(info);
            var parsed = JObject.Parse(json);
            var restored = JsonConvert.DeserializeObject<JudgeInfo>(json)!;

            Assert.That(parsed["Perfect"]!.Value<int>(), Is.EqualTo(9));
            Assert.That(parsed["FastGreat2nd"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(restored[JudgeGrade.Perfect], Is.EqualTo(9));
            Assert.That(restored[JudgeGrade.FastGreat2nd], Is.EqualTo(2));
            Assert.That(restored[JudgeGrade.Miss], Is.EqualTo(1));
            Assert.That(restored.Count, Is.EqualTo(15));
        }

        /// <summary>Checks that missing grades default to zero and legacy grade names are mapped to the current grades.</summary>
        [Test]
        public void JudgeInfo_ReadsLegacyNames()
        {
            const string json = "{\"Perfect\":4,\"FastGreat1\":1,\"FastGreat2\":2,\"LateGreat1\":3,\"LateGreat2\":4,"
                + "\"FastPerfect1\":5,\"FastPerfect2\":6,\"LatePerfect1\":7,\"LatePerfect2\":8,\"Unknown\":9}";

            var info = JsonConvert.DeserializeObject<JudgeInfo>(json)!;

            Assert.That(info[JudgeGrade.Perfect], Is.EqualTo(4));
            Assert.That(info[JudgeGrade.FastGreat2nd], Is.EqualTo(1));
            Assert.That(info[JudgeGrade.FastGreat3rd], Is.EqualTo(2));
            Assert.That(info[JudgeGrade.LateGreat2nd], Is.EqualTo(3));
            Assert.That(info[JudgeGrade.LateGreat3rd], Is.EqualTo(4));
            Assert.That(info[JudgeGrade.FastPerfect2nd], Is.EqualTo(5));
            Assert.That(info[JudgeGrade.FastPerfect3rd], Is.EqualTo(6));
            Assert.That(info[JudgeGrade.LatePerfect2nd], Is.EqualTo(7));
            Assert.That(info[JudgeGrade.LatePerfect3rd], Is.EqualTo(8));
            Assert.That(info[JudgeGrade.TooFast], Is.EqualTo(0));
        }

        /// <summary>Checks that a judge detail is written keyed by note type names and read back with its totals.</summary>
        [Test]
        public void JudgeDetail_RoundTrips()
        {
            var detail = new JudgeDetail(new Dictionary<ScoreNoteType, JudgeInfo>
            {
                { ScoreNoteType.Tap, CreateInfo() },
                { ScoreNoteType.Break, CreateInfo() },
            });

            var json = JsonConvert.SerializeObject(detail);
            var parsed = JObject.Parse(json);
            var restored = JsonConvert.DeserializeObject<JudgeDetail>(json)!;

            Assert.That(parsed.ContainsKey("Tap"), Is.True);
            Assert.That(parsed.ContainsKey("Break"), Is.True);
            Assert.That(parsed["Tap"]!["Perfect"]!.Value<int>(), Is.EqualTo(9));
            Assert.That(restored.Count, Is.EqualTo(2));
            Assert.That(restored[ScoreNoteType.Break][JudgeGrade.Miss], Is.EqualTo(1));
            Assert.That(restored.TotalJudgeInfo[JudgeGrade.Perfect], Is.EqualTo(18));
        }

        /// <summary>Checks that the converters can be used explicitly through serializer settings.</summary>
        [Test]
        public void Converters_WorkThroughSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new JudgeDetailConverter(), new JudgeInfoConverter() }
            };

            var json = JsonConvert.SerializeObject(JudgeDetail.Empty, settings);
            var restored = JsonConvert.DeserializeObject<JudgeDetail>(json, settings)!;

            Assert.That(restored.Count, Is.EqualTo(5));
            Assert.That(restored[ScoreNoteType.Slide][JudgeGrade.Perfect], Is.EqualTo(0));
        }
    }
}
