package net.majdata.majdataplay.oniimai;

public final class InputSnapshotTest {
    private static int checks;
    private static void eq(long expected, long actual) {
        checks++; if (expected != actual) throw new AssertionError("expected " + expected + ", got " + actual + " (#" + checks + ")");
    }
    private static InputSnapshot armed() { InputSnapshot s = new InputSnapshot(); s.enabled(true); s.read(0); return s; }
    public static void main(String[] args) {
        InputSnapshot s = new InputSnapshot();
        s.keyboard(1, true); eq(0, s.read(0)[0]); // off by default
        s.enabled(true); eq(0, s.read(0)[0]); // enabling a held key must not press a menu
        s.keyboard(0, true); s.keyboard(1, true); eq(1, s.read(1)[0]); eq(0, s.read(2)[2]);
        s.keyboard(0, true); eq(0, s.read(3)[0]);
        s = armed(); s.keyboard(1, true); s.keyboard(0, true);
        eq(0,s.diagnostic(1)[0]); eq(2,s.diagnostic(1)[3]); // monitor must not consume a short input pulse
        long[] f = s.read(1); eq(1, f[0]); eq(1, f[2]); eq(0, s.read(2)[0]); // short pulse exactly once
        s = armed(); s.keyboard(1, true); s.hid(1, 1); s.read(1); s.keyboard(0, false);
        eq(1, s.read(2)[0]); eq(0, s.read(2)[2]); // one source cannot release the other
        s.hid(0, 3); eq(0, s.read(3)[0]);
        s = armed(); s.touch(1L << 33, 1); eq(1L << 33, s.read(1)[1]);
        eq(1L<<33,s.diagnostic(2)[1]); eq(1,s.diagnostic(2)[3]);
        eq(1L << 33, s.read(501)[1]); eq(0, s.read(502)[1]); eq(0, s.read(503)[4]); // stale sensor release
        eq(0,s.diagnostic(504)[1]); eq(0,s.diagnostic(504)[3]);
        s = armed(); s.hid(255, 1); s.read(1); eq(0, s.read(502)[0]);
        s = armed(); s.keyboard(256,true); s.keyboard(0,true); eq(256,s.read(1)[0]); eq(0,s.read(2)[0]);
        s = armed(); s.hid(256,1); eq(256,s.read(1)[0]); eq(0,s.read(2)[2]);
        s.panel(true); eq(0,s.read(3)[0]); s.panel(false); eq(0,s.read(4)[0]);
        s.hid(0,5); s.hid(256,6); eq(256,s.read(6)[0]);
        s = armed(); s.hid(0, 1); s.read(1); s.keyboard(1, true); s.keyboard(0, true);
        eq(1, s.read(502)[0]); // expired HID cannot erase a fresh keyboard pulse
        s = armed(); s.touch(1, 0); s.read(0); s.read(501);
        s.touch(1, 502); eq(0, s.read(502)[1]); // stale held reports cannot re-press a menu
        s.touch(0, 503); s.touch(1, 504); eq(1, s.read(504)[1]);
        s = armed(); s.touch(3, 0); s.panel(true); eq(0, s.read(1)[1]);
        s.panel(false); eq(0, s.read(2)[1]); s.touch(0, 3); s.touch(3, 4); eq(3, s.read(4)[1]);
        s.foreground(false); eq(0, s.read(5)[1]); s.foreground(true); eq(0, s.read(6)[1]);
        s.touch(0, 7); s.touch(2, 8); eq(2, s.read(8)[1]);
        s.disconnect(); eq(0, s.read(9)[1]);
        s = armed(); s.touch(1L << 16, 0); eq(1L << 16, s.read(0)[3]);
        s.touch(3L << 16, 1); eq(0, s.read(1)[3]);
        s.touch(1L << 17, 2); eq(0, s.read(2)[3]);
        s.touch(0, 3); s.touch(1L << 17, 4); eq(1L << 17, s.read(4)[3]);
        s = armed(); s.awaitStreams(true, true); s.read(0);
        s.touch(1, 1); eq(0, s.read(1)[1]); // first report held during connection: suppress
        s.touch(0, 2); s.touch(1, 3); eq(0, s.read(3)[1]); // still waiting for HID's initial report
        s.hid(0, 4); s.touch(0, 5); s.touch(1, 6); eq(1, s.read(6)[1]);
        for (int i = 0; i < 34; i++) {
            s = armed(); s.touch(1L << i, 0); s.touch(0, 1); f = s.read(2);
            eq(1L << i, f[1]); eq(1L << i, f[3]); eq(0, s.read(3)[1]);
        }
        for (int i = 0; i < 8; i++) {
            s = armed(); s.keyboard(1 << i, true); f = s.read(0);
            eq(1 << i, f[0]); eq(1 << i, f[2]);
            for (int n = 0; n < 120; n++) { s.keyboard(1 << i, true); eq(0, s.read(n)[2]); }
        }
        eq(90, DisplayGeometry.angle(false)); eq(270, DisplayGeometry.angle(true));
        // Check all corners and the centre. 4K must fill exactly like 1080p;
        // changing direction must not crop or stretch a circle on a 16:9 monitor.
        for (int[] output : new int[][]{{1920,1080},{3840,2160},{1280,720},{2560,1440}}) {
            int w = output[0], h = output[1];
            for (boolean reverse : new boolean[]{false,true}) {
                float[] m = DisplayGeometry.textureMatrix(w,h,reverse);
                int[][] src = {{0,0},{w,0},{w,h},{0,h},{w/2,h/2}};
                int[][] dst = reverse ? new int[][]{{0,h},{0,0},{w,0},{w,h},{w/2,h/2}}
                        : new int[][]{{w,0},{w,h},{0,h},{0,0},{w/2,h/2}};
                for (int i=0;i<src.length;i++) {
                    eq(dst[i][0], Math.round(m[0]*src[i][0]+m[1]*src[i][1]+m[2]));
                    eq(dst[i][1], Math.round(m[3]*src[i][0]+m[4]*src[i][1]+m[5]));
                }
                float xScale = Math.abs(m[3])*w/DisplayGeometry.BUFFER_WIDTH;
                float yScale = Math.abs(m[1])*h/DisplayGeometry.BUFFER_HEIGHT;
                eq(Math.round(xScale*10000), Math.round(yScale*10000));
            }
        }
        try { DisplayGeometry.textureMatrix(0,1080,false); throw new AssertionError("zero width accepted"); }
        catch (IllegalArgumentException expected) { checks++; }
        System.out.println("InputSnapshot/DisplayGeometry: " + checks + " checks passed");
    }
}
