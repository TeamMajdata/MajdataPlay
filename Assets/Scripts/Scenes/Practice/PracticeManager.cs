using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game;
using MajSimai;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MajdataPlay.Scenes.Practice
{
#nullable enable
    public class PracticeManager : MonoBehaviour
    {
        public TextMeshProUGUI startTimeText;
        public TextMeshProUGUI endTimeText;
        public ChartVisualDisplayer chartAnalyzer;
        public RectTransform selectionBox;
        public TextMeshProUGUI timeText;
        public TextMeshProUGUI rTimeText;
        public Slider progress;

        private const string TIME_STRING = "{0}:{1:00}.{2:000}";

        private float _startTime = 0;
        private float _endTime = 0;
        private float _totalTime = 0;
        private InputRepeatState _playbackSpeedInput;
        private float _startDecreaseHeldDuration;
        private float _startIncreaseHeldDuration;
        private float _endDecreaseHeldDuration;
        private float _endIncreaseHeldDuration;
        private AudioSampleWrap? _audioTrack = AudioSampleWrap.Empty;

        [SerializeField]
        private TextMeshProUGUI _playbackSpeedTitle;
        [SerializeField]
        private TextMeshPro _playbackSpeedValue;

        private CancellationTokenSource cts = new CancellationTokenSource();

        private int _practiceCount = 114514;
        private float _step = 0.2f;
        private float _playbackSpeed = 1f;

        private bool _isInited = false;
        private bool _isExited = false;

        private GameInfo _gameInfo;
        private SimaiFile _simaiFile;

        private float _touchSimulationRadius = 0f;

        private void Awake()
        {
            InputManager.TouchButtonRingEdge = 4.8f;
            _touchSimulationRadius = MajEnv.UserSettings.Touch.TouchSimulationRadius;
            MajEnv.UserSettings.Touch.TouchSimulationRadius = 0;
        }
        private void Start()
        {
            _gameInfo = Majdata<GameInfo>.Instance!;
            _gameInfo.PracticeCount = _practiceCount;
            _playbackSpeed = MajEnv.SystemSettings.Mod.PlaybackSpeed;
            _playbackSpeedTitle.text = "MAJSETTING_PROPERTY_PlaybackSpeed".i18n();
            _playbackSpeedValue.text = ZString.Format("{0:F2}", _playbackSpeed);
            InitAsync().Forget();
        }
        private async UniTaskVoid InitAsync()
        {
            await using (UniTask.ReturnToMainThread())
            {
                await UniTask.SwitchToThreadPool();
                var songinfo = _gameInfo.Charts.FirstOrDefault();

                _ = _gameInfo.Levels.FirstOrDefault();
                await songinfo.PreloadAsync();
                _audioTrack = await songinfo.GetAudioTrackAsync();
                _totalTime = (float)_audioTrack.Length.TotalSeconds;
                _simaiFile = await songinfo.GetMaidataAsync(true);
                var levelIndex = (int)_gameInfo.CurrentLevel;
                var maidata = _simaiFile.Charts[levelIndex].Fumen;

                if (string.IsNullOrEmpty(maidata))
                {
                    await UniTask.SwitchToMainThread();
                    MajInstances.SceneSwitcher.SwitchScene("List", false);
                    return;
                }

                var chart = await SimaiParser.ParseChartAsync(songinfo.Levels[levelIndex], songinfo.Designers[levelIndex], maidata);
                await UniTask.SwitchToMainThread();
                chartAnalyzer.SetSimaiChart(chart, _totalTime);
                if (_gameInfo.TimeRange is not null)
                {
                    _startTime = (float)_gameInfo.TimeRange.Value.Start;
                    _endTime = (float)_gameInfo.TimeRange.Value.End;

                }
                else
                {
                    _startTime = _simaiFile.Offset;
                    _endTime = _totalTime;
                }
            }
            var bgmSFX = MajInstances.AudioManager.GetSFX("bgm_select.mp3");
            if (bgmSFX.IsPlaying)
            {
                bgmSFX.Stop();
            }
            _audioTrack.Play();
            _audioTrack.CurrentSec = _startTime;
            _audioTrack.Volume = MajEnv.UserSettings.Volume.BGM;
            CabinetLed.SetAllLight(Color.white);
            CabinetLed.SetButtonLight(Color.green, 3);
            CabinetLed.SetButtonLight(Color.red, 4);
            MajInstances.SceneSwitcher.FadeOut();
            _playbackSpeedInput.SuppressUntilRelease();
            _isInited = true;
        }
        private void ButtonCheck()
        {
            ref readonly var btnA4State = ref InputManager.GetButtonState(ButtonZone.A4);
            ref readonly var btnA5State = ref InputManager.GetButtonState(ButtonZone.A5);

            if (btnA4State.ClickCompletedThisFrame)
            {
                _isExited = true;
                _gameInfo.TimeRange = new Range<double>(_startTime, _endTime);
                MajEnv.SystemSettings.Mod.PlaybackSpeed = _playbackSpeed;
                MajInstances.SceneSwitcher.SwitchScene("Game", false);
                throw new OperationCanceledException();
            }
            else if (btnA5State.ClickCompletedThisFrame)
            {
                _isExited = true;
                MajEnv.SystemSettings.Mod.PlaybackSpeed = 1;
                MajInstances.SceneSwitcher.SwitchScene("List", false);
                throw new OperationCanceledException();
            }
        }
        private void SensorCheck()
        {
            // Start Time "<"
            ref readonly var e6State = ref InputManager.GetSensorState(SensorArea.A6);
            // Start Time ">"
            ref readonly var b5State = ref InputManager.GetSensorState(SensorArea.B5);
            // End Time "<"
            ref readonly var b4State = ref InputManager.GetSensorState(SensorArea.B4);
            // End Time ">"
            ref readonly var e4State = ref InputManager.GetSensorState(SensorArea.A3);
            //Playback Speed "<"
            ref readonly var e8State = ref InputManager.GetSensorState(SensorArea.E8);
            ref readonly var b7State = ref InputManager.GetSensorState(SensorArea.B7);
            //Playback Speed ">"
            ref readonly var e2State = ref InputManager.GetSensorState(SensorArea.E2);
            ref readonly var b2State = ref InputManager.GetSensorState(SensorArea.B2);

            if (e6State.PressedThisFrame)
            {
                _startTime = Mathf.Clamp(_startTime - 0.2f, 0, _totalTime);
                _audioTrack.CurrentSec = _startTime;
            }
            else if (b5State.PressedThisFrame)
            {
                _startTime = Mathf.Clamp(_startTime + 0.2f, 0, _totalTime);
                _audioTrack.CurrentSec = _startTime;
            }
            else if (b4State.PressedThisFrame)
            {
                _endTime = Mathf.Clamp(_endTime - 0.2f, 0, _totalTime);
                _audioTrack.CurrentSec = _endTime;
            }
            else if (e4State.PressedThisFrame)
            {
                _endTime = Mathf.Clamp(_endTime + 0.2f, 0, _totalTime);
                _audioTrack.CurrentSec = _endTime;
            }

            var needUpdatePBSValue = false;
            var increaseSpeedPressed = e2State.IsPressed || b2State.IsPressed;
            var decreaseSpeedPressed = e8State.IsPressed || b7State.IsPressed;
            var iterationSpeed = MajEnv.SystemSettings.Common.MenuOptionIterationSpeed;
            var repeatInterval = 1f / (iterationSpeed is 0 ? 15 : iterationSpeed);
            if (_playbackSpeedInput.Update(
                increaseSpeedPressed,
                decreaseSpeedPressed,
                MajTimeline.DeltaTime,
                0.4f,
                repeatInterval,
                0f,
                out var speedDirection))
            {
                _playbackSpeed += speedDirection * 0.01f;
                needUpdatePBSValue = true;
            }
            _playbackSpeed = Mathf.Max(_playbackSpeed, 0.01f);
            if (needUpdatePBSValue)
            {
                _playbackSpeed = MathF.Round(_playbackSpeed, 2);
                _playbackSpeedValue.text = ZString.Format("{0:F2}", _playbackSpeed);
            }
            var pressTime = Mathf.Max(
                Mathf.Max(
                    UpdateHeldDuration(ref _startDecreaseHeldDuration, e6State.IsPressed),
                    UpdateHeldDuration(ref _startIncreaseHeldDuration, b5State.IsPressed)),
                Mathf.Max(
                    UpdateHeldDuration(ref _endDecreaseHeldDuration, b4State.IsPressed),
                    UpdateHeldDuration(ref _endIncreaseHeldDuration, e4State.IsPressed)));
            if (pressTime < 0.5f)
            {
                return;
            }
            var ratio = pressTime switch
            {
                > 4 => 128,
                > 3 => 64,
                > 2 => 32,
                > 1 => 16,
                > 0.5f => 8,
                _ => 0
            };
            ref var value = ref Unsafe.NullRef<float>();
            var direction = 0;
            var minValue = 0f;
            var maxValue = 0f;
            if (e6State.IsPressed)
            {
                value = ref _startTime;
                direction = -1;
                maxValue = _endTime;
                minValue = 0;
            }
            else if (b5State.IsPressed)
            {
                value = ref _startTime;
                direction = 1;
                maxValue = _endTime;
                minValue = 0;
            }
            else if (b4State.IsPressed)
            {
                value = ref _endTime;
                direction = -1;
                maxValue = _totalTime;
                minValue = _startTime;
            }
            else if (e4State.IsPressed)
            {
                value = ref _endTime;
                direction = 1;
                maxValue = _totalTime;
                minValue = _startTime;
            }
            else
            {
                return;
            }
            value = (value + (_step * MajTimeline.DeltaTime * ratio * direction)).Clamp(minValue, maxValue);

            _audioTrack.CurrentSec = value;
            _audioTrack.Play();
        }
        private static float UpdateHeldDuration(ref float heldDuration, bool isPressed)
        {
            heldDuration = isPressed ? heldDuration + MajTimeline.DeltaTime : 0f;
            return heldDuration;
        }
        private void Update()
        {
            if (!_isInited || _isExited || _audioTrack is null)
            {
                return;
            }
            UpdateSBTextMeshProUGUI();
            ButtonCheck();
            SensorCheck();

            var currentSec = _audioTrack.CurrentSec;

            if (currentSec > _endTime)
            {
                _audioTrack.CurrentSec = _startTime;
            }
        }
        private void UpdateSBTextMeshProUGUI()
        {
            var start = TimeSpan.FromSeconds(_startTime - _simaiFile.Offset);
            var end = TimeSpan.FromSeconds(_endTime - _simaiFile.Offset);
            var anarect = chartAnalyzer.GetComponent<RectTransform>().rect;
            var x = (_startTime - _simaiFile.Offset) / _totalTime * anarect.width;
            var width = (_endTime - _startTime) / _totalTime * anarect.width;

            startTimeText.text = ZString.Format(TIME_STRING, start.Minutes, start.Seconds, start.Milliseconds);
            endTimeText.text = ZString.Format(TIME_STRING, end.Minutes, end.Seconds, end.Milliseconds);
            selectionBox.sizeDelta = new Vector2((float)width, anarect.height);
            selectionBox.anchoredPosition = new Vector2((float)x, 0);

            var audioLen = _audioTrack.Length;
            var current = TimeSpan.FromSeconds(_audioTrack.CurrentSec - _simaiFile.Offset);
            var remaining = audioLen - current;
            timeText.text = ZString.Format(TIME_STRING, current.Minutes, current.Seconds, current.Milliseconds);
            rTimeText.text = ZString.Format(TIME_STRING, remaining.Minutes, remaining.Seconds, remaining.Milliseconds);
            progress.value = ((float)(current.TotalMilliseconds / audioLen.TotalMilliseconds)).Clamp(0, 1);
        }
        private void OnDestroy()
        {
            cts?.Cancel();
            InputManager.TouchButtonRingEdge = 5.4f;
            _audioTrack?.Stop();
            _audioTrack = null;
            MajEnv.UserSettings.Touch.TouchSimulationRadius = _touchSimulationRadius;
            _isExited = true;
        }
    }
}
