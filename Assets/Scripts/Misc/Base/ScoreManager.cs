using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using MajdataPlay.Json;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using Newtonsoft.Json;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace MajdataPlay
{
    /// <summary>Stores local scores by level content and overlays the signed-in scores of each endpoint independently.</summary>
    internal static class ScoreManager
    {
        /// <summary>Records whether the score tables and in-memory records have been initialized.</summary>
        private static bool s_isInited;
        /// <summary>Guards the local and online in-memory records.</summary>
        private static readonly object s_lock = new();
        /// <summary>Serializes initialization, saves and migrations so database snapshots cannot overwrite newer results.</summary>
        private static readonly SemaphoreSlim s_storeGate = new(1, 1);
        /// <summary>Stores persistent scores by level hash, including plays made on online charts.</summary>
        private static readonly Dictionary<string, MaiScore> s_buckets = new(StringComparer.Ordinal);
        /// <summary>Stores signed-in scores by endpoint address and then by level hash; null outside an online session.</summary>
        private static Dictionary<string, Dictionary<string, MaiScore>>? s_onlineBuckets;
        /// <summary>Retains old account responses until the corresponding endpoint's chart list supplies level hashes.</summary>
        private static readonly Dictionary<string, MajNetAccountSongScore[]> s_legacyOnlineScores = new(StringComparer.Ordinal);
        /// <summary>The legacy JSON score file consumed before chart identities can be migrated.</summary>
        private const string LegacyDatabaseFileName = "MajDatabase.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db";
        /// <summary>Reads legacy judge records without appending to their default collections.</summary>
        private static readonly JsonSerializerSettings s_jsonReadSettings = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters = new List<JsonConverter>
            {
                new JudgeDetailConverter(),
                new JudgeInfoConverter(),
            }
        };

        /// <summary>Opens the score database and loads level scores, retaining old rows for migration after chart discovery.</summary>
        /// <returns>A task completing when initialization succeeds or its failure has been logged.</returns>
        internal static async UniTask InitAsync()
        {
            await s_storeGate.WaitAsync();
            try
            {
                if (s_isInited)
                {
                    return;
                }
                var database = await Task.Run(static () => MajDatabase.Scores);
                await database.WriteAsync(static connection =>
                {
                    connection.CreateTable<MajScoreDB>();
                    connection.CreateTable<LevelScoreDB>();
                    connection.CreateTable<JudgeInfoRecordDB>();
                    connection.CreateTable<ScoreMigrationDB>();
                });
                var legacyFile = MajEnv.RootDirectory.FindFile(LegacyDatabaseFileName);
                if (legacyFile is not null)
                {
                    var imported = await database.ReadAsync(static connection => connection.Find<ScoreMigrationDB>("LegacyJson") is not null);
                    if (imported || await MigrateFromJsonAsync(database, legacyFile))
                    {
                        try
                        {
                            legacyFile.Delete();
                            MajDebug.LogInfo(nameof(ScoreManager), "Imported legacy JSON scores; level identities will be migrated after chart discovery.");
                        }
                        catch (Exception exception)
                        {
                            MajDebug.LogException(nameof(ScoreManager), exception);
                        }
                    }
                }
                var loaded = await database.ReadAsync(static connection =>
                {
                    var judges = ReadJudgeInfo(connection);
                    return connection.Table<LevelScoreDB>().Where(row => row.PlayCount > 0).ToList()
                        .ToDictionary(row => row.LevelHash, row => row.ToMaiScore(judges), StringComparer.Ordinal);
                });
                lock (s_lock)
                {
                    foreach (var pair in loaded)
                    {
                        s_buckets[pair.Key] = pair.Value;
                    }
                    s_isInited = true;
                }
            }
            catch (Exception exception)
            {
                MajDebug.LogException(nameof(ScoreManager), exception);
            }
            finally
            {
                s_storeGate.Release();
            }
        }

        /// <summary>Gets the score for the content of one difficulty, using the endpoint's signed-in overlay when present.</summary>
        /// <param name="song">The chart supplying the level identity and optional endpoint.</param>
        /// <param name="level">The requested difficulty.</param>
        /// <returns>The score identified by the level hash, or an empty score when that hash is unavailable.</returns>
        /// <exception cref="ArgumentNullException">The chart is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The difficulty is invalid.</exception>
        public static MaiScore GetScore(ISongDetail song, ChartLevel level)
        {
            if (song is null)
            {
                throw new ArgumentNullException(nameof(song));
            }
            var hash = song.GetLevelHash(level);
            if (string.IsNullOrEmpty(hash))
            {
                return new MaiScore();
            }
            lock (s_lock)
            {
                var buckets = s_buckets;
                if (song is OnlineSongDetail online && s_onlineBuckets is not null &&
                    s_onlineBuckets.TryGetValue(OnlineChartCache.GetEndpointKey(online.ServerInfo), out var onlineScores))
                {
                    buckets = onlineScores;
                }
                return GetOrCreate(buckets, hash);
            }
        }

        /// <summary>Gets the scores of all seven difficulties using their individual level hashes.</summary>
        /// <param name="song">The chart whose scores are requested.</param>
        /// <returns>A difficulty-indexed view of the level scores.</returns>
        /// <exception cref="ArgumentNullException">The chart is null.</exception>
        public static SongScores GetSongScores(ISongDetail song)
        {
            return new SongScores
            {
                Easy = GetScore(song, ChartLevel.Easy),
                Basic = GetScore(song, ChartLevel.Basic),
                Advance = GetScore(song, ChartLevel.Advance),
                Expert = GetScore(song, ChartLevel.Expert),
                Master = GetScore(song, ChartLevel.Master),
                ReMaster = GetScore(song, ChartLevel.ReMaster),
                UTAGE = GetScore(song, ChartLevel.UTAGE),
            };
        }

        /// <summary>Merges one play into the local level score and the corresponding signed-in endpoint overlay.</summary>
        /// <param name="result">The completed play.</param>
        /// <param name="level">The played difficulty.</param>
        /// <returns>True when the local record was persisted; false when the store or hash is unavailable, or writing failed.</returns>
        public static async Task<bool> SaveScore(GameResult result, ChartLevel level)
        {
            await s_storeGate.WaitAsync();
            try
            {
                var song = result.SongDetail;
                var hash = song.GetLevelHash(level);
                if (string.IsNullOrEmpty(hash))
                {
                    MajDebug.LogWarning(nameof(ScoreManager), "The played level has no level hash; its score could not be stored.");
                    return false;
                }
                MaiScore record;
                lock (s_lock)
                {
                    record = GetOrCreate(s_buckets, hash);
                    ApplyResult(record, result);
                    if (song is OnlineSongDetail online && s_onlineBuckets is not null &&
                        s_onlineBuckets.TryGetValue(OnlineChartCache.GetEndpointKey(online.ServerInfo), out var onlineScores))
                    {
                        ApplyResult(GetOrCreate(onlineScores, hash), result);
                    }
                }
                if (!s_isInited)
                {
                    MajDebug.LogWarning(nameof(ScoreManager), "The score database is not ready, so the score is kept in memory only.");
                    return false;
                }
                await MajDatabase.Scores.WriteAsync(connection => WriteLevelScore(connection, record));
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogException(nameof(ScoreManager), exception);
                return false;
            }
            finally
            {
                s_storeGate.Release();
            }
        }

        /// <summary>Replaces only one endpoint's signed-in scores, retaining all other endpoints' records.</summary>
        /// <param name="endpoint">The endpoint returning these scores.</param>
        /// <param name="scores">The current account's best scores; old responses are resolved after chart discovery.</param>
        /// <exception cref="ArgumentNullException">The endpoint is null.</exception>
        public static void LoadOnlineScores(ApiEndpoint endpoint, ReadOnlySpan<MajNetAccountSongScore> scores)
        {
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            var buckets = new Dictionary<string, MaiScore>(StringComparer.Ordinal);
            var legacy = new List<MajNetAccountSongScore>();
            for (var i = 0; i < scores.Length; i++)
            {
                var score = scores[i];
                if (string.IsNullOrEmpty(score.LevelHash))
                {
                    legacy.Add(score);
                }
                else
                {
                    MergeScore(GetOrCreate(buckets, score.LevelHash), FromOnlineScore(score, score.LevelHash), false);
                }
            }
            var api = OnlineChartCache.GetEndpointKey(endpoint);
            lock (s_lock)
            {
                s_onlineBuckets ??= new Dictionary<string, Dictionary<string, MaiScore>>(StringComparer.Ordinal);
                s_onlineBuckets[api] = buckets;
                s_legacyOnlineScores[api] = legacy.ToArray();
            }
        }

        /// <summary>Ends the signed-in score overlays of every endpoint without changing local scores.</summary>
        public static void UnloadOnlineScores()
        {
            lock (s_lock)
            {
                s_onlineBuckets = null;
                s_legacyOnlineScores.Clear();
            }
        }

        /// <summary>Migrates matching whole-chart scores after local scanning or after all online chart lists have loaded.</summary>
        /// <param name="songs">The complete discovered charts, including unfiltered collection entries.</param>
        /// <param name="token">Cancels preparation and the transaction; canceled writes are rolled back.</param>
        /// <returns>A task completing after matched scores are merged and their old rows removed atomically.</returns>
        /// <exception cref="OperationCanceledException">Migration was canceled.</exception>
        /// <remarks>Unmatched rows remain available for later scans. Database failures are logged and keep every old row.</remarks>
        public static async Task MigrateLegacyScoresAsync(IEnumerable<ISongDetail> songs, CancellationToken token = default)
        {
            await s_storeGate.WaitAsync(token);
            try
            {
                var charts = songs.ToArray();
                await ResolveMissingLevelHashesAsync(charts, token);
                var identities = new Dictionary<(string Hash, int Level), string>();
                foreach (var song in charts)
                {
                    token.ThrowIfCancellationRequested();
                    for (var i = 0; i < 7; i++)
                    {
                        var levelHash = song.GetLevelHash((ChartLevel)i);
                        if (!string.IsNullOrEmpty(song.Hash) && !string.IsNullOrEmpty(levelHash))
                        {
                            identities[(song.Hash, i)] = levelHash;
                        }
                    }
                }
                ResolveLegacyOnlineScores(charts);
                if (!s_isInited || identities.Count == 0)
                {
                    return;
                }
                var migrated = new Dictionary<string, MaiScore>(StringComparer.Ordinal);
                var migratedCount = 0;
                await MajDatabase.Scores.WriteAsync(connection =>
                {
                    var legacyRows = connection.Table<MajScoreDB>().ToList();
                    var judges = ReadJudgeInfo(connection);
                    foreach (var row in legacyRows)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!identities.TryGetValue((row.Hash, row.ChartLevelInt), out var levelHash))
                        {
                            continue;
                        }
                        if (!migrated.TryGetValue(levelHash, out var score))
                        {
                            score = connection.Find<LevelScoreDB>(levelHash)?.ToMaiScore(judges) ?? new MaiScore { LevelHash = levelHash };
                            migrated.Add(levelHash, score);
                        }
                        var legacy = row.ToLegacyMaiScore(judges);
                        MergeScore(score, FromLegacyScore(legacy, levelHash), true);
                        WriteLevelScore(connection, score);
                        connection.Delete<MajScoreDB>(row.Key);
                        DeleteJudgeInfo(connection, row);
                        migratedCount++;
                    }
                    token.ThrowIfCancellationRequested();
                });
                lock (s_lock)
                {
                    foreach (var pair in migrated)
                    {
                        s_buckets[pair.Key] = pair.Value;
                    }
                }
                if (migratedCount > 0)
                {
                    MajDebug.LogInfo(nameof(ScoreManager), $"Migrated {migratedCount} legacy score records to level hashes.");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MajDebug.LogException(nameof(ScoreManager), exception);
            }
            finally
            {
                s_storeGate.Release();
            }
        }

        /// <summary>Resolves hashes from chart files only for played online charts whose server omits level metadata.</summary>
        /// <param name="charts">The discovered charts.</param>
        /// <param name="token">Cancels database reads and chart downloads.</param>
        /// <returns>A task completing when available hashes have been recovered; unreadable charts keep their old scores.</returns>
        /// <exception cref="OperationCanceledException">Hash recovery was canceled.</exception>
        private static async Task ResolveMissingLevelHashesAsync(ISongDetail[] charts, CancellationToken token)
        {
            var stored = new HashSet<(string Hash, int Level)>();
            if (s_isInited)
            {
                var rows = await MajDatabase.Scores.ReadAsync(static connection => connection.Table<MajScoreDB>().ToList());
                foreach (var row in rows)
                {
                    stored.Add((row.Hash, row.ChartLevelInt));
                }
            }
            Dictionary<string, MajNetAccountSongScore[]> accountScores;
            lock (s_lock)
            {
                accountScores = new Dictionary<string, MajNetAccountSongScore[]>(s_legacyOnlineScores, StringComparer.Ordinal);
            }
            var accountIdentities = accountScores.ToDictionary(
                pair => pair.Key,
                pair => new HashSet<(string Hash, int Level)>(pair.Value.Select(score => (score.Hash, (int)score.ChartLevel))),
                StringComparer.Ordinal);
            foreach (var song in charts)
            {
                token.ThrowIfCancellationRequested();
                if (song is not OnlineSongDetail online)
                {
                    continue;
                }
                var needsHashes = false;
                accountIdentities.TryGetValue(OnlineChartCache.GetEndpointKey(online.ServerInfo), out var legacy);
                for (var i = 0; i < 7; i++)
                {
                    if ((stored.Contains((song.Hash, i)) || legacy?.Contains((song.Hash, i)) == true) &&
                        string.IsNullOrEmpty(song.GetLevelHash((ChartLevel)i)))
                    {
                        needsHashes = true;
                        break;
                    }
                }
                if (!needsHashes)
                {
                    continue;
                }
                try
                {
                    await song.GetMaidataAsync(token: token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    MajDebug.LogWarning(nameof(ScoreManager), $"Could not resolve level hashes for online chart '{song.Id}'; its legacy scores are retained: {exception.Message}");
                }
            }
        }

        /// <summary>Maps old account responses using the matching endpoint's chart metadata.</summary>
        /// <param name="charts">The discovered charts.</param>
        private static void ResolveLegacyOnlineScores(ISongDetail[] charts)
        {
            var byEndpoint = new Dictionary<string, Dictionary<string, OnlineSongDetail>>(StringComparer.Ordinal);
            foreach (var song in charts)
            {
                if (song is not OnlineSongDetail online)
                {
                    continue;
                }
                var api = OnlineChartCache.GetEndpointKey(online.ServerInfo);
                if (!byEndpoint.TryGetValue(api, out var byHash))
                {
                    byHash = new Dictionary<string, OnlineSongDetail>(StringComparer.Ordinal);
                    byEndpoint.Add(api, byHash);
                }
                byHash[song.Hash] = online;
            }
            lock (s_lock)
            {
                if (s_onlineBuckets is null)
                {
                    return;
                }
                foreach (var pair in s_legacyOnlineScores)
                {
                    if (!byEndpoint.TryGetValue(pair.Key, out var byHash) || !s_onlineBuckets.TryGetValue(pair.Key, out var buckets))
                    {
                        continue;
                    }
                    foreach (var score in pair.Value)
                    {
                        if (string.IsNullOrEmpty(score.Hash) || (uint)score.ChartLevel > 6 || !byHash.TryGetValue(score.Hash, out var song))
                        {
                            continue;
                        }
                        var hash = song.GetLevelHash(score.ChartLevel);
                        if (!string.IsNullOrEmpty(hash))
                        {
                            MergeScore(GetOrCreate(buckets, hash), FromOnlineScore(score, hash), false);
                        }
                    }
                }
            }
        }

        /// <summary>Gets or creates an empty score in the supplied level-hash dictionary.</summary>
        /// <param name="buckets">The dictionary guarded by the caller.</param>
        /// <param name="hash">The nonempty level hash.</param>
        /// <returns>The existing or newly created score.</returns>
        private static MaiScore GetOrCreate(Dictionary<string, MaiScore> buckets, string hash)
        {
            if (!buckets.TryGetValue(hash, out var score))
            {
                score = new MaiScore { LevelHash = hash };
                buckets.Add(hash, score);
            }
            return score;
        }

        /// <summary>Merges the best values of a score while retaining the latest play details.</summary>
        /// <param name="target">The record updated in place.</param>
        /// <param name="source">The incoming score.</param>
        /// <param name="addPlayCount">Whether distinct stored histories have their play counts added.</param>
        private static void MergeScore(MaiScore target, MaiScore source, bool addPlayCount)
        {
            target.Acc = target.Acc.Update(source.Acc);
            target.DXScore = Math.Max(target.DXScore, source.DXScore);
            target.ComboState = source.ComboState > target.ComboState ? source.ComboState : target.ComboState;
            if (target.PlayCount == 0 || source.Timestamp >= target.Timestamp)
            {
                target.Timestamp = source.Timestamp;
                target.TotalDXScore = source.TotalDXScore;
                target.Fast = source.Fast;
                target.Late = source.Late;
                target.JudgeDetail = source.JudgeDetail;
            }
            target.PlayCount = addPlayCount ? target.PlayCount + source.PlayCount : Math.Max(target.PlayCount, source.PlayCount);
        }

        /// <summary>Applies one completed play, counting it once.</summary>
        /// <param name="record">The stored record.</param>
        /// <param name="result">The completed play.</param>
        private static void ApplyResult(MaiScore record, GameResult result)
        {
            MergeScore(record, new MaiScore
            {
                LevelHash = record.LevelHash,
                Acc = result.Acc,
                DXScore = result.DXScore,
                TotalDXScore = result.TotalDXScore,
                Fast = result.Fast,
                Late = result.Late,
                JudgeDetail = result.JudgeRecord,
                ComboState = result.ComboState,
                Timestamp = DateTime.Now,
                PlayCount = 1,
            }, true);
        }

        /// <summary>Converts an account best score into a local display record.</summary>
        /// <param name="score">The server score.</param>
        /// <param name="hash">The resolved level hash.</param>
        /// <returns>The score used by the endpoint's overlay.</returns>
        private static MaiScore FromOnlineScore(MajNetAccountSongScore score, string hash)
        {
            return new MaiScore
            {
                LevelHash = hash,
                Acc = score.Acc,
                DXScore = score.DXScore,
                ComboState = score.ComboState,
                Timestamp = score.Timestamp,
                PlayCount = 1,
            };
        }

        /// <summary>Converts an old whole-chart score after its played level has been identified.</summary>
        /// <param name="score">The old stored score.</param>
        /// <param name="hash">The resolved level hash.</param>
        /// <returns>The level score retaining all statistics and judge details.</returns>
        private static MaiScore FromLegacyScore(LegacyMaiScore score, string hash)
        {
            return new MaiScore
            {
                LevelHash = hash,
                Acc = score.Acc,
                DXScore = score.DXScore,
                TotalDXScore = score.TotalDXScore,
                Fast = score.Fast,
                Late = score.Late,
                JudgeDetail = score.JudgeDetail,
                ComboState = score.ComboState,
                Timestamp = score.Timestamp,
                PlayCount = score.PlayCount,
            };
        }

        /// <summary>Imports old JSON scores once, retaining their old identities until chart discovery.</summary>
        /// <param name="database">The initialized score database.</param>
        /// <param name="file">The legacy JSON file.</param>
        /// <returns>True if import committed or the file contained no scores; false if import failed.</returns>
        private static async Task<bool> MigrateFromJsonAsync(MajDatabaseHandle database, StorageFile file)
        {
            try
            {
                var json = await file.ReadAllTextAsync();
                json = json.Replace("\"JudgeDeatil\"", "\"JudgeDetail\"");
                var scores = JsonConvert.DeserializeObject<List<LegacyMaiScore>>(json, s_jsonReadSettings);
                await database.WriteAsync(connection =>
                {
                    foreach (var score in scores ?? new List<LegacyMaiScore>())
                    {
                        if (string.IsNullOrEmpty(score.Hash) || score.PlayCount <= 0)
                        {
                            continue;
                        }
                        var row = MajScoreDB.FromLegacyMaiScore(score);
                        if (connection.Find<MajScoreDB>(row.Key) is not null)
                        {
                            continue;
                        }
                        var detail = score.JudgeDetail ?? JudgeDetail.Empty;
                        row.TapDetailId = StoreJudgeInfo(connection, null, detail[ScoreNoteType.Tap]);
                        row.HoldDetailId = StoreJudgeInfo(connection, null, detail[ScoreNoteType.Hold]);
                        row.SlideDetailId = StoreJudgeInfo(connection, null, detail[ScoreNoteType.Slide]);
                        row.BreakDetailId = StoreJudgeInfo(connection, null, detail[ScoreNoteType.Break]);
                        row.TouchDetailId = StoreJudgeInfo(connection, null, detail[ScoreNoteType.Touch]);
                        connection.Insert(row);
                    }
                    connection.InsertOrReplace(new ScoreMigrationDB { Key = "LegacyJson" });
                });
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogException(nameof(ScoreManager), exception);
                return false;
            }
        }

        /// <summary>Loads the judge records referenced by both old and new score tables.</summary>
        /// <param name="connection">The open score connection.</param>
        /// <returns>Judge details indexed by their database identities.</returns>
        /// <exception cref="SQLiteException">Reading the judge table failed.</exception>
        private static Dictionary<int, JudgeInfo> ReadJudgeInfo(SQLiteConnection connection)
        {
            return connection.Table<JudgeInfoRecordDB>().ToList().ToDictionary(row => row.Id, row => row.ToJudgeInfo());
        }

        /// <summary>Writes a level score and its judge details within the caller's transaction.</summary>
        /// <param name="connection">The open score connection.</param>
        /// <param name="score">The level score to persist.</param>
        /// <exception cref="SQLiteException">Writing any record failed.</exception>
        private static void WriteLevelScore(SQLiteConnection connection, MaiScore score)
        {
            var old = connection.Find<LevelScoreDB>(score.LevelHash);
            var row = LevelScoreDB.FromMaiScore(score);
            var detail = score.JudgeDetail ?? JudgeDetail.Empty;
            row.TapDetailId = StoreJudgeInfo(connection, old?.TapDetailId, detail[ScoreNoteType.Tap]);
            row.HoldDetailId = StoreJudgeInfo(connection, old?.HoldDetailId, detail[ScoreNoteType.Hold]);
            row.SlideDetailId = StoreJudgeInfo(connection, old?.SlideDetailId, detail[ScoreNoteType.Slide]);
            row.BreakDetailId = StoreJudgeInfo(connection, old?.BreakDetailId, detail[ScoreNoteType.Break]);
            row.TouchDetailId = StoreJudgeInfo(connection, old?.TouchDetailId, detail[ScoreNoteType.Touch]);
            connection.InsertOrReplace(row);
        }

        /// <summary>Stores one judge record, reusing the existing row when available.</summary>
        /// <param name="connection">The open score connection.</param>
        /// <param name="id">The previous row identity, or null for a new record.</param>
        /// <param name="info">The judge counts to store.</param>
        /// <returns>The persisted row identity.</returns>
        /// <exception cref="SQLiteException">Writing the judge record failed.</exception>
        private static int StoreJudgeInfo(SQLiteConnection connection, int? id, JudgeInfo info)
        {
            var row = JudgeInfoRecordDB.FromJudgeInfo(info);
            if (id.HasValue)
            {
                row.Id = id.Value;
                connection.Update(row);
            }
            else
            {
                connection.Insert(row);
            }
            return row.Id;
        }

        /// <summary>Deletes the judge rows owned by a successfully migrated old score.</summary>
        /// <param name="connection">The open score connection.</param>
        /// <param name="row">The old score being removed in the same transaction.</param>
        /// <exception cref="SQLiteException">Deleting a judge record failed.</exception>
        private static void DeleteJudgeInfo(SQLiteConnection connection, MajScoreDB row)
        {
            foreach (var id in new[] { row.TapDetailId, row.HoldDetailId, row.SlideDetailId, row.BreakDetailId, row.TouchDetailId })
            {
                if (id.HasValue)
                {
                    connection.Delete<JudgeInfoRecordDB>(id.Value);
                }
            }
        }

        /// <summary>Records completed file imports so a failed file deletion cannot import the same scores twice.</summary>
        [Table("ScoreMigrations")]
        private sealed class ScoreMigrationDB
        {
            /// <summary>Gets or sets the completed migration name.</summary>
            [PrimaryKey]
            public string Key { get; set; } = string.Empty;
        }

        /// <summary>Retains old whole-chart identities until they can be resolved against discovered charts.</summary>
        [Table("MajScores")]
        private class MajScoreDB
        {
            /// <summary>Gets or sets the stored Key value.</summary>
            [PrimaryKey]
            public string Key { get; set; } = string.Empty;

            /// <summary>Gets or sets the stored Hash value.</summary>
            [Indexed]
            public string Hash { get; set; } = string.Empty;

            /// <summary>Gets or sets the stored ChartLevelInt value.</summary>
            public int ChartLevelInt { get; set; }

            /// <summary>Gets or sets the stored AccDX value.</summary>
            public double AccDX { get; set; }
            /// <summary>Gets or sets the stored AccClassic value.</summary>
            public double AccClassic { get; set; }
            /// <summary>Gets or sets the stored DXScore value.</summary>
            public long DXScore { get; set; }
            /// <summary>Gets or sets the stored TotalDXScore value.</summary>
            public long TotalDXScore { get; set; }
            /// <summary>Gets or sets the stored Fast value.</summary>
            public long Fast { get; set; }
            /// <summary>Gets or sets the stored Late value.</summary>
            public long Late { get; set; }
            /// <summary>Gets or sets the stored PlayCount value.</summary>
            public long PlayCount { get; set; }
            /// <summary>Gets or sets the stored TimestampTicks value.</summary>
            public long TimestampTicks { get; set; }
            /// <summary>Gets or sets the stored ComboStateInt value.</summary>
            public int ComboStateInt { get; set; }

            // FK to JudgeInfoRecords
            /// <summary>Gets or sets the stored TapDetailId value.</summary>
            public int? TapDetailId { get; set; }
            /// <summary>Gets or sets the stored HoldDetailId value.</summary>
            public int? HoldDetailId { get; set; }
            /// <summary>Gets or sets the stored SlideDetailId value.</summary>
            public int? SlideDetailId { get; set; }
            /// <summary>Gets or sets the stored BreakDetailId value.</summary>
            public int? BreakDetailId { get; set; }
            /// <summary>Gets or sets the stored TouchDetailId value.</summary>
            public int? TouchDetailId { get; set; }

            /// <summary>Builds the identity of an old chart-difficulty record.</summary>
            /// <param name="hash">The old whole-chart hash.</param>
            /// <param name="level">The played difficulty.</param>
            /// <returns>The old composite score key.</returns>
            public static string MakeKey(string hash, ChartLevel level) => $"{hash}|{(int)level}";
            /// <summary>Builds the identity of an old chart-difficulty record.</summary>
            /// <param name="hash">The old whole-chart hash.</param>
            /// <param name="levelInt">The played difficulty.</param>
            /// <returns>The old composite score key.</returns>
            public static string MakeKey(string hash, int levelInt) => $"{hash}|{levelInt}";

            /// <summary>Restores a score and its judge details from a database row.</summary>
            /// <param name="judgeInfoLookup">Judge details indexed by database identity.</param>
            /// <returns>The restored score.</returns>
            public LegacyMaiScore ToLegacyMaiScore(Dictionary<int, JudgeInfo> judgeInfoLookup)
            {
                var dict = new Dictionary<ScoreNoteType, JudgeInfo>
                {
                    { ScoreNoteType.Tap, ResolveJudgeInfo(TapDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Hold, ResolveJudgeInfo(HoldDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Slide, ResolveJudgeInfo(SlideDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Break, ResolveJudgeInfo(BreakDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Touch, ResolveJudgeInfo(TouchDetailId, judgeInfoLookup) },
                };

                return new LegacyMaiScore()
                {
                    Hash = Hash,
                    ChartLevel = (ChartLevel)ChartLevelInt,
                    Acc = new Accurate { DX = AccDX, Classic = AccClassic },
                    DXScore = DXScore,
                    TotalDXScore = TotalDXScore,
                    Fast = Fast,
                    Late = Late,
                    PlayCount = PlayCount,
                    JudgeDetail = new JudgeDetail(dict),
                    Timestamp = new DateTime(TimestampTicks, DateTimeKind.Local),
                    ComboState = (ComboState)ComboStateInt,
                };
            }

            /// <summary>Resolves an optional judge record reference.</summary>
            /// <param name="fkId">The optional referenced row.</param>
            /// <param name="lookup">The loaded judge records.</param>
            /// <returns>The referenced record, or empty judge counts.</returns>
            private static JudgeInfo ResolveJudgeInfo(int? fkId, Dictionary<int, JudgeInfo> lookup)
            {
                return fkId.HasValue && lookup.TryGetValue(fkId.Value, out var info) ? info : JudgeInfo.Empty;
            }

            /// <summary>Copies score or judge values into a database row.</summary>
            /// <param name="score">The values to copy.</param>
            /// <returns>The row ready for persistence.</returns>
            public static MajScoreDB FromLegacyMaiScore(LegacyMaiScore score)
            {
                var hash = score.Hash ?? string.Empty;

                return new MajScoreDB()
                {
                    Key = MakeKey(hash, score.ChartLevel),
                    Hash = hash,
                    ChartLevelInt = (int)score.ChartLevel,
                    AccDX = score.Acc.DX,
                    AccClassic = score.Acc.Classic,
                    DXScore = score.DXScore,
                    TotalDXScore = score.TotalDXScore,
                    Fast = score.Fast,
                    Late = score.Late,
                    PlayCount = score.PlayCount,
                    TimestampTicks = score.Timestamp.Ticks,
                    ComboStateInt = (int)score.ComboState,
                };
            }
        }


        /// <summary>Persists a score whose only content identity is its level hash.</summary>
        [Table("LevelScores")]
        private class LevelScoreDB
        {
            /// <summary>Gets or sets the stored LevelHash value.</summary>
            [PrimaryKey]
            public string LevelHash { get; set; } = string.Empty;

            /// <summary>Gets or sets the stored AccDX value.</summary>
            public double AccDX { get; set; }
            /// <summary>Gets or sets the stored AccClassic value.</summary>
            public double AccClassic { get; set; }
            /// <summary>Gets or sets the stored DXScore value.</summary>
            public long DXScore { get; set; }
            /// <summary>Gets or sets the stored TotalDXScore value.</summary>
            public long TotalDXScore { get; set; }
            /// <summary>Gets or sets the stored Fast value.</summary>
            public long Fast { get; set; }
            /// <summary>Gets or sets the stored Late value.</summary>
            public long Late { get; set; }
            /// <summary>Gets or sets the stored PlayCount value.</summary>
            public long PlayCount { get; set; }
            /// <summary>Gets or sets the stored TimestampTicks value.</summary>
            public long TimestampTicks { get; set; }
            /// <summary>Gets or sets the stored ComboStateInt value.</summary>
            public int ComboStateInt { get; set; }

            // FK to JudgeInfoRecords
            /// <summary>Gets or sets the stored TapDetailId value.</summary>
            public int? TapDetailId { get; set; }
            /// <summary>Gets or sets the stored HoldDetailId value.</summary>
            public int? HoldDetailId { get; set; }
            /// <summary>Gets or sets the stored SlideDetailId value.</summary>
            public int? SlideDetailId { get; set; }
            /// <summary>Gets or sets the stored BreakDetailId value.</summary>
            public int? BreakDetailId { get; set; }
            /// <summary>Gets or sets the stored TouchDetailId value.</summary>
            public int? TouchDetailId { get; set; }


            /// <summary>Restores a score and its judge details from a database row.</summary>
            /// <param name="judgeInfoLookup">Judge details indexed by database identity.</param>
            /// <returns>The restored score.</returns>
            public MaiScore ToMaiScore(Dictionary<int, JudgeInfo> judgeInfoLookup)
            {
                var dict = new Dictionary<ScoreNoteType, JudgeInfo>
                {
                    { ScoreNoteType.Tap, ResolveJudgeInfo(TapDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Hold, ResolveJudgeInfo(HoldDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Slide, ResolveJudgeInfo(SlideDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Break, ResolveJudgeInfo(BreakDetailId, judgeInfoLookup) },
                    { ScoreNoteType.Touch, ResolveJudgeInfo(TouchDetailId, judgeInfoLookup) },
                };

                return new MaiScore()
                {
                    LevelHash = LevelHash,
                    Acc = new Accurate { DX = AccDX, Classic = AccClassic },
                    DXScore = DXScore,
                    TotalDXScore = TotalDXScore,
                    Fast = Fast,
                    Late = Late,
                    PlayCount = PlayCount,
                    JudgeDetail = new JudgeDetail(dict),
                    Timestamp = new DateTime(TimestampTicks, DateTimeKind.Local),
                    ComboState = (ComboState)ComboStateInt,
                };
            }

            /// <summary>Resolves an optional judge record reference.</summary>
            /// <param name="fkId">The optional referenced row.</param>
            /// <param name="lookup">The loaded judge records.</param>
            /// <returns>The referenced record, or empty judge counts.</returns>
            private static JudgeInfo ResolveJudgeInfo(int? fkId, Dictionary<int, JudgeInfo> lookup)
            {
                return fkId.HasValue && lookup.TryGetValue(fkId.Value, out var info) ? info : JudgeInfo.Empty;
            }

            /// <summary>Copies score or judge values into a database row.</summary>
            /// <param name="score">The values to copy.</param>
            /// <returns>The row ready for persistence.</returns>
            public static LevelScoreDB FromMaiScore(MaiScore score)
            {
                return new LevelScoreDB()
                {
                    LevelHash = score.LevelHash,
                    AccDX = score.Acc.DX,
                    AccClassic = score.Acc.Classic,
                    DXScore = score.DXScore,
                    TotalDXScore = score.TotalDXScore,
                    Fast = score.Fast,
                    Late = score.Late,
                    PlayCount = score.PlayCount,
                    TimestampTicks = score.Timestamp.Ticks,
                    ComboStateInt = (int)score.ComboState,
                };
            }
        }


        /// <summary>Persists per-grade judge counts referenced by score rows.</summary>
        [Table("JudgeInfoRecords")]
        private class JudgeInfoRecordDB
        {
            /// <summary>Gets or sets the stored Id value.</summary>
            [PrimaryKey, AutoIncrement]
            public int Id { get; set; }

            /// <summary>Gets or sets the stored Miss value.</summary>
            public int Miss { get; set; }
            /// <summary>Gets or sets the stored LateGood value.</summary>
            public int LateGood { get; set; }
            /// <summary>Gets or sets the stored LateGreat3rd value.</summary>
            public int LateGreat3rd { get; set; }
            /// <summary>Gets or sets the stored LateGreat2nd value.</summary>
            public int LateGreat2nd { get; set; }
            /// <summary>Gets or sets the stored LateGreat value.</summary>
            public int LateGreat { get; set; }
            /// <summary>Gets or sets the stored LatePerfect3rd value.</summary>
            public int LatePerfect3rd { get; set; }
            /// <summary>Gets or sets the stored LatePerfect2nd value.</summary>
            public int LatePerfect2nd { get; set; }
            /// <summary>Gets or sets the stored Perfect value.</summary>
            public int Perfect { get; set; }
            /// <summary>Gets or sets the stored FastPerfect2nd value.</summary>
            public int FastPerfect2nd { get; set; }
            /// <summary>Gets or sets the stored FastPerfect3rd value.</summary>
            public int FastPerfect3rd { get; set; }
            /// <summary>Gets or sets the stored FastGreat value.</summary>
            public int FastGreat { get; set; }
            /// <summary>Gets or sets the stored FastGreat2nd value.</summary>
            public int FastGreat2nd { get; set; }
            /// <summary>Gets or sets the stored FastGreat3rd value.</summary>
            public int FastGreat3rd { get; set; }
            /// <summary>Gets or sets the stored FastGood value.</summary>
            public int FastGood { get; set; }
            /// <summary>Gets or sets the stored TooFast value.</summary>
            public int TooFast { get; set; }

            /// <summary>Restores the per-grade judge counts.</summary>
            /// <returns>The stored judge counts.</returns>
            public JudgeInfo ToJudgeInfo()
            {
                var dict = new Dictionary<JudgeGrade, int>
                {
                    { JudgeGrade.Miss, Miss },
                    { JudgeGrade.LateGood, LateGood },
                    { JudgeGrade.LateGreat3rd, LateGreat3rd },
                    { JudgeGrade.LateGreat2nd, LateGreat2nd },
                    { JudgeGrade.LateGreat, LateGreat },
                    { JudgeGrade.LatePerfect3rd, LatePerfect3rd },
                    { JudgeGrade.LatePerfect2nd, LatePerfect2nd },
                    { JudgeGrade.Perfect, Perfect },
                    { JudgeGrade.FastPerfect2nd, FastPerfect2nd },
                    { JudgeGrade.FastPerfect3rd, FastPerfect3rd },
                    { JudgeGrade.FastGreat, FastGreat },
                    { JudgeGrade.FastGreat2nd, FastGreat2nd },
                    { JudgeGrade.FastGreat3rd, FastGreat3rd },
                    { JudgeGrade.FastGood, FastGood },
                    { JudgeGrade.TooFast, TooFast },
                };
                return new JudgeInfo(dict);
            }

            /// <summary>Copies score or judge values into a database row.</summary>
            /// <param name="info">The values to copy.</param>
            /// <returns>The row ready for persistence.</returns>
            public static JudgeInfoRecordDB FromJudgeInfo(JudgeInfo info)
            {
                return new JudgeInfoRecordDB
                {
                    Miss = info[JudgeGrade.Miss],
                    LateGood = info[JudgeGrade.LateGood],
                    LateGreat3rd = info[JudgeGrade.LateGreat3rd],
                    LateGreat2nd = info[JudgeGrade.LateGreat2nd],
                    LateGreat = info[JudgeGrade.LateGreat],
                    LatePerfect3rd = info[JudgeGrade.LatePerfect3rd],
                    LatePerfect2nd = info[JudgeGrade.LatePerfect2nd],
                    Perfect = info[JudgeGrade.Perfect],
                    FastPerfect2nd = info[JudgeGrade.FastPerfect2nd],
                    FastPerfect3rd = info[JudgeGrade.FastPerfect3rd],
                    FastGreat = info[JudgeGrade.FastGreat],
                    FastGreat2nd = info[JudgeGrade.FastGreat2nd],
                    FastGreat3rd = info[JudgeGrade.FastGreat3rd],
                    FastGood = info[JudgeGrade.FastGood],
                    TooFast = info[JudgeGrade.TooFast],
                };
            }
        }

    }
}
