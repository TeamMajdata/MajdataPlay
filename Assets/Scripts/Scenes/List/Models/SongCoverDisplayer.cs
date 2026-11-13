using Cysharp.Threading.Tasks;
using MajdataPlay.Databases;
using MajdataPlay.Drawing;
using MajdataPlay.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List.Models
{
    /// <summary>Owns one song's cover and continuously transforms its existing decorations between side and center layouts.</summary>
    internal class SongCoverDisplayer : CoverSmallDisplayer
    {
        [SerializeField]
        [FormerlySerializedAs("coverDisplayer")]
        Image _coverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelDisplayerListRoot")]
        GameObject _levelDisplayerListRoot;

        [SerializeField]
        [FormerlySerializedAs("loadingComponent")]
        GameObject _loadingComponent;

        /// <summary>Draws the selected difficulty ring around this displayer's own cover.</summary>
        [SerializeField]
        [FormerlySerializedAs("LevelRingDisplayer")]
        private Image _levelRingDisplayer = null!;

        /// <summary>Fades only the existing large selected-difficulty badge.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedLevelGroup")]
        private CanvasGroup _selectedLevelGroup = null!;

        /// <summary>Displays the selected difficulty's name on the large badge.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedLevelTitle")]
        private TextMeshProUGUI _selectedLevelTitle = null!;

        /// <summary>Displays the selected chart level, or a dash when that chart is absent.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedLevelText")]
        private TextMeshProUGUI _selectedLevelText = null!;

        /// <summary>Colors the large selected-difficulty badge independently of its visibility.</summary>
        [SerializeField]
        [FormerlySerializedAs("SelectedLevelColor")]
        private Image _selectedLevelColor = null!;

        CancellationTokenSource _cts = new();

        RectTransform? _backgroundTransform;
        RectTransform? _coverTransform;
        Vector2 _backgroundOriginPosition;
        Vector2 _backgroundOriginSize;
        Vector2 _coverOriginPosition;
        Vector2 _coverOriginSize;
        float _selectedProgress = float.NaN;
        /// <summary>Retains the current eased size and difficulty-layout blend.</summary>
        private float _visualProgress;
        /// <summary>Retains the song whose level labels and cover belong to this pooled displayer.</summary>
        private ISongDetail? _songDetail;
        /// <summary>Retains the selected difficulty even before a song is bound.</summary>
        private int _selectedDifficulty;

        LevelDisplayer[] _levelDisplayers = Array.Empty<LevelDisplayer>();
        LevelDPOriginPosition[] _levelDPOriginPositions = Array.Empty<LevelDPOriginPosition>();

        /// <summary>Defines the centered white frame diameter.</summary>
        private const float CenterBackgroundSize = 365f;
        /// <summary>Defines the centered cover diameter and the scale reference for its decorations.</summary>
        private const float CenterCoverSize = 285f;
        /// <summary>Defines the selected difficulty ring diameter at the centered cover size.</summary>
        private const float CenterRingSize = 310f;
        /// <summary>Places the nearest lower difficulty beside the selected chart.</summary>
        private const float CenterLeftStartRotation = 67.5f;
        /// <summary>Places the nearest higher difficulty beside the selected chart.</summary>
        private const float CenterRightStartRotation = 32.5f;
        /// <summary>Separates the remaining centered difficulty badges.</summary>
        private const float CenterRotationStep = 10f;
        /// <summary>Retains the original centered small-badge pivot.</summary>
        private static readonly Vector2 s_centerLevelPivot = new(0.5f, -1.2f);

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
                var child = (RectTransform)levelDisplayerListRoot.GetChild(i);
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
                var group = child.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    throw new InvalidOperationException($"Level displayer {i} does not have a CanvasGroup component.");
                }
                _levelDisplayers[i] = new LevelDisplayer
                {
                    Object = child.gameObject,
                    Transform = child,
                    Group = group,
                    TextTransform = textTransform,
                    TextDisplayer = textDisplayer
                };
                _levelDPOriginPositions[i] = new LevelDPOriginPosition
                {
                    Position = child.localPosition,
                    Rotation = child.localRotation,
                    Pivot = child.pivot,
                    TextRotation = textTransform.localRotation
                };
            }
            SetSelectedProgress(0f);
            SetDifficulty(_selectedDifficulty);
        }

        private void OnDestroy()
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        /// <summary>Binds chart labels and loads the cover on the same pooled renderer used throughout selection movement.</summary>
        /// <param name="detail">The song represented by this displayer.</param>
        /// <param name="loadDelayMS">The delay before requesting an uncached compressed cover.</param>
        public void SetSongDetail(ISongDetail detail, int loadDelayMS = 0)
        {
            _songDetail = detail;
            SetLevelText(detail);
            UpdateSelectedLevelText();
            UpdateLevelLayout(_visualProgress);
            _coverDisplayer.sprite = SpriteLoader.EmptySprite;
            _cts.Cancel();
            _cts.Dispose();
            _cts = new();
            ListManager.AllBackgroundTasks.Add(SetCoverAsync(detail, loadDelayMS, _cts.Token)
                                               .Register($"[{nameof(SongCoverDisplayer)}]Load song cover"));
        }
        /// <summary>Updates the selected ring and badges without replacing this displayer's cover or song.</summary>
        /// <param name="difficulty">The selected chart difficulty index.</param>
        internal void SetDifficulty(int difficulty)
        {
            _selectedDifficulty = difficulty;
            var color = GameRuntime.Instance.DifficultyColors[difficulty];
            _selectedLevelColor.color = color;
            color.a = _visualProgress;
            _levelRingDisplayer.color = color;
            UpdateSelectedLevelText();
            UpdateLevelLayout(_visualProgress);
        }
        public void SetActive(bool state)
        {
            if (!state)
            {
                SetSelectedProgress(0f);
            }
            gameObject.SetActive(state);
        }
        /// <summary>Continuously blends this displayer's own frame, cover and difficulty badges into the center layout.</summary>
        /// <param name="progress">The selection blend from zero at the side layout to one at the center.</param>
        public void SetSelectedProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (_selectedProgress == progress)
            {
                return;
            }
            _selectedProgress = progress;

            var t = Mathf.SmoothStep(0f, 1f, progress);
            _visualProgress = t;
            ApplySelectedProgress(t);
        }
        /// <summary>Captures the authored side frame and cover rectangles before any selection interpolation.</summary>
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
        /// <summary>Applies a common eased blend to cover geometry, decorations and badge layout.</summary>
        /// <param name="progress">The eased selection blend.</param>
        void ApplySelectedProgress(float progress)
        {
            if (_backgroundTransform is not null)
            {
                _backgroundTransform.anchoredPosition = Vector2.Lerp(
                    _backgroundOriginPosition,
                    Vector2.zero,
                    progress);
                _backgroundTransform.sizeDelta = Vector2.Lerp(
                    _backgroundOriginSize,
                    Vector2.one * CenterBackgroundSize,
                    progress);
            }
            if (_coverTransform is not null)
            {
                _coverTransform.anchoredPosition = Vector2.Lerp(_coverOriginPosition, Vector2.zero, progress);
                _coverTransform.sizeDelta = Vector2.Lerp(
                    _coverOriginSize,
                    Vector2.one * CenterCoverSize,
                    progress);
            }
            var coverScale = _coverDisplayer.rectTransform.rect.width / CenterCoverSize;
            _levelRingDisplayer.rectTransform.sizeDelta = Vector2.one * (CenterRingSize * coverScale);
            var ringColor = _levelRingDisplayer.color;
            ringColor.a = progress;
            _levelRingDisplayer.color = ringColor;
            _levelRingDisplayer.enabled = progress > 0f;
            _selectedLevelGroup.alpha = progress;
            _selectedLevelGroup.transform.localScale = Vector3.one * coverScale;
            _selectedLevelGroup.gameObject.SetActive(progress > 0f);
            UpdateLevelLayout(progress);
        }
        /// <summary>Binds valid chart labels to the existing right-aligned side-layout slots.</summary>
        /// <param name="songDetail">The song whose nonempty levels occupy those slots.</param>
        void SetLevelText(ISongDetail songDetail)
        {
            var posIndex = _levelDPOriginPositions.Length - 1;
            var levels = songDetail.Levels;
            for (var i = _levelDisplayers.Length - 1; i >= 0; i--)
            {
                ref var displayer = ref _levelDisplayers[i];
                var level = i < levels.Length ? levels[i] : string.Empty;
                displayer.IsAvailable = !string.IsNullOrEmpty(level);
                if (!displayer.IsAvailable)
                {
                    displayer.Object.SetActive(false);
                    continue;
                }
                displayer.SidePose = _levelDPOriginPositions[posIndex--];
                displayer.TextDisplayer.text = level;
            }
        }

        /// <summary>Updates the selected difficulty's name and large level, including absent-chart dashes.</summary>
        private void UpdateSelectedLevelText()
        {
            _selectedLevelTitle.text = (ChartLevel)_selectedDifficulty switch
            {
                ChartLevel.Easy => "Easy",
                ChartLevel.Basic => "Basic",
                ChartLevel.Advance => "Advance",
                ChartLevel.Expert => "Expert",
                ChartLevel.Master => "Master",
                ChartLevel.ReMaster => "Re:Master",
                ChartLevel.UTAGE => "UTAGE",
                _ => string.Empty
            };
            var level = _songDetail != null && _selectedDifficulty < _songDetail.Levels.Length
                ? _songDetail.Levels[_selectedDifficulty] : string.Empty;
            _selectedLevelText.text = string.IsNullOrEmpty(level) ? "-" : level;
        }

        /// <summary>Interpolates the existing seven badges between their side slots and selected-cover arc.</summary>
        /// <param name="progress">The eased selection blend from side layout to centered layout.</param>
        private void UpdateLevelLayout(float progress)
        {
            if (_songDetail == null)
            {
                return;
            }
            var leftIndex = 0;
            for (var i = _selectedDifficulty - 1; i >= 0; i--)
            {
                if (_levelDisplayers[i].IsAvailable)
                {
                    ApplyLevelLayout(i, CenterLeftStartRotation + leftIndex * CenterRotationStep, progress);
                    leftIndex++;
                }
            }
            ApplyLevelLayout(_selectedDifficulty, 50f, progress);
            var rightIndex = 0;
            for (var i = _selectedDifficulty + 1; i < _levelDisplayers.Length; i++)
            {
                if (_levelDisplayers[i].IsAvailable)
                {
                    ApplyLevelLayout(i, CenterRightStartRotation - rightIndex * CenterRotationStep, progress);
                    rightIndex++;
                }
            }
        }

        /// <summary>Moves and fades one existing small badge without replacing its artwork or text object.</summary>
        /// <param name="index">The badge's difficulty index.</param>
        /// <param name="centerAngle">The original selected-cover arc angle in degrees.</param>
        /// <param name="progress">The eased selection blend.</param>
        private void ApplyLevelLayout(int index, float centerAngle, float progress)
        {
            ref var displayer = ref _levelDisplayers[index];
            if (!displayer.IsAvailable)
            {
                return;
            }
            var side = displayer.SidePose;
            displayer.Transform.pivot = Vector2.Lerp(side.Pivot, s_centerLevelPivot, progress);
            displayer.Transform.localPosition = Vector3.Lerp(side.Position, Vector3.zero, progress);
            displayer.Transform.localRotation = Quaternion.Slerp(side.Rotation, Quaternion.Euler(0f, 0f, centerAngle), progress);
            displayer.TextTransform.localRotation = Quaternion.Slerp(side.TextRotation, Quaternion.Euler(0f, 0f, -centerAngle), progress);
            var alpha = index == _selectedDifficulty ? 1f - progress : 1f;
            displayer.Group.alpha = alpha;
            displayer.Object.SetActive(alpha > 0f);
        }
        /// <summary>Publishes a loaded cover only while this pooled binding still owns the request.</summary>
        /// <param name="songDetail">The song whose compressed cover is requested.</param>
        /// <param name="loadDelayMS">The delay before requesting an uncached cover.</param>
        /// <param name="token">Cancels a replaced binding or destroyed displayer.</param>
        /// <returns>The asynchronous cover publication.</returns>
        /// <exception cref="OperationCanceledException">The binding is replaced or destroyed before publication.</exception>
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
            token.ThrowIfCancellationRequested();
            _coverDisplayer.sprite = cover;
            _loadingComponent.SetActive(false);
        }

        /// <summary>Retains one existing chart badge and its current packed side-layout pose.</summary>
        private struct LevelDisplayer
        {
            /// <summary>Gets the existing badge object.</summary>
            public required GameObject Object { get; init; }
            /// <summary>Gets the existing badge rectangle whose pivot defines its arc radius.</summary>
            public required RectTransform Transform { get; init; }
            /// <summary>Gets the authored opacity group used to fade this badge.</summary>
            public required CanvasGroup Group { get; init; }
            /// <summary>Gets the label transform counter-rotated within its badge.</summary>
            public required Transform TextTransform { get; init; }
            /// <summary>Gets the existing level label.</summary>
            public required TextMeshProUGUI TextDisplayer { get; init; }
            /// <summary>Gets or sets whether the current song provides this difficulty.</summary>
            public bool IsAvailable { get; set; }
            /// <summary>Gets or sets the current right-aligned slot's authored side pose.</summary>
            public LevelDPOriginPosition SidePose { get; set; }
        }
        /// <summary>Retains an authored side slot independently of subsequent selection interpolation.</summary>
        private readonly struct LevelDPOriginPosition
        {
            /// <summary>Gets the slot's local position.</summary>
            public Vector3 Position { get; init; }
            /// <summary>Gets the slot's local badge rotation.</summary>
            public Quaternion Rotation { get; init; }
            /// <summary>Gets the slot's radial pivot.</summary>
            public Vector2 Pivot { get; init; }
            /// <summary>Gets the slot's counter-rotated label orientation.</summary>
            public Quaternion TextRotation { get; init; }
        }
    }
}
