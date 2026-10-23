using System;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the JSON contracts of the MajNet data formats that carry courses, interactions and online scores.</summary>
    public class MajNetDataFormatTests
    {
        /// <summary>Checks that an online course description reads the camel case JSON returned by the server.</summary>
        [Test]
        public void OnlineDanInfo_DeserializesCamelCaseJson()
        {
            const string json = "{\"id\":\"dan-1\",\"name\":\"Expert\",\"createdBy\":\"author\",\"description\":\"Four songs\","
                + "\"songHashs\":[\"aaa\",\"bbb\"],\"isPlayList\":true,\"isForceGameover\":false}";

            var info = Serializer.Json.Deserialize<MajNetOnlineDanInfo>(json);

            Assert.That(info.Id, Is.EqualTo("dan-1"));
            Assert.That(info.Name, Is.EqualTo("Expert"));
            Assert.That(info.CreatedBy, Is.EqualTo("author"));
            Assert.That(info.Description, Is.EqualTo("Four songs"));
            Assert.That(info.SongHashs, Is.EqualTo(new[] { "aaa", "bbb" }));
            Assert.That(info.IsPlayList, Is.True);
            Assert.That(info.IsForceGameover, Is.False);
        }

        /// <summary>Checks that the optional course flags default to false when the server omits them.</summary>
        [Test]
        public void OnlineDanInfo_OmittedFlags_DefaultToFalse()
        {
            const string json = "{\"id\":\"d\",\"name\":\"n\",\"createdBy\":\"c\",\"description\":\"\",\"songHashs\":[]}";

            var info = Serializer.Json.Deserialize<MajNetOnlineDanInfo>(json);

            Assert.That(info.SongHashs, Is.Empty);
            Assert.That(info.IsPlayList, Is.False);
            Assert.That(info.IsForceGameover, Is.False);
        }

        /// <summary>Checks that a song interaction summary reads its like list and nested comment threads.</summary>
        [Test]
        public void SongInteract_DeserializesNestedComments()
        {
            const string json = "{\"isLiked\":true,\"plays\":12,\"likes\":[\"u1\",\"u2\"],\"disLikeCount\":1,\"comments\":["
                + "{\"sender\":\"alice\",\"content\":\"nice\",\"timestamp\":\"2024-01-02T03:04:05Z\",\"replies\":["
                + "{\"sender\":\"bob\",\"content\":\"agreed\",\"timestamp\":\"2024-01-02T03:05:00Z\",\"replies\":[]}]}]}";

            var interact = Serializer.Json.Deserialize<MajNetSongInteract>(json);

            Assert.That(interact.IsLiked, Is.True);
            Assert.That(interact.Plays, Is.EqualTo(12));
            Assert.That(interact.Likes, Is.EqualTo(new[] { "u1", "u2" }));
            Assert.That(interact.DisLikeCount, Is.EqualTo(1));
            Assert.That(interact.Comments, Has.Length.EqualTo(1));
            var comment = interact.Comments[0];
            Assert.That(comment.Sender, Is.EqualTo("alice"));
            Assert.That(comment.Content, Is.EqualTo("nice"));
            Assert.That(comment.Timestamp.ToUniversalTime(), Is.EqualTo(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            Assert.That(comment.Replies, Has.Length.EqualTo(1));
            Assert.That(comment.Replies[0].Sender, Is.EqualTo("bob"));
            Assert.That(comment.Replies[0].Content, Is.EqualTo("agreed"));
            Assert.That(comment.Replies[0].Replies, Is.Empty);
        }

        /// <summary>Checks that a song interaction summary serializes with camel case names and reads back unchanged.</summary>
        [Test]
        public void SongInteract_RoundTripsThroughJson()
        {
            var timestamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            var original = new MajNetSongInteract
            {
                IsLiked = false,
                Plays = 3,
                Likes = new[] { "x" },
                DisLikeCount = 2,
                Comments = new[]
                {
                    new ChartCommentSummary { Sender = "s", Content = "c", Timestamp = timestamp, Replies = Array.Empty<ChartCommentSummary>() },
                },
            };

            var json = Serializer.Json.Serialize(original);
            var copy = Serializer.Json.Deserialize<MajNetSongInteract>(json);

            Assert.That(json, Does.Contain("\"isLiked\":false"));
            Assert.That(json, Does.Contain("\"disLikeCount\":2"));
            Assert.That(json, Does.Not.Contain("\"IsLiked\""));
            Assert.That(copy.Plays, Is.EqualTo(3));
            Assert.That(copy.Likes, Is.EqualTo(new[] { "x" }));
            Assert.That(copy.DisLikeCount, Is.EqualTo(2));
            Assert.That(copy.Comments[0].Sender, Is.EqualTo("s"));
            Assert.That(copy.Comments[0].Timestamp.ToUniversalTime(), Is.EqualTo(timestamp));
            Assert.That(copy.Comments[0].Replies, Is.Empty);
        }

        /// <summary>Checks that an online score sheet reads its per-difficulty score lists, player profiles and combo states.</summary>
        [Test]
        public void SongScoreInfo_DeserializesJaggedScores()
        {
            const string json = "{\"hash\":\"h1\",\"scores\":[[{\"player\":{\"id\":\"p\",\"username\":\"player\",\"nickname\":\"nick\"},"
                + "\"acc\":100.5,\"comboState\":\"AP\",\"timestamp\":\"2024-03-04T05:06:07Z\"}],[],"
                + "[{\"player\":{\"username\":\"other\"},\"acc\":97.25,\"comboState\":1,\"timestamp\":\"2024-03-04T05:06:08Z\"}]]}";

            var info = Serializer.Json.Deserialize<MajNetSongScoreInfo>(json);

            Assert.That(info.Hash, Is.EqualTo("h1"));
            Assert.That(info.Scores, Has.Length.EqualTo(3));
            Assert.That(info.Scores[0], Has.Length.EqualTo(1));
            Assert.That(info.Scores[1], Is.Empty);
            var best = info.Scores[0][0];
            Assert.That(best.Player.Username, Is.EqualTo("player"));
            Assert.That(best.Player.DisplayName, Is.EqualTo("nick"));
            Assert.That(best.Acc, Is.EqualTo(100.5f));
            Assert.That(best.ComboState, Is.EqualTo(ComboState.AP));
            Assert.That(best.Timestamp.ToUniversalTime(), Is.EqualTo(new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc)));
            var other = info.Scores[2][0];
            Assert.That(other.Player.DisplayName, Is.EqualTo("other"));
            Assert.That(other.Acc, Is.EqualTo(97.25f));
            Assert.That(other.ComboState, Is.EqualTo(ComboState.FC));
        }

        /// <summary>Checks that an empty score sheet leaves the hash unset and the score lists null.</summary>
        [Test]
        public void SongScoreInfo_EmptyObject_HasNoScores()
        {
            var info = Serializer.Json.Deserialize<MajNetSongScoreInfo>("{}");

            Assert.That(info.Hash, Is.Null);
            Assert.That(info.Scores, Is.Null);
        }
    }
}
