using System;
using MajdataPlay.IO;
internal static class OniimaiFrameChecks
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Frame merge check " + checks); }
    public static void Run()
    {
        for (int i = 0; i < 34; i++)
        {
            var touch = new bool[34]; var buttons = new bool[12]; var tc = new int[34]; var bc = new int[8];
            touch[(i + 1) % 34] = true;
            OniimaiFrame.Merge(new long[]{0, 1L << i, 0, 1L << i, 0}, touch, buttons, tc, bc);
            Check(touch[i]); Check(touch[(i + 1) % 34]); Check(tc[i] == 1);
            Array.Clear(tc, 0, tc.Length); OniimaiFrame.Merge(new long[]{0, 1L << i, 0, 0, 0}, touch, buttons, tc, bc);
            Check(tc[i] == 0); // held physical touch must not repeat a tap
        }
        for (int i = 0; i < 8; i++)
        {
            var touch = new bool[34]; var buttons = new bool[12]; var tc = new int[34]; var bc = new int[8];
            buttons[(i + 1) % 8] = true; bc[i] = 1;
            OniimaiFrame.Merge(new long[]{1L << i, 0, 1L << i, 0, 0}, touch, buttons, tc, bc);
            Check(buttons[i]); Check(buttons[(i + 1) % 8]); Check(bc[i] == 1);
            bc[i] = 2; OniimaiFrame.Merge(new long[]{1L << i, 0, 1L << i, 0, 0}, touch, buttons, tc, bc); Check(bc[i] == 2);
            Array.Clear(bc, 0, bc.Length); OniimaiFrame.Merge(new long[]{1L << i, 0, 0, 0, 0}, touch, buttons, tc, bc); Check(bc[i] == 0);
        }
        foreach (var flags in new long[]{0,8}) {
            var touch = new bool[34]; var buttons = new bool[12]; var tc = new int[34]; var bc = new int[8];
            var target = flags == 0 ? 9 : 3;
            OniimaiFrame.Merge(new long[]{256,0,256,0,flags},touch,buttons,tc,bc);
            Check(buttons[target]); Check(!buttons[target==3?9:3]); Check(bc[3]==(target==3?1:0));
            Array.Clear(bc,0,bc.Length);
            OniimaiFrame.Merge(new long[]{256,0,0,0,flags},touch,buttons,tc,bc); Check(bc[3]==0);
            Array.Clear(buttons,0,buttons.Length);
            OniimaiFrame.Merge(new long[]{0,0,0,0,flags},touch,buttons,tc,bc); Check(!buttons[target]);
        }
        Console.WriteLine("FrameMerge: " + checks + " checks passed");
    }
}
