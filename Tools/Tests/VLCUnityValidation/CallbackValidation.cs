using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LibVLCSharp;

// Exercise production callbacks without Unity. Unity supplies this attribute in builds.
namespace AOT
{
    sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type type) { }
    }
}

static class Program
{
    static readonly Type CallbackType = typeof(VlcCpuVideoOutput);

    static object Call(string name, params object[] arguments)
    {
        return CallbackType.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, arguments);
    }

    static IntPtr NewFormat(VlcCpuVideoOutput output, uint width, uint height, out int pitch)
    {
        var handle = (GCHandle)CallbackType.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(output);
        IntPtr chroma = Marshal.AllocHGlobal(4);
        IntPtr pitches = Marshal.AllocHGlobal(16);
        IntPtr lines = Marshal.AllocHGlobal(16);
        try
        {
            object[] arguments = { GCHandle.ToIntPtr(handle), chroma, width, height, pitches, lines };
            Check((uint)Call("Setup", arguments) == 3, "Three bounded buffers allocated");
            pitch = Marshal.ReadInt32(pitches);
            Check(pitch % 32 == 0, "Pitch aligned");
            Check(Marshal.ReadInt32(lines) % 32 == 0, "Lines aligned");
            var bytes = new byte[4];
            Marshal.Copy(chroma, bytes, 0, 4);
            Check(System.Text.Encoding.ASCII.GetString(bytes) == "RGBA", "RGBA byte order");
            return (IntPtr)arguments[0];
        }
        finally
        {
            Marshal.FreeHGlobal(chroma);
            Marshal.FreeHGlobal(pitches);
            Marshal.FreeHGlobal(lines);
        }
    }

    static void Frame(IntPtr format, int pitch, int height, byte value, bool display)
    {
        IntPtr planes = Marshal.AllocHGlobal(IntPtr.Size * 4);
        try
        {
            IntPtr token = (IntPtr)Call("Lock", format, planes);
            IntPtr data = Marshal.ReadIntPtr(planes);
            Check(data.ToInt64() % 32 == 0, "Pixels aligned");
            var frame = new byte[pitch * height];
            Array.Fill(frame, value);
            Marshal.Copy(frame, 0, data, frame.Length);
            Call("Unlock", format, token, planes);
            if (display)
                Call("Display", format, token);
        }
        finally { Marshal.FreeHGlobal(planes); }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static void Main()
    {
        var output = new VlcCpuVideoOutput(16384);
        IntPtr format = NewFormat(output, 7, 3, out int pitch);
        byte[] pixels = null;

        // VLC can unlock a dropped picture without ever calling display.
        for (int frame = 0; frame < 10000; ++frame)
            Frame(format, pitch, 3, (byte)frame, false);
        Check(!output.TryCopyFrame(ref pixels, out int width, out int height), "Undisplayed frames stay hidden");

        Frame(format, pitch, 3, 77, true);
        Check(output.TryCopyFrame(ref pixels, out width, out height), "Published frame readable");
        Check(width == 7 && height == 3 && pixels.Length == 84, "Odd-width packing");
        foreach (byte value in pixels)
            Check(value == 77, "No padding bytes");
        Check(!output.TryCopyFrame(ref pixels, out width, out height), "Read once");
        ValidateOrientation(output, format, pitch, ref pixels);

        Task producer = Task.Run(() =>
        {
            for (int frame = 0; frame < 5000; ++frame)
                Frame(format, pitch, 3, (byte)frame, true);
        });
        int reads = 0;
        while (!producer.IsCompleted)
        {
            if (!output.TryCopyFrame(ref pixels, out width, out height))
                continue;
            foreach (byte value in pixels)
                Check(value == pixels[0], "No torn concurrent frame");
            ++reads;
        }
        producer.GetAwaiter().GetResult();

        IntPtr resized = NewFormat(output, 31, 9, out int resizedPitch);
        Call("Cleanup", format);
        Frame(resized, resizedPitch, 9, 99, true);
        Check(output.TryCopyFrame(ref pixels, out width, out height) && width == 31 && height == 9,
            "Resize survives old format cleanup");
        Call("Cleanup", resized);
        Check(!output.TryCopyFrame(ref pixels, out width, out height), "Cleanup clears pending frame");
        ValidateRejectedDimensions(output);
        output.Dispose();
        output.Dispose();
        Console.WriteLine("PASS: RGBA, alignment, odd width, row orientation, 10000 dropped frames, " +
            "concurrent reads=" + reads + ", resize, bounds, cleanup, repeated dispose.");
    }

    static void ValidateOrientation(VlcCpuVideoOutput output, IntPtr format, int pitch, ref byte[] pixels)
    {
        IntPtr planes = Marshal.AllocHGlobal(IntPtr.Size * 4);
        try
        {
            IntPtr token = (IntPtr)Call("Lock", format, planes);
            IntPtr data = Marshal.ReadIntPtr(planes);
            var rows = new byte[pitch * 3];
            for (int y = 0; y < 3; ++y)
                for (int x = 0; x < 28; ++x)
                    rows[y * pitch + x] = (byte)(10 + y);
            Marshal.Copy(rows, 0, data, rows.Length);
            Call("Unlock", format, token, planes);
            Call("Display", format, token);
            output.TryCopyFrame(ref pixels, out _, out _);
            Check(pixels[0] == 12 && pixels[28] == 11 && pixels[56] == 10, "Vertical orientation");
        }
        finally { Marshal.FreeHGlobal(planes); }
    }

    static void ValidateRejectedDimensions(VlcCpuVideoOutput output)
    {
        var handle = (GCHandle)CallbackType.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(output);
        IntPtr parameters = Marshal.AllocHGlobal(48);
        try
        {
            object[] arguments = { GCHandle.ToIntPtr(handle), parameters, uint.MaxValue, 1u,
                IntPtr.Add(parameters, 16), IntPtr.Add(parameters, 32) };
            Check((uint)Call("Setup", arguments) == 0, "Oversized format rejected without native unwind");
            Check(!string.IsNullOrEmpty(output.Error), "Format failure diagnostic available to main thread");
        }
        finally { Marshal.FreeHGlobal(parameters); }
    }
}
