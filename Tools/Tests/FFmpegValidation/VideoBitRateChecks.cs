using System;
using MajdataPlay.FFmpeg.Internal;

internal static class VideoBitRateChecks
{
    private static int _checks;

    public static int Run()
    {
        _checks = 0;
        TestWarmupAndWindow();
        TestReorderingAndFuturePackets();
        TestVariableDurationAndGaps();
        TestResetAndInvalidValues();
        TestCapacityAndLargeRates();
        return _checks;
    }

    private static void Check(long actual, long expected, string message)
    {
        _checks++;
        if (actual != expected)
            throw new Exception("Bitrate: " + message + "; expected " + expected + ", got " + actual);
    }

    private static void TestWarmupAndWindow()
    {
        var tracker = new VideoBitRateTracker();
        Check(tracker.Measure(0), 0, "empty history");
        tracker.Add(0, 0.25, 1000);
        Check(tracker.Measure(0.25), 32000, "startup uses actual media duration");
        tracker.Add(0.25, 0.25, 2000);
        Check(tracker.Measure(0.5), 48000, "startup accumulates compressed bytes");
        tracker.Add(0.5, 0.5, 1000);
        Check(tracker.Measure(1), 32000, "full one-second window");
        tracker.Add(1, 0.5, 4000);
        Check(tracker.Measure(1.25), 40000, "both window boundaries clip packet intervals");
        Check(tracker.Measure(1.5), 40000, "expired packets are removed");
        Check(tracker.Measure(1.5), 40000, "repaint and pause do not change the estimate");
    }

    private static void TestReorderingAndFuturePackets()
    {
        var tracker = new VideoBitRateTracker();
        tracker.Add(0, 0.25, 1000);
        tracker.Add(0.75, 0.25, 8000);
        tracker.Add(0.25, 0.25, 2000);
        tracker.Add(0.5, 0.25, 3000);
        Check(tracker.Measure(0.5), 48000, "B-frame ordering excludes future reference packets");
        Check(tracker.Measure(0.75), 64000, "future samples survive an earlier measurement");
        Check(tracker.Measure(1), 112000, "all out-of-order packets enter their presentation window");
        tracker.Add(1, 0.25, 1000);
        Check(tracker.Measure(1.25), 112000, "pruning does not rely on insertion order");

        tracker.Reset();
        tracker.Add(10, 0.25, 1000);
        Check(tracker.Measure(1), 0, "future-only history is unknown");
        Check(tracker.Measure(10.25), 32000, "future-only history is retained");

        tracker.Reset();
        tracker.Add(-0.25, 0.25, 1000);
        tracker.Add(0, 0.25, 1000);
        Check(tracker.Measure(0.25), 32000, "negative stream timestamps preserve duration");
    }

    private static void TestVariableDurationAndGaps()
    {
        var tracker = new VideoBitRateTracker();
        tracker.Add(0, 2, 2000);
        Check(tracker.Measure(0.5), 8000, "long VFR packet is distributed over its duration");
        Check(tracker.Measure(1.5), 8000, "window inside a long packet");
        Check(tracker.Measure(2.5), 4000, "gap consumes media time");
        Check(tracker.Measure(3), 0, "fully elapsed data leaves an empty window");
        tracker.Add(3, 0.25, 1000);
        Check(tracker.Measure(3.25), 8000, "a later packet does not reset the elapsed timeline");
    }

    private static void TestResetAndInvalidValues()
    {
        var tracker = new VideoBitRateTracker();
        tracker.Add(100, 0.5, 100000);
        tracker.Reset();
        Check(tracker.Measure(100.5), 0, "seek clears old bytes");
        tracker.Add(10, 0.5, 1000);
        Check(tracker.Measure(10.5), 16000, "seek establishes a new timeline window");
        tracker.Reset();
        tracker.Add(double.NaN, 1, 1000);
        tracker.Add(double.PositiveInfinity, 1, 1000);
        tracker.Add(double.NegativeInfinity, 1, 1000);
        tracker.Add(0, double.NaN, 1000);
        tracker.Add(0, double.PositiveInfinity, 1000);
        tracker.Add(0, 0, 1000);
        tracker.Add(0, -1, 1000);
        tracker.Add(0, 1, 0);
        tracker.Add(0, 1, -1);
        tracker.Add(double.MaxValue, double.MaxValue, 1000);
        tracker.Add(double.MaxValue, 1, 1000);
        Check(tracker.Measure(1), 0, "invalid samples are ignored");
        tracker.Add(0, 1, 1000);
        Check(tracker.Measure(double.NaN), 0, "invalid measurement timestamp");
        Check(tracker.Measure(double.PositiveInfinity), 0, "infinite measurement timestamp");
        Check(tracker.Measure(1), 8000, "invalid measurement does not alter valid history");
    }

    private static void TestCapacityAndLargeRates()
    {
        var tracker = new VideoBitRateTracker();
        for (var i = 0; i < 4096; i++) tracker.Add(0, 0.25, 1);
        Check(tracker.Measure(0.25), 131072, "fixed capacity includes all retained bytes");
        tracker.Add(0.25, 0.25, 1000);
        Check(tracker.Measure(0.5), 0, "overflow never reports a silently truncated window");
        tracker.Add(0.5, 0.75, 1000);
        Check(tracker.Measure(1.25), 16000, "measurement recovers after discarded data leaves the window");
        tracker.Reset();
        tracker.Add(0, 1, int.MaxValue);
        Check(tracker.Measure(1), (long)int.MaxValue * 8, "rate arithmetic is wider than int");
        tracker.Reset();
        tracker.Add(0, 1e-12, int.MaxValue);
        Check(tracker.Measure(1e-12), long.MaxValue, "unrepresentable rates saturate without overflow");
        tracker.Reset();
        tracker.Add(0, 1, 1000);
        Check(tracker.Measure(1), 8000, "reset clears capacity recovery state");
    }
}
