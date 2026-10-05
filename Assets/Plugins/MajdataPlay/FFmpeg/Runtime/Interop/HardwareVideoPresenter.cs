#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg.Internal;
using UnityEngine;
using UnityEngine.Rendering;
using FFmpeg.AutoGen;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>
    /// Optional native surface interoperability. A capability is reported only
    /// after Unity has provided the actual graphics device to the native plug-in.
    /// Presentation and capability queries run on Unity's main thread.
    /// Device acquisition callbacks run on the decoder worker and do not access Unity APIs.
    /// </summary>
    internal sealed class HardwareVideoPresenter : IDisposable
    {
        /// <summary>Define native capability bits for D3D11, Metal, D3D12 sharing, WGL sharing, and Windows Vulkan sharing, respectively.</summary>
        private const int D3D11Capability = 1, MetalCapability = 2, D3D12Capability = 4, WglCapability = 8, WindowsVulkanCapability = 16;
        /// <summary>Define capability bits for Linux DMA-BUF and Android AHardwareBuffer Vulkan transport.</summary>
        private const int LinuxVulkanCapability = 32, AndroidVulkanCapability = 64;
        /// <summary>Define capability bits for native D3D12VA and Vulkan Video decoding.</summary>
        private const int D3D12VideoCapability = 128, VulkanVideoDecodeCapability = 256;
        /// <summary>Combines presentation backends capable of consuming D3D11 decoded frames.</summary>
        private const int D3DDecodeCapabilities = D3D11Capability | D3D12Capability | WglCapability | WindowsVulkanCapability;
        /// <summary>Define bridge event offsets for frame submission, preparation, completion, and resource retirement on the render thread.</summary>
        private const int SubmitD3D11 = 1, CompleteMetal = 2, Drain = 3, PrepareD3D12 = 4,
            SubmitD3D12 = 5, SubmitWgl = 6, CompleteWgl = 7, DestroyWgl = 8, SubmitVulkan = 9, ReleaseVulkan = 10,
            SubmitNativeVulkan = 11, PrepareNativeD3D12 = 12, SubmitNativeD3D12 = 13;
        /// <summary>Caches the shader property identifier for the chroma plane.</summary>
        private static readonly int s_chromaId = Shader.PropertyToID("_FFUChroma");
        /// <summary>Caches the shader property identifier for frame rotation and UV transformation.</summary>
        private static readonly int s_transformId = Shader.PropertyToID("_FFUTransform");
        /// <summary>Caches the shader property identifier for color range and matrix selection.</summary>
        private static readonly int s_colorId = Shader.PropertyToID("_FFUColor");
        /// <summary>Store the native D3D11 presenter, render callback, output resource, and cross-API shared surface handles.</summary>
        private IntPtr _d3d11, _callback, _d3d11Output, _sharedSurface;
        /// <summary>Owns the native Vulkan presenter handle.</summary>
        private IntPtr _vulkan;
        /// <summary>Selects the native presentation transport, excluding independent video-decoder capability bits.</summary>
        private readonly int _backend;
        /// <summary>Caches bridge capabilities when this presenter is created.</summary>
        private readonly int _capabilities;
        /// <summary>Owns the native D3D12VA presenter handle.</summary>
        private IntPtr _nativeD3D12;
        /// <summary>Caches the Unity copy target's native graphics resource pointer.</summary>
        private IntPtr _copyTargetNative;
        /// <summary>Stores the base of the native bridge's reserved render-event range.</summary>
        private readonly int _eventBase;
        /// <summary>Own the displayed render texture and intermediate GPU copy target, respectively.</summary>
        private RenderTexture? _output, _copyTarget;
        /// <summary>Wraps native D3D11 output or owns a registered OpenGL texture.</summary>
        private Texture2D? _nativeOutput;
        /// <summary>Wrap the borrowed Metal luma and chroma plane textures.</summary>
        private Texture2D? _luma, _chroma;
        /// <summary>Owns the material used for YUV conversion and output transformation.</summary>
        private Material _material;
        /// <summary>Owns the reusable main-thread command buffer, or null after disposal; submission requires a live presenter.</summary>
        private CommandBuffer? _commands;
        /// <summary>Keeps Unity GL textures alive until the driver unregisters their native shared surfaces.</summary>
        private static readonly List<KeyValuePair<IntPtr, Texture2D>> s_retiredWglTextures = new List<KeyValuePair<IntPtr, Texture2D>>();
        /// <summary>Indicates that released Vulkan frames still require native fence polling.</summary>
        private static bool s_vulkanRetirementPending;
        /// <summary>Prevents submission after native presentation resources have been released.</summary>
        private bool _disposed;
        /// <summary>Gets the last successful transfer description, or null before the first presentation.</summary>
        public string? TransferMode { get; private set; }
        /// <summary>Gets the reason the native graphics bridge is unavailable, or null when available.</summary>
        public static string? AvailabilityReason { get; private set; }
        /// <summary>Gets whether the bridge can present D3D11 decoded frames on Unity's current graphics backend.</summary>
        public static bool SupportsD3D11 => (Capabilities & D3DDecodeCapabilities) != 0;
        /// <summary>Gets whether the bridge can share VideoToolbox planes with Metal.</summary>
        public static bool SupportsMetal => (Capabilities & MetalCapability) != 0;
        /// <summary>Gets whether the bridge supports VAAPI DMA-BUF import into Vulkan.</summary>
        public static bool SupportsLinuxVulkan => (Capabilities & LinuxVulkanCapability) != 0;
        /// <summary>Gets whether the bridge supports MediaCodec AHardwareBuffer import into Vulkan.</summary>
        public static bool SupportsAndroidVulkan => (Capabilities & AndroidVulkanCapability) != 0;
        /// <summary>Gets whether native D3D12VA decoding is available on Unity's device.</summary>
        internal static bool SupportsD3D12Video => (Capabilities & D3D12VideoCapability) != 0;
        /// <summary>Gets whether Vulkan Video decoding was negotiated on Unity's device.</summary>
        internal static bool SupportsVulkanVideoDecoding => (Capabilities & VulkanVideoDecodeCapability) != 0;
        /// <summary>Gets whether the current graphics device exposes any supported native transport.</summary>
        public static bool IsAvailable => Capabilities != 0;

        /// <summary>Gets supported bridge capability bits after checking the graphics backend and bridge ABI.</summary>
        private static int Capabilities
        {
            get
            {
                var api = SystemInfo.graphicsDeviceType;
                bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
                bool vulkanHost = Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor
                    || Application.platform == RuntimePlatform.Android;
                if (!(vulkanHost && api == GraphicsDeviceType.Vulkan) && api != GraphicsDeviceType.Metal
                    && !(windows && (api == GraphicsDeviceType.Direct3D11 || api == GraphicsDeviceType.Direct3D12
                    || api == GraphicsDeviceType.OpenGLCore || api == GraphicsDeviceType.Vulkan)))
                {
                    AvailabilityReason = "Native FFmpeg interoperability is not implemented for " + SystemInfo.graphicsDeviceType + ".";
                    return 0;
                }

                try
                {
#if UNITY_IOS && !UNITY_EDITOR
                    Native.FfuRegisterIos();
#endif
                    int abi = Native.FfuAbiVersion();
                    int expectedAbi = windows || vulkanHost ? 4 : 2;
                    if (abi != expectedAbi)
                    {
                        AvailabilityReason = "The FFmpeg Unity bridge ABI does not match this player (version "
                            + expectedAbi + " required). Restart Unity after replacing the native library.";
                        return 0;
                    }

                    int capabilities = Native.FfuCapabilities();
                    AvailabilityReason = capabilities == 0 ? DescribeInitializationFailure(Native.FfuInitializationStatus()) : null;
                    return capabilities;
                }
                catch (DllNotFoundException error)
                {
                    AvailabilityReason = error.Message;
                    return 0;
                }
                catch (EntryPointNotFoundException error)
                {
                    AvailabilityReason = error.Message;
                    return 0;
                }
                catch (BadImageFormatException error)
                {
                    AvailabilityReason = error.Message;
                    return 0;
                }
            }
        }

        /// <summary>Converts a native bridge initialization status into an actionable diagnostic.</summary>
        /// <param name="status">The native initialization status code to describe.</param>
        /// <returns>An actionable description of the native initialization failure.</returns>
        private static string DescribeInitializationFailure(int status)
        {
            switch (status)
            {
                case 1:
                    return "Unity has not initialized the FFmpeg native graphics bridge.";
                case 2:
                    return "The FFmpeg bridge could not resolve Unity's native graphics interface.";
                case 3:
                    return "The active Unity graphics device has no implemented native FFmpeg backend.";
                case 4:
                    return "Unity's native D3D11 device interface is unavailable.";
                case 5:
                    return "Unity has not supplied a D3D11 graphics device.";
                case 6:
                    return "Unity's D3D11 immediate context cannot be protected for threaded video decoding.";
                case 7:
                    return "Unity Metal V2 or the Core Video Metal texture cache is unavailable.";
                case 8:
                    return "Unity's D3D12 V8 native interface is unavailable.";
                case 9:
                    return "Unity has not supplied a D3D12 graphics device.";
                case 10:
                    return "The Vulkan sharing extensions were not enabled before device creation. Enable Preload for the graphics bridge and restart Unity.";
                case 100:
                    return "Vulkan sharing extensions are unavailable or were not enabled before device creation. Enable Preload and restart Unity.";
                case 101:
                    return "Unity's Vulkan graphics queue cannot run the video conversion shader.";
                case 200:
                    return "Unity's Vulkan GPU has no accessible matching /dev/dri/renderD* device.";
                case 201:
                    return "The loaded Linux FFmpeg was built without VAAPI. Rebuild using Tools/FFmpeg.";
                case 202:
                    return "The VAAPI driver could not open the DRM device used by Unity's Vulkan GPU.";
                case 300:
                    return "Android's AHardwareBuffer image reader is unavailable; API 26 or newer is required.";
                default:
                    return "The FFmpeg graphics bridge could not initialize video interoperability (status 0x" + status.ToString("X8") + ").";
            }
        }

        // Does not call SystemInfo: this is passed directly to the decoder worker.
        // The returned COM reference belongs to libavutil's D3D11VA device context.
        /// <summary>Acquires Unity's D3D11 device for the decoder worker without accessing Unity APIs.</summary>
        /// <returns>An added COM device reference owned by the caller, or zero when unavailable.</returns>
        public static IntPtr AcquireD3D11Device() => Native.FfuD3D11AcquireDevice();
        /// <summary>Acquires an FFmpeg D3D12VA device sharing Unity's graphics device.</summary>
        /// <returns>A newly owned FFmpeg AVBufferRef for the shared D3D12VA device.</returns>
        /// <exception cref="NotSupportedException">The native bridge cannot acquire a D3D12VA device.</exception>
        internal static IntPtr AcquireD3D12Device()
        {
            var device = Native.FfuD3D12VAAcquireDevice();
            if (device == IntPtr.Zero)
            {
                throw new NotSupportedException("The native D3D12VA device is unavailable (status 0x" + Native.FfuD3D12VAStatus().ToString("X8") + ").");
            }

            return device;
        }

        /// <summary>Reports the active graphics backend's native decoding availability or loading failure.</summary>
        /// <param name="api">The Unity graphics API whose native decoding availability is queried.</param>
        /// <returns>The backend status or native loading failure description.</returns>
        internal static string DescribeNativeBackendStatus(GraphicsDeviceType api)
        {
            try
            {
                if (Capabilities == 0 && AvailabilityReason != null)
                {
                    return AvailabilityReason;
                }

                return api == GraphicsDeviceType.Direct3D12 ? "D3D12VA status 0x" + Native.FfuD3D12VAStatus().ToString("X8")
                    : VulkanVideoInterop.DescribeError(VulkanVideoInterop.VideoStatus());
            }
            catch (DllNotFoundException error)
            {
                return error.Message;
            }
            catch (EntryPointNotFoundException error)
            {
                return error.Message;
            }
            catch (BadImageFormatException error)
            {
                return error.Message;
            }
        }

        /// <summary>Initializes main-thread GPU conversion resources and bridge event dispatch.</summary>
        /// <exception cref="NotSupportedException">The bridge is unavailable or the conversion shader is missing or unsupported.</exception>
        public HardwareVideoPresenter()
        {
            CollectRetiredWglTextures();
            _capabilities = Capabilities;
            _backend = _capabilities & ~(D3D12VideoCapability | VulkanVideoDecodeCapability);
            if (_capabilities == 0)
            {
                throw new NotSupportedException(AvailabilityReason);
            }

            var shader = Resources.Load<Shader>("FFmpegVideoPlanes");
            if (shader == null || !shader.isSupported)
            {
                throw new NotSupportedException("The FFmpeg video conversion shader is unavailable.");
            }

            _material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _callback = Native.FfuRenderCallback();
            // The bridge reserves one contiguous event range for its lifetime.
            _eventBase = Native.FfuEventId(0);
            _commands = new CommandBuffer
            {
                name = "FFmpeg video surface conversion"
            };
        }

        /// <summary>Records a native bridge event in the current Unity command buffer.</summary>
        /// <param name="id">The native render-event offset within the bridge's reserved event range.</param>
        /// <param name="data">The native data pointer passed to the operation.</param>
        private void Event(int id, IntPtr data) => _commands!.IssuePluginEventAndData(_callback, _eventBase + id, data);
        /// <summary>Submits the current presentation command buffer to Unity's graphics queue.</summary>
        private void SubmitCommands()
        {
            using var profile = UnityProfiler.Create("FFmpeg.Interop.SubmitCommands");
            Graphics.ExecuteCommandBuffer(_commands!);
        }

        /// <summary>Collects completed retirements and reports asynchronous GPU presentation failures.</summary>
        /// <exception cref="NotSupportedException">The native presenter reports a GPU conversion or cross-API sharing failure.</exception>
        public void CheckErrors()
        {
            CollectRetiredWglTextures();
            if (_nativeD3D12 != IntPtr.Zero && Native.FfuD3D12VAError(_nativeD3D12) != 0)
            {
                throw new NotSupportedException("D3D12VA GPU presentation failed (native 0x" + Native.FfuD3D12VAError(_nativeD3D12).ToString("X8") + ").");
            }

            if (_vulkan != IntPtr.Zero && VulkanVideoInterop.Error(_vulkan) != 0)
            {
                throw new NotSupportedException(VulkanVideoInterop.DescribeError(VulkanVideoInterop.Error(_vulkan)));
            }

            int error = _d3d11 == IntPtr.Zero ? 0 : Native.FfuD3D11Error(_d3d11);
            int sharingError = _sharedSurface == IntPtr.Zero ? 0 : Native.FfuSharedError(_sharedSurface);
            if (error != 0 || sharingError != 0)
            {
                throw new NotSupportedException("FFmpeg " + SystemInfo.graphicsDeviceType
                    + " GPU interoperability failed (native 0x" + error.ToString("X8") + ", sharing " + sharingError
                    + ").");
            }
        }

        /// <summary>Destroys Unity GL textures only after native interop registrations have been released.</summary>
        /// <returns>True when no registered GL textures remain awaiting retirement.</returns>
        internal static bool CollectRetiredWglTextures()
        {
            for (int i = s_retiredWglTextures.Count - 1; i >= 0; --i)
            {
                if (Native.FfuWglRetirementPoll(s_retiredWglTextures[i].Key) != 0)
                {
                    UnityEngine.Object.Destroy(s_retiredWglTextures[i].Value);
                    s_retiredWglTextures.RemoveAt(i);
                }
            }

            return s_retiredWglTextures.Count == 0;
        }

        /// <summary>Polls GL registrations and Vulkan fences for resources released by closed players.</summary>
        /// <returns>True when all GL and Vulkan retirements are complete.</returns>
        internal static bool CollectRetiredTextures()
        {
            bool wglDone = CollectRetiredWglTextures();
            if (s_vulkanRetirementPending && VulkanVideoInterop.PollRetiredFrames() == 0)
            {
                s_vulkanRetirementPending = false;
            }

            return wglDone && !s_vulkanRetirementPending;
        }

        /// <summary>Records GPU conversion and sharing commands for a borrowed decoded frame.</summary>
        /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
        /// <param name="texture">The texture resource to use or update.</param>
        /// <returns>True when GPU presentation was submitted and texture identifies the output; otherwise false with a null texture.</returns>
        public bool TryPresent(DecodedVideoFrame frame, out Texture? texture)
        {
            using var profile = UnityProfiler.Create("FFmpeg.Interop.TryPresent");
            texture = null;
            if (_disposed || !frame.IsHardwareFrame)
            {
                return false;
            }

            int rotation = ((int)Math.Round(frame.RotationDegrees / 90.0) % 4 + 4) % 4;
            int width = (rotation & 1) == 0 ? frame.Width : frame.Height;
            int height = (rotation & 1) == 0 ? frame.Height : frame.Width;
            EnsureTarget(ref _output, width, height, "FFmpeg video output");
            _commands!.Clear();
            // Native surfaces have top-left origins. Rotate from Unity's bottom-left
            // UV convention before sampling the retained decoded surface.
            _commands!.SetGlobalVector(s_transformId, new Vector4(rotation, 0, 0, 0));
            if (frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_D3D12)
            {
                if ((_capabilities & D3D12VideoCapability) == 0)
                {
                    return false;
                }

                if (_nativeD3D12 == IntPtr.Zero)
                {
                    _nativeD3D12 = Native.FfuD3D12VACreate();
                }

                if (_nativeD3D12 == IntPtr.Zero)
                {
                    return false;
                }

                CheckErrors();
                EnsureCopyTarget(frame.Width, frame.Height);
                IntPtr packet;
                using (UnityProfiler.Create("FFmpeg.Interop.PrepareD3D12VA"))
                {
                    packet = Native.FfuD3D12VAPrepare(_nativeD3D12, frame.NativeFrame, _copyTargetNative);
                }

                if (packet == IntPtr.Zero)
                {
                    CheckErrors();
                    return false;
                }

                try
                {
                    using (UnityProfiler.Create("FFmpeg.Interop.RecordCommands"))
                    {
                        Event(PrepareNativeD3D12, packet);
                        Event(SubmitNativeD3D12, packet);
                        _commands!.Blit(_copyTarget, _output, _material, 1);
                    }

                    SubmitCommands();
                    packet = IntPtr.Zero;
                }
                finally
                {
                    if (packet != IntPtr.Zero)
                    {
                        Native.FfuD3D12VACancel(packet);
                    }
                }

                TransferMode = "D3D12VA native decode + GPU conversion (no CPU readback)";
            }
            else if (frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_D3D11)
            {
                if ((_backend & D3DDecodeCapabilities) == 0)
                {
                    return false;
                }

                if (_d3d11 == IntPtr.Zero)
                {
                    _d3d11 = Native.FfuD3D11Create();
                }

                if (_d3d11 == IntPtr.Zero)
                {
                    return false;
                }

                CheckErrors();
                bool d3d12 = _backend == D3D12Capability;
                if (!EnsureWindowsOutput(frame.Width, frame.Height))
                {
                    return false;
                }

                IntPtr packet;
                using (UnityProfiler.Create("FFmpeg.Interop.PrepareD3D11"))
                {
                    packet = d3d12 ? Native.FfuD3D12Prepare(_d3d11, frame.NativeFrame, _copyTargetNative)
                        : _backend == D3D11Capability ? Native.FfuD3D11Prepare(_d3d11, frame.NativeFrame, _d3d11Output)
                        : Native.FfuSharedPrepare(_d3d11, frame.NativeFrame, _d3d11Output, _sharedSurface, _copyTargetNative);
                }

                if (packet == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    using (UnityProfiler.Create("FFmpeg.Interop.RecordCommands"))
                    {
                        if (d3d12)
                        {
                            Event(PrepareD3D12, packet);
                        }

                        Event(d3d12 ? SubmitD3D12 : _backend == WglCapability ? SubmitWgl : _backend == WindowsVulkanCapability ? SubmitVulkan : SubmitD3D11, packet);
                        _commands!.Blit(_copyTarget != null ? (Texture)_copyTarget : _nativeOutput, _output, _material, 1);
                        if (_backend == WglCapability)
                        {
                            Event(CompleteWgl, _sharedSurface);
                        }
                    }

                    SubmitCommands();
                    // The render callback now owns the packet and the AVFrame clone.
                    packet = IntPtr.Zero;
                }
                finally
                {
                    if (packet != IntPtr.Zero)
                    {
                        if (d3d12)
                        {
                            Native.FfuD3D12Cancel(packet);
                        }
                        else
                        {
                            Native.FfuPacketCancel(packet);
                        }
                    }
                }

                TransferMode = _backend == D3D12Capability ? "D3D12 GPU conversion + shared-resource copy (no CPU readback)"
                    : _backend == WglCapability ? "OpenGL shared RGBA + GPU conversion (no CPU readback)"
                    : _backend == WindowsVulkanCapability ? "Vulkan GPU conversion + shared-resource copy (no CPU readback)"
                    : "D3D11 GPU conversion (no CPU readback)";
            }
            else if (frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_VULKAN || (_backend & (LinuxVulkanCapability | AndroidVulkanCapability)) != 0)
            {
                if (_vulkan == IntPtr.Zero)
                {
                    _vulkan = VulkanVideoInterop.Create();
                }

                if (_vulkan == IntPtr.Zero)
                {
                    return false;
                }

                CheckErrors();
                EnsureCopyTarget(frame.Width, frame.Height);
                IntPtr packet;
                using (UnityProfiler.Create("FFmpeg.Interop.PrepareVulkan"))
                {
                    packet = VulkanVideoInterop.Prepare(_vulkan, frame, _copyTargetNative);
                }

                if (packet == IntPtr.Zero)
                {
                    CheckErrors();
                    return false;
                }

                try
                {
                    using (UnityProfiler.Create("FFmpeg.Interop.RecordCommands"))
                    {
                        Event(SubmitNativeVulkan, packet);
                        _commands!.Blit(_copyTarget, _output, _material, 1);
                    }

                    SubmitCommands();
                    packet = IntPtr.Zero;
                }
                finally
                {
                    if (packet != IntPtr.Zero)
                    {
                        VulkanVideoInterop.Cancel(packet);
                    }
                }

                TransferMode = frame.PixelFormat == AVPixelFormat.AV_PIX_FMT_VULKAN ? "Vulkan Video native decode + GPU conversion (no CPU readback)"
                    : _backend == AndroidVulkanCapability ? "Android MediaCodec AHardwareBuffer + Vulkan GPU conversion (no CPU readback)"
                    : "Linux VAAPI DMA-BUF + Vulkan GPU conversion (no CPU readback)";
            }
            else
            {
                MetalPlanes planes;
                using (UnityProfiler.Create("FFmpeg.Interop.PrepareMetal"))
                {
                    if (Native.FfuMetalPrepare(frame.NativeFrame, out planes) == 0)
                    {
                        return false;
                    }
                }

                IntPtr packet = planes.Packet;
                try
                {
                    using (UnityProfiler.Create("FFmpeg.Interop.RecordCommands"))
                    {
                        UpdateExternal(ref _luma, planes.Width, planes.Height, TextureFormat.R8, planes.Luma, "FFmpeg Metal luma");
                        UpdateExternal(ref _chroma, planes.ChromaWidth, planes.ChromaHeight, TextureFormat.RG16, planes.Chroma, "FFmpeg Metal chroma");
                        _commands!.SetGlobalTexture(s_chromaId, _chroma);
                        _commands!.SetGlobalVector(s_colorId, new Vector4(planes.FullRange, planes.Matrix709, 0, 0));
                        _commands!.Blit(_luma, _output, _material, 0);
                        // Must follow the blit in the SAME command buffer. The native
                        // packet is retired only when this Metal command buffer finishes.
                        Event(CompleteMetal, packet);
                    }

                    SubmitCommands();
                    packet = IntPtr.Zero;
                }
                finally
                {
                    if (packet != IntPtr.Zero)
                    {
                        Native.FfuPacketCancel(packet);
                    }
                }

                TransferMode = "Metal zero-copy NV12 planes + GPU color conversion";
            }

            texture = _output;
            return true;
        }

        /// <summary>Creates or reuses the D3D11 output and any cross-API sharing resources.</summary>
        /// <param name="width">The requested frame or texture width in pixels.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <returns>True if compatible output resources are available; otherwise false.</returns>
        /// <exception cref="NotSupportedException">Too many registered OpenGL surfaces remain awaiting driver retirement.</exception>
        private bool EnsureWindowsOutput(int width, int height)
        {
            if (_backend == D3D12Capability)
            {
                EnsureCopyTarget(width, height);
                return true;
            }

            if (_backend == WglCapability && s_retiredWglTextures.Count >= 24)
            {
                throw new NotSupportedException("The graphics driver has not released prior OpenGL video surfaces.");
            }

            if (_nativeOutput != null && _nativeOutput.width == width && _nativeOutput.height == height)
            {
                return true;
            }

            if (_backend == WindowsVulkanCapability && _copyTarget != null && _copyTarget.width == width && _copyTarget.height == height && _sharedSurface != IntPtr.Zero)
            {
                EnsureCopyTarget(width, height);
                return true;
            }

            IntPtr native = Native.FfuD3D11CreateOutput(_d3d11, width, height);
            if (native == IntPtr.Zero)
            {
                return false;
            }

            ReleaseWindowsOutput();
            _d3d11Output = native;
            if (_backend == WindowsVulkanCapability)
            {
                EnsureCopyTarget(width, height);
                _sharedSurface = Native.FfuSharedSurfaceCreate(_d3d11, native, 0);
                return _sharedSurface != IntPtr.Zero;
            }

            // Typed UNORM is accepted by the video processor; shader pass 1 performs
            // explicit gamma decoding in Linear projects (an sRGB SRV is not used).
            _nativeOutput = _backend == WglCapability ? new Texture2D(width, height, TextureFormat.RGBA32,
                false, true) : Texture2D.CreateExternalTexture(width, height, TextureFormat.RGBA32, false,
                true, native);
            _nativeOutput.name = "FFmpeg shared converted surface";
            _nativeOutput.hideFlags = HideFlags.HideAndDontSave;
            _nativeOutput.filterMode = FilterMode.Bilinear;
            _nativeOutput.wrapMode = TextureWrapMode.Clamp;
            if (_backend == WglCapability)
            {
                _nativeOutput.Apply(false, true);
                uint glName = unchecked((uint)_nativeOutput.GetNativeTexturePtr().ToInt64());
                _sharedSurface = Native.FfuSharedSurfaceCreate(_d3d11, native, glName);
                return _sharedSurface != IntPtr.Zero;
            }

            return true;
        }

        /// <summary>Creates or reuses a GPU copy target and refreshes its native pointer after resource loss.</summary>
        /// <param name="width">The requested frame or texture width in pixels.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <exception cref="NotSupportedException">Unity cannot create a usable GPU copy target or return its native resource pointer.</exception>
        private void EnsureCopyTarget(int width, int height)
        {
            if (_copyTarget != null && _copyTarget.width == width && _copyTarget.height == height && _copyTarget.IsCreated() && _copyTargetNative != IntPtr.Zero)
            {
                return;
            }

            using var profile = UnityProfiler.Create("FFmpeg.Interop.CreateCopyTarget");
            _copyTargetNative = IntPtr.Zero;
            if (_copyTarget != null)
            {
                UnityEngine.Object.Destroy(_copyTarget);
            }

            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                depthBufferBits = 0,
                msaaSamples = 1,
                volumeDepth = 1,
                mipCount = 1,
                dimension = TextureDimension.Tex2D,
                useMipMap = false,
                autoGenerateMips = false
            };
            _copyTarget = new RenderTexture(descriptor)
            {
                name = "FFmpeg GPU copy target",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!_copyTarget.Create())
            {
                throw new NotSupportedException("Could not allocate the FFmpeg GPU copy target.");
            }

            // GetNativeTexturePtr synchronizes with the render thread. Resolve it
            // only on creation, including after size changes or render-target loss.
            _copyTargetNative = _copyTarget.GetNativeTexturePtr();
            if (_copyTargetNative == IntPtr.Zero)
            {
                throw new NotSupportedException("The FFmpeg GPU copy target has no native texture.");
            }
        }

        /// <summary>Schedules shared output retirement after pending GPU sampling and releases ownership handles.</summary>
        private void ReleaseWindowsOutput()
        {
            if (_sharedSurface != IntPtr.Zero)
            {
                IntPtr releaseData = _sharedSurface;
                if (_backend == WglCapability)
                {
                    releaseData = Native.FfuWglRetirementCreate(_sharedSurface);
                    // A WGL shared surface always has a corresponding Unity texture.
                    // Keep the Unity GL name alive even if the driver cannot unregister
                    // it. A failed retirement retains a bounded number of textures.
                    s_retiredWglTextures.Add(new KeyValuePair<IntPtr, Texture2D>(releaseData, _nativeOutput!));
                    HardwareTextureRetirementPump.Ensure();
                    _nativeOutput = null;
                }

                // Runs after all queued sampling and before Unity retires the texture.
                using (var cleanup = new CommandBuffer
                {
                    name = "FFmpeg shared surface release"
                })
                {
                    cleanup.IssuePluginEventAndData(_callback, _eventBase + (_backend == WglCapability ? DestroyWgl : ReleaseVulkan), releaseData);
                    Graphics.ExecuteCommandBuffer(cleanup);
                }

                _sharedSurface = IntPtr.Zero;
            }

            if (_nativeOutput != null)
            {
                UnityEngine.Object.Destroy(_nativeOutput);
                _nativeOutput = null;
            }

            if (_d3d11Output != IntPtr.Zero)
            {
                Native.FfuD3D11ReleaseOutput(_d3d11Output);
                _d3d11Output = IntPtr.Zero;
            }
        }

        /// <summary>Creates or reuses an output render texture of the required dimensions.</summary>
        /// <param name="texture">The texture resource to use or update.</param>
        /// <param name="width">The requested frame or texture width in pixels.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <param name="name">The diagnostic name assigned to the Unity texture.</param>
        /// <exception cref="NotSupportedException">Unity cannot create the requested render texture.</exception>
        private static void EnsureTarget(ref RenderTexture? texture, int width, int height, string name)
        {
            if (texture != null && texture.width == width && texture.height == height && texture.IsCreated())
            {
                return;
            }

            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }

            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            if (!texture.Create())
            {
                throw new NotSupportedException("Could not allocate the FFmpeg video render target.");
            }
        }

        /// <summary>Creates or rebinds a Unity texture wrapper for a borrowed native plane.</summary>
        /// <param name="texture">The texture resource to use or update.</param>
        /// <param name="width">The requested frame or texture width in pixels.</param>
        /// <param name="height">The requested frame or texture height in pixels.</param>
        /// <param name="format">The pixel format exposed by the external texture wrapper.</param>
        /// <param name="native">The borrowed native graphics resource pointer to wrap.</param>
        /// <param name="name">The diagnostic name assigned to the Unity texture.</param>
        private static void UpdateExternal(ref Texture2D? texture, int width, int height, TextureFormat format, IntPtr native, string name)
        {
            if (texture != null && (texture.width != width || texture.height != height))
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }

            if (texture == null)
            {
                texture = Texture2D.CreateExternalTexture(width, height, format, false, true, native);
                texture.name = name;
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
            }
            else
            {
                texture.UpdateExternalTexture(native);
            }
        }

        /// <summary>Stops presentation and releases resources, deferring native retirement until GPU work completes.</summary>
        public void Dispose()
        {
            if (_nativeD3D12 != IntPtr.Zero)
            {
                Native.FfuD3D12VARelease(_nativeD3D12);
                _nativeD3D12 = IntPtr.Zero;
            }

            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_commands != null)
            {
                // Final decode surfaces can outlive the MonoBehaviour. Queue a drain
                // after all sampling, then allow native ownership to finish the work.
                _commands!.Clear();
                ReleaseWindowsOutput();
                Event(Drain, IntPtr.Zero);
                Graphics.ExecuteCommandBuffer(_commands!);
                _commands!.Dispose();
                // Disposed command buffers are never submitted again.
                _commands = null;
            }

            if (_d3d11 != IntPtr.Zero)
            {
                Native.FfuD3D11Release(_d3d11);
                _d3d11 = IntPtr.Zero;
            }

            if (_vulkan != IntPtr.Zero)
            {
                VulkanVideoInterop.Release(_vulkan);
                _vulkan = IntPtr.Zero;
                s_vulkanRetirementPending = true;
                HardwareTextureRetirementPump.Ensure();
            }

            if (_luma != null)
            {
                UnityEngine.Object.Destroy(_luma);
            }

            if (_chroma != null)
            {
                UnityEngine.Object.Destroy(_chroma);
            }

            if (_copyTarget != null)
            {
                UnityEngine.Object.Destroy(_copyTarget);
            }

            _copyTargetNative = IntPtr.Zero;
            if (_output != null)
            {
                UnityEngine.Object.Destroy(_output);
            }

            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
            }
        }

        /// <summary>Matches the native sequential Metal plane descriptor using pointer-sized handles and 32-bit integers.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct MetalPlanes
        {
            /// <summary>Store the owned presentation packet and its borrowed luma and chroma Metal textures.</summary>
            public IntPtr Packet, Luma, Chroma;
            /// <summary>Store luma and chroma dimensions in pixels, followed by full-range and BT.709 matrix flags.</summary>
            public int Width, Height, ChromaWidth, ChromaHeight, FullRange, Matrix709;
        }

        /// <summary>Declares Cdecl entry points and platform fallbacks for the native graphics bridge.</summary>
        private static class Native
        {
#if UNITY_IOS && !UNITY_EDITOR
            /// <summary>Names the native bridge library, or the executable's static symbols on iOS.</summary>
            private const string Library = "__Internal";
            /// <summary>Registers the statically linked iOS graphics bridge with Unity.</summary>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_register_ios")]
            internal static extern void FfuRegisterIos();
#else
            /// <summary>Names the native bridge library, or the executable's static symbols on iOS.</summary>
            private const string Library = "FFmpegUnityBridge";
#endif
            /// <summary>Returns the graphics bridge ABI version.</summary>
            /// <returns>The native bridge ABI version.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_abi_version")]
            internal static extern int FfuAbiVersion();
            /// <summary>Resolves a bridge event offset within Unity's reserved event range.</summary>
            /// <param name="id">The native render-event offset within the bridge's reserved event range.</param>
            /// <returns>The Unity event identifier for the requested bridge offset.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_event_id")]
            internal static extern int FfuEventId(int id);
            /// <summary>Returns the native graphics interoperability capability mask.</summary>
            /// <returns>The supported native transport capability mask.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_capabilities")]
            internal static extern int FfuCapabilities();
            /// <summary>Returns the graphics bridge initialization status code.</summary>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_initialization_status")]
            internal static extern int FfuInitializationStatus();
            /// <summary>Returns the native Unity render-event callback pointer.</summary>
            /// <returns>The borrowed Unity render-event callback pointer; callers must not release it.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_render_callback")]
            internal static extern IntPtr FfuRenderCallback();
            /// <summary>Acquires an added COM reference to Unity's D3D11 device.</summary>
            /// <returns>An added COM reference to Unity's D3D11 device, or zero when unavailable; ownership transfers to the caller.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d11_acquire_device")]
            internal static extern IntPtr FfuD3D11AcquireDevice();
            /// <summary>Creates an owned D3D11 video presenter.</summary>
            /// <returns>An owned presenter handle to release with FfuD3D11Release, or zero on failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d11_create")]
            internal static extern IntPtr FfuD3D11Create();
            /// <summary>Creates an owned D3D11 RGBA output resource.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="width">The requested frame or texture width in pixels.</param>
            /// <param name="height">The requested frame or texture height in pixels.</param>
            /// <returns>An owned output resource to release with FfuD3D11ReleaseOutput, or zero on failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d11_create_output")]
            internal static extern IntPtr FfuD3D11CreateOutput(IntPtr presenter, int width, int height);
            /// <summary>Releases an owned D3D11 output resource.</summary>
            /// <param name="texture">The texture resource to use or update.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d11_release_output")]
            internal static extern void FfuD3D11ReleaseOutput(IntPtr texture);
            /// <summary>Releases a D3D11 video presenter.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d11_release")]
            internal static extern void FfuD3D11Release(IntPtr presenter);
            /// <summary>Reads the last D3D11 presentation error code.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d11_error")]
            internal static extern int FfuD3D11Error(IntPtr presenter);
            /// <summary>Retains a D3D11 frame and prepares its GPU conversion packet.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d11_prepare")]
            internal static extern IntPtr FfuD3D11Prepare(IntPtr presenter, IntPtr frame, IntPtr target);
            /// <summary>Prepares a D3D11 frame for conversion and sharing into Unity's D3D12 target.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12_prepare")]
            internal static extern IntPtr FfuD3D12Prepare(IntPtr presenter, IntPtr frame, IntPtr target);
            /// <summary>Releases a D3D12 sharing packet that was never submitted.</summary>
            /// <param name="packet">The owned, unsubmitted native presentation packet to release.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12_cancel")]
            internal static extern void FfuD3D12Cancel(IntPtr packet);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            /// <summary>Acquires an owned FFmpeg D3D12VA device reference, or zero on unsupported platforms.</summary>
            /// <returns>An owned FFmpeg AVBufferRef to release with av_buffer_unref, or zero when unavailable.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_acquire_device")]
            internal static extern IntPtr FfuD3D12VAAcquireDevice();
            /// <summary>Reads D3D12VA initialization status, or returns -1 on unsupported platforms.</summary>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_status")]
            internal static extern int FfuD3D12VAStatus();
            /// <summary>Creates an owned D3D12VA presenter, or returns zero on unsupported platforms.</summary>
            /// <returns>An owned presenter handle to release with FfuD3D12VARelease, or zero when unavailable.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_create")]
            internal static extern IntPtr FfuD3D12VACreate();
            /// <summary>Releases a D3D12VA presenter; does nothing on unsupported platforms.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_release")]
            internal static extern void FfuD3D12VARelease(IntPtr presenter);
            /// <summary>Retains a D3D12VA frame for GPU presentation, or returns zero on unsupported platforms.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_prepare")]
            internal static extern IntPtr FfuD3D12VAPrepare(IntPtr presenter, IntPtr frame, IntPtr target);
            /// <summary>Releases an unsubmitted D3D12VA packet; does nothing on unsupported platforms.</summary>
            /// <param name="packet">The owned, unsubmitted native presentation packet to release.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_cancel")]
            internal static extern void FfuD3D12VACancel(IntPtr packet);
            /// <summary>Reads the D3D12VA presentation error, or returns -1 on unsupported platforms.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_d3d12va_error")]
            internal static extern int FfuD3D12VAError(IntPtr presenter);
#else
            /// <summary>Acquires an owned FFmpeg D3D12VA device reference, or zero on unsupported platforms.</summary>
            /// <returns>An owned FFmpeg AVBufferRef to release with av_buffer_unref, or zero when unavailable.</returns>
            internal static IntPtr FfuD3D12VAAcquireDevice() => IntPtr.Zero;
            /// <summary>Reads D3D12VA initialization status, or returns -1 on unsupported platforms.</summary>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            internal static int FfuD3D12VAStatus() => -1;
            /// <summary>Creates an owned D3D12VA presenter, or returns zero on unsupported platforms.</summary>
            /// <returns>An owned presenter handle to release with FfuD3D12VARelease, or zero when unavailable.</returns>
            internal static IntPtr FfuD3D12VACreate() => IntPtr.Zero;
            /// <summary>Releases a D3D12VA presenter; does nothing on unsupported platforms.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            internal static void FfuD3D12VARelease(IntPtr presenter)
            {
            }

            /// <summary>Retains a D3D12VA frame for GPU presentation, or returns zero on unsupported platforms.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            internal static IntPtr FfuD3D12VAPrepare(IntPtr presenter, IntPtr frame, IntPtr target) => IntPtr.Zero;
            /// <summary>Releases an unsubmitted D3D12VA packet; does nothing on unsupported platforms.</summary>
            /// <param name="packet">The owned, unsubmitted native presentation packet to release.</param>
            internal static void FfuD3D12VACancel(IntPtr packet)
            {
            }

            /// <summary>Reads the D3D12VA presentation error, or returns -1 on unsupported platforms.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            internal static int FfuD3D12VAError(IntPtr presenter) => -1;
#endif
            /// <summary>Creates an owned cross-API view of a D3D11 output texture.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="texture">The texture resource to use or update.</param>
            /// <param name="glName">The Unity OpenGL texture name to register, or zero for non-GL sharing.</param>
            /// <returns>An owned shared surface to retire through the matching render-thread release event, or zero on failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_shared_surface_create")]
            internal static extern IntPtr FfuSharedSurfaceCreate(IntPtr presenter, IntPtr texture, uint glName);
            /// <summary>Retains a frame for D3D11 conversion and cross-API texture sharing.</summary>
            /// <param name="presenter">The native presentation context handle.</param>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="texture">The texture resource to use or update.</param>
            /// <param name="surface">The native cross-API shared surface handle.</param>
            /// <param name="target">The borrowed native destination graphics resource owned by Unity.</param>
            /// <returns>An owned presentation packet, or zero on failure; submit it to transfer ownership to the render callback, or cancel it before submission.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_shared_prepare")]
            internal static extern IntPtr FfuSharedPrepare(IntPtr presenter, IntPtr frame, IntPtr texture, IntPtr surface, IntPtr target);
            /// <summary>Reads the last cross-API sharing error.</summary>
            /// <param name="surface">The native cross-API shared surface handle.</param>
            /// <returns>Zero on success or no error; otherwise a native status or error code.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_shared_error")]
            internal static extern int FfuSharedError(IntPtr surface);
            /// <summary>Creates a retirement ticket that keeps a WGL registration alive until safe release.</summary>
            /// <param name="surface">The native cross-API shared surface handle.</param>
            /// <returns>A retirement ticket consumed by a successful FfuWglRetirementPoll call, or zero on allocation failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_wgl_retirement_create")]
            internal static extern IntPtr FfuWglRetirementCreate(IntPtr surface);
            /// <summary>Polls a WGL retirement ticket for completed unregistration.</summary>
            /// <param name="ticket">The native WGL retirement ticket to poll.</param>
            /// <returns>Nonzero once the GL registration is retired; zero while it remains registered.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_wgl_retirement_poll")]
            internal static extern int FfuWglRetirementPoll(IntPtr ticket);
            /// <summary>Retains a VideoToolbox frame and maps its NV12 planes to Metal textures.</summary>
            /// <param name="frame">The borrowed decoded frame to process without consuming its ownership.</param>
            /// <param name="planes">Receives the retained Metal packet, borrowed plane textures, dimensions, and color flags.</param>
            /// <returns>Nonzero on success with an owned packet in planes; zero on failure.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_metal_prepare")]
            internal static extern int FfuMetalPrepare(IntPtr frame, out MetalPlanes planes);
            /// <summary>Releases a retained presentation packet that was never submitted.</summary>
            /// <param name="packet">The owned, unsubmitted native presentation packet to release.</param>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ffu_packet_cancel")]
            internal static extern void FfuPacketCancel(IntPtr packet);
        }
    }
}
