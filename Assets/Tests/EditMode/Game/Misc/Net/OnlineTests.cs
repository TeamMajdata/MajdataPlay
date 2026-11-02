using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
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
        /// <param name="chartId">The identifier of the chart, or null for a new one.</param>
        /// <returns>The online song.</returns>
        private OnlineSongDetail CreateSong(string? chartId = null)
        {
            var detail = new MajnetSongDetail
            {
                Id = chartId ?? Guid.NewGuid().ToString(),
                Title = "Song",
                Artist = "Artist",
                Hash = "aGFzaA==",
                Levels = new[] { "1", "2" },
                Uploader = "uploader",
                Designer = "designer",
                Tags = new[] { "tag" },
            };
            var cache = new OnlineChartCache(
                new ObjectStore(_temporary.Directory.CreateDirectory("Objects"), _temporary.Directory.CreateDirectory("Partials")),
                false);
            return new OnlineSongDetail(_endpoint, detail, cache.CreateTransientRecord(_endpoint, detail));
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
                    await Online.Account.LoginAsync(null!, "user", "pass");
                }
                catch (ArgumentNullException exception)
                {
                    failure = exception;
                }

                var placeholder = await Online.Account.LoginAsync(_endpoint, "YourUsername", "x");
                var empty = await Online.Account.LoginAsync(_endpoint, "user", string.Empty);

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

                var first = await Online.Account.LoginAsync(_endpoint, "user", "secret-password");
                var second = await Online.Account.LoginAsync(_endpoint, "user", "secret-password");

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

                    var response = await Online.Account.LoginAsync(_endpoint, "user", "pass");

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

                var response = await Online.Account.LoginAsync(_endpoint, "user", "pass");

                Assert.That(response.IsSuccessfully, Is.True);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.True);
            });
        }

        /// <summary>Checks that the profile request updates the login state.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetProfileAsync_TracksLoginState()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{\"username\":\"player\",\"nickname\":\"Nick\"}");
                var profile = await Online.Account.GetProfileAsync(_endpoint);
                Assert.That(profile.IsSuccessfully, Is.True);
                Assert.That(profile.Deserialize().DisplayName, Is.EqualTo("Nick"));
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.True);

                _http.RespondWith(HttpStatusCode.Unauthorized, "{}");
                _endpoint.RuntimeConfig.Username = "player";
                var unauthorized = await Online.Account.GetProfileAsync(_endpoint);
                Assert.That(unauthorized.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
                Assert.That(_endpoint.RuntimeConfig.Username, Is.Empty);

                _http.RespondWith(HttpStatusCode.InternalServerError, "{}");
                var failed = await Online.Account.GetProfileAsync(_endpoint);
                Assert.That(failed.IsSuccessfully, Is.False);
                Assert.That(_http.Requests.Length, Is.EqualTo(2 + MajEnv.HTTP_REQUEST_MAX_RETRY + 1));
            });
        }

        /// <summary>Checks the user score request, its unauthorized result and its retries.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetScoresAsync_HandlesResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "[{\"hash\":\"h\",\"dxScore\":12,\"chartLevel\":4,\"comboState\":2}]");
                var scores = await Online.Account.GetScoresAsync(_endpoint);
                Assert.That(scores.Single().Hash, Is.EqualTo("h"));
                Assert.That(scores.Single().DXScore, Is.EqualTo(12u));
                Assert.That(scores.Single().ChartLevel, Is.EqualTo(ChartLevel.Master));

                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.Account.GetScoresAsync(_endpoint), Is.Empty);

                _http.RespondWith(HttpStatusCode.BadGateway);
                var before = _http.Requests.Length;
                Assert.That(await Online.Account.GetScoresAsync(_endpoint), Is.Empty);
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
                Assert.That(Online.Machine.IsRegistered(_endpoint), Is.False);
                _http.RespondWith(HttpStatusCode.OK, "{}");
                var registered = await Online.Machine.RegisterAsync(_endpoint, new MachineInfo { Name = "Cabinet" });
                Assert.That(registered.IsSuccessfully, Is.True);
                Assert.That(Online.Machine.IsRegistered(_endpoint), Is.True);
                Assert.That(_http.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/api/machine/register"));
                Assert.That(_http.Requests.Single().Body, Does.Contain("Cabinet"));

                StubHttpScope.ClearOnlineState();
                _http.RespondWith(HttpStatusCode.NotFound);
                var unsupported = await Online.Machine.RegisterAsync(_endpoint, new MachineInfo());
                var cached = await Online.Machine.RegisterAsync(_endpoint, new MachineInfo());
                Assert.That(unsupported.ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That(cached.Message, Is.EqualTo("MAJTEXT_ONLINE_MACHINE_REGISTRATION_UNSUPPORTED"));
                Assert.That(_http.Requests.Length, Is.EqualTo(2));
                Assert.That(Online.Machine.IsRegistered(_endpoint), Is.False);
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

                await Online.Machine.RegisterAsync(_endpoint, new MachineInfo { Name = "Cabinet" });

                Assert.That(JObject.Parse(_http.Requests.Single().Body).ContainsKey("name"), Is.True);
            });
        }

        /// <summary>Checks the machine check request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CheckRegistrationAsync_FollowsStatus()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{}");
                Assert.That(await Online.Machine.CheckRegistrationAsync(_endpoint), Is.True);

                _http.RespondWith(HttpStatusCode.Forbidden);
                Assert.That(await Online.Machine.CheckRegistrationAsync(_endpoint), Is.False);
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
                Assert.That(await Online.Machine.CheckRegistrationAsync(_endpoint), Is.False);

                _http.Responder = _ => throw new System.Threading.Tasks.TaskCanceledException("timeout");
                Assert.That(await Online.Machine.CheckRegistrationAsync(_endpoint), Is.False);

                SetStatistic("IsMachineRegistrationSupported", true);
                _http.Responder = _ => throw new HttpRequestException("offline");
                var post = await Online.Machine.AuthRevokeAsync(_endpoint, "id");
                Assert.That(post.ErrorCode, Is.EqualTo(HttpErrorCode.Unreachable));

                _http.Responder = _ => throw new System.Threading.Tasks.TaskCanceledException("timeout");
                var timeout = await Online.Machine.AuthRevokeAsync(_endpoint, "id");
                Assert.That(timeout.ErrorCode, Is.EqualTo(HttpErrorCode.Timeout));

                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                var getCanceled = await Online.Machine.AuthCheckAsync(_endpoint, "id", canceled.Token);
                var postCanceled = await Online.Machine.AuthRevokeAsync(_endpoint, "id", canceled.Token);
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
                    async () => await Online.Machine.AuthRequestAsync(_endpoint),
                    async () => await Online.Machine.AuthCheckAsync(_endpoint, "id"),
                    async () => await Online.Machine.AuthRevokeAsync(_endpoint, "id"),
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
                Assert.That((await Online.Machine.AuthRequestAsync(_endpoint)).ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That((await Online.Machine.AuthCheckAsync(_endpoint, "id")).ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That((await Online.Machine.AuthRevokeAsync(_endpoint, "id")).ErrorCode, Is.EqualTo(HttpErrorCode.NotSupported));
                Assert.That(_http.Requests, Is.Empty);

                SetStatistic("IsMachineRegistrationSupported", true);
                _http.RespondWith(HttpStatusCode.Created, "{\"authId\":\"x\"}");
                var created = await Online.Machine.AuthRequestAsync(_endpoint);
                Assert.That(created.IsSuccessfully, Is.True);
                Assert.That(created.IsDeserializable, Is.True);

                _http.RespondWith(HttpStatusCode.OK, "{}");
                var notCreated = await Online.Machine.AuthRequestAsync(_endpoint);
                Assert.That(notCreated.IsSuccessfully, Is.False);
                Assert.That(notCreated.Message, Is.EqualTo("MAJTEXT_ONLINE_MACHINE_AUTH_REQUEST_FAILED"));

                var check = await Online.Machine.AuthCheckAsync(_endpoint, "abc");
                var revoke = await Online.Machine.AuthRevokeAsync(_endpoint, "abc");
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
                await Online.Account.LoginAsync(_endpoint, "user", "pass");
                _endpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.None;

                await Online.Account.LogoutAllAsync();

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
        public IEnumerator GetListAsync_HandlesResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "[{\"id\":\"1\",\"title\":\"A\",\"hash\":\"h\"}]");
                var list = await Online.MaiChart.GetListAsync(_endpoint);
                Assert.That(list!.Single().Title, Is.EqualTo("A"));

                _http.RespondWith(HttpStatusCode.NotFound);
                var before = _http.Requests.Length;
                Assert.That(await Online.MaiChart.GetListAsync(_endpoint), Is.Null);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(1));

                _http.RespondWith(HttpStatusCode.OK, "{ broken");
                before = _http.Requests.Length;
                Assert.That(await Online.MaiChart.GetListAsync(_endpoint), Is.Null);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY));
            });
        }

        /// <summary>
        /// Checks that chart info is posted in batches of chart identities without blank ones, and that the files of every
        /// batch are parsed in request order.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetInfosAsync_PostsBatchesAndParsesFiles()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.Responder = request =>
                {
                    var ids = JArray.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    var infos = new JArray(ids.Select(id => new JObject
                    {
                        ["metadata"] = new JObject { ["id"] = id, ["hash"] = "h" },
                        ["resources"] = new JObject
                        {
                            ["maidata"] = new JObject
                            {
                                ["url"] = $"https://cdn.invalid/MaiCharts/{id}/maidata.txt",
                                ["md5"] = "0123456789abcdef0123456789abcdef",
                                ["sha256"] = new string('a', 64),
                                ["length"] = 3
                            },
                            ["track"] = null,
                            ["video"] = null,
                            ["coverArt"] = null,
                            ["thumbnail"] = null
                        }
                    }));
                    return StubHttpScope.Respond(HttpStatusCode.OK, infos.ToString());
                };
                var ids = Enumerable.Range(0, 501).Select(i => i.ToString()).Append(" ").Append(null).ToArray();

                var infos = await Online.MaiChart.GetInfosAsync(_endpoint, ids);

                Assert.That(_http.Requests.Length, Is.EqualTo(2));
                Assert.That(_http.Requests.All(request => request.Method == HttpMethod.Post), Is.True);
                Assert.That(_http.Requests[0].Uri.AbsoluteUri, Is.EqualTo("https://api.invalid/api/maichart/info"));
                Assert.That(JArray.Parse(_http.Requests[0].Body).Count, Is.EqualTo(500));
                Assert.That(JArray.Parse(_http.Requests[1].Body).Single().ToString(), Is.EqualTo("500"));
                Assert.That(infos!.Select(info => info.Metadata!.Id), Is.EqualTo(Enumerable.Range(0, 501).Select(i => i.ToString())));
                var resources = infos[7].Resources!;
                Assert.That(resources.Maidata!.Url, Is.EqualTo(new Uri("https://cdn.invalid/MaiCharts/7/maidata.txt")));
                Assert.That(resources.Maidata.Md5, Is.EqualTo("0123456789abcdef0123456789abcdef"));
                Assert.That(resources.Maidata.Sha256, Is.EqualTo(new string('a', 64)));
                Assert.That(resources.Maidata.Length, Is.EqualTo(3));
                Assert.That(resources.Track, Is.Null);
            });
        }

        /// <summary>Checks that the server identity is read from <c>GET info</c>, and that a server without the API yields null.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetServerInfoAsync_ReadsIdentity()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{\"id\":\"majnet\",\"name\":\"MajdataNET\"}");
                var serverInfo = await Online.GetServerInfoAsync(_endpoint);
                Assert.That(serverInfo!.Id, Is.EqualTo("majnet"));
                Assert.That(serverInfo.Name, Is.EqualTo("MajdataNET"));
                Assert.That(_http.Requests.Single().Uri.AbsoluteUri, Is.EqualTo("https://api.invalid/api/info"));

                _http.RespondWith(HttpStatusCode.NotFound);
                var before = _http.Requests.Length;
                Assert.That(await Online.GetServerInfoAsync(_endpoint), Is.Null);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(1));
            });
        }

        /// <summary>Checks that an endpoint without the info API, or a failed batch, yields null.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetInfosAsync_ReturnsNullWhenBatchFails()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.NotFound);
                Assert.That(await Online.MaiChart.GetInfosAsync(_endpoint, new[] { "1" }), Is.Null);
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                _http.RespondWith(HttpStatusCode.OK, "{ broken");
                Assert.That(await Online.MaiChart.GetInfosAsync(_endpoint, new[] { "1" }), Is.Null);

                var before = _http.Requests.Length;
                Assert.That(await Online.MaiChart.GetInfosAsync(_endpoint, Array.Empty<string>()), Is.Empty);
                Assert.That(_http.Requests.Length, Is.EqualTo(before));
            });
        }

        /// <summary>Checks the favorite collection request, which requires a logged-in user.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetFavoriteCollectionsAsync_RequiresLogin()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(await Online.Account.GetFavoriteCollectionsAsync(_endpoint), Is.Null);
                Assert.That(_http.Requests, Is.Empty);

                SetStatistic("IsUserLoggedIn", true);
                _http.RespondWith(HttpStatusCode.OK, "[{\"id\":\"1\",\"name\":\"Fav\",\"createdBy\":\"u\",\"description\":\"d\",\"songHashs\":[\"a\"],\"isPlayList\":true}]");
                var collections = await Online.Account.GetFavoriteCollectionsAsync(_endpoint);
                Assert.That(collections!.Single().Name, Is.EqualTo("Fav"));
                Assert.That(collections.Single().IsPlayList, Is.True);

                _http.RespondWith(HttpStatusCode.InternalServerError);
                Assert.That(await Online.Account.GetFavoriteCollectionsAsync(_endpoint), Is.Null);

                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.Account.GetFavoriteCollectionsAsync(_endpoint), Is.Null);
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
            });
        }

        /// <summary>Checks the recommendation feed request.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetFeedAsync_HandlesResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(await Online.Recommend.GetFeedAsync(_endpoint, 0, 10), Is.Null);

                SetStatistic("IsUserLoggedIn", true);
                _http.RespondWith(HttpStatusCode.OK, "[{\"id\":\"1\",\"title\":\"R\",\"hash\":\"h\"}]");
                var feed = await Online.Recommend.GetFeedAsync(_endpoint, 1, 50);
                Assert.That(feed!.Single().Title, Is.EqualTo("R"));
                Assert.That(_http.Requests.Single().Uri.Query, Is.EqualTo("?source=play-feed&page=1&pageSize=50"));

                _http.RespondWith(HttpStatusCode.InternalServerError);
                Assert.That(await Online.Recommend.GetFeedAsync(_endpoint, 0, 10), Is.Null);

                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.Recommend.GetFeedAsync(_endpoint, 0, 10), Is.Null);
                Assert.That(_endpoint.RuntimeConfig.IsLoggedIn, Is.False);
            });
        }

        /// <summary>Checks that impressions are posted with the play feed source and the chart hashes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PostImpressionsAsync_PostsHashes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _http.RespondWith(HttpStatusCode.OK, "{}");

                var response = await Online.Recommend.PostImpressionsAsync(_endpoint, new[] { "a", "b" });

                Assert.That(response.IsSuccessfully, Is.True);
                var request = _http.Requests.Single();
                Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/api/recommend/impressions"));
                Assert.That(request.Body, Does.Contain("play-feed"));
                Assert.That(request.Body, Does.Contain("\"a\"").And.Contain("\"b\""));
            });
        }

        /// <summary>Checks that the chart counts are cached until a vote invalidates them, and that the vote is sent as JSON.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InteractSummary_IsCachedUntilVoted()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.Responder = request => request.Method == HttpMethod.Put
                    ? StubHttpScope.Respond(HttpStatusCode.OK, "{\"score\":4,\"upvotes\":5,\"downvotes\":1,\"myVote\":1}")
                    : StubHttpScope.Respond(HttpStatusCode.OK, "{\"chartId\":\"" + song.Id + "\",\"plays\":3,\"score\":3,\"upvotes\":4,\"downvotes\":1,\"comments\":2,\"myVote\":0}");

                var first = await Online.MaiChart.GetInteractSummaryAsync(song);
                var cached = await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(first!.Value.ChartId, Is.EqualTo(song.Id));
                Assert.That(first.Value.Plays, Is.EqualTo(3));
                Assert.That(first.Value.Comments, Is.EqualTo(2));
                Assert.That(cached!.Value.Score, Is.EqualTo(3));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{song.Id}/interactsum"));

                var vote = await Online.MaiChart.VoteAsync(song, 1);
                Assert.That(vote.IsSuccessfully, Is.True);
                Assert.That(vote.Deserialize().MyVote, Is.EqualTo(1));
                Assert.That(vote.Deserialize().Upvotes, Is.EqualTo(5));
                var request = _http.Requests[1];
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Put));
                Assert.That(request.Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{song.Id}/vote"));
                Assert.That(JObject.Parse(request.Body)["value"]!.Value<int>(), Is.EqualTo(1));
                await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(3), "A stored vote refreshes the counts.");

                Online.ClearResponseCache();
                await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(4));
            });
        }

        /// <summary>
        /// Checks that a withdrawn vote is sent as 0 in a JSON body, that an invalid vote is rejected without a request, and
        /// that a refused vote keeps the cached counts.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator VoteAsync_SendsWantedVote()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                var mediaType = string.Empty;
                _http.Responder = request =>
                {
                    if (request.Method == HttpMethod.Get)
                    {
                        return StubHttpScope.Respond(HttpStatusCode.OK, "{\"plays\":1}");
                    }
                    mediaType = request.Content?.Headers.ContentType?.MediaType ?? string.Empty;
                    return StubHttpScope.Respond(HttpStatusCode.OK, "{\"score\":0,\"upvotes\":0,\"downvotes\":0,\"myVote\":0}");
                };

                var withdrawn = await Online.MaiChart.VoteAsync(song, 0);
                Assert.That(withdrawn.IsSuccessfully, Is.True);
                Assert.That(JObject.Parse(_http.Requests[0].Body)["value"]!.Value<int>(), Is.EqualTo(0));
                Assert.That(mediaType, Is.EqualTo("application/json"));

                var failure = default(Exception);
                try
                {
                    await Online.MaiChart.VoteAsync(song, 2);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    failure = exception;
                }
                Assert.That(failure, Is.Not.Null);
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                await Online.MaiChart.GetInteractSummaryAsync(song);
                _http.RespondWith(HttpStatusCode.Forbidden, "{\"code\":-127,\"message\":\"The uploader disabled interactions on their charts\"}");
                var refused = await Online.MaiChart.VoteAsync(song, 1);
                Assert.That(refused.IsSuccessfully, Is.False);
                Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That((await Online.MaiChart.GetInteractSummaryAsync(song))!.Value.Plays, Is.EqualTo(1));
                Assert.That(_http.Requests.Length, Is.EqualTo(3), "A refused vote keeps the cached counts.");
            });
        }

        /// <summary>
        /// Checks that comment pages are read per order and page, keep placeholders and replies, and are cached until a
        /// comment is posted as JSON.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Comments_AreCachedPerPageUntilCommentPosted()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                const string page = "{\"items\":[{\"id\":\"65f0c0ffee0000000000000a\",\"chartId\":null,\"chartHash\":\"h\",\"parentId\":null,"
                    + "\"depth\":0,\"authorId\":null,\"author\":\"\",\"body\":\"\",\"createdAt\":\"2026-01-02T03:04:05Z\",\"editedAt\":null,"
                    + "\"deleted\":true,\"removed\":false,\"score\":0,\"myVote\":0,\"replyCount\":1,\"replies\":["
                    + "{\"id\":\"65f0c0ffee0000000000000b\",\"parentId\":\"65f0c0ffee0000000000000a\",\"depth\":1,\"author\":\"bob\","
                    + "\"body\":\"reply\",\"deleted\":false,\"removed\":false,\"score\":2,\"myVote\":1,\"replyCount\":0,\"replies\":[]}]}],"
                    + "\"total\":1,\"commentCount\":1,\"page\":1,\"pageSize\":20,\"sort\":\"best\"}";
                const string created = "{\"id\":\"65f0c0ffee0000000000000c\",\"author\":\"me\",\"body\":\"Nice chart\",\"score\":1,\"myVote\":1,\"replies\":[]}";
                _http.Responder = request =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        return StubHttpScope.Respond(HttpStatusCode.Created, created);
                    }
                    return request.RequestUri!.AbsolutePath.EndsWith("interactsum")
                        ? StubHttpScope.Respond(HttpStatusCode.OK, "{\"comments\":1}")
                        : StubHttpScope.Respond(HttpStatusCode.OK, page);
                };

                var first = await Online.MaiChart.GetCommentsAsync(song);
                await Online.MaiChart.GetCommentsAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{song.Id}/comments"));
                Assert.That(_http.Requests[0].Uri.Query, Is.EqualTo("?sort=best&page=1"));
                var placeholder = first!.Value.Items.Single();
                Assert.That(placeholder.IsPlaceholder, Is.True);
                Assert.That(placeholder.Replies.Single().Author, Is.EqualTo("bob"));
                Assert.That(placeholder.Replies.Single().ParentId, Is.EqualTo(placeholder.Id));
                Assert.That(first.Value.Sort, Is.EqualTo(CommentSort.Best));

                await Online.MaiChart.GetCommentsAsync(song, CommentSort.Controversial, 2);
                Assert.That(_http.Requests[1].Uri.Query, Is.EqualTo("?sort=controversial&page=2"));
                var failure = default(Exception);
                try
                {
                    await Online.MaiChart.GetCommentsAsync(song, CommentSort.Best, 0);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    failure = exception;
                }
                Assert.That(failure, Is.Not.Null);

                await Online.MaiChart.GetInteractSummaryAsync(song);
                var posted = await Online.MaiChart.PostCommentAsync(song, "Nice chart");
                Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.Created));
                Assert.That(posted.Deserialize().Body, Is.EqualTo("Nice chart"));
                var post = _http.Requests[3];
                Assert.That(post.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(post.Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{song.Id}/comments"));
                Assert.That(JObject.Parse(post.Body)["body"]!.Value<string>(), Is.EqualTo("Nice chart"));

                await Online.MaiChart.GetCommentsAsync(song);
                await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(6), "A posted comment refreshes the comments and the counts.");
            });
        }

        /// <summary>
        /// Checks that responses are cached by request URI, so every song object of a chart shares them, and that a change
        /// of the login drops them, since they carry the votes of the user who read them.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ResponseCache_IsSharedPerChartAndDroppedOnLoginChange()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                using var sameChart = CreateSong(song.Id);
                _http.RespondWith(HttpStatusCode.OK, "{\"plays\":1,\"myVote\":0}");

                await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That((await Online.MaiChart.GetInteractSummaryAsync(sameChart))!.Value.Plays, Is.EqualTo(1));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                SetStatistic("IsUserLoggedIn", true);
                await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(2), "A login drops the cached responses.");

                SetStatistic("IsUserLoggedIn", true);
                await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(2), "Confirming the same login keeps them.");

                SetStatistic("IsUserLoggedIn", false);
                await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(3), "A logout drops them.");
            });
        }

        /// <summary>Checks that an explicit refresh bypasses live cached counts and replaces them.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InteractSummary_ForceRefreshReplacesCachedResponse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.RespondWith(HttpStatusCode.OK, "{\"plays\":1}");
                await Online.MaiChart.GetInteractSummaryAsync(song);
                _http.RespondWith(HttpStatusCode.OK, "{\"plays\":2}");

                var cached = await Online.MaiChart.GetInteractSummaryAsync(song);
                Assert.That(cached!.Value.Plays, Is.EqualTo(1));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                var refreshed = await Online.MaiChart.GetInteractSummaryAsync(song, forceRefresh: true);
                Assert.That(refreshed!.Value.Plays, Is.EqualTo(2));
                var next = await Online.MaiChart.GetInteractSummaryAsync(song);
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

                var lost = await Online.MaiChart.PostCommentAsync(song, "First comment");

                Assert.That(lost.ErrorCode, Is.EqualTo(HttpErrorCode.Unreachable));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                _http.RespondWith(HttpStatusCode.TooManyRequests, "{}");

                var failed = await Online.MaiChart.PostCommentAsync(song, "Second comment");

                Assert.That(failed.IsSuccessfully, Is.False);
                Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
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
                Assert.That(await Online.MaiChart.GetInteractSummaryAsync(song), Is.Null);
                Assert.That(await Online.MaiChart.GetCommentsAsync(song), Is.Null);
                Assert.That((await Online.MaiChart.VoteAsync(song, 1)).IsSuccessfully, Is.False);

                _http.RespondWith(HttpStatusCode.InternalServerError);
                Assert.That(await Online.MaiChart.GetInteractSummaryAsync(song), Is.Null);
                Assert.That((await Online.MaiChart.VoteAsync(song, 1)).IsSuccessfully, Is.False);

                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                _http.RespondWith(HttpStatusCode.OK, "{}");
                Assert.That((await Online.MaiChart.VoteAsync(song, 1, canceled.Token)).ErrorCode, Is.EqualTo(HttpErrorCode.Canceled));
            });
        }

        /// <summary>
        /// Checks that chart requests retry an unreadable successful response and an unreachable server, but stop at the
        /// first status code that refuses them.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ChartInteract_RetriesOnlyUnreadableOrUnansweredResponses()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var song = CreateSong();
                _http.RespondWith(HttpStatusCode.OK, "{ broken");
                Assert.That(await Online.MaiChart.GetInteractSummaryAsync(song), Is.Null);
                Assert.That(_http.Requests.Length, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY + 1));

                _http.Responder = _ => throw new HttpRequestException("offline");
                var before = _http.Requests.Length;
                Assert.That(await Online.MaiChart.GetScoreInfoAsync(song), Is.Null);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(MajEnv.HTTP_REQUEST_MAX_RETRY + 1));

                _http.RespondWith(HttpStatusCode.ServiceUnavailable, "{}");
                before = _http.Requests.Length;
                Assert.That(await Online.MaiChart.GetCommentsAsync(song), Is.Null);
                Assert.That(_http.Requests.Length - before, Is.EqualTo(1));
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

                var first = await Online.MaiChart.GetScoreInfoAsync(song);
                await Online.MaiChart.GetScoreInfoAsync(song);
                Assert.That(first!.Value.Scores[0][0].Player.Username, Is.EqualTo("p"));
                Assert.That(_http.Requests.Length, Is.EqualTo(1));

                var posted = await Online.MaiChart.PostScoreAsync(song, new MaiScore { Hash = "h", DXScore = 10 });
                Assert.That(posted.IsSuccessfully, Is.True);
                Assert.That(_http.Requests[1].Uri.AbsolutePath, Is.EqualTo($"/api/maichart/{song.Id}/score"));
                await Online.MaiChart.GetScoreInfoAsync(song);
                Assert.That(_http.Requests.Length, Is.EqualTo(3));

                Online.ClearResponseCache();
                _http.RespondWith(HttpStatusCode.Unauthorized);
                Assert.That(await Online.MaiChart.GetScoreInfoAsync(song), Is.Null);
                Assert.That((await Online.MaiChart.PostScoreAsync(song, new MaiScore())).IsSuccessfully, Is.False);
                _http.RespondWith(HttpStatusCode.ServiceUnavailable);
                Assert.That(await Online.MaiChart.GetScoreInfoAsync(song), Is.Null);
                Assert.That((await Online.MaiChart.PostScoreAsync(song, new MaiScore())).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            });
        }

        /// <summary>Checks that a missing avatar identifier or avatar file yields no sprite.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetAvatarAsync_WithoutAvatar_ReturnsNull()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(await Online.Account.GetAvatarAsync(_endpoint, null), Is.Null);
                Assert.That(await Online.Account.GetAvatarAsync(_endpoint, string.Empty), Is.Null);
                Assert.That(_http.Requests, Is.Empty);

                _http.RespondWith(HttpStatusCode.NotFound);
                Assert.That(await Online.Account.GetAvatarAsync(_endpoint, "a b"), Is.Null);
                Assert.That(_http.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/api/avatar/a%20b"));
            });
        }
    }
}
