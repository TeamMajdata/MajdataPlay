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

        static SQLiteAsyncConnection? _db;

        readonly static JsonSerializerSettings _jsonReadSettings = new()
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters = new List<JsonConverter>
            {
                new JudgeDetailConverter(),
                new JudgeInfoConverter(),
            }
        };

        internal static async UniTask InitAsync()
        {
            if (_isInited)
            {
                return;
            }
            _isInited = true;
            try
            {
                var dbPath = MajEnv.ScoreDBPath;
                var isDbExists = FileSystem.File.Open(dbPath).Exists;
                _db = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                GameManager.OnAppQuit += OnAppQuit;
                await _db.CreateTableAsync<MajScoreDB>();
                await _db.CreateTableAsync<JudgeInfoRecordDB>();
                if (!isDbExists)
                {
                    // Migrate from legacy JSON file
                    var legacyPath = MajEnv.LegacyScoreDBPath;
                    if (FileSystem.File.Open(legacyPath).Exists)
                    {
                        var migrated = await MigrateFromJsonAsync(legacyPath);
                        if (migrated)
                        {
                            try
                            {
                                FileSystem.File.Open(legacyPath).Delete();
                                MajDebug.LogInfo("Migrated scores from legacy JSON to SQLite, old file deleted.");
                            }
                            catch (Exception ex)
                            {
                                MajDebug.LogError($"Failed to delete legacy score file: {ex.Message}");
                            }
                        }
                    }
                }                

                // Load from SQLite
                var rows = await _db.QueryAsync<MajScoreDB>("SELECT * FROM MajScores WHERE PlayCount > 0");

                // Load all JudgeInfoRecords into an id→JudgeInfo lookup
                var allJudgeRecords = await _db.Table<JudgeInfoRecordDB>().ToListAsync();
                var judgeInfoLookup = allJudgeRecords.ToDictionary(r => r.Id, r => r.ToJudgeInfo());

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
                    _buckets.Add(hash, scores);
                }
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

                if (_db is not null)
                {
                    var scoreKey = MajScoreDB.MakeKey(record.Hash ?? string.Empty, record.ChartLevel);

                    await _db.RunInTransactionAsync(conn =>
                    {
                        var detail = record.JudgeDetail ?? JudgeDetail.Empty;
                        var tapRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Tap]);
                        var holdRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Hold]);
                        var slideRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Slide]);
                        var breakRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Break]);
                        var touchRecord = JudgeInfoRecordDB.FromJudgeInfo(detail[ScoreNoteType.Touch]);

                        // Look up existing score row to get FK ids
                        var oldRow = conn.Find<MajScoreDB>(scoreKey);
                        conn.UpdateJudgeInfoDB(oldRow?.TapDetailId, tapRecord);
                        conn.UpdateJudgeInfoDB(oldRow?.HoldDetailId, holdRecord);
                        conn.UpdateJudgeInfoDB(oldRow?.SlideDetailId, slideRecord);
                        conn.UpdateJudgeInfoDB(oldRow?.BreakDetailId, breakRecord);
                        conn.UpdateJudgeInfoDB(oldRow?.TouchDetailId, touchRecord);

                        // Upsert MajScores with FK ids
                        var dbRow = MajScoreDB.FromMaiScore(record);
                        dbRow.TapDetailId = tapRecord.Id;
                        dbRow.HoldDetailId = holdRecord.Id;
                        dbRow.SlideDetailId = slideRecord.Id;
                        dbRow.BreakDetailId = breakRecord.Id;
                        dbRow.TouchDetailId = touchRecord.Id;
                        conn.InsertOrReplace(dbRow);
                    });
                }

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
        static async Task<bool> MigrateFromJsonAsync(string legacyPath)
        {
            try
            {
                var json = await FileSystem.File.Open(legacyPath).ReadAllTextAsync();
                // Fix legacy typo: "JudgeDeatil" → "JudgeDetail"
                json = json.Replace("\"JudgeDeatil\"", "\"JudgeDetail\"");
                var scores = JsonConvert.DeserializeObject<List<MaiScore>>(json, _jsonReadSettings);
                if (scores is null || scores.Count == 0)
                {
                    return true; // nothing to migrate
                }

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

                    await _db!.InsertAsync(tapRecord);
                    await _db.InsertAsync(holdRecord);
                    await _db.InsertAsync(slideRecord);
                    await _db.InsertAsync(breakRecord);
                    await _db.InsertAsync(touchRecord);

                    // Insert MajScoreDB with FK references
                    var dbRow = MajScoreDB.FromMaiScore(score);
                    dbRow.TapDetailId = tapRecord.Id;
                    dbRow.HoldDetailId = holdRecord.Id;
                    dbRow.SlideDetailId = slideRecord.Id;
                    dbRow.BreakDetailId = breakRecord.Id;
                    dbRow.TouchDetailId = touchRecord.Id;
                    await _db.InsertAsync(dbRow);
                }

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
        static void OnAppQuit(object? sender, EventArgs? args)
        {
            GameManager.OnAppQuit -= OnAppQuit;
            _db?.CloseAsync().Wait();
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
