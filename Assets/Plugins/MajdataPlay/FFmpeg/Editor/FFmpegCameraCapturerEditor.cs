#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.FFmpeg.Editor
{
    /// <summary>Provides camera recording settings, controls, and measured diagnostics in the Inspector.</summary>
    [CustomEditor(typeof(FFmpegCameraCapturer))]
    [CanEditMultipleObjects]
    internal sealed class FFmpegCameraCapturerEditor : UnityEditor.Editor
    {
        /// <summary>Specifies the minimum wall-clock interval between frame-rate samples.</summary>
        private const double SampleIntervalSeconds = 0.5;
        /// <summary>Tracks whether this Inspector may receive updates.</summary>
        private bool _inspectorActive;
        /// <summary>Distinguishes asynchronous operations from a previous Inspector activation.</summary>
        private int _activation;
        /// <summary>Observes the current start operation without binding recording lifetime to selection.</summary>
        private Task? _startOperation;
        /// <summary>Observes finalization and prevents repeated stop requests.</summary>
        private Task? _stopOperation;
        /// <summary>Retains errors raised by Inspector controls or file selection.</summary>
        private string? _operationError;
        /// <summary>Indicates whether the frame-rate counters have a valid baseline.</summary>
        private bool _hasSample;
        /// <summary>Stores the previous sample's Editor wall-clock time.</summary>
        private double _sampleWallSeconds;
        /// <summary>Stores the previous sample's active recording duration.</summary>
        private double _sampleRecordingSeconds;
        /// <summary>Stores the previous sample's successful encoder submission count.</summary>
        private long _sampleEncodedFrames;
        /// <summary>Stores the previous sample's Unity frame count.</summary>
        private int _sampleGameFrames;
        /// <summary>Stores the recording lifecycle used by the current sample baseline.</summary>
        private CameraCaptureState _sampleState;
        /// <summary>Stores the recording path used by the current sample baseline.</summary>
        private string? _sampleOutputPath;
        /// <summary>Stores whether the Editor was paused at the previous sample.</summary>
        private bool _sampleEditorPaused;
        /// <summary>Stores the measured successful encoder submissions per active recording second.</summary>
        private double _submittedFrameRate;
        /// <summary>Stores the measured Unity frames per Editor wall-clock second.</summary>
        private double _gameFrameRate;

        /// <summary>Registers frame-rate sampling for this Inspector activation.</summary>
        private void OnEnable()
        {
            _inspectorActive = true;
            _activation++;
            _hasSample = false;
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>Detaches Inspector updates while leaving the component's recording lifetime intact.</summary>
        private void OnDisable()
        {
            _inspectorActive = false;
            _activation++;
            EditorApplication.update -= OnEditorUpdate;
        }

        /// <summary>Draws serialized settings and single-component Play Mode controls and diagnostics.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length != 1)
            {
                EditorGUILayout.HelpBox("Recording controls and live diagnostics require one selected capturer. Multi-selection edits configuration only.", MessageType.Info);
                return;
            }

            if (!Application.isPlaying || target is not FFmpegCameraCapturer capturer || capturer == null)
            {
                return;
            }

            if (!IsLiveSceneCapturer(capturer))
            {
                EditorGUILayout.HelpBox("Recording controls require a live scene component. Prefab assets and prefab previews only expose configuration.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            DrawControls(capturer);
            EditorGUILayout.Space();
            DrawDiagnostics(capturer);
        }

        /// <summary>Requests Inspector updates while one selected component is running in Play Mode.</summary>
        /// <returns>True while this Inspector is active in single-selection Play Mode; otherwise false.</returns>
        public override bool RequiresConstantRepaint()
        {
            return _inspectorActive && Application.isPlaying && targets.Length == 1;
        }

        /// <summary>Draws recording controls with lifecycle and asynchronous-operation guards.</summary>
        /// <param name="capturer">The single live component controlled by this Inspector.</param>
        private void DrawControls(FFmpegCameraCapturer capturer)
        {
            var state = capturer.State;
            var starting = IsPending(_startOperation);
            var stopping = IsPending(_stopOperation) || state == CameraCaptureState.Stopping;
            var active = state == CameraCaptureState.Preparing || state == CameraCaptureState.Recording || state == CameraCaptureState.Paused;
            var canStart = !starting && !stopping && !active && capturer.CanStartRecording && !EditorApplication.isPaused;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!canStart))
                {
                    if (GUILayout.Button("Start Recording"))
                    {
                        SelectOutputAndStart(capturer);
                    }
                }

                using (new EditorGUI.DisabledScope(stopping || (state != CameraCaptureState.Recording && state != CameraCaptureState.Paused)))
                {
                    if (GUILayout.Button(state == CameraCaptureState.Paused ? "Resume" : "Pause"))
                    {
                        SetPaused(capturer, state != CameraCaptureState.Paused);
                    }
                }

                using (new EditorGUI.DisabledScope(stopping || !active))
                {
                    if (GUILayout.Button(stopping ? "Stopping..." : "Stop Recording"))
                    {
                        _operationError = null;
                        _stopOperation = ObserveOperationAsync(capturer.StopRecordingAsync(), capturer,
                            new WeakReference<FFmpegCameraCapturerEditor>(this), _activation);
                    }
                }
            }
        }

        /// <summary>Draws actual encoder diagnostics separately from editable preferences.</summary>
        /// <param name="capturer">The component whose active or final recording statistics are displayed.</param>
        private void DrawDiagnostics(FFmpegCameraCapturer capturer)
        {
            EditorGUILayout.LabelField("State", capturer.State.ToString());
            EditorGUILayout.LabelField("Encoder", capturer.EncoderName ?? "Not initialized");
            EditorGUILayout.LabelField("Encoder type", capturer.EncoderType?.ToString() ?? "Not initialized");
            EditorGUILayout.LabelField("Rate control", capturer.RateControlMode?.ToString() ?? "Not initialized");
            EditorGUILayout.LabelField("Encoding format", capturer.EncodingFormat?.ToString() ?? "Not started");
            EditorGUILayout.LabelField("Recording size", capturer.Width > 0 ? $"{capturer.Width} x {capturer.Height}" : "Not started");
            EditorGUILayout.LabelField("Configured FPS (next recording)", capturer.Options.FrameRate.ToString());
            EditorGUILayout.LabelField("Recording time base", capturer.FrameRate > 0 ? $"{capturer.FrameRate} fps" : "Not started");
            EditorGUILayout.LabelField("Submitted FPS (measured)", $"{_submittedFrameRate:F2} fps");
            EditorGUILayout.LabelField("Game FPS (measured)", $"{_gameFrameRate:F2} fps");
            EditorGUILayout.HelpBox("Configured FPS sets the capture limit and timestamp time base. Frames are not duplicated to fill gaps; slower game or camera rendering lowers the actual submitted FPS. Measurements use a 0.5-second window; queued frames can cause brief submission spikes. Recording Pause excludes paused time; the Editor's Pause toolbar does not pause the recorder clock.", MessageType.Info);
            if (EditorApplication.isPaused)
            {
                EditorGUILayout.HelpBox("Play Mode is paused. Pause recording before using the Editor Pause toolbar to avoid timestamp gaps. Stop may wait for GPU readback callbacks until Play Mode resumes.", MessageType.Warning);
            }

            EditorGUILayout.LabelField("Bitrate (last media window)", FormatBitRate(capturer.CurrentBitRate));
            EditorGUILayout.HelpBox("Bitrate measures compressed video over the last approximately one-second media window, excluding container overhead. It can retain its last value while recording is paused, and pending frames may still finish encoding.", MessageType.Info);
            EditorGUILayout.LabelField("Bitrate (target)", FormatBitRate(capturer.BitRate));
            EditorGUILayout.LabelField("Bitrate (maximum)", FormatBitRate(capturer.MaximumBitRate));
            EditorGUILayout.LabelField("Software threads (actual)", capturer.SoftwareThreadCount.ToString());
            EditorGUILayout.LabelField("Submitted frames", capturer.EncodedFrames.ToString());
            EditorGUILayout.LabelField("Dropped frame intervals", capturer.DroppedFrames.ToString());
            EditorGUILayout.LabelField("Recording time", $"{capturer.TimeSeconds:F3} s");
            EditorGUILayout.LabelField("Output path");
            EditorGUILayout.SelectableLabel(capturer.OutputPath ?? "Not started", EditorStyles.wordWrappedLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight * 2));
            if (!string.IsNullOrEmpty(capturer.HardwareFallbackReason))
            {
                EditorGUILayout.HelpBox(capturer.HardwareFallbackReason, MessageType.Info);
            }

            if (!string.IsNullOrEmpty(capturer.LastError))
            {
                EditorGUILayout.HelpBox(capturer.LastError, MessageType.Error);
            }

            if (!string.IsNullOrEmpty(_operationError) && _operationError != capturer.LastError)
            {
                EditorGUILayout.HelpBox(_operationError, MessageType.Error);
            }
        }

        /// <summary>Prompts for a new output filename and observes the component's start task.</summary>
        /// <param name="capturer">The component to start after a valid filename is selected.</param>
        private void SelectOutputAndStart(FFmpegCameraCapturer capturer)
        {
            var extension = capturer.Options.Format == VideoEncodingFormat.VP9 ? "webm" : "mp4";
            var filename = "camera-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = EditorUtility.SaveFilePanel("Record Camera", Application.persistentDataPath, filename, extension);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!_inspectorActive || capturer == null || !IsLiveSceneCapturer(capturer) || !capturer.CanStartRecording)
            {
                return;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                _operationError = "Choose a new output file. Camera recording does not overwrite existing files.";
                return;
            }

            _operationError = null;
            _hasSample = false;
            _startOperation = ObserveOperationAsync(capturer.StartRecordingAsync(path), capturer,
                new WeakReference<FFmpegCameraCapturerEditor>(this), _activation);
        }

        /// <summary>Changes capture pause state and reports synchronous control failures.</summary>
        /// <param name="capturer">The active component to pause or resume.</param>
        /// <param name="paused">True to pause capture; false to resume it.</param>
        private void SetPaused(FFmpegCameraCapturer capturer, bool paused)
        {
            try
            {
                if (paused)
                {
                    capturer.PauseRecording();
                }
                else
                {
                    capturer.ResumeRecording();
                }

                _operationError = null;
                _hasSample = false;
                _submittedFrameRate = 0;
            }
            catch (Exception error)
            {
                _operationError = error.Message;
                Debug.LogException(error, capturer);
            }
        }

        /// <summary>Observes every asynchronous control exception without retaining or repainting a closed Inspector.</summary>
        /// <param name="operation">The start or stop task to observe.</param>
        /// <param name="capturer">The component supplied as the diagnostic context while it exists.</param>
        /// <param name="inspectorReference">A weak reference to the Inspector that initiated the operation.</param>
        /// <param name="activation">The Inspector activation that may receive the resulting error.</param>
        /// <returns>A task that completes after the operation's success or failure has been observed.</returns>
        private static async Task ObserveOperationAsync(Task operation, FFmpegCameraCapturer capturer,
            WeakReference<FFmpegCameraCapturerEditor> inspectorReference, int activation)
        {
            try
            {
                await operation;
            }
            catch (Exception error)
            {
                Debug.LogException(error, capturer != null ? capturer : null);
                if (inspectorReference.TryGetTarget(out var inspector) && inspector != null
                    && inspector._inspectorActive && inspector._activation == activation)
                {
                    inspector._operationError = error.Message;
                }
            }
        }

        /// <summary>Samples successful submissions and game frames using separate recording and wall clocks.</summary>
        private void OnEditorUpdate()
        {
            if (!_inspectorActive || this == null)
            {
                return;
            }

            if (!Application.isPlaying || targets.Length != 1
                || target is not FFmpegCameraCapturer capturer || capturer == null || !IsLiveSceneCapturer(capturer))
            {
                _hasSample = false;
                _submittedFrameRate = 0;
                _gameFrameRate = 0;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var recordingTime = capturer.TimeSeconds;
            var encodedFrames = capturer.EncodedFrames;
            var gameFrames = Time.frameCount;
            if (!_hasSample || _sampleState != capturer.State || _sampleOutputPath != capturer.OutputPath
                || _sampleEditorPaused != EditorApplication.isPaused || encodedFrames < _sampleEncodedFrames
                || recordingTime < _sampleRecordingSeconds || gameFrames < _sampleGameFrames)
            {
                _submittedFrameRate = 0;
                _gameFrameRate = 0;
                SetSampleBaseline(capturer, now, recordingTime, encodedFrames, gameFrames);
                return;
            }

            var wallSeconds = now - _sampleWallSeconds;
            if (wallSeconds < SampleIntervalSeconds)
            {
                return;
            }

            var recordingSeconds = recordingTime - _sampleRecordingSeconds;
            _submittedFrameRate = capturer.IsRecording && !EditorApplication.isPaused && recordingSeconds > 0
                ? (encodedFrames - _sampleEncodedFrames) / recordingSeconds : 0;
            _gameFrameRate = EditorApplication.isPaused ? 0 : (gameFrames - _sampleGameFrames) / wallSeconds;
            SetSampleBaseline(capturer, now, recordingTime, encodedFrames, gameFrames);
            Repaint();
        }

        /// <summary>Stores the current counters for the next frame-rate window.</summary>
        /// <param name="capturer">The component supplying the recording state and output identity.</param>
        /// <param name="wallSeconds">The current Editor wall-clock time in seconds.</param>
        /// <param name="recordingSeconds">The active recording duration read for this sample.</param>
        /// <param name="encodedFrames">The successful encoder submission counter read for this sample.</param>
        /// <param name="gameFrames">The Unity frame count read for this sample.</param>
        private void SetSampleBaseline(FFmpegCameraCapturer capturer, double wallSeconds,
            double recordingSeconds, long encodedFrames, int gameFrames)
        {
            _hasSample = true;
            _sampleWallSeconds = wallSeconds;
            _sampleRecordingSeconds = recordingSeconds;
            _sampleEncodedFrames = encodedFrames;
            _sampleGameFrames = gameFrames;
            _sampleState = capturer.State;
            _sampleOutputPath = capturer.OutputPath;
            _sampleEditorPaused = EditorApplication.isPaused;
        }

        /// <summary>Tests whether a previously observed control operation is still pending.</summary>
        /// <param name="operation">The observed operation, or null when no request has been issued.</param>
        /// <returns>True when the operation has not completed; otherwise false.</returns>
        private static bool IsPending(Task? operation)
        {
            return operation != null && !operation.IsCompleted;
        }

        /// <summary>Distinguishes running scene instances from persistent assets and prefab previews.</summary>
        /// <param name="capturer">The existing component whose scene lifetime is inspected.</param>
        /// <returns>True for a component participating in the current Play Mode; otherwise false.</returns>
        private static bool IsLiveSceneCapturer(FFmpegCameraCapturer capturer)
        {
            return !EditorUtility.IsPersistent(capturer) && Application.IsPlaying(capturer.gameObject);
        }

        /// <summary>Formats an actual bit rate or indicates that recording has not initialized it.</summary>
        /// <param name="bitRate">The compressed-video bit rate in bits per second.</param>
        /// <returns>A readable bps, kbps, or Mbps value, or Unknown for nonpositive values.</returns>
        private static string FormatBitRate(long bitRate)
        {
            if (bitRate <= 0)
            {
                return "Unknown";
            }

            if (bitRate >= 1_000_000)
            {
                return $"{bitRate / 1_000_000d:F2} Mbps";
            }

            if (bitRate >= 1_000)
            {
                return $"{bitRate / 1_000d:F2} kbps";
            }

            return $"{bitRate} bps";
        }
    }
}
