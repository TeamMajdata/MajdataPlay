using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Utils;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>
    /// Records, in the runtime database, the digests of every online chart's files, so <see cref="OnlineSongDetail"/>
    /// reuses the files already in the <see cref="ObjectStore"/> and charts whose files are stored can be listed offline.
    /// </summary>
    /// <remarks>
    /// The table holds one row per chart and endpoint, unique on <c>(ChartId, Endpoint)</c>, with the MD5 and SHA-256
    /// digests of the chart, track, video, cover art and thumbnail, and the chart metadata of the last chart list.
    /// Every successful chart list refresh synchronizes the rows with the <c>maichart/info</c> API, so a file the
    /// server replaced points to its new object, and the cache reports the digests to the object store as references,
    /// so the objects no row points to any more are deleted at the next startup.
    /// </remarks>
    internal sealed class OnlineChartCache : IObjectReferenceSource
    {
        /// <summary>Gets the cache of the application over <see cref="MajDatabase.Runtime"/> and <see cref="ObjectStore.Shared"/>, creating it on first access.</summary>
        /// <exception cref="IOException">The object store directory could not be created.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating the object store directory was denied.</exception>
        public static OnlineChartCache Shared
        {
            get
            {
                lock (s_sharedLock)
                {
                    return s_shared ??= new OnlineChartCache(ObjectStore.Shared, true);
                }
            }
        }

        /// <summary>Gets the store holding the charts' files.</summary>
        public ObjectStore Objects { get; }
        /// <summary>Gets whether the records are kept in the runtime database; otherwise they only live in memory.</summary>
        public bool IsPersistent { get; }

        /// <summary>The name of the table in the runtime database.</summary>
        private const string TableName = "OnlineChartCache";
        /// <summary>The name of the unique index on the chart identity and the endpoint.</summary>
        private const string ChartIndexName = "IX_OnlineChartCache_ChartId_Endpoint";
        /// <summary>Every resource kind, in declaration order.</summary>
        private static readonly OnlineChartResourceKind[] s_kinds = (OnlineChartResourceKind[])Enum.GetValues(typeof(OnlineChartResourceKind));

        /// <summary>Guards <see cref="s_shared"/>.</summary>
        private static readonly object s_sharedLock = new();
        /// <summary>Stores the cache of the application, or null before the first access.</summary>
        private static OnlineChartCache? s_shared;
        /// <summary>Records whether the table has been created in the runtime database.</summary>
        private int _isTableReady;

        /// <summary>Creates a cache.</summary>
        /// <param name="objects">The store holding the charts' files.</param>
        /// <param name="isPersistent">
        /// Whether the records are kept in <see cref="MajDatabase.Runtime"/>; a persistent cache registers its digests as
        /// references of <paramref name="objects"/>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="objects"/> is null.</exception>
        public OnlineChartCache(ObjectStore objects, bool isPersistent)
        {
            Objects = objects ?? throw new ArgumentNullException(nameof(objects));
            IsPersistent = isPersistent;
            if (isPersistent)
            {
                objects.AddReferenceSource(this);
            }
        }

        /// <summary>Gets the key identifying an endpoint in the table.</summary>
        /// <param name="api">The endpoint.</param>
        /// <returns>The absolute URI of the endpoint.</returns>
        public static string GetEndpointKey(ApiEndpoint api)
        {
            return api.Url.AbsoluteUri;
        }

        /// <summary>Creates the table in the runtime database on the thread pool, unless it already exists.</summary>
        /// <param name="token">The token canceling the start of the operation.</param>
        /// <returns>A task completing when the table exists.</returns>
        /// <exception cref="OperationCanceledException">The operation was canceled before it started.</exception>
        /// <exception cref="SQLiteException">The table could not be created.</exception>
        public Task InitializeAsync(CancellationToken token = default)
        {
            return Task.Run(EnsureTable, token);
        }

        /// <summary>Synchronizes the records of an endpoint's chart list with the info API's digests.</summary>
        /// <param name="api">The endpoint which served the chart list.</param>
        /// <param name="charts">The charts of the list.</param>
        /// <param name="infos">The info of the charts keyed by chart identity, or null when the info could not be fetched.</param>
        /// <returns>The record of every chart with an identity, keyed by chart identity.</returns>
        /// <remarks>
        /// A chart with info gets the info's digests, replacing the digests of files the server changed. A chart without
        /// info keeps its recorded digests while its chart hash is unchanged; otherwise its digests are cleared, so its
        /// files are downloaded from the chart endpoint again. A database failure is logged and the records then only
        /// live in memory.
        /// </remarks>
        public async Task<Dictionary<string, OnlineChartRecord>> SyncAsync(ApiEndpoint api, ReadOnlyMemory<MajnetSongDetail> charts, IReadOnlyDictionary<string, MajNetChartInfo>? infos)
        {
            var endpoint = GetEndpointKey(api);
            var records = new Dictionary<string, OnlineChartRecord>(StringComparer.Ordinal);
            if (IsPersistent)
            {
                try
                {
                    EnsureTable();
                    await MajDatabase.Runtime.WriteAsync(connection =>
                    {
                        var rows = new Dictionary<string, OnlineChartRow>(StringComparer.Ordinal);
                        foreach (var row in connection.Table<OnlineChartRow>().Where(row => row.Endpoint == endpoint))
                        {
                            rows[row.ChartId] = row;
                        }
                        foreach (var chart in charts.Span)
                        {
                            if (string.IsNullOrWhiteSpace(chart.Id) || records.ContainsKey(chart.Id))
                            {
                                continue;
                            }
                            var info = FindInfo(infos, chart.Id);
                            var isNew = !rows.TryGetValue(chart.Id, out var row);
                            row ??= new OnlineChartRow
                            {
                                ChartId = chart.Id,
                                Endpoint = endpoint
                            };
                            if (Apply(row, chart, info))
                            {
                                if (isNew)
                                {
                                    connection.Insert(row);
                                }
                                else
                                {
                                    connection.Update(row);
                                }
                            }
                            records[chart.Id] = CreateRecord(row, info, true);
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    MajDebug.LogError(nameof(OnlineChartCache), $"Failed to record the chart files of {api.Name}: {e}");
                    records.Clear();
                }
            }
            AddTransientRecords(records, api, charts.Span, infos);
            return records;
        }

        /// <summary>Gives every chart with an identity but without a record one that only lives in memory.</summary>
        /// <param name="records">The records keyed by chart identity, which receives the missing records.</param>
        /// <param name="api">The endpoint serving the charts.</param>
        /// <param name="charts">The charts.</param>
        /// <param name="infos">The info of the charts keyed by chart identity, or null when it is not known.</param>
        private void AddTransientRecords(Dictionary<string, OnlineChartRecord> records, ApiEndpoint api, ReadOnlySpan<MajnetSongDetail> charts, IReadOnlyDictionary<string, MajNetChartInfo>? infos)
        {
            foreach (var chart in charts)
            {
                if (!string.IsNullOrWhiteSpace(chart.Id) && !records.ContainsKey(chart.Id))
                {
                    records[chart.Id] = CreateTransientRecord(api, chart, FindInfo(infos, chart.Id));
                }
            }
        }

        /// <summary>Gets the record of a chart outside the chart list, such as a recommended chart, creating its row when needed.</summary>
        /// <param name="api">The endpoint serving the chart.</param>
        /// <param name="chart">The chart.</param>
        /// <returns>The record; a chart without identity, or a failed database access, gets a record that only lives in memory.</returns>
        public OnlineChartRecord GetOrCreateRecord(ApiEndpoint api, MajnetSongDetail chart)
        {
            if (!IsPersistent || string.IsNullOrWhiteSpace(chart.Id))
            {
                return CreateTransientRecord(api, chart);
            }
            var endpoint = GetEndpointKey(api);
            var chartId = chart.Id;
            try
            {
                EnsureTable();
                var record = default(OnlineChartRecord);
                MajDatabase.Runtime.Write(connection =>
                {
                    var row = connection.Table<OnlineChartRow>()
                                        .Where(x => x.ChartId == chartId && x.Endpoint == endpoint)
                                        .FirstOrDefault();
                    var isNew = row is null;
                    row ??= new OnlineChartRow
                    {
                        ChartId = chartId,
                        Endpoint = endpoint
                    };
                    if (Apply(row, chart, null))
                    {
                        if (isNew)
                        {
                            connection.Insert(row);
                        }
                        else
                        {
                            connection.Update(row);
                        }
                    }
                    record = CreateRecord(row, null, true);
                });
                return record!;
            }
            catch (Exception e)
            {
                MajDebug.LogError(nameof(OnlineChartCache), $"Failed to record the chart files of {chartId}: {e}");
                return CreateTransientRecord(api, chart);
            }
        }

        /// <summary>Creates a record that only lives in memory and is never written to the database.</summary>
        /// <param name="api">The endpoint serving the chart.</param>
        /// <param name="chart">The chart.</param>
        /// <param name="info">The info of the chart, or null when it is not known.</param>
        /// <returns>The record, resolved when the info has resources.</returns>
        public OnlineChartRecord CreateTransientRecord(ApiEndpoint api, MajnetSongDetail chart, MajNetChartInfo? info = null)
        {
            var row = new OnlineChartRow
            {
                ChartId = chart.Id ?? string.Empty,
                Endpoint = GetEndpointKey(api)
            };
            Apply(row, chart, info);
            return CreateRecord(row, info, false);
        }

        /// <summary>Loads the charts of an endpoint whose chart and track are stored, for listing them while the endpoint is unreachable.</summary>
        /// <param name="api">The endpoint.</param>
        /// <returns>The metadata and record of every cached chart, in the order the charts were first recorded.</returns>
        /// <remarks>Run this off the main thread; rows whose metadata cannot be read are logged and skipped.</remarks>
        /// <exception cref="SQLiteException">The table could not be read.</exception>
        public IReadOnlyList<(MajnetSongDetail Detail, OnlineChartRecord Record)> LoadCached(ApiEndpoint api)
        {
            var charts = new List<(MajnetSongDetail, OnlineChartRecord)>();
            if (!IsPersistent)
            {
                return charts;
            }
            EnsureTable();
            var endpoint = GetEndpointKey(api);
            var rows = MajDatabase.Runtime.Read(connection => connection.Table<OnlineChartRow>()
                                                                        .Where(row => row.Endpoint == endpoint)
                                                                        .OrderBy(row => row.Id)
                                                                        .ToList());
            foreach (var row in rows)
            {
                // A chart can be played once its chart and track are stored; the other files are optional.
                if (!Objects.Contains(row.MaidataSha256) || !Objects.Contains(row.TrackSha256))
                {
                    continue;
                }
                if (!Serializer.Json.TryDeserialize<MajnetSongDetail>(row.Detail, out var detail, out var exception) || detail is null)
                {
                    MajDebug.LogWarning(nameof(OnlineChartCache), $"Skipped the cached chart {row.ChartId}, whose metadata cannot be read: {exception?.Message}");
                    continue;
                }
                charts.Add((detail, CreateRecord(row, null, true)));
            }
            return charts;
        }

        /// <summary>Gets every SHA-256 digest the table records, so the object store keeps the objects charts still use.</summary>
        /// <returns>The recorded digests.</returns>
        /// <exception cref="SQLiteException">The table could not be read.</exception>
        public IEnumerable<string> GetReferencedDigests()
        {
            if (!IsPersistent)
            {
                return Array.Empty<string>();
            }
            EnsureTable();
            return MajDatabase.Runtime.Read(static connection =>
            {
                var digests = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in connection.Table<OnlineChartRow>())
                {
                    foreach (var kind in s_kinds)
                    {
                        if (GetDigest(row, kind) is { } digest)
                        {
                            digests.Add(digest.Sha256);
                        }
                    }
                }
                return digests;
            });
        }

        /// <summary>Queues writing a downloaded file's digest to the row of its chart.</summary>
        /// <param name="record">The record of the chart.</param>
        /// <param name="kind">The resource kind.</param>
        /// <param name="digest">The digest of the downloaded file.</param>
        /// <remarks>A failure is logged; the write is dropped once the runtime database has been closed for shutdown.</remarks>
        internal void QueueDigestWrite(OnlineChartRecord record, OnlineChartResourceKind kind, ChartResourceDigest digest)
        {
            var (md5Column, sha256Column) = GetColumns(kind);
            var sql = $"UPDATE {TableName} SET {md5Column} = ?, {sha256Column} = ?, {nameof(OnlineChartRow.UpdatedAtTicks)} = ? " +
                      $"WHERE {nameof(OnlineChartRow.ChartId)} = ? AND {nameof(OnlineChartRow.Endpoint)} = ?";
            var updatedAt = DateTime.UtcNow.Ticks;
            try
            {
                var database = MajDatabase.Runtime;
                ObserveWriteAsync(database.WriteAsync(connection => connection.Execute(sql, digest.Md5, digest.Sha256, updatedAt, record.ChartId, record.Endpoint))).Forget();
            }
            catch (ObjectDisposedException)
            {
                // The runtime database has been closed for shutdown; the digest is learned again by the next download.
            }
            catch (Exception e)
            {
                // The downloaded file is still usable; only the record of its digest is lost.
                MajDebug.LogException(nameof(OnlineChartCache), e);
            }
        }

        /// <summary>Creates the table and its unique index once.</summary>
        /// <exception cref="SQLiteException">The table could not be created.</exception>
        private void EnsureTable()
        {
            if (Volatile.Read(ref _isTableReady) != 0)
            {
                return;
            }
            MajDatabase.Runtime.Write(static connection => connection.CreateTable<OnlineChartRow>());
            Volatile.Write(ref _isTableReady, 1);
        }

        /// <summary>Logs the failure of a queued write instead of leaving it unobserved.</summary>
        /// <param name="write">The queued write.</param>
        /// <returns>A task completing, without faulting, when the write has finished or failed.</returns>
        private static async UniTask ObserveWriteAsync(Task write)
        {
            try
            {
                await write.ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // The database closed for shutdown before the write ran.
            }
            catch (Exception e)
            {
                MajDebug.LogException(nameof(OnlineChartCache), e);
            }
        }

        /// <summary>Finds the info of a chart.</summary>
        /// <param name="infos">The info keyed by chart identity, or null.</param>
        /// <param name="chartId">The chart identity.</param>
        /// <returns>The info, or null when it is not known.</returns>
        private static MajNetChartInfo? FindInfo(IReadOnlyDictionary<string, MajNetChartInfo>? infos, string? chartId)
        {
            if (infos is null || chartId is null)
            {
                return null;
            }
            return infos.TryGetValue(chartId, out var info) ? info : null;
        }

        /// <summary>Updates a row with a chart's metadata and, when known, the info's digests.</summary>
        /// <param name="row">The row, new or read from the table.</param>
        /// <param name="chart">The chart metadata of the chart list.</param>
        /// <param name="info">The info of the chart, or null when it is not known.</param>
        /// <returns>True when the row changed and has to be written; otherwise false.</returns>
        private static bool Apply(OnlineChartRow row, MajnetSongDetail chart, MajNetChartInfo? info)
        {
            var isChanged = false;
            var detail = Serializer.Json.Serialize(chart);
            if (row.Detail != detail)
            {
                row.Detail = detail;
                isChanged = true;
            }
            var chartHash = chart.Hash ?? string.Empty;
            var resources = info?.Resources;
            if (resources is not null)
            {
                var isResolved = true;
                foreach (var kind in s_kinds)
                {
                    var digest = ReadDigest(resources.Get(kind), out var isValid);
                    // A malformed digest leaves the file unknown, so it is downloaded from the chart endpoint instead of skipped.
                    isResolved &= isValid;
                    var recorded = GetDigest(row, kind);
                    if (recorded == digest)
                    {
                        continue;
                    }
                    if (recorded is { } previous && digest is { } current)
                    {
                        MajDebug.LogInfo(nameof(OnlineChartCache), $"The {kind} of chart {row.ChartId} changed on the server: {previous.Sha256} -> {current.Sha256}");
                    }
                    SetDigest(row, kind, digest);
                    isChanged = true;
                }
                if (row.IsResolved != isResolved)
                {
                    row.IsResolved = isResolved;
                    isChanged = true;
                }
            }
            else if (row.ChartHash != chartHash)
            {
                // Without the info, the recorded digests may belong to the previous revision of the chart.
                foreach (var kind in s_kinds)
                {
                    SetDigest(row, kind, null);
                }
                row.IsResolved = false;
                isChanged = true;
            }
            if (row.ChartHash != chartHash)
            {
                row.ChartHash = chartHash;
                isChanged = true;
            }
            if (isChanged)
            {
                row.UpdatedAtTicks = DateTime.UtcNow.Ticks;
            }
            return isChanged;
        }

        /// <summary>Reads the digests of a file reported by the info API.</summary>
        /// <param name="resource">The file, or null when the server does not store it.</param>
        /// <param name="isValid">Set to false when the file has a malformed digest; otherwise true.</param>
        /// <returns>The lowercase digests, or null when the file is missing or its digests are malformed.</returns>
        private static ChartResourceDigest? ReadDigest(MajNetChartResource? resource, out bool isValid)
        {
            isValid = true;
            if (resource is null)
            {
                return null;
            }
            var sha256 = resource.Sha256?.ToLowerInvariant();
            var md5 = resource.Md5?.ToLowerInvariant();
            if (!ObjectStore.IsDigest(sha256) || string.IsNullOrEmpty(md5))
            {
                isValid = false;
                return null;
            }
            return new ChartResourceDigest(md5!, sha256!);
        }

        /// <summary>Creates the in-memory record of a row.</summary>
        /// <param name="row">The row.</param>
        /// <param name="info">The info of this session, which supplies the direct download URIs, or null.</param>
        /// <param name="isPersistent">Whether digests learned later are written to the row.</param>
        /// <returns>The record.</returns>
        private OnlineChartRecord CreateRecord(OnlineChartRow row, MajNetChartInfo? info, bool isPersistent)
        {
            var record = new OnlineChartRecord(this, row.Endpoint, row.ChartId, row.IsResolved, isPersistent);
            foreach (var kind in s_kinds)
            {
                record.Load(kind, GetDigest(row, kind), info?.Resources?.Get(kind)?.Url);
            }
            return record;
        }

        /// <summary>Gets the recorded digest of a file from a row.</summary>
        /// <param name="row">The row.</param>
        /// <param name="kind">The resource kind.</param>
        /// <returns>The digest, or null when none is recorded.</returns>
        private static ChartResourceDigest? GetDigest(OnlineChartRow row, OnlineChartResourceKind kind)
        {
            var (md5, sha256) = kind switch
            {
                OnlineChartResourceKind.Maidata => (row.MaidataMd5, row.MaidataSha256),
                OnlineChartResourceKind.Track => (row.TrackMd5, row.TrackSha256),
                OnlineChartResourceKind.Video => (row.VideoMd5, row.VideoSha256),
                OnlineChartResourceKind.CoverArt => (row.CoverArtMd5, row.CoverArtSha256),
                OnlineChartResourceKind.Thumbnail => (row.ThumbnailMd5, row.ThumbnailSha256),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
            if (md5 is null || sha256 is null)
            {
                return null;
            }
            return new ChartResourceDigest(md5, sha256);
        }

        /// <summary>Sets the digest of a file in a row.</summary>
        /// <param name="row">The row.</param>
        /// <param name="kind">The resource kind.</param>
        /// <param name="digest">The digest, or null to clear it.</param>
        private static void SetDigest(OnlineChartRow row, OnlineChartResourceKind kind, ChartResourceDigest? digest)
        {
            var md5 = digest?.Md5;
            var sha256 = digest?.Sha256;
            switch (kind)
            {
                case OnlineChartResourceKind.Maidata:
                    row.MaidataMd5 = md5;
                    row.MaidataSha256 = sha256;
                    break;
                case OnlineChartResourceKind.Track:
                    row.TrackMd5 = md5;
                    row.TrackSha256 = sha256;
                    break;
                case OnlineChartResourceKind.Video:
                    row.VideoMd5 = md5;
                    row.VideoSha256 = sha256;
                    break;
                case OnlineChartResourceKind.CoverArt:
                    row.CoverArtMd5 = md5;
                    row.CoverArtSha256 = sha256;
                    break;
                case OnlineChartResourceKind.Thumbnail:
                    row.ThumbnailMd5 = md5;
                    row.ThumbnailSha256 = sha256;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>Gets the names of the digest columns of a resource kind.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <returns>The MD5 and SHA-256 column names.</returns>
        private static (string Md5Column, string Sha256Column) GetColumns(OnlineChartResourceKind kind)
        {
            return kind switch
            {
                OnlineChartResourceKind.Maidata => (nameof(OnlineChartRow.MaidataMd5), nameof(OnlineChartRow.MaidataSha256)),
                OnlineChartResourceKind.Track => (nameof(OnlineChartRow.TrackMd5), nameof(OnlineChartRow.TrackSha256)),
                OnlineChartResourceKind.Video => (nameof(OnlineChartRow.VideoMd5), nameof(OnlineChartRow.VideoSha256)),
                OnlineChartResourceKind.CoverArt => (nameof(OnlineChartRow.CoverArtMd5), nameof(OnlineChartRow.CoverArtSha256)),
                OnlineChartResourceKind.Thumbnail => (nameof(OnlineChartRow.ThumbnailMd5), nameof(OnlineChartRow.ThumbnailSha256)),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        /// <summary>One chart of one endpoint, with the digests of its files.</summary>
        [Table(TableName)]
        private sealed class OnlineChartRow
        {
            /// <summary>Gets or sets the row identity, which keeps the order charts were first recorded in.</summary>
            [PrimaryKey]
            [AutoIncrement]
            public long Id { get; set; }
            /// <summary>Gets or sets the server-assigned chart identity; unique together with <see cref="Endpoint"/>.</summary>
            [Indexed(Name = ChartIndexName, Order = 1, Unique = true)]
            public string ChartId { get; set; } = string.Empty;
            /// <summary>Gets or sets the endpoint key; unique together with <see cref="ChartId"/>.</summary>
            [Indexed(Name = ChartIndexName, Order = 2, Unique = true)]
            public string Endpoint { get; set; } = string.Empty;
            /// <summary>Gets or sets the chart hash of the chart list the row was last synchronized with.</summary>
            public string ChartHash { get; set; } = string.Empty;
            /// <summary>Gets or sets the chart metadata of the chart list as JSON, which lists the chart offline.</summary>
            public string Detail { get; set; } = string.Empty;
            /// <summary>Gets or sets whether the digests came from the info API, so a missing digest means a missing file.</summary>
            public bool IsResolved { get; set; }
            /// <summary>Gets or sets the MD5 digest of the chart file.</summary>
            public string? MaidataMd5 { get; set; }
            /// <summary>Gets or sets the SHA-256 digest of the chart file.</summary>
            public string? MaidataSha256 { get; set; }
            /// <summary>Gets or sets the MD5 digest of the track.</summary>
            public string? TrackMd5 { get; set; }
            /// <summary>Gets or sets the SHA-256 digest of the track.</summary>
            public string? TrackSha256 { get; set; }
            /// <summary>Gets or sets the MD5 digest of the background video.</summary>
            public string? VideoMd5 { get; set; }
            /// <summary>Gets or sets the SHA-256 digest of the background video.</summary>
            public string? VideoSha256 { get; set; }
            /// <summary>Gets or sets the MD5 digest of the original cover image.</summary>
            public string? CoverArtMd5 { get; set; }
            /// <summary>Gets or sets the SHA-256 digest of the original cover image.</summary>
            public string? CoverArtSha256 { get; set; }
            /// <summary>Gets or sets the MD5 digest of the compressed cover image.</summary>
            public string? ThumbnailMd5 { get; set; }
            /// <summary>Gets or sets the SHA-256 digest of the compressed cover image.</summary>
            public string? ThumbnailSha256 { get; set; }
            /// <summary>Gets or sets when the row last changed, in UTC ticks.</summary>
            public long UpdatedAtTicks { get; set; }
        }
    }
}
