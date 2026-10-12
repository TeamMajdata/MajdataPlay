#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;

namespace MajdataPlay
{
    /// <summary>Supplies only the settings needed to compile storage's disabled online branches.</summary>
    internal sealed class TestOnlineSettings
    {
        /// <summary>Gets or sets whether the stub online branch is enabled.</summary>
        public bool Enable { get; set; }
    }

    /// <summary>Supplies storage's runtime list settings.</summary>
    internal sealed class TestRuntimeConfig
    {
        /// <summary>Gets the list configuration used to retain selections.</summary>
        public TestListConfig List { get; } = new TestListConfig();
    }

    /// <summary>Supplies only list selections and sort/filter preferences.</summary>
    internal sealed class TestListConfig
    {
        /// <summary>Gets or sets the preferred difficulty.</summary>
        public int SelectedDiff { get; set; }
        /// <summary>Gets or sets the selected chart index.</summary>
        public int SelectedSongIndex { get; set; }
        /// <summary>Gets or sets the selected collection index.</summary>
        public int SelectedDir { get; set; }
        /// <summary>Gets or sets the persisted identity of the selected collection.</summary>
        public Guid SelectedDirGuid { get; set; }
        /// <summary>Gets or sets the persisted hash of the selected chart.</summary>
        public string SelectedSongHash { get; set; } = string.Empty;
        /// <summary>Gets the runtime sort/filter settings.</summary>
        public SongOrder OrderBy { get; } = new SongOrder();
    }

    /// <summary>Supplies the online playlist shape referenced by production storage.</summary>
    internal sealed class MajNetOnlineDanInfo
    {
        /// <summary>Gets the fixture endpoint identity.</summary>
        public string Id { get; init; } = string.Empty;
        /// <summary>Gets the display name.</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Gets the description.</summary>
        public string Description { get; init; } = string.Empty;
        /// <summary>Gets the chart hashes.</summary>
        public string[] SongHashs { get; init; } = Array.Empty<string>();
        /// <summary>Gets the playlist flag.</summary>
        public bool IsPlayList { get; init; }
        /// <summary>Gets the forced game-over flag.</summary>
        public bool IsForceGameover { get; init; }
    }

    /// <summary>Compiles unused online chart branches without network or Unity dependencies.</summary>
    internal sealed class OnlineSongDetail : SongDetail
    {
        /// <summary>Gets constructed remote details so rollback tests can observe unpublished ownership cleanup.</summary>
        public static ConcurrentQueue<OnlineSongDetail> CreatedDetails { get; } = new ConcurrentQueue<OnlineSongDetail>();

        /// <summary>Creates fixture metadata while retaining the caller's authoritative song-cache directory.</summary>
        /// <param name="directory">The already-created per-song cache directory and its backend.</param>
        /// <param name="source">The endpoint supplying the metadata.</param>
        /// <param name="metadata">The fake response metadata.</param>
        public OnlineSongDetail(StorageDirectory directory, ApiEndpoint source, ChartFixture metadata) : base(metadata, directory)
        {
            IsOnline = true;
            CreatedDetails.Enqueue(this);
        }
    }
}

namespace Cysharp.Text
{
    /// <summary>Supplies formatting for production storage progress strings.</summary>
    internal static class ZString
    {
        /// <summary>Formats a progress message without the production pooling implementation.</summary>
        /// <param name="format">The format string.</param>
        /// <param name="arguments">The replacement values.</param>
        /// <returns>The formatted text.</returns>
        public static string Format(string format, params object?[] arguments)
        {
            return string.Format(format, arguments);
        }
    }
}

namespace Cysharp.Threading.Tasks.Linq
{
    /// <summary>Supplies the imported namespace without replacing any scanner implementation.</summary>
    internal static class NamespaceMarker
    {
    }
}

namespace MajdataPlay.Buffers
{
    /// <summary>Supplies the unused storage import without substituting the production pooling code.</summary>
    internal static class NamespaceMarker
    {
    }
}
namespace MajdataPlay.Net
{
    /// <summary>Supplies the endpoint data inspected by production collections and storage.</summary>
    public sealed class ApiEndpoint
    {
        /// <summary>Gets the endpoint display name.</summary>
        public string Name { get; init; } = "fixture";
        /// <summary>Gets the endpoint URI, never contacted by this harness.</summary>
        public Uri Url { get; init; } = new Uri("https://chart-storage.invalid/");
        /// <summary>Gets the authentication boundary.</summary>
        public TestEndpointConfig RuntimeConfig { get; } = new TestEndpointConfig();
    }

    /// <summary>Supplies the endpoint authentication flag.</summary>
    public sealed class TestEndpointConfig
    {
        /// <summary>Gets whether the offline endpoint is authenticated.</summary>
        public bool IsLoggedIn { get; set; }
    }

    /// <summary>Provides explicit in-process chart-list and playlist fixtures without any network execution.</summary>
    internal static class Online
    {
        /// <summary>Gets or sets deterministic offline playlist responses for endpoint refresh cases.</summary>
        public static Func<ApiEndpoint, CancellationToken, Task<MajNetOnlineDanInfo[]?>>? FavoritesHandler { get; set; }
        /// <summary>Gets or sets an explicitly configured in-process chart-list response.</summary>
        public static Func<ApiEndpoint, CancellationToken, Task<ChartFixture[]?>>? ChartListHandler { get; set; }
        /// <summary>Runs a configured offline chart-list fixture or rejects execution outside this harness's scope.</summary>
        /// <param name="endpoint">The requested endpoint.</param>
        /// <param name="cancellationToken">Cancels the deterministic offline API boundary.</param>
        /// <returns>The configured chart-list response without any network access.</returns>
        /// <exception cref="InvalidOperationException">The harness entered an online network branch.</exception>
        /// <exception cref="OperationCanceledException">The caller canceled the fixture request.</exception>
        public static Task<ChartFixture[]?> GetChartListAsync(ApiEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handler = ChartListHandler ?? throw new InvalidOperationException("Online requests are outside the chart-storage harness.");
            return handler(endpoint, cancellationToken);
        }

        /// <summary>Runs only an explicitly configured in-process playlist response.</summary>
        /// <param name="endpoint">The requested endpoint.</param>
        /// <param name="cancellationToken">Cancels the deterministic offline API boundary.</param>
        /// <returns>The configured response task without issuing any network request.</returns>
        /// <exception cref="InvalidOperationException">No offline response handler was configured.</exception>
        /// <exception cref="OperationCanceledException">The request was canceled.</exception>
        public static Task<MajNetOnlineDanInfo[]?> GetUserOnlineFavCollection(ApiEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return FavoritesHandler?.Invoke(endpoint, cancellationToken)
                ?? throw new InvalidOperationException("Online requests are outside the chart-storage harness.");
        }

        /// <summary>Leaves the nonexistent response cache unchanged.</summary>
        public static void ClearResponseCache()
        {
        }
    }
}

namespace MajdataPlay.Utils
{
    /// <summary>Compiles storage's unused remote identity conversion.</summary>
    internal static class ObjectIdHelper
    {
        /// <summary>Converts a fixture identity without claiming to validate MongoDB identities.</summary>
        /// <param name="id">The fixture identity.</param>
        /// <returns>The parsed GUID or an empty GUID.</returns>
        public static Guid ToGuid(string id)
        {
            return Guid.TryParse(id, out var result) ? result : Guid.Empty;
        }
    }
}

namespace SQLite
{
    /// <summary>Supplies flags used to construct the in-memory favorites boundary.</summary>
    [Flags]
    internal enum SQLiteOpenFlags
    {
        /// <summary>Allows reads and writes in production.</summary>
        ReadWrite = 1,
        /// <summary>Allows file creation in production.</summary>
        Create = 2,
        /// <summary>Requests serialized access in production.</summary>
        FullMutex = 4
    }

    /// <summary>Supplies the table attribute on the private production favorites model.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class TableAttribute : Attribute
    {
        /// <summary>Accepts the unused production table name.</summary>
        /// <param name="name">The production table name.</param>
        public TableAttribute(string name)
        {
        }
    }

    /// <summary>Supplies the favorites key attribute without loading native SQLite.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class PrimaryKeyAttribute : Attribute
    {
    }

    /// <summary>Provides an in-memory favorites boundary, not a SQLite integration test.</summary>
    internal sealed class SQLiteAsyncConnection
    {
        /// <summary>Gets or sets an injected favorites table-query failure.</summary>
        public static Exception? QueryException { get; set; }
        /// <summary>Owns the single in-memory favorites table.</summary>
        private readonly SQLiteConnection _connection = new SQLiteConnection();

        /// <summary>Constructs the in-memory substitute without creating a database file.</summary>
        /// <param name="path">The unused database path.</param>
        /// <param name="flags">The unused database flags.</param>
        public SQLiteAsyncConnection(string path, SQLiteOpenFlags flags)
        {
        }

        /// <summary>Completes table initialization without schema or native database work.</summary>
        /// <typeparam name="T">The production row type.</typeparam>
        /// <returns>A completed task.</returns>
        public Task CreateTableAsync<T>()
        {
            return Task.CompletedTask;
        }

        /// <summary>Exposes the in-memory favorites rows.</summary>
        /// <typeparam name="T">The production row type.</typeparam>
        /// <returns>The query substitute.</returns>
        public TestTableQuery<T> Table<T>()
        {
            return new TestTableQuery<T>(_connection.Rows.OfType<T>().ToList());
        }

        /// <summary>Executes a favorites mutation synchronously without emulating SQLite transactions.</summary>
        /// <param name="action">The favorites mutation.</param>
        /// <returns>A completed task after the mutation.</returns>
        public Task RunInTransactionAsync(Action<SQLiteConnection> action)
        {
            action(_connection);
            return Task.CompletedTask;
        }

        /// <summary>Closes no native resources.</summary>
        /// <returns>A completed task.</returns>
        public Task CloseAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Stores fixture favorites using the production Hash property as their key.</summary>
    internal sealed class SQLiteConnection
    {
        /// <summary>Gets the in-memory rows.</summary>
        public List<object> Rows { get; } = new List<object>();

        /// <summary>Appends a favorite row.</summary>
        /// <param name="row">The favorite row.</param>
        public void Insert(object row)
        {
            Rows.Add(row);
        }

        /// <summary>Replaces the favorite having the same chart hash.</summary>
        /// <param name="row">The replacement row.</param>
        public void InsertOrReplace(object row)
        {
            var hash = row.GetType().GetProperty("Hash")!.GetValue(row);
            Rows.RemoveAll(existing => Equals(existing.GetType().GetProperty("Hash")!.GetValue(existing), hash));
            Rows.Add(row);
        }

        /// <summary>Removes the favorite having the specified chart hash.</summary>
        /// <typeparam name="T">The production row type.</typeparam>
        /// <param name="hash">The chart hash to remove.</param>
        public void Delete<T>(string hash)
        {
            Rows.RemoveAll(existing => Equals(existing.GetType().GetProperty("Hash")!.GetValue(existing), hash));
        }
    }

    /// <summary>Supplies only materialization of the in-memory favorite rows.</summary>
    /// <typeparam name="T">The production row type.</typeparam>
    internal sealed class TestTableQuery<T>
    {
        /// <summary>Owns the copied row snapshot.</summary>
        private readonly List<T> _rows;

        /// <summary>Stores the snapshot returned by the connection boundary.</summary>
        /// <param name="rows">The current in-memory rows.</param>
        public TestTableQuery(List<T> rows)
        {
            _rows = rows;
        }

        /// <summary>Returns a copy of the in-memory favorites.</summary>
        /// <returns>A completed row snapshot.</returns>
        public Task<List<T>> ToListAsync()
        {
            var exception = SQLiteAsyncConnection.QueryException;
            return exception is null ? Task.FromResult(new List<T>(_rows)) : Task.FromException<List<T>>(exception);
        }
    }
}
