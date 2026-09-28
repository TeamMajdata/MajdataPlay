package net.majdata.majdataplay.oniimai;

/** Picks an advertised monitor mode; a requested mode is not proof of activation. */
final class OutputMode {
    static int frameRate(int saved) { return saved == 120 ? 120 : 60; }
    static boolean matches(float actual, int requested) {
        return Float.isFinite(actual) && Math.abs(actual - requested) < 1f;
    }
    static final class Mode {
        final int id, width, height;
        final float rate;
        Mode(int id, int width, int height, float rate) {
            this.id=id; this.width=width; this.height=height; this.rate=rate;
        }
        long pixels() { return (long)width*height; }
    }
    static Mode choose(Mode current, Mode[] modes, int requested) {
        Mode best=null;
        for (Mode mode:modes) {
            if (!matches(mode.rate,requested) || mode.width<=0 || mode.height<=0) continue;
            // Preserve the monitor aspect ratio, including portrait-oriented EDIDs.
            if (Math.abs((double)mode.width/mode.height-(double)current.width/current.height)>.03) continue;
            if (mode.id==current.id) return mode;
            boolean same=mode.width==current.width && mode.height==current.height;
            boolean bestSame=best!=null && best.width==current.width && best.height==current.height;
            if (best==null || (same&&!bestSame) || (same==bestSame&&mode.pixels()>best.pixels())) best=mode;
        }
        return best;
    }
}
