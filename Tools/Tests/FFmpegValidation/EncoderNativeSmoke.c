// Portable native recording validation for the pinned FFmpeg libraries.
// Build: cc -std=c11 -Wall -Wextra -Werror -I<prefix>/include this.c
//        -L<prefix>/lib -lavformat -lavcodec -lswscale -lavutil -lm -o encoder-native-smoke
// Run:   encoder-native-smoke <existing-output-directory> [encoder-name]
// Default: MPEG4 software CBR/VBR. Optional h264_videotoolbox forbids software fallback.
// MPEG4 CBR has a target-filling assertion. Optional hardware mode checks accepted
// CBR configuration and measured payload; constant target padding is not validated.
// This checks native encoding/muxing; Unity rendering and managed selection need separate tests.
#include <errno.h>
#include <inttypes.h>
#include <limits.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#if defined(__APPLE__)
#include <TargetConditionals.h>
#endif
#if defined(_WIN32)
#include <fcntl.h>
#include <io.h>
#include <sys/stat.h>
#endif
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/mem.h>
#include <libavutil/opt.h>
#include <libswscale/swscale.h>

enum { Width = 128, Height = 64, FrameRate = 30, FrameCount = 60, MaxThreads = 2 };
static const int64_t TargetBitRate = 200000;
static const int64_t MaximumBitRate = 400000;
static int Checks;
// The shared software suite enables a noise burst, quiet recovery, then sustained motion.
static int ComplexPattern;

typedef struct Output {
    FILE* file;
    AVIOContext* io;
    AVFormatContext* muxer;
    AVCodecContext* encoder;
    AVStream* stream;
    AVFrame* frame;
    AVPacket* packet;
    struct SwsContext* scaler;
    int packets, encoder_eof;
    int64_t bytes, last_second_bytes, previous_pts;
    int64_t packet_bytes[FrameCount], packet_pts[FrameCount];
} Output;

typedef struct Input {
    AVFormatContext* demuxer;
    AVCodecContext* decoder;
    AVPacket* packet;
    AVFrame* frame;
    struct SwsContext* scaler;
    int stream, frames, input_eof, drain_sent;
} Input;

static int Check(int condition, const char* message) {
    if (!condition) {
        fprintf(stderr, "FAIL: %s\n", message);
        return 0;
    }
    ++Checks;
    return 1;
}

static int Native(int result, const char* operation) {
    if (result < 0) {
        char error[AV_ERROR_MAX_STRING_SIZE];
        av_strerror(result, error, sizeof(error));
        fprintf(stderr, "FAIL: %s: %s (%d)\n", operation, error, result);
        return 0;
    }
    ++Checks;
    return 1;
}

// Keep a deliberate one-frame timestamp gap; no frame is duplicated to fill it.
static int64_t FrameIndex(int index) {
    return index + (index >= FrameCount / 2 ? 1 : 0);
}

// Legacy Windows CRT fopen may silently ignore the C11 x flag.
// Create the descriptor exclusively before transferring ownership to stdio.
static FILE* OpenExclusive(const char* path) {
#if defined(_WIN32)
    const int descriptor = _open(path, _O_WRONLY | _O_CREAT | _O_EXCL | _O_BINARY, _S_IREAD | _S_IWRITE);
    if (descriptor < 0) return NULL;
    FILE* file = _fdopen(descriptor, "wb");
    if (!file) {
        const int error = errno;
        _close(descriptor);
        errno = error;
    }
    return file;
#else
    return fopen(path, "wbx");
#endif
}

static int Write(void* opaque, const uint8_t* buffer, int size) {
    FILE* file = (FILE*)opaque;
    const size_t written = fwrite(buffer, 1, (size_t)size, file);
    if (written != (size_t)size) return AVERROR(errno ? errno : EIO);
    return size;
}

// Fixtures are small. Reject offsets outside C long rather than truncate them on Win32.
static int64_t Seek(void* opaque, int64_t offset, int origin) {
    FILE* file = (FILE*)opaque;
    origin &= ~AVSEEK_FORCE;
    if (origin == AVSEEK_SIZE) {
        const long current = ftell(file);
        if (current < 0 || fseek(file, 0, SEEK_END)) return AVERROR(errno ? errno : EIO);
        const long length = ftell(file);
        if (length < 0 || fseek(file, current, SEEK_SET)) return AVERROR(errno ? errno : EIO);
        return length;
    }
    if (offset < LONG_MIN || offset > LONG_MAX) return AVERROR(EINVAL);
    if (fseek(file, (long)offset, origin)) return AVERROR(errno ? errno : EIO);
    const long position = ftell(file);
    return position < 0 ? AVERROR(errno ? errno : EIO) : position;
}

static void CloseOutput(Output* test) {
    sws_freeContext(test->scaler);
    av_frame_free(&test->frame);
    av_packet_free(&test->packet);
    avcodec_free_context(&test->encoder);
    if (test->muxer) test->muxer->pb = NULL;
    avformat_free_context(test->muxer);
    if (test->io) av_freep(&test->io->buffer);
    avio_context_free(&test->io);
    if (test->file) fclose(test->file);
}

static void CloseInput(Input* test) {
    sws_freeContext(test->scaler);
    av_frame_free(&test->frame);
    av_packet_free(&test->packet);
    avcodec_free_context(&test->decoder);
    avformat_close_input(&test->demuxer);
}

// Receive until the encoder asks for input or confirms terminal EOF.
static int ReceivePackets(Output* test, int draining) {
    for (int iteration = 0; iteration < FrameCount + 16; ++iteration) {
        const int result = avcodec_receive_packet(test->encoder, test->packet);
        if (result == AVERROR(EAGAIN)) return Check(!draining, "encoder drain must reach EOF");
        if (result == AVERROR_EOF) {
            test->encoder_eof = 1;
            return Check(draining, "encoder EOF follows the explicit flush");
        }
        if (!Native(result, "receive encoded packet")) return 0;
        if (!Check(test->packet->pts >= 0 && test->packet->pts > test->previous_pts,
                   "encoded PTS are positive and strictly increasing")) return 0;
        if (!Check(test->packets < FrameCount, "encoded packet history remains bounded by accepted frames")) return 0;
        test->previous_pts = test->packet->pts;
        test->packet_bytes[test->packets] = test->packet->size;
        test->packet_pts[test->packets] = test->packet->pts;
        const char* name = test->encoder->codec->name;
        if (!strcmp(name, "libx264") || !strcmp(name, "libx265") || !strcmp(name, "mpeg4")) {
            int64_t bits = 0;
            for (int first = test->packets; first >= 0; --first) {
                bits += test->packet_bytes[first] * 8;
                const int64_t ticks = test->packet->pts - test->packet_pts[first] + 1;
                // VBV permits spending its capacity during a burst. Include one
                // packet duration and 1 KiB for headers/bitrate rounding, rather
                // than assuming every one-second payload must equal the rate.
                const int64_t budget = (test->encoder->rc_max_rate * ticks + FrameRate - 1) / FrameRate
                    + test->encoder->rc_buffer_size + 8192;
                if (!Check(bits <= budget, "every packet window respects the configured VBV rate plus capacity")) return 0;
            }
        }
        test->bytes += test->packet->size;
        if (test->packet->pts >= FrameIndex(FrameCount / 2)) test->last_second_bytes += test->packet->size;
        ++test->packets;
        if (test->packet->duration <= 0) test->packet->duration = 1;
        av_packet_rescale_ts(test->packet, test->encoder->time_base, test->stream->time_base);
        test->packet->stream_index = test->stream->index;
        if (!Native(av_interleaved_write_frame(test->muxer, test->packet), "mux encoded packet")) return 0;
        av_packet_unref(test->packet);
    }
    return Check(0, "encoder progresses within a bounded packet loop");
}

static int ConfigureHardware(AVCodecContext* codec, const char* name, int cbr) {
    if (!strcmp(name, "libx264")) {
        return Native(av_opt_set(codec->priv_data, "preset", "veryfast", 0), "x264 preset")
            && Native(av_opt_set(codec->priv_data, "nal-hrd", "vbr", 0), "MP4-compatible x264 HRD")
            && Native(av_opt_set(codec->priv_data, "x264opts", cbr
                          ? "sync-lookahead=0:rc-lookahead=0:lookahead-threads=1:filler=1"
                          : "sync-lookahead=0:rc-lookahead=0:lookahead-threads=1:filler=0", 0),
                      "checked x264 CBR filler/VBR configuration");
    }
    if (!strcmp(name, "libx265")) {
        return Check(strstr(avcodec_configuration(), "MajdataPlay-X265-Params-v1-5b457330f94a") != NULL,
                     "x265 parameters use the reviewed rejection patch")
            && Native(av_opt_set(codec->priv_data, "preset", "veryfast", 0), "x265 preset")
            && Native(av_opt_set(codec->priv_data, "x265-params", cbr
                          ? "pools=none:frame-threads=2:wpp=0:strict-cbr=1"
                          : "pools=none:frame-threads=2:wpp=0:strict-cbr=0", 0),
                      "checked x265 strict-CBR/VBR and bounded worker configuration");
    }
    if (!strcmp(name, "libvpx-vp9") || !strcmp(name, "libaom-av1")) {
        if (!Native(av_opt_set(codec->priv_data, "crf", "-1", 0), "disable constrained-quality mode")
            || !Native(av_opt_set(codec->priv_data, "cpu-used", "6", 0), "software speed")
            || !Native(av_opt_set(codec->priv_data, "lag-in-frames", "0", 0), "live zero-lookahead encoding")
            || !Native(av_opt_set(codec->priv_data, "drop-threshold", "0", 0), "disable native frame dropping")
            || !Native(av_opt_set(codec->priv_data, "undershoot-pct", "0", 0), "rate budget undershoot")
            || !Native(av_opt_set(codec->priv_data, "overshoot-pct", "0", 0), "rate budget overshoot")) return 0;
        if (!strcmp(name, "libvpx-vp9"))
            return Native(av_opt_set(codec->priv_data, "deadline", "good", 0), "VP9 one-pass good-quality mode");
        return Native(av_opt_set(codec->priv_data, "usage", "good", 0), "AV1 one-pass good-quality mode")
            && Native(av_opt_set(codec->priv_data, "aom-params", cbr
                          ? "max-intra-rate=100:max-inter-rate=100"
                          : "max-intra-rate=200:max-inter-rate=200", 0), "AV1 frame-target budgets");
    }
    if (!strcmp(name, "h264_videotoolbox")) {
        return Native(av_opt_set(codec->priv_data, "allow_sw", "0", 0), "VideoToolbox allow_sw=0")
            && Native(av_opt_set(codec->priv_data, "require_sw", "0", 0), "VideoToolbox require_sw=0")
            && Native(av_opt_set(codec->priv_data, "constant_bit_rate", cbr ? "1" : "0", 0),
                      "VideoToolbox CBR/VBR selection");
    }
    return Check(!(codec->codec->capabilities & AV_CODEC_CAP_HARDWARE),
                 "hardware codecs require an explicit reviewed configuration in this smoke test");
}

static int Encode(const char* path, const char* name, int cbr) {
    Output test = {0};
    int success = 0;
    uint8_t rgba[Width * Height * 4];
    const AVCodec* encoder = avcodec_find_encoder_by_name(name);
    // VideoToolbox's wrapper can allow software, so it omits the generic capability
    // flag. This test identifies that known wrapper and explicitly disallows fallback.
    const int hardware = encoder && ((encoder->capabilities & AV_CODEC_CAP_HARDWARE)
                                     || !strcmp(name, "h264_videotoolbox"));
    const int64_t maximum = cbr ? TargetBitRate : MaximumBitRate;
    test.previous_pts = -1;
    if (!Check(encoder && encoder->type == AVMEDIA_TYPE_VIDEO, "requested native encoder exists")) goto cleanup;
    if (!Check(!strcmp(encoder->name, name), "actual encoder identity matches the request")) goto cleanup;
    if (!strcmp(name, "mpeg4") && !Check(!hardware && encoder->id == AV_CODEC_ID_MPEG4,
                                        "MPEG4 is the built-in software encoder")) goto cleanup;
    if (!Native(avformat_alloc_output_context2(&test.muxer, NULL,
                        !strcmp(name, "libvpx-vp9") ? "webm" : "mp4", path), "allocate video output")) goto cleanup;
    test.encoder = avcodec_alloc_context3(encoder);
    if (!Check(test.encoder != NULL, "encoder context allocation")) goto cleanup;
    test.encoder->width = Width;
    test.encoder->height = Height;
    test.encoder->time_base = (AVRational){1, FrameRate};
    test.encoder->framerate = (AVRational){FrameRate, 1};
    test.encoder->pix_fmt = hardware ? AV_PIX_FMT_NV12 : AV_PIX_FMT_YUV420P;
    test.encoder->gop_size = FrameRate * 2;
    test.encoder->max_b_frames = 0;
    test.encoder->thread_count = hardware ? 1 : MaxThreads;
    test.encoder->bit_rate = TargetBitRate;
    test.encoder->rc_min_rate = cbr ? TargetBitRate : 0;
    test.encoder->rc_max_rate = maximum;
    test.encoder->rc_buffer_size = (int)maximum;
    test.encoder->color_range = AVCOL_RANGE_MPEG;
    test.encoder->colorspace = AVCOL_SPC_BT709;
    test.encoder->color_primaries = AVCOL_PRI_BT709;
    test.encoder->color_trc = AVCOL_TRC_IEC61966_2_1;
    if (test.muxer->oformat->flags & AVFMT_GLOBALHEADER) test.encoder->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
    if (!ConfigureHardware(test.encoder, name, cbr)) goto cleanup;
    if (!Native(avcodec_open2(test.encoder, encoder, NULL), "open selected encoder")) goto cleanup;
    if (!Check(test.encoder->bit_rate == TargetBitRate && test.encoder->rc_min_rate == (cbr ? TargetBitRate : 0)
               && test.encoder->rc_max_rate == maximum && test.encoder->rc_buffer_size == maximum,
               "opened codec preserves target, CBR/VBR minimum, effective maximum and one-second VBV")) goto cleanup;
    if (!Check(hardware || (test.encoder->thread_count > 0 && test.encoder->thread_count <= MaxThreads),
               "actual software worker count respects the configured maximum")) goto cleanup;
    test.stream = avformat_new_stream(test.muxer, NULL);
    if (!Check(test.stream != NULL, "output stream allocation")) goto cleanup;
    test.stream->time_base = test.encoder->time_base;
    if (!Native(avcodec_parameters_from_context(test.stream->codecpar, test.encoder), "copy encoded stream parameters")) goto cleanup;
    // Exclusive creation mirrors the managed writer's CreateNew ownership rule.
    test.file = OpenExclusive(path);
    if (!Check(test.file != NULL, "exclusive output creation refuses existing files")) goto cleanup;
    uint8_t* io_buffer = av_malloc(65536);
    if (!Check(io_buffer != NULL, "AVIO buffer allocation")) goto cleanup;
    test.io = avio_alloc_context(io_buffer, 65536, 1, test.file, NULL, Write, Seek);
    if (!test.io) av_free(io_buffer);
    if (!Check(test.io != NULL, "custom seekable output allocation")) goto cleanup;
    test.muxer->pb = test.io;
    test.muxer->flags |= AVFMT_FLAG_CUSTOM_IO;
    if (!Native(avformat_write_header(test.muxer, NULL), "write MP4 header")) goto cleanup;
    test.frame = av_frame_alloc();
    test.packet = av_packet_alloc();
    if (!Check(test.frame && test.packet, "encoding frame and packet allocation")) goto cleanup;
    test.frame->format = test.encoder->pix_fmt;
    test.frame->width = Width;
    test.frame->height = Height;
    test.frame->color_range = test.encoder->color_range;
    test.frame->colorspace = test.encoder->colorspace;
    test.frame->color_primaries = test.encoder->color_primaries;
    test.frame->color_trc = test.encoder->color_trc;
    if (!Native(av_frame_get_buffer(test.frame, 32), "allocate owned encoding pixels")) goto cleanup;
    test.scaler = sws_getContext(Width, Height, AV_PIX_FMT_RGBA, Width, Height, test.encoder->pix_fmt,
                                 SWS_BILINEAR, NULL, NULL, NULL);
    if (!Check(test.scaler != NULL, "RGBA encoder conversion context")) goto cleanup;
    const int* coefficients = sws_getCoefficients(SWS_CS_ITU709);
    if (!Native(sws_setColorspaceDetails(test.scaler, coefficients, 1, coefficients, 0, 0, 1 << 16, 1 << 16),
                "set BT709 encoder conversion")) goto cleanup;
    for (int y = 0; y < Height; ++y) {
        for (int x = 0; x < Width; ++x) {
            const int offset = (y * Width + x) * 4;
            rgba[offset] = y < Height / 2 ? 255 : 0;
            rgba[offset + 1] = 0;
            rgba[offset + 2] = y < Height / 2 ? 0 : 255;
            rgba[offset + 3] = 255;
        }
    }
    for (int index = 0; index < FrameCount; ++index) {
        if (!Native(av_frame_make_writable(test.frame), "reuse owned encoder pixels")) goto cleanup;
        if (ComplexPattern) {
            uint32_t state = UINT32_C(0xA17F542D) ^ (uint32_t)index;
            for (int y = 0; y < Height; ++y) {
                for (int x = Width / 2; x < Width; ++x) {
                    const int offset = (y * Width + x) * 4;
                    if (index >= 15 && index < 30) {
                        rgba[offset] = y < Height / 2 ? 255 : 0;
                        rgba[offset + 1] = 0;
                        rgba[offset + 2] = y < Height / 2 ? 0 : 255;
                    } else {
                        state = state * UINT32_C(1664525) + UINT32_C(1013904223);
                        rgba[offset] = (uint8_t)(state >> 24);
                        rgba[offset + 1] = (uint8_t)(state >> 16);
                        rgba[offset + 2] = (uint8_t)(state >> 8);
                    }
                }
            }
        }
        const uint8_t* source[] = {rgba};
        const int stride[] = {Width * 4};
        if (!Check(sws_scale(test.scaler, source, stride, 0, Height, test.frame->data, test.frame->linesize) == Height,
                   "convert complete RGBA fixture")) goto cleanup;
        test.frame->pts = FrameIndex(index);
        if (!Native(avcodec_send_frame(test.encoder, test.frame), "send fixture frame")) goto cleanup;
        if (!ReceivePackets(&test, 0)) goto cleanup;
    }
    if (!Native(avcodec_send_frame(test.encoder, NULL), "flush encoder once")) goto cleanup;
    if (!ReceivePackets(&test, 1)) goto cleanup;
    if (!Check(test.encoder_eof && test.packets == FrameCount && test.bytes > 0,
               "encoder flush publishes every accepted frame and reaches EOF")) goto cleanup;
    if (!Check(avcodec_receive_packet(test.encoder, test.packet) == AVERROR_EOF, "encoder EOF remains stable")) goto cleanup;
    if (!Native(av_write_trailer(test.muxer), "finalize MP4 index and trailer")) goto cleanup;
    avio_flush(test.io);
    if (!Native(test.io->error, "flush custom output")) goto cleanup;
    if (!Check(fflush(test.file) == 0, "flush exclusive output file")) goto cleanup;
    const int64_t actual = test.last_second_bytes * 8;
    printf("Encoded %s %s %s: softwareThreads=%d target=%" PRId64 " maximum=%" PRId64
           " firstSecond=%" PRId64 " lastSecond=%" PRId64 " bit/s packets=%d payload=%" PRId64 "\n",
           name, cbr ? "CBR" : "VBR", ComplexPattern ? "complex" : "flat", hardware ? 0 : test.encoder->thread_count,
           TargetBitRate, maximum, (test.bytes - test.last_second_bytes) * 8, actual, test.packets, test.bytes);
    if (!Check(actual > 0, "real compressed payload populates the measured media window")) goto cleanup;
    if (ComplexPattern && (!strcmp(name, "libvpx-vp9") || !strcmp(name, "libaom-av1"))) {
        printf("NOTE: VPX/AOM rate limits are codec budgets; this complex fixture records measured overshoot rather than asserting a hard payload boundary.\n");
    } else if (!Check(actual <= maximum + test.encoder->rc_buffer_size + 8192,
                      "last-second encoded payload fits the rate plus configured VBV burst capacity")) goto cleanup;
    if (cbr && !ComplexPattern && (!strcmp(name, "mpeg4") || !strcmp(name, "libx264"))
        && !Check(llabs(actual - TargetBitRate) <= TargetBitRate / 5,
                      "CBR flat scene maintains the target through native filler/rate control")) goto cleanup;
    if (cbr && hardware) printf("NOTE: this hardware test verifies accepted CBR configuration and measured payload; constant target padding is not validated.\n");
    success = 1;
cleanup:
    CloseOutput(&test);
    return success;
}

static int CheckDecodedFrame(Input* test, uint8_t* rgba) {
    if (!Check(test->frames < FrameCount, "decoder produces no duplicate or extra frames")) return 0;
    if (!Check(test->frame->width == Width && test->frame->height == Height,
               "decoded dimensions equal the captured fixture")) return 0;
    const int64_t pts = test->frame->best_effort_timestamp;
    const double actual = pts * av_q2d(test->demuxer->streams[test->stream]->time_base);
    const double expected = (double)FrameIndex(test->frames) / FrameRate;
    if (!Check(pts != AV_NOPTS_VALUE && fabs(actual - expected) < 0.001,
               "decoded PTS preserve every frame and the deliberate capture gap")) return 0;
    uint8_t* target[] = {rgba};
    const int stride[] = {Width * 4};
    if (!Check(sws_scale(test->scaler, (const uint8_t* const*)test->frame->data, test->frame->linesize,
                         0, Height, target, stride) == Height, "decode every row to RGBA")) return 0;
    const uint8_t* top = rgba + (8 * Width + 8) * 4;
    const uint8_t* bottom = rgba + ((Height - 9) * Width + 8) * 4;
    if (!Check(top[0] > 200 && top[2] < 50 && bottom[2] > 200 && bottom[0] < 50,
               "decoded red top and blue bottom preserve pixels and orientation")) return 0;
    ++test->frames;
    return 1;
}

static int ReceiveFrames(Input* test, uint8_t* rgba, int draining) {
    for (int iteration = 0; iteration < FrameCount + 16; ++iteration) {
        const int result = avcodec_receive_frame(test->decoder, test->frame);
        if (result == AVERROR(EAGAIN)) return Check(!draining, "decoder drain must reach EOF");
        if (result == AVERROR_EOF) return Check(draining, "decoder EOF follows its accepted drain packet");
        if (!Native(result, "receive decoded frame")) return 0;
        if (!CheckDecodedFrame(test, rgba)) return 0;
        av_frame_unref(test->frame);
    }
    return Check(0, "decoder progresses within a bounded frame loop");
}

static int Decode(const char* path, enum AVCodecID expected_codec) {
    Input test = {0};
    int success = 0;
    uint8_t rgba[Width * Height * 4];
    if (!Native(avformat_open_input(&test.demuxer, path, NULL, NULL), "open completed recording")) goto cleanup;
    if (!Native(avformat_find_stream_info(test.demuxer, NULL), "read completed recording index")) goto cleanup;
    test.stream = av_find_best_stream(test.demuxer, AVMEDIA_TYPE_VIDEO, -1, -1, NULL, 0);
    if (!Native(test.stream, "select recorded video stream")) goto cleanup;
    AVCodecParameters* parameters = test.demuxer->streams[test.stream]->codecpar;
    if (!Check(parameters->codec_id == expected_codec, "container retains the requested encoding format")) goto cleanup;
    const AVCodec* decoder = avcodec_find_decoder(parameters->codec_id);
    if (!Check(decoder != NULL && !(decoder->capabilities & AV_CODEC_CAP_HARDWARE), "roundtrip uses an actual software decoder")) goto cleanup;
    test.decoder = avcodec_alloc_context3(decoder);
    if (!Check(test.decoder != NULL, "decoder context allocation")) goto cleanup;
    if (!Native(avcodec_parameters_to_context(test.decoder, parameters), "read recording decoder parameters")) goto cleanup;
    test.decoder->thread_count = 1;
    if (!Native(avcodec_open2(test.decoder, decoder, NULL), "open recording software decoder")) goto cleanup;
    test.frame = av_frame_alloc();
    test.packet = av_packet_alloc();
    if (!Check(test.frame && test.packet, "decode frame and packet allocation")) goto cleanup;
    test.scaler = sws_getContext(Width, Height, test.decoder->pix_fmt, Width, Height, AV_PIX_FMT_RGBA,
                                 SWS_BILINEAR, NULL, NULL, NULL);
    if (!Check(test.scaler != NULL, "recording RGBA decoder conversion")) goto cleanup;
    const int* coefficients = sws_getCoefficients(SWS_CS_ITU709);
    if (!Native(sws_setColorspaceDetails(test.scaler, coefficients, 0, coefficients, 1, 0, 1 << 16, 1 << 16),
                "set BT709 decoder conversion")) goto cleanup;
    for (int iteration = 0; iteration < 100000; ++iteration) {
        const int result = av_read_frame(test.demuxer, test.packet);
        if (result == AVERROR_EOF) { test.input_eof = 1; break; }
        if (!Native(result, "read recorded packet")) goto cleanup;
        if (test.packet->stream_index == test.stream) {
            if (!Native(avcodec_send_packet(test.decoder, test.packet), "send recorded packet")) goto cleanup;
            if (!ReceiveFrames(&test, rgba, 0)) goto cleanup;
        }
        av_packet_unref(test.packet);
    }
    if (!Check(test.input_eof, "demuxer reaches final EOF within bounded input")) goto cleanup;
    if (!Native(avcodec_send_packet(test.decoder, NULL), "flush decoder once")) goto cleanup;
    test.drain_sent = 1;
    if (!ReceiveFrames(&test, rgba, 1)) goto cleanup;
    if (!Check(avcodec_receive_frame(test.decoder, test.frame) == AVERROR_EOF && test.frames == FrameCount,
               "decoder flush completes exactly sixty frames and stable EOF")) goto cleanup;
    printf("Decoded %s: codec=%s frames=%d pixels/orientation/PTS/flush PASS\n",
           path, decoder->name, test.frames);
    success = 1;
cleanup:
    CloseInput(&test);
    return success;
}

static int FileChecksum(const char* path, uint64_t* checksum, int64_t* length) {
    FILE* file = fopen(path, "rb");
    if (!Check(file != NULL, "completed output can be reopened after encoder cleanup")) return 0;
    *checksum = UINT64_C(1469598103934665603);
    *length = 0;
    uint8_t bytes[4096];
    size_t count;
    while ((count = fread(bytes, 1, sizeof(bytes), file)) != 0) {
        *length += (int64_t)count;
        for (size_t index = 0; index < count; ++index) {
            *checksum ^= bytes[index];
            *checksum *= UINT64_C(1099511628211);
        }
    }
    const int success = Check(!ferror(file), "read original output bytes");
    fclose(file);
    return success;
}

static int CheckNoOverwrite(const char* path) {
    uint64_t before, after;
    int64_t before_length, after_length;
    if (!FileChecksum(path, &before, &before_length)) return 0;
    errno = 0;
    FILE* duplicate = OpenExclusive(path);
    const int error = errno;
    if (duplicate) fclose(duplicate);
    if (!Check(duplicate == NULL && error == EEXIST, "exclusive output refuses to overwrite an existing recording")) return 0;
    if (!FileChecksum(path, &after, &after_length)) return 0;
    return Check(before == after && before_length == after_length && before_length > 0,
                 "refused output creation preserves every original byte");
}

int main(int argc, char** argv) {
    if (argc < 2 || argc > 3) {
        fprintf(stderr, "Usage: %s <existing-output-directory> [encoder-name]\n", argv[0]);
        return 2;
    }
    const char* name = argc == 3 ? argv[2] : "mpeg4";
#if defined(__APPLE__) && TARGET_OS_IPHONE
    // The pinned iPhoneOS wrapper does not apply the macOS RequireHardware flag.
    if (!strcmp(name, "h264_videotoolbox")) {
        fprintf(stderr, "FAIL: optional VideoToolbox hardware validation requires macOS; iOS does not apply the same RequireHardware flag.\n");
        return 2;
    }
#endif
    const AVCodec* encoder = avcodec_find_encoder_by_name(name);
    if (!Check(avcodec_version() == LIBAVCODEC_VERSION_INT && avformat_version() == LIBAVFORMAT_VERSION_INT
               && avutil_version() == LIBAVUTIL_VERSION_INT, "loaded FFmpeg ABI exactly matches compile headers")) return 1;
    if (!Check(encoder != NULL, "requested codec is compiled into the staged native profile")) return 1;
    printf("Native FFmpeg %s; requested encoder=%s\n", av_version_info(), name);
    for (int cbr = 0; cbr <= 1; ++cbr) {
        char path[4096];
        const int length = snprintf(path, sizeof(path), "%s/%s-%s.mp4", argv[1], name, cbr ? "cbr" : "vbr");
        if (!Check(length > 0 && length < (int)sizeof(path), "recording output path fits without truncation")) return 1;
        if (!Encode(path, name, cbr) || !Decode(path, encoder->id) || !CheckNoOverwrite(path)) return 1;
    }
    printf("PASS: %d checks; %s accepted CBR/VBR settings, measured payload, pixels, PTS, frame count, drain and exclusive output.\n",
           Checks, name);
    if (!strcmp(name, "mpeg4")) printf("Software MPEG4: actual worker maximum and CBR target filling PASS.\n");
    return 0;
}
