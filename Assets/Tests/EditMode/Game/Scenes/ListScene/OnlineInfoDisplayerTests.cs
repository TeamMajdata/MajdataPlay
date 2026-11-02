using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
using MajdataPlay.Scenes.List;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.ListScene
{
    /// <summary>
    /// Covers how <see cref="OnlineInfoDisplayer"/> shows the counts and comments of an online chart from the counts and
    /// comment APIs. Every request is answered by a stub HTTP handler, so no network access happens.
    /// </summary>
    public class OnlineInfoDisplayerTests
    {
        /// <summary>The color of the thumb for a chart the user voted up.</summary>
        private static readonly Color s_votedColor = Color.green;

        /// <summary>The color of the thumb for a chart with a score above 5.</summary>
        private static readonly Color s_popularColor = Color.yellow;

        /// <summary>The color of the thumb otherwise.</summary>
        private static readonly Color s_normalColor = Color.white;

        /// <summary>The longest wait for the displayer to show a response.</summary>
        private static readonly TimeSpan s_responseTimeout = TimeSpan.FromSeconds(10);

        /// <summary>The stub HTTP clients.</summary>
        private StubHttpScope _http = null!;

        /// <summary>The temporary storage for online song caches.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The game objects of the displayer.</summary>
        private TestObjects _objects = null!;

        /// <summary>The online song shown by the displayer.</summary>
        private OnlineSongDetail _song = null!;

        /// <summary>The displayer under test.</summary>
        private OnlineInfoDisplayer _displayer = null!;

        /// <summary>Cancels the requests and the comment rotation of the displayer.</summary>
        private CancellationTokenSource _rotation = null!;

        /// <summary>Installs the stub clients and creates the song and the displayer.</summary>
        [SetUp]
        public void SetUp()
        {
            _http = new StubHttpScope();
            _temporary = new TemporaryDirectory();
            _objects = new TestObjects();
            _rotation = new CancellationTokenSource();
            _song = CreateSong();
            _displayer = CreateDisplayer();
        }

        /// <summary>Stops the rotation, destroys the displayer and the song and restores the HTTP clients.</summary>
        [TearDown]
        public void TearDown()
        {
            _rotation.Cancel();
            _rotation.Dispose();
            _objects.Dispose();
            _song.Dispose();
            _http.Dispose();
            _temporary.Dispose();
        }

        /// <summary>
        /// Checks that the counts come from the counts API and that the rotation shows live comments of the best page,
        /// skipping the placeholders of deleted comments.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetSongDetail_ShowsCountsAndLiveComments()
        {
            return UniTask.ToCoroutine(async () =>
            {
                const string page = "{\"items\":["
                    + "{\"id\":\"a\",\"author\":\"\",\"body\":\"\",\"deleted\":true,\"replyCount\":1,\"replies\":[]},"
                    + "{\"id\":\"b\",\"author\":\"alice\",\"body\":\"nice\",\"replies\":[]}],"
                    + "\"total\":2,\"commentCount\":2,\"page\":1,\"pageSize\":20,\"sort\":\"best\"}";
                _http.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("interactsum")
                    ? StubHttpScope.Respond(HttpStatusCode.OK, "{\"plays\":7,\"score\":6,\"upvotes\":6,\"downvotes\":0,\"comments\":2,\"myVote\":0}")
                    : StubHttpScope.Respond(HttpStatusCode.OK, page);

                _displayer.SetSongDetail(_song, 0, _rotation.Token);
                await UniTask.WaitUntil(() => Text("_commentTextDisplayer").Contains("nice")).Timeout(s_responseTimeout);

                Assert.That(Text("_likeCountDisplayer"), Is.EqualTo("6"));
                Assert.That(Text("_playCountDisplayer"), Is.EqualTo("7"));
                Assert.That(Text("_commentCountDisplayer"), Is.EqualTo("2"));
                Assert.That(PrivateMember.GetField<Image>(_displayer, "_thumbUpImage").color, Is.EqualTo(s_popularColor));
                Assert.That(PrivateMember.GetField<GameObject>(_displayer, "_commentBox").activeSelf, Is.True);
                Assert.That(Text("_commentTextDisplayer"), Does.StartWith("alice"));
                var requests = _http.Requests;
                Assert.That(requests[0].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{_song.Id}/interactsum"));
                Assert.That(requests[1].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{_song.Id}/comments"));
                Assert.That(requests[1].Uri.Query, Is.EqualTo("?sort=best&page=1"));
            });
        }

        /// <summary>
        /// Checks that a chart without comments shows the vote of the user, hides the comment box left by the previous
        /// song and requests no comments.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetSongDetail_WithoutComments_RequestsNoComments()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{\"plays\":1,\"score\":1,\"upvotes\":1,\"downvotes\":0,\"comments\":0,\"myVote\":1}");
                var commentBox = PrivateMember.GetField<GameObject>(_displayer, "_commentBox");
                commentBox.SetActive(true);

                _displayer.SetSongDetail(_song, 0, _rotation.Token);
                Assert.That(commentBox.activeSelf, Is.False);
                await UniTask.WaitUntil(() => Text("_likeCountDisplayer") == "1").Timeout(s_responseTimeout);
                await UniTask.Yield();

                Assert.That(Text("_commentCountDisplayer"), Is.EqualTo("0"));
                Assert.That(PrivateMember.GetField<Image>(_displayer, "_thumbUpImage").color, Is.EqualTo(s_votedColor));
                Assert.That(commentBox.activeSelf, Is.False);
                Assert.That(_http.Requests.Select(request => request.Uri.AbsolutePath),
                    Is.EqualTo(new[] { $"/api/maichart/{_song.Id}/interactsum" }));
            });
        }

        /// <summary>Gets the text of a text field of the displayer.</summary>
        /// <param name="field">The name of the field.</param>
        /// <returns>The text; empty while nothing was assigned, since a new text component holds null.</returns>
        private string Text(string field)
        {
            return PrivateMember.GetField<TextMeshProUGUI>(_displayer, field).text ?? string.Empty;
        }

        /// <summary>Creates an online song served by a test endpoint.</summary>
        /// <returns>The online song.</returns>
        private OnlineSongDetail CreateSong()
        {
            var endpoint = new ApiEndpoint
            {
                Name = "Server",
                Url = new Uri("https://api.invalid/api/"),
                Username = "user",
                Password = "pass"
            };
            var detail = new MajnetSongDetail
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Song",
                Hash = "aGFzaA==",
                Levels = new[] { "1" },
            };
            var cache = new OnlineChartCache(
                new ObjectStore(_temporary.Directory.CreateDirectory("Objects"), _temporary.Directory.CreateDirectory("Partials")),
                false);
            return new OnlineSongDetail(endpoint, detail, cache.CreateTransientRecord(endpoint, detail));
        }

        /// <summary>Creates the displayer with its text fields, comment box, thumb image and thumb colors.</summary>
        /// <returns>The displayer, initialized as Awake would.</returns>
        private OnlineInfoDisplayer CreateDisplayer()
        {
            var displayer = _objects.Add<OnlineInfoDisplayer>("OnlineInfo");
            foreach (var field in new[] { "_likeCountDisplayer", "_playCountDisplayer", "_commentCountDisplayer", "_commentTextDisplayer" })
            {
                PrivateMember.SetField(displayer, field, _objects.Add<TextMeshProUGUI>(field, displayer.transform));
            }
            var commentBox = _objects.Create("CommentBox", displayer.transform);
            commentBox.AddComponent<RectTransform>();
            PrivateMember.SetField(displayer, "_commentBox", commentBox);
            PrivateMember.SetField(displayer, "_thumbUpImage", _objects.Add<Image>("ThumbUp", displayer.transform));
            PrivateMember.SetField(displayer, "_thumbUpGreenColor", s_votedColor);
            PrivateMember.SetField(displayer, "_thumbUpGoldColor", s_popularColor);
            PrivateMember.SetField(displayer, "_thumbUpNormalColor", s_normalColor);
            Assert.That(UnityMessages.Send(displayer, "Awake"), Is.True);
            return displayer;
        }
    }
}
