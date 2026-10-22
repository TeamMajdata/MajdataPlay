using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.IO.Storage;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using SQLite;
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
        private KeyValuePair<string, SongScores>[] _savedBuckets = Array.Empty<KeyValuePair<string, SongScores>>();

        /// <summary>The online buckets present before the test.</summary>
        private object? _savedOnlineBuckets;

        /// <summary>The database connection present before the test.</summary>
        private SQLiteAsyncConnection? _savedDatabase;

        /// <summary>The initialization flag before the test.</summary>
        private bool _savedIsInited;

        /// <summary>Gets the private local score buckets.</summary>
        private static Dictionary<string, SongScores> Buckets => PrivateMember.GetStaticField<Dictionary<string, SongScores>>(typeof(ScoreManager), "_buckets");

        /// <summary>Gets the database opened by the score store, or null.</summary>
        private static SQLiteAsyncConnection? Database => PrivateMember.GetStaticField<SQLiteAsyncConnection?>(typeof(ScoreManager), "_db");

        /// <summary>Points the score store at an empty temporary layout.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _root = _environment.UseTemporaryLayout(_temporary);
            _savedBuckets = new List<KeyValuePair<string, SongScores>>(Buckets).ToArray();
            _savedOnlineBuckets = PrivateMember.GetStaticField<object?>(typeof(ScoreManager), "_onlineBuckets");
            _savedDatabase = Database;
            _savedIsInited = PrivateMember.GetStaticField<bool>(typeof(ScoreManager), "_isInited");
            ResetStore();
        }

        /// <summary>Closes the test database and restores the score store.</summary>
        [TearDown]
        public void TearDown()
        {
            CloseDatabase();
            Buckets.Clear();
            foreach (var pair in _savedBuckets)
            {
                Buckets[pair.Key] = pair.Value;
            }
            PrivateMember.SetStaticField(typeof(ScoreManager), "_onlineBuckets", _savedOnlineBuckets);
            PrivateMember.SetStaticField(typeof(ScoreManager), "_db", _savedDatabase);
            PrivateMember.SetStaticField(typeof(ScoreManager), "_isInited", _savedIsInited);
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Forgets every loaded score so the next initialization reads the database again.</summary>
        private static void ResetStore()
        {
            Buckets.Clear();
            PrivateMember.SetStaticField(typeof(ScoreManager), "_onlineBuckets", null);
            PrivateMember.SetStaticField(typeof(ScoreManager), "_db", null);
            PrivateMember.SetStaticField(typeof(ScoreManager), "_isInited", false);
        }

        /// <summary>Closes the test database and removes the shutdown handler registered by initialization.</summary>
        private static void CloseDatabase()
        {
            var handler = (EventHandler<EventArgs?>)Delegate.CreateDelegate(typeof(EventHandler<EventArgs?>),
                typeof(ScoreManager).GetMethod("OnAppQuit", BindingFlags.Static | BindingFlags.NonPublic)!);
            GameManager.OnAppQuit -= handler;
            Database?.CloseAsync().Wait();
            PrivateMember.SetStaticField(typeof(ScoreManager), "_db", null);
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

        /// <summary>Checks that scores are kept in memory when no database is open and invalid levels are reported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SaveScore_WithoutDatabaseAndInvalidLevel()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var song = new FakeSongDetail("memory-only");

                Assert.That(await ScoreManager.SaveScore(CreateResult(song, 100, 50, ComboState.None, 1), ChartLevel.Basic), Is.True);
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
                Assert.That(score.ChartLevel, Is.EqualTo(level));
                Assert.That(score.Hash, Is.EqualTo("levels"));
            }
            Assert.That(ScoreManager.GetSongScores(song).Master, Is.SameAs(scores.Master));
        }

        /// <summary>Checks that online scores overlay online songs only and can be unloaded.</summary>
        [Test]
        public void LoadOnlineScores_OverlaysOnlineSongs()
        {
            var online = new FakeSongDetail("shared", location: ChartStorageLocation.Online);
            var local = new FakeSongDetail("shared");
            var timestamp = new DateTime(2024, 5, 6);
            var scores = new[]
            {
                new MajNetAccountSongScore { Hash = "shared", ChartLevel = ChartLevel.Expert, DXScore = 777, ComboState = ComboState.AP, Timestamp = timestamp, Acc = new Accurate { DX = 100, Classic = 100 } },
                new MajNetAccountSongScore { Hash = "shared", ChartLevel = ChartLevel.Easy, DXScore = 5 },
                new MajNetAccountSongScore { Hash = "other", ChartLevel = ChartLevel.UTAGE, DXScore = 9 },
            };

            ScoreManager.LoadOnlineScores(scores);

            var expert = ScoreManager.GetScore(online, ChartLevel.Expert);
            Assert.That(expert.DXScore, Is.EqualTo(777));
            Assert.That(expert.PlayCount, Is.EqualTo(1));
            Assert.That(expert.ComboState, Is.EqualTo(ComboState.AP));
            Assert.That(expert.Timestamp, Is.EqualTo(timestamp));
            Assert.That(ScoreManager.GetScore(online, ChartLevel.Easy).DXScore, Is.EqualTo(5));
            Assert.That(ScoreManager.GetScore(local, ChartLevel.Expert).PlayCount, Is.EqualTo(0));

            ScoreManager.UnloadOnlineScores();

            Assert.That(ScoreManager.GetScore(online, ChartLevel.Expert).PlayCount, Is.EqualTo(0));
        }

        /// <summary>Checks that an empty online score list clears previously loaded online scores and invalid levels are rejected.</summary>
        [Test]
        public void LoadOnlineScores_EmptyAndInvalid()
        {
            var online = new FakeSongDetail("online", location: ChartStorageLocation.Online);
            ScoreManager.LoadOnlineScores(new[] { new MajNetAccountSongScore { Hash = "online", ChartLevel = ChartLevel.Master, DXScore = 1 } });

            ScoreManager.LoadOnlineScores(ReadOnlySpan<MajNetAccountSongScore>.Empty);

            Assert.That(ScoreManager.GetScore(online, ChartLevel.Master).DXScore, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ScoreManager.LoadOnlineScores(new[] { new MajNetAccountSongScore { Hash = "bad", ChartLevel = (ChartLevel)42 } }));
            ScoreManager.UnloadOnlineScores();
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

        /// <summary>Checks that the shutdown handler closes the database.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnAppQuit_ClosesDatabase()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await ScoreManager.InitAsync();

                PrivateMember.InvokeStatic(typeof(ScoreManager), "OnAppQuit", null, null);
                PrivateMember.SetStaticField(typeof(ScoreManager), "_db", null);

                // An open SQLite database cannot be deleted on Windows, so a successful delete proves the connection was closed.
                var file = _root.FindFile("MajScores.db")!;
                file.Delete();
                Assert.That(file.Exists, Is.False);
            });
        }
    }
}
