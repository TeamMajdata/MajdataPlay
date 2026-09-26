using Cysharp.Threading.Tasks;
using MajdataPlay.Drawing;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List.Models
{
    internal class SongCoverDisplayer : CoverSmallDisplayer
    {
        public Sprite? CurrentCoverSprite
        {
            get
            {
                var sprite = _coverDisplayer.sprite;
                if (sprite == null || sprite.rect.width <= 0 || sprite.rect.height <= 0)
                {
                    return null;
                }

                return sprite;
            }
        }

        [SerializeField]
        [FormerlySerializedAs("coverDisplayer")]
        Image _coverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelDisplayerListRoot")]
        GameObject _levelDisplayerListRoot;

        [SerializeField]
        [FormerlySerializedAs("loadingComponent")]
        GameObject _loadingComponent;

        CancellationTokenSource _cts = new();

        RectTransform? _backgroundTransform;
        RectTransform? _coverTransform;
        Vector2 _backgroundOriginPosition;
        Vector2 _backgroundOriginSize;
        Vector2 _coverOriginPosition;
        Vector2 _coverOriginSize;

        LevelDisplayer[] _levelDisplayers = Array.Empty<LevelDisplayer>();
        LevelDPOriginPosition[] _levelDPOriginPositions = Array.Empty<LevelDPOriginPosition>();

        const float CENTER_STYLE_BACKGROUND_SIZE = 356.32f;
        const float CENTER_STYLE_BACKGROUND_Y = -4f;
        const float CENTER_STYLE_COVER_SIZE = 285.22f;
        const float SMALL_LEVELS_VISIBLE_PROGRESS_THRESHOLD = 0.01f;

        protected override void Awake()
        {
            base.Awake();
            _backgroundTransform = transform.Find("BG") as RectTransform ?? transform.Find("bg") as RectTransform;
            _coverTransform = _coverDisplayer.GetComponent<RectTransform>();

            var levelDisplayerListRoot = _levelDisplayerListRoot.transform;
            CaptureVisualOrigin();
            var displayerCount = levelDisplayerListRoot.childCount;
            _levelDisplayers = new LevelDisplayer[displayerCount];
            _levelDPOriginPositions = new LevelDPOriginPosition[displayerCount];
            for (var i = 0; i < displayerCount; i++)
            {
                var child = levelDisplayerListRoot.GetChild(i);
                var textTransform = child.Find("Text");
                if (textTransform == null)
                {
                    throw new Exception($"Level displayer {i} does not have a child named 'Text'.");
                }
                var textDisplayer = textTransform.GetComponent<TextMeshProUGUI>();
                if (textDisplayer == null)
                {
                    throw new Exception($"Level displayer {i}'s 'Text' child does not have a TextMeshProUGUI component.");
                }
                _levelDisplayers[i] = new LevelDisplayer
                {
                    Object = child.gameObject,
                    Transform = child,
                    TextTransform = textTransform,
                    TextDisplayer = textDisplayer
                };
                _levelDPOriginPositions[i] = new LevelDPOriginPosition
                {
                    Position = child.localPosition,
                    Rotation = child.localRotation,
                    TextRotation = textTransform.localRotation
                };
            }
            SetSelectedProgress(0f);
        }
        public void SetSongDetail(ISongDetail detail, int loadDelayMS = 0)
        {
            SetLevelText(detail);
            _coverDisplayer.sprite = SpriteLoader.EmptySprite;
            if(!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                _cts = new();
            }
            ListManager.AllBackgroundTasks.Add(SetCoverAsync(detail, loadDelayMS, _cts.Token)
                                               .Register($"[{nameof(SongCoverDisplayer)}]Load song cover"));
        }
        public void SetActive(bool state)
        {
            if (!state)
            {
                SetSelectedProgress(0f);
            }
            gameObject.SetActive(state);
        }
        public void SetSelectedProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);

            var t = Mathf.SmoothStep(0f, 1f, progress);
            ApplySelectedProgress(t);
        }
        void CaptureVisualOrigin()
        {
            if (_backgroundTransform is not null)
            {
                _backgroundOriginPosition = _backgroundTransform.anchoredPosition;
                _backgroundOriginSize = _backgroundTransform.sizeDelta;
            }
            if (_coverTransform is not null)
            {
                _coverOriginPosition = _coverTransform.anchoredPosition;
                _coverOriginSize = _coverTransform.sizeDelta;
            }
        }
        void ApplySelectedProgress(float progress)
        {
            if (_backgroundTransform is not null)
            {
                _backgroundTransform.anchoredPosition = Vector2.Lerp(
                    _backgroundOriginPosition,
                    new Vector2(0, CENTER_STYLE_BACKGROUND_Y),
                    progress);
                _backgroundTransform.sizeDelta = Vector2.Lerp(
                    _backgroundOriginSize,
                    Vector2.one * CENTER_STYLE_BACKGROUND_SIZE,
                    progress);
            }
            if (_coverTransform is not null)
            {
                _coverTransform.anchoredPosition = Vector2.Lerp(_coverOriginPosition, Vector2.zero, progress);
                _coverTransform.sizeDelta = Vector2.Lerp(
                    _coverOriginSize,
                    Vector2.one * CENTER_STYLE_COVER_SIZE,
                    progress);
            }
            _levelDisplayerListRoot.SetActive(progress <= SMALL_LEVELS_VISIBLE_PROGRESS_THRESHOLD);
        }
        void SetLevelText(ISongDetail songDetail)
        {
            var posIndex = _levelDPOriginPositions.Length - 1;
            for (var i = songDetail.Levels.Length - 1; i >= 0; i--)
            {
                var displayer = _levelDisplayers[i];
                var level = songDetail.Levels[i];
                if(string.IsNullOrEmpty(level))
                {
                    displayer.Object.SetActive(false);
                    continue;
                }
                displayer.Object.SetActive(true);
                var originPos = _levelDPOriginPositions[posIndex--];
                displayer.Transform.localPosition = originPos.Position;
                displayer.Transform.localRotation = originPos.Rotation;
                displayer.TextTransform.localRotation = originPos.TextRotation;
                displayer.TextDisplayer.text = level;
            }
        }
        async Task SetCoverAsync(ISongDetail songDetail, int loadDelayMS, CancellationToken token = default)
        {
            _loadingComponent.SetActive(true);
            _coverDisplayer.sprite = null!;
            if(!songDetail.IsCompressedCoverLoaded)
            {
                await Task.Delay(loadDelayMS, token);
            }
            var cover = await songDetail.GetCoverAsync(true, token: token);
            if(token.IsCancellationRequested)
            {
                return;
            }
            await UniTask.SwitchToMainThread();
            _coverDisplayer.sprite = cover;
            _loadingComponent.SetActive(false);
        }

        readonly struct LevelDisplayer
        {
            public required GameObject Object { get; init; }
            public required Transform Transform { get; init; }
            public required Transform TextTransform { get; init; }
            public required TextMeshProUGUI TextDisplayer { get; init; }
        }
        readonly struct LevelDPOriginPosition
        {
            public Vector3 Position { get; init; }
            public Quaternion Rotation { get; init; }
            public Quaternion TextRotation { get; init; }
        }
    }
}
