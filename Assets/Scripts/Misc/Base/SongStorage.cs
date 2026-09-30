using Cysharp.Text;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.Net;
using MajdataPlay.Numerics;
using MajdataPlay.Utils;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine.Networking;
#nullable enable
namespace MajdataPlay
{
    public static class SongStorage
    {
        public static bool IsEmpty => Collections.IsEmpty() || Collections.All(x => x.Count == 0);
        /// <summary>
        /// Current song collection index
        /// </summary>
        public static int CollectionIndex
        {
            get => _collectionIndex;
            set => _collectionIndex = value.Clamp(0, Collections.Length - 1);
        }
        /// <summary>
        /// Current song collection
        /// </summary>
        public static SongCollection WorkingCollection
        {
            get
            {
                if (Collections.IsEmpty())
                {
                    return EMPTY_SONG_COLLECTION;
                }
                return Collections[_collectionIndex];
            }
        }
        /// <summary>
        /// Loaded song collections
        /// </summary>
        public static SongCollection[] Collections { get; private set; } = Array.Empty<SongCollection>();
        public static SongOrder OrderBy 
        {
            get => MajEnv.RuntimeConfig.List.OrderBy;
        }
        public static long TotalChartCount
        { 
            get
            {
                return _totalChartCount;
            }
        }

        static int _collectionIndex = 0;
        static long _totalChartCount = 0;
        static long _parsedChartCount = 0;


        readonly static List<ISongDetail> _allCharts = new(8192);
        readonly static HashSet<string> _storageFav = new();
        static DanInfo? _userFavorites = null;
        static MyFavoriteSongCollection? _myFavorite;

        static bool _isInited = false;
        static SQLiteAsyncConnection? _favDb;

        readonly static SongCollection EMPTY_SONG_COLLECTION = SongCollection.Empty("default");
        readonly static string MY_FAVORITE_FILENAME = "MyFavorites.json";
        static string MY_FAVORITE_EXPORT_PATH = string.Empty;
        static string MY_FAVORITE_STORAGE_PATH = string.Empty;
        //these method are called from the game
        internal static async Task InitAsync(IProgress<string>? progressReporter = null)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await RefreshMyFavAsync();
                    if (!Directory.Exists(MajEnv.ChartPath))
                    {
                        Directory.CreateDirectory(MajEnv.ChartPath);
                        Directory.CreateDirectory(Path.Combine(MajEnv.ChartPath, "default"));
                        return;
                    }
                    var rootPath = MajEnv.ChartPath;
                    var songs = await GetCollections(rootPath, progressReporter);

                    Collections = songs;
                    MajDebug.LogInfo($"Loaded chart count: {TotalChartCount}");
                    _isInited = true;
                });
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                throw;
            }
            finally
            {
                GameManager.OnSave += OnSave;
            }
        }
        internal static async Task RefreshAsync(IProgress<string>? progressReporter = null)
        {
            if(!_isInited)
            {
                return;
            }
            await UniTask.SwitchToThreadPool();
            using var chartListBackup = new PooledList<ISongDetail>(_allCharts);
            try
            {
                _allCharts.Clear();
                _parsedChartCount = 0;
                _totalChartCount = 0;
                var listConfig = MajEnv.RuntimeConfig.List;
                var selectedDiff = listConfig.SelectedDiff;
                var selectedIndex = listConfig.SelectedSongIndex;
                var selectedDir = listConfig.SelectedDir;

                await RefreshMyFavAsync();
                var collections = await GetCollections(MajEnv.ChartPath, progressReporter);
                await Task.Delay(100);
                progressReporter?.Report($"{"MAJTEXT_CLEANING_UP".i18n()}");
                await Task.Delay(100);

                var tasks = new Task[chartListBackup.Count];
                var tasksI = -1;
                Parallel.For(0, chartListBackup.Count, i =>
                {
                    var songDetail = chartListBackup[i];
                    switch (songDetail)
                    {
                        case OnlineSongDetail online:
                            tasks[Interlocked.Increment(ref tasksI)] = online.DisposeAsync().AsTask();
                            break;
                        case SongDetail local:
                            tasks[Interlocked.Increment(ref tasksI)] = local.DisposeAsync().AsTask();
                            break;
                    }
                });
                var waitAllTask = Task.WhenAll(tasks);
                await using(UniTask.ReturnToCurrentSynchronizationContext())
                {
                    while(!waitAllTask.IsCompleted)
                    {
                        await UniTask.Yield();
                    }
                }
                tasks = null;
                waitAllTask = null;
                Collections = collections;
                MajDebug.LogInfo($"Loaded chart count: {TotalChartCount}");
                Online.ClearResponseCache();
                GC.Collect();

                CollectionIndex = selectedDir;
                var selectedCollection = WorkingCollection;

                if (selectedCollection.IsEmpty)
                {
                    return;
                }
                else if (selectedIndex >= selectedCollection.Count)
                {
                    selectedCollection.Index = 0;
                }
                else
                {
                    selectedCollection.Index = selectedIndex;
                }
            }
            catch(Exception e)
            {
                _allCharts.Clear();
                _allCharts.AddRange(chartListBackup);
                MajDebug.LogException(e);
                throw;
            }
        }
        internal static async Task RefreshLocalAsync(IProgress<string>? progressReporter = null)
        {
            if (!_isInited)
            {
                return;
            }
            await UniTask.SwitchToThreadPool();
            using var chartListBackup = new PooledList<ISongDetail>(_allCharts);
            var onlineCollections = MajEnv.Settings.Online.Enable
                ? Collections.Where(x => x.IsOnline).ToArray()
                : Array.Empty<SongCollection>();
            try
            {
                _allCharts.Clear();
                _parsedChartCount = 0;
                _totalChartCount = 0;
                var listConfig = MajEnv.RuntimeConfig.List;
                var selectedDiff = listConfig.SelectedDiff;
                var selectedIndex = listConfig.SelectedSongIndex;
                var selectedDir = listConfig.SelectedDir;

                var collections = await GetLocalCollections(MajEnv.ChartPath, progressReporter);
                if (onlineCollections.Length != 0)
                {
                    collections.AddRange(onlineCollections);
                    var onlineCount = onlineCollections.Sum(x => (long)x.Count);
                    Interlocked.Add(ref _totalChartCount, onlineCount);
                }
                await Task.Delay(100);
                progressReporter?.Report($"{"MAJTEXT_CLEANING_UP".i18n()}");
                await Task.Delay(100);

                var localDetails = chartListBackup.Where(x => !x.IsOnline).ToArray();
                var tasks = new Task[localDetails.Length];
                Parallel.For(0, localDetails.Length, i =>
                {
                    var songDetail = localDetails[i];
                    switch (songDetail)
                    {
                        case SongDetail local:
                            tasks[i] = local.DisposeAsync().AsTask();
                            break;
                        default:
                            tasks[i] = Task.CompletedTask;
                            break;
                    }
                });
                var waitAllTask = Task.WhenAll(tasks);
                await using (UniTask.ReturnToCurrentSynchronizationContext())
                {
                    while (!waitAllTask.IsCompleted)
                    {
                        await UniTask.Yield();
                    }
                }
                tasks = null;
                waitAllTask = null;
                Collections = await AddAllFolderAndDans(MajEnv.ChartPath, collections);
                MajDebug.LogInfo($"Loaded chart count: {TotalChartCount}");
                Online.ClearResponseCache();
                GC.Collect();

                CollectionIndex = selectedDir;
                var selectedCollection = WorkingCollection;

                if (selectedCollection.IsEmpty)
                {
                    return;
                }
                else if (selectedIndex >= selectedCollection.Count)
                {
                    selectedCollection.Index = 0;
                }
                else
                {
                    selectedCollection.Index = selectedIndex;
                }
            }
            catch (Exception e)
            {
                _allCharts.Clear();
                _allCharts.AddRange(chartListBackup);
                MajDebug.LogException(e);
                throw;
            }
        }
        internal static async Task<bool> RefreshUserOnlineFavAsync(IProgress<string>? progressReporter = null)
        {
            if (!MajEnv.Settings.Online.Enable)
            {
                return true;
            }
            var isSuccessful = true;
            using var collections = new PooledList<SongCollection>(Collections);
            for (var i = 0; i < collections.Count; i++)
            {
                var collection = collections[i];
                if(collection.IsOnline && collection.Type is ChartStorageType.PlayList)
                {
                    collections.RemoveAt(i--);
                }
            }
            foreach (var api in MajEnv.ApiEndpoints.OrderBy(x => x.Name))
            {
                if (api is null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(api.Name))
                {
                    continue;
                }
                if (!api.RuntimeConfig.IsLoggedIn)
                {
                    continue;
                }
                try
                {
                    MajDebug.LogInfo($"[Online]Fetching fav list from {api.Url.OriginalString}");
                    progressReporter?.Report(ZString.Format("MAJTEXT_SCANNING_FAVORITES_FROM_{0}".i18n(), api.Name));
                    var daninfos = await Online.GetUserOnlineFavCollection(api);
                    if (daninfos is null)
                    {
                        isSuccessful = false;
                    }
                    else if (daninfos.Length > 0)
                    {
                        foreach (var dan in daninfos)
                        {
                            //TODO: make this parallel
                            var collection = await GetOnlineDanCollectionAsync(_allCharts, dan, api);
                            if (collection is not null)
                            {
                                collections.Add(collection);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    isSuccessful = false;
                    MajDebug.LogError($"[Online]Fetching fav list from {api.Url.OriginalString} failed because of {e.Message}");
                    MajDebug.LogException(e);
                }
            }
            //Potential bug: user may not return to the dir because this change after refresh.
            Collections = collections.ToArray();
            return isSuccessful;
        }
        //get local song list and online song list
        static async Task<SongCollection[]> GetCollections(string rootPath, IProgress<string>? progressReporter)
        {
            var collections = await GetLocalCollections(rootPath, progressReporter);
            //Online Charts
            if (MajEnv.Settings.Online.Enable)
            {
                foreach (var api in MajEnv.ApiEndpoints.OrderBy(x => x.Name))
                {
                    if (api is null)
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(api.Name))
                    {
                        continue;
                    }
                    MajDebug.LogInfo($"[MaiChart Scanner]Fetching chart list from {api.Url.OriginalString}");
                    progressReporter?.Report(ZString.Format("MAJTEXT_SCANNING_CHARTS_FROM_{0}".i18n(), api.Name));
                    var result = await GetOnlineCollection(api, progressReporter);
                    if (!result.IsEmpty)
                    {
                        collections.Add(result);
                    }
                }
            }
            else
            {
                MajDebug.LogInfo("[MaiChart Scanner]Online function was disabled, skipping.");
            }
            return await AddAllFolderAndDans(rootPath, collections);
        }
        //scan the local chart folders
        static async Task<List<SongCollection>> GetLocalCollections(string rootPath, IProgress<string>? progressReporter)
        {
            var dirs = new DirectoryInfo(rootPath).GetDirectories();
            var tasks = new List<Task<SongCollection>>(dirs.Length);
            var collections = new List<SongCollection>(dirs.Length);

            //Local Charts
            Parallel.For(0, dirs.Length, i =>
            {
                var dir = dirs[i];
                var path = dir.FullName;

                tasks.Add(GetCollection(path));
            });

            var allTasks = Task.WhenAll(tasks);

            while (!allTasks.IsCompleted)
            {
                var percent = 0f;
                if (_totalChartCount != 0)
                {
                    percent = _parsedChartCount / (float)_totalChartCount;
                }
                progressReporter?.Report($"{"MAJTEXT_SCANNING_CHARTS".i18n()}...({percent * 100:F2}%)");
                await Task.Delay(33);
            }
            progressReporter?.Report($"{"MAJTEXT_SCANNING_CHARTS".i18n()}...(100.00%)");

            foreach (var task in tasks)
            {
                if (task.IsFaulted)
                {
                    MajDebug.LogException(task.Exception);
                    continue;
                }
                if (task.Result != null)
                {
                    collections.Add(task.Result);
                }
            }
            collections = collections.OrderBy(x => x.Name).ToList();
            await Task.Delay(1000);
            return collections;
        }
        //sort out the ALL folder, Dans and playlists
        static async Task<SongCollection[]> AddAllFolderAndDans(string rootPath, List<SongCollection> collections)
        {
            //Add all songs to "All" folder
            foreach (var collection in collections)
            {
                foreach (var item in collection)
                {
                    _allCharts.Add(item);
                }
            }
            collections.Add(new SongCollection("All", _allCharts.ToArray()));

            MajDebug.LogInfo("MyFavorite");
            if (_userFavorites is not null)
            {
                foreach (var hash in _userFavorites.SongHashs)
                {
                    _storageFav.Add(hash);
                }
            }
            var hashSet = _storageFav;
            var favoriteSongs = _allCharts.Where(x => hashSet.Any(y => y == x.Hash))
                                          .GroupBy(x => x.Hash)
                                          .Select(x => x.FirstOrDefault())
                                          .Where(x => x is not null)
                                          .OrderBy(x => hashSet.ToList().IndexOf(x.Hash))
                                          .ToList();
            MajDebug.LogInfo(favoriteSongs.Count);
            _myFavorite = new(favoriteSongs, new HashSet<string>(_storageFav));
            //The collections and _myFavorite share a same ref of original List<T>
            collections.Add(_myFavorite);

            MajDebug.LogInfo("Load Dans");
            var danFiles = new DirectoryInfo(rootPath).GetFiles("*.json");
            var loadDanTasks = new Task<SongCollection?>[danFiles.Length];
            for (var i = 0; i < loadDanTasks.Length; i++)
            {
                if (i >= danFiles.Length)
                {
                    loadDanTasks[i] = Task.FromResult<SongCollection?>(null);
                    continue;
                }
                var file = danFiles[i];
                if (file.Name == MY_FAVORITE_FILENAME)
                {
                    loadDanTasks[i] = Task.FromResult<SongCollection?>(null);
                    continue;
                }
                var jsonStream = File.OpenRead(file.FullName);
                var id = GetGuidFromStream(jsonStream);
                jsonStream.Position = 0;
                var (result, dan, e) = await Serializer.Json.TryDeserializeAsync<DanInfo>(jsonStream);

                if (result && dan is not null)
                {
                    loadDanTasks[i] = GetLocalDanCollectionAsync(_allCharts, id, dan);
                }
                if (e is not null)
                {
                    MajDebug.LogError($"Failed to load dan from {file.FullName}\nException: {e}");
                }
            }
            if (loadDanTasks.Length != 0)
            {
                await Task.WhenAll(loadDanTasks);

                foreach (var task in loadDanTasks)
                {
                    if (task.IsFaulted)
                    {
                        MajDebug.LogError(task.Exception);
                        continue;
                    }
                    var collection = task.Result;
                    if (collection is null)
                    {
                        continue;
                    }
                    collections.Add(collection);
                    MajDebug.LogInfo("Loaded Dan:" + collection.DanInfo?.Name ?? collection.Name);
                }
            }
            return collections.ToArray();
        }
        //scan one folder
        private static async Task<SongCollection> GetCollection(string rootPath)
        {
            await UniTask.SwitchToThreadPool();
            var thisDir = new DirectoryInfo(rootPath);
            var dirs = thisDir.GetDirectories()
                              .OrderBy(o => o.CreationTime)
                              .ToList();
            var flagDirPath = Path.Combine(rootPath, ".MajdataPlay");
            MajDebug.LogDebug($"[MaiChart Scanner]Enter folder: {rootPath}");
            if (!Directory.Exists(flagDirPath))
            {
                var info = Directory.CreateDirectory(flagDirPath);
                info.Attributes |= FileAttributes.Hidden;
            }
            if (dirs.Count == 0)
            {
                MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}]Empty folder, skipping");
                return SongCollection.Empty(rootPath, thisDir.Name);
            }
            using var charts = new PooledList<SongDetail>();
            using var tasks = new PooledList<Task<SongDetail?>>();
            
            foreach (var songDir in dirs)
            {
                if((songDir.Attributes & FileAttributes.Hidden) != 0)
                {
                    continue;
                }
                MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}]Enter folder: {songDir.Name}");
                var files = songDir.GetFiles();
                var maidataFile = files.FirstOrDefault(o => o.Name.ToLower() is "maidata.txt");
                var trackFile = files.FirstOrDefault(o => o.Name.ToLower() is "track.opus" or "track.mp3" or "track.ogg" or "track.aac" or "track.wav");

                if (maidataFile is null || trackFile is null)
                {
                    MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}/{songDir.Name}]No maidata or track files found, ignored.");
                    continue;
                }

                var parsingTask = Task.Run(async () =>
                {
                    try
                    {
                        MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}/{songDir.Name}]Parsing");
                        var chart = await SongDetail.ParseAsync(songDir.FullName);
                        Interlocked.Increment(ref _parsedChartCount);
                        MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}/{songDir.Name}]Successfully parsed");
                        return chart;
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogError($"[MaiChart Scanner][{thisDir.Name}/{songDir.Name}]Failed to parse: {e}");
                        return null;
                    }
                    finally
                    {
                        MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}/{songDir.Name}]Exit");
                    }
                });
                Interlocked.Increment(ref _totalChartCount);
                tasks.Add(parsingTask);
            }
            await Task.WhenAll(tasks);
            var loadedChartCount = 0;
            foreach (var task in tasks)
            {
                var result = task.Result;
                if (result is null)
                {
                    Interlocked.Decrement(ref _totalChartCount);
                    continue;
                }
                loadedChartCount++;
                charts.Add(result);
            }
            MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}]Chart count loaded: {loadedChartCount}");
            MajDebug.LogDebug($"[MaiChart Scanner][{thisDir.Name}]Exit");
            return new SongCollection(rootPath, thisDir.Name, charts.ToArray());
        }
        //scan one online api
        private static async Task<SongCollection> GetOnlineCollection(ApiEndpoint api, IProgress<string>? progressReporter)
        {
            var name = api.Name;
            var cachePath = Path.Combine(MajEnv.CachePath, "Net", name);
            if(!Directory.Exists(cachePath))
            {
                Directory.CreateDirectory(cachePath);
            }
            var collection = SongCollection.Empty(cachePath, name);
            var apiroot = api.Url;

            if (apiroot is null)
            {
                return collection;
            }
            MajDebug.LogInfo("Loading online charts from:" + api.Url);

            try
            {
                var client = MajEnv.SharedHttpClient;
                var rspText = string.Empty;
                var chartList = await Online.GetChartListAsync(api);
                if (chartList is null)
                {
                    progressReporter?.Report(ZString.Format("Failed to fetch list from {0}".i18n(), api.Name));
                    throw new OperationCanceledException();
                }
                if(chartList.Length == 0)
                {
                    return collection;
                }

                var gameList = new ISongDetail[chartList.Length];
                Parallel.For(0, chartList.Length, i =>
                {
                    var song = chartList[i];
                    var songDetail = new OnlineSongDetail(api, song);
                    gameList[i] = songDetail;
                });

                MajDebug.LogInfo("Loaded online charts list:" + gameList.Length);
                Interlocked.Add(ref _totalChartCount, chartList.Length);
                var cacheFolder = Path.Combine(MajEnv.CachePath, $"Net/{name}");
                if (!Directory.Exists(cacheFolder))
                {
                    Directory.CreateDirectory(cacheFolder);
                }
                return new OnlineSongCollection(api, cachePath, name, gameList.ToArray());
            }
            catch (OperationCanceledException)
            {
                if (!Directory.Exists(cachePath))
                {
                    return collection;
                }
                var c = await GetCollection(cachePath);
                MajDebug.LogInfo("Loaded cached online charts list:" + c.Count);
                return c;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                return collection;
            }
        }
        //import one dan file
        #region Dan or Playlist importor
        private static Task<SongCollection?> GetLocalDanCollectionAsync(IEnumerable<ISongDetail> allCharts, Guid id, DanInfo danInfo)
        {
            return GetDanCollectionAsync(allCharts, id, danInfo, null);
        }
        private static Task<SongCollection?> GetOnlineDanCollectionAsync(IEnumerable<ISongDetail> allCharts, MajNetOnlineDanInfo onlineDanInfo, ApiEndpoint source)
        {
            var danInfo = new DanInfo()
            {
                Name = onlineDanInfo.Name,
                Description = onlineDanInfo.Description,
                SongHashs = onlineDanInfo.SongHashs.ToArray(),
                IsPlayList = onlineDanInfo.IsPlayList,
                IsForceGameover = onlineDanInfo.IsForceGameover
            };
            var id = ObjectIdHelper.ToGuid(onlineDanInfo.Id);
            return GetDanCollectionAsync(allCharts, id, danInfo, source);
        }
        private static async Task<SongCollection?> GetDanCollectionAsync(IEnumerable<ISongDetail> allCharts, Guid id, DanInfo danInfo, ApiEndpoint? source)
        {
            return await Task.Run(() =>
            {
                var songHashs = danInfo.SongHashs.ToList();
                var targetCharts = allCharts.Where(x => songHashs.Any(y => y == x.Hash))
                                            .OrderByDescending(x => x.IsOnline)
                                            .GroupBy(x => x.Hash)
                                            .Select(x => x.FirstOrDefault())
                                            .Where(x => x is not null)
                                            .OrderBy(x => songHashs.IndexOf(x.Hash))
                                            .ToArray();
                if (targetCharts.Length == 0)
                {
                    MajDebug.LogError("Failed to load dan, songs are empty or unable to find:" + danInfo.Name);
                    return default;
                }
                if (source is not null)
                {
                    return new OnlineSongCollection(source, danInfo.Name, targetCharts)
                    {
                        Id = id,
                        Type = danInfo.IsPlayList ? ChartStorageType.PlayList : ChartStorageType.Dan,
                        DanInfo = danInfo.IsPlayList ? null : danInfo
                    };
                }
                else
                {
                    return new SongCollection(danInfo.Name, targetCharts)
                    {
                        Id = id,
                        Type = danInfo.IsPlayList ? ChartStorageType.PlayList : ChartStorageType.Dan,
                        DanInfo = danInfo.IsPlayList ? null : danInfo
                    };
                }
            });
        }
        #endregion
        static void OnSave(object? sender, EventArgs? args)
        {
            try
            {
                if (!_isInited)
                {
                    return;
                }
                var hashSet = _myFavorite.ExportHashSet();
                File.WriteAllText(MY_FAVORITE_EXPORT_PATH,
                                  Serializer.Json.Serialize(new DanInfo()
                                  {
                                      Name = "My Favorites",
                                      SongHashs = hashSet.ToArray(),
                                      IsPlayList = true
                                  }
                    ));
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }
        static Guid GetGuidFromStream(Stream stream)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(stream);
            var idBytes = (stackalloc byte[16]);
            hash.AsSpan(0, 16).CopyTo(idBytes);
            var id = new Guid(idBytes);

            return id;
        }
        public static void AddToMyFavorites(ISongDetail songDetail)
        {
            _myFavorite.Add(songDetail);
            var hash = songDetail.Hash;
            if (!string.IsNullOrEmpty(hash))
                _favDb?.RunInTransactionAsync(conn => conn.InsertOrReplace(new FavoriteHashDB { Hash = hash }));
        }
        public static bool IsInMyFavorites(ISongDetail songDetail)
        {
            return _myFavorite.Any(o => o.Hash == songDetail.Hash);
        }
        public static void RemoveFromMyFavorites(ISongDetail songDetail)
        {
            _myFavorite.Remove(songDetail);
            var hash = songDetail.Hash;
            if (!string.IsNullOrEmpty(hash))
                _favDb?.RunInTransactionAsync(conn => conn.Delete<FavoriteHashDB>(hash));
        }
        public static void RemoveFromMyFavorites(string hashBase64Str)
        {
            _myFavorite.Remove(hashBase64Str);
            if (!string.IsNullOrEmpty(hashBase64Str))
                _favDb?.RunInTransactionAsync(conn => conn.Delete<FavoriteHashDB>(hashBase64Str));
        }
        static async Task RefreshMyFavAsync()
        {
            if (string.IsNullOrEmpty(MY_FAVORITE_EXPORT_PATH))
            {
                MY_FAVORITE_EXPORT_PATH = Path.Combine(MajEnv.ChartPath, MY_FAVORITE_FILENAME);
            }
            if (string.IsNullOrEmpty(MY_FAVORITE_STORAGE_PATH))
            {
                MY_FAVORITE_STORAGE_PATH = Path.Combine(MajEnv.CachePath, "Runtime", MY_FAVORITE_FILENAME);
            }

            // Init SQLite DB (once)
            if (_favDb is null)
            {
                var dbPath = MajEnv.FavoriteDBPath;
                var isDbExists = File.Exists(dbPath);
                _favDb = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                GameManager.OnAppQuit += OnAppQuit;
                await _favDb.CreateTableAsync<FavoriteHashDB>();
                if (!isDbExists && File.Exists(MY_FAVORITE_STORAGE_PATH))
                {
                    await MigrateFavFromJsonAsync();
                }
            }

            // Load export JSON (user-facing, stays as JSON for portability)
            if (File.Exists(MY_FAVORITE_EXPORT_PATH))
            {
                bool result;
                Exception? exception;
                (result, _userFavorites, exception) = await Serializer.Json.TryDeserializeAsync<DanInfo>(File.OpenRead(MY_FAVORITE_EXPORT_PATH));
                if (!result)
                {
                    var bakPath = $"{MY_FAVORITE_EXPORT_PATH}.bak";
                    while (File.Exists(bakPath))
                    {
                        bakPath = $"{bakPath}.bak";
                    }
                    File.Copy(MY_FAVORITE_EXPORT_PATH, bakPath);
                    MajDebug.LogError($"Failed to load favorites\nPath: {MY_FAVORITE_EXPORT_PATH}\nException: {exception}");
                }
            }

            // Load from SQLite
            _storageFav.Clear();
            var rows = await _favDb.Table<FavoriteHashDB>().ToListAsync();
            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(row.Hash))
                {
                    _storageFav.Add(row.Hash);
                }
            }
        }
        static async Task MigrateFavFromJsonAsync()
        {
            try
            {
                var (result, storageFav, exception) = await Serializer.Json.TryDeserializeAsync<HashSet<string>>(File.OpenRead(MY_FAVORITE_STORAGE_PATH));
                if (result && storageFav is not null)
                {
                    await _favDb!.RunInTransactionAsync(conn =>
                    {
                        foreach (var hash in storageFav)
                        {
                            if (!string.IsNullOrEmpty(hash))
                                conn.Insert(new FavoriteHashDB { Hash = hash });
                        }
                    });
                }
                var bakPath = MY_FAVORITE_STORAGE_PATH + ".bak";
                try
                {
                    if (File.Exists(bakPath))
                        File.Delete(bakPath);
                    File.Move(MY_FAVORITE_STORAGE_PATH, bakPath);
                    MajDebug.LogInfo("Migrated favorites from legacy JSON to SQLite, old file backed up.");
                }
                catch (Exception ex)
                {
                    MajDebug.LogError($"Failed to backup legacy favorite file: {ex.Message}");
                }
            }
            catch (Exception e)
            {
                MajDebug.LogError($"Failed to migrate favorites from JSON: {e.Message}");
            }
        }

        static void OnAppQuit(object? sender, EventArgs? args)
        {
            GameManager.OnAppQuit -= OnAppQuit;
            _favDb?.CloseAsync().Wait();
        }

        [Table("FavoriteHashes")]
        class FavoriteHashDB
        {
            [PrimaryKey]
            public string Hash { get; set; } = string.Empty;
        }
    }
}
