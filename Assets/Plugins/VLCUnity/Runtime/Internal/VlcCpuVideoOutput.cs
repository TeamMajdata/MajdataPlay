using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;

namespace LibVLCSharp
{
    // vmem has no picture-release callback: unlock can be followed by a dropped frame.
    // Decoded slots are therefore reusable at the next lock, even without display.
    // Only display publishes a frame, so presentation still follows VLC's clock.
    internal sealed class VlcCpuVideoOutput : IDisposable
    {
        const int BufferCount = 3;
        const int MaxFrameBytes = 128 * 1024 * 1024;
        // At most 384 MiB native buffers plus one 128 MiB packed upload array.
        const long MaxAllocatedBytes = 384L * 1024 * 1024;
        readonly object _gate = new object();
        readonly List<Format> _formats = new List<Format>();
        readonly int _maxTextureSize;
        GCHandle _handle;
        Format _latest;
        long _allocatedBytes;
        bool _released;
        string _error;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate IntPtr LockCallback(IntPtr opaque, IntPtr planes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void UnlockCallback(IntPtr opaque, IntPtr picture, IntPtr planes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void DisplayCallback(IntPtr opaque, IntPtr picture);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate uint FormatCallback(ref IntPtr opaque, IntPtr chroma,
            ref uint width, ref uint height, IntPtr pitches, IntPtr lines);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void CleanupCallback(IntPtr opaque);

        // Static delegates and handles survive GC and IL2CPP reverse P/Invoke.
        static readonly LockCallback LockThunk = Lock;
        static readonly UnlockCallback UnlockThunk = Unlock;
        static readonly DisplayCallback DisplayThunk = Display;
        static readonly FormatCallback FormatThunk = Setup;
        static readonly CleanupCallback CleanupThunk = Cleanup;

        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        static extern void libvlc_video_set_callbacks(IntPtr player,
            LockCallback @lock, UnlockCallback unlock, DisplayCallback display, IntPtr opaque);
        [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl)]
        static extern void libvlc_video_set_format_callbacks(IntPtr player,
            FormatCallback setup, CleanupCallback cleanup);

        internal VlcCpuVideoOutput(int maxTextureSize)
        {
            _maxTextureSize = maxTextureSize;
            _handle = GCHandle.Alloc(this);
        }

        internal void Attach(IntPtr player)
        {
            libvlc_video_set_callbacks(player, LockThunk, UnlockThunk, DisplayThunk,
                GCHandle.ToIntPtr(_handle));
            libvlc_video_set_format_callbacks(player, FormatThunk, CleanupThunk);
        }

        internal string Error => Volatile.Read(ref _error);

        internal bool TryCopyFrame(ref byte[] pixels, out int width, out int height)
        {
            lock (_gate)
            {
                width = height = 0;
                if (_latest == null || _latest.Ready < 0)
                    return false;
                var format = _latest;
                var slot = format.Slots[format.Ready];
                width = format.Width;
                height = format.Height;
                int rowBytes = checked(width * 4);
                int size = checked(rowBytes * height);
                if (pixels == null || pixels.Length != size)
                    pixels = new byte[size];

                // VLC RGBA is byte order R,G,B,A. Reverse rows here to normalize
                // VLC's top-first memory to Unity's bottom-first texture coordinates.
                // Padding never becomes part of Unity's packed RGBA32 upload.
                for (int y = 0; y < height; ++y)
                    Marshal.Copy(IntPtr.Add(slot.Pixels, y * format.Pitch), pixels,
                        (height - y - 1) * rowBytes, rowBytes);
                slot.State = SlotState.Free;
                format.Ready = -1;
                return true;
            }
        }

        [MonoPInvokeCallback(typeof(FormatCallback))]
        static uint Setup(ref IntPtr opaque, IntPtr chroma,
            ref uint width, ref uint height, IntPtr pitches, IntPtr lines)
        {
            VlcCpuVideoOutput owner = null;
            try
            {
                var target = GCHandle.FromIntPtr(opaque).Target;
                owner = target as VlcCpuVideoOutput ?? ((Format)target).Owner;
                lock (owner._gate)
                {
                    if (owner._released || width == 0 || height == 0 ||
                        width > owner._maxTextureSize || height > owner._maxTextureSize)
                        throw new ArgumentOutOfRangeException("Video dimensions exceed this graphics device's texture limit.");
                    int pitch = checked(((int)width * 4 + 31) & ~31);
                    int lineCount = checked(((int)height + 31) & ~31);
                    int size = checked(pitch * lineCount);
                    if (size > MaxFrameBytes || owner._allocatedBytes + (long)size * BufferCount > MaxAllocatedBytes)
                        throw new ArgumentOutOfRangeException("Video buffers exceed the 512 MiB output budget.");
                    var format = new Format(owner, (int)width, (int)height, pitch, size);
                    try
                    {
                        owner._formats.Add(format);
                    }
                    catch
                    {
                        format.Dispose();
                        throw;
                    }
                    owner._allocatedBytes += (long)size * BufferCount;
                    owner._latest = format;
                    opaque = GCHandle.ToIntPtr(format.Handle);
                    // VLC_CODEC_RGBA: include/vlc_fourcc.h specifies memory order RGBA.
                    Marshal.WriteByte(chroma, 0, (byte)'R');
                    Marshal.WriteByte(chroma, 1, (byte)'G');
                    Marshal.WriteByte(chroma, 2, (byte)'B');
                    Marshal.WriteByte(chroma, 3, (byte)'A');
                    Marshal.WriteInt32(pitches, pitch);
                    Marshal.WriteInt32(lines, lineCount);
                    return BufferCount;
                }
            }
            catch (Exception error)
            {
                if (owner != null)
                    Volatile.Write(ref owner._error, error.Message);
                return 0; // Never unwind a managed exception through libVLC.
            }
        }

        [MonoPInvokeCallback(typeof(LockCallback))]
        static IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            var format = (Format)GCHandle.FromIntPtr(opaque).Target;
            lock (format.Owner._gate)
            {
                // vmem serializes prepare/display for a given format. Waiting only
                // matters if a future implementation prepares several frames at once.
                while (true)
                {
                    for (int i = 0; i < format.Slots.Length; ++i)
                    {
                        var slot = format.Slots[i];
                        if (slot.State == SlotState.Writing || slot.State == SlotState.Ready)
                            continue;
                        slot.State = SlotState.Writing;
                        slot.Token = new IntPtr(++format.Sequence);
                        Marshal.WriteIntPtr(planes, slot.Pixels);
                        return slot.Token;
                    }
                    Monitor.Wait(format.Owner._gate);
                }
            }
        }

        [MonoPInvokeCallback(typeof(UnlockCallback))]
        static void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            var format = (Format)GCHandle.FromIntPtr(opaque).Target;
            lock (format.Owner._gate)
            {
                foreach (var slot in format.Slots)
                    if (slot.Token == picture && slot.State == SlotState.Writing)
                    {
                        slot.State = SlotState.Decoded;
                        break;
                    }
                Monitor.PulseAll(format.Owner._gate);
            }
        }

        [MonoPInvokeCallback(typeof(DisplayCallback))]
        static void Display(IntPtr opaque, IntPtr picture)
        {
            var format = (Format)GCHandle.FromIntPtr(opaque).Target;
            lock (format.Owner._gate)
            {
                for (int i = 0; i < format.Slots.Length; ++i)
                    if (format.Slots[i].Token == picture && format.Slots[i].State == SlotState.Decoded)
                    {
                        if (format.Ready >= 0)
                            format.Slots[format.Ready].State = SlotState.Free;
                        format.Ready = i;
                        format.Slots[i].State = SlotState.Ready;
                        return;
                    }
            }
        }

        // Native signature is void(void *opaque), not void(void **opaque).
        [MonoPInvokeCallback(typeof(CleanupCallback))]
        static void Cleanup(IntPtr opaque)
        {
            var format = (Format)GCHandle.FromIntPtr(opaque).Target;
            lock (format.Owner._gate)
            {
                if (format.Owner._latest == format)
                    format.Owner._latest = null;
                format.Owner._formats.Remove(format);
                format.Owner._allocatedBytes -= (long)format.Size * BufferCount;
                format.Dispose();
            }
        }

        // Must run AFTER MediaPlayer.Dispose, which joins VLC's output threads.
        // Stop alone is insufficient because libVLC 4 stops asynchronously.
        public void Dispose()
        {
            lock (_gate)
            {
                if (_released)
                    return;
                _released = true;
                foreach (var format in _formats)
                    format.Dispose();
                _formats.Clear();
                _latest = null;
                _allocatedBytes = 0;
                if (_handle.IsAllocated)
                    _handle.Free();
            }
        }

        enum SlotState { Free, Writing, Decoded, Ready }

        sealed class Slot
        {
            internal IntPtr Allocation;
            internal IntPtr Pixels;
            internal IntPtr Token;
            internal SlotState State;
        }

        sealed class Format : IDisposable
        {
            internal readonly VlcCpuVideoOutput Owner;
            internal readonly int Width, Height, Pitch, Size;
            internal readonly Slot[] Slots = new Slot[BufferCount];
            internal GCHandle Handle;
            internal int Ready = -1;
            internal long Sequence;

            internal Format(VlcCpuVideoOutput owner, int width, int height, int pitch, int size)
            {
                Owner = owner;
                Width = width;
                Height = height;
                Pitch = pitch;
                Size = size;
                try
                {
                    for (int i = 0; i < Slots.Length; ++i)
                    {
                        var slot = Slots[i] = new Slot();
                        slot.Allocation = Marshal.AllocHGlobal(checked(size + 31));
                        slot.Pixels = new IntPtr((slot.Allocation.ToInt64() + 31) & ~31L);
                    }
                    Handle = GCHandle.Alloc(this);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                foreach (var slot in Slots)
                    if (slot != null && slot.Allocation != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(slot.Allocation);
                        slot.Allocation = IntPtr.Zero;
                    }
                if (Handle.IsAllocated)
                    Handle.Free();
            }
        }
    }
}
