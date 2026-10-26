package net.majdata.ffmpegvalidation;

import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.os.Build;

/** Runs actual FFmpeg encoding with explicitly selected hardware components in a device VM. */
public final class AndroidEncoderSmoke {
    private static final String[] ENCODERS = {
        "h264_mediacodec", "hevc_mediacodec", "vp8_mediacodec",
        "vp9_mediacodec", "mpeg4_mediacodec", "av1_mediacodec"
    };
    private static final String[] MIMES = {
        "video/avc", "video/hevc", "video/x-vnd.on2.vp8",
        "video/x-vnd.on2.vp9", "video/mp4v-es", "video/av01"
    };

    private AndroidEncoderSmoke() { }

    /** Verifies the staged ABI, JNI initialization and all six encoder registrations. */
    private static native boolean checkProfile();

    /** Rejects an unavailable named component instead of falling back to software. */
    private static native boolean rejectMissingComponent(boolean ndk);

    /** Encodes and software-decodes sixty frames, returning the number of passed assertions. */
    private static native int roundtrip(String directory, String encoder, String component,
                                         boolean cbr, boolean ndk, boolean complex);

    /** Requires the explicit Android hardware classification rather than guessing from a name. */
    private static MediaCodecInfo hardwareCodec(MediaCodecInfo[] codecs, String mime) {
        for (MediaCodecInfo codec : codecs) {
            if (!codec.isEncoder() || codec.isAlias() || !codec.isHardwareAccelerated() || codec.isSoftwareOnly()) {
                continue;
            }
            for (String type : codec.getSupportedTypes()) {
                if (!mime.equalsIgnoreCase(type)) {
                    continue;
                }
                MediaCodecInfo.CodecCapabilities capabilities = codec.getCapabilitiesForType(type);
                MediaCodecInfo.VideoCapabilities video = capabilities.getVideoCapabilities();
                boolean nv12 = false;
                for (int color : capabilities.colorFormats) {
                    nv12 |= color == MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420SemiPlanar;
                }
                if (nv12 && video.areSizeAndRateSupported(320, 192, 30)
                        && video.getBitrateRange().contains(200000)) {
                    return codec;
                }
            }
        }
        return null;
    }

    /** Loads only the supplied native directory and tests every usable hardware format. */
    public static void main(String[] args) {
        try {
            if (args.length != 2 || Build.VERSION.SDK_INT < 29) {
                throw new IllegalArgumentException("Usage: AndroidEncoderSmoke <native-dir> <output-dir>; API 29+ required for hardware classification");
            }
            System.load(args[0] + "/libandroid-encoder-smoke.so");
            if (!checkProfile()) {
                throw new AssertionError("Native profile validation failed");
            }
            if (!rejectMissingComponent(false) || !rejectMissingComponent(true)) {
                throw new AssertionError("Explicit component rejection failed");
            }
            MediaCodecInfo[] codecs = new MediaCodecList(MediaCodecList.REGULAR_CODECS).getCodecInfos();
            int runs = 0;
            int checks = 0;
            int h264VbrRuns = 0;
            for (int index = 0; index < ENCODERS.length; ++index) {
                MediaCodecInfo codec = hardwareCodec(codecs, MIMES[index]);
                if (codec == null) {
                    System.out.println("SKIP: " + ENCODERS[index] + "; no hardware NV12 component supports the fixture");
                    continue;
                }
                System.out.println("DEVICE: " + ENCODERS[index] + " component=" + codec.getName()
                        + " hardware=" + codec.isHardwareAccelerated() + " softwareOnly=" + codec.isSoftwareOnly());
                MediaCodecInfo.EncoderCapabilities capability = codec.getCapabilitiesForType(MIMES[index]).getEncoderCapabilities();
                for (boolean cbr : new boolean[] {false, true}) {
                    int mode = cbr ? MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_CBR
                                   : MediaCodecInfo.EncoderCapabilities.BITRATE_MODE_VBR;
                    if (!capability.isBitrateModeSupported(mode)) {
                        System.out.println("SKIP: " + ENCODERS[index] + "; unsupported bitrate mode=" + mode);
                        continue;
                    }
                    for (boolean ndk : new boolean[] {false, true}) {
                        for (boolean complex : new boolean[] {false, true}) {
                            int result = roundtrip(args[1], ENCODERS[index], codec.getName(), cbr, ndk, complex);
                            if (result < 0) {
                                throw new AssertionError("Roundtrip failed for " + ENCODERS[index]);
                            }
                            checks += result;
                            ++runs;
                            if (index == 0 && !cbr) {
                                ++h264VbrRuns;
                            }
                        }
                    }
                }
            }
            if (h264VbrRuns != 4) {
                throw new AssertionError("H.264 hardware JNI/NDK VBR flat/complex runs are mandatory");
            }
            System.out.println("PASS: " + runs + " hardware roundtrips; " + checks
                    + " assertions; pixels, PTS gaps, frame count, EOF, container and exclusive output");
            System.out.println("UNVERIFIED: driver bitrate-mode readback, maximum bitrate/VBV, Surface input, Unity capture and other devices");
        } catch (Throwable error) {
            error.printStackTrace();
            System.out.println("FAIL: Android native hardware encoding");
            System.exit(1);
        }
    }
}
