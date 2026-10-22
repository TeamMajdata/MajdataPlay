// Full software recording profile validation against the staged native libraries.
// Compile this file with EncoderNativeSmoke.c alongside it (included below):
// cc -std=c11 -Wall -Wextra -Werror -I<prefix>/include this.c
//    -L<native> -lavformat -lavcodec -lswscale -lavutil -lm -o software-encoder-smoke
// Run: software-encoder-smoke <new-existing-output-directory>
// All four codecs are required. Each CBR/VBR mode encodes flat and complex 2-second
// scenes and checks actual pixels, PTS gaps, frame counts, drain, and ownership.
// Complex scenes include a 0.5s burst, 0.5s flat recovery, then 1s sustained noise.
// x264/MPEG4 provide a flat-scene filler assertion. x265/VPX/AOM can undershoot;
// VPX/AOM VBR maximums are rate budgets rather than hard payload boundaries.
#define main LegacyEncoderNativeSmokeMain
#include "EncoderNativeSmoke.c"
#undef main

static int CheckRejectedX265Parameter(const char* parameter) {
    const AVCodec* codec = avcodec_find_encoder_by_name("libx265");
    AVCodecContext* context = avcodec_alloc_context3(codec);
    if (!Check(context != NULL, "invalid-parameter context allocation")) return 0;
    context->width = Width;
    context->height = Height;
    context->pix_fmt = AV_PIX_FMT_YUV420P;
    context->time_base = (AVRational){1, FrameRate};
    context->framerate = (AVRational){FrameRate, 1};
    context->bit_rate = TargetBitRate;
    context->thread_count = MaxThreads;
    int result = av_opt_set(context->priv_data, "x265-params", parameter, 0);
    if (!Native(result, "assign intentionally rejected x265 parameter")) {
        avcodec_free_context(&context);
        return 0;
    }
    result = avcodec_open2(context, codec, NULL);
    avcodec_free_context(&context);
    printf("Rejected x265 parameter '%s': result=%d\n", parameter, result);
    return Check(result == AVERROR(EINVAL), "native x265 opening rejects ignored parameter names and values");
}

int main(int argc, char** argv) {
    static const char* names[] = {"libx264", "libx265", "libaom-av1", "libvpx-vp9"};
    if (argc != 2) {
        fprintf(stderr, "Usage: %s <new-existing-output-directory>\n", argv[0]);
        return 2;
    }
    if (!Check(avcodec_version() == LIBAVCODEC_VERSION_INT && avformat_version() == LIBAVFORMAT_VERSION_INT
               && avutil_version() == LIBAVUTIL_VERSION_INT, "software profile ABI matches the compile headers")) return 1;
    printf("Native FFmpeg %s; full software profile; threads<=%d\n", av_version_info(), MaxThreads);
    for (unsigned int codec_index = 0; codec_index < sizeof(names) / sizeof(names[0]); ++codec_index) {
        const char* name = names[codec_index];
        const AVCodec* codec = avcodec_find_encoder_by_name(name);
        if (!Check(codec != NULL && !(codec->capabilities & AV_CODEC_CAP_HARDWARE),
                   "every requested software encoder is compiled into the staged native profile")) return 1;
        for (int cbr = 0; cbr <= 1; ++cbr) {
            for (int complex = 0; complex <= 1; ++complex) {
                char path[4096];
                const int length = snprintf(path, sizeof(path), "%s/%s-%s-%s.%s", argv[1], name,
                                            cbr ? "cbr" : "vbr", complex ? "complex" : "flat",
                                            codec->id == AV_CODEC_ID_VP9 ? "webm" : "mp4");
                if (!Check(length > 0 && length < (int)sizeof(path), "software output path is not truncated")) return 1;
                ComplexPattern = complex;
                if (!Encode(path, name, cbr) || !Decode(path, codec->id) || !CheckNoOverwrite(path)) return 1;
            }
        }
    }
    if (!CheckRejectedX265Parameter("majdataplay-unknown=1")
        || !CheckRejectedX265Parameter("strict-cbr=not-a-bool")) return 1;
    printf("PASS: %d checks; all four software codecs, both real rate-control modes, flat/complex pixels, "
           "PTS gaps, flush, worker limits, exclusive output, and x265 parameter rejection.\n", Checks);
    return 0;
}
