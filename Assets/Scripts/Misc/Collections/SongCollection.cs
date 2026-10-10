using MajdataPlay.Extensions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Numerics;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Collections
{
    public class SongCollection : IEnumerable<ISongDetail>, IReadOnlyCollection<ISongDetail>
    {
        public Guid Id { get; init; } = Guid.Empty;
        public string Path { get; init; }
        public ISongDetail Current
        {
            get
            {
                return Sorted[Index];
            }
        }
        public int Index
        {
            get => _index;
            set
            {
                if (IsEmpty)
                {
                    throw new ArgumentOutOfRangeException("this collection is empty");
                }
                _index = value.Clamp(0, Sorted.Length - 1);
            }
        }
        public ISongDetail this[int index]
        {
            get
            {
                return Sorted[index];
            }
        }
        public ChartStorageType Type { get; init; } = ChartStorageType.List;
        public bool IsOnline { get; init; } = false;
        public string Name { get; private set; }
        public bool IsSorted { get; private set; } = false;
        public int Count
        {
            get
            {
                return Sorted.Length;
            }
        }
        public bool IsEmpty
        {
            get
            {
                return Sorted.Length == 0;
            }
        }
        public bool IsVirtual { get; init; }
        public DanInfo? DanInfo { get; init; }

        protected ISongDetail[] Sorted;
        protected ISongDetail[] Origin;

        readonly static Guid COLLECTION_ALL_GUID = new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
        readonly static Guid COLLECTION_MY_FAVORITE_GUID = new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2);

        public SongCollection(string name, ISongDetail[] pArray): this(null, name, pArray)
        {

        }
        public SongCollection(string? dirPath, string name, ISongDetail[] pArray)
        {
            if (pArray.Length == 0)
            {
                Sorted = Array.Empty<ISongDetail>();
                Origin = Array.Empty<ISongDetail>();
            }
            else
            {
                var array = new ISongDetail[pArray.Length];
                Array.Copy(pArray, array, pArray.Length);
                Sorted = array;
                Origin = array;
            }
            if(string.IsNullOrEmpty(dirPath))
            {
                IsVirtual = true;
                Path = string.Empty;
                
                switch(name)
                {
                    case "All":
                        Id = COLLECTION_ALL_GUID;
                        break;
                    case "MyFavorites":
                        Id = COLLECTION_MY_FAVORITE_GUID;
                        break;
                    default:
                        //throw new ArgumentOutOfRangeException("Unexpected virtual collection name", nameof(name));
                        Id = Guid.Empty;
                        break;
                }
            }
            else
            {
                var collectionDirectory = FileSystem.OpenDirectory(dirPath);
                if (!collectionDirectory.Exists)
                {
                    throw new ArgumentException($"Directory '{dirPath}' is not exist.");
                }
                IsVirtual = false;
                Path = dirPath!;
                var flagDirectory = collectionDirectory.FindDirectory(".MajdataPlay");
                if (flagDirectory is null)
                {
                    flagDirectory = collectionDirectory.CreateDirectory(".MajdataPlay");
                    FileSystem.SetLocalAttributes(flagDirectory.Location, FileAttributes.Directory | FileAttributes.Hidden);
                }
                var flagFile = flagDirectory.FindFile("id");
                if (flagFile is not null)
                {
                    var guidStr = flagFile.ReadAllText();
                    if(Guid.TryParse(guidStr, out var guid))
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
                    Id = guid;
                    flagDirectory.CreateFile("id", "text/plain").WriteAllText(guid.ToString());
                }
            }
            Name = name;
        }
        public SongCollection()
        {
            Origin = Array.Empty<ISongDetail>();
            Sorted = Origin;
            Path = string.Empty;
            Name = string.Empty;
            Id = Guid.Empty;
            IsVirtual = true;
        }
        public bool MoveNext()
        {
            if (Index >= Count - 1)
                return false;
            Index++;
            return true;
        }
        public void Move(int diff)
        {
            Index = (Index + diff).Clamp(0, Count - 1);
        }
        public void SortAndFilter(SongOrder orderBy)
        {
            NormalizeIndex();
            if(Type == ChartStorageType.Dan || IsEmpty)
            {
                return;
            }
            IsSorted = true;
            var filtered = Filter(Origin, orderBy.Keyword);
            var sorted = Sort(filtered, orderBy.SortBy);

            SetCursor(Current.Hash, sorted);
            this.Sorted = sorted;
        }
        public async Task SortAndFilterAsync(SongOrder orderBy)
        {
            await Task.Run(() => SortAndFilter(orderBy));
        }
        public void Reset()
        {
            NormalizeIndex();
            if (!IsSorted)
            {
                return;
            }
            IsSorted = false;
            if (Sorted.Length != 0)
            {
                var newIndex = Origin.FindIndex(x => x == Current);
                newIndex = newIndex is -1 ? 0 : newIndex;
                _index = newIndex;
            }
            else
            {
                _index = 0;
            }
            Sorted = Origin;
        }
        protected void NormalizeIndex()
        {
            if (IsEmpty)
            {
                _index = 0;
                return;
            }
            _index = _index.Clamp(0, Sorted.Length - 1);
        }
        public void SetCursor(ISongDetail target)
        {
            if (IsEmpty)
            {
                return;
            }
            SetCursor(target.Hash, Sorted);
        }
        public void SetCursor(string hash)
        {
            if (IsEmpty)
            {
                return;
            }
            SetCursor(hash, Sorted);
        }
        void SetCursor(string hash, ISongDetail[] dataSet)
        {
            var newIndex = dataSet.FindIndex(x => x.Hash == hash);
            newIndex = newIndex is -1 ? 0 : newIndex;
            _index = newIndex;
        }
        public ISongDetail[] ToArray()
        {
            if(Sorted.Length == 0)
            {
                return Array.Empty<ISongDetail>();
            }
            var array = new ISongDetail[Sorted.Length];
            Array.Copy(Sorted, array, Sorted.Length);

            return array;
        }
        static ISongDetail[] Sort(ISongDetail[] origin, SortType sortType)
        {
            if (origin.IsEmpty())
                return origin;
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
        static ISongDetail[] Filter(ISongDetail[] origin, string keyword)
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
                var isDesMatch = song.Designers.Any(p => p == null ? false : p.ToLower().Contains(keyword));
                var isLevelMatch = song.Levels.Any(p => p == null ? false : p.ToLower() == keyword);
                var isTagDesMatch = song.Description.ToLower().Contains(keyword);

                var isMatch = isTitleMatch || isArtistMatch || isDesMatch || isLevelMatch || isTagDesMatch;
                if (isMatch)
                {
                    result[i++] = song;
                }
            }
            return result.Slice(0, i).ToArray();
        }
        public static SongCollection Empty(string name) => new SongCollection(name, Array.Empty<ISongDetail>());
        public static SongCollection Empty(string path, string name) => new SongCollection(path, name, Array.Empty<ISongDetail>());
        public IEnumerator<ISongDetail> GetEnumerator() => new Enumerator(Sorted);

        // Implementation for the GetEnumerator method.
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        struct Enumerator : IEnumerator<ISongDetail>
        {
            ISongDetail[] songs;
            public ISongDetail Current { get; private set; }
            object IEnumerator.Current { get => Current; }
            int index;
            public Enumerator(in ISongDetail[] songs)
            {
                this.songs = songs;
                Current = default;
                index = 0;
            }
            public bool MoveNext()
            {
                if (index >= songs.Length)
                    return false;
                Current = songs[index++];
                return true;
            }
            public void Reset() => index = 0;
            public void Dispose() { }
        }
        int _index = 0;
    }
}
