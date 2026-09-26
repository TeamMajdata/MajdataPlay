using System;

namespace MajdataPlay.IO
{
    // Pure frame merge, kept independent of Unity/JNI for reproducible regression tests.
    public static class OniimaiFrame
    {
        public static void Merge(long[] frame, Span<bool> touch, Span<bool> buttons,
                                   Span<int> touchClicks, Span<int> buttonClicks)
        {
            if (frame == null || frame.Length != 5) return;
            for (var i = 0; i < 8; i++)
            {
                buttons[i] |= (frame[0] & (1L << i)) != 0;
                if ((frame[2] & (1L << i)) != 0) buttonClicks[i] = Math.Max(1, buttonClicks[i]);
            }
            for (var i = 0; i < 34; i++)
            {
                touch[i] |= (frame[1] & (1L << i)) != 0;
                if ((frame[3] & (1L << i)) != 0) touchClicks[i] = Math.Max(1, touchClicks[i]);
            }
            // Separate auxiliary switch. Keep native P1 behavior by default; A4 is optional.
            var p1Target = (frame[4] & 8) != 0 ? 3 : 9;
            buttons[p1Target] |= (frame[0] & 256) != 0;
            if (p1Target == 3 && (frame[2] & 256) != 0)
                buttonClicks[3] = Math.Max(1, buttonClicks[3]);
            // InputManager's existing path merges physical C1/C2 -> game C (33 zones).
        }
    }
}
