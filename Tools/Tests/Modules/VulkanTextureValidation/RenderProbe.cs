#nullable enable
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MajdataPlay.Validation.VulkanTexture
{
    /// <summary>Uploads persistent CPU RGBA pixels without media libraries or native graphics plug-ins.</summary>
    public sealed class RenderProbe : MonoBehaviour
    {
        /// <summary>Owns the readable RGBA32 texture.</summary>
        private Texture2D? _texture;
        /// <summary>Owns the fullscreen quad mesh without requiring the physics module.</summary>
        private Mesh? _mesh;
        /// <summary>Owns the unlit sampling material.</summary>
        private Material? _material;
        /// <summary>Keeps the upload array pinned until the probe is destroyed.</summary>
        private GCHandle _pin;
        /// <summary>Stores the persistent, tightly packed source pixels.</summary>
        private byte[] _pixels = Array.Empty<byte>();
        /// <summary>Stores the pinned source address passed to LoadRawTextureData.</summary>
        private IntPtr _pointer;
        /// <summary>Stores command-line arguments once during initialization.</summary>
        private string[] _arguments = Array.Empty<string>();
        /// <summary>Stores the configured texture width.</summary>
        private int _width;
        /// <summary>Stores the configured texture height.</summary>
        private int _height;
        /// <summary>Stores the configured upload frequency.</summary>
        private double _uploadHz;
        /// <summary>Stores the configured run duration in seconds.</summary>
        private double _seconds;
        /// <summary>Disables updates after the initial upload when true.</summary>
        private bool _static;
        /// <summary>Stops work after completion or a failure.</summary>
        private bool _finished;
        /// <summary>Stores the experiment start time.</summary>
        private double _started;
        /// <summary>Stores the next upload deadline.</summary>
        private double _nextUpload;
        /// <summary>Stores the start time of the current reporting window.</summary>
        private double _lastReport;
        /// <summary>Counts Unity Update callbacks in the current reporting window.</summary>
        private long _frames;
        /// <summary>Counts uploads during the entire experiment.</summary>
        private long _uploads;
        /// <summary>Stores the upload count at the previous report.</summary>
        private long _reportedUploads;
        /// <summary>Accumulates CPU time inside LoadRawTextureData and Apply in the current window.</summary>
        private double _uploadCpuSeconds;

        /// <summary>Creates the probe after the isolated scene is loaded.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            new GameObject("RenderProbe").AddComponent<RenderProbe>();
        }

        /// <summary>Parses options and creates the persistent upload and display resources.</summary>
        private void Start()
        {
            try
            {
                _arguments = Environment.GetCommandLineArgs();
                _width = ReadInt("-probe-width", 854, 1, SystemInfo.maxTextureSize);
                _height = ReadInt("-probe-height", 480, 1, SystemInfo.maxTextureSize);
                _uploadHz = ReadInt("-probe-upload-hz", 30, 1, 1000);
                _seconds = ReadInt("-probe-seconds", 180, 1, 86400);
                _static = ReadInt("-probe-static", 0, 0, 1) != 0;
                QualitySettings.vSyncCount = ReadInt("-probe-vsync", 1, 0, 4);
                Application.targetFrameRate = ReadInt("-probe-fps", 144, 1, 1000);
                Application.runInBackground = true;
                _pixels = new byte[checked(_width * _height * 4)];
                for (var i = 0; i < _pixels.Length; i += 4)
                {
                    _pixels[i] = 24;
                    _pixels[i + 1] = 48;
                    _pixels[i + 2] = 72;
                    _pixels[i + 3] = 255;
                }

                _pin = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
                _pointer = _pin.AddrOfPinnedObject();
                _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false, false)
                {
                    name = "[TextureProbe] RGBA32",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var shader = Resources.Load<Shader>("RenderProbe");
                if (shader == null || !shader.isSupported)
                {
                    throw new NotSupportedException("The isolated unlit texture shader is unavailable or unsupported.");
                }

                _material = new Material(shader)
                {
                    mainTexture = _texture
                };
                _mesh = new Mesh
                {
                    name = "RenderProbe quad",
                    vertices = new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                        new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0)
                    },
                    uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                    triangles = new[] { 0, 2, 1, 2, 3, 1 }
                };
                var quad = new GameObject("RenderProbe fullscreen quad");
                quad.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = quad.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                LogConfiguration();
                Upload();
                _reportedUploads = _uploads;
                _uploadCpuSeconds = 0;
                _started = Time.realtimeSinceStartupAsDouble;
                _lastReport = _started;
                _nextUpload = _started + (1.0 / _uploadHz);
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>Uploads at most once per rendered Unity frame and reports progress every ten seconds.</summary>
        private void Update()
        {
            if (_finished)
            {
                return;
            }

            try
            {
                var now = Time.realtimeSinceStartupAsDouble;
                _frames++;
                if (now - _started >= _seconds)
                {
                    Report(now, "DONE");
                    _finished = true;
                    Application.Quit(0);
                    return;
                }

                if (!_static && now >= _nextUpload)
                {
                    ChangeStripe();
                    Upload();
                    // Skip missed slots instead of flooding a recovered frame with uploads.
                    var period = 1.0 / _uploadHz;
                    _nextUpload += (Math.Floor((now - _nextUpload) / period) + 1.0) * period;
                }

                if (now - _lastReport >= 10.0)
                {
                    Report(now, "PROGRESS");
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        /// <summary>Changes eight source rows with bounded CPU work and no temporary allocations.</summary>
        private void ChangeStripe()
        {
            var firstRow = (int)((_uploads * 8) % _height);
            var endRow = Math.Min(_height, firstRow + 8);
            var red = (byte)(_uploads * 17);
            var green = (byte)(_uploads * 31);
            for (var row = firstRow; row < endRow; row++)
            {
                var end = (row + 1) * _width * 4;
                for (var offset = row * _width * 4; offset < end; offset += 4)
                {
                    _pixels[offset] = red;
                    _pixels[offset + 1] = green;
                    _pixels[offset + 2] = (byte)(255 - red);
                }
            }
        }

        /// <summary>Matches the production IntPtr upload overload and preserves readable CPU texture storage.</summary>
        private void Upload()
        {
            var before = Stopwatch.GetTimestamp();
            _texture!.LoadRawTextureData(_pointer, _pixels.Length);
            _texture.Apply(false, false);
            _uploadCpuSeconds += (Stopwatch.GetTimestamp() - before) / (double)Stopwatch.Frequency;
            _uploads++;
        }

        /// <summary>Logs the actual device, renderer, Unity version, quality state, and display refresh rate.</summary>
        private void LogConfiguration()
        {
            var resolution = Screen.currentResolution;
            Debug.Log("[TextureProbe] START Unity=" + Application.unityVersion
                + "; backend=" + SystemInfo.graphicsDeviceType + "; GPU=" + SystemInfo.graphicsDeviceName
                + "; vendor=" + SystemInfo.graphicsDeviceVendor + "; driver=" + SystemInfo.graphicsDeviceVersion
                + "; texture=" + _width + "x" + _height + "; uploadHz=" + _uploadHz
                + "; static=" + _static + "; seconds=" + _seconds);
            Debug.Log("[TextureProbe] SETTINGS quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]
                + "; vSyncCount=" + QualitySettings.vSyncCount + "; targetFrameRate=" + Application.targetFrameRate
                + "; activeColorSpace=" + QualitySettings.activeColorSpace + "; antiAliasing=" + QualitySettings.antiAliasing
                + "; display=" + resolution.width + "x" + resolution.height
                + "; displayRefreshHz=" + resolution.refreshRateRatio.value.ToString("F3", CultureInfo.InvariantCulture)
                + "; window=" + Screen.width + "x" + Screen.height + "; fullscreen=" + Screen.fullScreenMode
                + "; threadingMode=" + SystemInfo.renderingThreadingMode + "; graphicsMultiThreaded=" + SystemInfo.graphicsMultiThreaded
                + "; disableGraphicsJobsArgument=" + (Array.IndexOf(_arguments, "-gfx-disable-graphics-jobs") >= 0)
                + "; pipeline=" + (GraphicsSettings.currentRenderPipeline == null ? "Built-in" : "Unexpected SRP"));
        }

        /// <summary>Logs measured Unity frame and upload rates without GPU readback.</summary>
        /// <param name="now">The monotonic time at the end of this reporting interval.</param>
        /// <param name="status">The progress or completion status.</param>
        private void Report(double now, string status)
        {
            var elapsed = now - _lastReport;
            var uploads = _uploads - _reportedUploads;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[TextureProbe] {0} elapsed={1:F3}s fps={2:F3} uploadsPerSecond={3:F3} uploads={4} uploadCpuMs={5:F3}",
                status, now - _started, _frames / elapsed, uploads / elapsed, _uploads,
                uploads == 0 ? 0 : (_uploadCpuSeconds * 1000.0 / uploads)));
            _lastReport = now;
            _reportedUploads = _uploads;
            _frames = 0;
            _uploadCpuSeconds = 0;
        }

        /// <summary>Reads a bounded integer option in separated or attached form.</summary>
        /// <param name="key">The command-line option prefix.</param>
        /// <param name="fallback">The value used when the option is absent.</param>
        /// <param name="minimum">The smallest permitted value.</param>
        /// <param name="maximum">The largest permitted value.</param>
        /// <returns>The parsed value or the default.</returns>
        /// <exception cref="ArgumentException">An option is missing a value or falls outside its permitted range.</exception>
        private int ReadInt(string key, int fallback, int minimum, int maximum)
        {
            for (var i = 0; i < _arguments.Length; i++)
            {
                var argument = _arguments[i];
                if (!argument.StartsWith(key, StringComparison.Ordinal))
                {
                    continue;
                }

                var text = argument.Length == key.Length
                    ? (i + 1 < _arguments.Length ? _arguments[i + 1] : "")
                    : argument.Substring(key.Length).TrimStart('=');
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                    || value < minimum || value > maximum)
                {
                    throw new ArgumentException(key + " requires an integer in [" + minimum + ", " + maximum + "].");
                }

                return value;
            }

            return fallback;
        }

        /// <summary>Reports a terminal failure and requests a nonzero process exit.</summary>
        /// <param name="error">The initialization or upload exception.</param>
        private void Fail(Exception error)
        {
            _finished = true;
            Debug.LogError("[TextureProbe] FAIL " + error);
            Application.Quit(1);
        }

        /// <summary>Releases the pin after the last synchronous source copy and retires Unity-owned objects.</summary>
        private void OnDestroy()
        {
            if (_pin.IsAllocated)
            {
                _pin.Free();
            }

            if (_texture != null)
            {
                Destroy(_texture);
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
            }

            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
