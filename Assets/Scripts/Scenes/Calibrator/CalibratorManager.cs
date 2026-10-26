using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO;
using MajdataPlay.Timer;
using MajdataPlay.Utils;
using System;
using TMPro;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Calibrator
{
    internal sealed class CalibratorManager : MonoBehaviour
    {
        const string CALIBRATION_AUDIO_FILE_NAME = "calibrate.ogg";
        const string SETTINGS_BACKGROUND_MUSIC = "bgm_select.mp3";
        const string EMPTY_OFFSET = "--";
        const string AVERAGE_PREFIX = "平均延迟: ";
        readonly static double[] _beatTimesMs = { 1000d, 3000d, 5000d, 7000d };

        [SerializeField] TextMeshProUGUI _avgOffset = null!;
        [SerializeField] TextMeshProUGUI _offset1 = null!;
        [SerializeField] TextMeshProUGUI _offset2 = null!;
        [SerializeField] TextMeshProUGUI _offset3 = null!;
        [SerializeField] TextMeshProUGUI _offset4 = null!;

        readonly double[] _offsetsMs = new double[_beatTimesMs.Length];
        AudioSampleWrap _calibrationAudio = AudioSampleWrap.Empty;
        MajTimer _timer;
        bool _isAudioReady;
        bool _isStartPending;
        bool _isCalibrating;
        bool _isExited;
        bool _isSettingsBackgroundMusicPaused;
        int _hitIndex;

        Span<TextMeshProUGUI> OffsetLabels => new[] { _offset1, _offset2, _offset3, _offset4 };

        void Awake()
        {
            ResetResults();
            InputManager.BindArea(OnCalibrationAreaDown, ButtonZone.A4);
            LoadCalibrationAudioAsync().Forget();
        }

        async UniTaskVoid LoadCalibrationAudioAsync()
        {
            try
            {
                var audioFile = MajEnv.AssetsDirectory.FindFileAt($"SFX/{CALIBRATION_AUDIO_FILE_NAME}");
                if (audioFile is null)
                {
                    MajDebug.LogError($"[Audio Calibration] Audio file not found: SFX/{CALIBRATION_AUDIO_FILE_NAME}");
                    return;
                }
                _calibrationAudio = await MajInstances.AudioManager.LoadMusicAsync(audioFile.Location, normalize: false);
                if (_calibrationAudio.IsEmpty)
                {
                    MajDebug.LogError($"[Audio Calibration] Failed to load: {audioFile.Location}");
                    return;
                }

                _calibrationAudio.SetVolume(1f);
                _isAudioReady = true;
                if (_isStartPending)
                {
                    StartCalibration();
                }
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }

        void OnCalibrationAreaDown(object? sender, InputEventArgs e)
        {
            if (!e.IsDown)
            {
                return;
            }

            if (!_isCalibrating)
            {
                StartCalibration();
                return;
            }

            var hitTimeMs = _timer.UnscaledElapsedMilliseconds;
            if (hitTimeMs < 500d || _hitIndex >= _beatTimesMs.Length)
            {
                return;
            }

            var offsetMs = hitTimeMs - _beatTimesMs[_hitIndex];
            _offsetsMs[_hitIndex] = offsetMs;
            OffsetLabels[_hitIndex].text = FormatSignedInteger(offsetMs);
            _hitIndex++;

            if (_hitIndex == _beatTimesMs.Length)
            {
                FinishCalibration();
            }
        }

        void StartCalibration()
        {
            ResetResults();
            if (!_isAudioReady)
            {
                _isStartPending = true;
                return;
            }

            _isStartPending = false;
            _isCalibrating = true;
            PauseSettingsBackgroundMusic();
            _calibrationAudio.PlayOneShot();
            _timer = MajTimeline.CreateTimer();
        }

        void FinishCalibration()
        {
            _isCalibrating = false;
            var sum = 0d;
            foreach (var offset in _offsetsMs)
            {
                sum += offset;
            }
            _avgOffset.text = AVERAGE_PREFIX + FormatSignedInteger(sum / _offsetsMs.Length) + "ms";
            ResumeSettingsBackgroundMusic();
        }

        void ResetResults()
        {
            _hitIndex = 0;
            Array.Clear(_offsetsMs, 0, _offsetsMs.Length);
            foreach (var label in OffsetLabels)
            {
                label.text = EMPTY_OFFSET;
            }
            _avgOffset.text = string.Empty;
        }

        static string FormatSignedInteger(double milliseconds)
        {
            var rounded = (int)Math.Round(milliseconds, MidpointRounding.AwayFromZero);
            return rounded.ToString("+0;-0;+0");
        }

        void Update()
        {
            if (_isExited)
            {
                return;
            }
            var isExitRequested = InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A5) ||
                                  InputManager.IsSensorClickCompletedInThisFrame(SensorArea.A5);
            if (isExitRequested)
            {
                _isExited = true;
                _isCalibrating = false;
                _calibrationAudio.Stop();
                ResumeSettingsBackgroundMusic();
                MajInstances.SceneSwitcher.SwitchScene("Setting");
                return;
            }
            if (_isCalibrating && !_calibrationAudio.IsPlaying)
            {
                _isCalibrating = false;
                ResumeSettingsBackgroundMusic();
            }
        }

        void OnDestroy()
        {
            InputManager.UnbindArea(OnCalibrationAreaDown, ButtonZone.A4);
            _calibrationAudio.Dispose();
        }

        void PauseSettingsBackgroundMusic()
        {
            var backgroundMusic = MajInstances.AudioManager.GetSFX(SETTINGS_BACKGROUND_MUSIC);
            if (!backgroundMusic.IsPlaying)
            {
                return;
            }
            backgroundMusic.Pause();
            _isSettingsBackgroundMusicPaused = true;
        }

        void ResumeSettingsBackgroundMusic()
        {
            if (!_isSettingsBackgroundMusicPaused)
            {
                return;
            }
            MajInstances.AudioManager.GetSFX(SETTINGS_BACKGROUND_MUSIC).Play();
            _isSettingsBackgroundMusicPaused = false;
        }
    }
}
