using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Numerics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Collections
{
    /// <summary>Maintains a song snapshot, its visible ordering and its selection cursor.</summary>
    public class SongCollection : IEnumerable<ISongDetail>, IReadOnlyCollection<ISongDetail>
    {
        /// <summary>Gets the persistent collection identifier, which is not changed by refreshes.</summary>
        public Guid Id { get; init; } = Guid.Empty;

        /// <summary>Gets the original storage handle, or null for a virtual collection.</summary>
        public StorageDirectory? Directory { get; }

        /// <summary>Gets the backing directory's authoritative location, or an empty string for a virtual collection.</summary>
        public string Path
        {
            get
            {
                return Directory?.Location ?? string.Empty;
            }
        }

        /// <summary>Gets the selected song from the current visible snapshot.</summary>
        /// <exception cref="IndexOutOfRangeException">The collection is empty.</exception>
        public ISongDetail Current
        {
            get
            {
                lock (_stateLock)
                {
                    NormalizeIndexCore();
                    return Sorted[_index];
                }
            }
        }

        /// <summary>Gets or sets the visible selection index, clamping assignments to the current snapshot.</summary>
        /// <exception cref="ArgumentOutOfRangeException">An index is assigned while the collection is empty.</exception>
        public int Index
        {
            get
            {
                lock (_stateLock)
                {
                    return _index;
                }
            }
            set
            {
                lock (_stateLock)
                {
                    if (Sorted.Length == 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), "This collection is empty.");
                    }
                    _index = value.Clamp(0, Sorted.Length - 1);
                    _cursorHash = Sorted[_index].Hash;
                }
            }
        }

        /// <summary>Gets a song at the specified index in the current visible snapshot.</summary>
        /// <param name="index">The zero-based visible index.</param>
        /// <returns>The song stored at the specified index.</returns>
        /// <exception cref="IndexOutOfRangeException">The index is outside the visible snapshot.</exception>
        public ISongDetail this[int index]
        {
            get
            {
                lock (_stateLock)
                {
                    return Sorted[index];
                }
            }
        }

        /// <summary>Gets the collection's list, playlist or course category.</summary>
        public ChartStorageType Type { get; init; } = ChartStorageType.List;

        /// <summary>Gets whether the collection is supplied by an online service.</summary>
        public bool IsOnline { get; init; } = false;

        /// <summary>Gets the collection's display name.</summary>
        public string Name { get; private set; }

        /// <summary>Gets whether a saved sort and filter is applied to the visible snapshot.</summary>
        public bool IsSorted
        {
            get
            {
                lock (_stateLock)
                {
                    return _isSorted;
                }
            }
        }

        /// <summary>Gets the number of songs in the current visible snapshot.</summary>
        public int Count
        {
            get
            {
                lock (_stateLock)
                {
                    return Sorted.Length;
                }
            }
        }

        /// <summary>Gets whether the current visible snapshot contains no songs.</summary>
        public bool IsEmpty
        {
            get
            {
                return Count == 0;
            }
        }

        /// <summary>Gets whether the collection has no independently scanned backing directory.</summary>
        public bool IsVirtual { get; init; }

        /// <summary>Gets optional course or playlist metadata.</summary>
        public DanInfo? DanInfo { get; init; }

        /// <summary>Stores the current visible song snapshot for derived virtual collections.</summary>
        protected ISongDetail[] Sorted;

        /// <summary>Stores the unfiltered song snapshot for derived virtual collections.</summary>
        protected ISongDetail[] Origin;

        /// <summary>Identifies the built-in collection containing all songs.</summary>
        private static readonly Guid s_collectionAllGuid = new Guid("00000000-0000-0000-0000-000000000001");

        /// <summary>Identifies the built-in favorites collection.</summary>
        private static readonly Guid s_collectionMyFavoriteGuid = new Guid("00000000-0000-0000-0000-000000000002");

        /// <summary>Protects snapshot publication, ordering and cursor changes.</summary>
        private readonly object _stateLock = new();

        /// <summary>Serializes refresh operations on this collection.</summary>
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        /// <summary>Stores the current visible selection index.</summary>
        private int _index = 0;

        /// <summary>Remembers the selection when a filter or refresh temporarily produces an empty snapshot.</summary>
        private string? _cursorHash;

        /// <summary>Stores whether the saved ordering is active.</summary>
        private bool _isSorted = false;

        /// <summary>Stores the sort mode independently of a caller's mutable options object.</summary>
        private SortType _sortBy = SortType.Default;

        /// <summary>Stores the filter keyword independently of a caller's mutable options object.</summary>
        private string _filterKeyword = string.Empty;

        /// <summary>Creates a virtual collection whose contents are not refreshed from storage.</summary>
        /// <param name="name">The collection's display name.</param>
        /// <param name="pArray">The songs copied into the initial snapshot.</param>
        /// <exception cref="ArgumentNullException">The name or song array is null.</exception>
        public SongCollection(string name, ISongDetail[] pArray) : this(null, name, pArray)
        {
        }

        /// <summary>Creates a collection using the supplied storage backend without reopening its location.</summary>
        /// <param name="directory">The backing directory, or null to create a virtual collection.</param>
        /// <param name="name">The collection's display name.</param>
        /// <param name="pArray">The songs copied into the initial snapshot.</param>
        /// <exception cref="ArgumentNullException">The name or song array is null.</exception>
        /// <exception cref="ArgumentException">The backing directory does not exist.</exception>
        /// <exception cref="IOException">The collection identifier cannot be read or persisted.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies access to the directory or identifier.</exception>
        /// <exception cref="NotSupportedException">The backend does not support the identifier operations.</exception>
        public SongCollection(StorageDirectory? directory, string name, ISongDetail[] pArray)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (pArray is null)
            {
                throw new ArgumentNullException(nameof(pArray));
            }
            Origin = pArray.Length == 0 ? Array.Empty<ISongDetail>() : (ISongDetail[])pArray.Clone();
            Sorted = Origin;
            Directory = directory;
            Name = name;
            IsVirtual = directory is null;

            if (directory is null)
            {
                switch (name)
                {
                    case "All":
                        Id = s_collectionAllGuid;
                        break;
                    case "MyFavorites":
                        Id = s_collectionMyFavoriteGuid;
                        break;
                    default:
                        Id = Guid.Empty;
                        break;
                }
                return;
            }
            if (!directory.Exists)
            {
                throw new ArgumentException($"Directory '{directory.Location}' does not exist.", nameof(directory));
            }
            var flagDirectory = directory.FindDirectory(".MajdataPlay");
            if (flagDirectory is null)
            {
                flagDirectory = directory.CreateDirectory(".MajdataPlay");
                flagDirectory.SetAttributes(FileAttributes.Directory | FileAttributes.Hidden);
            }
            var flagFile = flagDirectory.FindFile("id");
            if (flagFile is not null)
            {
                var guidStr = flagFile.ReadAllText();
                if (Guid.TryParse(guidStr, out var guid))
                {
                    Id = guid;
                }
                else
                {
                    guid = Guid.NewGuid();
                    flagFile.WriteAllText(guid.ToString());
                    Id = guid;
                }
            }
            else
            {
                var guid = Guid.NewGuid();
                flagDirectory.CreateFile("id", "text/plain").WriteAllText(guid.ToString());
                Id = guid;
            }
        }

        /// <summary>Creates an empty virtual collection.</summary>
        public SongCollection() : this(string.Empty, Array.Empty<ISongDetail>())
        {
        }

        /// <summary>Moves the selection to the next visible song, if one exists.</summary>
        /// <returns>True when the selection advanced; otherwise false.</returns>
        public bool MoveNext()
        {
            lock (_stateLock)
            {
                NormalizeIndexCore();
                if (_index >= Sorted.Length - 1)
                {
                    return false;
                }
                _index++;
                _cursorHash = Sorted[_index].Hash;
                return true;
            }
        }

        /// <summary>Moves the selection by an offset within the visible snapshot.</summary>
        /// <param name="diff">The signed number of positions to move.</param>
        public void Move(int diff)
        {
            lock (_stateLock)
            {
                if (Sorted.Length == 0)
                {
                    return;
                }
                _index = (_index + diff).Clamp(0, Sorted.Length - 1);
                _cursorHash = Sorted[_index].Hash;
            }
        }

        /// <summary>Saves and applies a sort and filter, even when the current visible snapshot is empty.</summary>
        /// <param name="orderBy">The options whose values are copied for subsequent refreshes.</param>
        /// <exception cref="ArgumentNullException">The options object is null.</exception>
        public void SortAndFilter(SongOrder orderBy)
        {
            if (orderBy is null)
            {
                throw new ArgumentNullException(nameof(orderBy));
            }
            var sortBy = orderBy.SortBy;
            var keyword = orderBy.Keyword ?? string.Empty;
            lock (_stateLock)
            {
                if (Type is ChartStorageType.Dan or ChartStorageType.Recommend)
                {
                    return;
                }
                var hash = GetCursorHash();
                var sorted = Sort(Filter(Origin, keyword), sortBy);
                SetCursorCore(hash, sorted);
                Sorted = sorted;
                _sortBy = sortBy;
                _filterKeyword = keyword;
                _isSorted = true;
            }
        }

        /// <summary>Applies sorting and filtering on a background worker.</summary>
        /// <param name="orderBy">The options copied before background work starts.</param>
        /// <returns>A task completing when the visible snapshot has been updated.</returns>
        /// <exception cref="ArgumentNullException">The options object is null.</exception>
        public Task SortAndFilterAsync(SongOrder orderBy)
        {
            if (orderBy is null)
            {
                throw new ArgumentNullException(nameof(orderBy));
            }
            var snapshot = new SongOrder
            {
                SortBy = orderBy.SortBy,
                Keyword = orderBy.Keyword
            };
            return Task.Run(() => SortAndFilter(snapshot));
        }

        /// <summary>Restores the unfiltered snapshot and clears the saved ordering.</summary>
        public void Reset()
        {
            lock (_stateLock)
            {
                NormalizeIndexCore();
                if (!_isSorted)
                {
                    return;
                }
                SetCursorCore(GetCursorHash(), Origin);
                Sorted = Origin;
                _isSorted = false;
                _sortBy = SortType.Default;
                _filterKeyword = string.Empty;
            }
        }

        /// <summary>Synchronously refreshes a local collection without blocking a captured Unity synchronization context.</summary>
        /// <param name="cancellationToken">The token used to cancel waiting, discovery or chart loading.</param>
        /// <remarks>
        /// Virtual and online collections are unchanged; online cache contents never replace remote song lists.
        /// Only this collection's snapshot is replaced; previous songs are not disposed because other views may still reference them.
        /// Cancellation is checked between synchronous provider calls and around the upstream tokenless metadata parser.
        /// </remarks>
        /// <exception cref="OperationCanceledException">A local refresh was canceled; the previous snapshot is retained.</exception>
        /// <exception cref="IOException">Discovery failed; the previous snapshot is retained.</exception>
        /// <exception cref="UnauthorizedAccessException">The backing directory could not be enumerated.</exception>
        /// <exception cref="NotSupportedException">The backend does not support directory enumeration.</exception>
        public void Refresh(CancellationToken cancellationToken = default)
        {
            if (Directory is null || IsVirtual || IsOnline)
            {
                return;
            }
            Task.Run(() => RefreshAsync(cancellationToken), cancellationToken).GetAwaiter().GetResult();
        }

        /// <summary>Refreshes a local collection and publishes its complete snapshot only after a successful scan.</summary>
        /// <param name="cancellationToken">The token used to cancel waiting, discovery or chart loading.</param>
        /// <returns>A task completing after publication of the new snapshot.</returns>
        /// <remarks>
        /// Virtual and online collections return a completed task without scanning or replacing their contents.
        /// Cancellation cannot interrupt a blocked synchronous provider call or the upstream tokenless metadata parser.
        /// Published songs may also be referenced by other collections or captured snapshots; refresh does not dispose them.
        /// </remarks>
        /// <exception cref="OperationCanceledException">A local refresh was canceled; the previous snapshot is retained.</exception>
        /// <exception cref="IOException">Discovery failed; the previous snapshot is retained.</exception>
        /// <exception cref="UnauthorizedAccessException">The backing directory could not be enumerated.</exception>
        /// <exception cref="NotSupportedException">The backend does not support directory enumeration.</exception>
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            var directory = Directory;
            if (directory is null || IsVirtual || IsOnline)
            {
                return Task.CompletedTask;
            }
            return RefreshCoreAsync(directory, cancellationToken);
        }

        /// <summary>Serializes scans and commits origin, visibility and selection under the snapshot lock.</summary>
        /// <param name="directory">The original backend handle to scan.</param>
        /// <param name="cancellationToken">The token controlling the entire refresh.</param>
        /// <returns>A task completing after the new snapshot is committed.</returns>
        /// <exception cref="OperationCanceledException">Waiting or scanning was canceled.</exception>
        /// <exception cref="IOException">The backing directory could not be enumerated.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denied enumeration.</exception>
        /// <exception cref="NotSupportedException">The backend does not support enumeration.</exception>
        private async Task RefreshCoreAsync(StorageDirectory directory, CancellationToken cancellationToken)
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            ISongDetail[]? replacement = null;
            try
            {
                using var scanner = new SongCollectionScanner(cancellationToken);
                replacement = await scanner.ScanAsync(directory).ConfigureAwait(false);
                lock (_stateLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var hash = GetCursorHash();
                    var sorted = _isSorted ? Sort(Filter(replacement, _filterKeyword), _sortBy) : replacement;
                    cancellationToken.ThrowIfCancellationRequested();
                    SetCursorCore(hash, sorted);
                    // Old songs may still be used by All, favorites, playlists or captured snapshots.
                    Origin = replacement;
                    Sorted = sorted;
                    replacement = null;
                }
            }
            finally
            {
                if (replacement is not null)
                {
                    DisposeSongs(replacement);
                }
                _refreshLock.Release();
            }
        }

        /// <summary>Clamps the cursor after a derived collection changes its visible snapshot.</summary>
        protected void NormalizeIndex()
        {
            lock (_stateLock)
            {
                NormalizeIndexCore();
            }
        }

        /// <summary>Clamps the cursor while the caller holds the snapshot lock.</summary>
        private void NormalizeIndexCore()
        {
            _index = Sorted.Length == 0 ? 0 : _index.Clamp(0, Sorted.Length - 1);
        }

        /// <summary>Selects the visible song matching another song's hash.</summary>
        /// <param name="target">The song whose hash should be selected.</param>
        /// <exception cref="ArgumentNullException">The target song is null.</exception>
        public void SetCursor(ISongDetail target)
        {
            if (target is null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            SetCursor(target.Hash);
        }

        /// <summary>Selects a visible song by hash, or remembers the hash if the visible snapshot is empty.</summary>
        /// <param name="hash">The hash to match against the visible snapshot.</param>
        public void SetCursor(string hash)
        {
            lock (_stateLock)
            {
                SetCursorCore(hash, Sorted);
            }
        }

        /// <summary>Gets the current selection hash while retaining a selection across an empty snapshot.</summary>
        /// <returns>The selected song hash, or null if no selection has ever been available.</returns>
        private string? GetCursorHash()
        {
            NormalizeIndexCore();
            return Sorted.Length == 0 ? _cursorHash : Sorted[_index].Hash;
        }

        /// <summary>Updates the cursor against a prepared snapshot while the caller holds the snapshot lock.</summary>
        /// <param name="hash">The preferred selection hash, or null to select the first song.</param>
        /// <param name="dataSet">The snapshot against which the cursor is computed.</param>
        private void SetCursorCore(string? hash, ISongDetail[] dataSet)
        {
            var newIndex = 0;
            if (hash is not null)
            {
                for (var i = 0; i < dataSet.Length; i++)
                {
                    if (dataSet[i].Hash == hash)
                    {
                        newIndex = i;
                        break;
                    }
                }
            }
            _index = newIndex;
            _cursorHash = dataSet.Length == 0 ? hash : dataSet[newIndex].Hash;
        }

        /// <summary>Copies the current visible snapshot.</summary>
        /// <returns>A new song array, or the shared empty array when no songs are visible.</returns>
        public ISongDetail[] ToArray()
        {
            lock (_stateLock)
            {
                return Sorted.Length == 0 ? Array.Empty<ISongDetail>() : (ISongDetail[])Sorted.Clone();
            }
        }

        /// <summary>Copies the complete unfiltered snapshot for storage ownership and aggregate collection management.</summary>
        /// <returns>A new array containing every original song, or the shared empty array when the origin is empty.</returns>
        /// <remarks>The returned array does not grant exclusive ownership of its song instances.</remarks>
        internal ISongDetail[] GetOriginSnapshot()
        {
            lock (_stateLock)
            {
                return Origin.Length == 0 ? Array.Empty<ISongDetail>() : (ISongDetail[])Origin.Clone();
            }
        }

        /// <summary>Applies the specified sort mode without mutating the input array.</summary>
        /// <param name="origin">The songs to order.</param>
        /// <param name="sortType">The requested ordering.</param>
        /// <returns>The ordered songs, or the input array when it is empty.</returns>
        private static ISongDetail[] Sort(ISongDetail[] origin, SortType sortType)
        {
            if (origin.Length == 0)
            {
                return origin;
            }
            IEnumerable<ISongDetail> result = sortType switch
            {
                SortType.ByTime => origin.OrderByDescending(o => o.Timestamp),
                SortType.ByDiff => origin.OrderByDescending(o => o.Levels[4]),
                SortType.ByDes => origin.OrderBy(o => o.Designers[4]),
                SortType.ByTitle => origin.OrderBy(o => o.Title),
                _ => origin
            };
            return result.ToArray();
        }

        /// <summary>Filters titles, artists, designers, levels and descriptions without changing the input array.</summary>
        /// <param name="origin">The unfiltered songs.</param>
        /// <param name="keyword">The case-insensitive search keyword, or an empty string to disable filtering.</param>
        /// <returns>The matching songs in their original order.</returns>
        private static ISongDetail[] Filter(ISongDetail[] origin, string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return origin;
            }
            keyword = keyword.ToLower();
            var result = new Span<ISongDetail>(new ISongDetail[origin.Length]);
            var i = 0;
            foreach (var song in origin)
            {
                var isTitleMatch = song.Title.ToLower().Contains(keyword);
                var isArtistMatch = song.Artist.ToLower().Contains(keyword);
                var isDesMatch = song.Designers.Any(p => p is not null && p.ToLower().Contains(keyword));
                var isLevelMatch = song.Levels.Any(p => p is not null && p.ToLower() == keyword);
                var isTagDesMatch = song.Description.ToLower().Contains(keyword);
                if (isTitleMatch || isArtistMatch || isDesMatch || isLevelMatch || isTagDesMatch)
                {
                    result[i++] = song;
                }
            }
            return result.Slice(0, i).ToArray();
        }

        /// <summary>Releases unpublished local songs without waiting for main-thread-only asynchronous cleanup.</summary>
        /// <param name="songs">The newly scanned songs that were never published to a collection snapshot.</param>
        private static void DisposeSongs(ISongDetail[] songs)
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
                        MajDebug.LogError($"[SongCollection]Failed to release chart '{song.Hash}': {exception}");
                    }
                }
            }
        }

        /// <summary>Creates an empty virtual collection.</summary>
        /// <param name="name">The collection's display name.</param>
        /// <returns>A virtual collection containing no songs.</returns>
        /// <exception cref="ArgumentNullException">The name is null.</exception>
        public static SongCollection Empty(string name)
        {
            return new SongCollection(name, Array.Empty<ISongDetail>());
        }

        /// <summary>Creates an empty collection retaining the supplied backend handle.</summary>
        /// <param name="directory">The backing storage directory.</param>
        /// <param name="name">The collection's display name.</param>
        /// <returns>An empty collection with its persistent storage identifier.</returns>
        /// <exception cref="ArgumentNullException">The directory or name is null.</exception>
        /// <exception cref="ArgumentException">The backing directory does not exist.</exception>
        /// <exception cref="IOException">The identifier cannot be read or persisted.</exception>
        /// <exception cref="UnauthorizedAccessException">The backend denies access to the identifier.</exception>
        /// <exception cref="NotSupportedException">The backend does not support identifier operations.</exception>
        public static SongCollection Empty(StorageDirectory directory, string name)
        {
            if (directory is null)
            {
                throw new ArgumentNullException(nameof(directory));
            }
            return new SongCollection(directory, name, Array.Empty<ISongDetail>());
        }

        /// <summary>Enumerates the visible snapshot captured at the time this method is called.</summary>
        /// <returns>An enumerator unaffected by subsequent snapshot replacements.</returns>
        public IEnumerator<ISongDetail> GetEnumerator()
        {
            lock (_stateLock)
            {
                return new Enumerator(Sorted);
            }
        }

        /// <summary>Enumerates the captured visible snapshot through the non-generic interface.</summary>
        /// <returns>An enumerator over the captured songs.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>Enumerates an immutable song-array snapshot.</summary>
        private struct Enumerator : IEnumerator<ISongDetail>
        {
            /// <summary>Stores the captured visible song array.</summary>
            private readonly ISongDetail[] _songs;

            /// <summary>Stores the next index to enumerate.</summary>
            private int _index;

            /// <summary>Stores the current song, or null before or after enumeration.</summary>
            private ISongDetail? _current;

            /// <summary>Gets the song at the current enumeration position.</summary>
            /// <exception cref="InvalidOperationException">Enumeration has not started or has finished.</exception>
            public ISongDetail Current
            {
                get
                {
                    return _current ?? throw new InvalidOperationException("The enumerator has no current song.");
                }
            }

            /// <summary>Gets the current song through the non-generic interface.</summary>
            /// <exception cref="InvalidOperationException">Enumeration has not started or has finished.</exception>
            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }

            /// <summary>Creates an enumerator over a captured song snapshot.</summary>
            /// <param name="songs">The immutable song array to enumerate.</param>
            public Enumerator(ISongDetail[] songs)
            {
                _songs = songs;
                _current = null;
                _index = 0;
            }

            /// <summary>Advances to the next song in the captured snapshot.</summary>
            /// <returns>True when a current song is available; otherwise false.</returns>
            public bool MoveNext()
            {
                if (_index >= _songs.Length)
                {
                    _current = null;
                    return false;
                }
                _current = _songs[_index++];
                return true;
            }

            /// <summary>Restores the enumerator to its initial position.</summary>
            public void Reset()
            {
                _index = 0;
                _current = null;
            }

            /// <summary>Completes enumeration without releasing shared song objects.</summary>
            public void Dispose()
            {
            }
        }
    }
}
