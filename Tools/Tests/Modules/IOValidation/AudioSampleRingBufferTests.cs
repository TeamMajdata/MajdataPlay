using System;
using MajdataPlay.IO;

internal static partial class Program
{
    /// <summary>Checks channel reduction, 16-bit scaling, wraparound, overflow accounting, and whole-frame reads of the audio recorder buffer.</summary>
    static void VerifyAudioSampleRingBuffer()
    {
        var buffer = new AudioSampleRingBuffer(2, 4);
        // A 4-channel mix keeps only its front pair.
        buffer.Write(new float[] { 1, 2, 9, 9, 3, 4, 9, 9 }, 4);
        Check(buffer.AvailableFrames == 2, "multichannel writes keep one frame per source frame");
        var output = new float[8];
        Check(buffer.Read(output.AsSpan(0, 3)) == 2, "reads return only whole frames");
        Check(output[0] == 1 && output[1] == 2, "the front pair is kept in channel order");

        buffer.Write(new short[] { 16384, -32768, 0, 32767, 8192, 8192 }, 2);
        Check(buffer.AvailableFrames == 4, "16-bit frames fill the remaining capacity across the wrap point");
        buffer.Write(new float[] { 7, 7, 8, 8 }, 2);
        Check(buffer.AvailableFrames == 4 && buffer.DroppedFrames == 2, "a full buffer drops new frames instead of overwriting");

        Array.Clear(output, 0, output.Length);
        Check(buffer.Read(output) == 8, "a read drains every buffered frame");
        Check(output[0] == 3 && output[1] == 4, "older frames are read first");
        Check(output[2] == 0.5f && output[3] == -1f && output[4] == 0f && output[5] == 32767f / 32768f,
            "16-bit samples are scaled to the nominal float range");
        Check(output[6] == 0.25f && output[7] == 0.25f, "frames written after the wrap point are read in order");
        Check(buffer.Read(output) == 0, "an empty buffer reads nothing");

        buffer.Clear();
        Check(buffer.DroppedFrames == 0 && buffer.AvailableFrames == 0, "clearing resets the frame and dropped counts");
        buffer.Write(new float[] { 5, 6, 7 }, 2);
        Check(buffer.AvailableFrames == 1, "a trailing partial frame is ignored");
        Expect<ArgumentOutOfRangeException>(() => buffer.Write(new float[] { 1 }, 1), "a source with fewer channels is rejected");
        Expect<ArgumentOutOfRangeException>(() => new AudioSampleRingBuffer(0, 4), "zero channels are rejected");
        Expect<ArgumentOutOfRangeException>(() => new AudioSampleRingBuffer(2, 0), "zero capacity is rejected");
        VerifyAudioSampleRingBufferSilence();
    }

    /// <summary>Checks that silent frames fill time in order, clear stale samples across the wrap point, and respect capacity.</summary>
    static void VerifyAudioSampleRingBufferSilence()
    {
        var buffer = new AudioSampleRingBuffer(2, 4);
        var output = new float[8];
        Check(buffer.Write(new float[] { 1, 1, 2, 2, 3, 3 }, 2) == 3, "a write reports the frames it stored");
        Check(buffer.Read(output.AsSpan(0, 4)) == 4, "two frames are consumed so the next writes wrap");
        // The free region now starts at frame 3 and wraps to frames 0 and 1, which still hold stale samples.
        Check(buffer.WriteSilence(2) == 2, "silence reports the frames it stored");
        Check(buffer.Write(new float[] { 4, 4 }, 2) == 1, "a frame after the silence is stored behind it");
        Check(buffer.AvailableFrames == 4 && buffer.DroppedFrames == 0, "silence and audio fill the buffer without drops");

        Array.Clear(output, 0, output.Length);
        Check(buffer.Read(output) == 8, "a read drains audio, silence, and audio in order");
        Check(output[0] == 3 && output[1] == 3, "audio written before the silence is read first");
        Check(output[2] == 0 && output[3] == 0 && output[4] == 0 && output[5] == 0, "silence written across the wrap point overwrites stale samples");
        Check(output[6] == 4 && output[7] == 4, "audio written after the silence follows it");

        Check(buffer.WriteSilence(0) == 0 && buffer.AvailableFrames == 0, "zero silent frames store nothing");
        buffer.Write(new float[] { 5, 5 }, 2);
        Check(buffer.WriteSilence(5) == 3, "silence beyond the free space is truncated to the capacity");
        Check(buffer.AvailableFrames == 4 && buffer.DroppedFrames == 2, "silence that does not fit is counted as dropped");
        Check(buffer.WriteSilence(1) == 0 && buffer.DroppedFrames == 3, "a full buffer drops silence like audio");
        Check(buffer.Write(new short[] { 1, 1 }, 2) == 0 && buffer.DroppedFrames == 4, "a full buffer stores no 16-bit audio either");
        Expect<ArgumentOutOfRangeException>(() => buffer.WriteSilence(-1), "a negative silence length is rejected");
    }

    /// <summary>Requires an operation to throw the expected exception type.</summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="operation">The operation to run.</param>
    /// <param name="message">The expectation description.</param>
    static void Expect<TException>(Action operation, string message) where TException : Exception
    {
        try
        {
            operation();
        }
        catch (TException)
        {
            Check(true, message);
            return;
        }
        throw new InvalidOperationException(message);
    }
}
