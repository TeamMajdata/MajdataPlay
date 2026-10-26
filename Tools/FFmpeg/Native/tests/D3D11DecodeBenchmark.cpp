// Optional headless throughput diagnostics using the smoke fixture's real
// isolated decode and presentation devices. No Unity frame timing is inferred.
#include "../Bridge.h"
#include <d3d11.h>
#include <algorithm>
#include <chrono>
#include <cstdio>
#include <vector>
extern "C" {
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
}
void FfuD3D11Trace(bool reset);
namespace {
using Clock = std::chrono::steady_clock;
double Milliseconds(Clock::time_point begin) { return std::chrono::duration<double, std::milli>(Clock::now() - begin).count(); }
AVPixelFormat Format(AVCodecContext*, const AVPixelFormat* formats) {
    for (auto* item = formats; *item != AV_PIX_FMT_NONE; ++item) if (*item == AV_PIX_FMT_D3D11) return *item;
    return AV_PIX_FMT_NONE;
}
struct State {
    AVFormatContext* input = nullptr;
    AVCodecContext* codec = nullptr;
    AVBufferRef* hardware = nullptr;
    AVPacket* packet = nullptr;
    AVFrame* frame = nullptr;
    void* stage = nullptr;
    void* sync = nullptr;
    ~State() {
        ffu_d3d11_stage_release(stage); ffu_d3d11_decode_sync_release(sync);
        av_frame_free(&frame); av_packet_free(&packet); avcodec_free_context(&codec);
        avformat_close_input(&input); av_buffer_unref(&hardware);
    }
};
bool Poll(void* value, bool staging, bool yieldOnly) {
    const auto begin = Clock::now();
    int ready;
    while ((ready = staging ? ffu_d3d11_stage_ready(value) : ffu_d3d11_decode_sync_poll(value)) == 0) {
        const double elapsed = Milliseconds(begin);
        if (elapsed > 15000) return false;
        if (yieldOnly || elapsed < 4) SwitchToThread(); else Sleep(1);
    }
    return ready == 1;
}
bool Run(const char* path, int stride, bool yieldOnly) {
    State test;
    if (avformat_open_input(&test.input, path, nullptr, nullptr) < 0 || avformat_find_stream_info(test.input, nullptr) < 0) return false;
    const int stream = av_find_best_stream(test.input, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
    if (stream < 0) return false;
    test.codec = avcodec_alloc_context3(avcodec_find_decoder(test.input->streams[stream]->codecpar->codec_id));
    test.hardware = av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D11VA);
    test.packet = av_packet_alloc(); test.frame = av_frame_alloc();
    if (!test.codec || !test.hardware || !test.packet || !test.frame ||
        avcodec_parameters_to_context(test.codec, test.input->streams[stream]->codecpar) < 0) return false;
    auto* hardware = reinterpret_cast<AVHWDeviceContext*>(test.hardware->data);
    static_cast<AVD3D11VADeviceContext*>(hardware->hwctx)->device = static_cast<ID3D11Device*>(ffu_d3d11_acquire_device());
    if (av_hwdevice_ctx_init(test.hardware) < 0) return false;
    test.stage = ffu_d3d11_stage_create(test.hardware); test.sync = ffu_d3d11_decode_sync_create(test.hardware);
    if (!test.stage || !test.sync) return false;
    test.codec->thread_count = 1; test.codec->extra_hw_frames = 12; test.codec->get_format = Format;
    test.codec->hw_device_ctx = av_buffer_ref(test.hardware);
    if (avcodec_open2(test.codec, nullptr, nullptr) < 0) return false;
    int frames = 0, maps = 0, markers = 0, pending = 0;
    double mapCpu = 0, mapWait = 0, syncCpu = 0, syncWait = 0, sends = 0;
    std::vector<double> mapTimes;
    FfuD3D11Trace(true);
    const auto begin = Clock::now();
    while (frames < 960) {
        const int received = avcodec_receive_frame(test.codec, test.frame);
        if (received == 0) {
            ++frames;
            if (stride > 0 && frames % stride == 0) {
                const auto mapBegin = Clock::now();
                AVFrame* mapped = ffu_d3d11_stage_frame(test.stage, test.frame);
                mapCpu += Milliseconds(mapBegin);
                const auto waitBegin = Clock::now();
                if (!mapped || !Poll(test.stage, true, yieldOnly)) return false;
                mapWait += Milliseconds(waitBegin); mapTimes.push_back(Milliseconds(mapBegin));
                ++maps; pending = 0; av_frame_free(&mapped);
            }
            av_frame_unref(test.frame);
        } else if (received == AVERROR(EAGAIN)) {
            do {
                if (av_read_frame(test.input, test.packet) < 0) return false;
                if (test.packet->stream_index == stream) break;
                av_packet_unref(test.packet);
            } while (true);
            const auto sendBegin = Clock::now();
            const int sent = avcodec_send_packet(test.codec, test.packet); sends += Milliseconds(sendBegin);
            av_packet_unref(test.packet); if (sent < 0) return false;
            if (++pending >= 16) {
                const auto markerBegin = Clock::now();
                if (ffu_d3d11_decode_sync_begin(test.sync) != 0) return false;
                syncCpu += Milliseconds(markerBegin);
                const auto waitBegin = Clock::now();
                if (!Poll(test.sync, false, yieldOnly)) return false;
                syncWait += Milliseconds(waitBegin); ++markers; pending = 0;
            }
        } else return false;
    }
    if (ffu_d3d11_decode_sync_begin(test.sync) != 0 || !Poll(test.sync, false, yieldOnly)) return false;
    const double total = Milliseconds(begin);
    std::sort(mapTimes.begin(), mapTimes.end());
    const double p95 = maps ? mapTimes[static_cast<size_t>((maps - 1) * .95)] : 0;
    const double maximum = maps ? mapTimes.back() : 0;
    const int64_t target = av_rescale_q(8, AVRational{1, 1}, test.input->streams[stream]->time_base);
    const auto seekBegin = Clock::now();
    const int seek = avformat_seek_file(test.input, stream, INT64_MIN, target, target, AVSEEK_FLAG_BACKWARD);
    const double seekMs = Milliseconds(seekBegin);
    const auto flushBegin = Clock::now(); avcodec_flush_buffers(test.codec);
    std::printf("BENCH staged: frames=%d stride=%d yield=%d fps=%.1f wall=%.1fms sends=%.1fms maps=%d map_cpu=%.1fms map_wait=%.1fms map_p95=%.3fms map_max=%.3fms markers=%d sync_cpu=%.1fms sync_wait=%.1fms seek=%.3fms flush=%.3fms\n",
        frames, stride, yieldOnly, frames * 1000 / total, total, sends, maps, mapCpu, mapWait, p95, maximum, markers, syncCpu, syncWait, seekMs, Milliseconds(flushBegin));
    FfuD3D11Trace(false); std::fflush(stdout); return seek >= 0;
}
}
bool FfuD3D11Benchmark(const char* path, int stride, bool yieldOnly) {
    if (stride >= 0) return Run(path, stride, yieldOnly);
    for (int stride : {0, 6, 1, 2, 32}) if (!Run(path, stride, false)) return false;
    return Run(path, 6, true);
}
