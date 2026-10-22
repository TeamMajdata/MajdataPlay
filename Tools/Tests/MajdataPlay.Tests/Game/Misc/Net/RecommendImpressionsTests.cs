using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Net;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Net
{
    /// <summary>Covers the List scene impression reporting of <see cref="RecommendImpressions"/> against a stub HTTP client.</summary>
    public class RecommendImpressionsTests
    {
        /// <summary>The stub HTTP clients.</summary>
        private StubHttpScope _http = null!;

        /// <summary>The logged-in endpoint serving the recommendations.</summary>
        private ApiEndpoint _endpoint = null!;

        /// <summary>The recommended song.</summary>
        private FakeSongDetail _song = null!;

        /// <summary>The recommendation folder containing the song.</summary>
        private OnlineSongCollection _folder = null!;

        /// <summary>Gets the private tracker table.</summary>
        private static IDictionary Entries => PrivateMember.GetStaticField<IDictionary>(typeof(RecommendImpressions), "_entries");

        /// <summary>Resets the trackers and prepares a logged-in endpoint with one recommended song.</summary>
        [SetUp]
        public void SetUp()
        {
            _http = new StubHttpScope();
            _http.RespondWith(HttpStatusCode.OK, "{}");
            Entries.Clear();
            PrivateMember.SetStaticField(typeof(RecommendImpressions), "_focused", null);
            PrivateMember.GetStaticField<IDictionary>(typeof(SongStorage), "s_recommendStates").Clear();
            _endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://api.invalid/api/") };
            _endpoint.RuntimeConfig.IsLoggedIn = true;
            _song = new FakeSongDetail("recommended-hash", location: ChartStorageLocation.Online);
            _folder = new OnlineSongCollection(_endpoint, "Recommend", new ISongDetail[] { _song }) { Type = ChartStorageType.Recommend };
        }

        /// <summary>Removes the trackers and restores the HTTP clients.</summary>
        [TearDown]
        public void TearDown()
        {
            Entries.Clear();
            PrivateMember.SetStaticField(typeof(RecommendImpressions), "_focused", null);
            PrivateMember.GetStaticField<IDictionary>(typeof(SongStorage), "s_recommendStates").Clear();
            _http.Dispose();
        }

        /// <summary>Waits until the stub client has received the given number of requests.</summary>
        /// <param name="count">The expected request count.</param>
        /// <returns>A task completing when the requests arrived or the wait timed out.</returns>
        private async UniTask WaitForRequestsAsync(int count)
        {
            for (var i = 0; i < 100 && _http.Requests.Length < count; i++)
            {
                await UniTask.Delay(20, DelayType.Realtime);
            }
        }

        /// <summary>Records a new recommendation feed for the endpoint, which starts a new impression batch.</summary>
        /// <param name="generation">The feed generation.</param>
        private void SetFeedGeneration(int generation)
        {
            var stateType = typeof(SongStorage).GetNestedType("RecommendFeedState", BindingFlags.NonPublic)!;
            var state = Activator.CreateInstance(stateType)!;
            PrivateMember.SetProperty(state, "Generation", generation);
            PrivateMember.SetProperty(state, "FetchedAt", DateTime.UtcNow);
            PrivateMember.SetProperty(state, "Charts", Array.Empty<MajnetSongDetail>());
            PrivateMember.GetStaticField<IDictionary>(typeof(SongStorage), "s_recommendStates")[_endpoint] = state;
        }

        /// <summary>Checks that songs outside recommendation folders are never tracked.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NonRecommendCollections_AreIgnored()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var list = new OnlineSongCollection(_endpoint, "List", new ISongDetail[] { _song });

                RecommendImpressions.Focus(list, _song);
                RecommendImpressions.Commit(list, _song);
                RecommendImpressions.Commit(_folder, null);
                RecommendImpressions.Blur();
                await UniTask.Delay(100, DelayType.Realtime);

                Assert.That(Entries.Count, Is.EqualTo(0));
                Assert.That(_http.Requests, Is.Empty);
            });
        }

        /// <summary>Checks that starting a recommended song reports it at once, only once per feed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Commit_ReportsOncePerFeed()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecommendImpressions.Commit(_folder, _song);
                await WaitForRequestsAsync(1);
                RecommendImpressions.Commit(_folder, _song);
                await UniTask.Delay(100, DelayType.Realtime);

                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(_http.Requests[0].Uri.AbsolutePath, Is.EqualTo("/api/recommend/impressions"));
                Assert.That(_http.Requests[0].Body, Does.Contain("recommended-hash"));

                SetFeedGeneration(5);
                RecommendImpressions.Commit(_folder, _song);
                await WaitForRequestsAsync(2);

                Assert.That(_http.Requests.Length, Is.EqualTo(2));
                Assert.That(SongStorage.GetRecommendGeneration(_endpoint), Is.EqualTo(5));
            });
        }

        /// <summary>Checks that a pending feed refresh is awaited, even when it fails, before sending.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Commit_WaitsForFeedRefresh()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var refresh = new TaskCompletionSource<bool>();

                RecommendImpressions.Commit(_folder, _song, refresh.Task);
                await UniTask.Delay(100, DelayType.Realtime);
                Assert.That(_http.Requests, Is.Empty);

                refresh.SetException(new InvalidOperationException("refresh failed"));
                await WaitForRequestsAsync(1);

                Assert.That(_http.Requests.Length, Is.EqualTo(1));
            });
        }

        /// <summary>Checks that nothing is reported for an endpoint whose user logged out.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Commit_AfterLogout_SendsNothing()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _endpoint.RuntimeConfig.IsLoggedIn = false;

                RecommendImpressions.Commit(_folder, _song);
                await UniTask.Delay(100, DelayType.Realtime);

                Assert.That(Entries.Count, Is.EqualTo(1));
                Assert.That(_http.Requests, Is.Empty);
            });
        }

        /// <summary>Checks that a song focused for the dwell time is reported, while a short focus is not.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Focus_ReportsAfterDwell()
        {
            return UniTask.ToCoroutine(async () =>
            {
                RecommendImpressions.Focus(_folder, _song);
                RecommendImpressions.Suspend();
                RecommendImpressions.Resume();
                RecommendImpressions.Blur();
                RecommendImpressions.FlushAll();
                await UniTask.Delay(50, DelayType.Realtime);
                Assert.That(_http.Requests, Is.Empty);

                RecommendImpressions.Focus(_folder, _song);
                await UniTask.Delay(1100, DelayType.Realtime);
                RecommendImpressions.Blur();
                RecommendImpressions.FlushAll();
                await WaitForRequestsAsync(1);

                Assert.That(_http.Requests.Length, Is.EqualTo(1));
                Assert.That(_http.Requests[0].Body, Does.Contain("recommended-hash"));
            });
        }

        /// <summary>Checks that focusing another folder blurs the previously focused folder.</summary>
        [Test]
        public void Focus_SwitchesFolders()
        {
            var other = new ApiEndpoint { Name = "Other", Url = new Uri("https://other.invalid/") };
            var otherFolder = new OnlineSongCollection(other, "Recommend", new ISongDetail[] { _song }) { Type = ChartStorageType.Recommend };

            RecommendImpressions.Focus(_folder, _song);
            RecommendImpressions.Focus(otherFolder, _song);

            Assert.That(Entries.Count, Is.EqualTo(2));
            Assert.That(PrivateMember.GetStaticField<object?>(typeof(RecommendImpressions), "_focused"), Is.Not.Null);
            RecommendImpressions.Blur();
            Assert.That(PrivateMember.GetStaticField<object?>(typeof(RecommendImpressions), "_focused"), Is.Null);
        }
    }
}
