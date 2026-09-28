package net.majdata.majdataplay.oniimai;

/** Portrait render buffer is rotated into the landscape output, without crop or letterbox. */
final class DisplayGeometry {
    static final int BUFFER_WIDTH = 1080, BUFFER_HEIGHT = 1920;

    // TextureView first maps the buffer onto its bounds. Map those bounds to the
    // rotated output explicitly, rather than rotating an oversized/clipped View.
    static float[] textureMatrix(int outputWidth, int outputHeight, boolean reverse) {
        if (outputWidth <= 0 || outputHeight <= 0) throw new IllegalArgumentException("Display size");
        float w = outputWidth, h = outputHeight;
        return reverse
                ? new float[]{0, w/h, 0, -h/w, 0, h, 0, 0, 1}
                : new float[]{0, -w/h, w, h/w, 0, 0, 0, 0, 1};
    }
    // SurfaceControl receives the original portrait pixels, before TextureView's
    // implicit stretch. Keep the existing full-output mapping in both routes.
    static float[] surfaceMatrix(int outputWidth, int outputHeight, boolean reverse) {
        if (outputWidth <= 0 || outputHeight <= 0) throw new IllegalArgumentException("Display size");
        float w = outputWidth, h = outputHeight;
        return reverse
                ? new float[]{0, w/BUFFER_HEIGHT, 0, -h/BUFFER_WIDTH, 0, h, 0, 0, 1}
                : new float[]{0, -w/BUFFER_HEIGHT, w, h/BUFFER_WIDTH, 0, 0, 0, 0, 1};
    }
    static int angle(boolean reverse) { return reverse ? 270 : 90; }
}
