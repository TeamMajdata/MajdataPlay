using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using MajdataPlay.Json;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Utils;
using Newtonsoft.Json;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay
{
#nullable enable
    internal static class ScoreManager
    {
        static bool _isInited = false;

        static SpinLock _lock = new();

        static Dictionary<string, SongScores>? _onlineBuckets = null;

        readonly static Dictionary<string, SongScores> _buckets = new();

        /// <summary>The legacy JSON score file name consumed by the one-time migration.</summary>
        const string LegacyDatabaseFileName = "MajDatabase.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db";

        readonly static JsonSerializerSettings _jsonReadSettings = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters = new List<JsonConverter>
            {
                new JudgeDetailConverter(),
                new JudgeInfoConverter(),
            }
        };

        /// <summary>Opens the score database, migrates the legacy JSON scores once and loads the stored scores.</summary>
        /// <returns>A task that completes when the scores are loaded or the failure has been logged.</returns>
        /// <remarks>
        /// A failure is logged and the next call tries again; until then <see cref="SaveScore"/> keeps scores in memory only.
        /// </remarks>
        internal static async UniTask InitAsync()
        {
            if (_isInited)
            {
                return;
            }
            try
            {
                // The first access may copy the database out of the root, so it is kept off the main thread.
                var database = await Task.Run(static () => MajDatabase.Scores);
                // Only a database that never held the score table receives the legacy JSON scores.
                var hasScoreTable = await database.ReadAsync(HasScoreTable);
                await database.WriteAsync(static connection =>
                {
                    connection.CreateTable<MajScoreDB>();
                    connection.CreateTable<JudgeInfoRecordDB>();
                });
                if (!hasScoreTable)
                {
                    // Migrate from legacy JSON file
                    var legacyFile = MajEnv.RootDirectory.FindFile(LegacyDatabaseFileName);
                    if (legacyFile is not null)
                    {
                        var migrated = await MigrateFromJsonAsync(database, legacyFile);
                        if (migrated)
                        {
                            try
                            {
                                legacyFile.Delete();
                                MajDebug.LogInfo("Migrated scores from legacy JSON to SQLite, old file deleted.");
                            }
                            catch (Exception ex)
                            {
                                MajDebug.LogError($"Failed to delete legacy score file: {ex.Message}");
                            }
                        }
                    }
                }                

                // Load from SQLite, together with all JudgeInfoRecords for an id→JudgeInfo lookup
                var (rows, allJudgeRecords) = await database.ReadAsync(static connection =>
                    (connection.Query<MajScoreDB>("SELECT * FROM MajScores WHERE PlayCount > 0"),
                     connection.Table<JudgeInfoRecordDB>().ToList()));
                var judgeInfoLookup = allJudgeRecords.ToDictionary(r => r.Id, r => r.ToJudgeInfo());

                var loadedBuckets = new Dictionary<string, SongScores>();
                var grouped = rows.Select(x => x.ToMaiScore(judgeInfoLookup)).GroupBy(x => x.Hash);
                foreach (var group in grouped)
                {
                    var hash = group.Key;
                    if (string.IsNullOrEmpty(hash))
                    {
                        continue;
                    }
                    var dict = group.ToDictionary(x => x.ChartLevel, x => x);

                    var scores = new SongScores()
                    {
                        Easy = GetOrCreate(dict, hash, ChartLevel.Easy),
                        Basic = GetOrCreate(dict, hash, ChartLevel.Basic),
                        Advance = GetOrCreate(dict, hash, ChartLevel.Advance),
                        Expert = GetOrCreate(dict, hash, ChartLevel.Expert),
                        Master = GetOrCreate(dict, hash, ChartLevel.Master),
                        ReMaster = GetOrCreate(dict, hash, ChartLevel.ReMaster),
                        UTAGE = GetOrCreate(dict, hash, ChartLevel.UTAGE),
                    };
                    loadedBuckets.Add(hash, scores);
                }
                PublishLoadedBuckets(loadedBuckets);
                _isInited = true;
            }
            catch(Exception e)
            {
                MajDebug.LogError(e);
            }
            finally
            {
                await UniTask.Yield();
            }
        }

        

        public static MaiScore GetScore(ISongDetail song, ChartLevel level)
        {
            var hash = song.Hash;
            var records = CheckAndGetSongScores(hash, song.IsOnline);
            return level switch
            {
                ChartLevel.Easy => records.Easy,
                ChartLevel.Basic => records.Basic,
                ChartLevel.Advance => records.Advance,
                ChartLevel.Expert => records.Expert,
                ChartLevel.Master => records.Master,
                ChartLevel.ReMaster => records.ReMaster,
                ChartLevel.UTAGE => records.UTAGE,
                _ => throw new ArgumentOutOfRangeException("sb"),
            };
        }
        public static SongScores GetSongScores(ISongDetail song)
        {
            var hash = song.Hash;

            return CheckAndGetSongScores(hash, song.IsOnline);
        }
        /// <summary>Merges a game result into the stored score of a chart level and persists it.</summary>
        /// <param name="result">The finished game.</param>
        /// <param name="level">The played chart level.</param>
        /// <returns>
        /// True when the score was written to the score database; false when the level is invalid, the store has not been
        /// initialized (the score is then kept in memory only) or the write failed.
        /// </returns>
        public static async Task<bool> SaveScore(GameResult result, ChartLevel level)
        {
            try
            {
                var songInfo = result.SongDetail;
                var hash = songInfo.Hash;
                var records = CheckAndGetSongScores(hash, result.SongDetail.IsOnline);
                var record = level switch
                {
                    ChartLevel.Easy => records.Easy,
                    ChartLevel.Basic => records.Basic,
                    ChartLevel.Advance => records.Advance,
                    ChartLevel.Expert => records.Expert,
                    ChartLevel.Master => records.Master,
                    ChartLevel.ReMaster => records.ReMaster,
                    ChartLevel.UTAGE => records.UTAGE,
                    _ => throw new ArgumentOutOfRangeException(nameof(level))
                };

                record.Acc = result.Acc > record.Acc ? record.Acc.Update(result.Acc) : record.Acc;

                record.DXScore = result.DXScore > record.DXScore ? result.DXScore : record.DXScore;
                record.TotalDXScore = result.TotalDXScore;

                record.JudgeDetail = result.JudgeRecord;
                record.Fast = result.Fast;
                record.Late = result.Late;
                record.ComboState = result.ComboState > record.ComboState ? result.ComboState : record.ComboState;
                record.Timestamp = DateTime.Now;
                record.PlayCount++;

                if (!_isInited)
                {
                    MajDebug.LogWarning(nameof(ScoreManager), "The score database is not ready, so the score is kept in memory only.");
                    return false;
                }

                // The rows are built now, so the queued write stores this result even if the record changes before it runs.
                var scoreKey = MajScoreDB.MakeKey(record.Hash ?? string.Empty, record.ChartLevel);
                var detail = record.JudgeDetail ?? JudgeDetail.Empty;
                var tapRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Tap]);
                var holdRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Hold]);
                var slideRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Slide]);
                var breakRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Break]);
                var touchRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Touch]);
                var dbRow = MajScoreDB.FromMaiScore(record);

                await MajDatabase.Scores.WriteAsync(conn =>
                {
                    // Look up existing score row to get FK ids
                    var oldRow = conn.Find<MajScoreDB>(scoreKey);
                    conn.UpdateJudgeInfoDB(oldRow?.TapDetailId, tapRecord);
                    conn.UpdateJudgeInfoDB(oldRow?.HoldDetailId, holdRecord);
                    conn.UpdateJudgeInfoDB(oldRow?.SlideDetailId, slideRecord);
                    conn.UpdateJudgeInfoDB(oldRow?.BreakDetailId, breakRecord);
                    conn.UpdateJudgeInfoDB(oldRow?.TouchDetailId, touchRecord);

                    // Upsert MajScores with FK ids
                    dbRow.TapDetailId = tapRecord.Id;
                    dbRow.HoldDetailId = holdRecord.Id;
                    dbRow.SlideDetailId = slideRecord.Id;
                    dbRow.BreakDetailId = breakRecord.Id;
                    dbRow.TouchDetailId = touchRecord.Id;
                    conn.InsertOrReplace(dbRow);
                });

                return true;
            }
            catch (Exception ex)
            {
                MajDebug.LogError(ex);
                return false;
            }
        }

        public static void LoadOnlineScores(ReadOnlySpan<MajNetAccountSongScore> scores)
        {
            ref var @lock = ref _lock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                _onlineBuckets = new();
                if (scores.IsEmpty)
                {
                    return;
                }
                for (var i = 0; i < scores.Length; i++)
                {
                    var score = scores[i];
                    if (!_onlineBuckets.TryGetValue(score.Hash, out var scoreRecord))
                    {
                        scoreRecord = SongScores.Create(score.Hash);
                        _onlineBuckets.Add(score.Hash, scoreRecord);
                    }
                    var maiScore = default(MaiScore);
                    maiScore = score.ChartLevel switch
                    {
                        ChartLevel.Easy => scoreRecord.Easy,
                        ChartLevel.Basic => scoreRecord.Basic,
                        ChartLevel.Advance => scoreRecord.Advance,
                        ChartLevel.Expert => scoreRecord.Expert,
                        ChartLevel.Master => scoreRecord.Master,
                        ChartLevel.ReMaster => scoreRecord.ReMaster,
                        ChartLevel.UTAGE => scoreRecord.UTAGE,
                        _ => throw new ArgumentOutOfRangeException(nameof(score.ChartLevel), score.ChartLevel, null),
                    };
                    maiScore.Acc = score.Acc;
                    maiScore.DXScore = score.DXScore;
                    maiScore.ComboState = score.ComboState;
                    maiScore.Timestamp = score.Timestamp;
                    maiScore.PlayCount = 1;
                }
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }
        public static void UnloadOnlineScores()
        {
            ref var @lock = ref _lock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                _onlineBuckets = null;
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }


        static MaiScore GetOrCreate(Dictionary<ChartLevel, MaiScore> dict, string hash, ChartLevel level)
        {
            return dict.TryGetValue(level, out var score)
                ? score
                : new MaiScore()
                {
                    Hash = hash,
                    ChartLevel = level,
                    PlayCount = 0,
                };
        }
        /// <summary>Replaces the local score buckets with the scores loaded from the database.</summary>
        /// <param name="loadedBuckets">The loaded scores keyed by chart hash.</param>
        /// <remarks>A bucket created in memory before initialization is replaced by the stored one of the same chart.</remarks>
        static void PublishLoadedBuckets(Dictionary<string, SongScores> loadedBuckets)
        {
            ref var @lock = ref _lock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                foreach (var pair in loadedBuckets)
                {
                    _buckets[pair.Key] = pair.Value;
                }
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }
        /// <summary>Checks whether the score database already contains the score table.</summary>
        /// <param name="connection">The open score database connection.</param>
        /// <returns>True when the score table exists; otherwise false.</returns>
        /// <exception cref="SQLiteException">The schema could not be queried.</exception>
        static bool HasScoreTable(SQLiteConnection connection)
        {
            var tableCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'MajScores'");
            return tableCount > 0;
        }
        /// <summary>Imports legacy JSON scores into the SQLite database in one transaction.</summary>
        /// <param name="database">The score database whose tables are ready.</param>
        /// <param name="legacyPath">The existing legacy score file to read.</param>
        /// <returns>Whether every legacy score was imported successfully; a failed import leaves the database unchanged.</returns>
        static async Task<bool> MigrateFromJsonAsync(MajDatabaseHandle database, StorageFile legacyPath)
        {
            try
            {
                var json = await legacyPath.ReadAllTextAsync();
                // Fix legacy typo: "JudgeDeatil" → "JudgeDetail"
                json = json.Replace("\"JudgeDeatil\"", "\"JudgeDetail\"");
                var scores = JsonConvert.DeserializeObject<List<MaiScore>>(json, _jsonReadSettings);
                if (scores is null || scores.Count == 0)
                {
                    return true; // nothing to migrate
                }

                await database.WriteAsync(conn =>
                {
                    foreach (var score in scores)
                    {
                        if (string.IsNullOrEmpty(score.Hash) || score.PlayCount == 0)
                        {
                            continue;
                        }

                        // Insert JudgeInfoRecords first
                        var detail = score.JudgeDetail ?? JudgeDetail.Empty;
                        var tapRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Tap]);
                        var holdRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Hold]);
                        var slideRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Slide]);
                        var breakRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Break]);
                        var touchRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Touch]);

                        conn.Insert(tapRecord);
                        conn.Insert(holdRecord);
                        conn.Insert(slideRecord);
                        conn.Insert(breakRecord);
                        conn.Insert(touchRecord);

                        // Insert MajScoreDB with FK references
                        var dbRow = MajScoreDB.FromMaiScore(score);
                        dbRow.TapDetailId = tapRecord.Id;
                        dbRow.HoldDetailId = holdRecord.Id;
                        dbRow.SlideDetailId = slideRecord.Id;
                        dbRow.BreakDetailId = breakRecord.Id;
                        dbRow.TouchDetailId = touchRecord.Id;
                        conn.Insert(dbRow);
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                MajDebug.LogError($"Failed to migrate legacy scores: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
        static void UpdateJudgeInfoDB(this SQLiteConnection conn, int? majScoreID, JudgeInfoRecordDB record)
        {
            if (majScoreID is int id)
            {
                record.Id = id;
                conn.Update(record);
            }
            else
            {
                conn.Insert(record);
            }
        }
        static SongScores CheckAndGetSongScores(string hash, bool isOnline)
        {
            ref var @lock = ref _lock;
            var isLocked = false;
            try
            {
                @lock.Enter(ref isLocked);
                var buckets = _buckets;
                if (isOnline && _onlineBuckets is not null)
                {
                    buckets = _onlineBuckets;
                }
                if (!buckets.TryGetValue(hash, out var records))
                {
                    records = SongScores.Create(hash);
                    buckets.Add(hash, records);
                }
                return records;
            }
            finally
            {
                if (isLocked)
                {
                    @lock.Exit();
                }
            }
        }

        [Table("MajScores")]
        class MajScoreDB
        {
            [PrimaryKey]
            public string Key { get; set; } = string.Empty;

            [Indexed]
            public string Hash { get; set; } = string.Empty;

            public int ChartLevelInt { get; set; }

            public double AccDX { get; set; }
            public double AccClassic { get; set; }
            public long DXScore { get; set; }
            public long TotalDXScore { get; set; }
            public long Fast { get; set; }
            public long Late { get; set; }
            public long PlayCount { get; set; }
            public long TimestampTicks { get; set; }
            public int ComboStateInt { get; set; }

            // FK to JudgeInfoRecords
            public int? TapDetailId { get; set; }
            public int? HoldDetailId { get; set; }
            public int? SlideDetailId { get; set; }
            public int? BreakDetailId { get; set; }
            public int? TouchDetailId { get; set; }

            public static string MakeKey(string hash, ChartLevel level) => $"{hash}|{(int)level}";
            public static string MakeKey(string hash, int levelInt) => $"{hash}|{levelInt}";

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

            static JudgeInfo ResolveJudgeInfo(int? fkId, Dictionary<int, JudgeInfo> lookup)
            {
                return fkId.HasValue && lookup.TryGetValue(fkId.Value, out var info) ? info : JudgeInfo.Empty;
            }

            public static MajScoreDB FromMaiScore(MaiScore score)
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

        [Table("JudgeInfoRecords")]
        class JudgeInfoRecordDB
        {
            [PrimaryKey, AutoIncrement]
            public int Id { get; set; }

            public int Miss { get; set; }
            public int LateGood { get; set; }
            public int LateGreat3rd { get; set; }
            public int LateGreat2nd { get; set; }
            public int LateGreat { get; set; }
            public int LatePerfect3rd { get; set; }
            public int LatePerfect2nd { get; set; }
            public int Perfect { get; set; }
            public int FastPerfect2nd { get; set; }
            public int FastPerfect3rd { get; set; }
            public int FastGreat { get; set; }
            public int FastGreat2nd { get; set; }
            public int FastGreat3rd { get; set; }
            public int FastGood { get; set; }
            public int TooFast { get; set; }

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
