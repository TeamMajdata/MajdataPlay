using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Collections
{
    /// <summary>Scans local song collections on dedicated worker threads shared by every scan of one instance.</summary>
    /// <remarks>
    /// Chart discovery, reading and parsing block on storage I/O; on Android shared storage every call is served by the
    /// MediaProvider FUSE daemon and can block for a long time. Running that work on the scanner's own threads keeps it off
    /// the thread pool, which replaces blocked workers only slowly, and lets each chart finish without any thread-pool hop.
    /// One instance may scan multiple collections concurrently; counters accumulate across all scans. Workers start on
    /// demand, take collection enumerations before chart loads, and exit as soon as no work is queued.
    /// </remarks>
    internal sealed class SongCollectionScanner : IDisposable
    {
        /// <summary>The maximum number of worker threads; more threads do not speed up storage that serializes requests.</summary>
        public const int MaxWorkerCount = 8;

        /// <summary>The name given to worker threads, so they can be identified in profilers and native traces.</summary>
        private const string WorkerThreadName = "Chart scanner";

        /// <summary>The minimum interval between two progress notifications, in <see cref="Stopwatch"/> ticks.</summary>
        private static readonly long s_progressIntervalTicks = Stopwatch.Frequency * 33 / 1000;

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

        /// <summary>Gets the cumulative number of visible child directories found in scanned collections, each a potential chart.</summary>
        public long DirectoryCount
        {
            get
            {
                return Interlocked.Read(ref _directoryCount);
            }
        }

        /// <summary>Gets the cumulative number of found child directories whose scan has finished, whether or not they held a chart.</summary>
        public long ScannedDirectoryCount
        {
            get
            {
                return Interlocked.Read(ref _scannedDirectoryCount);
            }
        }

        /// <summary>Controls discovery, chart loading and metadata reading for every scan.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Receives the scanned fraction of all found directories, or null when nobody observes progress.</summary>
        private readonly Action<double>? _progressChanged;

        /// <summary>Stores the maximum number of concurrently running workers.</summary>
        private readonly int _maxWorkerCount;

        /// <summary>Protects both work queues and the running worker count.</summary>
        private readonly object _queueLock = new();

        /// <summary>Holds collections waiting to be enumerated; workers take them before any chart.</summary>
        private readonly Queue<WorkItem> _pendingCollections = new();

        /// <summary>Holds chart directories waiting to be loaded, in collection and chart order.</summary>
        private readonly Queue<WorkItem> _pendingCharts = new();

        /// <summary>Counts workers that are started or reserved and have not exited yet.</summary>
        private int _workerCount = 0;

        /// <summary>Serializes progress notifications and protects their throttling state.</summary>
        private readonly object _progressLock = new();

        /// <summary>Records whether a progress notification has been sent, so the first one is never throttled.</summary>
        private bool _hasReportedProgress = false;

        /// <summary>The <see cref="Stopwatch"/> timestamp of the last progress notification.</summary>
        private long _lastProgressTimestamp = 0;

        /// <summary>The last reported fraction, which later notifications never go below.</summary>
        private double _lastReportedProgress = 0;

        /// <summary>Records whether new scans have been disabled by disposal.</summary>
        private volatile bool _isDisposed = false;

        /// <summary>Counts valid chart candidates across all scans.</summary>
        private long _discoveredChartCount = 0;

        /// <summary>Counts completed loading attempts across all scans.</summary>
        private long _processedChartCount = 0;

        /// <summary>Counts successfully parsed charts across all scans.</summary>
        private long _loadedChartCount = 0;

        /// <summary>Counts found child directories across all scans.</summary>
        private long _directoryCount = 0;

        /// <summary>Counts finished child directories across all scans.</summary>
        private long _scannedDirectoryCount = 0;

        /// <summary>Creates a reusable scanner whose worker count follows the processor count, up to <see cref="MaxWorkerCount"/>.</summary>
        /// <param name="cancellationToken">The token controlling every collection scanned by this instance.</param>
        /// <param name="progressChanged">
        /// Receives the scanned fraction of all directories found so far, between 0 and 1, on a worker thread. Notifications
        /// are throttled, never decrease and may omit the final state, so callers report completion themselves.
        /// </param>
        public SongCollectionScanner(CancellationToken cancellationToken = default, Action<double>? progressChanged = null)
        {
            _cancellationToken = cancellationToken;
            _progressChanged = progressChanged;
            _maxWorkerCount = Math.Clamp(Environment.ProcessorCount, 1, MaxWorkerCount);
        }

        /// <summary>Scans a directory on the scanner's worker threads while retaining its original storage backend.</summary>
        /// <param name="directory">The collection whose immediate visible child directories contain charts.</param>
        /// <returns>The loaded charts in ascending creation-time order, with ordinal name and location tie breakers.</returns>
        /// <remarks>
        /// Individual chart errors are logged and do not discard neighboring charts. Collection enumeration errors fail the scan.
        /// Synchronous provider operations cannot be interrupted while blocked; cancellation is checked between operations.
        /// The upstream metadata parser has no token overload and is checked before and after parsing.
        /// Every chart of the scan has finished before partial results are released or the returned task completes.
        /// </remarks>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        /// <exception cref="ObjectDisposedException">The scanner no longer accepts scans.</exception>
        /// <exception cref="OperationCanceledException">Discovery or chart loading was canceled.</exception>
        /// <exception cref="System.IO.IOException">The collection directory could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied collection enumeration.</exception>
        /// <exception cref="NotSupportedException">The backend does not support collection enumeration.</exception>
        public Task<ISongDetail[]> ScanAsync(StorageDirectory directory)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SongCollectionScanner));
            }
            var scan = new ScanState(directory);
            int workersToStart;
            lock (_queueLock)
            {
                _pendingCollections.Enqueue(new WorkItem(scan, WorkItem.CollectionIndex));
                workersToStart = ReserveWorkers();
            }
            StartWorkers(workersToStart);
            return scan.Completion.Task;
        }

        /// <summary>Rejects new scans; admitted scans continue under the constructor token and their workers exit when idle.</summary>
        public void Dispose()
        {
            _isDisposed = true;
        }

        /// <summary>Reserves worker slots for queued work; must be called while holding <see cref="_queueLock"/>.</summary>
        /// <returns>The number of workers the caller must start after releasing the lock.</returns>
        private int ReserveWorkers()
        {
            var pendingCount = _pendingCollections.Count + _pendingCharts.Count;
            var count = Math.Min(_maxWorkerCount - _workerCount, pendingCount);
            if (count <= 0)
            {
                return 0;
            }
            _workerCount += count;
            return count;
        }

        /// <summary>Starts reserved workers, releasing the reservations that cannot be started.</summary>
        /// <param name="count">The number of reserved workers to start.</param>
        private void StartWorkers(int count)
        {
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var thread = new Thread(RunWorker)
                    {
                        IsBackground = true,
                        Name = WorkerThreadName
                    };
                    thread.Start();
                }
                catch (Exception exception)
                {
                    AbandonWorkers(count - i, exception);
                    return;
                }
            }
        }

        /// <summary>Releases reservations of workers that could not start, failing queued work when no worker remains to run it.</summary>
        /// <param name="count">The number of reservations to release.</param>
        /// <param name="exception">The failure that prevented the workers from starting.</param>
        private void AbandonWorkers(int count, Exception exception)
        {
            MajDebug.LogError(nameof(SongCollectionScanner), $"Failed to start a chart scan worker: {exception}");
            WorkItem[] orphaned;
            lock (_queueLock)
            {
                _workerCount -= count;
                if (_workerCount > 0)
                {
                    return;
                }
                orphaned = _pendingCollections.Concat(_pendingCharts).ToArray();
                _pendingCollections.Clear();
                _pendingCharts.Clear();
            }
            foreach (var item in orphaned)
            {
                item.Scan.Fail(exception);
                if (item.IsCollection || item.Scan.CompleteChart())
                {
                    Finish(item.Scan);
                }
            }
        }

        /// <summary>Runs queued work until both queues are empty, then exits.</summary>
        private void RunWorker()
        {
            while (TryTakeWork(out var item))
            {
                try
                {
                    if (item.IsCollection)
                    {
                        EnumerateCollection(item.Scan);
                    }
                    else
                    {
                        LoadChart(item.Scan, item.Index);
                    }
                }
                catch (Exception exception)
                {
                    // Work items report their own failures; this only keeps an unexpected error from ending the process.
                    MajDebug.LogException(nameof(SongCollectionScanner), exception);
                }
            }
        }

        /// <summary>Takes the next work item, preferring collection enumerations, or retires the calling worker when none is queued.</summary>
        /// <param name="item">The taken work item, or default when the worker retires.</param>
        /// <returns>True when a work item was taken; false when the calling worker must exit.</returns>
        private bool TryTakeWork(out WorkItem item)
        {
            lock (_queueLock)
            {
                if (_pendingCollections.Count > 0)
                {
                    item = _pendingCollections.Dequeue();
                    return true;
                }
                if (_pendingCharts.Count > 0)
                {
                    item = _pendingCharts.Dequeue();
                    return true;
                }
                // Retiring under the lock means a later enqueue always sees the reduced count and starts a replacement.
                _workerCount--;
                item = default;
                return false;
            }
        }

        /// <summary>Captures a collection's ordered child directories and queues one load per directory.</summary>
        /// <param name="scan">The collection scan to start.</param>
        private void EnumerateCollection(ScanState scan)
        {
            FileSystemEntry[] ordered;
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var directories = new List<FileSystemEntry>();
                foreach (var entry in scan.Directory.EnumerateEntries())
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.IsDirectory || entry.IsHidden)
                    {
                        continue;
                    }
                    directories.Add(entry);
                }
                ordered = directories.OrderBy(entry => entry.CreationTimeUtc)
                                     .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                                     .ThenBy(entry => entry.Location, StringComparer.Ordinal)
                                     .ToArray();
            }
            catch (Exception exception)
            {
                scan.Fail(exception);
                Finish(scan);
                return;
            }
            scan.Begin(ordered);
            Interlocked.Add(ref _directoryCount, ordered.Length);
            if (ordered.Length == 0)
            {
                Finish(scan);
                return;
            }
            int workersToStart;
            lock (_queueLock)
            {
                for (var i = 0; i < ordered.Length; i++)
                {
                    _pendingCharts.Enqueue(new WorkItem(scan, i));
                }
                workersToStart = ReserveWorkers();
            }
            StartWorkers(workersToStart);
            ReportProgress();
        }

        /// <summary>Validates and loads one chart directory from a single enumeration of its entries.</summary>
        /// <param name="scan">The collection scan owning the directory.</param>
        /// <param name="index">The directory's index in the scan's ordered directories and result slots.</param>
        private void LoadChart(ScanState scan, int index)
        {
            var entry = scan.Directories[index];
            var isCandidate = false;
            try
            {
                // A failed or canceled scan discards its results, so its remaining directories are only counted.
                if (scan.Failure is null)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var directory = new StorageDirectory(scan.Directory.FileSystem, entry.Location);
                    var entries = directory.EnumerateEntries().ToArray();
                    if (SongDetail.IsChartDirectory(entries))
                    {
                        isCandidate = true;
                        Interlocked.Increment(ref _discoveredChartCount);
                        scan.Songs[index] = SongDetail.Load(directory, entries, _cancellationToken);
                        Interlocked.Increment(ref _loadedChartCount);
                    }
                }
            }
            catch (OperationCanceledException exception)
            {
                scan.Fail(exception);
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(SongCollectionScanner), $"Failed to load chart directory '{entry.Location}': {exception}");
            }
            finally
            {
                if (isCandidate)
                {
                    Interlocked.Increment(ref _processedChartCount);
                }
                Interlocked.Increment(ref _scannedDirectoryCount);
            }
            ReportProgress();
            if (scan.CompleteChart())
            {
                Finish(scan);
            }
        }

        /// <summary>Completes a scan whose every directory has finished, releasing its charts unless it succeeded.</summary>
        /// <param name="scan">The finished scan.</param>
        private void Finish(ScanState scan)
        {
            var failure = scan.Failure;
            if (failure is null && _cancellationToken.IsCancellationRequested)
            {
                failure = new OperationCanceledException(_cancellationToken);
            }
            if (failure is null)
            {
                scan.Completion.TrySetResult(scan.CollectSongs());
                return;
            }
            DisposeSongs(scan.Songs);
            if (failure is OperationCanceledException)
            {
                scan.Completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                scan.Completion.TrySetException(failure);
            }
        }

        /// <summary>Notifies the progress observer about the scanned fraction, at most once per throttling interval.</summary>
        private void ReportProgress()
        {
            var progressChanged = _progressChanged;
            if (progressChanged is null)
            {
                return;
            }
            var now = Stopwatch.GetTimestamp();
            lock (_progressLock)
            {
                if (_hasReportedProgress && now - _lastProgressTimestamp < s_progressIntervalTicks)
                {
                    return;
                }
                var directoryCount = DirectoryCount;
                var progress = directoryCount == 0 ? 0d : (double)ScannedDirectoryCount / directoryCount;
                // A collection enumerated later raises the total; the reported value must not move backwards.
                progress = Math.Max(progress, _lastReportedProgress);
                _hasReportedProgress = true;
                _lastProgressTimestamp = now;
                _lastReportedProgress = progress;
                try
                {
                    progressChanged(progress);
                }
                catch (Exception exception)
                {
                    MajDebug.LogException(nameof(SongCollectionScanner), exception);
                }
            }
        }

        /// <summary>Releases partially loaded charts after every directory of their scan has finished.</summary>
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
                        MajDebug.LogError(nameof(SongCollectionScanner), $"Failed to release chart '{song.Hash}': {exception}");
                    }
                }
            }
        }

        /// <summary>Identifies one queued unit of work: a collection enumeration or one chart directory load.</summary>
        private readonly struct WorkItem
        {
            /// <summary>The index marking a collection enumeration rather than a chart load.</summary>
            public const int CollectionIndex = -1;

            /// <summary>Gets the scan the work belongs to.</summary>
            public ScanState Scan { get; }

            /// <summary>Gets the chart directory index, or <see cref="CollectionIndex"/> for a collection enumeration.</summary>
            public int Index { get; }

            /// <summary>Gets whether the work enumerates the collection rather than loading a chart.</summary>
            public bool IsCollection
            {
                get
                {
                    return Index == CollectionIndex;
                }
            }

            /// <summary>Creates a work item.</summary>
            /// <param name="scan">The scan the work belongs to.</param>
            /// <param name="index">The chart directory index, or <see cref="CollectionIndex"/> for a collection enumeration.</param>
            public WorkItem(ScanState scan, int index)
            {
                Scan = scan;
                Index = index;
            }
        }

        /// <summary>Stores one collection's ordered inputs, uniquely claimed result slots and completion.</summary>
        private sealed class ScanState
        {
            /// <summary>Gets the original storage backend handle of the collection.</summary>
            public StorageDirectory Directory { get; }

            /// <summary>Gets the completion observed by the caller; continuations never run on a scanner worker.</summary>
            public TaskCompletionSource<ISongDetail[]> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Gets child-directory metadata in deterministic storage order, empty until the collection is enumerated.</summary>
            public FileSystemEntry[] Directories { get; private set; } = Array.Empty<FileSystemEntry>();

            /// <summary>Gets the result array whose slots are written by exactly one chart load each.</summary>
            public ISongDetail?[] Songs { get; private set; } = Array.Empty<ISongDetail?>();

            /// <summary>Gets the first failure of the scan, or null while it is succeeding.</summary>
            public Exception? Failure
            {
                get
                {
                    return Volatile.Read(ref _failure);
                }
            }

            /// <summary>Stores the first failure of the scan.</summary>
            private Exception? _failure;

            /// <summary>Counts chart directories whose load has not finished.</summary>
            private int _remainingChartCount;

            /// <summary>Creates the state of a scan that has not enumerated its collection yet.</summary>
            /// <param name="directory">The original collection storage handle.</param>
            public ScanState(StorageDirectory directory)
            {
                Directory = directory;
            }

            /// <summary>Stores the enumerated chart directories; called once, before any of their loads is queued.</summary>
            /// <param name="directories">The ordered chart-directory metadata.</param>
            public void Begin(FileSystemEntry[] directories)
            {
                Directories = directories;
                Songs = new ISongDetail?[directories.Length];
                _remainingChartCount = directories.Length;
            }

            /// <summary>Records a failure unless an earlier one is already recorded.</summary>
            /// <param name="exception">The failure.</param>
            public void Fail(Exception exception)
            {
                Interlocked.CompareExchange(ref _failure, exception, null);
            }

            /// <summary>Marks one chart directory as finished.</summary>
            /// <returns>True for the last chart directory of the scan, whose caller completes the scan.</returns>
            public bool CompleteChart()
            {
                return Interlocked.Decrement(ref _remainingChartCount) == 0;
            }

            /// <summary>Copies the successfully loaded charts, preserving directory order.</summary>
            /// <returns>A compact array of the loaded charts.</returns>
            public ISongDetail[] CollectSongs()
            {
                var loadedCount = 0;
                foreach (var song in Songs)
                {
                    if (song is not null)
                    {
                        loadedCount++;
                    }
                }
                var result = new ISongDetail[loadedCount];
                var resultIndex = 0;
                foreach (var song in Songs)
                {
                    if (song is not null)
                    {
                        result[resultIndex++] = song;
                    }
                }
                return result;
            }
        }
    }
}
