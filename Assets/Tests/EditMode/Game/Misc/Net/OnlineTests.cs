using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Net
{
    /// <summary>
    /// Covers request building, response handling, retries and caching of <see cref="Online"/>. Every request is answered
    /// by a stub HTTP handler, so no network access happens.
    /// </summary>
    public class OnlineTests
    {
        /// <summary>The stub HTTP clients.</summary>
        private StubHttpScope _http = null!;

        /// <summary>The endpoint used by the test.</summary>
        private ApiEndpoint _endpoint = null!;

        /// <summary>The temporary storage for online song caches.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>Installs the stub clients and a fresh endpoint.</summary>
        [SetUp]
        public void SetUp()
        {
            _http = new StubHttpScope();
            _temporary = new TemporaryDirectory();
            _endpoint = new ApiEndpoint
            {
                Name = "Server",
                Url = new Uri("https://api.invalid/api/"),
                Username = "user",
                Password = "pass"
            };
        }

        /// <summary>Restores the HTTP clients.</summary>
        [TearDown]
        public void TearDown()
        {
            _http.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Gets the private statistics object of the endpoint.</summary>
        /// <returns>The statistics.</returns>
        private object Statistics()
        {
            return PrivateMember.InvokeStatic(typeof(Online), "GetApiEndpointStatistic", _endpoint)!;
        }

        /// <summary>Sets a property of the endpoint statistics.</summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">The value.</param>
        private void SetStatistic(string name, bool? value)
        {
            PrivateMember.SetProperty(Statistics(), name, value);
        }

        /// <summary>Gets a property of the endpoint statistics.</summary>
        /// <param name="name">The property name.</param>
        /// <returns>The value.</returns>
        private bool? GetStatistic(string name)
        {
            return PrivateMember.GetProperty<bool?>(Statistics(), name);
        }

        /// <summary>Creates an online song served by the test endpoint.</summary>
        /// <returns>The online song.</returns>
        private OnlineSongDetail CreateSong()
        {
            return new OnlineSongDetail(_temporary.Directory.CreateDirectory(Guid.NewGuid().ToString("N")), _endpoint, new MajnetSongDetail
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Song",
                Artist = "Artist",
                Hash = "aGFzaA==",
                Levels = new[] { "1", "2" },
                Uploader = "uploader",
                Designer = "designer",
                Tags = new[] { "tag" },
            });
        }

        /// <summary>Checks that a missing endpoint and unset credentials are rejected without a request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoginAsync_RejectsMissingCredentials()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var failure = default(Exception);
                try
                {
                    await Online.LoginAsync(null!, "user", "pass");
                }
                catch (ArgumentNullException exception)
                {
                    failure = exception;
                }

                var placeholder = await Online.LoginAsync(_endpoint, "YourUsername", "x");
                var empty = await Online.LoginAsync(_endpoint, "user", string.Empty);

                Assert.That(failure, Is.Not.Null);
                Assert.That(placeholder.ErrorCode, Is.EqualTo(HttpErrorCode.InvalidRequest));
                Assert.That(placeholder.Message, Is.EqualTo("MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_UNSET"));
                Assert.That(empty.IsSuccessfully, Is.False);
                Assert.That(_http.Requests, Is.Empty);
            });
        }

        /// <summary>Checks that a successful login posts the hashed password and marks the user as logged in.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoginAsync_SucceedsOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{}");

                var first = await Online.LoginAsync(_endpoint, "user", "secret-password");
                var second = await Online.LoginAsync(_endpoint, "user", "secret-password");

                Assert.That(first.IsSuccessfully, Is.True);
                Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.True);
                var request = _http.Requests.Single();
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(request.Uri.AbsoluteUri, Is.EqualTo("https://api.invalid/api/account/login"));
                Assert.That(request.Body, Does.Contain("user"));
                Assert.That(request.Body, Does.Not.Contain("secret-password"));
            });
        }

        /// <summary>Checks the messages produced for each kind of rejected login.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoginAsync_MapsRejectionMessages()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var cases = new (HttpStatusCode Status, string Body, string Message)[]
                {
                    (HttpStatusCode.Unauthorized, "{}", "MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_INCORRECT"),
                    (HttpStatusCode.Forbidden, "{\"code\":11}", "MAJTEXT_ONLINE_ACCOUNT_PENDING_VERIFICATION"),
                    (HttpStatusCode.Forbidden, "{\"code\":12}", "MAJTEXT_ONLINE_ACCOUNT_BANNED"),
                    (HttpStatusCode.Forbidden, "not json", "MAJTEXT_ONLINE_ACCESS_FORBIDDEN"),
                };
                foreach (var (status, body, message) in cases)
                {
                    _http.RespondWith(status, body);

                    var response = await Online.LoginAsync(_endpoint, "user", "pass");

                    Assert.That(response.IsSuccessfully, Is.False, message);
                    Assert.That(response.StatusCode, Is.EqualTo(status), message);
                    Assert.That(response.Message, Is.EqualTo(message));
                    Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
                }
            });
        }

        /// <summary>Checks that a forbidden response meaning "already logged in" counts as success.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoginAsync_AlreadyLoggedIn()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.Forbidden, "{\"code\":0}");

                var response = await Online.LoginAsync(_endpoint, "user", "pass");

                Assert.That(response.IsSuccessfully, Is.True);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.True);
            });
        }

        /// <summary>Checks that the profile request updates the login state.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetUserProfileAsync_TracksLoginState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{\"username\":\"player\",\"nickname\":\"Nick\"}");
                var profile = await Online.GetUserProfileAsync(_endpoint);
                Assert.That(profile.IsSuccessfully, Is.True);
                Assert.That(profile.Deserialize().DisplayName, Is.EqualTo("Nick"));
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.True);

                _http.RespondWith(HttpStatusCode.Unauthorized, "{}");
                _endpoint.RuntimeConfig.Username = "player";
                var unauthorized = await Online.GetUserProfileAsync(_endpoint);
                Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
                Assert.That(_endpoint.RuntimeConfig.Username, Is.Empty);

                _http.RespondWith(HttpStatusCode.InternalServerError, "{}");
                var failed = await Online.GetUserProfileAsync(_endpoint);
                Assert.That(failed.IsSuccessfully, Is.False);
                Assert.That(_http.Requests.Length, Is.EqualTo(2 + MajEnv.HTTP_REQUEST_MAX_RETRY + 1));
            });
        }

        /// <summary>Checks the user score request, its unauthorized result and its retries.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetUserScoresAsync_HandlesResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "[{\"hash\":\"h\",\"dxScore\":12,\"chartLevel\":4,\"comboState\":2}]");
                var scores = await Online.GetUserScoresAsync(_endpoint);
                Assert.That(scores.Single().Hash, Is.EqualTo("h"));
                Assert.That(scores.Single().DXScore, Is.EqualTo(12u));
                Assert.That(scores.Single().ChartLevel, Is.EqualTo(ChartLevel.Master));

                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.GetUserScoresAsync(_endpoint), Is.Empty);

                _http.RespondWith(HttpStatusCode.BadGateway);
                var before = _http.Requests.Length;
                Assert.That(await Online.GetUserScoresAsync(_endpoint), Is.Empty);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY + 1));
            });
        }

        /// <summary>Checks the machine registration flow, including a server without registration support.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RegisterAsync_TracksSupport()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(Online.IsMachineRegistered(_endpoint), Is.False);
                _http.RespondWith(HttpStatusCode.OK, "{}");
                var registered = await Online.RegisterAsync(_endpoint, new MachineInfo { Name = "Cabinet" });
                Assert.That(registered.IsSuccessfully, Is.True);
                Assert.That(Online.IsMachineRegistered(_endpoint), Is.True);
                Assert.That(_http.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/api/machine/register"));
                Assert.That(_http.Requests.Single().Body, Does.Contain("Cabinet"));

                StubHttpScope.ClearOnlineState();
                _http.RespondWith(HttpStatusCode.NotFound);
                var unsupported = await Online.RegisterAsync(_endpoint, new MachineInfo());
                var cached = await Online.RegisterAsync(_endpoint, new MachineInfo());
                Assert.That(unsupported.ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That(cached.Message, Is.EqualTo("MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"));
                Assert.That(_http.Requests.Length, Is.EqualTo(2));
                Assert.That(Online.IsMachineRegistered(_endpoint), Is.False);
            });
        }

        /// <summary>Checks that request bodies use the camel case naming configured for the online service.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RegisterAsync_UsesCamelCaseBody()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{}");

                await Online.RegisterAsync(_endpoint, new MachineInfo { Name = "Cabinet" });

                Assert.That(JObject.Parse(_http.Requests.Single().Body).ContainsKey("name"), Is.True);
            });
        }

        /// <summary>Checks the machine check request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CheckMachineRegisterAsync_FollowsStatus()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{}");
                Assert.That(await Online.CheckMachineRegisterAsync(_endpoint), Is.True);

                _http.RespondWith(HttpStatusCode.Forbidden);
                Assert.That(await Online.CheckMachineRegisterAsync(_endpoint), Is.False);
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Is.EqualTo("/api/machine/info"));
            });
        }

        /// <summary>Checks that transport failures, timeouts and cancellations are turned into unsuccessful responses.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Requests_ReportTransportFailures()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = _ => throw new HttpRequestException("offline");
                Assert.That(await Online.CheckMachineRegisterAsync(_endpoint), Is.False);

                _http.Responder = _ => throw new System.Threading.Tasks.TaskCanceledException("timeout");
                Assert.That(await Online.CheckMachineRegisterAsync(_endpoint), Is.False);

                SetStatistic("IsMachineRegistrationSupported", true);
                _http.Responder = _ => throw new HttpRequestException("offline");
                var post = await Online.AuthRevokeAsync(_endpoint, "id");
                Assert.That(post.ErrorCode, Is.EqualTo(HttpErrorCode.Unreachable));

                _http.Responder = _ => throw new System.Threading.Tasks.TaskCanceledException("timeout");
                var timeout = await Online.AuthRevokeAsync(_endpoint, "id");
                Assert.That(timeout.ErrorCode, Is.EqualTo(HttpErrorCode.Timeout));

                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                var getCanceled = await Online.AuthCheckAsync(_endpoint, "id", canceled.Token);
                var postCanceled = await Online.AuthRevokeAsync(_endpoint, "id", canceled.Token);
                Assert.That(getCanceled.ErrorCode, Is.EqualTo(HttpErrorCode.Canceled));
                Assert.That(postCanceled.ErrorCode, Is.EqualTo(HttpErrorCode.Canceled));
            });
        }

        /// <summary>Checks the authorization requests and their registration support checks.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator AuthRequests_RequireRegistrationSupport()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var unknown = 0;
                foreach (var request in new Func<UniTask>[]
                {
                    async () => await Online.AuthRequestAsync(_endpoint),
                    async () => await Online.AuthCheckAsync(_endpoint, "id"),
                    async () => await Online.AuthRevokeAsync(_endpoint, "id"),
                })
                {
                    try
                    {
                        await request();
                    }
                    catch (InvalidOperationException)
                    {
                        unknown++;
                    }
                }
                Assert.That(unknown, Is.EqualTo(3));

                SetStatistic("IsMachineRegistrationSupported", false);
                Assert.That((await Online.AuthRequestAsync(_endpoint)).ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That((await Online.AuthCheckAsync(_endpoint, "id")).ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That((await Online.AuthRevokeAsync(_endpoint, "id")).ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That(_http.Requests, Is.Empty);

                SetStatistic("IsMachineRegistrationSupported", true);
                _http.RespondWith(HttpStatusCode.Created, "{\"authId\":\"x\"}");
                var created = await Online.AuthRequestAsync(_endpoint);
                Assert.That(created.IsSuccessfully, Is.True);
                Assert.That(created.IsDeserializable, Is.True);

                _http.RespondWith(HttpStatusCode.OK, "{}");
                var notCreated = await Online.AuthRequestAsync(_endpoint);
                Assert.That(notCreated.IsSuccessfully, Is.False);
                Assert.That(notCreated.Message, Is.EqualTo("MAJTEXT_ONLINE_MACHINE_AUTH_REQUEST_FAILED"));

                var check = await Online.AuthCheckAsync(_endpoint, "abc");
                var revoke = await Online.AuthRevokeAsync(_endpoint, "abc");
                Assert.That(check.IsSuccessfully, Is.True);
                Assert.That(revoke.IsSuccessfully, Is.True);
                var requests = _http.Requests;
                Assert.That(requests[2].Uri.Query, Is.EqualTo("?auth-id=abc"));
                Assert.That(requests[2].Method, Is.EqualTo(HttpMethod.Get));
                Assert.That(requests[3].Uri.AbsolutePath, Is.EqualTo("/api/machine/auth/revoke"));
                Assert.That(requests[3].Method, Is.EqualTo(HttpMethod.Post));
            });
        }

        /// <summary>Checks that logging out resets every known endpoint.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LogoutAllAsync_ResetsEndpoints()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{}");
                await Online.LoginAsync(_endpoint, "user", "pass");
                _endpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.None;

                await Online.LogoutAllAsync();

                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
                Assert.That(_endpoint.RuntimeConfig.Username, Is.EqualTo("???"));
                Assert.That(_endpoint.RuntimeConfig.AuthUsername, Is.EqualTo("user"));
                Assert.That(_endpoint.RuntimeConfig.AuthPassword, Is.EqualTo("pass"));
                Assert.That(_http.Requests.Last().Uri.AbsolutePath, Is.EqualTo("/api/account/logout"));
            });
        }

        /// <summary>Checks that the heartbeat revalidates registration and login.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator HeartbeatAsync_RevalidatesState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                SetStatistic("IsMachineRegistered", true);
                SetStatistic("IsUserLoggedIn", true);
                _http.Responder = request => request.RequestUri!.AbsolutePath.EndsWith("profile")
                    ? StubHttpScope.Respond(HttpStatusCode.Unauthorized, "{}")
                    : StubHttpScope.Respond(HttpStatusCode.NotFound, "{}");

                await Online.HeartbeatAsync();

                Assert.That(GetStatistic("IsMachineRegistered"), Is.False);
                Assert.That(GetStatistic("IsUserLoggedIn"), Is.False);
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
            });
        }

        /// <summary>Checks the chart list request, its invalid responses and its retries.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetChartListAsync_HandlesResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "[{\"id\":\"1\",\"title\":\"A\",\"hash\":\"h\"}]");
                var list = await Online.GetChartListAsync(_endpoint);
                Assert.That(list!.Single().Title, Is.EqualTo("A"));

                _http.RespondWith(HttpStatusCode.NotFound);
                var before = _http.Requests.Length;
                Assert.That(await Online.GetChartListAsync(_endpoint), Is.Null);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(1));

                _http.RespondWith(HttpStatusCode.OK, "{ broken");
                before = _http.Requests.Length;
                Assert.That(await Online.GetChartListAsync(_endpoint), Is.Null);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY));
            });
        }

        /// <summary>Checks the favorite collection request, which requires a logged-in user.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetUserOnlineFavCollection_RequiresLogin()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(await Online.GetUserOnlineFavCollection(_endpoint), Is.Null);
                Assert.That(_http.Requests, Is.Empty);

                SetStatistic("IsUserLoggedIn", true);
                _http.RespondWith(HttpStatusCode.OK, "[{\"id\":\"1\",\"name\":\"Fav\",\"createdBy\":\"u\",\"description\":\"d\",\"songHashs\":[\"a\"],\"isPlayList\":true}]");
                var collections = await Online.GetUserOnlineFavCollection(_endpoint);
                Assert.That(collections!.Single().Name, Is.EqualTo("Fav"));
                Assert.That(collections.Single().IsPlayList, Is.True);

                _http.RespondWith(HttpStatusCode.InternalServerError);
                Assert.That(await Online.GetUserOnlineFavCollection(_endpoint), Is.Null);

                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.GetUserOnlineFavCollection(_endpoint), Is.Null);
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
            });
        }

        /// <summary>Checks the recommendation feed request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetRecommendFeedAsync_HandlesResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(await Online.GetRecommendFeedAsync(_endpoint, 0, 10), Is.Null);

                SetStatistic("IsUserLoggedIn", true);
                _http.RespondWith(HttpStatusCode.OK, "[{\"id\":\"1\",\"title\":\"R\",\"hash\":\"h\"}]");
                var feed = await Online.GetRecommendFeedAsync(_endpoint, 1, 50);
                Assert.That(feed!.Single().Title, Is.EqualTo("R"));
                Assert.That(_http.Requests.Single().Uri.Query, Is.EqualTo("?source=play-feed&page=1&pageSize=50"));

                _http.RespondWith(HttpStatusCode.InternalServerError);
                Assert.That(await Online.GetRecommendFeedAsync(_endpoint, 0, 10), Is.Null);

                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.GetRecommendFeedAsync(_endpoint, 0, 10), Is.Null);
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
            });
        }

        /// <summary>Checks that impressions are posted with the play feed source and the chart hashes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PostRecommendImpressionsAsync_PostsHashes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{}");

                var response = await Online.PostRecommendImpressionsAsync(_endpoint, new[] { "a", "b" });

                Assert.That(response.IsSuccessfully, Is.True);
                var request = _http.Requests.Single();
                Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/api/recommend/impressions"));
                Assert.That(request.Body, Does.Contain("play-feed"));
                Assert.That(request.Body, Does.Contain("\"a\"").And.Contain("\"b\""));
            });
        }

        /// <summary>Checks that chart interactions are cached until a like invalidates them.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ChartInteract_IsCachedUntilLiked()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.RespondWith(HttpStatusCode.OK, "{\"isLiked\":false,\"plays\":3,\"likes\":[],\"comments\":[]}");

                var first = await Online.GetChartInteractAsync(song);
                var cached = await Online.GetChartInteractAsync(song);
                Assert.That(first!.Value.Plays, Is.EqualTo(3));
                Assert.That(cached!.Value.Plays, Is.EqualTo(3));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{song.Id}/interact"));

                var like = await Online.PostLikeAsync(song);
                Assert.That(like.IsSuccessfully, Is.True);
                Assert.That(_http.Requests[1].Body, Does.Contain("like"));
                await Online.GetChartInteractAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(3));

                Online.ClearResponseCache();
                await Online.GetChartInteractAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(4));
            });
        }

        /// <summary>Checks feedback methods, multipart fields and cache invalidation after each successful interaction.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ChartInteract_FeedbackRequestsInvalidateCache()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                var getCount = 0;
                var fields = Array.Empty<(string Name, string Content)>();
                var mediaType = string.Empty;
                _http.Responder = request =>
                {
                    if (request.Method == HttpMethod.Get)
                    {
                        getCount++;
                        return StubHttpScope.Respond(HttpStatusCode.OK, $"{{\"plays\":{getCount},\"likes\":[],\"comments\":[]}}");
                    }
                    mediaType = request.Content?.Headers.ContentType?.MediaType ?? string.Empty;
                    fields = request.Content is MultipartFormDataContent form
                        ? form.Select(part => (part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty,
                            part.ReadAsStringAsync().GetAwaiter().GetResult())).ToArray()
                        : Array.Empty<(string, string)>();
                    return StubHttpScope.Respond(HttpStatusCode.OK, "{}");
                };
                await Online.GetChartInteractAsync(song);
                var cases = new (Func<ValueTask<EndpointResponse>> Send, HttpMethod Method, string Type, string Content)[]
                {
                    (() => Online.PostDislikeAsync(song), HttpMethod.Post, "dislike", "dislike"),
                    (() => Online.PostCommentAsync(song, "Helpful comment"), HttpMethod.Post, "comment", "Helpful comment"),
                    (() => Online.DeleteLikeAsync(song), HttpMethod.Delete, "like", string.Empty),
                    (() => Online.DeleteDislikeAsync(song), HttpMethod.Delete, "dislike", string.Empty),
                };
                foreach (var item in cases)
                {
                    var before = _http.Requests.Length;
                    var expectedGetCount = getCount + 1;
                    var response = await item.Send();

                    Assert.That(response.IsSuccessfully, Is.True);
                    var request = _http.Requests.Last();
                    Assert.That(request.Method, Is.EqualTo(item.Method));
                    Assert.That(request.Uri.AbsoluteUri, Is.EqualTo($"https://api.invalid/api/maichart/{song.Id}/interact"));
                    Assert.That(mediaType, Is.EqualTo("multipart/form-data"));
                    Assert.That(fields, Is.EquivalentTo(new[] { ("type", item.Type), ("content", item.Content) }));

                    var refreshed = await Online.GetChartInteractAsync(song);
                    Assert.That(refreshed!.Value.Plays, Is.EqualTo(expectedGetCount));
                    await Online.GetChartInteractAsync(song);
                    Assert.That(_http.Requests.Length, Is.EqualTo(before + 2), "A successful interaction refreshes the cache once.");
                }
                Assert.That(getCount, Is.EqualTo(cases.Length + 1));
            });
        }

        /// <summary>Checks that an explicit refresh bypasses a live interaction cache and replaces its response.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ChartInteract_ForceRefreshReplacesCachedResponse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.RespondWith(HttpStatusCode.OK, "{\"plays\":1,\"likes\":[],\"comments\":[]}");
                await Online.GetChartInteractAsync(song);
                _http.RespondWith(HttpStatusCode.OK, "{\"plays\":2,\"likes\":[],\"comments\":[]}");

                var cached = await Online.GetChartInteractAsync(song);
                Assert.That(cached!.Value.Plays, Is.EqualTo(1));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                var refreshed = await Online.GetChartInteractAsync(song, forceRefresh: true);
                Assert.That(refreshed!.Value.Plays, Is.EqualTo(2));
                var next = await Online.GetChartInteractAsync(song);
                Assert.That(next!.Value.Plays, Is.EqualTo(2));
                Assert.That(_http.Requests.Length, Is.EqualTo(2));
            });
        }

        /// <summary>Checks that a failed comment is sent only once, even when its response is lost or the server fails.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PostCommentAsync_DoesNotRetryFailures()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.Responder = _ => throw new HttpRequestException("lost comment response");

                var lost = await Online.PostCommentAsync(song, "First comment");

                Assert.That(lost.ErrorCode, Is.EqualTo(HttpErrorCode.Unreachable));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                _http.RespondWith(HttpStatusCode.ServiceUnavailable, "{}");

                var failed = await Online.PostCommentAsync(song, "Second comment");

                Assert.That(failed.IsSuccessfully, Is.False);
                Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
                Assert.That(_http.Requests.Length, Is.EqualTo(2));
            });
        }

        /// <summary>Checks the failures of chart interaction requests.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ChartInteract_Failures()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.RespondWith(HttpStatusCode.NotFound);
                Assert.That(await Online.GetChartInteractAsync(song), Is.Null);
                Assert.That((await Online.PostLikeAsync(song)).IsSuccessfully, Is.False);

                _http.RespondWith(HttpStatusCode.InternalServerError);
                Assert.That(await Online.GetChartInteractAsync(song), Is.Null);
                Assert.That((await Online.PostLikeAsync(song)).IsSuccessfully, Is.False);

                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                _http.RespondWith(HttpStatusCode.OK, "{}");
                Assert.That((await Online.PostLikeAsync(song, canceled.Token)).ErrorCode, Is.EqualTo(HttpErrorCode.Canceled));
            });
        }

        /// <summary>Checks that chart score information is cached until a score is posted.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ChartScores_AreCachedUntilScorePosted()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.RespondWith(HttpStatusCode.OK, "{\"hash\":\"h\",\"scores\":[[{\"player\":{\"username\":\"p\"},\"acc\":100.5}]]}");

                var first = await Online.GetChartScoreInfoAsync(song);
                await Online.GetChartScoreInfoAsync(song);
                Assert.That(first!.Value.Scores[0][0].Player.Username, Is.EqualTo("p"));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                var posted = await Online.PostScoreAsync(song, new MaiScore { Hash = "h", DXScore = 10 });
                Assert.That(posted.IsSuccessfully, Is.True);
                Assert.That(_http.Requests[1].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{song.Id}/score"));
                await Online.GetChartScoreInfoAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(3));

                Online.ClearResponseCache();
                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.GetChartScoreInfoAsync(song), Is.Null);
                Assert.That((await Online.PostScoreAsync(song, new MaiScore())).IsSuccessfully, Is.False);
                _http.RespondWith(HttpStatusCode.ServiceUnavailable);
                Assert.That(await Online.GetChartScoreInfoAsync(song), Is.Null);
                Assert.That((await Online.PostScoreAsync(song, new MaiScore())).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            });
        }

        /// <summary>Checks that a missing avatar identifier or avatar file yields no sprite.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetAvatarAsync_WithoutAvatar_ReturnsNull()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(await Online.GetAvatarAsync(_endpoint, null), Is.Null);
                Assert.That(await Online.GetAvatarAsync(_endpoint, string.Empty), Is.Null);
                Assert.That(_http.Requests, Is.Empty);

                _http.RespondWith(HttpStatusCode.NotFound);
                Assert.That(await Online.GetAvatarAsync(_endpoint, "a b"), Is.Null);
                Assert.That(_http.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/api/avatar/a%20b"));
            });
        }
    }
}
