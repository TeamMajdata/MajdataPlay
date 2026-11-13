using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.Editor;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Settings.Runtime;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.List
{
    public class CollectionListManager : MonoBehaviour
    {
        /// <summary>Defines the collection scroll and navigation-arrow transition duration in seconds.</summary>
        private const float CollectionTransitionDurationSeconds = 0.25f;

        /// <summary>Limits binding to the category tabs within this index distance of the visible cursor.</summary>
        private const float VisibleCollectionDistance = 3f;

        public SongCollection SelectedCollection => _currentCollection;

        [SerializeField]
        [FormerlySerializedAs("centerCoverDisplayer")]
        CenterCoverDisplayer _centerCoverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("coverListManager")]
        CoverListManager _coverListManager;

        [SerializeField]
        [FormerlySerializedAs("collectionListRoot")]
        GameObject _collectionListRoot;

        /// <summary>Controls only the independent navigation arrows without fading any moving category.</summary>
        [SerializeField]
        [FormerlySerializedAs("NavigationArrows")]
        private CanvasGroup? _navigationArrows;

        [SerializeField, ReadOnlyField]
        int _selectedDifficulty = 0;

        [SerializeField, ReadOnlyField]
        int _listDesiredPos = 0;
        [SerializeField, ReadOnlyField]
        float _listCursorPos = 0;

        /// <summary>Reuses one harmless selection while storage has no published collections.</summary>
        private readonly SongCollection _emptyCollection = SongCollection.Empty("Empty");

        /// <summary>Stores the current sorted collection, or the reusable empty selection after initialization.</summary>
        private SongCollection _currentCollection = null!;
        SongCollection[] _collections = Array.Empty<SongCollection>();

        CollectionDisplayer[] _collectionDisplayers = Array.Empty<CollectionDisplayer>();
        int[] _collectionDisplayerIndices = Array.Empty<int>();

        SongCollection[] _easySortedCollections = Array.Empty<SongCollection>();
        SongCollection[] _basicSortedCollections = Array.Empty<SongCollection>();
        SongCollection[] _advanceSortedCollections = Array.Empty<SongCollection>();
        SongCollection[] _expertSortedCollections = Array.Empty<SongCollection>();
        SongCollection[] _masterSortedCollections = Array.Empty<SongCollection>();
        SongCollection[] _reMasterSortedCollections = Array.Empty<SongCollection>();
        SongCollection[] _utageSortedCollections = Array.Empty<SongCollection>();

        MotionHandle _scrollMotion;

        readonly ListConfig _listConfig = MajEnv.RuntimeConfig?.List ?? new();

        void Awake()
        {
            _currentCollection = _emptyCollection;
            Majdata<CollectionListManager>.Instance = this;
            var collectionListRoot = _collectionListRoot.transform;
            var displayerCount = collectionListRoot.childCount;
            _collectionDisplayers = new CollectionDisplayer[displayerCount];
            _collectionDisplayerIndices = new int[displayerCount];
            Array.Fill(_collectionDisplayerIndices, -1);
            for (int i = 0; i < displayerCount; i++)
            {
                var child = collectionListRoot.GetChild(i);
                var displayer = child.GetComponent<CollectionDisplayer>();
                if (displayer is null)
                {
                    throw new InvalidOperationException($"Child {child.name} does not have a CollectionDisplayer component.");
                }
                _collectionDisplayers[i] = displayer;
            }
        }
        void Start()
        {
            for (int i = 0; i < _collectionDisplayers.Length; i++)
            {
                var displayer = _collectionDisplayers[i];
                if (displayer is not null)
                {
                    displayer.gameObject.SetActive(false);
                }
            }
        }
        private void OnEnable()
        {
            if (_collections.Length > 0)
            {
                NormalizeListCursor();
            }
        }
        private void OnDisable()
        {
            _scrollMotion.TryCancel();
            _listCursorPos = _listDesiredPos;
            SetNavigationAlpha(1f);
        }
        void OnDestroy()
        {
            _scrollMotion.TryCancel();
        }
        internal void Init()
        {
            _scrollMotion.TryCancel();
            // A refreshed snapshot can reuse the same indices for different collections.
            Array.Fill(_collectionDisplayerIndices, -1);
            foreach (var displayer in _collectionDisplayers)
            {
                displayer.SetActive(false);
            }
            InitCollectionStorage();
            RestoreContextFromConfiguration();
        }
        public void SlideDifficulty(int delta)
        {
            if (delta == 0)
            {
                return;
            }
            var song = _coverListManager.SelectedSong;
            if (song is null)
            {
                return;
            }

            var levels = song.Levels;
            var direction = Math.Sign(delta);
            var pos = _selectedDifficulty;
            // Search at most one full circle, skipping difficulties without a chart.
            for (var i = 0; i < 7; i++)
            {
                pos = WrapIndex(pos + direction, 7);
                if (pos < levels.Length && !string.IsNullOrEmpty(levels[pos]))
                {
                    if (pos != _selectedDifficulty)
                    {
                        SlideToDifficulty(pos);
                    }
                    return;
                }
            }
        }
        void SlideListTo(int pos, bool disableAnimation, bool forceUpdate)
        {
            if (_collections.Length == 0)
            {
                _listDesiredPos = 0;
                _currentCollection = _emptyCollection;
                _scrollMotion.TryCancel();
                _coverListManager.SetCollection(_currentCollection, false);
                NormalizeListCursor();
                return;
            }
            var oldDesiredPos = _listDesiredPos;
            var crossesBoundary = pos < 0 || pos >= _collections.Length;
            _listDesiredPos = WrapIndex(pos, _collections.Length);
            UpdateSelectedSongCollection();
            UpdateListConfiguration();
            var shouldUpdate = _listDesiredPos != oldDesiredPos || forceUpdate;
            if (!shouldUpdate)
            {
                return;
            }

            _coverListManager.SetCollection(_currentCollection, false);
            if (disableAnimation || crossesBoundary)
            {
                _scrollMotion.TryCancel();
                NormalizeListCursor();
            }
            else
            {
                DisplayerMoveTo(_listDesiredPos, CollectionTransitionDurationSeconds);
            }
        }
        void SlideToDifficulty(int pos)
        {
            _selectedDifficulty = WrapIndex(pos, 7);
            var chartLevel = (ChartLevel)_selectedDifficulty;
            _listConfig.SelectedDiff = chartLevel;
            UpdateSelectedSongCollection();

            _centerCoverDisplayer.SetDifficulty(_selectedDifficulty);
            _coverListManager.SetDifficulty(_selectedDifficulty);
            _coverListManager.SetCollection(_currentCollection, true);
        }

        
        internal void NextCollection()
        {
            ChangeCollection(1);
        }
        internal void PreviousCollection()
        {
            ChangeCollection(-1);
        }
        void ChangeCollection(int delta)
        {
            var targetPosition = _listDesiredPos + delta;
            SlideListTo(targetPosition, false, false);
            if (delta > 0)
            {
                _coverListManager.SlideListToHead();
            }
            else
            {
                _coverListManager.SlideListToTail();
            }
        }

        void DisplayerMoveTo(float targetPos, float duration)
        {
            var startPos = _listCursorPos;
            var initialArrowAlpha = _navigationArrows != null ? _navigationArrows.alpha : 1f;
            _scrollMotion.TryCancel();
            _scrollMotion = LMotion.Create(0f, 1f, duration)
                                   .WithScheduler(MotionScheduler.PostLateUpdate)
                                   .WithEase(Ease.Linear)
                                   .WithOnComplete(NormalizeListCursor)
                                   .Bind(progress => UpdateScrollPresentation(progress, startPos, targetPos, initialArrowAlpha));
        }

        /// <summary>Uses one transition clock for the original OutQuad scroll and the arrow fade-out/pause/fade-in.</summary>
        /// <param name="progress">The linear zero-to-one transition time.</param>
        /// <param name="startPos">The cursor position captured before the previous transition was cancelled.</param>
        /// <param name="targetPos">The new desired collection position.</param>
        /// <param name="initialArrowAlpha">The current arrow opacity, retained when a rapid selection interrupts a fade.</param>
        private void UpdateScrollPresentation(float progress, float startPos, float targetPos, float initialArrowAlpha)
        {
            progress = Mathf.Clamp01(progress);
            var scrollProgress = progress * (2f - progress);
            _listCursorPos = Mathf.Lerp(startPos, targetPos, scrollProgress);
            UpdateDisplayerPosition();

            SetNavigationAlpha(MenuTitleDisplayer.GetNavigationAlpha(progress, initialArrowAlpha));
        }

        /// <summary>Updates the optional navigation-only group while preserving input and category opacity.</summary>
        /// <param name="alpha">The arrow opacity to apply.</param>
        private void SetNavigationAlpha(float alpha)
        {
            if (_navigationArrows != null)
            {
                _navigationArrows.alpha = alpha;
            }
        }

        void UpdateSelectedSongCollection()
        {
            if (_collections.Length == 0)
            {
                _currentCollection = _emptyCollection;
                return;
            }
            switch (_listConfig.SelectedDiff)
            {
                case ChartLevel.Easy:
                    _currentCollection = _easySortedCollections[_listDesiredPos];
                    break;
                case ChartLevel.Basic:
                    _currentCollection = _basicSortedCollections[_listDesiredPos];
                    break;
                case ChartLevel.Advance:
                    _currentCollection = _advanceSortedCollections[_listDesiredPos];
                    break;
                case ChartLevel.Expert:
                    _currentCollection = _expertSortedCollections[_listDesiredPos];
                    break;
                case ChartLevel.Master:
                    _currentCollection = _masterSortedCollections[_listDesiredPos];
                    break;
                case ChartLevel.ReMaster:
                    _currentCollection = _reMasterSortedCollections[_listDesiredPos];
                    break;
                case ChartLevel.UTAGE:
                    _currentCollection = _utageSortedCollections[_listDesiredPos];
                    break;
                default:
                    throw new ArgumentOutOfRangeException("sb");
            }
        }
        void UpdateDisplayerPosition()
        {
            var displayerCount = _collectionDisplayers.Length;
            if (displayerCount == 0 || _collections.Length == 0)
            {
                return;
            }

            var firstVisibleIndex = Mathf.Max(0, Mathf.CeilToInt(_listCursorPos - VisibleCollectionDistance));
            var lastVisibleIndex = Mathf.Min(
                _collections.Length - 1,
                Mathf.FloorToInt(_listCursorPos + VisibleCollectionDistance));

            for (var i = 0; i < displayerCount; i++)
            {
                var collectionIndex = _collectionDisplayerIndices[i];
                if (collectionIndex >= firstVisibleIndex && collectionIndex <= lastVisibleIndex)
                {
                    continue;
                }

                _collectionDisplayerIndices[i] = -1;
                _collectionDisplayers[i].SetActive(false);
            }

            for (var collectionIndex = firstVisibleIndex; collectionIndex <= lastVisibleIndex; collectionIndex++)
            {
                if (Array.IndexOf(_collectionDisplayerIndices, collectionIndex) >= 0)
                {
                    continue;
                }

                var displayerIndex = Array.IndexOf(_collectionDisplayerIndices, -1);
                if (displayerIndex < 0)
                {
                    break;
                }
                BindCollectionDisplayer(displayerIndex, collectionIndex);
            }

            for (var i = 0; i < displayerCount; i++)
            {
                var collectionIndex = _collectionDisplayerIndices[i];
                if (collectionIndex < 0)
                {
                    continue;
                }

                var displayer = _collectionDisplayers[i];
                var distance = collectionIndex - _listCursorPos;
                displayer.SetDistance(distance);
            }
        }
        void NormalizeListCursor()
        {
            SetNavigationAlpha(1f);
            _listCursorPos = _listDesiredPos;
            UpdateDisplayerPosition();
        }
        void BindCollectionDisplayer(int displayerIndex, int collectionIndex)
        {
            _collectionDisplayerIndices[displayerIndex] = collectionIndex;
            var displayer = _collectionDisplayers[displayerIndex];
            displayer.SetCollection(_collections[collectionIndex]);
            displayer.SetActive(true);
        }
        static int WrapIndex(int index, int count)
        {
            var wrappedIndex = index % count;
            return wrappedIndex < 0 ? wrappedIndex + count : wrappedIndex;
        }
        void UpdateListConfiguration()
        {
            _listConfig.SelectedDirGuid = _currentCollection.Id;
        }
        void InitCollectionStorage()
        {
            var collections = SongStorage.Collections;
            var newCollections = new SongCollection[collections.Length];

            Parallel.For(0, collections.Length, i =>
            {
                var collection = collections[i];
                if (collection.Type == ChartStorageType.FavoriteList)
                {
                    newCollections[i] = collection;
                }
                else if (collection is OnlineSongCollection onlineCollection)
                {
                    newCollections[i] = new OnlineSongCollection(onlineCollection.Source, onlineCollection.Name, onlineCollection.ToArray())
                    {
                        Id = onlineCollection.Id,
                        DanInfo = collection.DanInfo,
                        Type = collection.Type,
                    };
                }
                else
                {
                    newCollections[i] = new SongCollection(collection.Name, collection.ToArray())
                    {
                        Id = collection.Id,
                        DanInfo = collection.DanInfo,
                        Type = collection.Type,
                    };
                }

                newCollections[i].Reset();
                if (!collection.IsEmpty)
                {
                    newCollections[i].SetCursor(collection.Current);
                }
                newCollections[i].SortAndFilter(SongStorage.OrderBy);
            });
            _collections = newCollections;

            if (SongStorage.OrderBy.SortBy == SortType.ByRank)
            {
                _easySortedCollections = new SongCollection[collections.Length];
                _basicSortedCollections = new SongCollection[collections.Length];
                _advanceSortedCollections = new SongCollection[collections.Length];
                _expertSortedCollections = new SongCollection[collections.Length];
                _masterSortedCollections = new SongCollection[collections.Length];
                _reMasterSortedCollections = new SongCollection[collections.Length];
                _utageSortedCollections = new SongCollection[collections.Length];

                Parallel.For(0, newCollections.Length, i =>
                {
                    var originCollection = newCollections[i];
                    if (originCollection.Type == ChartStorageType.Recommend)
                    {
                        // Keep the server's ranking, and the endpoint that impressions are reported to
                        _easySortedCollections[i] = originCollection;
                        _basicSortedCollections[i] = originCollection;
                        _advanceSortedCollections[i] = originCollection;
                        _expertSortedCollections[i] = originCollection;
                        _masterSortedCollections[i] = originCollection;
                        _reMasterSortedCollections[i] = originCollection;
                        _utageSortedCollections[i] = originCollection;
                        return;
                    }
                    var songs = originCollection.ToArray();
                    var songAndScores = songs.Select(x => (x, ScoreManager.GetSongScores(x))).ToArray();
                    var sortedEasy = Array.Empty<ISongDetail>();
                    var sortedBasic = Array.Empty<ISongDetail>();
                    var sortedAdvance = Array.Empty<ISongDetail>();
                    var sortedExpert = Array.Empty<ISongDetail>();
                    var sortedMaster = Array.Empty<ISongDetail>();
                    var sortedReMaster = Array.Empty<ISongDetail>();
                    var sortedUTAGE = Array.Empty<ISongDetail>();
                    if (MajEnv.UserSettings.Judge.Mode == Settings.JudgeModeOption.Classic)
                    {
                        sortedEasy = songAndScores.OrderByDescending(x => x.Item2.Easy.Acc.Classic)
                                                  .Select(x => x.x)
                                                  .ToArray();
                        sortedBasic = songAndScores.OrderByDescending(x => x.Item2.Basic.Acc.Classic)
                                                   .Select(x => x.x)
                                                   .ToArray();
                        sortedAdvance = songAndScores.OrderByDescending(x => x.Item2.Advance.Acc.Classic)
                                                     .Select(x => x.x)
                                                     .ToArray();
                        sortedExpert = songAndScores.OrderByDescending(x => x.Item2.Expert.Acc.Classic)
                                                     .Select(x => x.x)
                                                     .ToArray();
                        sortedMaster = songAndScores.OrderByDescending(x => x.Item2.Master.Acc.Classic)
                                                     .Select(x => x.x)
                                                     .ToArray();
                        sortedReMaster = songAndScores.OrderByDescending(x => x.Item2.ReMaster.Acc.Classic)
                                                      .Select(x => x.x)
                                                      .ToArray();
                        sortedUTAGE = songAndScores.OrderByDescending(x => x.Item2.UTAGE.Acc.Classic)
                                                   .Select(x => x.x)
                                                   .ToArray();
                    }
                    else
                    {
                        sortedEasy = songAndScores.OrderByDescending(x => x.Item2.Easy.Acc.DX)
                                                  .Select(x => x.x)
                                                  .ToArray();
                        sortedBasic = songAndScores.OrderByDescending(x => x.Item2.Basic.Acc.DX)
                                                   .Select(x => x.x)
                                                   .ToArray();
                        sortedAdvance = songAndScores.OrderByDescending(x => x.Item2.Advance.Acc.DX)
                                                     .Select(x => x.x)
                                                     .ToArray();
                        sortedExpert = songAndScores.OrderByDescending(x => x.Item2.Expert.Acc.DX)
                                                    .Select(x => x.x)
                                                    .ToArray();
                        sortedMaster = songAndScores.OrderByDescending(x => x.Item2.Master.Acc.DX)
                                                    .Select(x => x.x)
                                                    .ToArray();
                        sortedReMaster = songAndScores.OrderByDescending(x => x.Item2.ReMaster.Acc.DX)
                                                      .Select(x => x.x)
                                                      .ToArray();
                        sortedUTAGE = songAndScores.OrderByDescending(x => x.Item2.UTAGE.Acc.DX)
                                                       .Select(x => x.x)
                                                       .ToArray();
                    }
                    _easySortedCollections[i] = new SongCollection(originCollection.Name, sortedEasy)
                    {
                        Id = originCollection.Id
                    };
                    _basicSortedCollections[i] = new SongCollection(originCollection.Name, sortedBasic)
                    {
                        Id = originCollection.Id
                    };
                    _advanceSortedCollections[i] = new SongCollection(originCollection.Name, sortedAdvance)
                    {
                        Id = originCollection.Id
                    };
                    _expertSortedCollections[i] = new SongCollection(originCollection.Name, sortedExpert)
                    {
                        Id = originCollection.Id
                    };
                    _masterSortedCollections[i] = new SongCollection(originCollection.Name, sortedMaster)
                    {
                        Id = originCollection.Id
                    };
                    _reMasterSortedCollections[i] = new SongCollection(originCollection.Name, sortedReMaster)
                    {
                        Id = originCollection.Id
                    };
                    _utageSortedCollections[i] = new SongCollection(originCollection.Name, sortedUTAGE)
                    {
                        Id = originCollection.Id
                    };
                });
            }
            else
            {
                _easySortedCollections = newCollections;
                _basicSortedCollections = newCollections;
                _advanceSortedCollections = newCollections;
                _expertSortedCollections = newCollections;
                _masterSortedCollections = newCollections;
                _reMasterSortedCollections = newCollections;
                _utageSortedCollections = newCollections;
            }
        }
        /// <summary>Restores the visible selection while retaining saved identities when their content is unavailable.</summary>
        void RestoreContextFromConfiguration()
        {
            var selectedCollectionUUID = _listConfig.SelectedDirGuid;
            var selectedSongId = _listConfig.SelectedSongId;
            try
            {
                var index = Array.FindIndex(_collections, x => x.Id == selectedCollectionUUID);
                if (index == -1)
                {
                    index = 0;
                }
                SlideListTo(index, true, true);
                SlideToDifficulty((int)_listConfig.SelectedDiff);
                if (_collections.Length == 0)
                {
                    return;
                }
                if (string.IsNullOrEmpty(selectedSongId))
                {
                    _coverListManager.SlideListToHead();
                }
                else
                {
                    // A missing song still moves the cursor to the first song, as an unmatched cursor always has.
                    var selectedSong = SongStorage.FindSongById(_collections[index], selectedSongId);
                    _coverListManager.SetCursor(selectedSong?.Hash ?? string.Empty, true, true);
                }
            }
            finally
            {
                _listConfig.SelectedDirGuid = selectedCollectionUUID;
                _listConfig.SelectedSongId = selectedSongId;
            }
        }

    }
}
