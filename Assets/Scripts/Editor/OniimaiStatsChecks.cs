using System;
using MajdataPlay.IO;

internal static class OniimaiStatsChecks
{
    private sealed class Record { internal int Miss; internal string Song = ""; }
    private static int _checks;
    private static void Check(bool ok) { _checks++; if (!ok) throw new Exception("Stats retention check " + _checks); }
    internal static void Run()
    {
        _checks = 0;
        var state = new OniimaiStatsRetention<Record>();
        Check(state.Advance(true, false, 10));
        state.Capture(new Record { Miss = 187, Song = "first" }, false);
        Check(state.Frame.Miss == 187 && !state.IsFinal);
        // A final note arrives after the last 100 ms sample.
        var final = new Record { Miss = 188, Song = "first" };
        state.Capture(final, true);
        Check(state.Frame == final && state.IsFinal);
        state.Capture(new Record { Miss = 0 }, false);
        Check(state.Frame == final);
        Check(!state.Advance(true, false, 0)); // singleton freed during fade
        Check(state.Frame == final);
        for (int i = 0; i < 100; i++) // Result / TotalResult, including a disconnected monitor
        {
            Check(!state.Advance(false, true, 0));
            Check(state.Frame == final && state.Frame.Miss == 188 && state.IsFinal);
        }
        Check(state.Advance(false, false, 0)); // leave results while monitor is off
        Check(state.Frame == null && !state.IsFinal);
        Check(!state.Advance(false, false, 0));
        Check(state.Advance(true, false, 20));
        state.Capture(new Record { Miss = 7, Song = "retry" }, false);
        Check(state.Advance(true, false, 21)); // Game -> Game retry
        Check(state.Frame == null && !state.IsFinal);
        state.Capture(new Record { Miss = 8 }, false);
        Check(state.Advance(false, true, 0)); // no completed record: don't invent results
        Check(state.Frame == null);
        Check(state.Advance(true, false, 30));
        state.Capture(new Record { Miss = 9 }, true);
        Check(!state.Advance(false, true, 0));
        Check(state.Advance(true, false, 0)); // next course song starts before singleton ready
        Check(state.Frame == null);
        Check(state.Advance(true, false, 31));
        state.Capture(new Record { Song = "new", Miss = 1 }, false);
        Check(state.Frame.Song == "new" && !state.IsFinal);
        Check(state.Advance(false, false, 0)); // abort to song list
        Check(state.Frame == null);
        Check(state.Advance(true, false, 40));
        state.Capture(new Record { Miss = 12 }, true);
        Check(state.Advance(true, false, 41)); // completed practice round directly to next round
        Check(state.Frame == null && !state.IsFinal);
        Console.WriteLine("StatsRetention: " + _checks + " checks passed");
    }
}
