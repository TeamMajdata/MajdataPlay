// Portable, CPU-only validation of the pinned FFmpeg libraries and libdav1d.
// Build: cc -std=c11 -Wall -Wextra -Werror -I<prefix>/include this.c
//        -L<prefix>/lib -lavformat -lavcodec -lswscale -lavutil -o av1-software-smoke
// Run:   av1-software-smoke test-av1.mp4 test-av1-10bit.mp4
#include <errno.h>
#include <inttypes.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/pixdesc.h>
#include <libswscale/swscale.h>

typedef struct Smoke {
    AVFormatContext* input;
    AVCodecContext* decoder;
    AVPacket* packet;
    AVFrame* frame;
    struct SwsContext* scaler;
    int stream, packet_pending, input_ended, drain_sent, checks, depth;
    double origin, duration, time_base;
} Smoke;

typedef struct Pass {
    int frames, minimum, maximum;
    double first, last;
    uint64_t checksum;
} Pass;

static int Check(Smoke* test, int condition, const char* message) {
    if (!condition) {
        fprintf(stderr, "FAIL: AV1 %d-bit: %s\n", test->depth, message);
        return 0;
    }
    ++test->checks;
    return 1;
}

static int Native(Smoke* test, int result, const char* operation) {
    if (result < 0) {
        char error[AV_ERROR_MAX_STRING_SIZE];
        av_strerror(result, error, sizeof(error));
        fprintf(stderr, "FAIL: AV1 %d-bit: %s: %s (%d)\n", test->depth, operation, error, result);
        return 0;
    }
    ++test->checks;
    return 1;
}

static void Close(Smoke* test) {
    sws_freeContext(test->scaler);
    av_frame_free(&test->frame);
    av_packet_free(&test->packet);
    avcodec_free_context(&test->decoder);
    avformat_close_input(&test->input);
}

// A packet is retained until send accepts it. A drain packet is accepted once;
// receive is then called through AVERROR_EOF, including all delayed frames.
static int NextFrame(Smoke* test) {
    av_frame_unref(test->frame);
    for (int iterations = 0; iterations < 100000; ++iterations) {
        int result = avcodec_receive_frame(test->decoder, test->frame);
        if (result == 0) return 1;
        if (result == AVERROR_EOF) {
            if (!Check(test, test->drain_sent, "decoder reaches EOF only after accepted drain")) return -1;
            return 0;
        }
        if (result != AVERROR(EAGAIN)) {
            Native(test, result, "receive frame");
            return -1;
        }
        if (!Check(test, !test->drain_sent, "draining decoder does not request further input")) return -1;
        if (!test->packet_pending && !test->input_ended) {
            for (;;) {
                result = av_read_frame(test->input, test->packet);
                if (result == AVERROR_EOF) { test->input_ended = 1; break; }
                if (!Native(test, result, "read compressed packet")) return -1;
                if (test->packet->stream_index == test->stream) { test->packet_pending = 1; break; }
                av_packet_unref(test->packet);
            }
        }
        result = avcodec_send_packet(test->decoder, test->packet_pending ? test->packet : NULL);
        // FFmpeg's send/receive contract forbids EAGAIN on both sides without
        // progress. Keep the packet owned until cleanup if this contract fails.
        if (!Check(test, result != AVERROR(EAGAIN), "send progresses after receive requests input")) return -1;
        if (!Native(test, result, test->packet_pending ? "send video packet" : "send drain packet")) return -1;
        if (test->packet_pending) {
            av_packet_unref(test->packet);
            test->packet_pending = 0;
        } else {
            test->drain_sent = 1;
        }
    }
    Check(test, 0, "decoder made progress within bounded iterations");
    return -1;
}

static int InspectFrame(Smoke* test, Pass* pass) {
    AVFrame* frame = test->frame;
    const AVPixFmtDescriptor* descriptor = av_pix_fmt_desc_get((enum AVPixelFormat)frame->format);
    if (!Check(test, descriptor != NULL, "decoded pixel format has a descriptor") ||
        !Check(test, frame->hw_frames_ctx == NULL && test->decoder->hw_device_ctx == NULL &&
            test->decoder->hw_frames_ctx == NULL && !(descriptor->flags & AV_PIX_FMT_FLAG_HWACCEL),
            "decoded frames and codec use no hardware context") ||
        !Check(test, descriptor->nb_components >= 3, "decoded frame has color components")) return 0;
    for (int component = 0; component < descriptor->nb_components; ++component)
        if (!Check(test, descriptor->comp[component].depth == test->depth, "decoded component bit depth matches fixture")) return 0;
    if (!Check(test, frame->width > 0 && frame->height > 0 && frame->data[0] != NULL, "decoded frame owns real pixels") ||
        !Check(test, frame->best_effort_timestamp != AV_NOPTS_VALUE, "decoded frame has presentation timestamp")) return 0;
    double seconds = frame->best_effort_timestamp * test->time_base - test->origin;
    if (!Check(test, isfinite(seconds), "presentation timestamp is finite") ||
        !Check(test, !pass->frames || seconds >= pass->last, "presentation timestamps are monotonic")) return 0;
    if (!pass->frames) pass->first = seconds;
    pass->last = seconds;
    test->scaler = sws_getCachedContext(test->scaler, frame->width, frame->height, (enum AVPixelFormat)frame->format,
        32, 32, AV_PIX_FMT_RGBA, SWS_BILINEAR, NULL, NULL, NULL);
    if (!Check(test, test->scaler != NULL, "create CPU RGBA conversion")) return 0;
    uint8_t rgba[32 * 32 * 4];
    uint8_t* output[4] = {rgba, NULL, NULL, NULL};
    int stride[4] = {32 * 4, 0, 0, 0};
    if (!Check(test, sws_scale(test->scaler, (const uint8_t* const*)frame->data, frame->linesize, 0,
        frame->height, output, stride) == 32, "convert actual decoded pixels to RGBA")) return 0;
    int minimum = 255, maximum = 0;
    for (int pixel = 0; pixel < 32 * 32; ++pixel) {
        int luma = (rgba[pixel * 4] + rgba[pixel * 4 + 1] + rgba[pixel * 4 + 2]) / 3;
        if (luma < minimum) minimum = luma;
        if (luma > maximum) maximum = luma;
        for (int channel = 0; channel < 3; ++channel)
            pass->checksum = (pass->checksum ^ rgba[pixel * 4 + channel]) * UINT64_C(1099511628211);
    }
    if (!Check(test, maximum - minimum > 15, "RGBA pixels contain visible spatial variation")) return 0;
    if (minimum < pass->minimum) pass->minimum = minimum;
    if (maximum > pass->maximum) pass->maximum = maximum;
    ++pass->frames;
    return 1;
}

static Pass NewPass(void) {
    Pass pass = {0};
    pass.minimum = 255;
    pass.checksum = UINT64_C(14695981039346656037);
    return pass;
}

static int ReadToEnd(Smoke* test, Pass* pass) {
    int result;
    while ((result = NextFrame(test)) > 0) {
        if (!InspectFrame(test, pass) || !Check(test, pass->frames <= 10000, "fixture stays within frame limit")) return 0;
    }
    return result == 0 && Check(test, pass->frames >= 10, "decode at least ten real frames through EOF") &&
        Check(test, pass->last > pass->first, "decoded timestamps advance") &&
        Check(test, NextFrame(test) == 0, "EOF remains stable after draining");
}

static int Seek(Smoke* test, double seconds) {
    int64_t timestamp = (int64_t)((seconds + test->origin) / test->time_base);
    if (!Native(test, av_seek_frame(test->input, test->stream, timestamp, AVSEEK_FLAG_BACKWARD), "seek video")) return 0;
    avcodec_flush_buffers(test->decoder);
    av_packet_unref(test->packet);
    av_frame_unref(test->frame);
    test->packet_pending = test->input_ended = test->drain_sent = 0;
    return 1;
}

static int Run(const char* path, int depth) {
    Smoke test = {0};
    test.depth = depth;
    int success = 0;
    const AVCodec* codec = avcodec_find_decoder_by_name("libdav1d");
    if (!Check(&test, codec != NULL && codec->id == AV_CODEC_ID_AV1, "libdav1d AV1 software decoder is registered") ||
        !Native(&test, avformat_open_input(&test.input, path, NULL, NULL), "open fixture") ||
        !Native(&test, avformat_find_stream_info(test.input, NULL), "probe fixture")) goto done;
    test.stream = av_find_best_stream(test.input, AVMEDIA_TYPE_VIDEO, -1, -1, NULL, 0);
    if (!Native(&test, test.stream, "find video stream")) goto done;
    AVStream* stream = test.input->streams[test.stream];
    if (!Check(&test, stream->codecpar->codec_id == AV_CODEC_ID_AV1, "fixture is AV1") ||
        !Check(&test, stream->time_base.num > 0 && stream->time_base.den > 0, "stream time base is valid") ||
        !Check(&test, test.input->pb != NULL && (test.input->pb->seekable & AVIO_SEEKABLE_NORMAL), "fixture is seekable")) goto done;
    test.time_base = av_q2d(stream->time_base);
    test.origin = stream->start_time != AV_NOPTS_VALUE ? stream->start_time * test.time_base : 0;
    test.duration = stream->duration != AV_NOPTS_VALUE ? stream->duration * test.time_base :
        test.input->duration != AV_NOPTS_VALUE ? (double)test.input->duration / AV_TIME_BASE : 0;
    if (!Check(&test, test.duration > 2, "fixture is longer than two seconds")) goto done;
    test.decoder = avcodec_alloc_context3(codec);
    test.packet = av_packet_alloc();
    test.frame = av_frame_alloc();
    if (!Check(&test, test.decoder != NULL && test.packet != NULL && test.frame != NULL, "allocate decoding resources") ||
        !Native(&test, avcodec_parameters_to_context(test.decoder, stream->codecpar), "copy codec parameters")) goto done;
    test.decoder->pkt_timebase = stream->time_base;
    test.decoder->thread_count = 2;
    if (!Native(&test, avcodec_open2(test.decoder, codec, NULL), "open explicit libdav1d decoder") ||
        !Check(&test, strcmp(test.decoder->codec->name, "libdav1d") == 0, "opened decoder is libdav1d")) goto done;
    Pass first = NewPass();
    if (!ReadToEnd(&test, &first)) goto done;
    double target = test.duration / 2;
    if (!Seek(&test, target)) goto done;
    Pass forward = NewPass();
    while (forward.frames < 10000) {
        if (!Check(&test, NextFrame(&test) == 1, "decode real frame after forward seek") || !InspectFrame(&test, &forward)) goto done;
        if (forward.last + 0.000001 >= target) break;
    }
    if (!Check(&test, forward.last + 0.000001 >= target && forward.last <= target + 0.25,
        "forward seek reaches requested presentation time") || !Seek(&test, 0)) goto done;
    Pass backward = NewPass();
    if (!ReadToEnd(&test, &backward) ||
        !Check(&test, backward.frames == first.frames, "backward seek reproduces full frame count") ||
        !Check(&test, fabs(backward.first - first.first) < 0.000001 && fabs(backward.last - first.last) < 0.000001,
            "backward seek reproduces original timestamp range") ||
        !Check(&test, backward.checksum == first.checksum, "backward seek reproduces actual RGBA pixels")) goto done;
    printf("PASS: AV1 %d-bit; decoder=%s; %dx%d; frames=%d; PTS=%.6f..%.6f; seek=%.6f; "
        "RGBA=%d..%d; checksum=%016" PRIx64 "; software=1; EOF=1; checks=%d\n", depth,
        test.decoder->codec->name, test.decoder->width, test.decoder->height, first.frames, first.first, first.last,
        forward.last, first.minimum, first.maximum, first.checksum, test.checks);
    success = 1;
done:
    Close(&test);
    return success;
}

int main(int argc, char** argv) {
    if (argc != 3) {
        fprintf(stderr, "Usage: %s test-av1-8bit.mp4 test-av1-10bit.mp4\n", argv[0]);
        return 2;
    }
    if (LIBAVCODEC_VERSION_MAJOR != 63 || LIBAVFORMAT_VERSION_MAJOR != 63 || LIBAVUTIL_VERSION_MAJOR != 61 ||
        LIBSWSCALE_VERSION_MAJOR != 10 || avcodec_version() != LIBAVCODEC_VERSION_INT ||
        avformat_version() != LIBAVFORMAT_VERSION_INT || avutil_version() != LIBAVUTIL_VERSION_INT ||
        swscale_version() != LIBSWSCALE_VERSION_INT) {
        fprintf(stderr, "FAIL: compile-time headers and loaded libraries must match pinned FFmpeg 9.0.1\n");
        return 1;
    }
    printf("Native AV1 software smoke; pointer-bits=%zu; FFmpeg=%s; no GPU or Unity required\n", sizeof(void*) * 8, av_version_info());
    int eight = Run(argv[1], 8);
    int ten = Run(argv[2], 10);
    return eight && ten ? 0 : 1;
}
