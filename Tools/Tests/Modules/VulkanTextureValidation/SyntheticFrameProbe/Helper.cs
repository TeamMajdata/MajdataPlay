#nullable enable
using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MajdataPlay.Validation.SyntheticFrameProbe
{
    /// <summary>Replaces only a game background's pixels while retaining its original UI and material.</summary>
    public static class Helper
    {
        /// <summary>Declares the optional Windows buffer-visibility diagnostic ABI without loading it in disabled modes.</summary>
        private static class BufferBarrierNative
        {
            /// <summary>Gets the native diagnostic ABI version.</summary>
            /// <returns>One for the supported buffer-barrier probe ABI.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "bb_abi_version", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int AbiVersion();

            /// <summary>Gets whether the native probe enabled its interception during preload.</summary>
            /// <returns>Zero when disabled or one when enabled.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "bb_mode", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int Mode();

            /// <summary>Gets the native enabled-hook readiness status.</summary>
            /// <returns>Zero when ready, positive one when disabled, or a negative unavailable status.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "bb_status", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int Status();

            /// <summary>Gets the cumulative count of intercepted buffer-copy calls.</summary>
            /// <returns>The native unsigned 64-bit observation counter.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "bb_copy_calls", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern ulong CopyCalls();

            /// <summary>Gets the cumulative count of inserted buffer-visibility barriers.</summary>
            /// <returns>The native unsigned 64-bit observation counter.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "bb_barrier_calls", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern ulong BarrierCalls();

            /// <summary>Gets the cumulative native diagnostic error count.</summary>
            /// <returns>The native unsigned 64-bit observation counter.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "bb_errors", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern ulong Errors();

            /// <summary>Gets the cumulative narrow-mode copies skipped because the buffer is not index or vertex data.</summary>
            /// <returns>The native unsigned 64-bit observation counter.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "bb_skipped_calls", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
            internal static extern ulong SkippedCalls();
        }

        /// <summary>Observes the buffer-barrier probe only when enabled for this normal-path installation.</summary>
        private static bool s_bufferBarrier;
        /// <summary>Stores the requested probe mode: zero disabled, one narrow, two broad.</summary>
        private static int s_bufferBarrierMode;
        /// <summary>Stores the synthetic frame width.</summary>
        private const int Width = 854;
        /// <summary>Stores the synthetic frame height.</summary>
        private const int Height = 480;
        /// <summary>Stores the requested uploads per second.</summary>
        private const double UploadHz = 60;
        /// <summary>Retains the owning game component until cleanup.</summary>
        private static MonoBehaviour? s_manager;
        /// <summary>Retains the original RawImage receiving pixels.</summary>
        private static RawImage? s_image;
        /// <summary>Owns the synthetic readable upload texture.</summary>
        private static Texture2D? s_texture;
        /// <summary>Owns an identically configured texture initialized once for the display control.</summary>
        private static Texture2D? s_displayTexture;
        /// <summary>Displays the unchanged control texture instead of the upload target when true.</summary>
        private static bool s_decoupled;
        /// <summary>Counts the control texture's initialization uploads separately from the upload target.</summary>
        private static int s_displayInitialUploads;
        /// <summary>Counts unique-frame CPU object binding checks during this installation.</summary>
        private static long s_bindingChecks;
        /// <summary>Counts CPU object bindings that differ from the selected texture during this installation.</summary>
        private static long s_bindingMismatches;
        /// <summary>Owns tightly packed source pixels.</summary>
        private static byte[]? s_pixels;
        /// <summary>Pins the persistent source buffer.</summary>
        private static GCHandle s_pin;
        /// <summary>Stores the pinned source address.</summary>
        private static IntPtr s_pointer;
        /// <summary>Guards multiple Canvas callbacks in the same Unity frame.</summary>
        private static int s_lastFrame = -1;
        /// <summary>Stores the next upload deadline.</summary>
        private static double s_nextUpload;
        /// <summary>Stores the last progress report time.</summary>
        private static double s_reportTime;
        /// <summary>Tracks whether this installation has emitted its initial one-second progress report.</summary>
        private static bool s_hasReported;
        /// <summary>Suppresses uploads after initialization for the static-texture control.</summary>
        private static bool s_staticFrames;
        /// <summary>Counts unique Unity frames since the last report.</summary>
        private static long s_frames;
        /// <summary>Counts all uploads in this installation.</summary>
        private static long s_uploads;
        /// <summary>Stores the upload count at the last report.</summary>
        private static long s_reportedUploads;
        /// <summary>Accumulates CPU wall time in upload API calls.</summary>
        private static double s_cpuSeconds;

        /// <summary>Installs synthetic uploads into the existing game background without starting its decoder.</summary>
        /// <param name="manager">The initialized BGManager, passed through its MonoBehaviour base class.</param>
        /// <exception cref="MissingFieldException">The expected background ABI does not match.</exception>
        /// <exception cref="InvalidOperationException">The expected UI or video component is absent.</exception>
        public static void Install(MonoBehaviour manager)
        {
            var readbackGate = string.Equals(Environment.GetEnvironmentVariable("MAJ_SYNTHETIC_READBACK_GATE"), "1", StringComparison.Ordinal);
            var nativeFence = string.Equals(Environment.GetEnvironmentVariable("MAJ_SYNTHETIC_NATIVE_FENCE"), "1", StringComparison.Ordinal);
            var bufferBarrierValue = Environment.GetEnvironmentVariable("MAJ_SYNTHETIC_BUFFER_BARRIER");
            var bufferBarrierMode = 0;
            if (string.Equals(bufferBarrierValue, "1", StringComparison.Ordinal))
            {
                bufferBarrierMode = 1;
            }
            else if (string.Equals(bufferBarrierValue, "2", StringComparison.Ordinal))
            {
                bufferBarrierMode = 2;
            }
            var bufferBarrier = bufferBarrierMode != 0;
            if (bufferBarrier && (readbackGate || nativeFence))
            {
                Debug.LogError("[SyntheticVideoProbe] FAIL bufferBarrier=True conflicts with a completion-gate mode.");
                throw new InvalidOperationException("The buffer-barrier probe requires the ordinary synthetic path without completion gates.");
            }
            if (readbackGate && nativeFence)
            {
                throw new InvalidOperationException("Select either retired readback acknowledgements or native queue fences, not both.");
            }
            if (readbackGate || nativeFence)
            {
                ReadbackGate.Install(manager);
                return;
            }

            Cleanup();
            s_bufferBarrier = bufferBarrier;
            s_bufferBarrierMode = bufferBarrierMode;
            if (manager == null)
            {
                throw new InvalidOperationException("The background manager is absent.");
            }

            if ((bool)ReadField(manager, "_destroyed")!)
            {
                return;
            }

            try
            {
                if (s_bufferBarrier)
                {
                    ValidateBufferBarrier();
                }
                var player = ReadField(manager, "_videoPlayer") as Behaviour;
                var cover = ReadField(manager, "_coverRenderer") as Image;
                var image = ReadField(manager, "_videoRenderer") as RawImage;
                if (player == null || cover == null || image == null)
                {
                    throw new InvalidOperationException("The background UI or video component is absent.");
                }

                var close = player.GetType().GetMethod("Close", BindingFlags.Instance | BindingFlags.Public,
                    null, Type.EmptyTypes, null) ?? throw new MissingMethodException("The video Close ABI does not match.");
                close.Invoke(player, null);
                player.enabled = false;
                s_manager = manager;
                s_image = image;
                s_staticFrames = string.Equals(Environment.GetEnvironmentVariable("MAJ_SYNTHETIC_STATIC"), "1", StringComparison.Ordinal);
                s_decoupled = string.Equals(Environment.GetEnvironmentVariable("MAJ_SYNTHETIC_DECOUPLE"), "1", StringComparison.Ordinal);
                s_pixels = new byte[Width * Height * 4];
                for (var offset = 0; offset < s_pixels.Length; offset += 4)
                {
                    s_pixels[offset] = 24;
                    s_pixels[offset + 1] = 48;
                    s_pixels[offset + 2] = 72;
                    s_pixels[offset + 3] = 255;
                }

                s_pin = GCHandle.Alloc(s_pixels, GCHandleType.Pinned);
                s_pointer = s_pin.AddrOfPinnedObject();
                s_texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
                {
                    name = "Synthetic game background",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                s_displayTexture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
                {
                    name = "Synthetic unchanged display",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                s_uploads = s_reportedUploads = s_frames = 0;
                s_bindingChecks = s_bindingMismatches = 0;
                s_displayInitialUploads = 0;
                s_cpuSeconds = 0;
                Upload();
                s_displayTexture.LoadRawTextureData(s_pointer, Width * Height * 4);
                s_displayTexture.Apply(false, false);
                s_displayInitialUploads++;
                Field(manager, "_usePictureAsBackground").SetValue(manager, false);
                Field(manager, "_videoLoadVersion").SetValue(manager, (int)ReadField(manager, "_videoLoadVersion")! + 1);
                cover.enabled = false;
                image.texture = s_decoupled ? s_displayTexture : s_texture;
                image.transform.localScale = new Vector3(1, Height / (float)Width, 1);
                image.enabled = true;
                s_lastFrame = -1;
                s_reportTime = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                s_hasReported = false;
                s_nextUpload = s_reportTime + (1.0 / UploadHz);
                s_reportedUploads = s_uploads;
                s_cpuSeconds = 0;
                Canvas.willRenderCanvases += Tick;
                Application.quitting += Quit;
                Debug.Log("[SyntheticVideoProbe] INSTALL width=854 height=480 uploadHz=60 scene="
                    + manager.gameObject.scene.name + "; material=" + image.material.name
                    + "; backend=" + SystemInfo.graphicsDeviceType
                    + "; refreshHz=" + Screen.currentResolution.refreshRateRatio.value.ToString("F3", CultureInfo.InvariantCulture)
                    + "; window=" + Screen.width + "x" + Screen.height
                    + "; vSyncCount=" + QualitySettings.vSyncCount + "; targetFrameRate=" + Application.targetFrameRate
                    + "; static=" + s_staticFrames
                    + "; decoupled=" + s_decoupled
                    + "; displayInitialUploads=" + s_displayInitialUploads
                    + "; uploadTextureDisplayed=" + (image.texture == s_texture)
                    + "; original UI/material retained; video CurrentSec remains zero"
                    + (s_bufferBarrier ? "; bufferBarrier=True; bufferBarrierMode=" + s_bufferBarrierMode + "; bufferBarrierStatus=0" : "; bufferBarrier=False"));
            }
            catch (Exception error)
            {
                if (s_bufferBarrier)
                {
                    Debug.LogError("[SyntheticVideoProbe] FAIL bufferBarrier=True initialization=" + error);
                }
                Cleanup();
                throw;
            }
        }

        /// <summary>Requires the enabled native buffer-visibility probe before any synthetic frame is installed.</summary>
        /// <exception cref="NotSupportedException">The probe is unavailable, disabled, or missing a required Vulkan entry point.</exception>
        private static void ValidateBufferBarrier()
        {
            var requested = s_bufferBarrierMode;
            var abi = BufferBarrierNative.AbiVersion();
            if (abi != 2)
            {
                throw new NotSupportedException("The buffer-barrier probe ABI is " + abi + " instead of 2.");
            }

            var mode = BufferBarrierNative.Mode();
            if (mode != requested)
            {
                throw new NotSupportedException("The buffer-barrier probe mode is " + mode + " instead of the requested " + requested + ".");
            }

            var status = BufferBarrierNative.Status();
            if (status != 0)
            {
                throw new NotSupportedException("The buffer-barrier probe is not ready; status=" + status + ".");
            }
        }

        /// <summary>Observes the native copy and inserted-barrier counters for the current report only.</summary>
        /// <returns>The log suffix for the buffer-barrier mode, or an empty string when the mode is disabled.</returns>
        /// <exception cref="InvalidOperationException">The enabled probe reported a fault after installation.</exception>
        private static string BufferBarrierObservation()
        {
            if (!s_bufferBarrier)
            {
                return string.Empty;
            }

            var status = BufferBarrierNative.Status();
            var copies = BufferBarrierNative.CopyCalls();
            var barriers = BufferBarrierNative.BarrierCalls();
            var skipped = BufferBarrierNative.SkippedCalls();
            var errors = BufferBarrierNative.Errors();
            if (status != 0 || errors > 0)
            {
                throw new InvalidOperationException("The buffer-barrier probe failed after installation; status=" + status
                    + " errors=" + errors + " copies=" + copies + " barriers=" + barriers + ".");
            }

            return "; bufferBarrier=True bufferBarrierMode=" + s_bufferBarrierMode + " bufferCopies=" + copies
                + " bufferBarriers=" + barriers + " bufferSkipped=" + skipped
                + " bufferBarrierErrors=" + errors + " bufferBarrierStatus=" + status;
        }

        /// <summary>Resolves an expected private instance field without depending on the game assembly.</summary>
        /// <param name="manager">The owning component.</param>
        /// <param name="name">The required field name.</param>
        /// <returns>The validated field metadata.</returns>
        /// <exception cref="MissingFieldException">The field is absent.</exception>
        private static FieldInfo Field(MonoBehaviour manager, string name)
        {
            return manager.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(manager.GetType().FullName, name);
        }

        /// <summary>Reads a background field once during installation.</summary>
        /// <param name="manager">The owning component.</param>
        /// <param name="name">The expected field name.</param>
        /// <returns>The field's current value.</returns>
        private static object? ReadField(MonoBehaviour manager, string name) => Field(manager, name).GetValue(manager);

        /// <summary>Updates at most once per frame and submits no catch-up bursts.</summary>
        private static void Tick()
        {
            if (s_lastFrame == Time.frameCount)
            {
                return;
            }

            s_lastFrame = Time.frameCount;
            if (s_manager == null || s_image == null || !s_image.enabled)
            {
                Cleanup();
                return;
            }

            try
            {
                s_bindingChecks++;
                var expectedTexture = s_decoupled ? s_displayTexture : s_texture;
                if (s_image.texture != expectedTexture)
                {
                    s_bindingMismatches++;
                }

                s_frames++;
                var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                if (!s_staticFrames && now >= s_nextUpload)
                {
                    var firstRow = (int)((s_uploads * 8) % Height);
                    var endRow = Math.Min(Height, firstRow + 8);
                    for (var offset = firstRow * Width * 4; offset < endRow * Width * 4; offset += 4)
                    {
                        s_pixels![offset] = (byte)(s_uploads * 17);
                        s_pixels[offset + 1] = (byte)(s_uploads * 31);
                        s_pixels[offset + 2] = (byte)(255 - (byte)(s_uploads * 17));
                    }

                    Upload();
                    var period = 1.0 / UploadHz;
                    s_nextUpload += (Math.Floor((now - s_nextUpload) / period) + 1.0) * period;
                }

                if (now - s_reportTime >= (s_hasReported ? 10 : 1))
                {
                    var duration = now - s_reportTime;
                    var uploads = s_uploads - s_reportedUploads;
                    var bufferBarrierObservation = BufferBarrierObservation();
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[SyntheticVideoProbe] PROGRESS uploadHz=60 fps={0:F3} uploadsPerSecond={1:F3} uploads={2} uploadCpuMs={3:F3} static={4} decoupled={5} bindingChecks={6} bindingMismatches={7}",
                        s_frames / duration, uploads / duration, s_uploads,
                        uploads == 0 ? 0 : s_cpuSeconds * 1000 / uploads, s_staticFrames,
                        s_decoupled, s_bindingChecks, s_bindingMismatches) + bufferBarrierObservation);
                    s_hasReported = true;
                    s_frames = 0;
                    s_reportTime = now;
                    s_reportedUploads = s_uploads;
                    s_cpuSeconds = 0;
                }
            }
            catch (Exception error)
            {
                Debug.LogError("[SyntheticVideoProbe] FAIL " + error);
                Cleanup();
            }
        }

        /// <summary>Copies the pinned buffer through the production CPU texture upload overload.</summary>
        private static void Upload()
        {
            var before = Stopwatch.GetTimestamp();
            s_texture!.LoadRawTextureData(s_pointer, Width * Height * 4);
            s_texture.Apply(false, false);
            s_cpuSeconds += (Stopwatch.GetTimestamp() - before) / (double)Stopwatch.Frequency;
            s_uploads++;
        }

        /// <summary>Releases static subscriptions when the application quits.</summary>
        private static void Quit() => Cleanup();

        /// <summary>Detaches callbacks before releasing the texture and persistent source pin.</summary>
        private static void Cleanup()
        {
            Canvas.willRenderCanvases -= Tick;
            Application.quitting -= Quit;
            if (s_image != null && (s_image.texture == s_texture || s_image.texture == s_displayTexture))
            {
                s_image.texture = null!;
            }

            if (s_texture != null)
            {
                UnityEngine.Object.Destroy(s_texture);
                Debug.Log("[SyntheticVideoProbe] CLEANUP uploads=" + s_uploads);
            }

            if (s_displayTexture != null)
            {
                UnityEngine.Object.Destroy(s_displayTexture);
            }

            if (s_pin.IsAllocated)
            {
                s_pin.Free();
            }

            s_texture = null;
            s_displayTexture = null;
            s_pixels = null;
            s_pointer = IntPtr.Zero;
            s_image = null;
            s_manager = null;
        }
    }
}
