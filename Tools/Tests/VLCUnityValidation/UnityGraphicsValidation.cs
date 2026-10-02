using System;
using System.Diagnostics;
using System.IO;
using LibVLCSharp;
using UnityEditor;
using UnityEngine;

// Copied into a minimal, isolated Unity project by Run-GraphicsValidation.ps1.
// Deliberately does not use -nographics: real texture imports and GPU readback
// are required to validate channels, orientation and render-thread lifetime.
public static class VlcUnityGraphicsValidation
{
    [Serializable]
    sealed class Result
    {
        public bool success;
        public string graphicsApi;
        public bool gpuInterop;
        public bool forcedCpu;
        public int decodedFrames;
        public int casesPassed;
        public bool seekPassed;
        public string error;
    }

    enum Stage { Initialize, AwaitFrame, AwaitStop }
    static LibVLC _library;
    static VlcVideoOutput _output;
    static Media _media;
    static Stage _stage;
    static readonly Stopwatch Clock = new Stopwatch();
    static double _deadline;
    static string _resultPath;
    static string _samplePath;
    static string _fixturePath;
    static int _case;
    static int _frames;
    static bool _sampleRewound;
    static Result _result;
    static bool _finished;

    public static void Run()
    {
        try
        {
            _resultPath = Argument("-vlc-result");
            _samplePath = Argument("-vlc-sample");
            _result = new Result
            {
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                forcedCpu = Array.IndexOf(Environment.GetCommandLineArgs(), "-vlc-force-cpu") >= 0
            };
            var pluginPath = Path.Combine(Application.dataPath, "Plugins", "x86_64");
            Core.Initialize(pluginPath);
            _library = new LibVLC(enableDebugLogs: false, "--no-audio", "--no-video-title-show");
            _output = new VlcVideoOutput(_library, true, true, _result.forcedCpu);
            Clock.Start();
            _deadline = 25;
            _stage = Stage.Initialize;
            EditorApplication.update += Tick;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception error) { Finish(error); }
    }

    static void Tick()
    {
        try
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (Clock.Elapsed.TotalSeconds > _deadline)
                throw new TimeoutException("Timed out in " + _stage + ", case " + _case + ", player " + _output.Player.State);
            if (_stage == Stage.Initialize)
            {
                if (_output.IsReady)
                    StartCase();
                return;
            }
            if (_stage == Stage.AwaitStop)
            {
                if (_output.Player.State != VLCState.Stopped)
                    return;
                _output.Player.Media = null;
                _media.Dispose();
                _media = null;
                if (++_case == 3)
                    Finish(null);
                else
                    StartCase();
                return;
            }
            if (!_output.TryUpdateTexture() || _output.Texture == null)
                return;
            ++_frames;
            ++_result.decodedFrames;
            _result.gpuInterop = _output.UsesGpuInterop;
            if (_case < 2)
                ValidateQuadrants(_output.Texture);
            else
            {
                if (_frames < 3)
                    return;
                if (!_sampleRewound)
                {
                    // Advance before rewinding so this verifies a real seek,
                    // rather than setting zero while the first frame is pending.
                    if (_output.Player.Time < 250)
                        return;
                    _output.Player.SetPause(true);
                    if (!_output.Player.SetTime(0, true))
                        throw new InvalidOperationException("VLC rejected the sample rewind.");
                    _output.Player.SetPause(false);
                    _sampleRewound = true;
                    _frames = 0;
                    _deadline = Clock.Elapsed.TotalSeconds + 25;
                    return;
                }
                _result.seekPassed = true; // Several frames decoded after rewind.
            }

            ++_result.casesPassed;
            _output.Player.SetPause(true);
            _output.TryUpdateTexture(); // Paused updates must also be safe.
            _output.Player.Stop();
            _stage = Stage.AwaitStop;
            _deadline = Clock.Elapsed.TotalSeconds + 25;
        }
        catch (Exception error) { Finish(error); }
    }

    static void StartCase()
    {
        _frames = 0;
        string path;
        if (_case < 2)
        {
            int width = _case == 0 ? 64 : 79;
            int height = _case == 0 ? 48 : 53;
            _fixturePath = Path.Combine(Path.GetDirectoryName(_resultPath), "quadrants-" + width + ".png");
            CreateFixture(_fixturePath, width, height);
            path = _fixturePath;
        }
        else
            path = _samplePath;
        _media = new Media(new Uri(Path.GetFullPath(path)));
        if (_case < 2)
            _media.AddOption(":image-duration=5");
        _output.Player.Media = _media;
        if (!_output.Player.Play())
            throw new InvalidOperationException("VLC rejected " + path);
        _stage = Stage.AwaitFrame;
        _deadline = Clock.Elapsed.TotalSeconds + 25;
    }

    static void CreateFixture(string path, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var colors = new Color32[width * height];
        for (int y = 0; y < height; ++y)
            for (int x = 0; x < width; ++x)
                colors[y * width + x] = y < height / 2
                    ? (x < width / 2 ? new Color32(0, 0, 255, 255) : new Color32(255, 255, 0, 255))
                    : (x < width / 2 ? new Color32(255, 0, 0, 255) : new Color32(0, 255, 0, 255));
        texture.SetPixels32(colors);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
    }

    static void ValidateQuadrants(Texture source)
    {
        int expectedWidth = _case == 0 ? 64 : 79;
        int expectedHeight = _case == 0 ? 48 : 53;
        if (source.width != expectedWidth || source.height != expectedHeight)
            throw new InvalidOperationException("Unexpected output dimensions " + source.width + "x" + source.height);
        var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readback.Apply();
            AssertColor(readback.GetPixel(source.width / 4, source.height * 3 / 4), Color.red, "top left");
            AssertColor(readback.GetPixel(source.width * 3 / 4, source.height * 3 / 4), Color.green, "top right");
            AssertColor(readback.GetPixel(source.width / 4, source.height / 4), Color.blue, "bottom left");
            AssertColor(readback.GetPixel(source.width * 3 / 4, source.height / 4), Color.yellow, "bottom right");
        }
        catch
        {
            File.WriteAllBytes(Path.ChangeExtension(_resultPath, ".failure.png"), readback.EncodeToPNG());
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            UnityEngine.Object.DestroyImmediate(readback);
        }
    }

    static void AssertColor(Color actual, Color expected, string quadrant)
    {
        if (Mathf.Abs(actual.r - expected.r) > 0.2f || Mathf.Abs(actual.g - expected.g) > 0.2f ||
            Mathf.Abs(actual.b - expected.b) > 0.2f || actual.a < 0.8f)
            throw new InvalidOperationException(quadrant + " color mismatch: " + actual + " expected " + expected);
    }

    static string Argument(string key)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, key);
        if (i < 0 || i + 1 == args.Length)
            throw new ArgumentException("Missing " + key);
        return args[i + 1];
    }

    static void Finish(Exception error)
    {
        if (_finished) return;
        _finished = true;
        EditorApplication.update -= Tick;
        _result = _result ?? new Result();
        try
        {
            // Exercise disposal while render events from TryUpdateTexture may
            // still be queued. The native context is retired by ordered event 3.
            _output?.Dispose();
            _media?.Dispose();
            _library?.Dispose();
        }
        catch (Exception cleanupError) { error = error ?? cleanupError; }
        _result.success = error == null;
        _result.error = error?.ToString();
        string json = JsonUtility.ToJson(_result, true);
        if (!string.IsNullOrEmpty(_resultPath)) File.WriteAllText(_resultPath, json);
        UnityEngine.Debug.Log("VLC_VALIDATION " + json);
        // Allow the queued render-thread cleanup to execute before editor exit.
        int remainingUpdates = 4;
        EditorApplication.CallbackFunction exit = null;
        exit = () =>
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (--remainingUpdates > 0) return;
            EditorApplication.update -= exit;
            EditorApplication.Exit(_result.success ? 0 : 1);
        };
        EditorApplication.update += exit;
    }
}
