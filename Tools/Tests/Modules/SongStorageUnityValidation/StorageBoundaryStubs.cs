#nullable enable
using MajdataPlay.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Net
{
    /// <summary>Supplies the progress contract used by the real song detail.</summary>
    public interface INetProgress
    {
        /// <summary>Reports a completed fraction to an unused media/network boundary.</summary>
        /// <param name="value">The completed fraction.</param>
        void Report(float value);
        /// <summary>Gets or sets the expected transfer length required by the compiled download implementation.</summary>
        long TotalBytes { get; set; }
        /// <summary>Gets or sets the transferred byte count required by the compiled download implementation.</summary>
        long TransferredBytes { get; set; }
    }

    /// <summary>Supplies only endpoint fields inspected by production storage.</summary>
    public sealed class ApiEndpoint
    {
        /// <summary>Gets the offline fixture endpoint name.</summary>
        public string Name { get; init; } = "fixture";
        /// <summary>Gets a non-routable URI that is never contacted.</summary>
        public Uri Url { get; init; } = new Uri("https://song-storage.invalid/");
        /// <summary>Gets the unauthenticated runtime settings.</summary>
        public ValidationEndpointConfig RuntimeConfig { get; } = new ValidationEndpointConfig();
    }

    /// <summary>Supplies the authentication flag without a real account.</summary>
    public sealed class ValidationEndpointConfig
    {
        /// <summary>Gets whether the offline endpoint is authenticated.</summary>
        public bool IsLoggedIn { get; init; }
    }

    /// <summary>Rejects any unexpected network request from the offline harness.</summary>
    internal static class Online
    {
        /// <summary>Stores the one endpoint explicitly supplied with an offline chart-list fixture.</summary>
        private static ApiEndpoint? s_fixtureSource;
        /// <summary>Stores offline DTOs; production online-song construction still executes unchanged.</summary>
        private static MajnetSongDetail[]? s_fixtureCharts;
        /// <summary>Counts every chart-list boundary entry, including rejected unexpected requests.</summary>
        private static int s_chartListRequestCount;
        /// <summary>Gets the number of chart-list requests attempted by the production caller.</summary>
        public static int ChartListRequestCount => Volatile.Read(ref s_chartListRequestCount);

        /// <summary>Supplies one offline chart-list response for a real SongStorage construction assertion.</summary>
        /// <param name="source">The endpoint allowed to read the fixture DTOs.</param>
        /// <param name="charts">The fixture DTO snapshot, or null for cached fallback; no HTTP request is made.</param>
        public static void SupplyChartList(ApiEndpoint source, MajnetSongDetail[]? charts)
        {
            s_fixtureSource = source;
            s_fixtureCharts = charts?.ToArray();
        }

        /// <summary>Returns only an explicitly supplied DTO fixture and rejects all other chart-list requests.</summary>
        /// <param name="source">The endpoint whose fixture response is requested.</param>
        /// <param name="cancellationToken">The token checked before returning the offline response.</param>
        /// <returns>A copy of the explicitly supplied fixture list; network execution is forbidden.</returns>
        /// <exception cref="NotSupportedException">A network request was attempted.</exception>
        public static Task<MajnetSongDetail[]?> GetChartListAsync(ApiEndpoint source, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref s_chartListRequestCount);
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(source, s_fixtureSource))
            {
                return Task.FromResult<MajnetSongDetail[]?>(s_fixtureCharts?.ToArray());
            }
            throw new NotSupportedException("Network requests are outside the metadata harness.");
        }

        /// <summary>Rejects online favorite requests.</summary>
        /// <param name="source">The unused endpoint.</param>
        /// <param name="cancellationToken">The unused request token.</param>
        /// <returns>No list; network execution is forbidden.</returns>
        /// <exception cref="NotSupportedException">A network request was attempted.</exception>
        public static Task<MajNetOnlineDanInfo[]?> GetUserOnlineFavCollection(ApiEndpoint source, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Network requests are outside the metadata harness.");
        }

        /// <summary>Clears only the explicitly supplied offline response, never remote state.</summary>
        public static void ClearResponseCache()
        {
            s_fixtureSource = null;
            s_fixtureCharts = null;
        }
    }
}

namespace MajdataPlay
{
    /// <summary>Supplies only the remote playlist shape needed to compile disabled branches.</summary>
    internal sealed class MajNetOnlineDanInfo
    {
        /// <summary>Gets the unused remote identity.</summary>
        public string Id { get; init; } = string.Empty;
        /// <summary>Gets the display name.</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Gets the description.</summary>
        public string Description { get; init; } = string.Empty;
        /// <summary>Gets the requested chart hashes.</summary>
        public string[] SongHashs { get; init; } = Array.Empty<string>();
        /// <summary>Gets whether this is a playlist.</summary>
        public bool IsPlayList { get; init; }
        /// <summary>Gets the unused gameplay rule.</summary>
        public bool IsForceGameover { get; init; }
    }
}

namespace SQLite
{
    /// <summary>Supplies only the database flags requested by production storage.</summary>
    [Flags]
    internal enum SQLiteOpenFlags
    {
        /// <summary>Allows reading and writing.</summary>
        ReadWrite = 1,
        /// <summary>Allows creation.</summary>
        Create = 2,
        /// <summary>Requests serialized access.</summary>
        FullMutex = 4
    }

    /// <summary>Compiles the real private favorite-row attribute.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class TableAttribute : Attribute
    {
        /// <summary>Accepts the unused table name.</summary>
        /// <param name="name">The unused table name.</param>
        public TableAttribute(string name)
        {
        }
    }

    /// <summary>Compiles the real favorite primary-key attribute.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class PrimaryKeyAttribute : Attribute
    {
    }

    /// <summary>Keeps favorite rows in memory instead of loading SQLite native code.</summary>
    internal sealed class SQLiteAsyncConnection
    {
        /// <summary>Stores the in-memory transaction boundary.</summary>
        private readonly SQLiteConnection _connection = new SQLiteConnection();

        /// <summary>Creates no database file.</summary>
        /// <param name="path">The unused database path.</param>
        /// <param name="flags">The unused open flags.</param>
        public SQLiteAsyncConnection(string path, SQLiteOpenFlags flags)
        {
        }

        /// <summary>Creates no native table.</summary>
        /// <typeparam name="T">The real favorite row type.</typeparam>
        /// <returns>A completed operation.</returns>
        public Task CreateTableAsync<T>()
        {
            return Task.CompletedTask;
        }

        /// <summary>Captures rows from the in-memory boundary.</summary>
        /// <typeparam name="T">The row type.</typeparam>
        /// <returns>A copied row query.</returns>
        public ValidationTableQuery<T> Table<T>()
        {
            return new ValidationTableQuery<T>(_connection.Rows.OfType<T>().ToList());
        }

        /// <summary>Executes one fixture transaction in memory.</summary>
        /// <param name="action">The production favorite operation.</param>
        /// <returns>A completed operation.</returns>
        public Task RunInTransactionAsync(Action<SQLiteConnection> action)
        {
            action(_connection);
            return Task.CompletedTask;
        }

        /// <summary>Closes no native connection.</summary>
        /// <returns>A completed operation.</returns>
        public Task CloseAsync()
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Supplies the insert/delete surface without database persistence.</summary>
    internal sealed class SQLiteConnection
    {
        /// <summary>Gets the in-memory rows.</summary>
        public List<object> Rows { get; } = new List<object>();

        /// <summary>Appends an in-memory favorite.</summary>
        /// <param name="row">The favorite row.</param>
        public void Insert(object row)
        {
            Rows.Add(row);
        }

        /// <summary>Replaces the favorite having the same hash.</summary>
        /// <param name="row">The replacement row.</param>
        public void InsertOrReplace(object row)
        {
            var hash = row.GetType().GetProperty("Hash")!.GetValue(row);
            Rows.RemoveAll(existing => Equals(existing.GetType().GetProperty("Hash")!.GetValue(existing), hash));
            Rows.Add(row);
        }

        /// <summary>Removes an in-memory favorite.</summary>
        /// <typeparam name="T">The real favorite row type.</typeparam>
        /// <param name="hash">The hash to remove.</param>
        public void Delete<T>(string hash)
        {
            Rows.RemoveAll(existing => Equals(existing.GetType().GetProperty("Hash")!.GetValue(existing), hash));
        }
    }

    /// <summary>Supplies materialization of a copied in-memory row list.</summary>
    /// <typeparam name="T">The real favorite row type.</typeparam>
    internal sealed class ValidationTableQuery<T>
    {
        /// <summary>Stores the copied row snapshot.</summary>
        private readonly List<T> _rows;

        /// <summary>Captures the current favorite rows.</summary>
        /// <param name="rows">The copied row list.</param>
        public ValidationTableQuery(List<T> rows)
        {
            _rows = rows;
        }

        /// <summary>Returns the copied in-memory rows.</summary>
        /// <returns>A completed task containing the row snapshot.</returns>
        public Task<List<T>> ToListAsync()
        {
            return Task.FromResult(new List<T>(_rows));
        }
    }
}
