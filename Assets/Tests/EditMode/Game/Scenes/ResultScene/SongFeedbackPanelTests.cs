using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
using MajdataPlay.Scenes.Result.Components;
using MajdataPlay.Tests.Game.Misc.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.ResultScene
{
    /// <summary>
    /// Covers how <see cref="SongFeedbackPanel"/> loads the vote of the user and sends votes and comments through the
    /// vote and comment APIs. Every request is answered by a stub HTTP handler, so no network access happens.
    /// </summary>
    public class SongFeedbackPanelTests
    {
        /// <summary>The stub HTTP clients.</summary>
        private StubHttpScope _http = null!;

        /// <summary>The temporary storage for online song caches.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The game objects of the panel.</summary>
        private TestObjects _objects = null!;

        /// <summary>The online song the feedback is about.</summary>
        private OnlineSongDetail _song = null!;

        /// <summary>The panel under test, ready to send feedback for <see cref="_song"/>.</summary>
        private SongFeedbackPanel _panel = null!;

        /// <summary>Installs the stub clients and creates the song and the panel.</summary>
        [SetUp]
        public void SetUp()
        {
            _http = new StubHttpScope();
            _temporary = new TemporaryDirectory();
            _objects = new TestObjects();
            _song = CreateSong();
            _panel = CreatePanel();
        }

        /// <summary>Destroys the panel and the song and restores the HTTP clients.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _song.Dispose();
            _http.Dispose();
            _temporary.Dispose();
        }

        /// <summary>
        /// Checks that the panel loads the stored vote from the counts API, then sends a changed vote with PUT and a trimmed
        /// comment with POST, and has nothing left to send afterwards.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SubmitAsync_SendsChangedVoteAndComment()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = request =>
                {
                    if (request.Method == HttpMethod.Get)
                    {
                        return StubHttpScope.Respond(HttpStatusCode.OK, "{\"score\":-1,\"downvotes\":1,\"myVote\":-1}");
                    }
                    return request.Method == HttpMethod.Put
                        ? StubHttpScope.Respond(HttpStatusCode.OK, "{\"score\":1,\"upvotes\":1,\"downvotes\":0,\"myVote\":1}")
                        : StubHttpScope.Respond(HttpStatusCode.Created, "{\"id\":\"c\",\"author\":\"me\",\"body\":\"Nice chart\",\"replies\":[]}");
                };

                Assert.That(await _panel.SubmitAsync(), Is.False, "The first submit loads the stored vote.");
                Assert.That(PrivateMember.GetField<GameObject>(_panel, "_dislikeOutline").activeSelf, Is.True);
                Assert.That(_http.Requests.Single().Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{_song.Id}/interactsum"));

                _panel.SelectVote(1);
                Input().text = "  Nice chart  ";
                Assert.That(await _panel.SubmitAsync(), Is.True);

                var requests = _http.Requests;
                Assert.That(requests.Length, Is.EqualTo(3));
                Assert.That(requests[1].Method, Is.EqualTo(HttpMethod.Put));
                Assert.That(requests[1].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{_song.Id}/vote"));
                Assert.That(JObject.Parse(requests[1].Body)["value"]!.Value<int>(), Is.EqualTo(1));
                Assert.That(requests[2].Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(requests[2].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{_song.Id}/comments"));
                Assert.That(JObject.Parse(requests[2].Body)["body"]!.Value<string>(), Is.EqualTo("Nice chart"));
                Assert.That(Input().text, Is.Empty);

                Assert.That(await _panel.SubmitAsync(), Is.True);
                Assert.That(_http.Requests.Length, Is.EqualTo(3), "An unchanged vote without a comment sends nothing.");
            });
        }

        /// <summary>
        /// Checks that a vote whose response was lost stays unconfirmed, so going back to the stored vote still sends it,
        /// because the server may already hold the lost vote.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SubmitAsync_ResendsVoteAfterLostResponse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var loseVotes = true;
                _http.Responder = request =>
                {
                    if (request.Method == HttpMethod.Get)
                    {
                        return StubHttpScope.Respond(HttpStatusCode.OK, "{\"myVote\":0}");
                    }
                    if (loseVotes)
                    {
                        throw new HttpRequestException("lost vote response");
                    }
                    return StubHttpScope.Respond(HttpStatusCode.OK, "{\"score\":0,\"upvotes\":0,\"downvotes\":0,\"myVote\":0}");
                };
                Assert.That(await _panel.SubmitAsync(), Is.False);

                _panel.SelectVote(1);
                Assert.That(await _panel.SubmitAsync(), Is.False, "A lost vote response fails the submit.");
                loseVotes = false;
                _panel.SelectVote(1);
                Assert.That(await _panel.SubmitAsync(), Is.True);

                var votes = _http.Requests.Where(request => request.Method == HttpMethod.Put).ToArray();
                Assert.That(votes.Length, Is.EqualTo(2));
                Assert.That(JObject.Parse(votes[0].Body)["value"]!.Value<int>(), Is.EqualTo(1));
                Assert.That(JObject.Parse(votes[1].Body)["value"]!.Value<int>(), Is.EqualTo(0));

                Assert.That(await _panel.SubmitAsync(), Is.True);
                Assert.That(_http.Requests.Count(request => request.Method == HttpMethod.Put), Is.EqualTo(2), "A confirmed vote is not sent again.");
            });
        }

        /// <summary>Checks that a refused comment keeps its text so the player can send it again.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SubmitAsync_KeepsCommentWhenRefused()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = request => request.Method == HttpMethod.Get
                    ? StubHttpScope.Respond(HttpStatusCode.OK, "{\"myVote\":0}")
                    : StubHttpScope.Respond(HttpStatusCode.TooManyRequests, "{\"code\":16,\"message\":\"Too many comments, try again later\"}");
                Assert.That(await _panel.SubmitAsync(), Is.False);

                Input().text = "Another comment";
                Assert.That(await _panel.SubmitAsync(), Is.False);

                Assert.That(Input().text, Is.EqualTo("Another comment"));
                Assert.That(_http.Requests.Count(request => request.Method == HttpMethod.Post), Is.EqualTo(1));
                Assert.That(_http.Requests.Any(request => request.Method == HttpMethod.Put), Is.False);
            });
        }

        /// <summary>Gets the comment input of the panel.</summary>
        /// <returns>The input field.</returns>
        private TMP_InputField Input()
        {
            return PrivateMember.GetField<TMP_InputField>(_panel, "_commentInput");
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

        /// <summary>Creates the panel with every serialized view it touches, allowed to send feedback for <see cref="_song"/>.</summary>
        /// <returns>The panel.</returns>
        private SongFeedbackPanel CreatePanel()
        {
            var feedback = _objects.Add<SongFeedbackPanel>("Feedback");
            var panel = CreateRect("Panel", feedback.transform);
            var songResult = CreateRect("SongResult", feedback.transform);
            var scoreIndicators = CreateRect("ScoreIndicators", feedback.transform);
            var entry = CreateRect("Entry", feedback.transform);
            PrivateMember.SetField(feedback, "_panel", panel);
            PrivateMember.SetField(feedback, "_songResult", songResult);
            PrivateMember.SetField(feedback, "_scoreIndicators", scoreIndicators);
            PrivateMember.SetField(feedback, "_feedbackEntry", entry);
            PrivateMember.SetField(feedback, "_feedbackEntryLabel", _objects.Add<TextMeshProUGUI>("EntryLabel", entry.transform));
            PrivateMember.SetField(feedback, "_scoreCanvasGroup", songResult.AddComponent<CanvasGroup>());
            PrivateMember.SetField(feedback, "_scoreIndicatorsCanvasGroup", scoreIndicators.AddComponent<CanvasGroup>());
            PrivateMember.SetField(feedback, "_feedbackCanvasGroup", panel.AddComponent<CanvasGroup>());
            PrivateMember.SetField(feedback, "_likeButton", _objects.Add<Button>("Like", panel.transform));
            PrivateMember.SetField(feedback, "_dislikeButton", _objects.Add<Button>("Dislike", panel.transform));
            PrivateMember.SetField(feedback, "_likeOutline", CreateRect("LikeOutline", panel.transform));
            PrivateMember.SetField(feedback, "_dislikeOutline", CreateRect("DislikeOutline", panel.transform));
            PrivateMember.SetField(feedback, "_commentInput", CreateInputField(panel.transform));
            PrivateMember.SetField(feedback, "_clearButton", _objects.Add<Button>("Clear", panel.transform));
            PrivateMember.SetField(feedback, "_status", _objects.Add<TextMeshProUGUI>("Status", panel.transform));
            PrivateMember.SetField(feedback, "_backIcon", CreateRect("BackIcon", feedback.transform));
            PrivateMember.SetField(feedback, "_nextIcon", CreateRect("NextIcon", feedback.transform));
            PrivateMember.SetField(feedback, "_backLabel", _objects.Add<TextMeshProUGUI>("BackLabel", feedback.transform));
            PrivateMember.SetField(feedback, "_nextLabel", _objects.Add<TextMeshProUGUI>("NextLabel", feedback.transform));
            PrivateMember.SetField(feedback, "_song", _song);
            PrivateMember.SetField(feedback, "_canSend", true);
            return feedback;
        }

        /// <summary>Creates a game object with a rect transform.</summary>
        /// <param name="name">The game object name.</param>
        /// <param name="parent">The parent.</param>
        /// <returns>The game object.</returns>
        private GameObject CreateRect(string name, Transform parent)
        {
            var gameObject = _objects.Create(name, parent);
            gameObject.AddComponent<RectTransform>();
            return gameObject;
        }

        /// <summary>Creates a TextMeshPro input field with a viewport and a text component.</summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The input field.</returns>
        private TMP_InputField CreateInputField(Transform parent)
        {
            var root = CreateRect("Comment", parent);
            root.SetActive(false);
            var viewport = CreateRect("Text Area", root.transform);
            viewport.AddComponent<RectMask2D>();
            var text = _objects.Add<TextMeshProUGUI>("Text", viewport.transform);
            var field = root.AddComponent<TMP_InputField>();
            field.textViewport = (RectTransform)viewport.transform;
            field.textComponent = text;
            root.SetActive(true);
            return field;
        }
    }
}
