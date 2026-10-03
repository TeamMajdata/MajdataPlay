using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MajdataPlay.FFmpeg.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using FFmpeg.AutoGen;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>
    /// Optional native surface interoperability. A capability is reported only
    /// after Unity has provided the actual graphics device to the native plug-in.
    /// All methods except AcquireD3D11Device must run on Unity's main thread.
    /// </summary>
    internal sealed class HardwareVideoPresenter : IDisposable
    {
        const int D3D11Capability = 1, MetalCapability = 2, D3D12Capability = 4, WglCapability = 8, WindowsVulkanCapability = 16;
        const int LinuxVulkanCapability = 32, AndroidVulkanCapability = 64;
        const int D3D12VideoCapability = 128, VulkanVideoDecodeCapability = 256;
        const int D3DDecodeCapabilities = D3D11Capability | D3D12Capability | WglCapability | WindowsVulkanCapability;
        const int SubmitD3D11 = 1, CompleteMetal = 2, Drain = 3, PrepareD3D12 = 4, SubmitD3D12 = 5,
            SubmitWgl = 6, CompleteWgl = 7, DestroyWgl = 8, SubmitVulkan = 9, ReleaseVulkan = 10, SubmitNativeVulkan = 11,
            PrepareNativeD3D12 = 12, SubmitNativeD3D12 = 13;
        static readonly int ChromaId = Shader.PropertyToID("_FFUChroma");
        static readonly int TransformId = Shader.PropertyToID("_FFUTransform");
        static readonly int ColorId = Shader.PropertyToID("_FFUColor");
        IntPtr _d3d11, _callback, _d3d11Output, _sharedSurface;
        IntPtr _vulkan;
        readonly int _backend;
        readonly int _capabilities;
        IntPtr _nativeD3D12;
        RenderTexture _output, _copyTarget;
        Texture2D _nativeOutput;
        Texture2D _luma, _chroma;
        Material _material;
        CommandBuffer _commands;
        static readonly List<KeyValuePair<IntPtr, Texture2D>> RetiredWglTextures = new List<KeyValuePair<IntPtr, Texture2D>>();
        static bool _vulkanRetirementPending;
        bool _disposed;
        public string TransferMode { get; private set; }
        public static string AvailabilityReason { get; private set; }

        public static bool SupportsD3D11 => (Capabilities & D3DDecodeCapabilities) != 0;
        public static bool SupportsMetal => (Capabilities & MetalCapability) != 0;
        public static bool SupportsLinuxVulkan => (Capabilities & LinuxVulkanCapability) != 0;
        public static bool SupportsAndroidVulkan => (Capabilities & AndroidVulkanCapability) != 0;
        internal static bool SupportsD3D12Video => (Capabilities & D3D12VideoCapability) != 0;
        internal static bool SupportsVulkanVideoDecoding => (Capabilities & VulkanVideoDecodeCapability) != 0;
        public static bool IsAvailable => Capabilities != 0;
        static int Capabilities
        {
            get
            {
                var api = SystemInfo.graphicsDeviceType;
                bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
                bool vulkanHost = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor ||
                    Application.platform == RuntimePlatform.Android;
                if (!(vulkanHost && api == GraphicsDeviceType.Vulkan) && api != GraphicsDeviceType.Metal && !(windows && (api == GraphicsDeviceType.Direct3D11 ||
                    api == GraphicsDeviceType.Direct3D12 || api == GraphicsDeviceType.OpenGLCore || api == GraphicsDeviceType.Vulkan)))
                {
                    AvailabilityReason = "Native FFmpeg interoperability is not implemented for " + SystemInfo.graphicsDeviceType + ".";
                    return 0;
                }
                try
                {
#if UNITY_IOS && !UNITY_EDITOR
                    Native.ffu_register_ios();
#endif
                    int abi = Native.ffu_abi_version();
                    int expectedAbi = windows || vulkanHost ? 4 : 2;
                    if (abi != expectedAbi)
                    {
                        AvailabilityReason = "The FFmpeg Unity bridge ABI does not match this player (version " + expectedAbi + " required). Restart Unity after replacing the native library.";
                        return 0;
                    }
                    int capabilities = Native.ffu_capabilities();
                    AvailabilityReason = capabilities == 0
                        ? DescribeInitializationFailure(Native.ffu_initialization_status())
                        : null;
                    return capabilities;
                }
                catch (DllNotFoundException error) { AvailabilityReason = error.Message; return 0; }
                catch (EntryPointNotFoundException error) { AvailabilityReason = error.Message; return 0; }
                catch (BadImageFormatException error) { AvailabilityReason = error.Message; return 0; }
            }
        }

        static string DescribeInitializationFailure(int status)
        {
            switch (status)
            {
                case 1: return "Unity has not initialized the FFmpeg native graphics bridge.";
                case 2: return "The FFmpeg bridge could not resolve Unity's native graphics interface.";
                case 3: return "The active Unity graphics device has no implemented native FFmpeg backend.";
                case 4: return "Unity's native D3D11 device interface is unavailable.";
                case 5: return "Unity has not supplied a D3D11 graphics device.";
                case 6: return "Unity's D3D11 immediate context cannot be protected for threaded video decoding.";
                case 7: return "Unity Metal V2 or the Core Video Metal texture cache is unavailable.";
                case 8: return "Unity's D3D12 V8 native interface is unavailable.";
                case 9: return "Unity has not supplied a D3D12 graphics device.";
                case 10: return "The Vulkan sharing extensions were not enabled before device creation. Enable Preload for the graphics bridge and restart Unity.";
                case 100: return "Vulkan sharing extensions are unavailable or were not enabled before device creation. Enable Preload and restart Unity.";
                case 101: return "Unity's Vulkan graphics queue cannot run the video conversion shader.";
                case 200: return "Unity's Vulkan GPU has no accessible matching /dev/dri/renderD* device.";
                case 201: return "The loaded Linux FFmpeg was built without VAAPI. Rebuild using Tools/FFmpeg.";
                case 202: return "The VAAPI driver could not open the DRM device used by Unity's Vulkan GPU.";
                case 300: return "Android's AHardwareBuffer image reader is unavailable; API 26 or newer is required.";
                default: return "The FFmpeg graphics bridge could not initialize video interoperability (status 0x" + status.ToString("X8") + ").";
            }
        }

        // Does not call SystemInfo: this is passed directly to the decoder worker.
        // The returned COM reference belongs to libavutil's D3D11VA device context.
        public static IntPtr AcquireD3D11Device() => Native.ffu_d3d11_acquire_device();
        internal static IntPtr AcquireD3D12Device()
        {
            var device = Native.ffu_d3d12va_acquire_device();
            if (device == IntPtr.Zero)
                throw new NotSupportedException("The native D3D12VA device is unavailable (status 0x" + Native.ffu_d3d12va_status().ToString("X8") + ").");
            return device;
        }

        internal static string DescribeNativeBackendStatus(GraphicsDeviceType api)
        {
            try
            {
                if (Capabilities == 0 && AvailabilityReason != null) return AvailabilityReason;
                return api == GraphicsDeviceType.Direct3D12
                    ? "D3D12VA status 0x" + Native.ffu_d3d12va_status().ToString("X8")
                    : VulkanVideoInterop.DescribeError(VulkanVideoInterop.VideoStatus());
            }
            catch (DllNotFoundException error) { return error.Message; }
            catch (EntryPointNotFoundException error) { return error.Message; }
            catch (BadImageFormatException error) { return error.Message; }
        }

        public HardwareVideoPresenter()
        {
            CollectRetiredWglTextures();
            _capabilities = Capabilities;
            _backend = _capabilities & ~(D3D12VideoCapability | VulkanVideoDecodeCapability);
            if (_capabilities == 0) throw new NotSupportedException(AvailabilityReason);
            var shader = Resources.Load<Shader>("FFmpegVideoPlanes");
            if (shader == null || !shader.isSupported)
                throw new NotSupportedException("The FFmpeg video conversion shader is unavailable.");
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _callback = Native.ffu_render_callback();
            _commands = new CommandBuffer { name = "FFmpeg video surface conversion" };
        }

        void Event(int id, IntPtr data) => _commands.IssuePluginEventAndData(_callback, Native.ffu_event_id(id), data);

        public void CheckErrors()
        {
            CollectRetiredWglTextures();
            if (_nativeD3D12 != IntPtr.Zero && Native.ffu_d3d12va_error(_nativeD3D12) != 0)
                throw new NotSupportedException("D3D12VA GPU presentation failed (native 0x" + Native.ffu_d3d12va_error(_nativeD3D12).ToString("X8") + ").");
            if (_vulkan != IntPtr.Zero && VulkanVideoInterop.Error(_vulkan) != 0)
                throw new NotSupportedException(VulkanVideoInterop.DescribeError(VulkanVideoInterop.Error(_vulkan)));
            int error = _d3d11 == IntPtr.Zero ? 0 : Native.ffu_d3d11_error(_d3d11);
            int sharingError = _sharedSurface == IntPtr.Zero ? 0 : Native.ffu_shared_error(_sharedSurface);
            if (error != 0 || sharingError != 0)
                throw new NotSupportedException("FFmpeg " + SystemInfo.graphicsDeviceType + " GPU interoperability failed (native 0x" +
                    error.ToString("X8") + ", sharing " + sharingError + ").");
        }

        internal static bool CollectRetiredWglTextures()
        {
            for (int i = RetiredWglTextures.Count - 1; i >= 0; --i)
                if (Native.ffu_wgl_retirement_poll(RetiredWglTextures[i].Key) != 0)
                {
                    UnityEngine.Object.Destroy(RetiredWglTextures[i].Value);
                    RetiredWglTextures.RemoveAt(i);
                }
            return RetiredWglTextures.Count == 0;
        }

        internal static bool CollectRetiredTextures()
        {
            bool wglDone = CollectRetiredWglTextures();
            if (_vulkanRetirementPending && VulkanVideoInterop.PollRetiredFrames() == 0)
                _vulkanRetirementPending = false;
            return wglDone && !_vulkanRetirementPending;
        }

        public bool TryPresent(DecodedVideoFrame frame, out Texture texture)
        {
            texture = null;
            if (_disposed || !frame.IsHardwareFrame) return false;
            int rotation = ((int)Math.Round(frame.RotationDegrees / 90.0) % 4 + 4) % 4;
            int width = (rotation & 1) == 0 ? frame.Width : frame.Height;
            int height = (rotation & 1) == 0 ? frame.Height : frame.Width;
            EnsureTarget(ref _output, width, height, "FFmpeg video output");
            _commands.Clear();
            // Native surfaces have top-left origins. Rotate from Unity's bottom-left
            // UV convention before sampling the retained decoded surface.
            _commands.SetGlobalVector(TransformId, new Vector4(rotation, 0, 0, 0));
            if (frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_D3D12)
            {
                if ((_capabilities & D3D12VideoCapability) == 0) return false;
                if (_nativeD3D12 == IntPtr.Zero) _nativeD3D12 = Native.ffu_d3d12va_create();
                if (_nativeD3D12 == IntPtr.Zero) return false;
                CheckErrors();
                EnsureCopyTarget(frame.Width, frame.Height);
                IntPtr packet = Native.ffu_d3d12va_prepare(_nativeD3D12, frame.NativeFrame, _copyTarget.GetNativeTexturePtr());
                if (packet == IntPtr.Zero) { CheckErrors(); return false; }
                try
                {
                    Event(PrepareNativeD3D12, packet);
                    Event(SubmitNativeD3D12, packet);
                    _commands.Blit(_copyTarget, _output, _material, 1);
                    Graphics.ExecuteCommandBuffer(_commands);
                    packet = IntPtr.Zero;
                }
                finally { if (packet != IntPtr.Zero) Native.ffu_d3d12va_cancel(packet); }
                TransferMode = "D3D12VA native decode + GPU conversion (no CPU readback)";
            }
            else if (frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_D3D11)
            {
                if ((_backend & D3DDecodeCapabilities) == 0) return false;
                if (_d3d11 == IntPtr.Zero) _d3d11 = Native.ffu_d3d11_create();
                if (_d3d11 == IntPtr.Zero) return false;
                CheckErrors();
                bool d3d12 = _backend == D3D12Capability;
                if (!EnsureWindowsOutput(frame.Width, frame.Height)) return false;
                IntPtr packet = d3d12
                    ? Native.ffu_d3d12_prepare(_d3d11, frame.NativeFrame, _copyTarget.GetNativeTexturePtr())
                    : _backend == D3D11Capability
                        ? Native.ffu_d3d11_prepare(_d3d11, frame.NativeFrame, _d3d11Output)
                        : Native.ffu_shared_prepare(_d3d11, frame.NativeFrame, _d3d11Output, _sharedSurface,
                            _copyTarget == null ? IntPtr.Zero : _copyTarget.GetNativeTexturePtr());
                if (packet == IntPtr.Zero) return false;
                try
                {
                    if (d3d12) Event(PrepareD3D12, packet);
                    Event(d3d12 ? SubmitD3D12 : _backend == WglCapability ? SubmitWgl :
                        _backend == WindowsVulkanCapability ? SubmitVulkan : SubmitD3D11, packet);
                    _commands.Blit(_copyTarget != null ? (Texture)_copyTarget : _nativeOutput, _output, _material, 1);
                    if (_backend == WglCapability) Event(CompleteWgl, _sharedSurface);
                    Graphics.ExecuteCommandBuffer(_commands);
                    // The render callback now owns the packet and the AVFrame clone.
                    packet = IntPtr.Zero;
                }
                finally
                {
                    if (packet != IntPtr.Zero)
                    {
                        if (d3d12) Native.ffu_d3d12_cancel(packet);
                        else Native.ffu_packet_cancel(packet);
                    }
                }
                TransferMode = _backend == D3D12Capability ? "D3D12 GPU conversion + shared-resource copy (no CPU readback)" :
                    _backend == WglCapability ? "OpenGL shared RGBA + GPU conversion (no CPU readback)" :
                    _backend == WindowsVulkanCapability ? "Vulkan GPU conversion + shared-resource copy (no CPU readback)" :
                    "D3D11 GPU conversion (no CPU readback)";
            }
            else if (frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_VULKAN ||
                (_backend & (LinuxVulkanCapability | AndroidVulkanCapability)) != 0)
            {
                if (_vulkan == IntPtr.Zero) _vulkan = VulkanVideoInterop.Create();
                if (_vulkan == IntPtr.Zero) return false;
                CheckErrors();
                EnsureCopyTarget(frame.Width, frame.Height);
                IntPtr packet = VulkanVideoInterop.Prepare(_vulkan, frame, _copyTarget.GetNativeTexturePtr());
                if (packet == IntPtr.Zero) { CheckErrors(); return false; }
                try
                {
                    Event(SubmitNativeVulkan, packet);
                    _commands.Blit(_copyTarget, _output, _material, 1);
                    Graphics.ExecuteCommandBuffer(_commands);
                    packet = IntPtr.Zero;
                }
                finally { if (packet != IntPtr.Zero) VulkanVideoInterop.Cancel(packet); }
                TransferMode = frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_VULKAN
                    ? "Vulkan Video native decode + GPU conversion (no CPU readback)"
                    : _backend == AndroidVulkanCapability
                    ? "Android MediaCodec AHardwareBuffer + Vulkan GPU conversion (no CPU readback)"
                    : "Linux VAAPI DMA-BUF + Vulkan GPU conversion (no CPU readback)";
            }
            else
            {
                if (Native.ffu_metal_prepare(frame.NativeFrame, out var planes) == 0) return false;
                IntPtr packet = planes.Packet;
                try
                {
                    UpdateExternal(ref _luma, planes.Width, planes.Height, TextureFormat.R8, planes.Luma, "FFmpeg Metal luma");
                    UpdateExternal(ref _chroma, planes.ChromaWidth, planes.ChromaHeight, TextureFormat.RG16, planes.Chroma, "FFmpeg Metal chroma");
                    _commands.SetGlobalTexture(ChromaId, _chroma);
                    _commands.SetGlobalVector(ColorId, new Vector4(planes.FullRange, planes.Matrix709, 0, 0));
                    _commands.Blit(_luma, _output, _material, 0);
                    // Must follow the blit in the SAME command buffer. The native
                    // packet is retired only when this Metal command buffer finishes.
                    Event(CompleteMetal, packet);
                    Graphics.ExecuteCommandBuffer(_commands);
                    packet = IntPtr.Zero;
                }
                finally { if (packet != IntPtr.Zero) Native.ffu_packet_cancel(packet); }
                TransferMode = "Metal zero-copy NV12 planes + GPU color conversion";
            }
            texture = _output;
            return true;
        }

        bool EnsureWindowsOutput(int width, int height)
        {
            if (_backend == D3D12Capability)
            {
                EnsureCopyTarget(width, height);
                return true;
            }
            if (_backend == WglCapability && RetiredWglTextures.Count >= 24)
                throw new NotSupportedException("The graphics driver has not released prior OpenGL video surfaces.");
            if (_nativeOutput != null && _nativeOutput.width == width && _nativeOutput.height == height) return true;
            if (_backend == WindowsVulkanCapability && _copyTarget != null && _copyTarget.width == width &&
                _copyTarget.height == height && _sharedSurface != IntPtr.Zero) return true;
            IntPtr native = Native.ffu_d3d11_create_output(_d3d11, width, height);
            if (native == IntPtr.Zero) return false;
            ReleaseWindowsOutput();
            _d3d11Output = native;
            if (_backend == WindowsVulkanCapability)
            {
                EnsureCopyTarget(width, height);
                _sharedSurface = Native.ffu_shared_surface_create(_d3d11, native, 0);
                return _sharedSurface != IntPtr.Zero;
            }
            // Typed UNORM is accepted by the video processor; shader pass 1 performs
            // explicit gamma decoding in Linear projects (an sRGB SRV is not used).
            _nativeOutput = _backend == WglCapability
                ? new Texture2D(width, height, TextureFormat.RGBA32, false, true)
                : Texture2D.CreateExternalTexture(width, height, TextureFormat.RGBA32, false, true, native);
            _nativeOutput.name = "FFmpeg shared converted surface";
            _nativeOutput.hideFlags = HideFlags.HideAndDontSave;
            _nativeOutput.filterMode = FilterMode.Bilinear;
            _nativeOutput.wrapMode = TextureWrapMode.Clamp;
            if (_backend == WglCapability)
            {
                _nativeOutput.Apply(false, true);
                uint glName = unchecked((uint)_nativeOutput.GetNativeTexturePtr().ToInt64());
                _sharedSurface = Native.ffu_shared_surface_create(_d3d11, native, glName);
                return _sharedSurface != IntPtr.Zero;
            }
            return true;
        }

        void EnsureCopyTarget(int width, int height)
        {
            if (_copyTarget != null && _copyTarget.width == width && _copyTarget.height == height && _copyTarget.IsCreated()) return;
            if (_copyTarget != null) UnityEngine.Object.Destroy(_copyTarget);
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0, msaaSamples = 1, volumeDepth = 1, mipCount = 1,
                dimension = TextureDimension.Tex2D, useMipMap = false, autoGenerateMips = false
            };
            _copyTarget = new RenderTexture(descriptor) { name = "FFmpeg GPU copy target",
                hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!_copyTarget.Create()) throw new NotSupportedException("Could not allocate the FFmpeg GPU copy target.");
        }

        void ReleaseWindowsOutput()
        {
            if (_sharedSurface != IntPtr.Zero)
            {
                IntPtr releaseData = _sharedSurface;
                if (_backend == WglCapability)
                {
                    releaseData = Native.ffu_wgl_retirement_create(_sharedSurface);
                    // Keep the Unity GL name alive even if the driver cannot unregister
                    // it. A failed retirement retains a bounded number of textures.
                    RetiredWglTextures.Add(new KeyValuePair<IntPtr, Texture2D>(releaseData, _nativeOutput));
                    HardwareTextureRetirementPump.Ensure();
                    _nativeOutput = null;
                }
                // Runs after all queued sampling and before Unity retires the texture.
                using (var cleanup = new CommandBuffer { name = "FFmpeg shared surface release" })
                {
                    cleanup.IssuePluginEventAndData(_callback, Native.ffu_event_id(_backend == WglCapability ? DestroyWgl : ReleaseVulkan), releaseData);
                    Graphics.ExecuteCommandBuffer(cleanup);
                }
                _sharedSurface = IntPtr.Zero;
            }
            if (_nativeOutput != null) { UnityEngine.Object.Destroy(_nativeOutput); _nativeOutput = null; }
            if (_d3d11Output != IntPtr.Zero) { Native.ffu_d3d11_release_output(_d3d11Output); _d3d11Output = IntPtr.Zero; }
        }

        static void EnsureTarget(ref RenderTexture texture, int width, int height, string name)
        {
            if (texture != null && texture.width == width && texture.height == height && texture.IsCreated()) return;
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name, hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                useMipMap = false, autoGenerateMips = false, antiAliasing = 1
            };
            if (!texture.Create()) throw new NotSupportedException("Could not allocate the FFmpeg video render target.");
        }
        static void UpdateExternal(ref Texture2D texture, int width, int height, TextureFormat format, IntPtr native, string name)
        {
            if (texture != null && (texture.width != width || texture.height != height))
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
            if (texture == null)
                texture = Texture2D.CreateExternalTexture(width, height, format, false, true, native);
            else texture.UpdateExternalTexture(native);
            texture.name = name;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
        }

        public void Dispose()
        {
            if (_nativeD3D12 != IntPtr.Zero) { Native.ffu_d3d12va_release(_nativeD3D12); _nativeD3D12 = IntPtr.Zero; }
            if (_disposed) return;
            _disposed = true;
            if (_commands != null)
            {
                // Final decode surfaces can outlive the MonoBehaviour. Queue a drain
                // after all sampling, then allow native ownership to finish the work.
                _commands.Clear();
                ReleaseWindowsOutput();
                Event(Drain, IntPtr.Zero);
                Graphics.ExecuteCommandBuffer(_commands);
                _commands.Dispose();
                _commands = null;
            }
            if (_d3d11 != IntPtr.Zero) { Native.ffu_d3d11_release(_d3d11); _d3d11 = IntPtr.Zero; }
            if (_vulkan != IntPtr.Zero)
            {
                VulkanVideoInterop.Release(_vulkan); _vulkan = IntPtr.Zero;
                _vulkanRetirementPending = true;
                HardwareTextureRetirementPump.Ensure();
            }
            if (_luma != null) UnityEngine.Object.Destroy(_luma);
            if (_chroma != null) UnityEngine.Object.Destroy(_chroma);
            if (_copyTarget != null) UnityEngine.Object.Destroy(_copyTarget);
            if (_output != null) UnityEngine.Object.Destroy(_output);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MetalPlanes
        {
            public IntPtr Packet, Luma, Chroma;
            public int Width, Height, ChromaWidth, ChromaHeight, FullRange, Matrix709;
        }
        static class Native
        {
#if UNITY_IOS && !UNITY_EDITOR
            const string Library = "__Internal";
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_register_ios();
#else
            const string Library = "FFmpegUnityBridge";
#endif
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_abi_version();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_event_id(int id);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_capabilities();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_initialization_status();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_render_callback();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d11_acquire_device();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d11_create();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d11_create_output(IntPtr presenter, int width, int height);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_d3d11_release_output(IntPtr texture);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_d3d11_release(IntPtr presenter);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_d3d11_error(IntPtr presenter);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d11_prepare(IntPtr presenter, IntPtr frame, IntPtr target);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d12_prepare(IntPtr presenter, IntPtr frame, IntPtr target);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_d3d12_cancel(IntPtr packet);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d12va_acquire_device();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_d3d12va_status();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d12va_create();
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_d3d12va_release(IntPtr presenter);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_d3d12va_prepare(IntPtr presenter, IntPtr frame, IntPtr target);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_d3d12va_cancel(IntPtr packet);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_d3d12va_error(IntPtr presenter);
#else
            internal static IntPtr ffu_d3d12va_acquire_device() => IntPtr.Zero;
            internal static int ffu_d3d12va_status() => -1;
            internal static IntPtr ffu_d3d12va_create() => IntPtr.Zero;
            internal static void ffu_d3d12va_release(IntPtr presenter) { }
            internal static IntPtr ffu_d3d12va_prepare(IntPtr presenter, IntPtr frame, IntPtr target) => IntPtr.Zero;
            internal static void ffu_d3d12va_cancel(IntPtr packet) { }
            internal static int ffu_d3d12va_error(IntPtr presenter) => -1;
#endif
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_shared_surface_create(IntPtr presenter, IntPtr texture, uint glName);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_shared_prepare(IntPtr presenter, IntPtr frame, IntPtr texture, IntPtr surface, IntPtr target);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_shared_error(IntPtr surface);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr ffu_wgl_retirement_create(IntPtr surface);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_wgl_retirement_poll(IntPtr ticket);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int ffu_metal_prepare(IntPtr frame, out MetalPlanes planes);
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void ffu_packet_cancel(IntPtr packet);
        }
    }
}
