using Cysharp.Threading.Tasks;
using LitMotion;
using MajdataPlay.Drawing;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Threading;
using MajdataPlay.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List
{
    public class CenterCoverDisplayer : MajBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("levelRingDisplayer")]
        private Image _levelRingDisplayer;
        [SerializeField]
        [FormerlySerializedAs("songCoverDisplayer")]
        private Image _songCoverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("loadingObj")]
        private GameObject _loadingObj;

        [SerializeField]
        [FormerlySerializedAs("scoreDisplayer")]
        private MaiScoreDisplayer _scoreDisplayer;

        [SerializeField]
        [FormerlySerializedAs("metadataDisplayer")]
        private ChartMetadataDisplayer _metadataDisplayer;

        [SerializeField]
        [FormerlySerializedAs("chartAnalyzer")]
        private ChartVisualDisplayer _chartAnalyzer;

        [SerializeField]
        [FormerlySerializedAs("majRadar")]
        private MajRadarDisplayer _majRadar;

        [SerializeField]
        [FormerlySerializedAs("onlineInfoDisplayer")]
        private OnlineInfoDisplayer _onlineInfoDisplayer;

        [SerializeField]
        [FormerlySerializedAs("onlineScoreRankDisplayer")]
        private OnlineScoreRankDisplayer _onlineScoreRankDisplayer;

        [SerializeField]
        [FormerlySerializedAs("previewPlayer")]
        private PreviewSoundPlayer _previewPlayer;

        [SerializeField]
        [FormerlySerializedAs("favoriteAdder")]
        private FavoriteAdder _favoriteAdder;

        [SerializeField]
        [FormerlySerializedAs("bgSongCoverDisplayer")]
        private Image _bgSongCoverDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelDisplayerListRoot")]
        private GameObject _levelDisplayerListRoot;

        [SerializeField]
        [FormerlySerializedAs("selectedLevelTitle")]
        private TextMeshProUGUI _selectedLevelTitle;

        [SerializeField]
        [FormerlySerializedAs("selectedLevelText")]
        private TextMeshProUGUI _selectedLevelText;

        [SerializeField]
        [FormerlySerializedAs("selectedLevelColor")]
        private Image _selectedLevelColor;

        private int _diff = 0;

        private ISongDetail? _currentSongDetail = null;

        private ListManager _listManager;
        private GameObject? _embeddedCoverRoot;
        private GameObject? _favoriteRoot;
        private RectTransform _bgSongCoverTransform;

        private MotionHandle _bgSongCoverAnim;

        private LevelBinding[] _levelBindings = Array.Empty<LevelBinding>();
        private LevelDisplayer[] _levelDisplayers = Array.Empty<LevelDisplayer>();

        private CancellationTokenSource _cts = new();
        private CancellationTokenSource _ctsOnLevelChanged = new();

        private readonly ListConfig _listConfig = MajEnv.RuntimeConfig?.List ?? new();

        private const float LEVEL_RING_LEFT_START_ROTATION = 67.5f;
        private const float LEVEL_RING_RIGHT_START_ROTATION = 32.5f;
        private const float LEVEL_RING_ROTATION_STEP = 10f;
        private const float BG_COVER_FADE_IN_DURATION_SEC = 0.3f;
        
        protected override void Awake()
        {
            base.Awake();

            var levelDisplayerListRoot = _levelDisplayerListRoot.transform;
            var displayerCount = levelDisplayerListRoot.childCount;
            _levelDisplayers = new LevelDisplayer[displayerCount];
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
            }
            var levelValues = (ChartLevel[])Enum.GetValues(typeof(ChartLevel));
            _levelBindings = new LevelBinding[levelValues.Length];
            for (var i = 0; i < levelValues.Length; i++)
            {
                _levelBindings[i] = new LevelBinding
                {
                    Level = levelValues[i],
                    IsLevelEmpty = true
                };
            }
            SetDifficulty((int)_listConfig.SelectedDiff);

            var coverTransform = _songCoverDisplayer.GetComponent<RectTransform>();
            _embeddedCoverRoot = coverTransform.parent?.gameObject;
            _favoriteRoot = transform.Find("Favorite")?.gameObject;
            _bgSongCoverTransform = _bgSongCoverDisplayer.GetComponent<RectTransform>();
        }
        private void Start()
        {
            _listManager = Majdata<ListManager>.Instance!;
        }
        private void OnDestroy()
        {
            _cts.Cancel();
            _ctsOnLevelChanged.Cancel();
        }
        private void OnDisable()
        {
            _cts.Cancel();
            _ctsOnLevelChanged.Cancel();
        }

        public void SetDifficulty(int i)
        {
            _ctsOnLevelChanged.Cancel();
            _ctsOnLevelChanged = new();
            _levelRingDisplayer.color = RuntimeDatabase.DifficultyColors[i];
            _selectedLevelColor.color = RuntimeDatabase.DifficultyColors[i];
            _diff = i;
            if (i + 1 < RuntimeDatabase.DifficultyColors.Length)
            {
                CabinetLed.SetButtonLight(RuntimeDatabase.DifficultyColors[i + 1], 0);
            }
            else
            {
                CabinetLed.SetButtonLight(RuntimeDatabase.DifficultyColors[0], 0);
            }
            if (i - 1 >= 0)
            {
                CabinetLed.SetButtonLight(RuntimeDatabase.DifficultyColors[i - 1], 7);
            }
            else
            {
                CabinetLed.SetButtonLight(RuntimeDatabase.DifficultyColors[6], 7);
            }
            UpdateLevelRing();
            UpdateMetadataAndScoreDisplayer(false, true);
            
        }
        public void SetSongDetail(ISongDetail detail, int loadDelayMS = 0, Sprite? immediateCover = null)
        {
            if(detail == _currentSongDetail)
            {
                return;
            }
            _cts.Cancel();
            _ctsOnLevelChanged.Cancel();
            _cts = new();
            _ctsOnLevelChanged = new();
            _currentSongDetail = detail;
            
            var chartLevels = detail.Levels;
            for (var i = 0; i < chartLevels.Length; i++)
            {
                var level = chartLevels[i];
                var displayer = _levelDisplayers[i];
                ref var binding = ref _levelBindings[i];
                if (string.IsNullOrEmpty(level))
                {
                    binding.IsLevelEmpty = true;
                    binding.Displayer = null;
                    binding.Value = null;
                    displayer.Object.SetActive(false);
                    continue;
                }
                binding.IsLevelEmpty = false;
                binding.Value = level;
                binding.Displayer = displayer;
                displayer.TextDisplayer.text = level;
                displayer.Object.SetActive(true);
            }
            UpdateLevelRing();
            UpdateMetadataAndScoreDisplayer(true, false, loadDelayMS);
            _previewPlayer.PlayPreviewSound(detail, 1000, _cts.Token);
            _favoriteAdder.SetSong(detail);
            ListManager.AllBackgroundTasks.Add(SetCoverAsync(detail, loadDelayMS, _cts.Token, immediateCover)
                                               .Register($"[{nameof(CenterCoverDisplayer)}]Load song cover"));
        }
        public void SetEmbeddedCoverVisible(bool visible)
        {
            if (!visible)
            {
                _onlineScoreRankDisplayer.Hide();
                _cts.Cancel();
                _ctsOnLevelChanged.Cancel();
            }
            _embeddedCoverRoot?.SetActive(visible);
            _favoriteRoot?.SetActive(visible);
        }
        public void SetActive(bool state)
        {
            gameObject.SetActive(state);
            if(!state)
            {
                _bgSongCoverDisplayer.sprite = SpriteLoader.EmptySprite;
            }
        }
        private void UpdateLevelRing()
        {
            var currentLevel = (ChartLevel)_diff;
            var currentLevelBinding = _levelBindings[_diff];
            var leftIndex = 0;
            var rightIndex = 0;

            _selectedLevelText.text = currentLevelBinding.Value ?? "-";
            switch(currentLevel)
            {
                case ChartLevel.Easy:
                    _selectedLevelTitle.text = "Easy";
                    break;
                case ChartLevel.Basic:
                    _selectedLevelTitle.text = "Basic";
                    break;
                case ChartLevel.Advance:
                    _selectedLevelTitle.text = "Advance";
                    break;
                case ChartLevel.Expert:
                    _selectedLevelTitle.text = "Expert";
                    break;
                case ChartLevel.Master:
                    _selectedLevelTitle.text = "Master";
                    break;
                case ChartLevel.ReMaster:
                    _selectedLevelTitle.text = "Re:Master";
                    break;
                case ChartLevel.UTAGE:
                    _selectedLevelTitle.text = "UTAGE";
                    break;
            }
            for (var i = _diff - 1; i >= 0; i--)
            {
                var binding = _levelBindings[i];
                if (binding.IsLevelEmpty)
                {
                    continue;
                }
                if(binding.Displayer is LevelDisplayer displayer)
                {
                    displayer.Transform.localRotation = Quaternion.Euler(0, 0, LEVEL_RING_LEFT_START_ROTATION + (leftIndex * LEVEL_RING_ROTATION_STEP));
                    displayer.TextTransform.localRotation = Quaternion.Euler(0, 0, -(LEVEL_RING_LEFT_START_ROTATION + (leftIndex * LEVEL_RING_ROTATION_STEP)));
                    leftIndex++;
                }
            }
            if (!currentLevelBinding.IsLevelEmpty)
            {
                if (currentLevelBinding.Displayer is LevelDisplayer displayer)
                {
                    displayer.Transform.localRotation = Quaternion.Euler(0, 0, 50);
                    displayer.TextTransform.localRotation = Quaternion.Euler(0, 0, -50);
                }
            }
            for (var i = _diff + 1; i < _levelBindings.Length; i++)
            {
                var binding = _levelBindings[i];
                if (binding.IsLevelEmpty)
                {
                    continue;
                }
                if (binding.Displayer is LevelDisplayer displayer)
                {
                    displayer.Transform.localRotation = Quaternion.Euler(0, 0, LEVEL_RING_RIGHT_START_ROTATION - (rightIndex * LEVEL_RING_ROTATION_STEP));
                    displayer.TextTransform.localRotation = Quaternion.Euler(0, 0, -(LEVEL_RING_RIGHT_START_ROTATION - (rightIndex * LEVEL_RING_ROTATION_STEP)));
                    rightIndex++;
                }
            }
        }
        private void UpdateMetadataAndScoreDisplayer(bool songChanged, bool levelChanged, int loadDelayMS = 0)
        {
            if(_currentSongDetail is null)
            {
                return;
            }
            var cancellationToken = _cts.Token;

            if(songChanged)
            {
                _onlineInfoDisplayer.SetSongDetail(_currentSongDetail, loadDelayMS, cancellationToken);
            }

            if(songChanged || levelChanged)
            {
                var cancellationTokenOnLevelChanged = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _ctsOnLevelChanged.Token)
                                                                             .Token;
                _metadataDisplayer.SetMetadataFromSongDetail(_currentSongDetail, (ChartLevel)_diff, loadDelayMS, cancellationTokenOnLevelChanged);
                _chartAnalyzer.SetSongDeatil(_currentSongDetail, (ChartLevel)_diff, null, loadDelayMS, cancellationTokenOnLevelChanged);
                _majRadar.SetRadarFromSongDetail(_currentSongDetail, (ChartLevel)_diff, loadDelayMS, cancellationTokenOnLevelChanged);
                _onlineScoreRankDisplayer.SetSongDetail(_currentSongDetail, (ChartLevel)_diff, loadDelayMS, cancellationTokenOnLevelChanged);
                _scoreDisplayer.SetScore(_currentSongDetail, (ChartLevel)_diff);
            }
        }
        private void UpdateBGSongCoverAnim(float progress)
        {
            const float MaxFadeInAlpha = 0.3f;
            const float CoverEndYPos = 0;
            const float CoverStartYPos = -100f;

            var nP = Vector3.Lerp(new Vector3(0, CoverStartYPos), new(0, CoverEndYPos), progress);
            var nAlpha = Mathf.Lerp(0, MaxFadeInAlpha, progress);

            _bgSongCoverDisplayer.color = new Color(1, 1, 1, nAlpha);
            _bgSongCoverTransform.anchoredPosition = nP;
        }
        
        private async Task SetCoverAsync(ISongDetail detail, int loadDelayMS, CancellationToken token = default, Sprite? immediateCover = null)
        {
            _bgSongCoverAnim.TryCancel();
            _bgSongCoverDisplayer.sprite = SpriteLoader.EmptySprite;
            if (immediateCover != null && immediateCover.rect.width > 0 && immediateCover.rect.height > 0)
            {
                _songCoverDisplayer.sprite = immediateCover;
                _loadingObj.SetActive(false);
            }
            else
            {
                _loadingObj.SetActive(true);
                _songCoverDisplayer.sprite = null!;
            }
            if (!detail.IsCompressedCoverLoaded)
            {
                await Task.Delay(loadDelayMS, token);
            }
            var cover = await detail.GetCoverAsync(true, token: token);
            await UniTask.SwitchToMainThread();
            token.ThrowIfCancellationRequested();
            _songCoverDisplayer.sprite = cover;
            _bgSongCoverDisplayer.sprite = cover;
            _bgSongCoverAnim = LMotion.Create(0f, 1f, BG_COVER_FADE_IN_DURATION_SEC)
                                      .WithScheduler(MotionScheduler.PostLateUpdate)
                                      .WithEase(Ease.OutQuad)
                                      .Bind(x =>
                                      {
                                          UpdateBGSongCoverAnim(x);
                                      });
            _loadingObj.SetActive(false);
        }

        private readonly struct LevelDisplayer
        {
            public required GameObject Object { get; init; }
            public required Transform Transform { get; init; }
            public required Transform TextTransform { get; init; }
            public required TextMeshProUGUI TextDisplayer { get; init; }
        }
        private struct LevelBinding
        {
            public required ChartLevel Level { get; init; }
            public bool IsLevelEmpty { get; set; }
            public string? Value { get; set; }
            public LevelDisplayer? Displayer { get; set; }
        }
    }
}
