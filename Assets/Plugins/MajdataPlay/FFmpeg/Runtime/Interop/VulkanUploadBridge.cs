#nullable enable
using System;
using System.Runtime.InteropServices;
using MajdataPlay.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.FFmpeg.Interop
{
    /// <summary>
    /// Managed wrapper for the standalone Vulkan texture upload plug-in. The plug-in copies CPU RGBA8 pixels into
    /// a persistent staging buffer and records its own <c>vkCmdCopyBufferToImage</c> into Unity's command buffer,
    /// which replaces the <c>Texture2D.LoadRawTextureData</c> + <c>Texture2D.Apply</c> path that reproduces a
    /// driver hang on Vulkan.
    /// </summary>
    /// <remarks>
    /// The native plug-in ships for the Windows 64-bit player and the Windows Editor only. Callers must treat
    /// every member as an optional accelerator: a refusal means the frame has to be uploaded the usual way.
    /// The wrapper validates only what the managed side can see (backend, texture format and size); an image the
    /// plug-in refuses later in the frame is refused on the following enqueue, so at most one frame is lost.
    /// </remarks>
    internal static class VulkanUploadBridge
    {
        /// <summary>Version of the native C ABI this wrapper speaks.</summary>
        private const int AbiVersion = 1;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>Names the native texture upload plug-in without its file extension.</summary>
        private const string Library = "MajdataPlayTextureUpload";
        /// <summary>Stores the first native loading failure so later calls do not repeat the exception.</summary>
        private static bool s_loadFailed;
        /// <summary>Caches the instance identifier of the texture whose native pointer is cached.</summary>
        private static int s_cachedTextureId;
        /// <summary>Caches the native texture pointer resolved for the cached instance identifier.</summary>
        private static IntPtr s_cachedNative;
        /// <summary>Caches the borrowed native render event callback.</summary>
        private static IntPtr s_renderEvent;
        /// <summary>Caches the native render event identifier.</summary>
        private static int s_eventId = -1;
#endif
        /// <summary>Gets the reason the bridge cannot be used, or null while it can.</summary>
        public static string? AvailabilityReason { get; private set; }

        /// <summary>Gets whether Unity runs Vulkan with a native device the plug-in can copy into.</summary>
        /// <remarks>This property is cheap enough to query once per frame; it never allocates.</remarks>
        public static bool IsAvailable
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                if (s_loadFailed)
                {
                    return false;
                }

                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                {
                    AvailabilityReason = "The Vulkan texture upload plug-in needs the Vulkan graphics backend.";
                    return false;
                }

                try
                {
                    if (Native.MajVkUploadAbiVersion() != AbiVersion)
                    {
                        AvailabilityReason = "The Vulkan texture upload plug-in ABI does not match this player (version "
                            + AbiVersion + " required). Restart Unity after replacing the native library.";
                        return false;
                    }

                    if (Native.MajVkUploadReady() == 0)
                    {
                        AvailabilityReason = "Unity has not created its Vulkan device yet.";
                        return false;
                    }
                }
                catch (DllNotFoundException error)
                {
                    s_loadFailed = true;
                    AvailabilityReason = "The Vulkan texture upload plug-in is missing for this player: " + error.Message;
                    MajDebug.LogWarning("FFmpeg", "[Upload] " + AvailabilityReason);
                    return false;
                }
                catch (EntryPointNotFoundException error)
                {
                    s_loadFailed = true;
                    AvailabilityReason = "The Vulkan texture upload plug-in is outdated: " + error.Message;
                    MajDebug.LogWarning("FFmpeg", "[Upload] " + AvailabilityReason);
                    return false;
                }
                catch (BadImageFormatException error)
                {
                    s_loadFailed = true;
                    AvailabilityReason = "The Vulkan texture upload plug-in has an unsupported architecture: " + error.Message;
                    MajDebug.LogWarning("FFmpeg", "[Upload] " + AvailabilityReason);
                    return false;
                }

                AvailabilityReason = null;
                return true;
#else
                AvailabilityReason = "The Vulkan texture upload plug-in ships for Windows only.";
                return false;
#endif
            }
        }

        /// <summary>Gets the number of uploads the render event has not recorded yet.</summary>
        /// <returns>The queued upload count, or zero when the bridge is unavailable.</returns>
        public static int PendingCount
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                if (!IsAvailable)
                {
                    return 0;
                }

                try
                {
                    return Native.MajVkUploadPending();
                }
                catch (DllNotFoundException)
                {
                    s_loadFailed = true;
                    return 0;
                }
                catch (EntryPointNotFoundException)
                {
                    s_loadFailed = true;
                    return 0;
                }
#else
                return 0;
#endif
            }
        }

        /// <summary>Copies one tightly packed RGBA8 frame into the plug-in's staging ring.</summary>
        /// <param name="texture">The Unity texture the pixels belong to; it must be <see cref="TextureFormat.RGBA32"/>.</param>
        /// <param name="rgba">The borrowed CPU pixels; they are copied before this method returns.</param>
        /// <param name="width">The frame width in pixels, which must match the texture width.</param>
        /// <param name="height">The frame height in pixels, which must match the texture height.</param>
        /// <param name="rowPitch">The byte distance between two source rows, at least <paramref name="width"/> times four.</param>
        /// <returns>True when the upload is queued and <see cref="IssueRenderEvent"/> must run this frame; otherwise false.</returns>
        public static bool TryEnqueue(Texture2D texture, IntPtr rgba, int width, int height, int rowPitch)
        {
            if (texture == null || rgba == IntPtr.Zero || width <= 0 || height <= 0 || rowPitch < width * 4)
            {
                return false;
            }

            // Only plain 8-bit RGBA textures are uploaded, and only when the frame matches their size.
            if (texture.format != TextureFormat.RGBA32 || texture.width != width || texture.height != height)
            {
                return false;
            }

            if (!IsAvailable)
            {
                return false;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var native = ResolveNativeTexture(texture);
            if (native == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return Native.MajVkUploadEnqueue(native, rgba, width, height, rowPitch) != 0;
            }
            catch (DllNotFoundException)
            {
                s_loadFailed = true;
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                s_loadFailed = true;
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>Schedules the plug-in's render event, which records the queued uploads into the current frame.</summary>
        /// <remarks>The event runs on Unity's render thread after the caller's render commands for this frame.</remarks>
        public static void IssueRenderEvent()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!IsAvailable)
            {
                return;
            }

            var callback = RenderEventFunc();
            if (callback == IntPtr.Zero || s_eventId < 0)
            {
                return;
            }

            GL.IssuePluginEvent(callback, s_eventId);
#endif
        }

        /// <summary>Drops queued uploads and forgets refused textures after the caller recreated its texture.</summary>
        /// <remarks>The managed texture pointer cache is cleared as well, so the next enqueue resolves it again.</remarks>
        public static void Reset()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            s_cachedTextureId = 0;
            s_cachedNative = IntPtr.Zero;
            if (!IsAvailable)
            {
                return;
            }

            try
            {
                Native.MajVkUploadReset();
            }
            catch (DllNotFoundException)
            {
                s_loadFailed = true;
            }
            catch (EntryPointNotFoundException)
            {
                s_loadFailed = true;
            }
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>Resolves and caches the native texture pointer of a Unity texture.</summary>
        /// <param name="texture">The texture whose native pointer is requested.</param>
        /// <returns>The borrowed native texture pointer, or zero when Unity has none.</returns>
        /// <remarks>
        /// <see cref="Texture.GetNativeTexturePtr"/> synchronizes with the render thread, so it is resolved once per
        /// texture and reused until <see cref="Reset"/> or a different texture invalidates the cached value.
        /// </remarks>
        private static IntPtr ResolveNativeTexture(Texture2D texture)
        {
            var id = texture.GetInstanceID();
            if (id == s_cachedTextureId && s_cachedNative != IntPtr.Zero)
            {
                return s_cachedNative;
            }

            var native = texture.GetNativeTexturePtr();
            s_cachedTextureId = id;
            s_cachedNative = native;
            return native;
        }

        /// <summary>Gets the cached native render event callback, resolving it on first use.</summary>
        /// <returns>The borrowed native callback pointer, or zero when the plug-in reports none.</returns>
        private static IntPtr RenderEventFunc()
        {
            if (s_renderEvent != IntPtr.Zero && s_eventId >= 0)
            {
                return s_renderEvent;
            }

            try
            {
                s_renderEvent = Native.MajVkUploadRenderEventFunc();
                s_eventId = Native.MajVkUploadEventId();
            }
            catch (DllNotFoundException)
            {
                s_loadFailed = true;
                s_renderEvent = IntPtr.Zero;
            }
            catch (EntryPointNotFoundException)
            {
                s_loadFailed = true;
                s_renderEvent = IntPtr.Zero;
            }

            return s_renderEvent;
        }

        /// <summary>Declares the Cdecl entry points of the native Vulkan texture upload plug-in.</summary>
        private static class Native
        {
            /// <summary>Returns the native C ABI version of the plug-in.</summary>
            /// <returns>The plug-in ABI version, which must equal <see cref="AbiVersion"/>.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "MajVkUpload_AbiVersion")]
            internal static extern int MajVkUploadAbiVersion();

            /// <summary>Reports whether Unity exposes a Vulkan device the plug-in can copy into.</summary>
            /// <returns>One while a device is available, otherwise zero.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "MajVkUpload_Ready")]
            internal static extern int MajVkUploadReady();

            /// <summary>Copies tightly packed RGBA8 pixels into a persistent staging slot and queues them.</summary>
            /// <param name="unityTexture">The borrowed Unity texture pointer the pixels belong to.</param>
            /// <param name="pixels">The borrowed CPU RGBA8 pixels, copied before the call returns.</param>
            /// <param name="width">The frame width in pixels.</param>
            /// <param name="height">The frame height in pixels.</param>
            /// <param name="rowPitch">The byte distance between two source rows.</param>
            /// <returns>One when the upload is queued, otherwise zero and the caller must fall back.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "MajVkUpload_Enqueue")]
            internal static extern int MajVkUploadEnqueue(IntPtr unityTexture, IntPtr pixels, int width, int height,
                int rowPitch);

            /// <summary>Returns the number of queued uploads no render event has recorded yet.</summary>
            /// <returns>The queued upload count.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "MajVkUpload_Pending")]
            internal static extern int MajVkUploadPending();

            /// <summary>Returns the Unity event identifier the plug-in configured for itself.</summary>
            /// <returns>The event identifier to pass to <see cref="GL.IssuePluginEvent(IntPtr, int)"/>.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "MajVkUpload_EventId")]
            internal static extern int MajVkUploadEventId();

            /// <summary>Returns the borrowed Unity render event callback of the plug-in.</summary>
            /// <returns>The callback pointer to pass to <see cref="GL.IssuePluginEvent(IntPtr, int)"/>.</returns>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "MajVkUpload_RenderEventFunc")]
            internal static extern IntPtr MajVkUploadRenderEventFunc();

            /// <summary>Drops queued uploads and forgets refused textures.</summary>
            [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "MajVkUpload_Reset")]
            internal static extern void MajVkUploadReset();
        }
#endif
    }
}
