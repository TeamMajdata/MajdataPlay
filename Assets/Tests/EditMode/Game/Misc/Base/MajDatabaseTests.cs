using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using SQLite;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>
    /// Covers <see cref="MajDatabase"/> and <see cref="MajDatabaseHandle"/> against temporary storage: lazy opening,
    /// write ordering and failure isolation, synchronous access alongside queued writes, and shutdown.
    /// </summary>
    public class MajDatabaseTests
    {
        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the application directories and closing the databases opened by a test.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The application root inside the temporary storage.</summary>
        private StorageDirectory _root = null!;

        /// <summary>Points the databases at an empty temporary layout.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _root = _environment.UseTemporaryLayout(_temporary);
        }

        /// <summary>Closes the databases and restores the environment.</summary>
        [TearDown]
        public void TearDown()
        {
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>A row of the test table.</summary>
        [Table("Items")]
        private sealed class ItemRow
        {
            /// <summary>Gets or sets the primary key.</summary>
            [PrimaryKey]
            public int Id { get; set; }

            /// <summary>Gets or sets the stored value.</summary>
            public int Value { get; set; }
        }

        /// <summary>Creates the test table in the runtime database.</summary>
        /// <returns>The runtime database.</returns>
        private static MajDatabaseHandle CreateItemTable()
        {
            var database = MajDatabase.Runtime;
            database.Write(static connection => connection.CreateTable<ItemRow>());
            return database;
        }

        /// <summary>Counts the test rows through a separate read-only connection to the runtime database file.</summary>
        /// <returns>The number of stored rows.</returns>
        private static int CountItemsInFile()
        {
            var file = MajEnv.RuntimeDirectory.FindFile(MajDatabase.RuntimeDatabaseFileName)!;
            using var connection = new SQLiteConnection(file.Location, SQLiteOpenFlags.ReadOnly);
            return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Items");
        }

        /// <summary>Awaits a task and returns the exception it failed with.</summary>
        /// <param name="task">The task to await.</param>
        /// <returns>The exception, or null when the task succeeded.</returns>
        private static async UniTask<Exception?> CaptureExceptionAsync(Task task)
        {
            try
            {
                await task;
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        /// <summary>Checks that the runtime database opens on first access in the runtime directory and is shared afterwards.</summary>
        [Test]
        public void Runtime_OpensOnFirstAccessAndIsShared()
        {
            Assert.That(MajEnv.RuntimeDirectory.FindFile(MajDatabase.RuntimeDatabaseFileName), Is.Null);

            var first = MajDatabase.Runtime;

            Assert.That(MajEnv.RuntimeDirectory.FindFile(MajDatabase.RuntimeDatabaseFileName), Is.Not.Null);
            Assert.That(MajDatabase.Runtime, Is.SameAs(first));
            Assert.That(first.Name, Is.EqualTo(nameof(MajDatabase.Runtime)));
            Assert.That(first.IsClosed, Is.False);
        }

        /// <summary>Checks that the score database opens on first access in the persistent directory, separately from the runtime database.</summary>
        [Test]
        public void Scores_OpensInPersistentDirectory()
        {
            var persistent = _temporary.Directory.CreateDirectory("Persistent");
            _environment.Set(nameof(MajEnv.PersistentDirectory), persistent);

            var scores = MajDatabase.Scores;

            Assert.That(persistent.FindFile(MajDatabase.ScoresDatabaseFileName), Is.Not.Null);
            Assert.That(_root.FindFile(MajDatabase.ScoresDatabaseFileName), Is.Null);
            Assert.That(scores, Is.Not.SameAs(MajDatabase.Runtime));
            Assert.That(MajDatabase.Scores, Is.SameAs(scores));
        }

        /// <summary>Checks that queued writes reach the database in call order.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WriteAsync_RunsInCallOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var database = CreateItemTable();

                for (var i = 0; i < 100; i++)
                {
                    var value = i;
                    _ = database.WriteAsync(connection => connection.InsertOrReplace(new ItemRow { Id = 1, Value = value }));
                }
                await database.WaitForWritesAsync();

                Assert.That(database.Read(static connection => connection.Find<ItemRow>(1).Value), Is.EqualTo(99));
            });
        }

        /// <summary>Checks that a failed write faults its own task, is rolled back and does not stop the writes queued after it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WriteAsync_FailureIsRolledBackAndIsolated()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var database = CreateItemTable();

                var failing = database.WriteAsync(static connection =>
                {
                    connection.Insert(new ItemRow { Id = 1, Value = 1 });
                    throw new InvalidOperationException("write failed");
                });
                var following = database.WriteAsync(static connection => connection.Insert(new ItemRow { Id = 2, Value = 2 }));

                Assert.That(await CaptureExceptionAsync(failing), Is.InstanceOf<InvalidOperationException>());
                await following;
                await database.WaitForWritesAsync();

                Assert.That(database.WaitForWritesAsync().IsFaulted, Is.False);
                Assert.That(database.Read(static connection => connection.Find<ItemRow>(1)), Is.Null);
                Assert.That(database.Read(static connection => connection.Find<ItemRow>(2)), Is.Not.Null);
            });
        }

        /// <summary>
        /// Checks that synchronous writes on the calling thread interleave with queued writes on the same connection
        /// without lock contention, which two separate connections to one file could not guarantee.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Write_AlongsideQueuedWrites_KeepsEveryRow()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var database = CreateItemTable();
                var queued = new List<Task>();

                for (var i = 0; i < 200; i++)
                {
                    var id = i;
                    queued.Add(database.WriteAsync(connection => connection.Insert(new ItemRow { Id = id, Value = id })));
                    if (i % 4 == 0)
                    {
                        database.Write(connection => connection.Insert(new ItemRow { Id = 1000 + id, Value = id }));
                    }
                }
                await Task.WhenAll(queued);

                Assert.That(database.Read(static connection => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Items")), Is.EqualTo(250));
            });
        }

        /// <summary>Checks that a read on the thread pool sees the committed writes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAsync_ReturnsCommittedRows()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var database = CreateItemTable();
                await database.WriteAsync(static connection => connection.Insert(new ItemRow { Id = 7, Value = 42 }));

                var value = await database.ReadAsync(static connection => connection.Find<ItemRow>(7).Value);

                Assert.That(value, Is.EqualTo(42));
            });
        }

        /// <summary>
        /// Checks that closing waits for every queued write, closes the file, and that the handle and the properties refuse
        /// any later use.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CloseAllAsync_DrainsWritesAndRefusesLaterUse()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var database = CreateItemTable();
                for (var i = 0; i < 200; i++)
                {
                    var id = i;
                    _ = database.WriteAsync(connection => connection.Insert(new ItemRow { Id = id, Value = id }));
                }

                await MajDatabase.CloseAllAsync();

                Assert.That(database.IsClosed, Is.True);
                Assert.That(database.WaitForWritesAsync().IsCompleted, Is.True);
                Assert.That(CountItemsInFile(), Is.EqualTo(200));
                Assert.That(await CaptureExceptionAsync(database.WriteAsync(static _ => { })), Is.InstanceOf<ObjectDisposedException>());
                Assert.Throws<ObjectDisposedException>(() => database.Read(static _ => 0));
                Assert.Throws<ObjectDisposedException>(() => database.Write(static _ => { }));
                Assert.Throws<ObjectDisposedException>(() => _ = MajDatabase.Runtime);
                Assert.Throws<ObjectDisposedException>(() => _ = MajDatabase.Scores);

                // An open SQLite database cannot be deleted on Windows, so a successful delete proves the connection was closed.
                var file = MajEnv.RuntimeDirectory.FindFile(MajDatabase.RuntimeDatabaseFileName)!;
                file.Delete();
                Assert.That(file.Exists, Is.False);
            });
        }

        /// <summary>Checks that the shutdown handler is registered once by the first open and closes every open database.</summary>
        [Test]
        public void OnAppQuit_ClosesEveryOpenDatabase()
        {
            var runtime = MajDatabase.Runtime;
            var scores = MajDatabase.Scores;
            Assert.That(PrivateMember.GetStaticField<bool>(typeof(MajDatabase), "s_isQuitHandlerRegistered"), Is.True);

            PrivateMember.InvokeStatic(typeof(MajDatabase), "OnAppQuit", null, null);

            Assert.That(runtime.IsClosed, Is.True);
            Assert.That(scores.IsClosed, Is.True);
            Assert.That(PrivateMember.GetStaticField<bool>(typeof(MajDatabase), "s_isQuitHandlerRegistered"), Is.False);
        }
    }
}
