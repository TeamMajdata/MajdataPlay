#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace MajdataPlay.Validation.SyntheticFrameProbe
{
    /// <summary>Runs one bounded retired-texture protocol with readback acknowledgements or native Unity graphics-queue fences.</summary>
    internal static class ReadbackGate
    {
        /// <summary>Retains the only session, including resources whose acknowledgements have not completed.</summary>
        private static Session? s_session;

        /// <summary>Installs a readback-gated synthetic source into the existing background UI.</summary>
        /// <param name="manager">The initialized background manager passed through its Unity base type.</param>
        /// <exception cref="InvalidOperationException">An earlier session has not drained or the UI/camera ABI is invalid.</exception>
        /// <exception cref="NotSupportedException">The actual Player does not support asynchronous GPU readback.</exception>
        internal static void Install(MonoBehaviour manager)
        {
            if (s_session != null)
            {
                s_session.BeginClose();
                throw new InvalidOperationException("A previous readback-gate session has not drained; no additional pool was allocated.");
            }

            var session = new Session();
            s_session = session;
            try
            {
                session.Initialize(manager);
            }
            catch (Exception error)
            {
                session.InitializationFailed();
                Debug.LogError("[SyntheticVideoProbe] FAIL " + session.ModeName + " initialization=" + error);
                throw;
            }
        }

        /// <summary>Declares the diagnostic plug-in ABI; all handles are pointer-sized and all calls use Cdecl.</summary>
        private static class Native
        {
            /// <summary>Returns the native diagnostic ABI version.</summary>
            /// <returns>One for the supported ABI.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "mf_abi_version", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int AbiVersion();
            /// <summary>Queries whether Unity initialized the diagnostic graphics queue.</summary>
            /// <returns>Zero when ready, otherwise the native status.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "mf_status", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int Status();
            /// <summary>Gets the render event reserved by the diagnostic plug-in.</summary>
            /// <returns>The native render-event identifier.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "mf_event_id", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int EventId();
            /// <summary>Gets the Unity rendering callback for queue marker submission.</summary>
            /// <returns>The native rendering callback pointer.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "mf_render_callback", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr RenderCallback();
            /// <summary>Reserves one owned bounded native fence ticket.</summary>
            /// <returns>A ticket pointer, or zero when admission fails.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "mf_ticket_create", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr TicketCreate();
            /// <summary>Polls an owned ticket without blocking for GPU completion.</summary>
            /// <param name="ticket">The owned outstanding ticket.</param>
            /// <returns>Zero while pending, one for fence success, or a negative error.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "mf_ticket_poll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int TicketPoll(IntPtr ticket);
            /// <summary>Releases only a native ticket whose lifetime permits release.</summary>
            /// <param name="ticket">The owned ticket to release.</param>
            /// <returns>One after safe release, zero when refused, or a negative native error.</returns>
            [DllImport("MajdataPlayVulkanPacing", EntryPoint = "mf_ticket_release", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int TicketRelease(IntPtr ticket);
        }

        /// <summary>Describes CPU-side ownership only; these values do not describe Vulkan image states.</summary>
        private enum SlotState
        {
            /// <summary>Allows an initial write, or a write after a successful retired readback acknowledgement.</summary>
            Available,
            /// <summary>Names the texture currently bound to the background image.</summary>
            Current,
            /// <summary>Prevents reuse until a matching rendering context records a marker.</summary>
            RetiredAwaitMarker,
            /// <summary>Prevents reuse until its own cached callback acknowledges the recorded request.</summary>
            PendingAck,
            /// <summary>Retains storage after an uncertain submission or failed readback.</summary>
            Quarantined
        }

        /// <summary>Owns one fixed texture and one cached callback; only the acknowledgement fields cross threads.</summary>
        private sealed class Slot
        {
            /// <summary>Owns the Unity texture, or null before allocation.</summary>
            internal Texture2D? Texture;
            /// <summary>Stores the main-thread ownership state.</summary>
            internal SlotState State;
            /// <summary>Stores zero while pending, one for success, or minus one for error.</summary>
            internal int Ack;
            /// <summary>Stores the callback timestamp using an atomic exchange.</summary>
            internal long AckTimestamp;
            /// <summary>Stores the submission timestamp for observed acknowledgement latency.</summary>
            internal long RequestTimestamp;
            /// <summary>Marks slots that have completed at least one retired acknowledgement.</summary>
            internal bool AcknowledgedBefore;
            /// <summary>Owns at most one native queue-fence ticket, retained after uncertain completion.</summary>
            internal IntPtr NativeTicket;
            /// <summary>Gets the delegate reused only by this slot's nonoverlapping readback requests.</summary>
            internal Action<AsyncGPUReadbackRequest>? Callback { get; }

            /// <summary>Creates a cached callback only for the retired-readback backend.</summary>
            /// <param name="nativeMode">Whether this slot uses native tickets instead of readback callbacks.</param>
            internal Slot(bool nativeMode)
            {
                if (!nativeMode)
                {
                    Callback = Complete;
                }
            }

            /// <summary>Publishes request status without touching Unity objects, logging, or returning resources.</summary>
            /// <param name="request">The completed asynchronous readback request.</param>
            private void Complete(AsyncGPUReadbackRequest request)
            {
                var success = false;
                try
                {
                    success = request.done && !request.hasError;
                }
                catch
                {
                    success = false;
                }

                Interlocked.Exchange(ref AckTimestamp, Stopwatch.GetTimestamp());
                Interlocked.Exchange(ref Ack, success ? 1 : -1);
            }
        }

        /// <summary>Owns a three-slot pool and refuses to retire its storage while completion is uncertain.</summary>
        private sealed class Session
        {
            /// <summary>Defines the source width in pixels.</summary>
            private const int Width = 854;
            /// <summary>Defines the source height in pixels.</summary>
            private const int Height = 480;
            /// <summary>Defines the nominal upload schedule, independently of actual throughput.</summary>
            private const double UploadHz = 60;
            /// <summary>Bounds all owned textures and pending requests.</summary>
            private const int PoolSize = 3;
            /// <summary>Owns all slots for the lifetime of this installation.</summary>
            private readonly Slot[] _slots = new Slot[PoolSize];
            /// <summary>Selects native queue-fence tickets instead of retired-region readback acknowledgements.</summary>
            private bool _nativeMode;
            /// <summary>Caches the validated native Unity rendering callback.</summary>
            private IntPtr _nativeCallback;
            /// <summary>Caches the native plug-in's reserved rendering event identifier.</summary>
            private int _nativeEvent;
            /// <summary>Gets the backend identifier used in failure and cleanup diagnostics.</summary>
            internal string ModeName => _nativeMode ? "nativeQueueFence=True" : "readbackGate=True";
            /// <summary>Retains the owning component without creating an additional Unity component.</summary>
            private MonoBehaviour? _manager;
            /// <summary>Retains the original RawImage and its material binding.</summary>
            private RawImage? _image;
            /// <summary>Caches the root Camera Canvas's actual rendering camera during installation.</summary>
            private Camera? _camera;
            /// <summary>Owns the reused marker command buffer.</summary>
            private CommandBuffer? _commands;
            /// <summary>Owns the persistent tightly packed RGBA source.</summary>
            private byte[]? _pixels;
            /// <summary>Retains the source pin until the entire session can be released.</summary>
            private GCHandle _pin;
            /// <summary>Caches the pinned address passed to the synchronous CPU copy API.</summary>
            private IntPtr _pointer;
            /// <summary>Names the current displayed slot, or minus one after closing.</summary>
            private int _current = -1;
            /// <summary>Deduplicates Canvas callbacks in a Unity frame.</summary>
            private int _lastCanvasFrame = -1;
            /// <summary>Deduplicates matching rendering contexts in a Unity frame.</summary>
            private int _lastContextFrame = -1;
            /// <summary>Stops additional upload work after closing begins.</summary>
            private bool _closing;
            /// <summary>Prevents repeated terminal failure logging.</summary>
            private bool _failed;
            /// <summary>Indicates that a texture may already have been exposed to rendering.</summary>
            private bool _everBound;
            /// <summary>Tracks event subscriptions for partial initialization cleanup.</summary>
            private bool _hooked;
            /// <summary>Prevents duplicate Unity resource release.</summary>
            private bool _released;
            /// <summary>Limits the pending-cleanup notice to one per session.</summary>
            private bool _cleanupPendingLogged;
            /// <summary>Stores the next nominal upload deadline.</summary>
            private double _nextUpload;
            /// <summary>Stores the previous progress-report timestamp.</summary>
            private double _reportTime;
            /// <summary>Changes the first progress interval from one second to ten seconds afterward.</summary>
            private bool _hasReported;
            /// <summary>Counts unique Canvas frames in the current reporting interval.</summary>
            private long _frames;
            /// <summary>Counts scheduled uploads after the three initialization uploads.</summary>
            private long _uploads;
            /// <summary>Stores scheduled uploads already included in a progress report.</summary>
            private long _reportedUploads;
            /// <summary>Counts texture initialization uploads separately.</summary>
            private int _initUploads;
            /// <summary>Counts background texture switches.</summary>
            private long _displaySwitches;
            /// <summary>Counts successfully recorded marker requests for the selected backend.</summary>
            private long _readbackRequests;
            /// <summary>Counts successful selected-backend acknowledgements consumed on the main thread.</summary>
            private long _readbackCompleted;
            /// <summary>Counts callback, native poll/release, or submission failures; failed slots are never reused.</summary>
            private long _readbackErrors;
            /// <summary>Counts uploads to slots with a previously consumed acknowledgement.</summary>
            private long _reuseAfterAck;
            /// <summary>Counts rejected ownership violations.</summary>
            private long _illegalReuse;
            /// <summary>Counts unexpected attempts to mark the displayed slot.</summary>
            private long _unexpectedCurrentMarker;
            /// <summary>Counts CPU RawImage binding observations.</summary>
            private long _bindingChecks;
            /// <summary>Counts observations that do not match the owned current slot.</summary>
            private long _bindingMismatches;
            /// <summary>Counts due uploads skipped because no slot was available.</summary>
            private long _backpressure;
            /// <summary>Counts matching unique rendering-context frames.</summary>
            private long _contextFrames;
            /// <summary>Stores the largest number of pending requests.</summary>
            private int _pendingPeak;
            /// <summary>Stores the maximum request-to-callback or request-to-successful-poll observation delay in milliseconds.</summary>
            private double _maxReadbackMs;
            /// <summary>Accumulates selected-backend CPU-observed acknowledgement delays in milliseconds.</summary>
            private double _readbackMs;
            /// <summary>Accumulates wall-clock CPU time inside scheduled upload API calls.</summary>
            private double _uploadCpuSeconds;

            /// <summary>Allocates the bounded slots and hooks existing public Canvas and SRP events.</summary>
            /// <param name="manager">The background component whose existing UI and material are retained.</param>
            /// <exception cref="InvalidOperationException">The background fields or rendering camera are unavailable.</exception>
            /// <exception cref="NotSupportedException">Readback is unsupported or the image is not camera-rendered.</exception>
            internal void Initialize(MonoBehaviour manager)
            {
                if (manager == null || (bool)Read(manager, "_destroyed")!)
                {
                    throw new InvalidOperationException("The initialized background manager is unavailable.");
                }
                _nativeMode = string.Equals(Environment.GetEnvironmentVariable("MAJ_SYNTHETIC_NATIVE_FENCE"), "1", StringComparison.Ordinal);
                if (_nativeMode)
                {
                    if (Native.AbiVersion() != 1)
                    {
                        throw new NotSupportedException("The native queue-fence diagnostic requires ABI 1.");
                    }
                    var status = Native.Status();
                    if (status != 0)
                    {
                        throw new NotSupportedException("The native queue-fence diagnostic is not ready; status=" + status + ".");
                    }
                    _nativeCallback = Native.RenderCallback();
                    _nativeEvent = Native.EventId();
                    if (_nativeCallback == IntPtr.Zero)
                    {
                        throw new NotSupportedException("The native queue-fence rendering callback is unavailable.");
                    }
                }
                else if (!SystemInfo.supportsAsyncGPUReadback)
                {
                    throw new NotSupportedException("This actual Player/device reports no asynchronous GPU readback support.");
                }

                var player = Read(manager, "_videoPlayer") as Behaviour;
                var cover = Read(manager, "_coverRenderer") as Image;
                var image = Read(manager, "_videoRenderer") as RawImage;
                if (player == null || cover == null || image == null)
                {
                    throw new InvalidOperationException("The background UI/player ABI is unavailable.");
                }
                var canvas = image.canvas;
                var root = canvas != null ? canvas.rootCanvas : null;
                var camera = root != null ? root.worldCamera : null;
                if (root == null || root.renderMode == RenderMode.ScreenSpaceOverlay || camera == null)
                {
                    throw new NotSupportedException("A root Camera Canvas with an explicit worldCamera is required for the context filter.");
                }

                var close = player.GetType().GetMethod("Close", BindingFlags.Instance | BindingFlags.Public,
                    null, Type.EmptyTypes, null) ?? throw new MissingMethodException("The video Close ABI does not match.");
                close.Invoke(player, null);
                player.enabled = false;
                _manager = manager;
                _image = image;
                _camera = camera;
                _pixels = new byte[Width * Height * 4];
                for (var offset = 0; offset < _pixels.Length; offset += 4)
                {
                    _pixels[offset] = 24;
                    _pixels[offset + 1] = 48;
                    _pixels[offset + 2] = 72;
                    _pixels[offset + 3] = 255;
                }
                _pin = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
                _pointer = _pin.AddrOfPinnedObject();
                _commands = new CommandBuffer { name = _nativeMode ? "Synthetic retired native queue-fence markers" : "Synthetic retired readback markers" };
                for (var index = 0; index < PoolSize; index++)
                {
                    var slot = new Slot(_nativeMode);
                    _slots[index] = slot;
                    slot.Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false)
                    {
                        name = "Synthetic readback-gate slot " + index,
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear
                    };
                    CopyPixels(slot);
                    _initUploads++;
                }

                Field(manager, "_usePictureAsBackground").SetValue(manager, false);
                Field(manager, "_videoLoadVersion").SetValue(manager, (int)Read(manager, "_videoLoadVersion")! + 1);
                cover.enabled = false;
                _slots[0].State = SlotState.Current;
                _current = 0;
                _everBound = true;
                image.texture = _slots[0].Texture!;
                image.transform.localScale = new Vector3(1, Height / (float)Width, 1);
                image.enabled = true;
                _reportTime = Seconds();
                _nextUpload = _reportTime + (1.0 / UploadHz);
                Hook();
                Debug.Log("[SyntheticVideoProbe] INSTALL width=854 height=480 uploadHz=60 scene="
                    + manager.gameObject.scene.name + "; material=" + image.material.name
                    + "; backend=" + SystemInfo.graphicsDeviceType
                    + "; refreshHz=" + Screen.currentResolution.refreshRateRatio.value.ToString("F3", CultureInfo.InvariantCulture)
                    + "; window=" + Screen.width + "x" + Screen.height
                    + "; vSyncCount=" + QualitySettings.vSyncCount + "; targetFrameRate=" + Application.targetFrameRate
                    + "; static=False; decoupled=False; " + (_nativeMode
                        ? "nativeQueueFence=True; poolSize=3; nativeMarkerAvailable=True; markerScope=UnityGraphicsQueue"
                        : "readbackGate=True; poolSize=3; readbackSupported=True; region=1x1; lastUseNotProven=True")
                    + "; initUploads=" + _initUploads + "; camera=" + camera.name);
            }

            /// <summary>Retains exposed resources, but releases never-displayed initialization storage.</summary>
            internal void InitializationFailed()
            {
                if (_everBound)
                {
                    Hook();
                    BeginClose();
                }
                else
                {
                    ReleaseResources();
                }
            }

            /// <summary>Subscribes once using delegates retained by this one session.</summary>
            private void Hook()
            {
                if (_hooked)
                {
                    return;
                }
                Canvas.willRenderCanvases += Tick;
                RenderPipelineManager.endContextRendering += EndContext;
                Application.quitting += BeginClose;
                _hooked = true;
            }

            /// <summary>Consumes acknowledgements and switches only to a noncurrent available slot.</summary>
            private void Tick()
            {
                if (_closing || _released || _lastCanvasFrame == Time.frameCount)
                {
                    return;
                }
                _lastCanvasFrame = Time.frameCount;
                try
                {
                    ConsumeAcks();
                    if (_closing)
                    {
                        return;
                    }
                    if (_manager == null || _image == null || !_image.enabled)
                    {
                        BeginClose();
                        return;
                    }
                    _bindingChecks++;
                    if (_current < 0 || _image.texture != _slots[_current].Texture)
                    {
                        _bindingMismatches++;
                        Fail("The original RawImage binding no longer matches the current slot.");
                        return;
                    }
                    _frames++;
                    var now = Seconds();
                    if (now >= _nextUpload)
                    {
                        var period = 1.0 / UploadHz;
                        _nextUpload += (Math.Floor((now - _nextUpload) / period) + 1.0) * period;
                        var available = FindAvailable();
                        if (available < 0)
                        {
                            _backpressure++;
                        }
                        else
                        {
                            SwitchTo(available);
                        }
                    }
                    if (now - _reportTime >= (_hasReported ? 10 : 1))
                    {
                        Report(now);
                    }
                }
                catch (Exception error)
                {
                    Fail("Canvas update: " + error);
                }
            }

            /// <summary>Finds a slot eligible under this protocol without waiting or writing the displayed texture.</summary>
            /// <returns>The available noncurrent slot index, or minus one when backpressure is required.</returns>
            private int FindAvailable()
            {
                for (var index = 0; index < PoolSize; index++)
                {
                    if (index != _current && _slots[index].State == SlotState.Available)
                    {
                        return index;
                    }
                }
                return -1;
            }

            /// <summary>Copies source pixels and retires the previous displayed slot without immediately marking it reusable.</summary>
            /// <param name="index">The noncurrent available slot selected on the main thread.</param>
            private void SwitchTo(int index)
            {
                var next = _slots[index];
                if (index == _current || next.State != SlotState.Available || next.NativeTicket != IntPtr.Zero || _current < 0
                    || _slots[_current].State != SlotState.Current)
                {
                    _illegalReuse++;
                    Fail("Rejected an upload outside the noncurrent available state.");
                    return;
                }
                var firstRow = (int)((_uploads * 8) % Height);
                var endRow = Math.Min(Height, firstRow + 8);
                for (var offset = firstRow * Width * 4; offset < endRow * Width * 4; offset += 4)
                {
                    _pixels![offset] = (byte)(_uploads * 17);
                    _pixels[offset + 1] = (byte)(_uploads * 31);
                    _pixels[offset + 2] = (byte)(255 - (byte)(_uploads * 17));
                }
                var before = Stopwatch.GetTimestamp();
                CopyPixels(next);
                _uploadCpuSeconds += (Stopwatch.GetTimestamp() - before) / (double)Stopwatch.Frequency;
                _uploads++;
                if (next.AcknowledgedBefore)
                {
                    _reuseAfterAck++;
                }
                _slots[_current].State = SlotState.RetiredAwaitMarker;
                next.State = SlotState.Current;
                _current = index;
                _image!.texture = next.Texture!;
                _displaySwitches++;
            }

            /// <summary>Copies the persistent source through the production synchronous CPU upload overload.</summary>
            /// <param name="slot">The owned texture to initialize or update while permitted by the protocol.</param>
            private void CopyPixels(Slot slot)
            {
                slot.Texture!.LoadRawTextureData(_pointer, Width * Height * 4);
                slot.Texture.Apply(false, false);
            }

            /// <summary>Records region readbacks only after a context containing the original Canvas camera.</summary>
            /// <param name="context">The pipeline rendering context whose cameras have been processed.</param>
            /// <param name="cameras">The actual cameras supplied to that public SRP context callback.</param>
            private void EndContext(ScriptableRenderContext context, List<Camera> cameras)
            {
                if (_released)
                {
                    return;
                }
                try
                {
                    ConsumeAcks();
                    if (!_closing && (_manager == null || _image == null || !_image.enabled))
                    {
                        BeginClose();
                    }
                    if (_released)
                    {
                        return;
                    }
                    var matching = false;
                    if (_camera != null)
                    {
                        for (var index = 0; index < cameras.Count; index++)
                        {
                            if (cameras[index] == _camera)
                            {
                                matching = true;
                                break;
                            }
                        }
                    }
                    if (matching && _lastContextFrame != Time.frameCount)
                    {
                        _lastContextFrame = Time.frameCount;
                        _contextFrames++;
                        RecordMarkers();
                    }
                    if (_closing)
                    {
                        TryFinishClose();
                    }
                }
                catch (Exception error)
                {
                    QuarantinePending();
                    _readbackErrors++;
                    Fail("Context marker submission: " + error);
                }
            }

            /// <summary>Submits at most one pending request per retired slot without marking the current slot.</summary>
            private void RecordMarkers()
            {
                var any = false;
                _commands!.Clear();
                for (var index = 0; index < PoolSize; index++)
                {
                    var slot = _slots[index];
                    if (slot.State != SlotState.RetiredAwaitMarker)
                    {
                        continue;
                    }
                    if (index == _current)
                    {
                        _unexpectedCurrentMarker++;
                        slot.State = SlotState.Quarantined;
                        Fail("Rejected a marker for the currently displayed slot.");
                        return;
                    }
                    if (_nativeMode)
                    {
                        if (slot.NativeTicket != IntPtr.Zero)
                        {
                            _illegalReuse++;
                            slot.State = SlotState.Quarantined;
                            Fail("A retired slot still owns an earlier native ticket.");
                            return;
                        }
                        slot.NativeTicket = Native.TicketCreate();
                        if (slot.NativeTicket == IntPtr.Zero)
                        {
                            _readbackErrors++;
                            slot.State = SlotState.Quarantined;
                            Fail("Native queue-fence ticket admission failed; the retired slot remains retained.");
                            return;
                        }
                    }
                    Interlocked.Exchange(ref slot.Ack, 0);
                    Interlocked.Exchange(ref slot.AckTimestamp, 0);
                    slot.RequestTimestamp = Stopwatch.GetTimestamp();
                    slot.State = SlotState.PendingAck;
                    if (_nativeMode)
                    {
                        _commands.IssuePluginEventAndData(_nativeCallback, _nativeEvent, slot.NativeTicket);
                    }
                    else
                    {
                        // This 1x1 request is an acknowledgement experiment, not a last-use fence.
                        _commands.RequestAsyncReadback(slot.Texture!, 0, 0, 1, 0, 1, 0, 1, slot.Callback!);
                    }
                    _readbackRequests++;
                    any = true;
                }
                if (any)
                {
                    var pending = Count(SlotState.PendingAck);
                    if (pending > _pendingPeak)
                    {
                        _pendingPeak = pending;
                    }
                    Graphics.ExecuteCommandBuffer(_commands);
                    _commands.Clear();
                }
            }

            /// <summary>Consumes only the acknowledgement belonging to the slot's one outstanding request.</summary>
            private void ConsumeAcks()
            {
                for (var index = 0; index < PoolSize; index++)
                {
                    var slot = _slots[index];
                    if (slot == null || slot.State != SlotState.PendingAck)
                    {
                        continue;
                    }
                    var ack = 0;
                    var completedAt = 0L;
                    if (_nativeMode)
                    {
                        try
                        {
                            if (slot.NativeTicket == IntPtr.Zero)
                            {
                                _illegalReuse++;
                                throw new InvalidOperationException("A pending native slot has no ticket.");
                            }
                            var status = Native.TicketPoll(slot.NativeTicket);
                            if (status == 0)
                            {
                                continue;
                            }
                            if (status != 1)
                            {
                                _readbackErrors++;
                                slot.State = SlotState.Quarantined;
                                Fail("Native queue-fence poll failed; status=" + status + ". The ticket and texture remain retained.");
                                continue;
                            }
                            if (Native.TicketRelease(slot.NativeTicket) != 1)
                            {
                                _readbackErrors++;
                                slot.State = SlotState.Quarantined;
                                Fail("Native queue-fence ticket release was refused; its slot remains retained.");
                                continue;
                            }
                            slot.NativeTicket = IntPtr.Zero;
                            ack = 1;
                            // This is CPU poll-observation time, not a GPU execution timestamp.
                            completedAt = Stopwatch.GetTimestamp();
                        }
                        catch (Exception error)
                        {
                            _readbackErrors++;
                            slot.State = SlotState.Quarantined;
                            Fail("Native queue-fence polling/release failed: " + error);
                            continue;
                        }
                    }
                    else
                    {
                        ack = Interlocked.CompareExchange(ref slot.Ack, 0, 0);
                        if (ack == 0)
                        {
                            continue;
                        }
                        completedAt = Interlocked.Read(ref slot.AckTimestamp);
                    }
                    var milliseconds = Math.Max(0, completedAt - slot.RequestTimestamp) * 1000.0 / Stopwatch.Frequency;
                    _readbackMs += milliseconds;
                    _maxReadbackMs = Math.Max(_maxReadbackMs, milliseconds);
                    if (ack > 0)
                    {
                        _readbackCompleted++;
                        slot.AcknowledgedBefore = true;
                        slot.State = SlotState.Available;
                    }
                    else
                    {
                        _readbackErrors++;
                        slot.State = SlotState.Quarantined;
                        Fail("A retired 1x1 readback completed with an error; its slot will not be reused.");
                    }
                }
            }

            /// <summary>Quarantines uncertain recorded/submitted storage instead of inferring completion.</summary>
            private void QuarantinePending()
            {
                for (var index = 0; index < PoolSize; index++)
                {
                    var slot = _slots[index];
                    if (slot != null && slot.State == SlotState.PendingAck)
                    {
                        slot.State = SlotState.Quarantined;
                    }
                }
            }

            /// <summary>Stops uploads and retains exposed resources until their region acknowledgement protocol drains.</summary>
            internal void BeginClose()
            {
                if (_released || _closing)
                {
                    return;
                }
                _closing = true;
                Canvas.willRenderCanvases -= Tick;
                if (_image != null)
                {
                    for (var index = 0; index < PoolSize; index++)
                    {
                        if (_slots[index] != null && _image.texture == _slots[index].Texture)
                        {
                            _image.texture = null!;
                            break;
                        }
                    }
                }
                if (_current >= 0)
                {
                    var slot = _slots[_current];
                    if (slot.State == SlotState.Current)
                    {
                        slot.State = SlotState.RetiredAwaitMarker;
                    }
                    _current = -1;
                }
                TryFinishClose();
            }

            /// <summary>Releases only when no displayed, retired, pending, or quarantined slot remains.</summary>
            private void TryFinishClose()
            {
                if (_released || !_closing)
                {
                    return;
                }
                var available = 0;
                for (var index = 0; index < PoolSize; index++)
                {
                    if (_slots[index] == null || (_slots[index].State == SlotState.Available && _slots[index].NativeTicket == IntPtr.Zero))
                    {
                        available++;
                    }
                }
                if (available == PoolSize)
                {
                    ReleaseResources();
                }
                else if (!_cleanupPendingLogged)
                {
                    _cleanupPendingLogged = true;
                    Debug.Log("[SyntheticVideoProbe] CLEANUP_PENDING " + ModeName + " pending=" + Count(SlotState.PendingAck)
                        + " retired=" + Count(SlotState.RetiredAwaitMarker) + " quarantined=" + Count(SlotState.Quarantined)
                        + " poolSize=3; resources retained; no blocking wait");
                }
            }

            /// <summary>Destroys owned Unity objects only on a main-thread call path after permitted drain or unused initialization.</summary>
            private void ReleaseResources()
            {
                if (_released)
                {
                    return;
                }
                _released = true;
                Canvas.willRenderCanvases -= Tick;
                if (_hooked)
                {
                    RenderPipelineManager.endContextRendering -= EndContext;
                    Application.quitting -= BeginClose;
                }
                for (var index = 0; index < PoolSize; index++)
                {
                    var slot = _slots[index];
                    if (slot != null && slot.Texture != null)
                    {
                        UnityEngine.Object.Destroy(slot.Texture);
                        slot.Texture = null;
                    }
                }
                _commands?.Dispose();
                _commands = null;
                if (_pin.IsAllocated)
                {
                    _pin.Free();
                }
                _pointer = IntPtr.Zero;
                _pixels = null;
                _manager = null;
                _image = null;
                _camera = null;
                if (ReferenceEquals(s_session, this))
                {
                    s_session = null;
                }
                Debug.Log("[SyntheticVideoProbe] CLEANUP_COMPLETE " + ModeName + " pending=0 uploads=" + _uploads
                    + (_nativeMode ? " markerRequests=" : " readbackRequests=") + _readbackRequests
                    + (_nativeMode ? " markerCompleted=" : " readbackCompleted=") + _readbackCompleted
                    + (_nativeMode ? "; markerScope=UnityGraphicsQueue; CPU poll observation only" : "; acknowledgement drain only; lastUseNotProven=True"));
            }

            /// <summary>Publishes one terminal failure and begins bounded nonblocking retirement.</summary>
            /// <param name="message">The violated diagnostic condition or API failure.</param>
            private void Fail(string message)
            {
                if (!_failed)
                {
                    _failed = true;
                    Debug.LogError("[SyntheticVideoProbe] FAIL " + ModeName + " " + message);
                }
                BeginClose();
            }

            /// <summary>Counts main-thread ownership states without allocating temporary collections.</summary>
            /// <param name="state">The ownership state to count.</param>
            /// <returns>The number of slots currently in that state.</returns>
            private int Count(SlotState state)
            {
                var result = 0;
                for (var index = 0; index < PoolSize; index++)
                {
                    if (_slots[index] != null && _slots[index].State == state)
                    {
                        result++;
                    }
                }
                return result;
            }

            /// <summary>Logs actual throughput and acknowledgement counters without treating them as GPU-idle proof.</summary>
            /// <param name="now">The current monotonic seconds value.</param>
            private void Report(double now)
            {
                var duration = now - _reportTime;
                var uploads = _uploads - _reportedUploads;
                var format = _nativeMode
                    ? "[SyntheticVideoProbe] PROGRESS uploadHz=60 fps={0:F3} uploadsPerSecond={1:F3} uploads={2} uploadCpuMs={3:F3} static=False decoupled=False nativeQueueFence=True poolSize=3 initUploads={4} displaySwitches={5} markerRequests={6} markerCompleted={7} markerErrors={8} reuseAfterAck={9} illegalReuse={10} unexpectedCurrentMarker={11} bindingChecks={12} bindingMismatches={13} backpressure={14} contextFrames={15} contextCameraMatched={15} pending={16} pendingPeak={17} markerPollLatencyMs={18:F3} maxMarkerPollLatencyMs={19:F3} markerScope=UnityGraphicsQueue"
                    : "[SyntheticVideoProbe] PROGRESS uploadHz=60 fps={0:F3} uploadsPerSecond={1:F3} uploads={2} uploadCpuMs={3:F3} static=False decoupled=False readbackGate=True poolSize=3 initUploads={4} displaySwitches={5} readbackRequests={6} readbackCompleted={7} readbackErrors={8} reuseAfterAck={9} illegalReuse={10} unexpectedCurrentMarker={11} bindingChecks={12} bindingMismatches={13} backpressure={14} contextFrames={15} contextCameraMatched={15} pending={16} pendingPeak={17} readbackLatencyMs={18:F3} maxReadbackLatencyMs={19:F3} lastUseNotProven=True";
                Debug.Log(string.Format(CultureInfo.InvariantCulture, format,
                    _frames / duration, uploads / duration, _uploads,
                    uploads == 0 ? 0 : _uploadCpuSeconds * 1000.0 / uploads,
                    _initUploads, _displaySwitches, _readbackRequests, _readbackCompleted,
                    _readbackErrors, _reuseAfterAck, _illegalReuse, _unexpectedCurrentMarker,
                    _bindingChecks, _bindingMismatches, _backpressure, _contextFrames,
                    Count(SlotState.PendingAck), _pendingPeak,
                    _readbackCompleted == 0 ? 0 : _readbackMs / _readbackCompleted, _maxReadbackMs));
                _hasReported = true;
                _frames = 0;
                _reportTime = now;
                _reportedUploads = _uploads;
                _uploadCpuSeconds = 0;
            }

            /// <summary>Converts the monotonic CPU clock into seconds.</summary>
            /// <returns>The current Stopwatch time in seconds.</returns>
            private static double Seconds()
            {
                return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            }

            /// <summary>Gets one expected background field during installation.</summary>
            /// <param name="manager">The background manager instance.</param>
            /// <param name="name">The exact expected private field name.</param>
            /// <returns>The field metadata used for this diagnostic ABI.</returns>
            /// <exception cref="MissingFieldException">The expected background field is absent.</exception>
            private static FieldInfo Field(MonoBehaviour manager, string name)
            {
                return manager.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(manager.GetType().FullName, name);
            }

            /// <summary>Reads one expected private field without a compile-time game-assembly dependency.</summary>
            /// <param name="manager">The background manager instance.</param>
            /// <param name="name">The exact field name.</param>
            /// <returns>The value read during installation.</returns>
            private static object? Read(MonoBehaviour manager, string name)
            {
                return Field(manager, name).GetValue(manager);
            }
        }
    }
}
