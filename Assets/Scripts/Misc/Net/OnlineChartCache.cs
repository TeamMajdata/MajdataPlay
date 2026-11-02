using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Net.Models;
using MajdataPlay.Utils;
using SQLite;
using System;
using System.Collections.Concurrent;
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
    /// The <c>OnlineChartCache</c> table holds one row per chart and server, unique on <c>(ChartId, Server)</c>, with
    /// the MD5 and SHA-256 digests of the chart, track, video, cover art and thumbnail, and the chart metadata of the
    /// last chart list. The server key is the identifier the server reports through <c>GET info</c>, so every address
    /// of one server, such as a reverse proxy, shares one row per chart; a server reporting no identifier is keyed by
    /// its endpoint's URL. The <c>OnlineServers</c> table remembers the server key of every endpoint URL, so cached
    /// charts are listed offline through any address that reached the server before. The <c>OnlineChartChangeQueries</c>
    /// table remembers, per server key, the end of the last <c>maichart/changes</c> range the chart list was brought up
    /// to, from which the next query continues, and when the whole list was last read. Every row records whether its
    /// chart is listed and where, so a chart list refreshed from changes alone is listed from the table.
    /// <para>
    /// Every successful chart list refresh synchronizes the rows with the <c>maichart/info</c> API, so a file the
    /// server replaced points to its new object, and the cache reports the digests to the object store as references,
    /// so the objects no row points to any more are deleted at the next startup.
    /// </para>
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

        /// <summary>The name of the chart table in the runtime database.</summary>
        private const string TableName = "OnlineChartCache";
        /// <summary>The name of the table mapping endpoint URLs to server keys.</summary>
        private const string ServerTableName = "OnlineServers";
        /// <summary>The name of the table holding the end of the last chart changes query of every server.</summary>
        private const string ChangeQueryTableName = "OnlineChartChangeQueries";
        /// <summary>The name of the unique index on the chart identity and the server.</summary>
        private const string ChartIndexName = "IX_OnlineChartCache_ChartId_Server";
        /// <summary>The column that held the endpoint URL before charts were keyed by server.</summary>
        private const string LegacyEndpointColumnName = "Endpoint";
        /// <summary>The unique index on the chart identity and the endpoint URL before charts were keyed by server.</summary>
        private const string LegacyChartIndexName = "IX_OnlineChartCache_ChartId_Endpoint";
        /// <summary>Every resource kind, in declaration order.</summary>
        private static readonly OnlineChartResourceKind[] s_kinds = (OnlineChartResourceKind[])Enum.GetValues(typeof(OnlineChartResourceKind));

        /// <summary>Guards <see cref="s_shared"/>.</summary>
        private static readonly object s_sharedLock = new();
        /// <summary>Stores the cache of the application, or null before the first access.</summary>
        private static OnlineChartCache? s_shared;
        /// <summary>Records whether the tables have been created in the runtime database.</summary>
        private int _isTableReady;
        /// <summary>Stores the server key of every endpoint URL resolved or looked up in this session.</summary>
        private readonly ConcurrentDictionary<string, string> _serverKeys = new(StringComparer.Ordinal);

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

        /// <summary>Gets the key identifying an endpoint address.</summary>
        /// <param name="api">The endpoint.</param>
        /// <returns>The absolute URI of the endpoint.</returns>
        public static string GetEndpointKey(ApiEndpoint api)
        {
            return api.Url.AbsoluteUri;
        }

        /// <summary>Resolves the server an endpoint reaches from the identity it reported, and remembers it for later sessions.</summary>
        /// <param name="api">The endpoint.</param>
        /// <param name="serverInfo">The identity reported by <c>GET info</c>, or null when it could not be fetched.</param>
        /// <returns>The server key: the server's identifier, or the endpoint's URL when the server reports none.</returns>
        /// <remarks>
        /// Without a reported identity, the key remembered for the endpoint is kept. Once the server reports an
        /// identifier, the rows recorded under the endpoint's URL move to it, so the addresses of one server share one
        /// row per chart. A database failure is logged and the key then only lives in memory.
        /// </remarks>
        public string ResolveServer(ApiEndpoint api, MajNetServerInfo? serverInfo)
        {
            if (serverInfo is null)
            {
                return GetServerKey(api);
            }
            var endpoint = GetEndpointKey(api);
            var server = string.IsNullOrWhiteSpace(serverInfo.Id) ? endpoint : serverInfo.Id;
            _serverKeys[endpoint] = server;
            if (!IsPersistent)
            {
                return server;
            }
            try
            {
                EnsureTable();
                var updatedAt = DateTime.UtcNow.Ticks;
                MajDatabase.Runtime.Write(connection =>
                {
                    connection.InsertOrReplace(new OnlineServerRow
                    {
                        Endpoint = endpoint,
                        Server = server,
                        UpdatedAtTicks = updatedAt
                    });
                    if (server != endpoint)
                    {
                        // Rows recorded under the URL belong to this endpoint alone; a chart the server already has a row for keeps that row.
                        connection.Execute($"UPDATE OR IGNORE {TableName} SET {nameof(OnlineChartRow.Server)} = ? WHERE {nameof(OnlineChartRow.Server)} = ?", server, endpoint);
                        connection.Execute($"DELETE FROM {TableName} WHERE {nameof(OnlineChartRow.Server)} = ?", endpoint);
                        // The changes query continues where the rows moved with it left off, unless the server already has its own.
                        connection.Execute($"UPDATE OR IGNORE {ChangeQueryTableName} SET {nameof(ChangeQueryRow.Server)} = ? WHERE {nameof(ChangeQueryRow.Server)} = ?", server, endpoint);
                        connection.Execute($"DELETE FROM {ChangeQueryTableName} WHERE {nameof(ChangeQueryRow.Server)} = ?", endpoint);
                    }
                });
            }
            catch (Exception e)
            {
                MajDebug.LogError(nameof(OnlineChartCache), $"Failed to record the server of {api.Name}: {e}");
            }
            return server;
        }

        /// <summary>Gets the server key of an endpoint: the one resolved in this session, else the one remembered from an earlier session, else its URL.</summary>
        /// <param name="api">The endpoint.</param>
        /// <returns>The server key.</returns>
        /// <remarks>A database failure is logged and the URL is used.</remarks>
        public string GetServerKey(ApiEndpoint api)
        {
            var endpoint = GetEndpointKey(api);
            if (_serverKeys.TryGetValue(endpoint, out var server))
            {
                return server;
            }
            server = endpoint;
            if (IsPersistent)
            {
                try
                {
                    EnsureTable();
                    server = MajDatabase.Runtime.Read(connection => connection.Find<OnlineServerRow>(endpoint))?.Server ?? endpoint;
                }
                catch (Exception e)
                {
                    MajDebug.LogError(nameof(OnlineChartCache), $"Failed to read the server of {api.Name}: {e}");
                }
            }
            return _serverKeys.GetOrAdd(endpoint, server);
        }

        /// <summary>Gets where the cached chart list of the server an endpoint reaches was last brought up to.</summary>
        /// <param name="api">The endpoint.</param>
        /// <returns>
        /// The sync point recorded by the last <see cref="SyncAsync"/> or <see cref="ApplyChangesAsync"/> that was given a
        /// changes range; null when none is recorded, the cache is not persistent or the database could not be read.
        /// </returns>
        /// <remarks>Run this off the main thread; a database failure is logged.</remarks>
        public OnlineChartSyncPoint? GetSyncPoint(ApiEndpoint api)
        {
            if (!IsPersistent)
            {
                return null;
            }
            var server = GetServerKey(api);
            try
            {
                EnsureTable();
                var row = MajDatabase.Runtime.Read(connection => connection.Find<ChangeQueryRow>(server));
                if (row is null)
                {
                    return null;
                }
                return new OnlineChartSyncPoint(new DateTime(row.LastQueryToTicks, DateTimeKind.Utc), new DateTime(row.LastFullSyncTicks, DateTimeKind.Utc));
            }
            catch (Exception e)
            {
                MajDebug.LogError(nameof(OnlineChartCache), $"Failed to read the chart list sync point of {api.Name}: {e}");
                return null;
            }
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
        /// <param name="changesTo">
        /// The <see cref="MajNetChartChanges.To"/> of a <c>maichart/changes</c> query sent before the list was read, which
        /// becomes the sync point of the server together with this full sync; null to leave the sync point unchanged.
        /// </param>
        /// <returns>The record of every chart with an identity, keyed by chart identity.</returns>
        /// <remarks>
        /// A chart with info gets the info's digests, replacing the digests of files the server changed. A chart without
        /// info keeps its recorded digests while its chart hash is unchanged; otherwise its digests are cleared, so its
        /// files are downloaded from the chart endpoint again. The charts of the list are marked listed in list order. A
        /// listed chart that left the list was deleted: when its chart file and track are stored it is marked deleted and
        /// stays listed after the list, read back with <see cref="LoadChartsDeletedOnServer"/>. A deleted chart whose files are no
        /// longer stored loses its row, and the other charts of the server are unlisted. A database failure is logged
        /// and the records then only live in memory.
        /// </remarks>
        public async Task<Dictionary<string, OnlineChartRecord>> SyncAsync(ApiEndpoint api, ReadOnlyMemory<MajnetSongDetail> charts, IReadOnlyDictionary<string, MajNetChartInfo>? infos, DateTime? changesTo = null)
        {
            var server = GetServerKey(api);
            var records = new Dictionary<string, OnlineChartRecord>(StringComparer.Ordinal);
            if (IsPersistent)
            {
                try
                {
                    EnsureTable();
                    await MajDatabase.Runtime.WriteAsync(connection =>
                    {
                        var rows = new Dictionary<string, OnlineChartRow>(StringComparer.Ordinal);
                        foreach (var row in connection.Table<OnlineChartRow>().Where(row => row.Server == server))
                        {
                            rows[row.ChartId] = row;
                        }
                        var listOrder = 0L;
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
                                Server = server
                            };
                            var isChanged = Apply(row, chart, info);
                            isChanged |= SetListed(row, listOrder++);
                            if (isChanged)
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
                        var missingRows = rows.Values.Where(row => !records.ContainsKey(row.ChartId) && (row.IsListed || row.IsDeletedOnServer))
                                                     .OrderBy(row => row.ListOrder)
                                                     .ThenBy(row => row.Id)
                                                     .ToList();
                        foreach (var row in missingRows)
                        {
                            var isPlayable = HasPlayableFiles(row);
                            if (row.IsListed && isPlayable)
                            {
                                // A listed chart that left the list was deleted; its stored files keep it listed after the server's charts.
                                row.IsDeletedOnServer = true;
                                SetListed(row, listOrder++);
                                row.UpdatedAtTicks = DateTime.UtcNow.Ticks;
                                connection.Update(row);
                            }
                            else if (row.IsDeletedOnServer && !isPlayable)
                            {
                                // The stored files that kept the deleted chart are gone, so nothing plays it any more.
                                connection.Delete(row);
                            }
                            else if (row.IsListed)
                            {
                                // A row outside the list keeps its digests, so a chart reached another way, such as a recommendation, keeps its files.
                                row.IsListed = false;
                                row.UpdatedAtTicks = DateTime.UtcNow.Ticks;
                                connection.Update(row);
                            }
                        }
                        if (changesTo is { } to)
                        {
                            connection.InsertOrReplace(new ChangeQueryRow
                            {
                                Server = server,
                                LastQueryToTicks = ToUtcTicks(to),
                                LastFullSyncTicks = DateTime.UtcNow.Ticks,
                                UpdatedAtTicks = DateTime.UtcNow.Ticks
                            });
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

        /// <summary>
        /// Applies a complete <c>maichart/changes</c> range to the listed charts of the server an endpoint reaches, records
        /// the end of the range as the server's sync point, and lists the charts afterwards.
        /// </summary>
        /// <param name="api">The endpoint which answered the query.</param>
        /// <param name="changes">The changes; <see cref="MajNetChartChanges.IsComplete"/> must be true.</param>
        /// <param name="infos">The info of every added or modified chart keyed by chart identity, as the info API returned it.</param>
        /// <returns>
        /// The metadata and record of every listed chart in list order, or null when the cache is not persistent or the
        /// database could not be written, in which case nothing changed.
        /// </returns>
        /// <remarks>
        /// A deleted chart whose chart file and track are stored is marked deleted and keeps its row and its place, so it
        /// stays playable; any other deleted chart loses its row, so the objects of its files are released. An added or
        /// modified chart takes its metadata and digests from its info, which also clears a deletion mark, and is listed,
        /// a chart that was not listed going before every listed chart. One without info was deleted after the range: it
        /// is unlisted unless its chart file and track are stored, and its deletion arrives with a later range. The time
        /// of the last full sync is kept. A database failure is logged.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="changes"/> is not complete.</exception>
        public async Task<IReadOnlyList<(MajnetSongDetail Detail, OnlineChartRecord Record)>?> ApplyChangesAsync(ApiEndpoint api, MajNetChartChanges changes, IReadOnlyDictionary<string, MajNetChartInfo> infos)
        {
            if (!changes.IsComplete)
            {
                throw new ArgumentException("Only a complete range of changes can be applied.", nameof(changes));
            }
            if (!IsPersistent)
            {
                return null;
            }
            var server = GetServerKey(api);
            var listedRows = default(List<OnlineChartRow>);
            try
            {
                EnsureTable();
                await MajDatabase.Runtime.WriteAsync(connection =>
                {
                    var rows = new Dictionary<string, OnlineChartRow>(StringComparer.Ordinal);
                    var frontOrder = 0L;
                    foreach (var row in connection.Table<OnlineChartRow>().Where(row => row.Server == server))
                    {
                        rows[row.ChartId] = row;
                        if (row.IsListed)
                        {
                            frontOrder = Math.Min(frontOrder, row.ListOrder);
                        }
                    }
                    foreach (var change in changes.Changes)
                    {
                        var chartId = change.ChartId;
                        if (string.IsNullOrWhiteSpace(chartId))
                        {
                            continue;
                        }
                        rows.TryGetValue(chartId, out var row);
                        if (change.Kind == MajNetChartChangeKind.Deleted)
                        {
                            if (row is null)
                            {
                                continue;
                            }
                            if (HasPlayableFiles(row))
                            {
                                // The stored chart stays playable, so it keeps its row and its place in the list.
                                if (!row.IsDeletedOnServer)
                                {
                                    row.IsDeletedOnServer = true;
                                    row.UpdatedAtTicks = DateTime.UtcNow.Ticks;
                                    connection.Update(row);
                                }
                            }
                            else
                            {
                                connection.Delete(row);
                                rows.Remove(chartId);
                            }
                            continue;
                        }
                        var info = FindInfo(infos, chartId);
                        if (info?.Metadata is not { } detail)
                        {
                            // The chart was deleted after the range; the deletion arrives with a later range, which keeps a stored chart listed.
                            if (row is { IsListed: true } && !HasPlayableFiles(row))
                            {
                                row.IsListed = false;
                                row.UpdatedAtTicks = DateTime.UtcNow.Ticks;
                                connection.Update(row);
                            }
                            continue;
                        }
                        var isNew = row is null;
                        row ??= new OnlineChartRow
                        {
                            ChartId = chartId,
                            Server = server
                        };
                        var isChanged = Apply(row, detail, info);
                        if (!row.IsListed)
                        {
                            // The latest change comes last, so the latest added chart ends up first, as a fresh list would show it.
                            isChanged |= SetListed(row, --frontOrder);
                        }
                        if (isChanged)
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
                        rows[chartId] = row;
                    }
                    var lastFullSyncTicks = connection.Find<ChangeQueryRow>(server)?.LastFullSyncTicks ?? 0;
                    connection.InsertOrReplace(new ChangeQueryRow
                    {
                        Server = server,
                        LastQueryToTicks = ToUtcTicks(changes.To),
                        LastFullSyncTicks = lastFullSyncTicks,
                        UpdatedAtTicks = DateTime.UtcNow.Ticks
                    });
                    listedRows = rows.Values.Where(row => row.IsListed)
                                            .OrderBy(row => row.ListOrder)
                                            .ThenBy(row => row.Id)
                                            .ToList();
                }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                MajDebug.LogError(nameof(OnlineChartCache), $"Failed to apply the chart changes of {api.Name}: {e}");
                return null;
            }

            var charts = new List<(MajnetSongDetail, OnlineChartRecord)>(listedRows!.Count);
            AddCharts(charts, listedRows, infos);
            return charts;
        }

        /// <summary>Adds the metadata and record of rows to a chart listing, skipping rows whose metadata cannot be read.</summary>
        /// <param name="charts">The listing receiving the charts.</param>
        /// <param name="rows">The rows, in listing order.</param>
        /// <param name="infos">The info of this session keyed by chart identity, which supplies direct download URIs, or null.</param>
        private void AddCharts(List<(MajnetSongDetail, OnlineChartRecord)> charts, List<OnlineChartRow> rows, IReadOnlyDictionary<string, MajNetChartInfo>? infos)
        {
            foreach (var row in rows)
            {
                if (!Serializer.Json.TryDeserialize<MajnetSongDetail>(row.Detail, out var detail, out var exception) || detail is null)
                {
                    MajDebug.LogWarning(nameof(OnlineChartCache), $"Skipped the listed chart {row.ChartId}, whose metadata cannot be read: {exception?.Message}");
                    continue;
                }
                charts.Add((detail, CreateRecord(row, FindInfo(infos, row.ChartId), true)));
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
            var server = GetServerKey(api);
            var chartId = chart.Id;
            try
            {
                EnsureTable();
                var record = default(OnlineChartRecord);
                MajDatabase.Runtime.Write(connection =>
                {
                    var row = connection.Table<OnlineChartRow>()
                                        .Where(x => x.ChartId == chartId && x.Server == server)
                                        .FirstOrDefault();
                    var isNew = row is null;
                    row ??= new OnlineChartRow
                    {
                        ChartId = chartId,
                        Server = server
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
                Server = GetServerKey(api)
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
            var server = GetServerKey(api);
            var rows = MajDatabase.Runtime.Read(connection => connection.Table<OnlineChartRow>()
                                                                        .Where(row => row.Server == server)
                                                                        .OrderBy(row => row.Id)
                                                                        .ToList());
            foreach (var row in rows)
            {
                // A chart can be played once its chart and track are stored; the other files are optional.
                if (!HasPlayableFiles(row))
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

        /// <summary>
        /// Loads the listed charts of an endpoint's server that the server deleted, which stay listed because their chart
        /// and track are stored, so a chart list read in full lists them after the server's charts.
        /// </summary>
        /// <param name="api">The endpoint.</param>
        /// <returns>
        /// The metadata and record of every such chart in list order; empty when the cache is not persistent or the
        /// database could not be read.
        /// </returns>
        /// <remarks>Run this off the main thread; a database failure and rows whose metadata cannot be read are logged.</remarks>
        public IReadOnlyList<(MajnetSongDetail Detail, OnlineChartRecord Record)> LoadChartsDeletedOnServer(ApiEndpoint api)
        {
            var charts = new List<(MajnetSongDetail, OnlineChartRecord)>();
            if (!IsPersistent)
            {
                return charts;
            }
            var server = GetServerKey(api);
            try
            {
                EnsureTable();
                var rows = MajDatabase.Runtime.Read(connection => connection.Table<OnlineChartRow>()
                                                                            .Where(row => row.Server == server && row.IsListed == true && row.IsDeletedOnServer == true)
                                                                            .OrderBy(row => row.ListOrder)
                                                                            .ThenBy(row => row.Id)
                                                                            .ToList());
                AddCharts(charts, rows, null);
            }
            catch (Exception e)
            {
                MajDebug.LogError(nameof(OnlineChartCache), $"Failed to read the deleted charts of {api.Name}: {e}");
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
                      $"WHERE {nameof(OnlineChartRow.ChartId)} = ? AND {nameof(OnlineChartRow.Server)} = ?";
            var updatedAt = DateTime.UtcNow.Ticks;
            try
            {
                var database = MajDatabase.Runtime;
                ObserveWriteAsync(database.WriteAsync(connection => connection.Execute(sql, digest.Md5, digest.Sha256, updatedAt, record.ChartId, record.Server))).Forget();
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

        /// <summary>Creates the tables and their indexes once, upgrading a chart table keyed by endpoint URL.</summary>
        /// <exception cref="SQLiteException">The tables could not be created or upgraded.</exception>
        private void EnsureTable()
        {
            if (Volatile.Read(ref _isTableReady) != 0)
            {
                return;
            }
            MajDatabase.Runtime.Write(static connection =>
            {
                RenameLegacyEndpointColumn(connection);
                connection.CreateTable<OnlineChartRow>();
                connection.CreateTable<OnlineServerRow>();
                connection.CreateTable<ChangeQueryRow>();
            });
            Volatile.Write(ref _isTableReady, 1);
        }

        /// <summary>Upgrades a chart table written before charts were keyed by server, keeping its rows and their files.</summary>
        /// <param name="connection">The runtime database connection, inside a transaction.</param>
        /// <remarks>
        /// The old <c>Endpoint</c> column holds endpoint URLs, which are the server keys of endpoints whose server reports
        /// no identifier, so it becomes the <c>Server</c> column; the rows move to the server's identifier when the
        /// endpoint is next resolved.
        /// </remarks>
        /// <exception cref="SQLiteException">The table could not be upgraded.</exception>
        private static void RenameLegacyEndpointColumn(SQLiteConnection connection)
        {
            var hasLegacyColumn = false;
            foreach (var column in connection.GetTableInfo(TableName))
            {
                if (column.Name == nameof(OnlineChartRow.Server))
                {
                    return;
                }
                hasLegacyColumn |= column.Name == LegacyEndpointColumnName;
            }
            if (!hasLegacyColumn)
            {
                return;
            }
            connection.Execute($"DROP INDEX IF EXISTS \"{LegacyChartIndexName}\"");
            connection.Execute($"ALTER TABLE \"{TableName}\" RENAME COLUMN \"{LegacyEndpointColumnName}\" TO \"{nameof(OnlineChartRow.Server)}\"");
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
            if (row.IsDeletedOnServer)
            {
                // The server reported the chart again, so it exists.
                row.IsDeletedOnServer = false;
                isChanged = true;
            }
            if (isChanged)
            {
                row.UpdatedAtTicks = DateTime.UtcNow.Ticks;
            }
            return isChanged;
        }

        /// <summary>Gets whether the chart file and track of a row are stored, so the chart can be played without the server.</summary>
        /// <param name="row">The row.</param>
        /// <returns>True when both objects are in the object store; otherwise false.</returns>
        private bool HasPlayableFiles(OnlineChartRow row)
        {
            return Objects.Contains(row.MaidataSha256) && Objects.Contains(row.TrackSha256);
        }

        /// <summary>Marks a row listed at a position of the chart list.</summary>
        /// <param name="row">The row.</param>
        /// <param name="listOrder">The position; lower is listed first.</param>
        /// <returns>True when the row changed and has to be written; otherwise false.</returns>
        private static bool SetListed(OnlineChartRow row, long listOrder)
        {
            if (row.IsListed && row.ListOrder == listOrder)
            {
                return false;
            }
            row.IsListed = true;
            row.ListOrder = listOrder;
            row.UpdatedAtTicks = DateTime.UtcNow.Ticks;
            return true;
        }

        /// <summary>Converts a moment reported by the server to UTC ticks.</summary>
        /// <param name="moment">The moment; one of unspecified kind is taken to be UTC already.</param>
        /// <returns>The UTC ticks.</returns>
        private static long ToUtcTicks(DateTime moment)
        {
            return (moment.Kind == DateTimeKind.Local ? moment.ToUniversalTime() : moment).Ticks;
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
            var record = new OnlineChartRecord(this, row.Server, row.ChartId, row.IsResolved, isPersistent, row.IsDeletedOnServer);
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

        /// <summary>One chart of one server, with the digests of its files.</summary>
        [Table(TableName)]
        private sealed class OnlineChartRow
        {
            /// <summary>Gets or sets the row identity, which keeps the order charts were first recorded in.</summary>
            [PrimaryKey]
            [AutoIncrement]
            public long Id { get; set; }
            /// <summary>Gets or sets the server-assigned chart identity; unique together with <see cref="Server"/>.</summary>
            [Indexed(Name = ChartIndexName, Order = 1, Unique = true)]
            public string ChartId { get; set; } = string.Empty;
            /// <summary>Gets or sets the server key, the server's identifier or else its endpoint's URL; unique together with <see cref="ChartId"/>.</summary>
            [Indexed(Name = ChartIndexName, Order = 2, Unique = true)]
            public string Server { get; set; } = string.Empty;
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
            /// <summary>Gets or sets whether the chart is in the server's chart list as of the last full or incremental refresh.</summary>
            public bool IsListed { get; set; }
            /// <summary>Gets or sets the position of the chart in the chart list, lower first; meaningful while <see cref="IsListed"/> is true.</summary>
            public long ListOrder { get; set; }
            /// <summary>
            /// Gets or sets whether the server deleted the chart. Only a chart whose chart file and track are stored keeps its
            /// row once deleted, and a listed one stays listed, after the charts of the server's list.
            /// </summary>
            public bool IsDeletedOnServer { get; set; }
            /// <summary>Gets or sets when the row last changed, in UTC ticks.</summary>
            public long UpdatedAtTicks { get; set; }
        }

        /// <summary>The server an endpoint address reaches, remembered so its cached charts can be listed offline.</summary>
        [Table(ServerTableName)]
        private sealed class OnlineServerRow
        {
            /// <summary>Gets or sets the endpoint key, the absolute URI of the endpoint.</summary>
            [PrimaryKey]
            public string Endpoint { get; set; } = string.Empty;
            /// <summary>Gets or sets the server key the endpoint resolved to.</summary>
            public string Server { get; set; } = string.Empty;
            /// <summary>Gets or sets when the endpoint was last resolved, in UTC ticks.</summary>
            public long UpdatedAtTicks { get; set; }
        }

        /// <summary>Where the chart list of a server was last brought up to with <c>maichart/changes</c>.</summary>
        [Table(ChangeQueryTableName)]
        private sealed class ChangeQueryRow
        {
            /// <summary>Gets or sets the server key, the server's identifier or else its endpoint's URL.</summary>
            [PrimaryKey]
            public string Server { get; set; } = string.Empty;
            /// <summary>Gets or sets the end of the last changes range, in UTC ticks of the server's clock.</summary>
            public long LastQueryToTicks { get; set; }
            /// <summary>Gets or sets when the whole chart list was last read, in UTC ticks of the local clock.</summary>
            public long LastFullSyncTicks { get; set; }
            /// <summary>Gets or sets when the row was last written, in UTC ticks of the local clock.</summary>
            public long UpdatedAtTicks { get; set; }
        }
    }
}
