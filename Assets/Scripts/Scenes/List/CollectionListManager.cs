using LitMotion;
using MajdataPlay.Collections;
using MajdataPlay.Editor;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Settings.Runtime;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List
{
    public class CollectionListManager : MonoBehaviour
    {
        const int DISPLAYER_ANIM_DURATION_MS = 250;
        const float VISIBLE_COLLECTION_DISTANCE = 3f;

        public SongCollection SelectedCollection => _currentCollection;

        #region icon and color ref
        [SerializeField]
        [FormerlySerializedAs("onlineCollectionIcon")]
        Sprite _onlineCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("onlineCollectionIconColor")]
        Color _onlineCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("folderCollectionIcon")]
        Sprite _folderCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("folderCollectionIconColor")]
        Color _folderCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("favoriteCollectionIcon")]
        Sprite _favoriteCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("favoriteCollectionIconColor")]
        Color _favoriteCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("onlineFavoriteCollectionIcon")]
        Sprite _onlineFavoriteCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("onlineFavoriteCollectionIconColor")]
        Color _onlineFavoriteCollectionIconColor;

        [SerializeField]
        [FormerlySerializedAs("danCollectionIcon")]
        Sprite _danCollectionIcon;

        [SerializeField]
        [FormerlySerializedAs("danCollectionIconColor")]
        Color _danCollectionIconColor;
        #endregion

        [SerializeField]
        [FormerlySerializedAs("centerCoverDisplayer")]
        CenterCoverDisplayer _centerCoverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("coverListManager")]
        CoverListManager _coverListManager;

        [SerializeField]
        [FormerlySerializedAs("collectionListRoot")]
        GameObject _collectionListRoot;

        [SerializeField]
        [FormerlySerializedAs("nameDisplayer")]
        TextMeshProUGUI _nameDisplayer;

        [SerializeField]
        [FormerlySerializedAs("iconDisplayer")]
        Image _iconDisplayer;

        [SerializeField, ReadOnlyField]
        int _selectedDifficulty = 0;

        [SerializeField, ReadOnlyField]
        int _listDesiredPos = 0;
        [SerializeField, ReadOnlyField]
        float _listCursorPos = 0;

        SongCollection _currentCollection = SongCollection.Empty("Empty");
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
        void OnDestroy()
        {
            _scrollMotion.TryCancel();
        }
        internal void Init()
        {
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
            var oldDesiredPos = _listDesiredPos;
            var crossesBoundary = pos < 0 || pos >= _collections.Length;
            _listDesiredPos = WrapIndex(pos, _collections.Length);
            UpdateSelectedSongCollection();
            UpdateListConfiguration();
            UpdateCollectionIcon();
            var shouldUpdate = _listDesiredPos != oldDesiredPos || forceUpdate;
            if (!shouldUpdate)
            {
                return;
            }

            _coverListManager.SetCollection(_currentCollection, false);
            if (disableAnimation || crossesBoundary)
            {
                _scrollMotion.TryCancel();
                _listCursorPos = _listDesiredPos;
                UpdateDisplayerPosition();
            }
            else
            {
                DisplayerMoveTo(_listDesiredPos, DISPLAYER_ANIM_DURATION_MS / 1000f);
            }
        }
        void SlideToDifficulty(int pos)
        {
            _selectedDifficulty = WrapIndex(pos, 7);
            var chartLevel = (ChartLevel)_selectedDifficulty;
            _listConfig.SelectedDiff = chartLevel;
            UpdateSelectedSongCollection();

            _centerCoverDisplayer.SetDifficulty(_selectedDifficulty);
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
            _scrollMotion.TryCancel();
            _scrollMotion = LMotion.Create(_listCursorPos, targetPos, duration)
                                   .WithScheduler(MotionScheduler.PostLateUpdate)
                                   .WithEase(Ease.OutQuad)
                                   .WithOnComplete(NormalizeListCursor)
                                   .Bind(x =>
                                   {
                                       _listCursorPos = x;
                                       UpdateDisplayerPosition();
                                   });
        }
        void UpdateSelectedSongCollection()
        {
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
            _nameDisplayer.text = _currentCollection.Name;
        }
        void UpdateDisplayerPosition()
        {
            var displayerCount = _collectionDisplayers.Length;
            if (displayerCount == 0 || _collections.Length == 0)
            {
                return;
            }

            var firstVisibleIndex = Mathf.Max(0, Mathf.CeilToInt(_listCursorPos - VISIBLE_COLLECTION_DISTANCE));
            var lastVisibleIndex = Mathf.Min(
                _collections.Length - 1,
                Mathf.FloorToInt(_listCursorPos + VISIBLE_COLLECTION_DISTANCE));

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
                displayer.RectTransform.anchoredPosition = GetDisplayerPositionFromDelta(distance);
            }
        }
        void NormalizeListCursor()
        {
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
            _listConfig.SelectedDir = _listDesiredPos;
            _listConfig.SelectedDirGuid = _currentCollection.Id;
        }
        void UpdateCollectionIcon()
        {
            switch (_currentCollection.Type)
            {
                case ChartStorageType.List:
                    if (_currentCollection.IsOnline)
                    {
                        _iconDisplayer.enabled = true;
                        _iconDisplayer.sprite = _onlineCollectionIcon;
                        _iconDisplayer.color = _onlineCollectionIconColor;
                    }
                    else
                    {
                        _iconDisplayer.enabled = false;
                    }
                    break;
                case ChartStorageType.Dan:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _danCollectionIcon;
                    _iconDisplayer.color = _danCollectionIconColor;
                    break;
                case ChartStorageType.PlayList:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _folderCollectionIcon;
                    _iconDisplayer.color = _folderCollectionIconColor;
                    break;
                case ChartStorageType.FavoriteList:
                    _iconDisplayer.enabled = true;
                    _iconDisplayer.sprite = _favoriteCollectionIcon;
                    _iconDisplayer.color = _favoriteCollectionIconColor;
                    break;
            }
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
                    var songs = originCollection.ToArray();
                    var songAndScores = songs.Select(x => (x, ScoreManager.GetSongScores(x))).ToArray();
                    var sortedEasy = Array.Empty<ISongDetail>();
                    var sortedBasic = Array.Empty<ISongDetail>();
                    var sortedAdvance = Array.Empty<ISongDetail>();
                    var sortedExpert = Array.Empty<ISongDetail>();
                    var sortedMaster = Array.Empty<ISongDetail>();
                    var sortedReMaster = Array.Empty<ISongDetail>();
                    var sortedUTAGE = Array.Empty<ISongDetail>();
                    if (MajEnv.Settings.Judge.Mode == Settings.JudgeModeOption.Classic)
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
        void RestoreContextFromConfiguration()
        {
            var selectedCollectionUUID = _listConfig.SelectedDirGuid;
            var selectedSongHash = _listConfig.SelectedSongHash;
            var index = Array.FindIndex(_collections, x => x.Id == selectedCollectionUUID);
            if(index == -1)
            {
                index = 0;
            }
            SlideListTo(index, true, true);
            SlideToDifficulty((int)_listConfig.SelectedDiff);
            if(string.IsNullOrEmpty(selectedSongHash))
            {
                _coverListManager.SlideListToHead();
            }
            else
            {
                _coverListManager.SetCursor(selectedSongHash, true, true);
            }            
        }

        Vector2 GetDisplayerPositionFromDelta(float delta)
        {
            const int X_POS_WITH_DELTA_1 = 240;
            const int X_POS_WITH_DELTA_2 = 390;
            const int X_POS_WITH_DELTA_3 = 540;
            const int X_POS_WITH_DELTA_4 = 690;
            var absDelta = Mathf.Abs(delta);

            var x = absDelta switch
            {
                <= 1f => 240f * absDelta,
                <= 2f => Mathf.Lerp(X_POS_WITH_DELTA_1, X_POS_WITH_DELTA_2, absDelta - 1f),
                <= 3f => Mathf.Lerp(X_POS_WITH_DELTA_2, X_POS_WITH_DELTA_3, absDelta - 2f),
                <= 4f => Mathf.Lerp(X_POS_WITH_DELTA_3, X_POS_WITH_DELTA_4, absDelta - 3f),
                _ => X_POS_WITH_DELTA_4
            };

            return new Vector2(Mathf.Sign(delta) * x, 0);
        }
    }
}
