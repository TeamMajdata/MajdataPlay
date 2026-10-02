using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

namespace LibVLCSharp
{
    /// <summary>
    /// Owns a VLC player and its Unity presentation texture. Construct, poll IsReady,
    /// update and dispose on Unity's main thread. Wait for IsReady before Player.Play.
    /// The player must not be disposed separately or have its output callbacks replaced.
    /// </summary>
    public sealed class VlcVideoOutput : IDisposable
    {
        const string Plugin = "VLCUnityPlugin";
        const int Initializing = 4;
        readonly int _mainThread = Thread.CurrentThread.ManagedThreadId;
        readonly Vector2 _nativeScale;
        readonly Vector2 _nativeOffset;
        VlcCpuVideoOutput _cpu;
        Texture2D _uploadTexture;
        Texture2D _externalTexture;
        RenderTexture _renderTexture;
        CommandBuffer _commands;
        byte[] _pixels;
        IntPtr _renderEvent;
        int _renderEventBase;
        IntPtr _renderContext;
        IntPtr _externalPointer;
        ulong _version;
        bool _ready;
        bool _disposed;
        bool _fallingBack;
        bool _restartAfterFallback;
        bool _pauseAfterFallback;
        bool _restoreAfterFallback;
        long _fallbackTime;
        string _reportedError;

        public MediaPlayer Player { get; }

        /// <summary>Unity-owned texture, safe for normal materials and RawImage.</summary>
        public Texture Texture => _cpu != null ? (Texture)_uploadTexture : _renderTexture;

        /// <summary>
        /// True when frames stay on the GPU between VLC and Unity. VLC may still
        /// select a software decoder when the codec has no hardware decoder.
        /// </summary>
        public bool UsesGpuInterop { get; private set; }

        /// <summary>Poll on the main thread before playback; initializes CPU fallback if needed.</summary>
        public bool IsReady
        {
            get
            {
                RequireMainThread();
                if (_disposed)
                    return false;
                if (!_ready)
                    SelectOutput(NativeCapabilities(Player.NativeReference));
                return _ready;
            }
        }

        /// <param name="nativeFlipX">Horizontal correction for the native output.</param>
        /// <param name="nativeFlipY">Vertical correction for the native output.</param>
        /// <param name="forceCpu">Use portable RGBA uploads even when GPU sharing is available.</param>
        public VlcVideoOutput(LibVLC library, bool nativeFlipX = true,
            bool nativeFlipY = true, bool forceCpu = false)
        {
            if (library == null)
                throw new ArgumentNullException(nameof(library));
            _nativeScale = new Vector2(nativeFlipX ? -1 : 1, nativeFlipY ? -1 : 1);
            _nativeOffset = new Vector2(nativeFlipX ? 1 : 0, nativeFlipY ? 1 : 0);
            Player = new MediaPlayer(library);
            try
            {
                _renderEvent = NativeRenderEvent();
                _renderEventBase = NativeRenderEventBase();
                _renderContext = NativeRenderContext(Player.NativeReference);
                if (_renderEvent == IntPtr.Zero || _renderContext == IntPtr.Zero)
                    throw new InvalidOperationException("VLC graphics interop has no render callback or context.");
                if (forceCpu)
                    NativeDisableGpu(Player.NativeReference);
                int capability = forceCpu ? 0 : NativeCapabilities(Player.NativeReference);
                if (capability == Initializing)
                    QueueEvent(0);
                SelectOutput(capability);
            }
            catch
            {
                Player.Dispose();
                _cpu?.Dispose();
                if (_renderEvent != IntPtr.Zero && _renderContext != IntPtr.Zero)
                    QueueEvent(3);
                throw;
            }
        }

        void SelectOutput(int capability)
        {
            if (capability == Initializing)
                return;
            if (capability == 0)
            {
                _cpu = new VlcCpuVideoOutput(SystemInfo.maxTextureSize);
                _cpu.Attach(Player.NativeReference);
                UsesGpuInterop = false;
            }
            else if (capability == 1 || capability == 2 || capability == 3 || capability == 5)
            {
                _commands = new CommandBuffer { name = "VLC present shared video frame" };
                UsesGpuInterop = true;
            }
            else
                throw new InvalidOperationException("Unknown VLC graphics interop capability: " + capability);
            _ready = true;
        }

        /// <summary>Upload/present the newest available frame. Safe while paused or stopped.</summary>
        public bool TryUpdateTexture()
        {
            RequireMainThread();
            if (_disposed || !IsReady)
                return false;
            if (_cpu != null)
            {
                RestorePlaybackAfterFallback();
                return UpdateCpuTexture();
            }

            // Some interop extensions can only be tested after VLC creates its
            // first surface. A failed import must restart with portable callbacks.
            if (_fallingBack || NativeCapabilities(Player.NativeReference) == 0)
            {
                ContinueCpuFallback();
                return false;
            }

            // OpenGL imports must happen with Unity's context current. New surface
            // generations become available on a subsequent main-thread update.
            QueueEvent(0);
            uint width, height;
            ulong version;
            var pointer = NativeTextureInfo(Player.NativeReference, out width, out height, out version);
            if (pointer == IntPtr.Zero || width == 0 || height == 0)
                return false;
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                throw new InvalidOperationException("VLC texture exceeds this graphics device's size limit.");

            bool updated = _externalPointer != pointer || _version != version;
            if (!updated)
                return false; // Paused/low-FPS video must not stall the GPU every Unity frame.
            if (_externalTexture == null || _externalTexture.width != width || _externalTexture.height != height)
            {
                DestroyTexture(_externalTexture);
                DestroyTexture(_renderTexture);
                _externalTexture = Texture2D.CreateExternalTexture((int)width, (int)height,
                    TextureFormat.RGBA32, false, true, pointer);
                _externalTexture.wrapMode = TextureWrapMode.Clamp;
                _externalTexture.filterMode = FilterMode.Bilinear;
                _renderTexture = new RenderTexture((int)width, (int)height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "VLC video",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                _renderTexture.Create();
            }
            else if (_externalPointer != pointer)
                _externalTexture.UpdateExternalTexture(pointer);

            // Keep the acquire/blit/release commands together on Unity's render
            // thread. Only the resulting Unity-owned RT is exposed to consumers.
            _commands.Clear();
            _commands.IssuePluginEventAndData(_renderEvent, _renderEventBase + 1, _renderContext);
            _commands.Blit(_externalTexture, _renderTexture, _nativeScale, _nativeOffset);
            _commands.IssuePluginEventAndData(_renderEvent, _renderEventBase + 2, _renderContext);
            Graphics.ExecuteCommandBuffer(_commands);
            _externalPointer = pointer;
            _version = version;
            return updated;
        }

        void ContinueCpuFallback()
        {
            if (!_fallingBack)
            {
                var state = Player.State;
                _fallbackTime = Math.Max(0, Player.Time);
                _pauseAfterFallback = state == VLCState.Paused;
                _restartAfterFallback = state != VLCState.NothingSpecial && state != VLCState.Stopped;
                _fallingBack = true;
                if (state != VLCState.Stopped && state != VLCState.NothingSpecial)
                    Player.Stop();
                Debug.LogWarning("[VLC] GPU texture sharing is unavailable; switching to RGBA uploads.");
            }
            var currentState = Player.State;
            if (currentState != VLCState.Stopped && currentState != VLCState.NothingSpecial)
                return; // Stop in libVLC 4 is asynchronous; never replace live callbacks.

            NativeDisableGpu(Player.NativeReference);
            SelectOutput(0);
            _fallingBack = false;
            if (_restartAfterFallback)
            {
                _restoreAfterFallback = true;
                Player.Play();
            }
        }

        void RestorePlaybackAfterFallback()
        {
            if (!_restoreAfterFallback || (Player.State != VLCState.Playing && Player.State != VLCState.Paused))
                return;
            _restoreAfterFallback = false;
            if (_fallbackTime > 0)
                Player.SetTime(_fallbackTime, false);
            if (_pauseAfterFallback)
                Player.SetPause(true);
        }

        bool UpdateCpuTexture()
        {
            string error = _cpu.Error;
            if (error != null && error != _reportedError)
            {
                _reportedError = error;
                Debug.LogError("[VLC] Video output: " + error);
            }
            int width, height;
            if (!_cpu.TryCopyFrame(ref _pixels, out width, out height))
                return false;
            if (_uploadTexture == null || _uploadTexture.width != width || _uploadTexture.height != height)
            {
                DestroyTexture(_uploadTexture);
                // CPU output contains sRGB encoded video samples.
                _uploadTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    name = "VLC uploaded video",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }
            _uploadTexture.LoadRawTextureData(_pixels);
            _uploadTexture.Apply(false, false);
            return true;
        }

        public void Dispose()
        {
            RequireMainThread();
            if (_disposed)
                return;
            _disposed = true;
            // Dispose/release joins native output threads. Stop() in VLC 4 only
            // requests an asynchronous stop and cannot justify freeing callbacks.
            Player.Dispose();
            _cpu?.Dispose();
            // Native release retains the context and its surfaces. Event 3 retires
            // them on the render thread after all queued acquire/blit/release work.
            QueueEvent(3);
            _commands?.Dispose();
            DestroyTexture(_uploadTexture);
            DestroyTexture(_externalTexture);
            DestroyTexture(_renderTexture);
            _uploadTexture = _externalTexture = null;
            _renderTexture = null;
            _pixels = null;
        }

        void RequireMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread)
                throw new InvalidOperationException("VlcVideoOutput must be used on its Unity main thread.");
        }

        void QueueEvent(int eventId)
        {
            using (var commands = new CommandBuffer { name = "VLC graphics lifecycle" })
            {
                commands.IssuePluginEventAndData(_renderEvent, _renderEventBase + eventId, _renderContext);
                Graphics.ExecuteCommandBuffer(commands);
            }
        }

        static void DestroyTexture(Texture texture)
        {
            if (texture == null)
                return;
#if UNITY_EDITOR
            // Edit-mode previews have no end-of-frame Object.Destroy processing.
            // Native surfaces remain owned by the interop context until event 3;
            // disposing the Unity wrapper does not release that native ownership.
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return;
            }
#endif
            UnityEngine.Object.Destroy(texture);
        }

        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_get_capabilities")]
        static extern int NativeCapabilities(IntPtr player);
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_get_texture_info")]
        static extern IntPtr NativeTextureInfo(IntPtr player, out uint width, out uint height, out ulong version);
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_get_render_event_func")]
        static extern IntPtr NativeRenderEvent();
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_get_render_event_base")]
        static extern int NativeRenderEventBase();
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_get_render_context")]
        static extern IntPtr NativeRenderContext(IntPtr player);
        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_disable_gpu")]
        static extern void NativeDisableGpu(IntPtr player);
    }
}
