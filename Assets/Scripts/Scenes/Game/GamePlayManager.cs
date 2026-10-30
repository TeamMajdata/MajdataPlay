using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajdataPlay.Numerics;
using MajdataPlay.Recording;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.List;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Timer;
using MajdataPlay.Utils;
using MajSimai;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MajdataPlay.Scenes.Game
{
#nullable enable
    public class GamePlayManager : MajBehaviour, INoteController
    {
        public float NoteSpeed { get; private set; } = 7f;
        public float TouchSpeed { get; private set; } = 7f;
        public bool IsClassicMode
        {
            get
            {
                return (_gameSettings?.Judge.Mode ?? JudgeModeOption.Modern) == JudgeModeOption.Classic;
            }
        }
        // Timeline
        /// <summary>
        /// The timing of the current Update<para>Unit: Second</para>
        /// </summary>
        public float ThisFrameSec => _thisFrameSec;
        /// <summary>
        ///  The first Note appear timing
        /// </summary>
        public float FirstNoteAppearTiming
        {
            get => _firstNoteAppearTiming;
            set => _firstNoteAppearTiming = value;
        }
        /// <summary>
        /// Current audio Total length
        /// </summary>
        public float AudioLength { get; private set; } = 0f;
        /// <summary>
        /// The timing of audio starting to play
        /// </summary>
        public float AudioStartTime => _audioStartTime;
        // Control
        public bool IsStart { get; private set; } = false;
        public bool IsAutoplay => AutoplayMode != AutoplayModeOption.Disable;
        public AutoplayModeOption AutoplayMode
        {
            get => ModInfo.AutoPlay;
        }

        public JudgeGrade AutoplayGrade { get; private set; } = JudgeGrade.Perfect;
        public GameModInfo ModInfo { get; private set; }
        public float PlaybackSpeed
        {
            get => ModInfo.PlaybackSpeed;
        }
        public GamePlayStatus State { get; private set; } = GamePlayStatus.Start;
        // Data
        public bool IsPracticeMode => _gameInfo.IsPracticeMode;
        internal GameMode Mode => _gameInfo.Mode;
        public MaiScore? HistoryScore { get; private set; }
        public Material BreakMaterial { get; private set; }
        public Material DefaultMaterial { get; private set; }
        public Material HoldShineMaterial { get; private set; }

        [SerializeField]
        private GameObject _allPerfectAnimation;
        [SerializeField]
        private GameObject _fullComboAnimation;
        [SerializeField]
        private GameObject _gameOverAnimation;

        [SerializeField]
        [FormerlySerializedAs("chartVisualDisplayer")]
        private ChartVisualDisplayer _chartVisualDisplayer;

        [SerializeField]
        [FormerlySerializedAs("timeDisplayer")]
        private TimeDisplayer _timeDisplayer;

        [SerializeField]
        [FormerlySerializedAs("chartInfoDisplayer")]
        private ChartInfoDisplayer _chartInfoDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelCircleDisplayer")]
        private Image _levelCircleDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelBGDisplayer")]
        private Image _levelBGDisplayer;

        [SerializeField]
        private Sprite _maskSpriteA;
        [SerializeField]
        private Sprite _maskSpriteB;
        [SerializeField]
        private Animator _bgInfoHeaderAnim;
        [SerializeField]
        private GameSetting _gameSettings;
        [SerializeField]
        private GameObject _skipBtn;
        [SerializeField]
        private SpriteMask _noteMask;
        [SerializeField]
        private RectTransform _mainDisplayer;
        [ReadOnlyField]
        [SerializeField]
        private float _thisFrameSec = 0f;
        [ReadOnlyField]
        [SerializeField]
        private float _thisFixedUpdateSec = 0f;
        [ReadOnlyField]
        [SerializeField]
        private float _firstNoteAppearTiming = 0f;
        [ReadOnlyField]
        [SerializeField]
        private float _audioStartTime = -114514;
        private int _chartRotation = 0;

        /// <summary>The controller that owns gameplay shortcut input and hold state.</summary>
        private FnController _fnController = null!;

        private Accurate _historyAccurate;

        private bool _isEnforceFastRetry = false;
        private bool _isManualStartGame = false;
        private float? _allNotesFinishedTiming = null;
        private EnforceGameFailureCondition _enforceGameFailureCondition = EnforceGameFailureCondition.Disabled;

        private float _devicePlaybackOffset = 0f;

        // Offset
        private float _chartOffset = 0f;
        /// <summary>
        /// Setting - Judge - AudioOffset
        /// </summary>
        private float _audioTimeOffsetSec = 0f;
        private float _displayOffsetSec = 0f;

        // From simai command &mv_seek
        private float _videoOffsetSec = 0f;
        // From simai command &mv_wait
        private float _videoWaitTimeSec = 0f;

        private Task _generateAnswerSFXTask = Task.CompletedTask;
        private TextMeshProUGUI _errText;
        private MajTimer _timer = MajTimeline.CreateTimer();
        private float _audioTrackStartAt = 0f;

        private GameInfo _gameInfo;

        private SimaiFile _simaiFile;
        private SimaiChart _chart;
        private ChartSetting _chartSetting;
        private ISongDetail _songDetail;

        private float _trackVolume = 1f;

        private GameplayScreenRotationAngleOption _screenRotationAngle = GameplayScreenRotationAngleOption.Zero;

        private AudioSampleWrap? _audioSample = null;

        private BGManager _bgManager;
        private NoteLoader _noteLoader;
        private NoteManager _noteManager;
        private NoteAudioManager _noteAudioManager;
        private NotePoolManager _notePoolManager;
        private NoteEffectPool _noteEffectPool;
        private ObjectCounter _objectCounter;
        private RecorderStatusDisplayer _recorderStateDisplayer;

        private readonly CancellationTokenSource _cts = new();
        private readonly ListConfig _listConfig = MajEnv.RuntimeConfig?.List ?? new();
        private readonly SceneSwitcher _sceneSwitcher = MajInstances.SceneSwitcher;

        private static readonly Utf16PreparedFormat<float, float, float> ERROR_TEXT_FORMAT = ZString.PrepareUtf16<float, float, float>("Audio {0:F4}\nVideo {1:F4}\n DEV {2:F4}");

        #region GameLoading

        protected override void Awake()
        {
            base.Awake();
            var gameplayCamera = Camera.main;
            if (gameplayCamera != null)
            {
                gameplayCamera.transparencySortMode = TransparencySortMode.CustomAxis;
                gameplayCamera.transparencySortAxis = Vector3.forward;
            }
            Majdata<GamePlayManager>.Instance = this;
            Majdata<INoteController>.Instance = this;
            Majdata<INoteTimeProvider>.Instance = this;
            _gameInfo = Majdata<GameInfo>.Instance!;
            _gameSettings = MajEnv.Settings;
            _enforceGameFailureCondition = _gameSettings.Game.EnforceGameFailure;
            _isEnforceFastRetry = (int)_enforceGameFailureCondition % 2 == 0;
            _isManualStartGame = _gameSettings.Game.ManualStartGame;
            DefaultMaterial = RuntimeDatabase.Note.DefaultMaterial;
            BreakMaterial = RuntimeDatabase.Note.BreakMaterial;
            HoldShineMaterial = RuntimeDatabase.Note.HoldShineMaterial;
            if (_gameInfo is null || _gameInfo.Current is null)
            {
                throw new ArgumentNullException(nameof(_gameInfo));
            }
            //print(MajInstances.GameManager.SelectedIndex);
            _screenRotationAngle = _gameSettings.Display.GameplayScreenRotationAngle;
            _songDetail = _gameInfo.Current;
            HistoryScore = ScoreManager.GetScore(_songDetail, _listConfig.SelectedDiff);
            if (HistoryScore is not null)
            {
                _historyAccurate = HistoryScore.Acc;
            }
            _timer = MajTimeline.CreateTimer();
            _chartSetting = _gameInfo.ChartSettings;
            var diffColor = RuntimeDatabase.DifficultyColors[(int)_gameInfo.CurrentLevel];
            _levelCircleDisplayer.color = diffColor;
            _levelBGDisplayer.color = diffColor;
            if (_gameSettings.Debug.OffsetUnit == OffsetUnitOption.Second)
            {
                _audioTimeOffsetSec = _gameSettings.Judge.AudioOffset;
                _audioTimeOffsetSec += _chartSetting.AudioOffset;
                _displayOffsetSec = _gameSettings.Debug.DisplayOffset;
            }
            else
            {
                _audioTimeOffsetSec = _gameSettings.Judge.AudioOffset * MajEnv.FRAME_LENGTH_SEC;
                _audioTimeOffsetSec += _chartSetting.AudioOffset * MajEnv.FRAME_LENGTH_SEC;
                _displayOffsetSec = _gameSettings.Debug.DisplayOffset * MajEnv.FRAME_LENGTH_SEC;
            }
            _trackVolume = (MajEnv.Settings.Audio.Volume.Track + _chartSetting.TrackVolumeOffset).Clamp(0, 2);
#if !UNITY_EDITOR && UNITY_STANDALONE
            if(_gameSettings.Debug.HideCursorInGame)
            {
                Cursor.visible = false;
            }
#endif
            LoadGameMod();
            if (_gameInfo.IsDanMode)
            {
                LoadDanModSettings();
            }
            if (InputManager.IsTouchPanelConnected)
            {
                Destroy(GameObject.Find("EventSystem"));
            }
#if UNITY_ANDROID || UNITY_IOS
            InputManager.UseOuterTouchAsSensor = _gameSettings.Game.ButtonRingForTouch;
            InputManager.UseGameplayTouchEnhancementFeatures = true;
#endif
            InputManager.TouchButtonRingEdge = 5.4f;
            InputManager.Override_TouchSimulationRadius = default;
            InputManager.Override_TouchAAreaExtraRadius = default;
            InputManager.Override_TouchBAreaExtraRadius = default;
            InputManager.Override_TouchCAreaExtraRadius = default;
            InputManager.Override_TouchDAreaExtraRadius = default;
            InputManager.Override_TouchEAreaExtraRadius = default;

            MajInstances.BackgroundVideo.Hide();

            _chartInfoDisplayer.SetMetadata(_songDetail, _gameInfo.CurrentLevel);
            _fnController = GetComponent<FnController>();
            _fnController.Init(this);
        }
        private void Start()
        {
            _noteManager = Majdata<NoteManager>.Instance!;
            _bgManager = Majdata<BGManager>.Instance!;
            _objectCounter = Majdata<ObjectCounter>.Instance!;
            _noteAudioManager = Majdata<NoteAudioManager>.Instance!;
            _notePoolManager = Majdata<NotePoolManager>.Instance!;
            _noteEffectPool = Majdata<NoteEffectPool>.Instance!;
            _timeDisplayer = Majdata<TimeDisplayer>.Instance!;
            _noteLoader = Majdata<NoteLoader>.Instance!;
            _recorderStateDisplayer = Majdata<RecorderStatusDisplayer>.Instance!;

            _errText = GameObject.Find("ErrText").GetComponent<TextMeshProUGUI>();
            _chartRotation = _gameSettings.Game.Rotation.Clamp(-7, 7);

            switch (_screenRotationAngle)
            {
                case GameplayScreenRotationAngleOption._90:
                    _mainDisplayer.rotation = Quaternion.Euler(0, 0, -90);
                    break;
                case GameplayScreenRotationAngleOption._180:
                    _mainDisplayer.rotation = Quaternion.Euler(0, 0, -180);
                    break;
                case GameplayScreenRotationAngleOption._270:
                    _mainDisplayer.rotation = Quaternion.Euler(0, 0, -270);
                    break;
            }

            InitGame().Forget();
            return;
        }
        private void LoadDanModSettings()
        {
            var danInfo = _gameInfo.DanInfo;
            var playbackSpeed = ModInfo.PlaybackSpeed;
            var isAllBreak = ModInfo.AllBreak;
            var isAllEx = ModInfo.AllEx;
            var isAllTouch = ModInfo.AllTouch;
            var isUseButtonRingForTouch = ModInfo.ButtonRingForTouch;
            var isSlideNoHead = ModInfo.SlideNoHead;
            var isSlideNoTrack = ModInfo.SlideNoTrack;
            var autoplayMode = ModInfo.AutoPlay;
            var judgeStyle = ModInfo.JudgeStyle;
            var subdivideSlideJudgeGrade = ModInfo.SubdivideSlideJudgeGrade;
            var noteMask = ModInfo.NoteMask;
            foreach (var (k, v) in danInfo!.Mods)
            {
                switch (k)
                {
                    case "PlaybackSpeed":
                        {
                            if (v.Type == JTokenType.Float || v.Type == JTokenType.Integer)
                            {
                                playbackSpeed = v.ToObject<float>();
                            }
                            else if (float.TryParse(v.ToString(), out var playbackSpeed1))
                            {
                                playbackSpeed = playbackSpeed1;
                            }
                        }
                        break;
                    case "AllBreak":
                    case "AllEx":
                    case "AllTouch":
                    case "ButtonRingForTouch":
                    case "IsSlideNoHead":
                    case "IsSlideNoTrack":
                    case "SubdivideSlideJudgeGrade":
                        {
                            if (v.Type == JTokenType.Boolean)
                            {
                                bool value = v.ToObject<bool>();
                                switch (k)
                                {
                                    case "AllBreak":
                                        isAllBreak = value;
                                        break;
                                    case "AllEx":
                                        isAllEx = value;
                                        break;
                                    case "AllTouch":
                                        isAllTouch = value;
                                        break;
                                    case "ButtonRingForTouch":
                                        isUseButtonRingForTouch = value;
                                        break;
                                    case "IsSlideNoHead":
                                        isSlideNoHead = value;
                                        break;
                                    case "IsSlideNoTrack":
                                        isSlideNoTrack = value;
                                        break;
                                    case "SubdivideSlideJudgeGrade":
                                        subdivideSlideJudgeGrade = value;
                                        break;
                                }
                            }
                            else if (bool.TryParse(v.ToString(), out var boolValue))
                            {
                                switch (k)
                                {
                                    case "AllBreak":
                                        isAllBreak = boolValue;
                                        break;
                                    case "AllEx":
                                        isAllEx = boolValue;
                                        break;
                                    case "AllTouch":
                                        isAllTouch = boolValue;
                                        break;
                                    case "ButtonRingForTouch":
                                        isUseButtonRingForTouch = boolValue;
                                        break;
                                    case "IsSlideNoHead":
                                        isSlideNoHead = boolValue;
                                        break;
                                    case "IsSlideNoTrack":
                                        isSlideNoTrack = boolValue;
                                        break;
                                    case "SubdivideSlideJudgeGrade":
                                        subdivideSlideJudgeGrade = boolValue;
                                        break;
                                }
                            }
                        }
                        break;
                    case "AutoPlay":
                        {
                            if (v.Type == JTokenType.Integer)
                            {
                                autoplayMode = (AutoplayModeOption)v.ToObject<int>();
                            }
                            else if (Enum.TryParse<AutoplayModeOption>(v.ToString(), out var autoplayMode1))
                            {
                                autoplayMode = autoplayMode1;
                            }
                        }
                        break;
                    case "JudgeStyle":
                        {
                            if (v.Type == JTokenType.Integer)
                            {
                                judgeStyle = (JudgeStyleOption)v.ToObject<int>();
                            }
                            else if (Enum.TryParse<JudgeStyleOption>(v.ToString(), out var judgeStyle1))
                            {
                                judgeStyle = judgeStyle1;
                            }
                        }
                        break;
                    case "NoteMask":
                        noteMask = v.ToString();
                        break;
                }
            }
            ModInfo = new(ModInfo)
            {
                PlaybackSpeed = playbackSpeed,
                AllBreak = isAllBreak,
                AllEx = isAllEx,
                AllTouch = isAllTouch,
                ButtonRingForTouch = isUseButtonRingForTouch,
                SlideNoHead = isSlideNoHead,
                SlideNoTrack = isSlideNoTrack,
                AutoPlay = autoplayMode,
                JudgeStyle = judgeStyle,
                NoteMask = noteMask,
                SubdivideSlideJudgeGrade = subdivideSlideJudgeGrade
            };
        }
        private void LoadGameMod()
        {
            var modsetting = MajInstances.GameManager.Settings.Mod;
            ModInfo = modsetting;
            //AutoplayParam = mod5.Value ?? 7;
        }
        /// <summary>
        /// Parse the chart and load it into memory, or dump it locally if the chart is online
        /// </summary>
        /// <returns></returns>
        private async UniTaskVoid InitGame()
        {
            State = GamePlayStatus.Loading;
            var token = _cts.Token;
            try
            {
                if (_songDetail.IsOnline)
                {
                    var progress = new NetProgress();
                    var lastPercent = 0f;
                    _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING".i18n()}...");
                    _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_AUDIO_TRACK".i18n()}...");
                    var task1 = _songDetail.GetAudioTrackAsync(progress, token: _cts.Token);
                    while (!task1.IsCompleted)
                    {
                        await UniTask.Yield(cancellationToken: token);
                        var percent = progress.Percent.Clamp(0, 1);
                        LedRingLoadingUpdate(percent, lastPercent);
                        lastPercent = percent;
                        _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_AUDIO_TRACK".i18n()}...\n{percent * 100:F2}%");
                    }
                    lastPercent = 0;
                    progress.Reset();
                    token.ThrowIfCancellationRequested();
                    _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_MAIDATA".i18n()}...");
                    var task2 = _songDetail.GetMaidataAsync(false, progress, token: _cts.Token);
                    while (!task2.IsCompleted)
                    {
                        await UniTask.Yield(cancellationToken: token);
                        var percent = progress.Percent.Clamp(0, 1);
                        LedRingLoadingUpdate(percent, lastPercent);
                        lastPercent = percent;
                        _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_MAIDATA".i18n()}...\n{percent * 100:F2}%");
                    }
                    lastPercent = 0;
                    progress.Reset();
                    token.ThrowIfCancellationRequested();
                    _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_PICTURE".i18n()}...");
                    var task3 = _songDetail.GetCoverAsync(false, progress, token: _cts.Token);
                    while (!task3.IsCompleted)
                    {
                        await UniTask.Yield(cancellationToken: token);
                        var percent = progress.Percent.Clamp(0, 1);
                        LedRingLoadingUpdate(percent, lastPercent);
                        lastPercent = percent;
                        _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_PICTURE".i18n()}...\n{percent * 100:F2}%");
                    }
                    if (!(_chartSetting.DisableVideoBG || _gameSettings.Display.SkipVideoDownload))
                    {
                        lastPercent = 0;
                        progress.Reset();
                        token.ThrowIfCancellationRequested();
                        _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_VIDEO".i18n()}...");
                        var task4 = _songDetail.GetVideoPathAsync(progress, token: _cts.Token);
                        while (!task4.IsCompleted)
                        {
                            await UniTask.Yield(cancellationToken: token);
                            var percent = progress.Percent.Clamp(0, 1);
                            LedRingLoadingUpdate(percent, lastPercent);
                            lastPercent = percent;
                            _sceneSwitcher.SetLoadingText($"{"MAJTEXT_DOWNLOADING_VIDEO".i18n()}...\n{percent * 100:F2}%");
                        }
                    }
                    _sceneSwitcher.SetLoadingText(string.Empty);
                }

                _chartInfoDisplayer.SetCover(await _songDetail.GetCoverAsync(false));
                await LoadAudioTrack();
                token.ThrowIfCancellationRequested();
                await InitBackground();
                token.ThrowIfCancellationRequested();
                await ParseChart();
                token.ThrowIfCancellationRequested();
                await LoadNotes();
                token.ThrowIfCancellationRequested();
                await PrepareToPlay();
            }
            catch (EmptyChartException)
            {
                await UniTask.SwitchToMainThread();
                InputManager.ClearAllSubscriber();
                MajInstances.SceneSwitcher.SetLoadingText("MAJTEXT_ERR_EMPTY_CHART".i18n(), Color.red);
                await UniTask.Delay(1000);
                ReturnTo().Forget();
            }
            catch (OBSRecorderException)
            {
                await UniTask.SwitchToMainThread();
                InputManager.ClearAllSubscriber();
                MajInstances.SceneSwitcher.SetLoadingText("MAJTEXT_ERR_OBSERROR".i18n(), Color.red);
                await UniTask.Delay(1000);
                ReturnTo().Forget();
            }
            catch (InvalidSimaiMarkupException syntaxE)
            {
                await UniTask.SwitchToMainThread();
                MajInstances.SceneSwitcher.SetLoadingText($"{"Invalid syntax".i18n()}\n(at L{syntaxE.Line}:C{syntaxE.Column}) \"{syntaxE.Content}\"\n{syntaxE.Message}", Color.red);
                MajDebug.LogError(syntaxE);
                return;
            }
            catch (HttpException httpEx)
            {
                await UniTask.SwitchToMainThread();
                MajInstances.SceneSwitcher.SetLoadingText("MAJTEXT_ERR_DOWNLOAD_FAILED".i18n(), Color.red);
                MajDebug.LogError(httpEx);
                return;
            }
            catch (InvalidAudioTrackException audioEx)
            {
                await UniTask.SwitchToMainThread();
                MajInstances.SceneSwitcher.SetLoadingText($"{"MAJTEXT_ERR_LOAD_CHART_FAILED".i18n()}\n{audioEx.Message}", Color.red);
                MajDebug.LogError(audioEx);
                return;
            }
            catch (TaskCanceledException e)
            {
                MajDebug.LogWarning(e);
                return;
            }
            catch (OperationCanceledException canceledEx)
            {
                MajDebug.LogWarning(canceledEx);
                return;
            }
            catch (Exception e)
            {
                await UniTask.SwitchToMainThread();
                MajInstances.SceneSwitcher.SetLoadingText($"{"MAJTEXT_ERR_UNKNOWN".i18n()}\n{e.Message}", Color.red);
                MajDebug.LogError(e);
                throw;
            }
        }
        private void LedRingLoadingUpdate(float percent, float lastPercent)
        {
            var progress = (int)(8 * percent);
            var lastProgress = (int)(8 * lastPercent);
            if (progress == lastProgress)
            {
                return;
            }
            CabinetLed.SetAllLight(Color.black);
            if (progress == 0)
            {
                CabinetLed.SetSineFunc(0, Color.green, 1000);
                return;
            }
            else if (progress == 8)
            {
                CabinetLed.SetAllLight(Color.green);
                return;
            }
            for (var i = 0; i < progress; i++)
            {
                CabinetLed.SetButtonLight(Color.green, i);
            }
            CabinetLed.SetSineFunc(progress, Color.green, 1000);
        }


        private async UniTask LoadAudioTrack()
        {
            var audioSample = await _songDetail.GetAudioTrackAsync();
            if (audioSample is null || audioSample.IsEmpty)
            {
                throw new InvalidAudioTrackException("Failed to decode audio track", string.Empty);
            }
            _audioSample = audioSample;
            _audioSample.SetVolume(_trackVolume);
            _audioSample.Speed = PlaybackSpeed;
            _audioSample.IsLoop = false;
            _audioSample.CurrentSec = 0;
            if (IsPracticeMode)
            {
                if (_gameInfo.TimeRange is Range<double> timeRange)
                {
                    var playbackSpeed = PlaybackSpeed;
                    var startAt = timeRange.Start;
                    var endAt = timeRange.End;
                    startAt = Math.Max(startAt - 3, 0) / playbackSpeed;
                    endAt = Math.Min(endAt, _audioSample.Length.TotalSeconds) / playbackSpeed;

                    if (startAt >= endAt)
                    {
                        //throw a exception
                    }

                    _audioTrackStartAt = (float)startAt;
                }
            }
            AudioLength = (float)_audioSample.Length.TotalSeconds / MajEnv.Settings.Mod.PlaybackSpeed;
        }
        /// <summary>
        /// Parse the chart into memory
        /// </summary>
        /// <returns></returns>
        /// <exception cref="TaskCanceledException"></exception>
        private async UniTask ParseChart()
        {
            void ChartMirror(ref string chartContent)
            {
                var mirrorType = _gameSettings.Game.Mirror;
                if (mirrorType is MirrorOption.Off)
                {
                    return;
                }

                chartContent = SimaiMirror.NoteMirrorHandle(chartContent, mirrorType);
            }
            MajInstances.SceneSwitcher.SetLoadingText($"{"MAJTEXT_DESERIALIZATION".i18n()}...");

            _simaiFile = await _songDetail.GetMaidataAsync(true);
            _chartOffset = _simaiFile.Offset;
            var levelIndex = (int)_gameInfo.CurrentLevel;
            var maidata = _simaiFile.Charts[levelIndex].Fumen;

            if (string.IsNullOrEmpty(maidata))
            {
                throw new EmptyChartException();
            }

            ChartMirror(ref maidata);
            _chart = await SimaiParser.ParseChartAsync(_songDetail.Levels[levelIndex], _songDetail.Designers[levelIndex], maidata);
            var mvSeekCmd = _simaiFile.Commands.FirstOrDefault(x => x.Prefix == "mv_seek");
            var mvWaitCmd = _simaiFile.Commands.FirstOrDefault(x => x.Prefix == "mv_wait");
            if (float.TryParse(mvSeekCmd.Value, out var offsetSec))
            {
                if (offsetSec < 0)
                {
                    MajDebug.LogWarning($"Invalid \"&mv_seek\" value: {offsetSec}. Value must be non-negative. Ignored.");
                }
                else
                {
                    _videoOffsetSec = offsetSec;
                }
            }
            else
            {
                MajDebug.LogWarning($"Failed to parse \"&mv_seek\" value: {mvSeekCmd.Value}");
            }
            if (float.TryParse(mvWaitCmd.Value, out var waitTimeSec))
            {
                if (waitTimeSec < 0)
                {
                    MajDebug.LogWarning($"Invalid \"&mv_wait\" value: {waitTimeSec}. Value must be non-negative. Ignored.");
                }
                else
                {
                    _videoWaitTimeSec = waitTimeSec;
                }
            }
            else
            {
                MajDebug.LogWarning($"Failed to parse \"&mv_wait\" value: {mvSeekCmd.Value}");
            }
            if (IsPracticeMode)
            {
                if (_gameInfo.TimeRange is Range<double> timeRange)
                {
                    var range = new Range<double>(timeRange.Start - _simaiFile.Offset, timeRange.End - _simaiFile.Offset);
                    _chart = _chart.Clamp(range);
                }
                //else if (_gameInfo.ComboRange is Range<long> comboRange)
                //{
                //    _chart = _chart.Clamp(comboRange);
                //    if (_chart.NoteTimings.Length != 0)
                //    {
                //        var startAt = _chart.NoteTimings[0].Timing;
                //        startAt = Math.Max(startAt - 3, 0);

                //        _audioTrackStartAt = (float)startAt;
                //    }
                //}
            }
            _chart = _chart.AddOffset(_chartOffset + _audioTimeOffsetSec);
            if (ModInfo.PlaybackSpeed != 1)
            {
                _chart = _chart.Scale(PlaybackSpeed);
            }
            if (ModInfo.AllBreak)
            {
                _chart = _chart.ConvertToBreak();
            }
            if (ModInfo.AllEx)
            {
                _chart = _chart.ConvertToEx();
            }
            if (ModInfo.AllTouch)
            {
                _chart = _chart.ConvertToTouch();
            }
            if (_chart.IsEmpty)
            {
                throw new EmptyChartException();
            }
            _chart = _chart.AddOffset(-_displayOffsetSec);
            await UniTask.SwitchToMainThread();
            _chartVisualDisplayer.SetSimaiChart(_chart, _audioTimeOffsetSec, AudioLength);
            await UniTask.SwitchToThreadPool();
            var simaiCmd = _simaiFile.Commands.FirstOrDefault(x => x.Prefix == "clock_count");
            var countnum = 4;
            if (!int.TryParse(simaiCmd.Value, out countnum))
            {
                countnum = 4;
            }
            _generateAnswerSFXTask = _noteAudioManager.GenerateAnswerSFX(_chart, IsPracticeMode, countnum);
        }

        /// <summary>
        /// Load the background picture and set brightness
        /// </summary>
        /// <returns></returns>
        private async UniTask InitBackground()
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

            var dim = _gameSettings.Game.BackgroundDim;

            if (dim < 1f)
            {
                var isNeed2DownloadVideo = _songDetail.IsOnline && !_songDetail.IsVideoLoaded;
                var cover = await _songDetail.GetCoverAsync(false);
                await UniTask.SwitchToMainThread();
                if (_chartSetting.DisableVideoBG || (_gameSettings.Display.SkipVideoDownload && isNeed2DownloadVideo))
                {
                    _bgManager.SetBackgroundPic(cover);
                }
                else
                {
                    var videoPath = await _songDetail.GetVideoPathAsync();
                    await UniTask.SwitchToMainThread();
                    if (string.IsNullOrEmpty(videoPath))
                    {
                        _bgManager.SetBackgroundPic(cover);
                    }
                    else
                    {
                        await _bgManager.SetMovieAsync(videoPath, cover);
                        await UniTask.SwitchToMainThread();
                    }
                }
            }

            _bgManager.SetBrightness(0f);
        }
        /// <summary>
        /// Parse and load notes into NotePool
        /// </summary>
        /// <returns></returns>
        private async UniTask LoadNotes()
        {
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

            var tapSpeed = Math.Abs(_gameSettings.Game.TapSpeed);

            if (_gameSettings.Game.TapSpeed < 0)
            {
                _noteLoader.NoteSpeed = -((float)(107.25 / (71.4184491 * Mathf.Pow(tapSpeed + 0.9975f, -0.985558604f))));
            }
            else
            {
                _noteLoader.NoteSpeed = ((float)(107.25 / (71.4184491 * Mathf.Pow(tapSpeed + 0.9975f, -0.985558604f))));
            }
            _noteLoader.TouchSpeed = _gameSettings.Game.TouchSpeed;
            _noteLoader.SlidePathStyle = _gameSettings.Display.SlidePathStyle;
            _noteLoader.ChartRotation = _chartRotation + (2 * (int)_screenRotationAngle);

            //var loaderTask = noteLoader.LoadNotes(Chart);
            var loaderTask = _noteLoader.LoadNotesIntoPoolAsync(_chart, _cts.Token);
            var lastPercent = 0f;
            while (!loaderTask.Status.IsCompleted())
            {
                var percent = (float)_noteLoader.Progress.Clamp(0, 1);
                MajInstances.SceneSwitcher.SetLoadingText($"{"MAJTEXT_LOADING_CHART".i18n()}...\n{_noteLoader.Progress * 100:F2}%");
                LedRingLoadingUpdate(percent, lastPercent);
                lastPercent = percent;
                await UniTask.Yield();
            }
            if (loaderTask.Status.IsCanceled())
            {
                CabinetLed.SetAllLight(Color.white);
                return;
            }
            else if (loaderTask.Status.IsFaulted())
            {
                var task = loaderTask.AsTask();
                var e = task.Exception.InnerException;

                MajInstances.SceneSwitcher.SetLoadingText($"{"MAJTEXT_ERR_LOAD_CHART_FAILED".i18n()}\n{e.Message}%", Color.red);
                CabinetLed.SetAllLight(Color.red);
                MajDebug.LogException(task.Exception);
                StopAllCoroutines();
                throw e;
            }
            MajInstances.SceneSwitcher.SetLoadingText($"{"MAJTEXT_LOADING_CHART".i18n()}...\n100.00%");

            _noteEffectPool.Init();
            await UniTask.Yield();
        }
        private async UniTask PrepareToPlay()
        {
            if (_audioSample is null)
            {
                return;
            }
            CabinetLed.SetAllLight(Color.white);
            await UniTask.SwitchToMainThread();
            switch (ModInfo.NoteMask)
            {
                case "Inner":
                    _noteMask.gameObject.SetActive(true);
                    _noteMask.sprite = _maskSpriteB;
                    break;
                case "Outer":
                    _noteMask.gameObject.SetActive(true);
                    _noteMask.sprite = _maskSpriteA;
                    break;
                case "Disable":
                    _noteMask.gameObject.SetActive(false);
                    break;
            }
            var token = _cts.Token;
            const float BG_FADE_IN_LENGTH_SEC = 0.25f;
            Time.timeScale = 1f;
            var firstClockTiming = _noteAudioManager.FirstClockTiming;
            var leadInTime = _gameSettings.Game.LeadInTime.Clamp(1, 5);
            float extraTime = leadInTime;
            if (firstClockTiming < 0f)
            {
                extraTime = (-(float)firstClockTiming) + leadInTime;
            }
            if (FirstNoteAppearTiming < 0f)
            {
                extraTime = MathF.Min(extraTime, (-FirstNoteAppearTiming + leadInTime));
            }

            await _noteManager.InitAsync();
            while (!_generateAnswerSFXTask.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await UniTask.Yield();
            }
            var allBackgroundTasks = ListManager.WaitForBackgroundTaskSuspendAsync();
            await UniTask.SwitchToMainThread();
            var isAwaited = !allBackgroundTasks.IsCompleted;
            if (!allBackgroundTasks.IsCompleted)
            {
                _sceneSwitcher.SetLoadingText($"{"MAJTEXT_WAITING_FOR_BACKGROUND_TASKS_SUSPEND".i18n()}...");
            }
            while (!allBackgroundTasks.IsCompleted)
            {
                await UniTask.Yield(token);
            }
            if (isAwaited)
            {
                GC.Collect();
                await UniTask.Delay(2000, true, cancellationToken: token);
            }
            token.ThrowIfCancellationRequested();
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

            var wait4Recorder = Recording.Recorder.StartRecordAsync($"{_songDetail.Title}_{_songDetail.Designers[(int)_gameInfo.CurrentLevel]}");
            while (!wait4Recorder.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await UniTask.SwitchToMainThread();
                _sceneSwitcher.SetLoadingText($"{"Waiting for recorder".i18n()}...");
                await UniTask.Yield();
            }
            token.ThrowIfCancellationRequested();
            if (wait4Recorder.IsFaulted)
            {
                throw wait4Recorder.Exception.GetBaseException();
            }
            await UniTask.SwitchToMainThread();
            _sceneSwitcher.SetLoadingText("Loading...");
            MajInstances.GameManager.DisableGC();

            await UniTask.Delay(1000, cancellationToken: token);
            if (_isManualStartGame)
            {
                _sceneSwitcher.SetLoadingText($"{"MAJTEXT_GAME_PRESS_4TH_BUTTON_TO_CONTINUE".i18n()}...");
                do
                {
                    // Read the shared scene input after GameUpdater has sampled this frame.
                    await UniTask.Yield(PlayerLoopTiming.LastUpdate, token);
                }
                while (!(InputManager.IsButtonPressedInThisFrame(ButtonZone.A4) ||
                         InputManager.IsSensorPressedInThisFrame(SensorArea.A4)));
                _sceneSwitcher.SetLoadingText("Loading...");
                await UniTask.Yield(token);
            }
            _sceneSwitcher.SetLoadingText(string.Empty);

            await MajInstances.SceneSwitcher.FadeOutAsync(); //wait the animation

            _audioStartTime = (float)(_timer.ElapsedSecondsAsFloat + _audioSample.CurrentSec) + extraTime;
            _thisFrameSec = -extraTime;
            _thisFixedUpdateSec = _thisFrameSec;

            State = GamePlayStatus.Running;
            IsStart = true;
            var startSec = _audioTrackStartAt * PlaybackSpeed;
            if (!IsPracticeMode)
            {
                var userSettingBGDim = _gameSettings.Game.BackgroundDim;
                var dimDiff = 1 - userSettingBGDim;
                var bgFadeStartTiming = MathF.Min(firstClockTiming, -BG_FADE_IN_LENGTH_SEC);
                while (_timer.ElapsedSecondsAsFloat - _audioStartTime < 0)
                {
                    var timeDiff = _timer.ElapsedSecondsAsFloat - _audioStartTime;
                    if (timeDiff > bgFadeStartTiming)
                    {
                        var fadeProgress = ((timeDiff - bgFadeStartTiming) / BG_FADE_IN_LENGTH_SEC).Clamp(0f, 1f);
                        var dim = fadeProgress * dimDiff;
                        _bgManager.SetBrightness(dim);
                    }
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                    token.ThrowIfCancellationRequested();
                }
            }
            UniTask.Void(async () =>
            {
                MajDebug.LogDebug($"Waiting for the video to be ready to play\nVideo seek: {_videoOffsetSec}s\nVideo wait time: {_videoWaitTimeSec}s");
                var videoStartTime = startSec + (_videoWaitTimeSec * PlaybackSpeed);
                while (_timer.ElapsedSecondsAsFloat < _videoWaitTimeSec)
                {
                    token.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                }
                token.ThrowIfCancellationRequested();
                var videoStartAt = startSec + (_videoOffsetSec * PlaybackSpeed);
                MajDebug.LogDebug($"Start playing video at {videoStartAt}s");
                _bgManager.PlayVideo(videoStartAt, PlaybackSpeed);
                MajDebug.LogDebug("Video wait loop exited");
            });
            _bgManager.SetBrightness(1 - _gameSettings.Game.BackgroundDim);
            _audioSample.Play();
            _audioSample.Volume = 0;
            _audioSample.CurrentSec = startSec;

            _audioStartTime = _timer.ElapsedSecondsAsFloat - _audioTrackStartAt;
            MajDebug.LogInfo($"Chart playback speed: {PlaybackSpeed}x");
            _bgInfoHeaderAnim.SetTrigger("fadeIn");
            if (IsPracticeMode)
            {
                var elapsedSeconds = 0f;
                var originVol = _trackVolume;

                BgHeaderFadeOut();
                try
                {
                    while (elapsedSeconds < 3)
                    {
                        token.ThrowIfCancellationRequested();
                        _audioSample.Volume = elapsedSeconds / 3f * originVol;
                        await UniTask.Yield();
                        elapsedSeconds += MajTimeline.DeltaTime;
                    }
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                }
            }
            else
            {
                token.ThrowIfCancellationRequested();
                _audioSample.Volume = _trackVolume;
                await UniTask.Delay(3000);
                token.ThrowIfCancellationRequested();
                BgHeaderFadeOut();
            }
        }
        private void BgHeaderFadeOut()
        {
            if (_gameInfo.IsDanMode)
            {
                return;
            }
            switch (MajEnv.Settings.Game.BGInfo)
            {
                case BGInfoOption.Achievement_101:
                case BGInfoOption.Achievement_100:
                case BGInfoOption.Achievement:
                case BGInfoOption.AchievementClassical:
                case BGInfoOption.AchievementClassical_100:
                case BGInfoOption.S_Border:
                case BGInfoOption.SS_Border:
                case BGInfoOption.SSS_Border:
                case BGInfoOption.MyBest:
                case BGInfoOption.DXScore:
                    _bgInfoHeaderAnim.SetTrigger("fadeOut");
                    break;
                case BGInfoOption.CPCombo:
                case BGInfoOption.PCombo:
                case BGInfoOption.Combo:
                case BGInfoOption.DXScoreRank:
                case BGInfoOption.Diff:
                    break;
                default:
                    return;
            }
        }

        #endregion

        #region GameUpdate
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("GamePlayManager.OnPreUpdate"))
            {
                AudioTimeUpdate();
                ComponentPreUpdate();
            }
        }
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("GamePlayManager.OnUpdate"))
            {
                NoteManagerUpdate();
                _fnController.OnUpdate();
            }
        }
        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create("GamePlayManager.OnLateUpdate"))
            {
                switch (State)
                {
                    case GamePlayStatus.WaitForEnd:
                    case GamePlayStatus.Blocking:
                    case GamePlayStatus.Running:
                        _noteAudioManager.OnLateUpdate();
                        _noteManager.OnLateUpdate();
                        _objectCounter.OnLateUpdate();
                        EnforceGameFailureLateUpdate();
                        break;
                }
                GameControlLateUpdate();
                _noteEffectPool.OnLateUpdate();
                _recorderStateDisplayer.OnLateUpdate();
                if (_bgManager.IsVideoEnded)
                {
                    _bgManager.SetBrightness(0f);
                }
            }
        }
        private void GameControlLateUpdate()
        {
            using (UnityProfiler.Create("GamePlayManager.GameControlUpdate"))
            {
                if (_audioSample is null)
                {
                    return;
                }
                else if (State < GamePlayStatus.Running)
                {
                    return;
                }
                else if (!_objectCounter.AllFinished)
                {
                    return;
                }
                if (_allNotesFinishedTiming is null)
                {
                    _allNotesFinishedTiming = _thisFrameSec;
                    return;
                }
                else
                {
                    if (_thisFrameSec - (float)_allNotesFinishedTiming < 0.1)
                    {
                        return;
                    }
                }
                var remainingTime = _thisFrameSec - (_audioSample.Length.TotalSeconds / PlaybackSpeed);
                switch (State)
                {
                    case GamePlayStatus.Running:
                        {
                            var result = CalculateScore();

                            switch (result.ComboState)
                            {
                                case ComboState.APPlus:
                                case ComboState.AP:
                                case ComboState.FCPlus:
                                case ComboState.FC:
                                    if (IsPracticeMode)
                                    {
                                        NextRound4Practice(2000).Forget();
                                    }
                                    else
                                    {
                                        EndGame(5000).Forget();
                                    }
                                    return;
                            }
                            if (remainingTime < -7 && !IsPracticeMode)
                            {
                                _skipBtn.SetActive(true);
                            }
                            State = GamePlayStatus.WaitForEnd;
                        }
                        break;
                    case GamePlayStatus.WaitForEnd:
                        {
                            if (IsPracticeMode)
                            {
                                NextRound4Practice().Forget();
                                return;
                            }
                            else if (remainingTime >= 0)
                            {
                                _skipBtn.SetActive(false);
                                EndGame(2000).Forget();
                            }
                        }
                        break;
                }
            }
        }
        private void ComponentPreUpdate()
        {
            switch (State)
            {
                case GamePlayStatus.WaitForEnd:
                case GamePlayStatus.Blocking:
                case GamePlayStatus.Running:
                    _noteAudioManager.OnPreUpdate();
                    _noteManager.OnPreUpdate();
                    _notePoolManager.OnPreUpdate();
                    _objectCounter.OnPreUpdate();
                    break;
            }
            Profiler.BeginSample("TimeDisplayer.OnPreUpdate");
            _timeDisplayer.OnPreUpdate();
            Profiler.EndSample();
        }
        private void NoteManagerUpdate()
        {
            switch (State)
            {
                case GamePlayStatus.WaitForEnd:
                case GamePlayStatus.Blocking:
                case GamePlayStatus.Running:
                    _noteManager.OnUpdate();
                    break;
            }
        }
        private void EnforceGameFailureLateUpdate()
        {
            if (_enforceGameFailureCondition == EnforceGameFailureCondition.Disabled)
            {
                return;
            }
            else if (State != GamePlayStatus.Running && State != GamePlayStatus.Blocking)
            {
                return;
            }
            else if (_gameInfo.IsDanMode || IsPracticeMode)
            {
                return;
            }

            var accStats = _objectCounter.AccurateStats;
            double maxAchievement;
            if (IsClassicMode)
            {
                maxAchievement = accStats.ClassicAchievement_B;
            }
            else
            {
                maxAchievement = accStats.Achievement_A;
            }
            ref readonly var judgeStats = ref _objectCounter.JudgeStats;
            switch (_enforceGameFailureCondition)
            {
                case EnforceGameFailureCondition.TrackSkip_S:
                case EnforceGameFailureCondition.Retry_S:
                    if (maxAchievement < 97f)
                    {
                        goto ZAKO_ZAKO;
                    }
                    break;
                case EnforceGameFailureCondition.TrackSkip_SS:
                case EnforceGameFailureCondition.Retry_SS:
                    if (maxAchievement < 99f)
                    {
                        goto ZAKO_ZAKO;
                    }
                    break;
                case EnforceGameFailureCondition.TrackSkip_SSS:
                case EnforceGameFailureCondition.Retry_SSS:
                    if (maxAchievement < 100f)
                    {
                        goto ZAKO_ZAKO;
                    }
                    break;
                case EnforceGameFailureCondition.TrackSkip_SSSPlus:
                case EnforceGameFailureCondition.Retry_SSSPlus:
                    if (maxAchievement < 100.5f)
                    {
                        goto ZAKO_ZAKO;
                    }
                    break;
                case EnforceGameFailureCondition.TrackSkip_Best:
                case EnforceGameFailureCondition.Retry_Best:
                    if (maxAchievement < (IsClassicMode ? _historyAccurate.Classic : _historyAccurate.DX))
                    {
                        goto ZAKO_ZAKO;
                    }
                    break;
                case EnforceGameFailureCondition.TrackSkip_FC:
                case EnforceGameFailureCondition.Retry_FC:
                    if (judgeStats.TotalMissCount != 0)
                    {
                        goto ZAKO_ZAKO;
                    }
                    break;
                case EnforceGameFailureCondition.TrackSkip_AP:
                case EnforceGameFailureCondition.Retry_AP:
                    if (judgeStats.TotalGreatCount != 0 ||
                        judgeStats.TotalGoodCount != 0 ||
                        judgeStats.TotalMissCount != 0)
                    {
                        goto ZAKO_ZAKO;
                    }
                    break;
                case EnforceGameFailureCondition.Disabled:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
                ZAKO_ZAKO:
                    if (_isEnforceFastRetry)
                    {
                        EnforceFastRetry().Forget();
                    }
                    else
                    {
                        EnforceTrackSkipTo().Forget();
                    }
                    break;
            }
        }
        private void AudioTimeUpdate()
        {
            using (UnityProfiler.Create("GamePlayManager.AudioTimeUpdate"))
            {
                if (_audioSample is null)
                {
                    return;
                }
                else if (AudioStartTime == -114514f)
                {
                    return;
                }

                switch (State)
                {
                    case GamePlayStatus.Running:
                    case GamePlayStatus.Blocking:
                    case GamePlayStatus.WaitForEnd:
                        {
                            //Do not use this!!!! This have connection with sample batch size
                            //AudioTime = (float)audioSample.GetCurrentTime();
                            var elapsedSeconds = _timer.ElapsedSecondsAsFloat;
                            var playbackSpeed = PlaybackSpeed;
                            var timeOffset = elapsedSeconds - _audioStartTime + _devicePlaybackOffset;
                            var realTimeDifference = (float)_audioSample.CurrentSec - (timeOffset * playbackSpeed);
                            var realTimeDifferenceb = (float)_bgManager.CurrentSec - (timeOffset * playbackSpeed);

                            _thisFrameSec = timeOffset;
                            if (MajEnv.Settings.Audio.Backend == SoundBackendOption.BassSimple)
                            {
                                var diff = _thisFrameSec - _audioTrackStartAt;
                                if (diff <= 2f && diff >= 0f)
                                {
                                    _devicePlaybackOffset += realTimeDifference * 0.8f;
                                }
                            }

                            var sb = ZString.CreateStringBuilder(true);
                            try
                            {
                                ERROR_TEXT_FORMAT.FormatTo(ref sb, realTimeDifference, realTimeDifferenceb, _devicePlaybackOffset);
                                var a = sb.AsArraySegment();
                                _errText.SetCharArray(a.Array, a.Offset, a.Count);
                            }
                            finally
                            {
                                sb.Dispose();
                            }
                        }
                        break;
                }
            }
        }

        #endregion

        #region GameEnding

        private GameResult CalculateScore(bool playEffect = true)
        {
            var accStats = _objectCounter.AccurateStats;
            print("GameResult: " + accStats.Achievement_A);
            var result = _objectCounter.GetPlayRecord(_songDetail, _listConfig.SelectedDiff);
            _gameInfo.RecordResult(result);

            if (!playEffect)
            {
                return result;
            }
            PlayComboEffect(result);

            return result;
        }
        private void PlayComboEffect(GameResult result)
        {
            switch (result.ComboState)
            {
                case ComboState.APPlus:
                    _allPerfectAnimation.SetActive(true);
                    MajInstances.AudioManager.PlaySFX("all_perfect_plus.wav");
                    MajInstances.AudioManager.PlaySFX("bgm_explosion.mp3");
                    CabinetLed.SetAllLightSineFunc(Color.yellow, 2000);
                    break;
                case ComboState.AP:
                    _allPerfectAnimation.SetActive(true);
                    MajInstances.AudioManager.PlaySFX("all_perfect.wav");
                    MajInstances.AudioManager.PlaySFX("bgm_explosion.mp3");
                    CabinetLed.SetAllLightSineFunc(Color.red, 2000);
                    break;
                case ComboState.FCPlus:
                    _fullComboAnimation.SetActive(true);
                    MajInstances.AudioManager.PlaySFX("full_combo_plus.wav");
                    MajInstances.AudioManager.PlaySFX("bgm_explosion.mp3");
                    CabinetLed.SetAllLightSineFunc(Color.green, 2000);
                    break;
                case ComboState.FC:
                    _fullComboAnimation.SetActive(true);
                    MajInstances.AudioManager.PlaySFX("full_combo.wav");
                    MajInstances.AudioManager.PlaySFX("bgm_explosion.mp3");
                    CabinetLed.SetAllLightSineFunc(Color.green, 2000);
                    break;
            }
        }
        private void PlayGameOverEffect()
        {
            if (_gameOverAnimation is null)
            {
                return;
            }

            _gameOverAnimation.SetActive(true);
            MajInstances.AudioManager.PlaySFX("GameOver.wav");
        }
        private async UniTaskVoid NextRound4Practice(int delayMiliseconds = 100)
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }

            State = GamePlayStatus.Ended;

            await UniTask.Delay(delayMiliseconds);
            ClearAllResources();
            var remainingSeconds = 1f;
            var originVol = _trackVolume;
            _audioSample!.Volume = 0;
            while (remainingSeconds > 0)
            {
                _audioSample.Volume = remainingSeconds / 1f * originVol;

                await UniTask.Yield();
                remainingSeconds -= Time.deltaTime;
            }
            _audioSample.Volume = 0;
            _audioSample.Pause();

            await UniTask.Delay(200);
            if (_gameInfo.NextRound())
            {
                MajInstances.SceneSwitcher.SwitchScene("Game", false);
            }
            else
            {
                MajInstances.SceneSwitcher.SwitchScene("Result");
            }
        }
        /// <summary>Stops the current run and reloads the gameplay scene after recording has finished.</summary>
        /// <returns>The asynchronous gameplay restart.</returns>
        internal async UniTaskVoid FastRetry()
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }
            State = GamePlayStatus.Ended;
            var wait4Recorder = Recording.Recorder.StopRecordAsync();
            while (!wait4Recorder.IsCompleted)
            {
                _sceneSwitcher.SetLoadingText($"{"Waiting for recorder".i18n()}...");
                await UniTask.Yield();
            }
            MajInstances.SceneSwitcher.FadeIn();
            await UniTask.Delay(400);
            ClearAllResources();
            MajInstances.SceneSwitcher.SwitchScene("Game", false);
        }

        public void GameOver()
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }

            CalculateScore(playEffect: false);
            _cts.Cancel();
            _audioSample?.Stop();
            PlayGameOverEffect();
            EndGame(5000, targetScene: "TotalResult").Forget();
        }

        /// <summary>Skips the current track or returns to the current practice selection.</summary>
        /// <param name="quickReturn">Whether practice mode uses the shorter return transition.</param>
        /// <returns>The asynchronous transition to the song list or practice selection.</returns>
        internal UniTaskVoid SkipTrack(bool quickReturn = false)
        {
            if (State == GamePlayStatus.Ended)
            {
                return default;
            }
            if (!IsPracticeMode)
            {
                return TrackSkipTo();
            }

            var info = new GameInfo(GameMode.Practice, _gameInfo.Charts, _gameInfo.Levels, 114514)
            {
                TimeRange = _gameInfo.TimeRange,
            };
            Majdata<GameInfo>.Instance = info;
            if (quickReturn)
            {
                return ReturnTo("Practice");
            }
            return TrackSkipTo("Practice", 2000);
        }

        /// <summary>Opens practice selection around the current playback time and stops the current run.</summary>
        internal void FastPractice()
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }

            var startTime = Math.Max(ThisFrameSec - 5f, 0f);
            var endTime = Math.Min(ThisFrameSec + 10f, (float)_audioSample!.Length.TotalSeconds);
            var info = new GameInfo(GameMode.Practice, _gameInfo.Charts, _gameInfo.Levels, 114514)
            {
                TimeRange = new Range<double>(startTime, endTime),
            };
            Majdata<GameInfo>.Instance = info;

            State = GamePlayStatus.Ended;
            _cts.Cancel();
            _audioSample?.Stop();
            ExitToScene("Practice", 0).Forget();
        }

        private async UniTaskVoid ReturnTo(string sceneName = "List")
        {
            State = GamePlayStatus.Ended;
            _audioSample?.Stop();
            await ExitToScene(sceneName, 500, false);
        }
        private async UniTaskVoid TrackSkipTo(string sceneName = "List", int delayMiliseconds = 0)
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }

            State = GamePlayStatus.Ended;
            _cts.Cancel();
            _audioSample?.Stop();
            await ExitToScene(sceneName, delayMiliseconds);
        }
        private async UniTaskVoid EnforceTrackSkipTo(string sceneName = "List")
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }

            State = GamePlayStatus.Ended;
            _cts.Cancel();
            _audioSample?.Stop();
            PlayGameOverEffect();
            await ExitToScene(sceneName, 5000, true);
        }
        private async UniTaskVoid EnforceFastRetry()
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }

            State = GamePlayStatus.Ended;
            _cts.Cancel();
            _audioSample?.Stop();
            PlayGameOverEffect();
            await UniTask.Delay(5000);
            MajInstances.SceneSwitcher.FadeIn();
            await UniTask.Delay(400);
            ClearAllResources();
            MajInstances.SceneSwitcher.SwitchScene("Game", false);
        }
        public async UniTaskVoid EndGame(int delayMiliseconds = 100, string targetScene = "Result")
        {
            if (State == GamePlayStatus.Ended)
            {
                return;
            }
            State = GamePlayStatus.Ended;

            await UniTask.Delay(delayMiliseconds);
            await MajInstances.SceneSwitcher.FadeInAsync();
            ClearAllResources();
            await UniTask.DelayFrame(5);

            MajInstances.SceneSwitcher.SwitchScene(targetScene);
        }
        private async UniTask ExitToScene(string sceneName, int delayMiliseconds = 0, bool delayBeforeFade = false)
        {
            var sceneSwitcher = MajInstances.SceneSwitcher;
            if (delayBeforeFade && delayMiliseconds > 0)
            {
                await UniTask.Delay(delayMiliseconds);
            }
            await sceneSwitcher.FadeInAsync();
            if (!delayBeforeFade && delayMiliseconds > 0)
            {
                await UniTask.Delay(delayMiliseconds);
            }
            ClearAllResources();
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);

            var wait4Recorder = Recording.Recorder.StopRecordAsync();
            while (!wait4Recorder.IsCompleted)
            {
                _sceneSwitcher.SetLoadingText($"{"Waiting for recorder".i18n()}...");
                await UniTask.Yield();
            }
            _sceneSwitcher.SetLoadingText(string.Empty);
            sceneSwitcher.SwitchScene(sceneName, false);
        }

        #endregion

        #region Events

        #endregion

        #region Clean Up
        private void DisposeAudioTrack()
        {
            if (_audioSample is not null)
            {
                _audioSample.Stop();
                _audioSample = null;
            }
        }
        private void ClearAllResources()
        {
            StopAllCoroutines();

            _cts.Cancel();

            InputManager.ClearAllSubscriber();
            MajInstances.SceneSwitcher.SetLoadingText(string.Empty, Color.white);
            MajInstances.GameManager.EnableGC();
            Majdata<GamePlayManager>.Free();
            Majdata<INoteController>.Free();
            Majdata<INoteTimeProvider>.Free();
        }

        private void OnDestroy()
        {
            try
            {
                MajDebug.LogInfo("GPManagerDestroy");
                //we dont StopRecordAsync at here because we want the result screen as well
                DisposeAudioTrack();
                ClearAllResources();
            }
            finally
            {
                Cursor.visible = true;
                InputManager.UseOuterTouchAsSensor = false;
                InputManager.UseGameplayTouchEnhancementFeatures = false;
                InputManager.Override_TouchSimulationRadius = 0.1f;
                InputManager.Override_TouchAAreaExtraRadius = 0f;
                InputManager.Override_TouchBAreaExtraRadius = 0f;
                InputManager.Override_TouchCAreaExtraRadius = 0f;
                InputManager.Override_TouchDAreaExtraRadius = 0f;
                InputManager.Override_TouchEAreaExtraRadius = 0f;

                MajInstances.BackgroundVideo.Show();
            }
        }
        #endregion
    }
}
