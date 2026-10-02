using LitMotion;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.i18n;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.List.Models;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Threading;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.List
{
    public class CoverListManager : MonoBehaviour
    {
        public ISongDetail? SelectedSong { get; private set; } = null;
        public float PreloadCooldownTimer
        {
            get
            {
                return _preloadCooldownTimer;
            }
        }

        [SerializeField]
        [FormerlySerializedAs("songCoverDisplayerPrefab")]
        GameObject _songCoverDisplayerPrefab;

        [SerializeField]
        [FormerlySerializedAs("centerCoverDisplayer")]
        CenterCoverDisplayer _centerCoverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("songCoverListRoot")]
        GameObject _songCoverListRoot;

        [SerializeField]
        [FormerlySerializedAs("thumbnailListRoot")]
        GameObject _thumbnailListRoot;

        [SerializeField]
        [FormerlySerializedAs("progressDisplayer")]
        TextMeshProUGUI _progressDisplayer;

        [SerializeField]
        [FormerlySerializedAs("collectionListManager")]
        CollectionListManager _collectionListManager;

        [SerializeField]
        [FormerlySerializedAs("emptyCollectionNotice")]
        GameObject _emptyCollectionNotice;

        [SerializeField, ReadOnlyField]
        int _selectedDifficulty = 0;

        [SerializeField, ReadOnlyField]
        int _songCount = 0;

        // List cursor position and desired position
        [SerializeField, ReadOnlyField]
        int _listDesiredPos = 0;

        [SerializeField, ReadOnlyField]
        float _listCursorPos = 0;

        float _preloadCooldownTimer = 0.5f;
        bool _isNeedPreload = false;
        bool _isEmptyCollection = true;
        int _scrollMotionVersion = 0;
        int _firstVisibleCoverIndex = 0;
        int _lastVisibleCoverIndex = -1;
        int _firstVisibleThumbnailIndex = 0;
        int _lastVisibleThumbnailIndex = -1;
        bool _hasPendingBindings = false;

        ListManager _listManager;
        PreviewSoundPlayer _previewSoundPlayer;
        TextMeshProUGUI _emptyCollectionMessageDisplayer = null!;

        SongCollection _currentCollection = SongCollection.Empty("Empty");

        SongCoverDisplayer[] _songCoverDisplayers = Array.Empty<SongCoverDisplayer>();
        ThumbnailDisplayer[] _songThumbnailDisplayers = Array.Empty<ThumbnailDisplayer>();

        MotionHandle _scrollMotion;

        readonly PooledList<ISongDetail> _songDetails = new();
        readonly PooledList<SongCoverBinding> _songCoverBindings = new();
        readonly PooledList<SongThumbnailBinding> _songThumbnailBindings = new();

        readonly Queue<SongCoverDisplayer> _idleSongCoverDisplayer = new();
        readonly Queue<ThumbnailDisplayer> _idleSongThumbnailDisplayer = new();

        readonly ListConfig _listConfig = MajEnv.RuntimeConfig?.List ?? new();

        const int DISPLAYER_ANIM_DURATION_MS = 250;
        const int COVER_VISIBLE_DISTANCE = 3;
        const int THUMBNAIL_VISIBLE_DISTANCE = 6;
        const float SELECTED_COVER_SCALE = 1f;
        const float UNSELECTED_COVER_SCALE = 0.86f;

        #region Unity Lifecycle
        void Awake()
        {
            Majdata<CoverListManager>.Instance = this;
            _previewSoundPlayer = GetComponent<PreviewSoundPlayer>();
            _emptyCollectionMessageDisplayer = _emptyCollectionNotice.GetComponentInChildren<TextMeshProUGUI>(true);
            Localization.OnLanguageChanged += OnLanguageChanged;
            UpdateEmptyCollectionMessage();
        }
        void Start()
        {
            _listManager = Majdata<ListManager>.Instance!;
            var songCoverListRoot = _songCoverListRoot.transform;
            var coverDisplayerCount = songCoverListRoot.childCount;
            _songCoverDisplayers = new SongCoverDisplayer[coverDisplayerCount];
            for (var i = 0; i < coverDisplayerCount; i++)
            {
                var displayerTransform = songCoverListRoot.GetChild(i);
                var displayer = displayerTransform.GetComponent<SongCoverDisplayer>();
                displayer.SetActive(false);
                if (displayer is null)
                {
                    throw new InvalidOperationException($"Child {i} of {_songCoverListRoot.name} does not have a SongCoverDisplayer component.");
                }                
                _songCoverDisplayers[i] = displayer;
                _idleSongCoverDisplayer.Enqueue(displayer);
            }

            var thumbnailListRoot = _thumbnailListRoot.transform;
            var thumbnailDisplayerCount = thumbnailListRoot.childCount;
            _songThumbnailDisplayers = new ThumbnailDisplayer[thumbnailDisplayerCount];
            for (var i = 0; i < thumbnailDisplayerCount; i++)
            {
                var displayerTransform = thumbnailListRoot.GetChild(i);
                var displayer = displayerTransform.GetComponent<ThumbnailDisplayer>();
                displayer.SetActive(false);
                if (displayer is null)
                {
                    throw new InvalidOperationException($"Child {i} of {_thumbnailListRoot.name} does not have a ThumbnailDisplayer component.");
                }
                _songThumbnailDisplayers[i] = displayer;
                _idleSongThumbnailDisplayer.Enqueue(displayer);
            }
            _centerCoverDisplayer.SetEmbeddedCoverVisible(false);
        }
        void OnDestroy()
        {
            _scrollMotion.TryCancel();
            Localization.OnLanguageChanged -= OnLanguageChanged;
            Majdata<CoverListManager>.Free();
            
            _idleSongCoverDisplayer.Clear();
        }
        void OnLanguageChanged(object? sender, Language language)
        {
            UpdateEmptyCollectionMessage();
        }
        void UpdateEmptyCollectionMessage()
        {
            _emptyCollectionMessageDisplayer.text = "MAJTEXT_LIST_EMPTY_COLLECTION".i18n();
        }
        #endregion
        internal void SetCollection(SongCollection collection, bool keepCursor)
        {
            if(collection is null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            if(collection == _currentCollection)
            {
                return;
            }
            var oldSelectedHash = SelectedSong?.Hash ?? string.Empty;
            _currentCollection = collection;
            Clear();
            if (!collection.IsEmpty)
            {
                _songCoverListRoot.SetActive(true);
                _thumbnailListRoot.SetActive(true);
                _centerCoverDisplayer.SetActive(true);
                _emptyCollectionNotice.SetActive(false);
                collection.Index = 0;                
                _isEmptyCollection = false;

                _songCount = _currentCollection.Count;
                _listCursorPos = 0;
                _listDesiredPos = 0;
                for (var i = 0; i < _songCount; i++)
                {
                    var songDetail = _currentCollection[i];
                    var coverBinding = new SongCoverBinding()
                    {
                        SongDetail = songDetail
                    };
                    var thumbnailBinding = new SongThumbnailBinding()
                    {
                        SongDetail = songDetail
                    };
                    _songDetails.Add(songDetail);
                    _songCoverBindings.Add(coverBinding);
                    _songThumbnailBindings.Add(thumbnailBinding);
                }
                if (keepCursor && !string.IsNullOrEmpty(oldSelectedHash))
                {
                    SetCursorInternal(oldSelectedHash, true, true);
                }
                else
                {
                    UpdateListConfiguration();
                    UpdateDisplayerBinding(0);
                    UpdateDisplayerPosition();
                }
            }
            else
            {
                _isEmptyCollection = true;
                SelectedSong = null;
                UpdateListConfiguration();
                _songCoverListRoot.SetActive(false);
                _thumbnailListRoot.SetActive(false);
                _centerCoverDisplayer.SetActive(false);
                _emptyCollectionNotice.SetActive(true);
                _progressDisplayer.text = $"Ciallo～(∠・ω< )⌒★";
            }
        }

        public void SlideList(int delta, bool disableAnimation = false, bool forceUpdate = false, int loadDelayMS = DISPLAYER_ANIM_DURATION_MS)
        {
            if(_isEmptyCollection)
            {
                if(delta > 0)
                {
                    _collectionListManager.NextCollection();
                }
                else if(delta < 0)
                {
                    _collectionListManager.PreviousCollection();
                }
                return;
            }
            
            var nP = _listDesiredPos + delta;
            SlideListTo(nP, disableAnimation, forceUpdate, loadDelayMS);
        }
        public void RandomSelect()
        {
            if(_isEmptyCollection)
            {
                return;
            }
            var randomizer = MajEnv.Randomizer;
            var index = randomizer.Next(0, _songCount);
            SlideListTo(index, false, false, 500);
        }
        public void SlideListToHead()
        {
            if (_isEmptyCollection)
            {
                return;
            }
            SlideListTo(0, true, false, 500);
        }
        public void SlideListToTail()
        {
            if (_isEmptyCollection)
            {
                return;
            }
            SlideListTo(_songCount - 1, true, false, 500);
        }
        void SlideListTo(int pos, bool disableAnimation, bool forceUpdate, int loadDelayMS)
        {
            var oldDesiredPos = _listDesiredPos;
            _listDesiredPos = pos;
            if (_listDesiredPos < 0)
            {
                _listDesiredPos = 0;
                _collectionListManager.PreviousCollection();
                return;
            }
            else if (_listDesiredPos >= _songCount)
            {
                _listDesiredPos = _songCount - 1;
                _collectionListManager.NextCollection();
                return;
            }

            
            var shouldUpdateDisplayer = _listDesiredPos != oldDesiredPos || forceUpdate;
            SelectedSong = _songDetails[_listDesiredPos];
            _progressDisplayer.text = $"{_listDesiredPos + 1}/<size=70%>{_songCount}";
            UpdateListConfiguration();
            if (disableAnimation)
            {
                _scrollMotion.TryCancel();
                if (shouldUpdateDisplayer)
                {
                    _listCursorPos = _listDesiredPos;
                    UpdateDisplayerBinding(loadDelayMS);
                    UpdateDisplayerPosition();
                }
                UpdateCenterDisplayer(loadDelayMS);
            }
            else
            {
                if (shouldUpdateDisplayer)
                {
                    _centerCoverDisplayer.SetEmbeddedCoverVisible(false);
                    DisplayerMoveTo(
                        _listDesiredPos,
                        DISPLAYER_ANIM_DURATION_MS / 1000f,
                        loadDelayMS,
                        () => UpdateCenterDisplayer(loadDelayMS));
                }
            }            
        }

        void Clear()
        {
            _scrollMotion.TryCancel();
            _scrollMotionVersion++;
            SelectedSong = null;
            for (var i = _firstVisibleCoverIndex; i <= _lastVisibleCoverIndex; i++)
            {
                ReleaseSongCoverBinding(i);
            }
            for (var i = _firstVisibleThumbnailIndex; i <= _lastVisibleThumbnailIndex; i++)
            {
                ReleaseSongThumbnailBinding(i);
            }
            _firstVisibleCoverIndex = 0;
            _lastVisibleCoverIndex = -1;
            _firstVisibleThumbnailIndex = 0;
            _lastVisibleThumbnailIndex = -1;
            _hasPendingBindings = false;
            _songCount = 0;
            _songDetails.Clear();
            _songCoverBindings.Clear();
            _songThumbnailBindings.Clear();
        }
        void ReleaseSongCoverBinding(int index)
        {
            ref var binding = ref _songCoverBindings.AsSpan()[index];
            var displayer = binding.Displayer;
            if (displayer is null)
            {
                return;
            }
            binding.Displayer = null;
            displayer.SetActive(false);
            _idleSongCoverDisplayer.Enqueue(displayer);
        }
        void ReleaseSongThumbnailBinding(int index)
        {
            ref var binding = ref _songThumbnailBindings.AsSpan()[index];
            var displayer = binding.Displayer;
            if (displayer is null)
            {
                return;
            }
            binding.Displayer = null;
            displayer.SetActive(false);
            _idleSongThumbnailDisplayer.Enqueue(displayer);
        }
        void DisplayerMoveTo(float targetPos, float duration, int loadDelayMS, Action? onComplete = null)
        {
            _scrollMotion.TryCancel();
            var motionVersion = ++_scrollMotionVersion;
            _scrollMotion = LMotion.Create(_listCursorPos, targetPos, duration)
                                   .WithScheduler(MotionScheduler.PostLateUpdate)
                                   .WithEase(Ease.OutQuad)
                                   .WithOnComplete(() =>
                                   {
                                       if (motionVersion == _scrollMotionVersion)
                                       {
                                           onComplete?.Invoke();
                                       }
                                   })
                                   .Bind(x =>
                                   {
                                       _listCursorPos = x;
                                       UpdateDisplayerBinding(loadDelayMS);
                                       UpdateDisplayerPosition();
                                   });
        }
        void UpdateCenterDisplayer(int loadDelayMS)
        {
            _centerCoverDisplayer.SetSongDetail(SelectedSong!, loadDelayMS, GetSelectedCoverSpriteSnapshot());
            _centerCoverDisplayer.SetEmbeddedCoverVisible(true);
        }
        Sprite? GetSelectedCoverSpriteSnapshot()
        {
            var coverDisplayer = _songCoverBindings.AsSpan()[_listDesiredPos].Displayer;

            return coverDisplayer?.CurrentCoverSprite;
        }
        void UpdateDisplayerBinding(int loadDelayMS)
        {
            if (_isEmptyCollection)
            {
                return;
            }
            var currentListCursorPos = (int)_listCursorPos;
            var firstCoverIndex = Math.Max(0, currentListCursorPos - COVER_VISIBLE_DISTANCE);
            var lastCoverIndex = Math.Min(_songCount - 1, currentListCursorPos + COVER_VISIBLE_DISTANCE);
            var firstThumbnailIndex = Math.Max(0, currentListCursorPos - THUMBNAIL_VISIBLE_DISTANCE);
            var lastThumbnailIndex = Math.Min(_songCount - 1, currentListCursorPos + THUMBNAIL_VISIBLE_DISTANCE);
            if (!_hasPendingBindings &&
                firstCoverIndex == _firstVisibleCoverIndex && lastCoverIndex == _lastVisibleCoverIndex &&
                firstThumbnailIndex == _firstVisibleThumbnailIndex && lastThumbnailIndex == _lastVisibleThumbnailIndex)
            {
                return;
            }

            // Return every outgoing displayer before binding incoming songs. This also
            // keeps backward scrolling and long jumps from temporarily exhausting the pool.
            for (var i = _firstVisibleCoverIndex; i <= _lastVisibleCoverIndex; i++)
            {
                if (i < firstCoverIndex || i > lastCoverIndex)
                {
                    ReleaseSongCoverBinding(i);
                }
            }
            for (var i = _firstVisibleThumbnailIndex; i <= _lastVisibleThumbnailIndex; i++)
            {
                if (i < firstThumbnailIndex || i > lastThumbnailIndex)
                {
                    ReleaseSongThumbnailBinding(i);
                }
            }

            _firstVisibleCoverIndex = firstCoverIndex;
            _lastVisibleCoverIndex = lastCoverIndex;
            _firstVisibleThumbnailIndex = firstThumbnailIndex;
            _lastVisibleThumbnailIndex = lastThumbnailIndex;
            _hasPendingBindings = false;
            var songCoverBindings = _songCoverBindings.AsSpan();
            for (var i = firstCoverIndex; i <= lastCoverIndex; i++)
            {
                ref var binding = ref songCoverBindings[i];
                if (binding.Displayer is not null)
                {
                    continue;
                }
                if (_idleSongCoverDisplayer.TryDequeue(out var displayer))
                {
                    binding.Displayer = displayer;
                    displayer.SetSongDetail(binding.SongDetail, loadDelayMS);
                    displayer.SetActive(true);
                }
                else
                {
                    _hasPendingBindings = true;
                    MajDebug.LogWarning("No idle song cover displayer available.");
                }
            }
            var songThumbnailBindings = _songThumbnailBindings.AsSpan();
            for (var i = firstThumbnailIndex; i <= lastThumbnailIndex; i++)
            {
                ref var binding = ref songThumbnailBindings[i];
                if (binding.Displayer is not null)
                {
                    continue;
                }
                if (_idleSongThumbnailDisplayer.TryDequeue(out var displayer))
                {
                    binding.Displayer = displayer;
                    displayer.SetSongDetail(binding.SongDetail, loadDelayMS);
                    displayer.SetActive(true);
                }
                else
                {
                    _hasPendingBindings = true;
                    MajDebug.LogWarning("No idle song thumbnail displayer available.");
                }
            }
        }
        void UpdateDisplayerPosition()
        {
            var songCoverBindings = _songCoverBindings.AsSpan();
            SongCoverDisplayer? frontCoverDisplayer = null;
            var frontCoverAbsDelta = float.MaxValue;
            for (var i = _firstVisibleCoverIndex; i <= _lastVisibleCoverIndex; i++)
            {
                var coverDisplayer = songCoverBindings[i].Displayer;
                if (coverDisplayer is null)
                {
                    continue;
                }
                var delta = i - _listCursorPos;
                var rectTransform = coverDisplayer.RectTransform;
                rectTransform.anchoredPosition = GetCoverDisplayerPositionFromDelta(delta);
                var scale = GetCoverDisplayerScaleFromDelta(delta);
                if (rectTransform.localScale != scale)
                {
                    rectTransform.localScale = scale;
                }
                coverDisplayer.SetSelectedProgress(GetCoverDisplayerSelectedProgressFromDelta(delta));

                var absDelta = Mathf.Abs(delta);
                if (absDelta < frontCoverAbsDelta)
                {
                    frontCoverAbsDelta = absDelta;
                    frontCoverDisplayer = coverDisplayer;
                }
            }
            var songThumbnailBindings = _songThumbnailBindings.AsSpan();
            for (var i = _firstVisibleThumbnailIndex; i <= _lastVisibleThumbnailIndex; i++)
            {
                var thumbnailDisplayer = songThumbnailBindings[i].Displayer;
                if (thumbnailDisplayer is not null)
                {
                    var delta = i - _listCursorPos;
                    thumbnailDisplayer.RectTransform.anchoredPosition = GetThumbnailDisplayerPositionFromDelta(delta);
                }
            }
            if (frontCoverDisplayer is not null)
            {
                var rectTransform = frontCoverDisplayer.RectTransform;
                if (rectTransform.GetSiblingIndex() != rectTransform.parent.childCount - 1)
                {
                    rectTransform.SetAsLastSibling();
                }
            }
        }
        void UpdateListConfiguration()
        {
            _listConfig.SelectedSongIndex = _listDesiredPos;
            _listConfig.SelectedSongHash = SelectedSong?.Hash ?? string.Empty;
        }
        

        internal void SetCursor(ISongDetail songDetail, bool disableAnimation = false, bool forceUpdate = false)
        {
            if(_isEmptyCollection)
            {
                return;
            }
            SetCursorInternal(songDetail.Hash, disableAnimation, forceUpdate);
        }
        internal void SetCursor(string hash, bool disableAnimation = false, bool forceUpdate = false)
        {
            if (_isEmptyCollection)
            {
                return;
            }
            SetCursorInternal(hash, disableAnimation, forceUpdate);
        }
        void SetCursorInternal(string hash, bool disableAnimation, bool forceUpdate)
        {
            _currentCollection.SetCursor(hash);
            SlideListTo(_currentCollection.Index, disableAnimation, forceUpdate, DISPLAYER_ANIM_DURATION_MS);
        }
        //async Task AnalyzeAndUpdateBpmLedAsync(ISongDetail songDetail, ChartLevel level)
        //{
        //    await chartAnalyzer.AnalyzeAndDrawGraphAsync(songDetail, level);
        //    if (!IsChartList || !ReferenceEquals(_currentCollection.Current, songDetail) || _listConfig.SelectedDiff != level)
        //    {
        //        return;
        //    }

        //    var bpm = chartAnalyzer.LastAnalyzeBpm;
        //    if (chartAnalyzer.LastAnalyzeIsEmpty)
        //    {
        //        CabinetLed.SetButtonLight(Color.red, 3);
        //        CabinetLed.SetCabinetLight(1.0f);
        //        return;
        //    }
        //    CabinetLed.SetButtonLight(Color.green, 3);
        //    CabinetLed.SetCabinetLight(1.0f);
        //    while (IsChartList &&
        //           ReferenceEquals(_currentCollection.Current, songDetail) &&
        //           _listConfig.SelectedDiff == level &&
        //           _previewSoundPlayer.IsPreviewPending(songDetail) &&
        //           !_previewSoundPlayer.IsPreviewPlaying(songDetail))
        //    {
        //        await UniTask.Yield();
        //    }
        //    if (!IsChartList ||
        //        !ReferenceEquals(_currentCollection.Current, songDetail) ||
        //        _listConfig.SelectedDiff != level ||
        //        !_previewSoundPlayer.IsPreviewPlaying(songDetail))
        //    {
        //        return;
        //    }
        //    if (bpm <= 0f)
        //    {
        //        CabinetLed.SetButtonLight(Color.green, 3);
        //        CabinetLed.SetCabinetLight(1.0f);
        //        return;
        //    }

        //    var halfNoteMs = 120000f / bpm;
        //    CabinetLed.SetSineFunc(3, Color.green, (long)halfNoteMs);
        //    CabinetLed.SetCabinetLightSineFunc(1.0f, (long)(halfNoteMs * 2));
        //}

        Vector2 GetCoverDisplayerPositionFromDelta(float delta)
        {
            const int X_POS_STEP = 169;
            const int X_POS_WITH_DELTA_1 = 264;

            var absDelta = Mathf.Abs(delta);
            if (delta == 0)
            {
                return Vector2.zero;
            }
            else if (absDelta.InRange(0, 1))
            {
                return new Vector2(X_POS_WITH_DELTA_1 * absDelta * Mathf.Sign(delta), 0);
            }
            else
            {
                var index = (int)absDelta;
                var posStartAt = X_POS_WITH_DELTA_1 + (X_POS_STEP * (index - 1));
                var middle = X_POS_STEP * (absDelta - Mathf.Floor(absDelta));

                return new Vector2((posStartAt + middle) * Mathf.Sign(delta), 0);
            }
        }
        Vector3 GetCoverDisplayerScaleFromDelta(float delta)
        {
            var t = Mathf.Clamp01(Mathf.Abs(delta));
            t = Mathf.SmoothStep(0f, 1f, t);
            var scale = Mathf.Lerp(SELECTED_COVER_SCALE, UNSELECTED_COVER_SCALE, t);

            return new Vector3(scale, scale, 1f);
        }
        float GetCoverDisplayerSelectedProgressFromDelta(float delta)
        {
            var t = Mathf.Clamp01(Mathf.Abs(delta));
            t = Mathf.SmoothStep(0f, 1f, t);

            return 1f - t;
        }
        Vector2 GetThumbnailDisplayerPositionFromDelta(float delta)
        {
            const int X_POS_STEP = 80;
            const int X_POS_WITH_DELTA_1 = 203;
            
            var absDelta = Mathf.Abs(delta);
            if (delta == 0)
            {
                return Vector2.zero;
            }
            else if (absDelta.InRange(0, 1))
            {
                return new Vector2(X_POS_WITH_DELTA_1 * absDelta * Mathf.Sign(delta), 0);
            }
            else
            {
                var index = (int)absDelta;
                var posStartAt = X_POS_WITH_DELTA_1 + (X_POS_STEP * (index - 1));
                var middle = X_POS_STEP * (absDelta - Mathf.Floor(absDelta));

                return new Vector2((posStartAt + middle) * Mathf.Sign(delta), 0);
            }
        }
        struct SongCoverBinding
        {
            public ISongDetail SongDetail { get; set; }
            public SongCoverDisplayer? Displayer { get; set; }
            public ValueTask? PreloadTask { get; set; }


            public SongCoverBinding(ISongDetail songDetail, SongCoverDisplayer? displayer)
            {
                SongDetail = songDetail;
                Displayer = displayer;
                PreloadTask = null;
            }
            public void PreloadAsync()
            {
                if(PreloadTask is ValueTask task)
                {
                    if(!task.IsCompleted || task.IsCompletedSuccessfully)
                    {
                        return;
                    }
                }
                var preloadTask = SongDetail.PreloadAsync();
                if(!preloadTask.IsCompleted)
                {
                    ListManager.AllBackgroundTasks.Add(preloadTask.AsTask().Register($"[{nameof(CoverListManager)}]Song detail preload"));
                }
                PreloadTask = preloadTask;
            }
        }
        struct SongThumbnailBinding
        {
            public ISongDetail SongDetail { get; init; }
            public ThumbnailDisplayer? Displayer { get; set; }
            public ValueTask? PreloadTask { get; set; }

            public SongThumbnailBinding(SongDetail songDetail, ThumbnailDisplayer? displayer)
            {
                SongDetail = songDetail;
                Displayer = displayer;
            }
        }
    }
}
