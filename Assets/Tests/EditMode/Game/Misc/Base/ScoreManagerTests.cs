using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Net.Models;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using MajSimai;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>
    /// Covers the score store <see cref="ScoreManager"/> with a SQLite database in temporary storage, including the
    /// legacy JSON migration, the root database import and the online score overlay.
    /// </summary>
    public class ScoreManagerTests
    {
        /// <summary>The legacy JSON score file name.</summary>
        private const string LegacyFileName = "MajDatabase.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db";

        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the application directories.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The application root inside the temporary storage.</summary>
        private StorageDirectory _root = null!;

        /// <summary>The score buckets present before the test.</summary>
        private KeyValuePair<string, MaiScore>[] _savedBuckets = Array.Empty<KeyValuePair<string, MaiScore>>();

        /// <summary>The online buckets present before the test.</summary>
        private object? _savedOnlineBuckets;

        /// <summary>The old account responses pending level-hash resolution before the test.</summary>
        private KeyValuePair<string, MajNetAccountSongScore[]>[] _savedLegacyOnlineScores = Array.Empty<KeyValuePair<string, MajNetAccountSongScore[]>>();

        /// <summary>The initialization flag before the test.</summary>
        private bool _savedIsInited;

        /// <summary>Gets the private local score buckets.</summary>
        private static Dictionary<string, MaiScore> Buckets => PrivateMember.GetStaticField<Dictionary<string, MaiScore>>(typeof(ScoreManager), "s_buckets");

        /// <summary>Gets account responses that require their endpoint's chart metadata.</summary>
        private static Dictionary<string, MajNetAccountSongScore[]> LegacyOnlineScores => PrivateMember.GetStaticField<Dictionary<string, MajNetAccountSongScore[]>>(typeof(ScoreManager), "s_legacyOnlineScores");

        /// <summary>Gets the score database opened through <see cref="MajDatabase"/>, or null.</summary>
        private static MajDatabaseHandle? Database => PrivateMember.GetStaticField<MajDatabaseHandle?>(typeof(MajDatabase), "s_scores");

        /// <summary>Points the score store at an empty temporary layout.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _root = _environment.UseTemporaryLayout(_temporary);
            _savedBuckets = new List<KeyValuePair<string, MaiScore>>(Buckets).ToArray();
            _savedOnlineBuckets = PrivateMember.GetStaticField<object?>(typeof(ScoreManager), "s_onlineBuckets");
            _savedLegacyOnlineScores = new List<KeyValuePair<string, MajNetAccountSongScore[]>>(LegacyOnlineScores).ToArray();
            _savedIsInited = PrivateMember.GetStaticField<bool>(typeof(ScoreManager), "s_isInited");
            ResetStore();
        }

        /// <summary>Restores the score store; the scope closes the test database.</summary>
        [TearDown]
        public void TearDown()
        {
            Buckets.Clear();
            foreach (var pair in _savedBuckets)
            {
                Buckets[pair.Key] = pair.Value;
            }
            PrivateMember.SetStaticField(typeof(ScoreManager), "s_onlineBuckets", _savedOnlineBuckets);
            LegacyOnlineScores.Clear();
            foreach (var pair in _savedLegacyOnlineScores)
            {
                LegacyOnlineScores[pair.Key] = pair.Value;
            }
            PrivateMember.SetStaticField(typeof(ScoreManager), "s_isInited", _savedIsInited);
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Forgets every loaded score so the next initialization reads the database again.</summary>
        private static void ResetStore()
        {
            Buckets.Clear();
            LegacyOnlineScores.Clear();
            PrivateMember.SetStaticField(typeof(ScoreManager), "s_onlineBuckets", null);
            PrivateMember.SetStaticField(typeof(ScoreManager), "s_isInited", false);
        }

        /// <summary>Closes the score database, so the next access opens the file of the current layout again.</summary>
        private static void CloseDatabase()
        {
            Database?.CloseAsync().Wait();
            PrivateMember.SetStaticField(typeof(MajDatabase), "s_scores", null);
        }

        /// <summary>Creates a judge detail in which every note type has the given perfect count.</summary>
        /// <param name="perfect">The critical perfect count of each note type.</param>
        /// <param name="miss">The miss count of the tap records.</param>
        /// <returns>The judge detail.</returns>
        private static JudgeDetail CreateDetail(int perfect, int miss = 0)
        {
            var records = new Dictionary<ScoreNoteType, JudgeInfo>();
            foreach (ScoreNoteType type in Enum.GetValues(typeof(ScoreNoteType)))
            {
                var counts = new Dictionary<JudgeGrade, int>();
                foreach (JudgeGrade grade in Enum.GetValues(typeof(JudgeGrade)))
                {
                    counts[grade] = 0;
                }
                counts[JudgeGrade.Perfect] = perfect;
                if (type == ScoreNoteType.Tap)
                {
                    counts[JudgeGrade.Miss] = miss;
                }
                records[type] = new JudgeInfo(counts);
            }
            return new JudgeDetail(records);
        }

        /// <summary>Creates a game result for a song.</summary>
        /// <param name="song">The played song.</param>
        /// <param name="dxScore">The achieved DX score.</param>
        /// <param name="accuracy">The achieved DX accuracy.</param>
        /// <param name="combo">The achieved combo state.</param>
        /// <param name="perfect">The perfect count of each note type.</param>
        /// <returns>The game result.</returns>
        private static GameResult CreateResult(ISongDetail song, long dxScore, double accuracy, ComboState combo, int perfect)
        {
            return new GameResult
            {
                Acc = new Accurate { DX = accuracy, Classic = accuracy },
                SongDetail = song,
                JudgeRecord = CreateDetail(perfect, 1),
                Level = ChartLevel.Master,
                Fast = 2,
                Late = 3,
                DXScore = dxScore,
                TotalDXScore = 2000,
                ComboState = combo
            };
        }

        /// <summary>Checks that the first initialization creates an empty database and later calls do nothing.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_CreatesDatabase()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var database = Database;
                await ScoreManager.InitAsync();

                Assert.That(database, Is.Not.Null);
                Assert.That(Database, Is.SameAs(database));
                Assert.That(_root.FindFile("MajScores.db"), Is.Not.Null);
                Assert.That(Buckets, Is.Empty);
            });
        }

        /// <summary>Checks that saved scores keep their best values and are loaded again after a restart.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SaveScore_PersistsBestScore()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var song = new FakeSongDetail("persisted");

                Assert.That(await ScoreManager.SaveScore(CreateResult(song, 1500, 99.5, ComboState.FC, 10), ChartLevel.Master), Is.True);
                Assert.That(await ScoreManager.SaveScore(CreateResult(song, 1200, 98, ComboState.None, 20), ChartLevel.Master), Is.True);
                var inMemory = ScoreManager.GetScore(song, ChartLevel.Master);
                Assert.That(inMemory.PlayCount, Is.EqualTo(2));
                Assert.That(inMemory.DXScore, Is.EqualTo(1500));
                Assert.That(inMemory.ComboState, Is.EqualTo(ComboState.FC));
                Assert.That(inMemory.Acc.DX, Is.EqualTo(99.5));

                CloseDatabase();
                ResetStore();
                await ScoreManager.InitAsync();

                var restored = ScoreManager.GetScore(song, ChartLevel.Master);
                Assert.That(restored.PlayCount, Is.EqualTo(2));
                Assert.That(restored.DXScore, Is.EqualTo(1500));
                Assert.That(restored.TotalDXScore, Is.EqualTo(2000));
                Assert.That(restored.Fast, Is.EqualTo(2));
                Assert.That(restored.Late, Is.EqualTo(3));
                Assert.That(restored.ComboState, Is.EqualTo(ComboState.FC));
                Assert.That(restored.Acc.DX, Is.EqualTo(99.5));
                Assert.That(restored.JudgeDetail![ScoreNoteType.Tap][JudgeGrade.Perfect], Is.EqualTo(20));
                Assert.That(restored.JudgeDetail[ScoreNoteType.Tap][JudgeGrade.Miss], Is.EqualTo(1));
                Assert.That(restored.JudgeDetail[ScoreNoteType.Break][JudgeGrade.Perfect], Is.EqualTo(20));
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Easy).PlayCount, Is.EqualTo(0));
            });
        }

        /// <summary>
        /// Checks that a score saved before initialization is kept in memory but reported as not persisted, without
        /// opening the database, and that invalid levels are reported.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SaveScore_WithoutDatabaseAndInvalidLevel()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var song = new FakeSongDetail("memory-only");

                Assert.That(await ScoreManager.SaveScore(CreateResult(song, 100, 50, ComboState.None, 1), ChartLevel.Basic), Is.False);
                Assert.That(Database, Is.Null);
                Assert.That(await ScoreManager.SaveScore(CreateResult(song, 100, 50, ComboState.None, 1), (ChartLevel)99), Is.False);

                Assert.That(ScoreManager.GetScore(song, ChartLevel.Basic).PlayCount, Is.EqualTo(1));
                Assert.Throws<ArgumentOutOfRangeException>(() => ScoreManager.GetScore(song, (ChartLevel)99));
            });
        }

        /// <summary>Checks that every chart level maps to its own record and that song buckets are reused.</summary>
        [Test]
        public void GetScore_ReturnsLevelRecords()
        {
            var song = new FakeSongDetail("levels");
            var scores = ScoreManager.GetSongScores(song);

            foreach (ChartLevel level in Enum.GetValues(typeof(ChartLevel)))
            {
                var score = ScoreManager.GetScore(song, level);
                Assert.That(score.LevelHash, Is.EqualTo("levels:" + (int)level));
            }
            Assert.That(ScoreManager.GetSongScores(song).Master, Is.SameAs(scores.Master));
        }

        /// <summary>Creates an endpoint with a distinct base URI.</summary>
        /// <param name="name">The host label identifying the endpoint.</param>
        /// <returns>The endpoint.</returns>
        private static ApiEndpoint Endpoint(string name)
        {
            return new ApiEndpoint { Name = name, Url = new Uri("https://" + name + ".invalid/api/") };
        }

        /// <summary>Creates an online song with explicit level hashes and no downloaded files.</summary>
        /// <param name="endpoint">The endpoint supplying the song and account scores.</param>
        /// <param name="hash">The old whole-chart hash.</param>
        /// <param name="hashes">The hashes indexed by difficulty.</param>
        /// <returns>The disposable online song.</returns>
        private static OnlineSongDetail OnlineSong(ApiEndpoint endpoint, string hash, params string?[] hashes)
        {
            var detail = new MajnetSongDetail { Id = hash, Hash = hash };
            var record = OnlineChartCache.Shared.CreateTransientRecord(endpoint, detail);
            record.SetLevelHashes(hashes);
            return new OnlineSongDetail(endpoint, detail, record);
        }

        /// <summary>Checks that account scores overlay only the online songs served by their endpoint.</summary>
        [Test]
        public void LoadOnlineScores_OverlaysOnlineSongs()
        {
            var endpoint = Endpoint("first");
            var hash = new string('a', 64);
            using var online = OnlineSong(endpoint, "shared", null, null, null, hash);
            var local = new FakeSongDetail("shared") { LevelHashValues = new string?[] { null, null, null, hash } };
            var timestamp = new DateTime(2024, 5, 6);
            ScoreManager.LoadOnlineScores(endpoint, new[]
            {
                new MajNetAccountSongScore { LevelHash = hash, ChartLevel = ChartLevel.Expert, DXScore = 777, ComboState = ComboState.AP, Timestamp = timestamp, Acc = new Accurate { DX = 100, Classic = 100 } },
            });

            var expert = ScoreManager.GetScore(online, ChartLevel.Expert);
            Assert.That(expert.DXScore, Is.EqualTo(777));
            Assert.That(expert.PlayCount, Is.EqualTo(1));
            Assert.That(expert.ComboState, Is.EqualTo(ComboState.AP));
            Assert.That(expert.Timestamp, Is.EqualTo(timestamp));
            Assert.That(ScoreManager.GetScore(local, ChartLevel.Expert).PlayCount, Is.EqualTo(0));

            ScoreManager.UnloadOnlineScores();

            Assert.That(ScoreManager.GetScore(online, ChartLevel.Expert).PlayCount, Is.EqualTo(0));
        }

        /// <summary>Checks that loading and clearing one API preserves every other API's score for the same level hash.</summary>
        [Test]
        public void LoadOnlineScores_KeepsEndpointsIndependent()
        {
            var first = Endpoint("first");
            var second = Endpoint("second");
            var hash = new string('b', 64);
            using var firstSong = OnlineSong(first, "same-chart", hash);
            using var secondSong = OnlineSong(second, "same-chart", hash);
            ScoreManager.LoadOnlineScores(first, new[] { new MajNetAccountSongScore { LevelHash = hash, DXScore = 100 } });
            ScoreManager.LoadOnlineScores(second, new[] { new MajNetAccountSongScore { LevelHash = hash, DXScore = 200 } });

            Assert.That(ScoreManager.GetScore(firstSong, ChartLevel.Easy).DXScore, Is.EqualTo(100));
            Assert.That(ScoreManager.GetScore(secondSong, ChartLevel.Easy).DXScore, Is.EqualTo(200));

            ScoreManager.LoadOnlineScores(first, ReadOnlySpan<MajNetAccountSongScore>.Empty);

            Assert.That(ScoreManager.GetScore(firstSong, ChartLevel.Easy).DXScore, Is.Zero);
            Assert.That(ScoreManager.GetScore(secondSong, ChartLevel.Easy).DXScore, Is.EqualTo(200));

            var equivalentFirst = new ApiEndpoint { Name = "renamed", Url = first.Url };
            ScoreManager.LoadOnlineScores(equivalentFirst, new[] { new MajNetAccountSongScore { LevelHash = hash, DXScore = 300 } });
            Assert.That(ScoreManager.GetScore(firstSong, ChartLevel.Easy).DXScore, Is.EqualTo(300));
            Assert.That(ScoreManager.GetScore(secondSong, ChartLevel.Easy).DXScore, Is.EqualTo(200));
        }

        /// <summary>Checks that repeated response rows for one level hash retain the best independent achievements.</summary>
        [Test]
        public void LoadOnlineScores_MergesRepeatedLevelHashes()
        {
            var endpoint = Endpoint("first");
            var hash = new string('c', 64);
            using var song = OnlineSong(endpoint, "chart", hash);
            ScoreManager.LoadOnlineScores(endpoint, new[]
            {
                new MajNetAccountSongScore { LevelHash = hash, DXScore = 900, Acc = new Accurate { DX = 100, Classic = 80 }, ComboState = ComboState.FC },
                new MajNetAccountSongScore { LevelHash = hash, DXScore = 500, Acc = new Accurate { DX = 90, Classic = 99 }, ComboState = ComboState.AP },
            });

            var score = ScoreManager.GetScore(song, ChartLevel.Easy);
            Assert.That(score.DXScore, Is.EqualTo(900));
            Assert.That(score.Acc.DX, Is.EqualTo(100));
            Assert.That(score.Acc.Classic, Is.EqualTo(99));
            Assert.That(score.ComboState, Is.EqualTo(ComboState.AP));
        }

        /// <summary>Checks that local songs share a level's record across whole-chart hashes and difficulty indices.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SaveScore_UsesOnlyLevelHashForLocalLookup()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var first = new FakeSongDetail("old-whole-hash") { LevelHashValues = new string?[] { "shared-level", "other-level" } };
                var second = new FakeSongDetail("new-whole-hash") { LevelHashValues = new string?[] { "changed-level", null, null, null, "shared-level" } };

                Assert.That(await ScoreManager.SaveScore(CreateResult(first, 1500, 99, ComboState.FC, 10), ChartLevel.Easy), Is.True);
                Assert.That(await ScoreManager.SaveScore(CreateResult(first, 1000, 90, ComboState.None, 4), ChartLevel.Basic), Is.True);
                Assert.That(ScoreManager.GetScore(second, ChartLevel.Master), Is.SameAs(ScoreManager.GetScore(first, ChartLevel.Easy)));
                Assert.That(ScoreManager.GetScore(second, ChartLevel.Master).DXScore, Is.EqualTo(1500));
                Assert.That(ScoreManager.GetScore(second, ChartLevel.Easy).PlayCount, Is.Zero);
                Assert.That(ScoreManager.GetScore(first, ChartLevel.Basic).DXScore, Is.EqualTo(1000));

                CloseDatabase();
                ResetStore();
                await ScoreManager.InitAsync();

                Assert.That(ScoreManager.GetScore(second, ChartLevel.Master).DXScore, Is.EqualTo(1500));
                Assert.That(ScoreManager.GetScore(second, ChartLevel.Easy).PlayCount, Is.Zero);
                Assert.That(ScoreManager.GetScore(first, ChartLevel.Basic).DXScore, Is.EqualTo(1000));
                Assert.That(Database!.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LevelScores")), Is.EqualTo(2));
            });
        }

        /// <summary>Checks that an account overlay is updated without writing account history into the local score database.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SaveScore_SeparatesAccountHistoryFromLocalPlays()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var endpoint = Endpoint("first");
                var hash = new string('d', 64);
                using var song = OnlineSong(endpoint, "chart", hash);
                ScoreManager.LoadOnlineScores(endpoint, new[] { new MajNetAccountSongScore { LevelHash = hash, DXScore = 1900, Acc = new Accurate { DX = 100, Classic = 100 } } });

                Assert.That(await ScoreManager.SaveScore(CreateResult(song, 1000, 90, ComboState.FC, 10), ChartLevel.Easy), Is.True);
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Easy).DXScore, Is.EqualTo(1900));
                ScoreManager.UnloadOnlineScores();

                Assert.That(ScoreManager.GetScore(song, ChartLevel.Easy).DXScore, Is.EqualTo(1000));
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Easy).PlayCount, Is.EqualTo(1));
                CloseDatabase();
                ResetStore();
                await ScoreManager.InitAsync();
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Easy).DXScore, Is.EqualTo(1000));
            });
        }

        /// <summary>Checks that legacy JSON scores are migrated once and the legacy file is removed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_MigratesLegacyJson()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _root.CreateFile(LegacyFileName).WriteAllText(
                    "[{\"Acc\":{\"DX\":100.5,\"Classic\":99.0},\"DXScore\":1000,\"TotalDXScore\":1200,\"Fast\":1,\"Late\":2," +
                    "\"ChartLevel\":4,\"Hash\":\"legacy\",\"PlayCount\":3,\"ComboState\":1,\"Timestamp\":\"2020-01-01T00:00:00\"," +
                    "\"JudgeDeatil\":{\"Tap\":{\"Perfect\":5,\"FastGreat1\":2},\"Hold\":{},\"Slide\":{},\"Break\":{},\"Touch\":{}}}," +
                    "{\"Hash\":\"never-played\",\"PlayCount\":0}," +
                    "{\"Hash\":\"\",\"PlayCount\":4}," +
                    "{\"Hash\":\"no-detail\",\"ChartLevel\":0,\"PlayCount\":1,\"DXScore\":10}]");

                await ScoreManager.InitAsync();

                Assert.That(_root.FindFile(LegacyFileName), Is.Null);
                await ScoreManager.MigrateLegacyScoresAsync(new ISongDetail[] { new FakeSongDetail("legacy"), new FakeSongDetail("no-detail") });
                var migrated = ScoreManager.GetScore(new FakeSongDetail("legacy"), ChartLevel.Master);
                Assert.That(migrated.PlayCount, Is.EqualTo(3));
                Assert.That(migrated.DXScore, Is.EqualTo(1000));
                Assert.That(migrated.Acc.DX, Is.EqualTo(100.5));
                Assert.That(migrated.ComboState, Is.EqualTo(ComboState.FC));
                Assert.That(migrated.JudgeDetail![ScoreNoteType.Tap][JudgeGrade.Perfect], Is.EqualTo(5));
                Assert.That(migrated.JudgeDetail[ScoreNoteType.Tap][JudgeGrade.FastGreat2nd], Is.EqualTo(2));
                Assert.That(ScoreManager.GetScore(new FakeSongDetail("no-detail"), ChartLevel.Easy).DXScore, Is.EqualTo(10));
                Assert.That(ScoreManager.GetScore(new FakeSongDetail("never-played"), ChartLevel.Easy).PlayCount, Is.EqualTo(0));
            });
        }

        /// <summary>Inserts an old SQLite score using the schema that predates level-hash storage.</summary>
        /// <param name="hash">The old whole-chart hash.</param>
        /// <param name="level">The old difficulty index.</param>
        /// <param name="dxScore">The stored best DX score.</param>
        /// <param name="plays">The stored play count.</param>
        /// <param name="timestamp">The timestamp distinguishing the latest play.</param>
        /// <param name="dxAccuracy">The stored best DX accuracy.</param>
        /// <param name="classicAccuracy">The stored best classic accuracy.</param>
        private static void InsertLegacyScore(string hash, ChartLevel level, long dxScore, long plays, DateTime timestamp, double dxAccuracy = 90, double classicAccuracy = 90)
        {
            Database!.Write(connection => connection.Execute(
                "INSERT INTO MajScores (Key, Hash, ChartLevelInt, AccDX, AccClassic, DXScore, TotalDXScore, Fast, Late, PlayCount, TimestampTicks, ComboStateInt) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                hash + "|" + (int)level, hash, (int)level, dxAccuracy, classicAccuracy, dxScore, 2000, 7, 8, plays, timestamp.Ticks, (int)ComboState.FC));
        }

        /// <summary>Checks that unresolvable legacy rows survive restarts and migrate exactly once when metadata becomes available.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MigrateLegacyScoresAsync_PreservesMissingChartsAndIsIdempotent()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                InsertLegacyScore("local", ChartLevel.Master, 1200, 3, new DateTime(2020, 1, 1));
                InsertLegacyScore("missing", ChartLevel.Master, 1000, 2, new DateTime(2020, 1, 2));
                var song = new FakeSongDetail("local");

                await ScoreManager.MigrateLegacyScoresAsync(new[] { song });
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Master).PlayCount, Is.EqualTo(3));
                Assert.That(Database!.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.EqualTo(1));
                Assert.That(Database.Read(static connection => connection.ExecuteScalar<string>("SELECT Hash FROM MajScores")), Is.EqualTo("missing"));

                CloseDatabase();
                ResetStore();
                await ScoreManager.InitAsync();
                await ScoreManager.MigrateLegacyScoresAsync(new[] { song });
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Master).PlayCount, Is.EqualTo(3));
                Assert.That(Database!.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.EqualTo(1));

                var missing = new FakeSongDetail("missing");
                await ScoreManager.MigrateLegacyScoresAsync(new[] { missing });
                Assert.That(ScoreManager.GetScore(missing, ChartLevel.Master).DXScore, Is.EqualTo(1000));
                Assert.That(Database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.Zero);
            });
        }

        /// <summary>Checks that old rows sharing a level hash merge with existing achievements and retain the latest play's detail.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MigrateLegacyScoresAsync_MergesSameLevelHash()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var local = new FakeSongDetail("current") { LevelHashValues = new string?[] { "shared-migration" } };
                Assert.That(await ScoreManager.SaveScore(CreateResult(local, 1800, 95, ComboState.AP, 20), ChartLevel.Easy), Is.True);
                var latest = ScoreManager.GetScore(local, ChartLevel.Easy).Timestamp;
                InsertLegacyScore("old-a", ChartLevel.Expert, 1500, 3, new DateTime(2020, 1, 1), 99, 80);
                InsertLegacyScore("old-b", ChartLevel.Master, 1900, 4, new DateTime(2020, 1, 2), 90, 100);
                var first = new FakeSongDetail("old-a") { LevelHashValues = new string?[] { null, null, null, "shared-migration" } };
                var second = new FakeSongDetail("old-b") { LevelHashValues = new string?[] { null, null, null, null, "shared-migration" } };

                await ScoreManager.MigrateLegacyScoresAsync(new[] { first, second, first });
                await ScoreManager.MigrateLegacyScoresAsync(new[] { first, second });

                var score = ScoreManager.GetScore(local, ChartLevel.Easy);
                Assert.That(score.PlayCount, Is.EqualTo(8));
                Assert.That(score.DXScore, Is.EqualTo(1900));
                Assert.That(score.Acc.DX, Is.EqualTo(99));
                Assert.That(score.Acc.Classic, Is.EqualTo(100));
                Assert.That(score.ComboState, Is.EqualTo(ComboState.AP));
                Assert.That(score.Timestamp, Is.EqualTo(latest));
                Assert.That(score.JudgeDetail![ScoreNoteType.Tap][JudgeGrade.Perfect], Is.EqualTo(20));
                Assert.That(Database!.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LevelScores")), Is.EqualTo(1));
                Assert.That(Database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.Zero);

                CloseDatabase();
                ResetStore();
                await ScoreManager.InitAsync();
                Assert.That(ScoreManager.GetScore(local, ChartLevel.Easy).PlayCount, Is.EqualTo(8));
                Assert.That(ScoreManager.GetScore(local, ChartLevel.Easy).DXScore, Is.EqualTo(1900));
                Assert.That(ScoreManager.GetScore(local, ChartLevel.Easy).JudgeDetail![ScoreNoteType.Tap][JudgeGrade.Perfect], Is.EqualTo(20));
            });
        }

        /// <summary>Checks that absent level hashes leave old rows untouched and later metadata resolves them.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MigrateLegacyScoresAsync_WaitsForLevelHashes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                InsertLegacyScore("unknown-level", ChartLevel.Master, 1500, 3, new DateTime(2020, 1, 1));
                var unknown = new FakeSongDetail("unknown-level") { LevelHashValues = new string?[7] };

                await ScoreManager.MigrateLegacyScoresAsync(new[] { unknown });
                Assert.That(Database!.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.EqualTo(1));
                Assert.That(Database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LevelScores")), Is.Zero);

                var known = new FakeSongDetail("unknown-level");
                await ScoreManager.MigrateLegacyScoresAsync(new[] { known });
                Assert.That(ScoreManager.GetScore(known, ChartLevel.Master).DXScore, Is.EqualTo(1500));
                Assert.That(Database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.Zero);
            });
        }

        /// <summary>Checks that a failed new-table write rolls back old-row removal and can be retried without double counting.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MigrateLegacyScoresAsync_RollsBackOnWriteFailure()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                InsertLegacyScore("retry", ChartLevel.Master, 1400, 3, new DateTime(2020, 1, 1));
                var song = new FakeSongDetail("retry");
                Database!.Write(static connection => connection.Execute("CREATE TRIGGER RejectMigration BEFORE INSERT ON LevelScores BEGIN SELECT RAISE(ABORT, 'migration test failure'); END"));
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    await ScoreManager.MigrateLegacyScoresAsync(new[] { song });
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = false;
                }

                Assert.That(Database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.EqualTo(1));
                Assert.That(Database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LevelScores")), Is.Zero);
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Master).PlayCount, Is.Zero);
                Database.Write(static connection => connection.Execute("DROP TRIGGER RejectMigration"));

                await ScoreManager.MigrateLegacyScoresAsync(new[] { song });
                await ScoreManager.MigrateLegacyScoresAsync(new[] { song });
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Master).PlayCount, Is.EqualTo(3));
                Assert.That(Database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.Zero);
            });
        }

        /// <summary>Checks that old API account responses resolve only against their own endpoint's chart list.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MigrateLegacyScoresAsync_ResolvesOldAccountResponsesPerApi()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var first = Endpoint("first");
                var second = Endpoint("second");
                var firstHash = new string('e', 64);
                var secondHash = new string('f', 64);
                using var firstSong = OnlineSong(first, "old-online", firstHash);
                using var secondSong = OnlineSong(second, "old-online", secondHash);
                ScoreManager.LoadOnlineScores(first, new[] { new MajNetAccountSongScore { Hash = "old-online", ChartLevel = ChartLevel.Easy, DXScore = 100 } });
                ScoreManager.LoadOnlineScores(second, new[] { new MajNetAccountSongScore { Hash = "old-online", ChartLevel = ChartLevel.Easy, DXScore = 200 } });

                await ScoreManager.MigrateLegacyScoresAsync(new ISongDetail[] { firstSong, secondSong });

                Assert.That(ScoreManager.GetScore(firstSong, ChartLevel.Easy).DXScore, Is.EqualTo(100));
                Assert.That(ScoreManager.GetScore(secondSong, ChartLevel.Easy).DXScore, Is.EqualTo(200));
                Assert.That(Database!.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM LevelScores")), Is.Zero);
                ScoreManager.LoadOnlineScores(first, ReadOnlySpan<MajNetAccountSongScore>.Empty);
                Assert.That(ScoreManager.GetScore(firstSong, ChartLevel.Easy).DXScore, Is.Zero);
                Assert.That(ScoreManager.GetScore(secondSong, ChartLevel.Easy).DXScore, Is.EqualTo(200));
            });
        }

        /// <summary>Checks that old servers' played charts supply missing level hashes by downloading only their chart file.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MigrateLegacyScoresAsync_RecoversOldServerHashesFromChart()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                using var http = new StubHttpScope();
                var bytes = Encoding.UTF8.GetBytes(ChartFixture.CreateMaidata("Legacy online migration"));
                using var stream = new System.IO.MemoryStream(bytes);
                var metadata = SimaiParser.ParseMetadata(stream);
                using var sha = SHA256.Create();
                var digest = Convert.ToBase64String(sha.ComputeHash(bytes));
                http.Responder = _ =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                    response.Headers.Add("hash", digest);
                    return response;
                };
                var endpoint = Endpoint("legacy");
                using var played = OnlineSong(endpoint, metadata.Hash);
                using var unplayed = OnlineSong(endpoint, "unplayed");
                InsertLegacyScore(metadata.Hash, ChartLevel.Master, 1300, 3, new DateTime(2020, 1, 1));
                ScoreManager.LoadOnlineScores(endpoint, new[]
                {
                    new MajNetAccountSongScore { Hash = metadata.Hash, ChartLevel = ChartLevel.Master, DXScore = 1700 },
                });

                await ScoreManager.MigrateLegacyScoresAsync(new ISongDetail[] { played, unplayed });
                await ScoreManager.MigrateLegacyScoresAsync(new ISongDetail[] { played, unplayed });

                Assert.That(played.GetLevelHash(ChartLevel.Master), Is.Not.Null.And.Not.Empty);
                Assert.That(ScoreManager.GetScore(played, ChartLevel.Master).DXScore, Is.EqualTo(1700));
                Assert.That(http.Requests, Has.Length.EqualTo(1));
                Assert.That(http.Requests[0].Uri.AbsolutePath, Does.EndWith("/chart"));
                Assert.That(Database!.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM MajScores")), Is.Zero);
                ScoreManager.UnloadOnlineScores();
                Assert.That(ScoreManager.GetScore(played, ChartLevel.Master).DXScore, Is.EqualTo(1300));
                Assert.That(ScoreManager.GetScore(played, ChartLevel.Master).PlayCount, Is.EqualTo(3));
                Assert.That(unplayed.GetLevelHash(ChartLevel.Master), Is.Null);
            });
        }

        /// <summary>Checks that an unreadable legacy file is kept for a later attempt.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_KeepsUnreadableLegacyJson()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _root.CreateFile(LegacyFileName).WriteAllText("{ not a list");

                await ScoreManager.InitAsync();

                Assert.That(_root.FindFile(LegacyFileName), Is.Not.Null);
                Assert.That(Buckets, Is.Empty);
            });
        }

        /// <summary>Checks that an empty legacy file counts as migrated.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_RemovesEmptyLegacyJson()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _root.CreateFile(LegacyFileName).WriteAllText("[]");

                await ScoreManager.InitAsync();

                Assert.That(_root.FindFile(LegacyFileName), Is.Null);
            });
        }

        /// <summary>Checks that a database left in a separate root is imported into the persistent directory.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_ImportsRootDatabase()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var song = new FakeSongDetail("imported");
                await ScoreManager.SaveScore(CreateResult(song, 900, 90, ComboState.None, 3), ChartLevel.Master);
                CloseDatabase();
                ResetStore();
                var persistent = _temporary.Directory.CreateDirectory("Persistent");
                _environment.Set(nameof(MajEnv.PersistentDirectory), persistent);

                await ScoreManager.InitAsync();

                Assert.That(persistent.FindFile("MajScores.db"), Is.Not.Null);
                Assert.That(persistent.FindFile("MajScores.db.importing"), Is.Null);
                Assert.That(_root.FindFile("MajScores.db"), Is.Not.Null);
                Assert.That(ScoreManager.GetScore(song, ChartLevel.Master).DXScore, Is.EqualTo(900));
            });
        }

        /// <summary>Checks that an empty database in a separate root is not imported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_SkipsEmptyRootDatabase()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _root.CreateFile("MajScores.db");
                var persistent = _temporary.Directory.CreateDirectory("Persistent");
                _environment.Set(nameof(MajEnv.PersistentDirectory), persistent);

                await ScoreManager.InitAsync();

                Assert.That(persistent.FindFile("MajScores.db"), Is.Not.Null);
                Assert.That(Database, Is.Not.Null);
            });
        }

        /// <summary>
        /// Checks that the database shutdown handler waits for a score write still in flight, closes the database, and
        /// that later saves are reported as not persisted.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnAppQuit_WaitsForScoreWriteAndClosesDatabase()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();
                var song = new FakeSongDetail("quit");
                var inFlight = ScoreManager.SaveScore(CreateResult(song, 800, 80, ComboState.None, 4), ChartLevel.Master);

                PrivateMember.InvokeStatic(typeof(MajDatabase), "OnAppQuit", null, null);

                Assert.That(await inFlight, Is.True);
                Assert.That(Database, Is.Null);
                Assert.That(await ScoreManager.SaveScore(CreateResult(song, 900, 90, ComboState.None, 5), ChartLevel.Master), Is.False);
                using (var connection = new SQLite.SQLiteConnection(_root.FindFile("MajScores.db")!.Location, SQLite.SQLiteOpenFlags.ReadOnly))
                {
                    Assert.That(connection.ExecuteScalar<long>("SELECT DXScore FROM LevelScores"), Is.EqualTo(800));
                }

                // An open SQLite database cannot be deleted on Windows, so a successful delete proves the connection was closed.
                var file = _root.FindFile("MajScores.db")!;
                file.Delete();
                Assert.That(file.Exists, Is.False);
            });
        }
    }
}
