using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using SQLite;
using System;
using System.IO;
using System.Threading.Tasks;

#nullable enable
namespace MajdataPlay
{
    /// <summary>
    /// Owns every SQLite database the game keeps open: it resolves their files, opens one shared connection per file on
    /// first access and closes them in order when the application quits.
    /// </summary>
    /// <remarks>
    /// Callers only reach a database through its <see cref="MajDatabaseHandle"/>, so no caller can close a connection
    /// another one still uses. Table definitions and data migrations stay with the code that owns the data.
    /// </remarks>
    internal static class MajDatabase
    {
        /// <summary>The runtime database file name inside <see cref="MajEnv.RuntimeDirectory"/>, which holds the runtime configuration and favorites.</summary>
        internal const string RuntimeDatabaseFileName = "runtime.db";
        /// <summary>The score database file name inside <see cref="MajEnv.PersistentDirectory"/>.</summary>
        internal const string ScoresDatabaseFileName = "MajScores.db";

        /// <summary>Gets the runtime database, opening it on first access.</summary>
        /// <remarks>The first access creates <see cref="RuntimeDatabaseFileName"/> in <see cref="MajEnv.RuntimeDirectory"/> when missing.</remarks>
        /// <exception cref="ObjectDisposedException">The databases have been closed for shutdown.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating the database file was denied.</exception>
        /// <exception cref="IOException">Creating the database file failed.</exception>
        /// <exception cref="SQLiteException">The database could not be opened.</exception>
        public static MajDatabaseHandle Runtime
        {
            get
            {
                lock (s_runtimeLock)
                {
                    ThrowIfShutDown();
                    return s_runtime ??= Open(nameof(Runtime), ResolveRuntimeFile());
                }
            }
        }
        /// <summary>Gets the score database, opening it on first access.</summary>
        /// <remarks>
        /// The first access may copy a database left in <see cref="MajEnv.RootDirectory"/> into
        /// <see cref="MajEnv.PersistentDirectory"/>, so it should run off the main thread.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The databases have been closed for shutdown.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating the database file was denied.</exception>
        /// <exception cref="IOException">Creating the database file failed.</exception>
        /// <exception cref="SQLiteException">The database could not be opened.</exception>
        public static MajDatabaseHandle Scores
        {
            get
            {
                lock (s_scoresLock)
                {
                    ThrowIfShutDown();
                    return s_scores ??= Open(nameof(Scores), ResolveScoresFile());
                }
            }
        }

        /// <summary>The temporary file name receiving a score database streamed from a root other than the persistent directory.</summary>
        private const string ImportingScoresDatabaseFileName = "MajScores.db.importing";
        /// <summary>The content type requested for database files created on a provider.</summary>
        private const string DatabaseMimeType = "application/vnd.sqlite3";
        /// <summary>The flags every managed connection is opened with.</summary>
        private const SQLiteOpenFlags OpenFlags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex;
        /// <summary>The longest time, in milliseconds, application shutdown waits for queued writes.</summary>
        private const int ShutdownTimeoutMs = 5000;

        /// <summary>Guards <see cref="s_runtime"/>.</summary>
        private static readonly object s_runtimeLock = new();
        /// <summary>Guards <see cref="s_scores"/>.</summary>
        private static readonly object s_scoresLock = new();
        /// <summary>Guards <see cref="s_isShutDown"/> and <see cref="s_isQuitHandlerRegistered"/>.</summary>
        private static readonly object s_stateLock = new();
        /// <summary>Stores the open runtime database, or null before the first access.</summary>
        private static MajDatabaseHandle? s_runtime;
        /// <summary>Stores the open score database, or null before the first access.</summary>
        private static MajDatabaseHandle? s_scores;
        /// <summary>Records whether the databases have been closed for shutdown; they are not opened again afterwards.</summary>
        private static bool s_isShutDown;
        /// <summary>Records whether <see cref="OnAppQuit"/> has been registered.</summary>
        private static bool s_isQuitHandlerRegistered;

        /// <summary>Closes every open database after its queued writes, and refuses to open databases afterwards.</summary>
        /// <returns>A task that completes when every open database is closed.</returns>
        /// <exception cref="SQLiteException">Closing a connection failed.</exception>
        internal static async Task CloseAllAsync()
        {
            lock (s_stateLock)
            {
                s_isShutDown = true;
            }
            MajDatabaseHandle? runtime;
            MajDatabaseHandle? scores;
            lock (s_runtimeLock)
            {
                runtime = s_runtime;
                s_runtime = null;
            }
            lock (s_scoresLock)
            {
                scores = s_scores;
                s_scores = null;
            }
            var runtimeClose = runtime?.CloseAsync() ?? Task.CompletedTask;
            var scoresClose = scores?.CloseAsync() ?? Task.CompletedTask;
            await Task.WhenAll(runtimeClose, scoresClose).ConfigureAwait(false);
        }
        /// <summary>Opens a connection to a database file and registers the shutdown handler once.</summary>
        /// <param name="name">The name of the database.</param>
        /// <param name="file">The existing database file.</param>
        /// <returns>The handle owning the new connection.</returns>
        /// <exception cref="SQLiteException">The database could not be opened.</exception>
        private static MajDatabaseHandle Open(string name, StorageFile file)
        {
            var handle = new MajDatabaseHandle(name, new SQLiteConnection(file.Location, OpenFlags));
            RegisterQuitHandler();
            MajDebug.LogDebug(nameof(MajDatabase), $"Opened the {name} database: {file.Location}");
            return handle;
        }
        /// <summary>Finds or creates the runtime database file.</summary>
        /// <returns>The runtime database file.</returns>
        /// <exception cref="UnauthorizedAccessException">Creating the file was denied.</exception>
        /// <exception cref="IOException">Creating the file failed.</exception>
        private static StorageFile ResolveRuntimeFile()
        {
            var runtimeDirectory = MajEnv.RuntimeDirectory;
            // SQLite needs a resolvable location, so the file is created through the directory before the connection opens it.
            return runtimeDirectory.FindFile(RuntimeDatabaseFileName)
                ?? runtimeDirectory.CreateFile(RuntimeDatabaseFileName, DatabaseMimeType);
        }
        /// <summary>Finds the score database file in the persistent directory, importing it from the root or creating it when needed.</summary>
        /// <returns>The score database file.</returns>
        /// <remarks>SQLite opens the database by filename, so it lives in the persistent directory, which is always a native path.</remarks>
        /// <exception cref="UnauthorizedAccessException">Creating the file was denied.</exception>
        /// <exception cref="IOException">Creating the file failed.</exception>
        private static StorageFile ResolveScoresFile()
        {
            var rootDirectory = MajEnv.RootDirectory;
            var persistentDirectory = MajEnv.PersistentDirectory;
            var databaseFile = persistentDirectory.FindFile(ScoresDatabaseFileName);
            // SQLite leaves a database empty until its first table is created, so an empty file is still a new database.
            var isDbExists = databaseFile is not null && databaseFile.Entry is { Length: > 0 };
            if (!isDbExists && !ReferenceEquals(rootDirectory, persistentDirectory))
            {
                // MajEnv shares one handle when the root is the persistent directory; any other root may still hold the database.
                databaseFile = ImportRootScoresFile(rootDirectory, persistentDirectory) ?? databaseFile;
            }
            return databaseFile ?? persistentDirectory.CreateFile(ScoresDatabaseFileName, DatabaseMimeType);
        }
        /// <summary>Copies a score database left in a root other than the persistent directory, such as an Android SAF tree or the legacy shared folder, into the persistent directory once.</summary>
        /// <param name="rootDirectory">The application root which may still hold a database.</param>
        /// <param name="persistentDirectory">The native directory owning the live database.</param>
        /// <returns>The imported database file, or null when the root holds no database or the import failed.</returns>
        /// <remarks>
        /// The database is streamed into a temporary sibling which then replaces an empty persistent database by rename,
        /// so a failed import never leaves a partial database under the live name. The copy in the root stays in place as
        /// a backup and is not read again while a non-empty persistent database exists.
        /// </remarks>
        private static StorageFile? ImportRootScoresFile(StorageDirectory rootDirectory, StorageDirectory persistentDirectory)
        {
            try
            {
                var rootFile = rootDirectory.FindFile(ScoresDatabaseFileName);
                // A provider may not report sizes, so only a database known to be empty is skipped.
                if (rootFile is null || rootFile.Entry is { Length: 0 })
                {
                    return null;
                }
                var stagingFile = rootFile.CopyTo(
                    persistentDirectory,
                    ImportingScoresDatabaseFileName,
                    overwrite: true,
                    cancellationToken: MajEnv.GlobalCT);
                persistentDirectory.FindFile(ScoresDatabaseFileName)?.Delete();
                var importedFile = stagingFile.Rename(ScoresDatabaseFileName);
                MajDebug.LogInfo(nameof(MajDatabase), $"Imported the score database from the root: {rootFile.Location} -> {importedFile.Location}");
                return importedFile;
            }
            catch (Exception e)
            {
                MajDebug.LogError(nameof(MajDatabase), $"Failed to import the score database from the root: {e}");
                return null;
            }
        }
        /// <summary>Registers <see cref="OnAppQuit"/> the first time a database opens.</summary>
        private static void RegisterQuitHandler()
        {
            lock (s_stateLock)
            {
                if (s_isQuitHandlerRegistered)
                {
                    return;
                }
                s_isQuitHandlerRegistered = true;
                GameManager.OnAppQuit += OnAppQuit;
            }
        }
        /// <summary>Throws when the databases have been closed for shutdown.</summary>
        /// <exception cref="ObjectDisposedException">The databases have been closed.</exception>
        private static void ThrowIfShutDown()
        {
            lock (s_stateLock)
            {
                if (s_isShutDown)
                {
                    throw new ObjectDisposedException(nameof(MajDatabase), "The databases have been closed for shutdown.");
                }
            }
        }
        /// <summary>Unsubscribes the shutdown handler and closes every open database after its queued writes.</summary>
        /// <param name="sender">The source of the shutdown event.</param>
        /// <param name="args">The shutdown event arguments.</param>
        /// <remarks>
        /// When the writes do not finish within <see cref="ShutdownTimeoutMs"/>, the remaining connections are left to the
        /// process exit instead of being closed under a running transaction.
        /// </remarks>
        private static void OnAppQuit(object? sender, EventArgs? args)
        {
            GameManager.OnAppQuit -= OnAppQuit;
            lock (s_stateLock)
            {
                s_isQuitHandlerRegistered = false;
            }
            try
            {
                // Every continuation uses ConfigureAwait(false), so blocking the main thread here cannot deadlock.
                if (!CloseAllAsync().Wait(ShutdownTimeoutMs))
                {
                    MajDebug.LogWarning(nameof(MajDatabase), "Database writes did not finish before shutdown; the remaining connections were left open.");
                }
            }
            catch (AggregateException e)
            {
                MajDebug.LogException(nameof(MajDatabase), e.GetBaseException());
            }
        }
    }
}
