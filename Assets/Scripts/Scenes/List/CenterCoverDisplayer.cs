using Cysharp.Threading.Tasks;
using LitMotion;
using MajdataPlay.Drawing;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Threading;
using MajdataPlay.Utils;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.List
{
    /// <summary>Updates settled-song information, preview, favorites and the decorative background without owning foreground cover visuals.</summary>
    public class CenterCoverDisplayer : MajBehaviour
    {
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

        private int _diff = 0;

        private ISongDetail? _currentSongDetail = null;

        private GameObject? _favoriteRoot;
        private RectTransform _bgSongCoverTransform;

        private MotionHandle _bgSongCoverAnim;

        private CancellationTokenSource _cts = new();
        private CancellationTokenSource _ctsOnLevelChanged = new();

        private readonly ListConfig _listConfig = MajEnv.RuntimeConfig?.List ?? new();

        private const float BG_COVER_FADE_IN_DURATION_SEC = 0.3f;
        
        protected override void Awake()
        {
            base.Awake();

            SetDifficulty((int)_listConfig.SelectedDiff);

            _favoriteRoot = transform.Find("Favorite")?.gameObject;
            _bgSongCoverTransform = _bgSongCoverDisplayer.GetComponent<RectTransform>();
        }
        private void OnDestroy()
        {
            _cts.Cancel();
            _ctsOnLevelChanged.Cancel();
            _bgSongCoverAnim.TryCancel();
            _cts.Dispose();
            _ctsOnLevelChanged.Dispose();
        }
        private void OnDisable()
        {
            _cts.Cancel();
            _ctsOnLevelChanged.Cancel();
            _bgSongCoverAnim.TryCancel();
        }

        /// <summary>Updates the selected chart's information and cabinet lights without replacing cover graphics.</summary>
        /// <param name="i">The selected chart difficulty index.</param>
        public void SetDifficulty(int i)
        {
            _ctsOnLevelChanged.Cancel();
            _ctsOnLevelChanged.Dispose();
            _ctsOnLevelChanged = new();
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
            UpdateMetadataAndScoreDisplayer(false);
            
        }
        /// <summary>Updates selected-song services and reloads requests cancelled while the same song was scrolling.</summary>
        /// <param name="detail">The settled selected song.</param>
        /// <param name="loadDelayMS">The delay used for uncached cover and information requests.</param>
        public void SetSongDetail(ISongDetail detail, int loadDelayMS = 0)
        {
            if(detail == _currentSongDetail && !_cts.IsCancellationRequested)
            {
                return;
            }
            _cts.Cancel();
            _ctsOnLevelChanged.Cancel();
            _cts.Dispose();
            _ctsOnLevelChanged.Dispose();
            _cts = new();
            _ctsOnLevelChanged = new();
            _currentSongDetail = detail;
            
            UpdateMetadataAndScoreDisplayer(true, loadDelayMS);
            _previewPlayer.PlayPreviewSound(detail, 1000, _cts.Token);
            _favoriteAdder.SetSong(detail);
            ListManager.AllBackgroundTasks.Add(SetCoverAsync(detail, loadDelayMS, _cts.Token)
                                               .Register($"[{nameof(CenterCoverDisplayer)}]Load song cover"));
        }
        /// <summary>Controls settled-selection details without taking ownership of any foreground cover visuals.</summary>
        /// <param name="visible">Whether the settled song's favorite control and asynchronous details are visible.</param>
        public void SetSelectionDetailsVisible(bool visible)
        {
            if (!visible)
            {
                _onlineScoreRankDisplayer.Hide();
                _cts.Cancel();
                _ctsOnLevelChanged.Cancel();
                _bgSongCoverAnim.TryCancel();
            }
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
        /// <summary>Refreshes chart details for every selection change and reloads song-wide information only when needed.</summary>
        /// <param name="songChanged">Whether the selected song changed, requiring its online information to reload.</param>
        /// <param name="loadDelayMS">The delay before requesting uncached chart and song information.</param>
        private void UpdateMetadataAndScoreDisplayer(bool songChanged, int loadDelayMS = 0)
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

            // Selection cancellation also cancels the current level source. A difficulty
            // change may replace that source while the selection is already cancelled.
            var cancellationTokenOnLevelChanged = _cts.IsCancellationRequested
                ? cancellationToken : _ctsOnLevelChanged.Token;
            _metadataDisplayer.SetMetadataFromSongDetail(_currentSongDetail, (ChartLevel)_diff, loadDelayMS, cancellationTokenOnLevelChanged);
            _chartAnalyzer.SetSongDeatil(_currentSongDetail, (ChartLevel)_diff, 0, null, loadDelayMS, cancellationTokenOnLevelChanged);
            _majRadar.SetRadarFromSongDetail(_currentSongDetail, (ChartLevel)_diff, loadDelayMS, cancellationTokenOnLevelChanged);
            _onlineScoreRankDisplayer.SetSongDetail(_currentSongDetail, (ChartLevel)_diff, loadDelayMS, cancellationTokenOnLevelChanged);
            _scoreDisplayer.SetScore(_currentSongDetail, (ChartLevel)_diff);
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

        /// <summary>Loads only the decorative background half-cover without owning the pooled foreground cover.</summary>
        /// <param name="detail">The selected song whose cached cover supplies the background.</param>
        /// <param name="loadDelayMS">The delay before requesting an uncached compressed cover.</param>
        /// <param name="token">Cancels background loading when the selected song or scene changes.</param>
        /// <returns>The asynchronous background-image update.</returns>
        /// <exception cref="System.OperationCanceledException">The selected song or scene changes before publication.</exception>
        private async Task SetCoverAsync(ISongDetail detail, int loadDelayMS, CancellationToken token = default)
        {
            _bgSongCoverAnim.TryCancel();
            _bgSongCoverDisplayer.sprite = SpriteLoader.EmptySprite;
            if (!detail.IsCompressedCoverLoaded)
            {
                await Task.Delay(loadDelayMS, token);
            }
            var cover = await detail.GetCoverAsync(true, token: token);
            await UniTask.SwitchToMainThread();
            token.ThrowIfCancellationRequested();
            _bgSongCoverDisplayer.sprite = cover;
            _bgSongCoverAnim = LMotion.Create(0f, 1f, BG_COVER_FADE_IN_DURATION_SEC)
                                      .WithScheduler(MotionScheduler.PostLateUpdate)
                                      .WithEase(Ease.OutQuad)
                                      .Bind(x =>
                                      {
                                          UpdateBGSongCoverAnim(x);
                                      });
        }
    }
}
