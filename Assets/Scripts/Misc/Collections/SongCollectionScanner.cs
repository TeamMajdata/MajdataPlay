using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Collections
{
    /// <summary>Scans local song collections with a shared, bounded chart-loading budget.</summary>
    /// <remarks>One instance may scan multiple collections concurrently; counters accumulate across all scans.</remarks>
    internal sealed class SongCollectionScanner : IDisposable
    {
        /// <summary>Gets the cumulative number of directories containing both chart metadata and a supported track.</summary>
        public long DiscoveredChartCount
        {
            get
            {
                return Interlocked.Read(ref _discoveredChartCount);
            }
        }

        /// <summary>Gets the cumulative number of discovered candidates whose loading attempt has finished.</summary>
        public long ProcessedChartCount
        {
            get
            {
                return Interlocked.Read(ref _processedChartCount);
            }
        }

        /// <summary>Gets the cumulative number of charts successfully parsed, including results of subsequently canceled scans.</summary>
        public long LoadedChartCount
        {
            get
            {
                return Interlocked.Read(ref _loadedChartCount);
            }
        }

        /// <summary>Controls discovery, worker admission and metadata reading for every scan.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Bounds chart validation and parsing across all collections scanned by this instance.</summary>
        private readonly SemaphoreSlim _parseSemaphore;

        /// <summary>Stores the maximum worker count for one collection and the shared parsing budget.</summary>
        private readonly int _maxConcurrency;

        /// <summary>Protects scan admission and deferred semaphore disposal.</summary>
        private readonly object _lifetimeLock = new();

        /// <summary>Counts admitted scans until all their workers have been observed.</summary>
        private int _activeScanCount = 0;

        /// <summary>Records whether new scans have been disabled by disposal.</summary>
        private bool _isDisposed = false;

        /// <summary>Counts valid chart candidates across all scans.</summary>
        private long _discoveredChartCount = 0;

        /// <summary>Counts completed loading attempts across all scans.</summary>
        private long _processedChartCount = 0;

        /// <summary>Counts successfully parsed charts across all scans.</summary>
        private long _loadedChartCount = 0;

        /// <summary>Creates a reusable scanner with a processor-count-sized shared concurrency budget.</summary>
        /// <param name="cancellationToken">The token controlling every collection scanned by this instance.</param>
        public SongCollectionScanner(CancellationToken cancellationToken = default)
        {
            _cancellationToken = cancellationToken;
            _maxConcurrency = Math.Max(1, Environment.ProcessorCount);
            _parseSemaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        }

        /// <summary>Scans a directory on background workers while retaining its original storage backend.</summary>
        /// <param name="directory">The collection whose immediate visible child directories contain charts.</param>
        /// <returns>The loaded charts in ascending creation-time order, with ordinal name and location tie breakers.</returns>
        /// <remarks>
        /// Individual chart errors are logged and do not discard neighboring charts. Collection enumeration errors fail the scan.
        /// Synchronous provider operations cannot be interrupted while blocked; cancellation is checked between operations.
        /// Metadata reading receives the token, but the upstream parser has no token overload and is checked before and after parsing.
        /// All workers finish and their tasks are observed before partial results are released or a scan completes.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="ObjectDisposedException">The scanner no longer accepts scans.</exception>
        /// <exception cref="OperationCanceledException">Discovery or chart loading was canceled.</exception>
        /// <exception cref="IOException">The collection directory could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied collection enumeration.</exception>
        /// <exception cref="NotSupportedException">The backend does not support collection enumeration.</exception>
        public Task<ISongDetail[]> ScanAsync(StorageDirectory directory)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            lock (_lifetimeLock)
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(nameof(SongCollectionScanner));
                }
                _activeScanCount++;
            }
            try
            {
                // Do not cancel scheduling: the admitted scan must always reach its lifetime cleanup.
                return Task.Run(() => ScanAndReleaseAsync(directory));
            }
            catch
            {
                CompleteScan();
                throw;
            }
        }

        /// <summary>Runs one admitted scan and releases its lifetime reservation on every exit path.</summary>
        /// <param name="directory">The collection to scan.</param>
        /// <returns>The successfully loaded charts in storage order.</returns>
        /// <exception cref="OperationCanceledException">The scan was canceled.</exception>
        /// <exception cref="IOException">The collection could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied collection enumeration.</exception>
        /// <exception cref="NotSupportedException">The backend does not support enumeration.</exception>
        private async Task<ISongDetail[]> ScanAndReleaseAsync(StorageDirectory directory)
        {
            try
            {
                return await ScanCoreAsync(directory).ConfigureAwait(false);
            }
            finally
            {
                CompleteScan();
            }
        }

        /// <summary>Captures ordered child directories and waits for a fixed number of indexed workers.</summary>
        /// <param name="directory">The original collection storage handle.</param>
        /// <returns>A compact array of successfully loaded charts in the captured child order.</returns>
        /// <exception cref="OperationCanceledException">Discovery or loading was canceled.</exception>
        /// <exception cref="IOException">The collection could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied collection enumeration.</exception>
        /// <exception cref="NotSupportedException">The backend does not support enumeration.</exception>
        private async Task<ISongDetail[]> ScanCoreAsync(StorageDirectory directory)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var directories = new List<FileSystemEntry>();
            foreach (var entry in directory.EnumerateEntries())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!entry.IsDirectory || entry.IsHidden)
                {
                    continue;
                }
                directories.Add(entry);
            }
            _cancellationToken.ThrowIfCancellationRequested();
            if (directories.Count == 0)
            {
                return Array.Empty<ISongDetail>();
            }
            var ordered = directories.OrderBy(entry => entry.CreationTimeUtc)
                                     .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                                     .ThenBy(entry => entry.Location, StringComparer.Ordinal)
                                     .ToArray();
            var scan = new ScanState(directory, ordered);
            var workers = new Task[Math.Min(_maxConcurrency, ordered.Length)];
            for (var i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.CompletedTask;
            }
            try
            {
                try
                {
                    for (var i = 0; i < workers.Length; i++)
                    {
                        workers[i] = LoadChartsAsync(scan);
                    }
                }
                finally
                {
                    // Observe every started worker, even if starting a later worker fails.
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                _cancellationToken.ThrowIfCancellationRequested();
                var loadedCount = 0;
                foreach (var song in scan.Songs)
                {
                    if (song is not null)
                    {
                        loadedCount++;
                    }
                }
                var result = new ISongDetail[loadedCount];
                var resultIndex = 0;
                foreach (var song in scan.Songs)
                {
                    if (song is not null)
                    {
                        result[resultIndex++] = song;
                    }
                }
                _cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch
            {
                // WhenAll has already observed every worker before any partial song can be released.
                DisposeSongs(scan.Songs);
                throw;
            }
        }

        /// <summary>Claims exclusive result slots and validates and parses charts within the shared concurrency budget.</summary>
        /// <param name="scan">The ordered directories, exclusive result slots and shared claim cursor.</param>
        /// <returns>A task completing after this worker has exhausted its assigned candidates.</returns>
        /// <exception cref="OperationCanceledException">Worker admission, validation or parsing was canceled.</exception>
        private async Task LoadChartsAsync(ScanState scan)
        {
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var index = Interlocked.Increment(ref scan.NextIndex);
                if (index >= scan.Directories.Length)
                {
                    return;
                }
                await _parseSemaphore.WaitAsync(_cancellationToken).ConfigureAwait(false);
                var isCandidate = false;
                var entry = scan.Directories[index];
                try
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var directory = new StorageDirectory(scan.Directory.FileSystem, entry.Location);
                    var hasTrack = false;
                    foreach (var file in directory.EnumerateEntries())
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        if (!file.IsDirectory && SongStorage.SupportedTrackFileNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            hasTrack = true;
                            break;
                        }
                    }
                    if (!hasTrack || directory.FindFile(SongStorage.ChartFileName) is null)
                    {
                        continue;
                    }
                    _cancellationToken.ThrowIfCancellationRequested();
                    isCandidate = true;
                    Interlocked.Increment(ref _discoveredChartCount);
                    var song = await SongDetail.ParseAsync(directory, _cancellationToken).ConfigureAwait(false);
                    scan.Songs[index] = song;
                    Interlocked.Increment(ref _loadedChartCount);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    MajDebug.LogError($"[SongCollectionScanner]Failed to load chart directory '{entry.Location}': {exception}");
                }
                finally
                {
                    if (isCandidate)
                    {
                        Interlocked.Increment(ref _processedChartCount);
                    }
                    _parseSemaphore.Release();
                }
            }
        }

        /// <summary>Releases partially loaded charts after all workers have finished.</summary>
        /// <param name="songs">The result slots containing charts from an unsuccessful scan.</param>
        private static void DisposeSongs(ISongDetail?[] songs)
        {
            foreach (var song in songs)
            {
                if (song is SongDetail localSong)
                {
                    try
                    {
                        localSong.Dispose();
                    }
                    catch (Exception exception)
                    {
                        MajDebug.LogError($"[SongCollectionScanner]Failed to release chart '{song.Hash}': {exception}");
                    }
                }
            }
        }

        /// <summary>Ends an admitted scan and performs deferred disposal after the last scan's workers finish.</summary>
        private void CompleteScan()
        {
            lock (_lifetimeLock)
            {
                _activeScanCount--;
                if (_isDisposed && _activeScanCount == 0)
                {
                    _parseSemaphore.Dispose();
                }
            }
        }

        /// <summary>Rejects new scans and disposes the concurrency gate once all admitted scans have completed.</summary>
        /// <remarks>In-flight scans continue under the constructor token; disposal never invalidates a worker's gate.</remarks>
        public void Dispose()
        {
            lock (_lifetimeLock)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
                if (_activeScanCount == 0)
                {
                    _parseSemaphore.Dispose();
                }
            }
        }

        /// <summary>Stores one collection's ordered inputs and uniquely claimed result slots.</summary>
        private sealed class ScanState
        {
            /// <summary>Gets the original storage backend handle.</summary>
            public StorageDirectory Directory { get; }

            /// <summary>Gets child-directory metadata in deterministic storage order.</summary>
            public FileSystemEntry[] Directories { get; }

            /// <summary>Gets the result array whose slots are written by exactly one worker.</summary>
            public ISongDetail?[] Songs { get; }

            /// <summary>Stores the last claimed result index for atomic worker admission.</summary>
            public int NextIndex = -1;

            /// <summary>Creates per-collection worker state without sharing mutable result lists.</summary>
            /// <param name="directory">The original backend handle.</param>
            /// <param name="directories">The ordered chart-directory metadata.</param>
            public ScanState(StorageDirectory directory, FileSystemEntry[] directories)
            {
                Directory = directory;
                Directories = directories;
                Songs = new ISongDetail?[directories.Length];
            }
        }
    }
}
