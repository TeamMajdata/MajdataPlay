using System;
using System.Linq;
using MajdataPlay.Net.Models;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Net.Models
{
    /// <summary>Covers the JSON contracts of the online models: profiles, machines, chart metadata, collections, interactions and online scores.</summary>
    public class NetModelTests
    {
        /// <summary>Checks that the display name falls back to the user name when no nickname is set.</summary>
        [Test]
        public void UserProfileDisplayName_FallsBackToUsername()
        {
            var withoutNickname = new UserProfile { Username = "user", Nickname = string.Empty };
            var withNickname = new UserProfile { Username = "user", Nickname = "nick" };
            var nullNickname = new UserProfile { Username = "user" };

            Assert.That(withoutNickname.DisplayName, Is.EqualTo("user"));
            Assert.That(withNickname.DisplayName, Is.EqualTo("nick"));
            Assert.That(nullNickname.DisplayName, Is.EqualTo("user"));
        }

        /// <summary>Checks that the user profile reads the camel case JSON returned by the server.</summary>
        [Test]
        public void UserProfile_DeserializesCamelCaseJson()
        {
            var profile = Serializer.Json.Deserialize<UserProfile>("{\"id\":\"1\",\"username\":\"u\",\"nickname\":\"n\",\"avatarId\":\"a\"}");

            Assert.That(profile.Id, Is.EqualTo("1"));
            Assert.That(profile.Username, Is.EqualTo("u"));
            Assert.That(profile.Nickname, Is.EqualTo("n"));
            Assert.That(profile.AvatarId, Is.EqualTo("a"));
        }

        /// <summary>Checks the default machine description.</summary>
        [Test]
        public void MachineInfo_HasDefaultName()
        {
            var info = new MachineInfo();

            Assert.That(info.Name, Is.EqualTo("Private arcade"));
            Assert.That(info.Description, Is.Null);
        }

        /// <summary>Checks that the online song detail metadata defaults its tag lists to empty arrays.</summary>
        [Test]
        public void MajnetSongDetail_DefaultsTagsToEmpty()
        {
            var detail = Serializer.Json.Deserialize<MajnetSongDetail>("{\"id\":\"x\",\"title\":\"t\",\"hash\":\"h\"}");

            Assert.That(detail, Is.Not.Null);
            Assert.That(detail!.Id, Is.EqualTo("x"));
            Assert.That(detail.Title, Is.EqualTo("t"));
            Assert.That(detail.Tags, Is.Empty);
            Assert.That(detail.PublicTags, Is.Empty);
        }

        /// <summary>Checks that the fields the server leaves out keep empty values instead of null.</summary>
        [Test]
        public void MajnetSongDetail_OmittedFields_AreEmpty()
        {
            var detail = Serializer.Json.Deserialize<MajnetSongDetail>("{}");

            Assert.That(detail, Is.Not.Null);
            Assert.That(detail!.Id, Is.Empty);
            Assert.That(detail.Title, Is.Empty);
            Assert.That(detail.Artist, Is.Empty);
            Assert.That(detail.Designer, Is.Empty);
            Assert.That(detail.Description, Is.Empty);
            Assert.That(detail.Uploader, Is.Empty);
            Assert.That(detail.Hash, Is.Empty);
            Assert.That(detail.Levels, Is.Empty);
        }

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

        /// <summary>Checks that the chart counts read the camel case JSON of <c>GET maichart/{chartId}/interactsum</c>.</summary>
        [Test]
        public void ChartInteractSummary_DeserializesCamelCaseJson()
        {
            const string json = "{\"chartId\":\"6f9619ff-8b86-d011-b42d-00c04fc964ff\",\"plays\":12,\"score\":3,\"upvotes\":5,"
                + "\"downvotes\":2,\"comments\":4,\"myVote\":-1}";

            var summary = Serializer.Json.Deserialize<MajNetChartInteractSummary>(json);

            Assert.That(summary.ChartId, Is.EqualTo("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
            Assert.That(summary.Plays, Is.EqualTo(12));
            Assert.That(summary.Score, Is.EqualTo(3));
            Assert.That(summary.Upvotes, Is.EqualTo(5));
            Assert.That(summary.Downvotes, Is.EqualTo(2));
            Assert.That(summary.Comments, Is.EqualTo(4));
            Assert.That(summary.MyVote, Is.EqualTo(-1));
        }

        /// <summary>Checks that a comment vote result, which carries the score alone, leaves the vote counts null.</summary>
        [Test]
        public void VoteResult_CommentVote_HasNoCounts()
        {
            var chart = Serializer.Json.Deserialize<MajNetVoteResult>("{\"score\":1,\"upvotes\":2,\"downvotes\":1,\"myVote\":1}");
            var comment = Serializer.Json.Deserialize<MajNetVoteResult>("{\"score\":-2,\"myVote\":0}");

            Assert.That(chart.Upvotes, Is.EqualTo(2));
            Assert.That(chart.Downvotes, Is.EqualTo(1));
            Assert.That(comment.Score, Is.EqualTo(-2));
            Assert.That(comment.Upvotes, Is.Null);
            Assert.That(comment.Downvotes, Is.Null);
        }

        /// <summary>
        /// Checks that a comment page reads its order by camel case name, nested replies, placeholders and the optional
        /// identifiers and edit moment.
        /// </summary>
        [Test]
        public void CommentPage_DeserializesThreads()
        {
            const string json = "{\"items\":[{\"id\":\"a\",\"chartId\":\"c\",\"chartHash\":\"h\",\"parentId\":null,\"depth\":0,"
                + "\"authorId\":\"u\",\"author\":\"alice\",\"body\":\"nice\",\"createdAt\":\"2024-01-02T03:04:05Z\","
                + "\"editedAt\":\"2024-01-02T04:00:00Z\",\"deleted\":false,\"removed\":false,\"score\":3,\"myVote\":1,\"replyCount\":2,"
                + "\"replies\":[{\"id\":\"b\",\"parentId\":\"a\",\"depth\":1,\"authorId\":null,\"author\":\"\",\"body\":\"\","
                + "\"createdAt\":\"2024-01-02T03:05:00Z\",\"editedAt\":null,\"deleted\":false,\"removed\":true,\"replies\":[]}]}],"
                + "\"total\":1,\"commentCount\":2,\"page\":1,\"pageSize\":20,\"sort\":\"controversial\"}";

            var page = Serializer.Json.Deserialize<MajNetCommentPage>(json);

            Assert.That(page.Sort, Is.EqualTo(CommentSort.Controversial));
            Assert.That(page.CommentCount, Is.EqualTo(2));
            Assert.That(page.PageSize, Is.EqualTo(20));
            var comment = page.Items.Single();
            Assert.That(comment.Author, Is.EqualTo("alice"));
            Assert.That(comment.Body, Is.EqualTo("nice"));
            Assert.That(comment.ParentId, Is.Null);
            Assert.That(comment.CreatedAt.ToUniversalTime(), Is.EqualTo(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            Assert.That(comment.EditedAt, Is.Not.Null);
            Assert.That(comment.IsPlaceholder, Is.False);
            Assert.That(comment.MyVote, Is.EqualTo(1));
            var removed = comment.Replies.Single();
            Assert.That(removed.ParentId, Is.EqualTo("a"));
            Assert.That(removed.AuthorId, Is.Null);
            Assert.That(removed.EditedAt, Is.Null);
            Assert.That(removed.IsPlaceholder, Is.True);
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
