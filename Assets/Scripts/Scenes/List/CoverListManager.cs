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

        ListManager _listManager;
        PreviewSoundPlayer _previewSoundPlayer;
        TextMeshProUGUI _emptyCollectionMessageDisplayer = null!;

        SongCollection _currentCollection = SongCollection.Empty("Empty");

        SongCoverDisplayer[] _songCoverDisplayers = Array.Empty<SongCoverDisplayer>();
        ThumbnailDisplayer[] _songThumbnailDisplayers = Array.Empty<ThumbnailDisplayer>();

        MotionHandle _scrollMotion;

        readonly RentedList<ISongDetail> _songDetails = new();
        readonly RentedList<SongCoverBinding> _songCoverBindings = new();
        readonly RentedList<SongThumbnailBinding> _songThumbnailBindings = new();

        readonly Queue<SongCoverDisplayer> _idleSongCoverDisplayer = new();
        readonly Queue<ThumbnailDisplayer> _idleSongThumbnailDisplayer = new();

        readonly ListConfig _listConfig = MajEnv.RuntimeConfig?.List ?? new();

        const int DISPLAYER_ANIM_DURATION_MS = 250;
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
            SelectedSong = null;
            var songCoverBindings = _songCoverBindings.AsSpan();
            var songThumbnailBindings = _songThumbnailBindings.AsSpan();
            for (var i = 0; i < _songCount; i++)
            {
                ref var coverBinding = ref songCoverBindings[i];
                ref var thumbnailBinding = ref songThumbnailBindings[i];
                var coverDisplayer = coverBinding.Displayer;
                var thumbnailDisplayer = thumbnailBinding.Displayer;
                if (coverDisplayer is not null)
                {
                    coverBinding.Displayer = null;
                    coverDisplayer.SetActive(false);
                    _idleSongCoverDisplayer.Enqueue(coverDisplayer);
                }
                if (thumbnailDisplayer is not null)
                {
                    thumbnailBinding.Displayer = null;
                    thumbnailDisplayer.SetActive(false);
                    _idleSongThumbnailDisplayer.Enqueue(thumbnailDisplayer);
                }
            }
            _songCount = 0;
            _songDetails.Clear();
            _songCoverBindings.Clear();
            _songThumbnailBindings.Clear();
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
            if(_isEmptyCollection)
            {
                return;
            }
            var songCoverBindings = _songCoverBindings.AsSpan();
            var songThumbnailBindings = _songThumbnailBindings.AsSpan();
            var currentListCursorPos = (int)_listCursorPos;
            for (var i = 0; i < _songCount; i++)
            {
                ref var coverBinding = ref songCoverBindings[i];
                ref var thumbnailBinding = ref songThumbnailBindings[i];
                var absDistance = Math.Abs(i - currentListCursorPos);

                // Update song cover binding
                var coverDisplayer = coverBinding.Displayer;
                if (absDistance > 3)
                {
                    if(coverDisplayer is not null)
                    {
                        coverBinding.Displayer = null;
                        coverDisplayer.SetActive(false);
                        _idleSongCoverDisplayer.Enqueue(coverDisplayer);
                    }
                }
                else
                {
                    if (coverDisplayer is null)
                    {
                        if (_idleSongCoverDisplayer.TryDequeue(out coverDisplayer))
                        {
                            coverBinding.Displayer = coverDisplayer;
                            coverDisplayer.SetSongDetail(coverBinding.SongDetail, loadDelayMS);
                            coverDisplayer.SetActive(true);
                        }
                        else
                        {
                            MajDebug.LogWarning("No idle song cover displayer available.");
                        }                            
                    }                    
                }

                // Update thumbnail binding
                var thumbnailDisplayer = thumbnailBinding.Displayer;
                if (absDistance > 6)
                {
                    if (thumbnailDisplayer is not null)
                    {
                        thumbnailBinding.Displayer = null;
                        thumbnailDisplayer.SetActive(false);
                        _idleSongThumbnailDisplayer.Enqueue(thumbnailDisplayer);
                    }
                }
                else
                {
                    if (thumbnailDisplayer is null)
                    {
                        if (_idleSongThumbnailDisplayer.TryDequeue(out thumbnailDisplayer))
                        {
                            thumbnailBinding.Displayer = thumbnailDisplayer;
                            thumbnailDisplayer.SetSongDetail(thumbnailBinding.SongDetail, loadDelayMS);
                            thumbnailDisplayer.SetActive(true);
                        }
                        else
                        {
                            MajDebug.LogWarning("No idle song thumbnail displayer available.");
                        }
                    }
                }
            }
        }
        void UpdateDisplayerPosition()
        {
            var songCoverBindings = _songCoverBindings.AsSpan();
            var songThumbnailBindings = _songThumbnailBindings.AsSpan();
            SongCoverDisplayer? frontCoverDisplayer = null;
            var frontCoverAbsDelta = float.MaxValue;
            for (var i = 0; i < _songCount; i++)
            {
                ref var coverBinding = ref songCoverBindings[i];
                ref var thumbnailBinding = ref songThumbnailBindings[i];
                var delta = i - _listCursorPos;

                // Update song cover position
                var coverDisplayer = coverBinding.Displayer;
                if (coverDisplayer is not null)
                {
                    coverDisplayer.RectTransform.anchoredPosition = GetCoverDisplayerPositionFromDelta(delta);
                    coverDisplayer.RectTransform.localScale = GetCoverDisplayerScaleFromDelta(delta);
                    coverDisplayer.SetSelectedProgress(GetCoverDisplayerSelectedProgressFromDelta(delta));

                    var absDelta = Mathf.Abs(delta);
                    if (absDelta < frontCoverAbsDelta)
                    {
                        frontCoverAbsDelta = absDelta;
                        frontCoverDisplayer = coverDisplayer;
                    }
                }

                // Update thumbnail position
                var thumbnailDisplayer = thumbnailBinding.Displayer;
                if (thumbnailDisplayer is not null)
                {
                    thumbnailDisplayer.RectTransform.anchoredPosition = GetThumbnailDisplayerPositionFromDelta(delta);
                }
            }
            frontCoverDisplayer?.RectTransform.SetAsLastSibling();
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
