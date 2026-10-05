#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MajdataPlay.FFmpeg;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Draws the real camera Inspector and checks its controls in an isolated Editor Play Mode.</summary>
[InitializeOnLoad]
public sealed class FFmpegCameraEditorSmoke : EditorWindow
{
    /// <summary>Identifies the report retained across Play Mode domain reload.</summary>
    private const string ReportKey = "FFmpegCameraEditorSmoke.Report";
    /// <summary>Identifies the active isolated validation run.</summary>
    private const string ActiveKey = "FFmpegCameraEditorSmoke.Active";
    /// <summary>Stores the report destination.</summary>
    private string _report = "";
    /// <summary>Retains the live recording component.</summary>
    private FFmpegCameraCapturer? _capturer;
    /// <summary>Retains a second component for multi-selection rendering.</summary>
    private FFmpegCameraCapturer? _second;
    /// <summary>Retains the explicitly rendered source camera.</summary>
    private Camera? _camera;
    /// <summary>Retains the caller-owned offscreen camera target.</summary>
    private RenderTexture? _texture;
    /// <summary>Retains the production CustomEditor selected by Unity.</summary>
    private UnityEditor.Editor? _inspector;
    /// <summary>Tracks asynchronous start and stop operations.</summary>
    private Task? _operation;
    /// <summary>Tracks the current validation phase.</summary>
    private int _phase;
    /// <summary>Stores the current phase's Editor wall-clock origin.</summary>
    private double _phaseStarted;
    /// <summary>Stores the bounded run deadline.</summary>
    private double _deadline;
    /// <summary>Stores the active clock value while paused.</summary>
    private double _frozen;
    /// <summary>Counts successful assertions.</summary>
    private int _checks;
    /// <summary>Counts actual Inspector Layout events.</summary>
    private int _layouts;
    /// <summary>Counts actual Inspector Repaint events.</summary>
    private int _repaints;
    /// <summary>Stores the first unexpected GUI or rendering diagnostic.</summary>
    private string? _error;
    /// <summary>Scopes Unity diagnostics to the actual Inspector draw call.</summary>
    private bool _drawing;
    /// <summary>Scopes Unity diagnostics to the fixture's explicit camera render call.</summary>
    private bool _rendering;
    /// <summary>Records the recording states actually painted by Unity.</summary>
    private readonly HashSet<CameraCaptureState> _paintedStates = new HashSet<CameraCaptureState>();
    /// <summary>Stores measured rates for the final report.</summary>
    private string _rateEvidence = "";

    /// <summary>Registers the domain-reload-safe Play Mode entry callback.</summary>
    static FFmpegCameraEditorSmoke()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    /// <summary>Enters Play Mode in the prepared isolated project without changing product assets.</summary>
    /// <exception cref="ArgumentException">The report argument is missing.</exception>
    public static void Run()
    {
        var arguments = Environment.GetCommandLineArgs();
        var report = "";
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index] == "-cameraEditorReport")
            {
                report = arguments[index + 1];
            }
        }
        if (string.IsNullOrEmpty(report))
        {
            throw new ArgumentException("The isolated Inspector validation requires -cameraEditorReport.");
        }
        SessionState.SetString(ReportKey, report);
        SessionState.SetBool(ActiveKey, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Creates the test window after Unity has completed Play Mode domain reload.</summary>
    /// <param name="state">The new Editor Play Mode state.</param>
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(ActiveKey, false))
        {
            var window = GetWindow<FFmpegCameraEditorSmoke>();
            window.titleContent = new GUIContent("Camera Inspector validation");
            window.position = new Rect(30, 30, 640, 1100);
            window.Initialize();
        }
    }

    /// <summary>Resets restored window state and creates the recording fixture and product Inspector.</summary>
    /// <exception cref="Exception">Unity selects an unexpected Inspector implementation.</exception>
    private void Initialize()
    {
        _error = null;
        _phase = 0;
        _checks = 0;
        _layouts = 0;
        _repaints = 0;
        _drawing = false;
        _rendering = false;
        _paintedStates.Clear();
        _operation = null;
        _rateEvidence = "";
        _report = SessionState.GetString(ReportKey, "");
        _deadline = EditorApplication.timeSinceStartup + 40;
        _phaseStarted = EditorApplication.timeSinceStartup;
        var source = new GameObject("Inspector recording camera");
        _camera = source.AddComponent<Camera>();
        _camera.enabled = false;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Color.cyan;
        _texture = new RenderTexture(128, 96, 16);
        _texture.Create();
        _camera.targetTexture = _texture;
        _capturer = source.AddComponent<FFmpegCameraCapturer>();
        _capturer.TargetCamera = _camera;
        _capturer.Options = new EncoderOptions
        {
            Width = 128,
            Height = 96,
            FrameRate = 30,
            MaximumSoftwareThreads = 1,
            Format = VideoEncodingFormat.MPEG4,
            PreferredEncoderType = VideoEncoderType.Software,
            RateControlMode = VideoRateControlMode.VBR,
            BitRate = 200000,
            MaximumBitRate = 400000
        };
        _second = new GameObject("Inspector multi-selection component").AddComponent<FFmpegCameraCapturer>();
        _second.TargetCamera = _camera;
        _inspector = UnityEditor.Editor.CreateEditor(_capturer);
        Check(_inspector.GetType().FullName == "MajdataPlay.FFmpeg.Editor.FFmpegCameraCapturerEditor",
            "Unity selects the production camera CustomEditor");
        Application.logMessageReceived += OnLogMessage;
        EditorApplication.update += Advance;
        Repaint();
    }

    /// <summary>Runs the Inspector only from genuine Unity GUI events.</summary>
    private void OnGUI()
    {
        if (_inspector == null)
        {
            return;
        }
        try
        {
            _drawing = true;
            _inspector.OnInspectorGUI();
            if (Event.current.type == EventType.Layout)
            {
                _layouts++;
            }
            if (Event.current.type == EventType.Repaint)
            {
                _repaints++;
                if (_inspector.targets.Length == 1)
                {
                    _paintedStates.Add(_capturer!.State);
                }
            }
        }
        catch (Exception error)
        {
            _error = error.ToString();
        }
        finally
        {
            _drawing = false;
        }
    }

    /// <summary>Observes unexpected Editor errors without throwing from the log callback.</summary>
    /// <param name="message">The Unity diagnostic message.</param>
    /// <param name="stack">The diagnostic stack trace.</param>
    /// <param name="type">The Unity diagnostic severity.</param>
    private void OnLogMessage(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception)
            && (_drawing || _rendering || stack.Contains("MajdataPlay.FFmpeg") || stack.Contains(nameof(FFmpegCameraEditorSmoke))))
        {
            _error ??= type + ": " + message + Environment.NewLine + stack;
        }
    }

    /// <summary>Advances controls and sampling while the window receives Layout and Repaint.</summary>
    private void Advance()
    {
        try
        {
            if (_error != null)
            {
                throw new Exception(_error);
            }
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                throw new TimeoutException("Isolated Inspector validation did not finish.");
            }
            var capturer = _capturer!;
            if (capturer.LastError != null || capturer.State == CameraCaptureState.Error)
            {
                throw new Exception("Inspector recording failed in phase " + _phase + ": " + capturer.LastError);
            }
            var elapsed = EditorApplication.timeSinceStartup - _phaseStarted;
            if (capturer.IsRecording)
            {
                try
                {
                    _rendering = true;
                    _camera!.Render();
                }
                finally
                {
                    _rendering = false;
                }
            }
            Repaint();
            switch (_phase)
            {
                case 0:
                    if (_layouts < 2 || _repaints < 2)
                    {
                        return;
                    }
                    Check(capturer.CanStartRecording, "stopped Inspector component permits startup");
                    _operation = capturer.StartRecordingAsync(Path.Combine(Path.GetDirectoryName(_report)!,
                        "inspector-" + Guid.NewGuid().ToString("N") + ".mp4"));
                    SetPhase(1);
                    break;
                case 1:
                    if (!_operation!.IsCompleted)
                    {
                        return;
                    }
                    _operation.GetAwaiter().GetResult();
                    Check(capturer.IsRecording && _inspector!.RequiresConstantRepaint(), "active Inspector requests constant repaint");
                    SetPhase(2);
                    break;
                case 2:
                    if (elapsed < 1.3)
                    {
                        return;
                    }
                    var submitted = GetRate("_submittedFrameRate");
                    var game = GetRate("_gameFrameRate");
                    Check(submitted > 0 && submitted <= 40 && game > 0, "Inspector measures submitted and game FPS");
                    _rateEvidence = "submittedFPS=" + submitted + "; gameFPS=" + game;
                    SetPaused(true);
                    _frozen = capturer.TimeSeconds;
                    Check(capturer.IsPaused && !capturer.CanStartRecording, "Inspector Pause handler preserves the live session");
                    SetPhase(3);
                    break;
                case 3:
                    if (elapsed < 0.8)
                    {
                        return;
                    }
                    Check(capturer.TimeSeconds == _frozen && GetRate("_submittedFrameRate") == 0,
                        "paused Inspector keeps recording time frozen and submitted FPS zero");
                    SetPaused(false);
                    Check(capturer.IsRecording && !capturer.IsPaused, "Inspector Resume handler restores capture");
                    SetPhase(4);
                    break;
                case 4:
                    if (elapsed < 0.8)
                    {
                        return;
                    }
                    Check(capturer.TimeSeconds > _frozen + 0.5 && GetRate("_submittedFrameRate") > 0,
                        "resumed Inspector samples new submissions");
                    _operation = capturer.StopRecordingAsync();
                    SetPhase(5);
                    break;
                case 5:
                    if (!_operation!.IsCompleted)
                    {
                        return;
                    }
                    _operation.GetAwaiter().GetResult();
                    Check(capturer.CanStartRecording && capturer.State == CameraCaptureState.Stopped,
                        "Inspector recording finalizes without changing component ownership");
                    SetPhase(6);
                    break;
                case 6:
                    if (elapsed < 0.3)
                    {
                        return;
                    }
                    Check(_paintedStates.Contains(CameraCaptureState.Stopped), "Unity paints finalized single-component recording controls");
                    DestroyImmediate(_inspector);
                    _inspector = UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { capturer, _second! });
                    Check(_inspector.GetType().FullName == "MajdataPlay.FFmpeg.Editor.FFmpegCameraCapturerEditor",
                        "multi-selection uses the same production Inspector");
                    SetPhase(7);
                    break;
                case 7:
                    if (elapsed < 0.3)
                    {
                        return;
                    }
                    Check(!_inspector!.RequiresConstantRepaint(), "multi-selection avoids live diagnostics sampling");
                    Check(_layouts > 10 && _repaints > 10, "real Layout and Repaint cover recording, pause, resume, stop, and multi-selection");
                    Check(_paintedStates.Contains(CameraCaptureState.Stopped) && _paintedStates.Contains(CameraCaptureState.Recording)
                        && _paintedStates.Contains(CameraCaptureState.Paused), "Unity paints stopped, recording, and paused controls");
                    Finish(null);
                    break;
            }
        }
        catch (Exception error)
        {
            Finish(error);
        }
    }

    /// <summary>Reads an actual sampled rate from the production Inspector.</summary>
    /// <param name="field">The measured rate field to inspect.</param>
    /// <returns>The current Inspector rate in frames per second.</returns>
    private double GetRate(string field)
    {
        return (double)_inspector!.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_inspector)!;
    }

    /// <summary>Invokes the same guarded pause control handler used by the production GUI button.</summary>
    /// <param name="paused">True to pause the recording; false to resume it.</param>
    private void SetPaused(bool paused)
    {
        _inspector!.GetType().GetMethod("SetPaused", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_inspector, new object[] { _capturer!, paused });
    }

    /// <summary>Starts the next validation phase with a fresh wall-clock baseline.</summary>
    /// <param name="phase">The new validation phase.</param>
    private void SetPhase(int phase)
    {
        _phase = phase;
        _phaseStarted = EditorApplication.timeSinceStartup;
    }

    /// <summary>Checks one expected Inspector or component behavior.</summary>
    /// <param name="condition">The required condition.</param>
    /// <param name="message">The diagnostic reported on failure.</param>
    /// <exception cref="Exception">The condition is false.</exception>
    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
        _checks++;
    }

    /// <summary>Writes a self-contained result and exits the isolated Editor.</summary>
    /// <param name="failure">The first validation failure, or null after success.</param>
    /// <exception cref="IOException">The isolated result file cannot be written.</exception>
    private void Finish(Exception? failure)
    {
        EditorApplication.update -= Advance;
        Application.logMessageReceived -= OnLogMessage;
        SessionState.SetBool(ActiveKey, false);
        _capturer?.StopRecording();
        var result = failure == null ? "PASS: " + _checks + " Camera Inspector checks; Layout=" + _layouts
            + "; Repaint=" + _repaints + "; " + _rateEvidence : "FAIL: " + failure;
        File.WriteAllText(_report, result);
        Debug.Log(result);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }

    /// <summary>Detaches callbacks when Unity closes or reloads this isolated window.</summary>
    private void OnDisable()
    {
        EditorApplication.update -= Advance;
        Application.logMessageReceived -= OnLogMessage;
        if (_inspector != null)
        {
            DestroyImmediate(_inspector);
        }
        if (_texture != null)
        {
            _texture.Release();
            DestroyImmediate(_texture);
        }
    }
}
