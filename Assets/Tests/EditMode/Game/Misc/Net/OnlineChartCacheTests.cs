using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Net
{
    /// <summary>
    /// Covers <see cref="OnlineChartCache"/> against a runtime database in temporary storage: the table and its unique
    /// index, synchronizing digests with the info API, recording downloaded digests, listing cached charts offline and
    /// keeping the referenced objects when the object store is pruned.
    /// </summary>
    public class OnlineChartCacheTests
    {
        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the application directories and closing the databases opened by a test.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The endpoint serving the charts.</summary>
        private ApiEndpoint _endpoint = null!;

        /// <summary>The cache under test, bound to the temporary layout.</summary>
        private OnlineChartCache _cache = null!;

        /// <summary>Points the runtime database and the object store at an empty temporary layout.</summary>
        [SetUp]
        public void SetUp()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MajEnv).TypeHandle);
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _environment.UseTemporaryLayout(_temporary);
            _endpoint = new ApiEndpoint { Name = "Server", Url = new Uri("https://api.invalid/api/") };
            _cache = OnlineChartCache.Shared;
        }

        /// <summary>Closes the databases and restores the environment.</summary>
        [TearDown]
        public void TearDown()
        {
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Creates chart metadata.</summary>
        /// <param name="id">The chart identity.</param>
        /// <param name="hash">The chart hash.</param>
        /// <returns>The metadata.</returns>
        private static MajnetSongDetail Chart(string id, string hash = "hash")
        {
            return new MajnetSongDetail
            {
                Id = id,
                Title = "Title " + id,
                Artist = "Artist",
                Designer = "Designer",
                Description = string.Empty,
                Levels = new[] { "1", "2" },
                Uploader = "Uploader",
                Hash = hash,
                Timestamp = new DateTime(2024, 1, 2),
            };
        }

        /// <summary>Creates the info API's description of a file with the given content.</summary>
        /// <param name="content">The file content.</param>
        /// <returns>The file description.</returns>
        private static MajNetChartResource Resource(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            using var sha256 = SHA256.Create();
            using var md5 = MD5.Create();
            return new MajNetChartResource
            {
                Url = new Uri("https://cdn.invalid/" + content),
                Sha256 = ObjectStore.ToHex(sha256.ComputeHash(bytes)),
                Md5 = ObjectStore.ToHex(md5.ComputeHash(bytes)),
                Length = bytes.Length,
            };
        }

        /// <summary>Creates the info of a chart.</summary>
        /// <param name="id">The chart identity.</param>
        /// <param name="maidata">The chart file, or null.</param>
        /// <param name="track">The track, or null.</param>
        /// <returns>The info keyed by chart identity.</returns>
        private static Dictionary<string, MajNetChartInfo> Info(string id, MajNetChartResource? maidata, MajNetChartResource? track)
        {
            return new Dictionary<string, MajNetChartInfo>
            {
                [id] = new MajNetChartInfo
                {
                    Metadata = Chart(id),
                    Resources = new MajNetChartResources { Maidata = maidata, Track = track },
                },
            };
        }

        /// <summary>Stores an object with the given digest in the shared object store.</summary>
        /// <param name="sha256">The digest.</param>
        private static void StoreObject(string sha256)
        {
            ObjectStore.Shared.Directory.CreateDirectory(sha256.Substring(0, 2)).CreateFile(sha256).WriteAllText("object");
        }

        /// <summary>Reads one column of a chart's row after the queued writes have finished.</summary>
        /// <param name="column">The column name.</param>
        /// <param name="chartId">The chart identity.</param>
        /// <returns>The column value.</returns>
        private static async UniTask<string?> ReadColumnAsync(string column, string chartId)
        {
            var database = MajDatabase.Runtime;
            await database.WaitForWritesAsync();
            return database.Read(connection => connection.ExecuteScalar<string?>($"SELECT {column} FROM OnlineChartCache WHERE ChartId = ?", chartId));
        }

        /// <summary>Counts the rows of the table.</summary>
        /// <returns>The number of rows.</returns>
        private static int CountRows()
        {
            return MajDatabase.Runtime.Read(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM OnlineChartCache"));
        }

        /// <summary>
        /// Checks that the table is unique on the chart identity and the server, and that endpoints whose servers report
        /// no identifier are keyed by their URLs.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Table_IsUniqueOnChartIdAndServer()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await _cache.InitializeAsync();
                var indexSql = MajDatabase.Runtime.Read(connection => connection.ExecuteScalar<string>(
                    "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_OnlineChartCache_ChartId_Server'"));

                Assert.That(indexSql, Does.Contain("unique").IgnoreCase);
                Assert.That(indexSql, Does.Contain("\"ChartId\", \"Server\""));

                var other = new ApiEndpoint { Name = "Other", Url = new Uri("https://other.invalid/api/") };
                await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, null);
                await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, null);
                await _cache.SyncAsync(other, new[] { Chart("1") }, null);

                Assert.That(CountRows(), Is.EqualTo(2));
                Assert.That(_cache.ResolveServer(other, new MajNetServerInfo { Id = " " }), Is.EqualTo("https://other.invalid/api/"));
            });
        }

        /// <summary>Checks that the addresses of one server, such as reverse proxies, share one row per chart.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ResolveServer_ProxiesOfOneServerShareRows()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var proxy = new ApiEndpoint { Name = "Proxy", Url = new Uri("https://proxy.invalid/majdata/api/") };
                var serverInfo = new MajNetServerInfo { Id = "majnet", Name = "MajdataNET" };

                Assert.That(_cache.ResolveServer(_endpoint, serverInfo), Is.EqualTo("majnet"));
                Assert.That(_cache.ResolveServer(proxy, serverInfo), Is.EqualTo("majnet"));
                var direct = (await _cache.SyncAsync(_endpoint, new[] { Chart("1"), Chart("2") }, Info("1", Resource("maidata"), null)))["1"];
                var proxied = (await _cache.SyncAsync(proxy, new[] { Chart("1"), Chart("2") }, Info("1", Resource("maidata"), null)))["1"];

                Assert.That(CountRows(), Is.EqualTo(2));
                Assert.That(direct.Server, Is.EqualTo("majnet"));
                Assert.That(proxied.Server, Is.EqualTo("majnet"));
                Assert.That(proxied.TryGetDigest(OnlineChartResourceKind.Maidata, out _), Is.True);
                Assert.That(MajDatabase.Runtime.Read(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM OnlineServers WHERE Server = 'majnet'")), Is.EqualTo(2));
            });
        }

        /// <summary>
        /// Checks that rows recorded under an endpoint's URL move to the server's identifier once the server reports one,
        /// keeping the server's row of a chart both have.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ResolveServer_MovesUrlRowsToServerIdentifier()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var proxy = new ApiEndpoint { Name = "Proxy", Url = new Uri("https://proxy.invalid/majdata/api/") };
                var serverInfo = new MajNetServerInfo { Id = "majnet" };
                var kept = Resource("kept track");
                await _cache.SyncAsync(_endpoint, new[] { Chart("1"), Chart("2") }, null);
                _cache.ResolveServer(proxy, serverInfo);
                await _cache.SyncAsync(proxy, new[] { Chart("2") }, Info("2", null, kept));

                _cache.ResolveServer(_endpoint, serverInfo);

                Assert.That(CountRows(), Is.EqualTo(2));
                Assert.That(MajDatabase.Runtime.Read(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM OnlineChartCache WHERE Server = 'majnet'")), Is.EqualTo(2));
                Assert.That(await ReadColumnAsync("TrackSha256", "2"), Is.EqualTo(kept.Sha256));
            });
        }

        /// <summary>
        /// Checks that the server of an endpoint is remembered across sessions and kept when its identity cannot be
        /// fetched, so cached charts are listed offline through any address that reached the server before.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetServerKey_RemembersServerAcrossSessions()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var proxy = new ApiEndpoint { Name = "Proxy", Url = new Uri("https://proxy.invalid/majdata/api/") };
                var unknown = new ApiEndpoint { Name = "Unknown", Url = new Uri("https://unknown.invalid/api/") };
                var infos = Info("1", Resource("maidata"), Resource("track"));
                _cache.ResolveServer(_endpoint, new MajNetServerInfo { Id = "majnet" });
                _cache.ResolveServer(proxy, new MajNetServerInfo { Id = "majnet" });
                await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, infos);
                StoreObject(infos["1"].Resources!.Maidata!.Sha256);
                StoreObject(infos["1"].Resources!.Track!.Sha256);

                var nextSession = new OnlineChartCache(ObjectStore.Shared, true);

                Assert.That(nextSession.GetServerKey(proxy), Is.EqualTo("majnet"));
                Assert.That(nextSession.ResolveServer(proxy, null), Is.EqualTo("majnet"));
                Assert.That(nextSession.GetServerKey(unknown), Is.EqualTo("https://unknown.invalid/api/"));
                Assert.That(nextSession.LoadCached(proxy).Select(chart => chart.Detail.Id), Is.EqualTo(new[] { "1" }));
            });
        }

        /// <summary>
        /// Checks that the sync point of a full sync is kept per server across sessions, follows the URL rows to the
        /// server's identifier, yields to the sync point the server already has, and is kept by a sync without a range.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SyncAsync_KeepsSyncPointPerServer()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var proxy = new ApiEndpoint { Name = "Proxy", Url = new Uri("https://proxy.invalid/majdata/api/") };
                var other = new ApiEndpoint { Name = "Other", Url = new Uri("https://other.invalid/api/") };
                var serverInfo = new MajNetServerInfo { Id = "majnet" };
                var first = new DateTime(2026, 11, 1, 8, 0, 0, 123, DateTimeKind.Utc);
                var second = new DateTime(2026, 11, 2, 8, 0, 0, 456, DateTimeKind.Utc);
                Assert.That(_cache.GetSyncPoint(_endpoint), Is.Null);

                var beforeSync = DateTime.UtcNow;
                await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, null, first.ToLocalTime());
                var point = _cache.GetSyncPoint(_endpoint)!.Value;
                Assert.That(point.ChangesTo, Is.EqualTo(first));
                Assert.That(point.FullSyncAt, Is.InRange(beforeSync, DateTime.UtcNow));
                _cache.ResolveServer(_endpoint, serverInfo);

                var nextSession = new OnlineChartCache(ObjectStore.Shared, true);
                Assert.That(nextSession.GetSyncPoint(proxy), Is.Null);
                nextSession.ResolveServer(proxy, serverInfo);
                var restored = nextSession.GetSyncPoint(proxy)!.Value.ChangesTo;
                Assert.That(restored, Is.EqualTo(first));
                Assert.That(restored.Kind, Is.EqualTo(DateTimeKind.Utc));

                await nextSession.SyncAsync(proxy, new[] { Chart("1") }, null, second);
                await _cache.SyncAsync(other, new[] { Chart("1") }, null, first);
                nextSession.ResolveServer(other, serverInfo);

                Assert.That(nextSession.GetSyncPoint(_endpoint)!.Value.ChangesTo, Is.EqualTo(second));
                Assert.That(nextSession.GetSyncPoint(other)!.Value.ChangesTo, Is.EqualTo(second));
                Assert.That(MajDatabase.Runtime.Read(connection => connection.QueryScalars<string>("SELECT Server FROM OnlineChartChangeQueries").ToArray()), Is.EqualTo(new[] { "majnet" }));

                await nextSession.SyncAsync(proxy, new[] { Chart("1") }, null);
                Assert.That(nextSession.GetSyncPoint(proxy)!.Value.ChangesTo, Is.EqualTo(second));
            });
        }

        /// <summary>Checks that a cache which is not persistent records no sync point and applies no changes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SyncPoint_IgnoredWhenNotPersistent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var transient = new OnlineChartCache(
                    new ObjectStore(_temporary.Directory.CreateDirectory("TransientObjects"), _temporary.Directory.CreateDirectory("TransientPartials")),
                    false);

                await transient.SyncAsync(_endpoint, new[] { Chart("1") }, null, DateTime.UtcNow);

                Assert.That(transient.GetSyncPoint(_endpoint), Is.Null);
                Assert.That(_cache.GetSyncPoint(_endpoint), Is.Null);
                Assert.That(await transient.ApplyChangesAsync(_endpoint, Changes(DateTime.UtcNow), new Dictionary<string, MajNetChartInfo>()), Is.Null);
            });
        }

        /// <summary>Creates a complete range of chart changes.</summary>
        /// <param name="to">The end of the range.</param>
        /// <param name="changes">The kind and chart identity of every change, in order.</param>
        /// <returns>The changes.</returns>
        private static MajNetChartChanges Changes(DateTime to, params (MajNetChartChangeKind Kind, string ChartId)[] changes)
        {
            return new MajNetChartChanges
            {
                To = to,
                IsComplete = true,
                Changes = changes.Select(change => new MajNetChartChange { ChartId = change.ChartId, Kind = change.Kind, ChangedAt = to }).ToArray(),
            };
        }

        /// <summary>Lists the listed charts of a server key in list order, as the table records them.</summary>
        /// <param name="server">The server key.</param>
        /// <returns>The chart identities.</returns>
        private static string[] ListedChartIds(string server)
        {
            return MajDatabase.Runtime.Read(connection => connection.QueryScalars<string>(
                "SELECT ChartId FROM OnlineChartCache WHERE Server = ? AND IsListed = 1 ORDER BY ListOrder, Id", server).ToArray());
        }

        /// <summary>Checks that a full sync lists the charts in list order and unlists the charts no longer in the list, keeping their rows.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SyncAsync_RecordsListOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var server = OnlineChartCache.GetEndpointKey(_endpoint);
                await _cache.SyncAsync(_endpoint, new[] { Chart("1"), Chart("2"), Chart("3") }, null);
                Assert.That(ListedChartIds(server), Is.EqualTo(new[] { "1", "2", "3" }));

                await _cache.SyncAsync(_endpoint, new[] { Chart("3"), Chart("1") }, null);

                Assert.That(ListedChartIds(server), Is.EqualTo(new[] { "3", "1" }));
                Assert.That(CountRows(), Is.EqualTo(3));
            });
        }

        /// <summary>
        /// Checks that applying changes puts added charts first, updates modified charts from their info, removes deleted
        /// charts, unlists charts the info no longer knows, records the end of the range and keeps the time of the full sync.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ApplyChangesAsync_UpdatesListedCharts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var server = OnlineChartCache.GetEndpointKey(_endpoint);
                var fullSyncTo = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
                var changesTo = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
                await _cache.SyncAsync(_endpoint, new[] { Chart("1"), Chart("2"), Chart("3"), Chart("4") }, null, fullSyncTo);
                var fullSyncAt = _cache.GetSyncPoint(_endpoint)!.Value.FullSyncAt;
                var track = Resource("new track");
                var infos = Info("2", Resource("maidata"), track);
                infos["2"].Metadata!.PublicTags = new[] { "爆发" };
                foreach (var added in Info("a", Resource("a maidata"), Resource("a track")).Concat(Info("b", null, null)))
                {
                    infos.Add(added.Key, added.Value);
                }
                var changes = Changes(
                    changesTo,
                    (MajNetChartChangeKind.Added, "a"),
                    (MajNetChartChangeKind.Modified, "2"),
                    (MajNetChartChangeKind.Deleted, "3"),
                    (MajNetChartChangeKind.Modified, "4"),
                    (MajNetChartChangeKind.Added, "b"),
                    (MajNetChartChangeKind.Deleted, "unknown"));

                var listed = await _cache.ApplyChangesAsync(_endpoint, changes, infos);

                Assert.That(listed!.Select(chart => chart.Detail.Id), Is.EqualTo(new[] { "b", "a", "1", "2" }));
                Assert.That(ListedChartIds(server), Is.EqualTo(new[] { "b", "a", "1", "2" }));
                Assert.That(CountOnlineChartRows("3"), Is.Zero);
                Assert.That(CountOnlineChartRows("4"), Is.EqualTo(1));
                var modified = listed.Single(chart => chart.Detail.Id == "2");
                Assert.That(modified.Detail.PublicTags, Is.EqualTo(new[] { "爆发" }));
                Assert.That(modified.Record.TryGetDigest(OnlineChartResourceKind.Track, out var digest), Is.True);
                Assert.That(digest.Sha256, Is.EqualTo(track.Sha256));
                Assert.That(modified.Record.GetUrl(OnlineChartResourceKind.Track), Is.EqualTo(track.Url));
                var point = _cache.GetSyncPoint(_endpoint)!.Value;
                Assert.That(point.ChangesTo, Is.EqualTo(changesTo));
                Assert.That(point.FullSyncAt, Is.EqualTo(fullSyncAt));

                // A range overlapping the last one reports the same changes again, which leaves the list as it is.
                var again = await _cache.ApplyChangesAsync(_endpoint, changes, infos);
                Assert.That(again!.Select(chart => chart.Detail.Id), Is.EqualTo(new[] { "b", "a", "1", "2" }));
                Assert.Throws<ArgumentException>(() => _cache.ApplyChangesAsync(_endpoint, new MajNetChartChanges { To = changesTo }, infos).GetAwaiter().GetResult());
            });
        }

        /// <summary>
        /// Checks that a deleted chart whose chart file and track are stored is marked deleted and stays listed, both when
        /// a changes range deletes it and when it leaves the list of a full sync, and that it loses its row once its
        /// files are gone, while a deleted chart without stored files loses its row at once.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DeletedCharts_StayListedWhileStored()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var server = OnlineChartCache.GetEndpointKey(_endpoint);
                var infos = new Dictionary<string, MajNetChartInfo>();
                foreach (var id in new[] { "1", "2", "4" })
                {
                    infos[id] = Info(id, Resource(id + " maidata"), Resource(id + " track"))[id];
                }
                await _cache.SyncAsync(_endpoint, new[] { Chart("1"), Chart("2"), Chart("4") }, infos);
                foreach (var id in new[] { "1", "4" })
                {
                    StoreObject(infos[id].Resources!.Maidata!.Sha256);
                    StoreObject(infos[id].Resources!.Track!.Sha256);
                }
                var to = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

                var listed = await _cache.ApplyChangesAsync(_endpoint, Changes(to, (MajNetChartChangeKind.Deleted, "1"), (MajNetChartChangeKind.Deleted, "2")), new Dictionary<string, MajNetChartInfo>());

                Assert.That(listed!.Select(chart => chart.Detail.Id), Is.EqualTo(new[] { "1", "4" }));
                Assert.That(listed.Select(chart => chart.Record.IsDeletedOnServer), Is.EqualTo(new[] { true, false }));
                Assert.That(CountOnlineChartRows("2"), Is.Zero);

                var records = await _cache.SyncAsync(_endpoint, new[] { Chart("3") }, null);

                Assert.That(records.Keys, Is.EqualTo(new[] { "3" }));
                Assert.That(ListedChartIds(server), Is.EqualTo(new[] { "3", "1", "4" }));
                var deleted = _cache.LoadChartsDeletedOnServer(_endpoint);
                Assert.That(deleted.Select(chart => chart.Detail.Id), Is.EqualTo(new[] { "1", "4" }));
                Assert.That(deleted.All(chart => chart.Record.IsDeletedOnServer), Is.True);
                Assert.That(deleted[0].Record.TryGetDigest(OnlineChartResourceKind.Track, out _), Is.True);

                ObjectStore.Shared.Find(infos["1"].Resources!.Track!.Sha256)!.Delete();
                await _cache.SyncAsync(_endpoint, new[] { Chart("3") }, null);

                Assert.That(CountOnlineChartRows("1"), Is.Zero);
                Assert.That(_cache.LoadChartsDeletedOnServer(_endpoint).Select(chart => chart.Detail.Id), Is.EqualTo(new[] { "4" }));

                await _cache.SyncAsync(_endpoint, new[] { Chart("4"), Chart("3") }, null);

                Assert.That(_cache.LoadChartsDeletedOnServer(_endpoint), Is.Empty);
                Assert.That(ListedChartIds(server), Is.EqualTo(new[] { "4", "3" }));
            });
        }

        /// <summary>Counts the rows of a chart.</summary>
        /// <param name="chartId">The chart identity.</param>
        /// <returns>The number of rows.</returns>
        private static int CountOnlineChartRows(string chartId)
        {
            return MajDatabase.Runtime.Read(connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM OnlineChartCache WHERE ChartId = ?", chartId));
        }

        /// <summary>A row of the chart table as it was written before charts were keyed by server.</summary>
        [SQLite.Table("OnlineChartCache")]
        private sealed class LegacyChartRow
        {
            /// <summary>Gets or sets the row identity.</summary>
            [SQLite.PrimaryKey]
            [SQLite.AutoIncrement]
            public long Id { get; set; }
            /// <summary>Gets or sets the chart identity.</summary>
            [SQLite.Indexed(Name = "IX_OnlineChartCache_ChartId_Endpoint", Order = 1, Unique = true)]
            public string ChartId { get; set; } = string.Empty;
            /// <summary>Gets or sets the endpoint URL.</summary>
            [SQLite.Indexed(Name = "IX_OnlineChartCache_ChartId_Endpoint", Order = 2, Unique = true)]
            public string Endpoint { get; set; } = string.Empty;
            /// <summary>Gets or sets the MD5 digest of the chart file.</summary>
            public string? MaidataMd5 { get; set; }
            /// <summary>Gets or sets the SHA-256 digest of the chart file.</summary>
            public string? MaidataSha256 { get; set; }
        }

        /// <summary>Checks that a chart table keyed by endpoint URL is upgraded in place, keeping its rows and their files.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitializeAsync_UpgradesEndpointColumn()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var maidata = Resource("maidata");
                MajDatabase.Runtime.Write(connection =>
                {
                    connection.CreateTable<LegacyChartRow>();
                    connection.Insert(new LegacyChartRow { ChartId = "1", Endpoint = "https://api.invalid/api/", MaidataMd5 = maidata.Md5, MaidataSha256 = maidata.Sha256 });
                });

                await _cache.InitializeAsync();

                var columns = MajDatabase.Runtime.Read(connection => connection.GetTableInfo("OnlineChartCache").Select(column => column.Name).ToArray());
                var indexes = MajDatabase.Runtime.Read(connection => connection.QueryScalars<string>("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'OnlineChartCache'").ToArray());
                Assert.That(columns, Does.Contain("Server").And.Not.Contain("Endpoint"));
                Assert.That(indexes, Does.Contain("IX_OnlineChartCache_ChartId_Server").And.Not.Contain("IX_OnlineChartCache_ChartId_Endpoint"));
                Assert.That(await ReadColumnAsync("Server", "1"), Is.EqualTo("https://api.invalid/api/"));
                Assert.That(_cache.GetReferencedDigests(), Does.Contain(maidata.Sha256));

                _cache.ResolveServer(_endpoint, new MajNetServerInfo { Id = "majnet" });
                Assert.That(await ReadColumnAsync("Server", "1"), Is.EqualTo("majnet"));
            });
        }

        /// <summary>Checks that the info API's digests and direct URIs are recorded, and a missing file is known missing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SyncAsync_RecordsInfoDigests()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var maidata = Resource("maidata");
                var track = Resource("track");

                var records = await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, Info("1", maidata, track));

                var record = records["1"];
                Assert.That(record.IsResolved, Is.True);
                Assert.That(record.Server, Is.EqualTo("https://api.invalid/api/"));
                Assert.That(record.TryGetDigest(OnlineChartResourceKind.Maidata, out var maidataDigest), Is.True);
                Assert.That(maidataDigest, Is.EqualTo(new ChartResourceDigest(maidata.Md5, maidata.Sha256)));
                Assert.That(record.GetUrl(OnlineChartResourceKind.Track), Is.EqualTo(track.Url));
                Assert.That(record.IsMissing(OnlineChartResourceKind.Video), Is.True);
                Assert.That(record.IsMissing(OnlineChartResourceKind.Track), Is.False);
                Assert.That(await ReadColumnAsync("MaidataSha256", "1"), Is.EqualTo(maidata.Sha256));
                Assert.That(await ReadColumnAsync("MaidataMd5", "1"), Is.EqualTo(maidata.Md5));
                Assert.That(await ReadColumnAsync("TrackSha256", "1"), Is.EqualTo(track.Sha256));
                Assert.That(await ReadColumnAsync("VideoSha256", "1"), Is.Null);
                Assert.That(await ReadColumnAsync("Detail", "1"), Does.Contain("Title 1"));
            });
        }

        /// <summary>Checks that a file the server replaced points to its new digest after the next synchronization.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SyncAsync_UpdatesChangedDigests()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var maidata = Resource("maidata");
                await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, Info("1", maidata, Resource("old track")));

                var replaced = Resource("new track");
                var records = await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, Info("1", maidata, replaced));

                Assert.That(records["1"].TryGetDigest(OnlineChartResourceKind.Track, out var digest), Is.True);
                Assert.That(digest.Sha256, Is.EqualTo(replaced.Sha256));
                Assert.That(await ReadColumnAsync("TrackSha256", "1"), Is.EqualTo(replaced.Sha256));
                Assert.That(await ReadColumnAsync("TrackMd5", "1"), Is.EqualTo(replaced.Md5));
                Assert.That(CountRows(), Is.EqualTo(1));
            });
        }

        /// <summary>Checks that, without the info, the digests of the same chart revision are kept and those of a new revision are cleared.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SyncAsync_WithoutInfo_KeepsOnlyDigestsOfSameRevision()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var track = Resource("track");
                await _cache.SyncAsync(_endpoint, new[] { Chart("1", "first") }, Info("1", Resource("maidata"), track));

                var same = (await _cache.SyncAsync(_endpoint, new[] { Chart("1", "first") }, null))["1"];
                Assert.That(same.IsResolved, Is.True);
                Assert.That(same.TryGetDigest(OnlineChartResourceKind.Track, out var kept), Is.True);
                Assert.That(kept.Sha256, Is.EqualTo(track.Sha256));
                Assert.That(same.GetUrl(OnlineChartResourceKind.Track), Is.Null);

                var revised = (await _cache.SyncAsync(_endpoint, new[] { Chart("1", "second") }, null))["1"];
                Assert.That(revised.IsResolved, Is.False);
                Assert.That(revised.TryGetDigest(OnlineChartResourceKind.Track, out _), Is.False);
                Assert.That(revised.IsMissing(OnlineChartResourceKind.Video), Is.False);
                Assert.That(await ReadColumnAsync("TrackSha256", "1"), Is.Null);
                Assert.That(await ReadColumnAsync("ChartHash", "1"), Is.EqualTo("second"));
            });
        }

        /// <summary>Checks that a malformed digest leaves the record unresolved, so the file is downloaded instead of treated as missing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SyncAsync_MalformedDigestLeavesRecordUnresolved()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var malformed = new MajNetChartResource { Sha256 = "not-a-digest", Md5 = "md5" };

                var record = (await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, Info("1", Resource("maidata"), malformed)))["1"];

                Assert.That(record.IsResolved, Is.False);
                Assert.That(record.TryGetDigest(OnlineChartResourceKind.Maidata, out _), Is.True);
                Assert.That(record.TryGetDigest(OnlineChartResourceKind.Track, out _), Is.False);
                Assert.That(record.IsMissing(OnlineChartResourceKind.Track), Is.False);
            });
        }

        /// <summary>Checks that a downloaded file's digest is written to the row and read back by later records.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetDigest_PersistsDownloadedDigest()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var record = (await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, null))["1"];
                var track = Resource("track");

                record.SetDigest(OnlineChartResourceKind.Track, new ChartResourceDigest(track.Md5, track.Sha256));

                Assert.That(await ReadColumnAsync("TrackSha256", "1"), Is.EqualTo(track.Sha256));
                Assert.That(await ReadColumnAsync("TrackMd5", "1"), Is.EqualTo(track.Md5));
                var reread = _cache.GetOrCreateRecord(_endpoint, Chart("1"));
                Assert.That(reread.TryGetDigest(OnlineChartResourceKind.Track, out var digest), Is.True);
                Assert.That(digest.Sha256, Is.EqualTo(track.Sha256));
            });
        }

        /// <summary>Checks that a chart outside the chart list gets a row once, and that transient records never reach the database.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GetOrCreateRecord_CreatesRowAndTransientRecordsStayInMemory()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var first = _cache.GetOrCreateRecord(_endpoint, Chart("1"));
                var second = _cache.GetOrCreateRecord(_endpoint, Chart("1"));
                Assert.That(first.IsResolved, Is.False);
                Assert.That(second.ChartId, Is.EqualTo("1"));
                Assert.That(CountRows(), Is.EqualTo(1));

                var track = Resource("track");
                var transient = _cache.CreateTransientRecord(_endpoint, Chart("2"));
                transient.SetDigest(OnlineChartResourceKind.Track, new ChartResourceDigest(track.Md5, track.Sha256));
                await MajDatabase.Runtime.WaitForWritesAsync();
                Assert.That(CountRows(), Is.EqualTo(1));
                Assert.That(transient.TryGetDigest(OnlineChartResourceKind.Track, out _), Is.True);
            });
        }

        /// <summary>Checks that only charts whose chart file and track are stored are listed offline.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadCached_ListsChartsWithStoredChartAndTrack()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var infos = new Dictionary<string, MajNetChartInfo>();
                foreach (var id in new[] { "complete", "no-track", "missing-objects" })
                {
                    infos[id] = Info(id, Resource(id + " maidata"), Resource(id + " track"))[id];
                }
                await _cache.SyncAsync(_endpoint, infos.Keys.Select(id => Chart(id)).ToArray(), infos);
                StoreObject(infos["complete"].Resources!.Maidata!.Sha256);
                StoreObject(infos["complete"].Resources!.Track!.Sha256);
                StoreObject(infos["no-track"].Resources!.Maidata!.Sha256);

                var cached = _cache.LoadCached(_endpoint);

                Assert.That(cached.Select(chart => chart.Detail.Id), Is.EqualTo(new[] { "complete" }));
                Assert.That(cached[0].Detail.Title, Is.EqualTo("Title complete"));
                Assert.That(cached[0].Record.IsResolved, Is.True);
                Assert.That(_cache.LoadCached(new ApiEndpoint { Name = "Other", Url = new Uri("https://other.invalid/api/") }), Is.Empty);
            });
        }

        /// <summary>Checks that the startup scan keeps the objects rows reference and deletes the rest.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ScanAsync_KeepsReferencedObjects()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var maidata = Resource("maidata");
                var orphan = Resource("orphan");
                await _cache.SyncAsync(_endpoint, new[] { Chart("1") }, Info("1", maidata, null));
                StoreObject(maidata.Sha256);
                StoreObject(orphan.Sha256);

                Assert.That(_cache.GetReferencedDigests(), Is.EquivalentTo(new[] { maidata.Sha256 }));
                await _cache.InitializeAsync();
                await ObjectStore.Shared.ScanAsync();

                Assert.That(ObjectStore.Shared.Contains(maidata.Sha256), Is.True);
                Assert.That(ObjectStore.Shared.Contains(orphan.Sha256), Is.False);
            });
        }
    }
}
