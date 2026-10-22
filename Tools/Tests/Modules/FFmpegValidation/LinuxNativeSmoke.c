// Build without linking FFmpeg: its ELF dependency resolution is part of the test.
// Compile with -std=gnu11 -Wall -Wextra -Werror, the FFmpeg include directory,
// and -ldl. Deliberately do not link any FFmpeg shared library into the executable.
// env -u LD_LIBRARY_PATH /tmp/ffmpeg-linux-smoke /absolute/staged/lib/dir /absolute/video.mp4
#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libswscale/swscale.h>

#define LOAD(handle, name) \
    __typeof__(&name) p_##name = (__typeof__(&name))dlsym(handle, #name); \
    if (!p_##name) { fprintf(stderr, "FAIL: missing %s: %s\n", #name, dlerror()); return 1; }
#define CHECK(condition, message) do { \
    if (!(condition)) { fprintf(stderr, "FAIL: %s\n", message); return 1; } \
    ++checks; \
} while (0)

static void* Open(const char* directory, const char* name) {
    char path[PATH_MAX];
    if (snprintf(path, sizeof(path), "%s/%s", directory, name) >= (int)sizeof(path)) return NULL;
    void* handle = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (!handle) fprintf(stderr, "FAIL: dlopen %s: %s\n", path, dlerror());
    return handle;
}

int main(int argc, char** argv) {
    int checks = 0;
    if (argc != 3) { fprintf(stderr, "Usage: %s /absolute/staged/library/dir video.mp4\n", argv[0]); return 2; }
    CHECK(!getenv("LD_LIBRARY_PATH"), "Run with env -u LD_LIBRARY_PATH; do not mask missing ELF RUNPATH");
    char directory[PATH_MAX];
    CHECK(realpath(argv[1], directory) != NULL, "staged directory exists");

    // avformat goes FIRST: its avcodec/avutil dependencies must resolve from
    // the staged artifacts' own $ORIGIN RUNPATH, without preloading either one.
    void* format = Open(directory, "libavformat.so.63");
    CHECK(format != NULL, "absolute-path libavformat dlopen resolves transitive dependencies");
    void* scale = Open(directory, "libswscale.so.10");
    CHECK(scale != NULL, "libswscale loads");
    const char* extraNames[] = {"libavcodec.so.63", "libavutil.so.61", "libavdevice.so.63", "libavfilter.so.12", "libswresample.so.7"};
    void* extra[5];
    for (int i = 0; i < 5; ++i) { extra[i] = Open(directory, extraNames[i]); CHECK(extra[i] != NULL, extraNames[i]); }
    LOAD(format, avformat_version); LOAD(format, avcodec_version); LOAD(format, avutil_version);
    LOAD(format, avformat_open_input); LOAD(format, avformat_find_stream_info);
    LOAD(format, av_find_best_stream); LOAD(format, avformat_close_input); LOAD(format, av_read_frame);
    LOAD(format, avcodec_alloc_context3); LOAD(format, avcodec_parameters_to_context); LOAD(format, avcodec_open2);
    LOAD(format, avcodec_send_packet); LOAD(format, avcodec_receive_frame); LOAD(format, avcodec_free_context);
    LOAD(format, av_packet_alloc); LOAD(format, av_packet_unref); LOAD(format, av_packet_free);
    LOAD(format, av_frame_alloc); LOAD(format, av_frame_free); LOAD(format, av_frame_unref);
    LOAD(scale, sws_getContext); LOAD(scale, sws_scale); LOAD(scale, sws_freeContext);
    CHECK((p_avformat_version() >> 16) == 63 && (p_avcodec_version() >> 16) == 63 && (p_avutil_version() >> 16) == 61,
          "loaded FFmpeg version matches the generated binding ABI");
    Dl_info resolved;
    CHECK(dladdr((void*)p_avcodec_version, &resolved) != 0 && strncmp(resolved.dli_fname, directory, strlen(directory)) == 0,
          "libavcodec is the staged binary, not a system installation");
    printf("Loader: avformat absolute dlopen; codec=%s; LD_LIBRARY_PATH unset\n", resolved.dli_fname);

    AVFormatContext* input = NULL;
    CHECK(p_avformat_open_input(&input, argv[2], NULL, NULL) >= 0, "open media");
    CHECK(p_avformat_find_stream_info(input, NULL) >= 0, "probe streams");
    const AVCodec* codec = NULL;
    int stream = p_av_find_best_stream(input, AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
    CHECK(stream >= 0 && codec != NULL, "find video decoder");
    AVCodecContext* decoder = p_avcodec_alloc_context3(codec);
    CHECK(decoder != NULL, "allocate decoder");
    CHECK(p_avcodec_parameters_to_context(decoder, input->streams[stream]->codecpar) >= 0, "copy decoder parameters");
    decoder->thread_count = 2;
    CHECK(p_avcodec_open2(decoder, codec, NULL) >= 0, "initialize video decoder");
    AVPacket* packet = p_av_packet_alloc();
    AVFrame* frame = p_av_frame_alloc();
    CHECK(packet != NULL && frame != NULL, "allocate frame and packet");
    int frames = 0, eof = 0, draining = 0, minimum = 255, maximum = 0;
    uint64_t checksum = 1469598103934665603ULL;
    uint8_t rgba[32 * 32 * 4];
    while (frames < 30) {
        int result = p_avcodec_receive_frame(decoder, frame);
        if (result >= 0) {
            struct SwsContext* converter = p_sws_getContext(frame->width, frame->height, frame->format,
                32, 32, AV_PIX_FMT_RGBA, SWS_BILINEAR, NULL, NULL, NULL);
            CHECK(converter != NULL, "create RGBA scaler");
            uint8_t* output[4] = {rgba, NULL, NULL, NULL};
            int stride[4] = {32 * 4, 0, 0, 0};
            CHECK(p_sws_scale(converter, (const uint8_t* const*)frame->data, frame->linesize, 0, frame->height, output, stride) == 32,
                  "scale decoded pixels to RGBA");
            p_sws_freeContext(converter);
            for (int pixel = 0; pixel < 32 * 32; ++pixel) {
                int luma = (rgba[pixel * 4] + rgba[pixel * 4 + 1] + rgba[pixel * 4 + 2]) / 3;
                if (luma < minimum) minimum = luma;
                if (luma > maximum) maximum = luma;
                checksum = (checksum ^ rgba[pixel * 4]) * 1099511628211ULL;
            }
            ++frames;
            p_av_frame_unref(frame);
            continue;
        }
        if (result == AVERROR_EOF) break;
        CHECK(result == AVERROR(EAGAIN), "receive_frame returns frame or normal decoder backpressure");
        if (eof) {
            CHECK(!draining && p_avcodec_send_packet(decoder, NULL) >= 0, "flush delayed decoder frames");
            draining = 1;
            continue;
        }
        do {
            result = p_av_read_frame(input, packet);
            if (result < 0) { eof = 1; break; }
            if (packet->stream_index == stream) break;
            p_av_packet_unref(packet);
        } while (1);
        if (eof) continue;
        CHECK(p_avcodec_send_packet(decoder, packet) >= 0, "submit compressed video packet");
        p_av_packet_unref(packet);
    }
    CHECK(frames >= 10, "decode at least ten real video frames");
    CHECK(maximum - minimum > 15, "actual decoded RGBA pixels contain visible variation");
    printf("PASS: Linux x64 %d checks; codec=%s; %dx%d; frames=%d; RGBA range=%d..%d; checksum=%016llx\n",
        checks, codec->name, decoder->width, decoder->height, frames, minimum, maximum, (unsigned long long)checksum);
    p_av_packet_free(&packet); p_av_frame_free(&frame); p_avcodec_free_context(&decoder); p_avformat_close_input(&input);
    for (int i = 4; i >= 0; --i) dlclose(extra[i]);
    dlclose(scale); dlclose(format);
    return 0;
}
