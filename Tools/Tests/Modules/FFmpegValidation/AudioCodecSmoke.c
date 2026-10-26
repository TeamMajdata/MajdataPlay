// Native audio round trips against the pinned FFmpeg libraries; no Unity or audio device.
// Link avformat, avcodec, swresample, avutil and libm. Run with a new output directory.
#include <inttypes.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/opt.h>
#include <libavutil/samplefmt.h>
#include <libswresample/swresample.h>

enum { Rate = 48000, Channels = 2, Capacity = 8192 };
static const double Tau = 6.2831853071795864769;
static int Checks;

static void Require(int condition, const char* message)
{
    if (!condition)
    {
        fprintf(stderr, "FAIL: %s\n", message);
        exit(1);
    }
    ++Checks;
}

static void Native(int result, const char* operation)
{
    if (result < 0)
    {
        char error[AV_ERROR_MAX_STRING_SIZE];
        av_strerror(result, error, sizeof(error));
        fprintf(stderr, "FAIL: %s: %s (%d)\n", operation, error, result);
        exit(1);
    }
    ++Checks;
}

typedef struct Audio
{
    AVCodecContext* encoder;
    AVCodecContext* decoder;
    AVFrame* decoded;
    AVPacket* packet;
    SwrContext* converter;
    AVFormatContext* muxer;
    AVStream* stream;
    int64_t samples;
    int packets;
    double power[Channels], real[Channels], imaginary[Channels], other[Channels], other_imaginary[Channels];
} Audio;

static void ReceiveAudio(Audio* audio, int draining)
{
    int result;
    while ((result = avcodec_receive_frame(audio->decoder, audio->decoded)) >= 0)
    {
        AVFrame* frame = audio->decoded;
        Require(frame->sample_rate == Rate && frame->ch_layout.nb_channels == Channels, "decoded rate/channel layout");
        Require(frame->nb_samples > 0 && frame->nb_samples <= Capacity, "bounded nonempty audio frame");
        if (!audio->converter)
        {
            Native(swr_alloc_set_opts2(&audio->converter, &frame->ch_layout, AV_SAMPLE_FMT_FLT, Rate,
                &frame->ch_layout, (enum AVSampleFormat)frame->format, Rate, 0, NULL), "decode converter allocation");
            Native(swr_init(audio->converter), "decode converter initialization");
        }
        float samples[Capacity * Channels];
        uint8_t* output[] = { (uint8_t*)samples };
        int count = swr_convert(audio->converter, output, Capacity, (const uint8_t**)frame->extended_data, frame->nb_samples);
        Native(count, "decoded samples to float");
        Require(count == frame->nb_samples, "sample conversion preserves count");
        for (int i = 0; i < count; ++i)
        {
            for (int channel = 0; channel < Channels; ++channel)
            {
                double value = samples[i * Channels + channel];
                double angle = Tau * (channel ? 880.0 : 440.0) * (double)(audio->samples + i) / Rate;
                double other = Tau * (channel ? 440.0 : 880.0) * (double)(audio->samples + i) / Rate;
                Require(isfinite(value), "finite decoded sample");
                audio->power[channel] += value * value;
                audio->real[channel] += value * cos(angle);
                audio->imaginary[channel] += value * sin(angle);
                audio->other[channel] += value * cos(other);
                audio->other_imaginary[channel] += value * sin(other);
            }
        }
        audio->samples += count;
        av_frame_unref(frame);
    }
    Require(result == (draining ? AVERROR_EOF : AVERROR(EAGAIN)), "decoder drain/EAGAIN contract");
}

static void ReceivePackets(Audio* audio, int draining)
{
    int result;
    while ((result = avcodec_receive_packet(audio->encoder, audio->packet)) >= 0)
    {
        Require(audio->packet->size > 0, "nonempty encoded packet");
        Native(avcodec_send_packet(audio->decoder, audio->packet), "submit encoded audio to decoder");
        ReceiveAudio(audio, 0);
        if (audio->muxer)
        {
            AVPacket* copy = av_packet_clone(audio->packet);
            Require(copy != NULL, "mux packet reference allocation");
            copy->stream_index = audio->stream->index;
            av_packet_rescale_ts(copy, audio->encoder->time_base, audio->stream->time_base);
            Native(av_interleaved_write_frame(audio->muxer, copy), "mux audio packet");
            av_packet_free(&copy);
        }
        ++audio->packets;
        av_packet_unref(audio->packet);
    }
    Require(result == (draining ? AVERROR_EOF : AVERROR(EAGAIN)), "encoder drain/EAGAIN contract");
}

static void RoundTrip(const char* encoder_name, const char* decoder_name, const char* container, const char* directory)
{
    printf("RUN %s -> %s\n", encoder_name, decoder_name);
    Audio audio = { 0 };
    const AVCodec* encoder = avcodec_find_encoder_by_name(encoder_name);
    const AVCodec* decoder = avcodec_find_decoder_by_name(decoder_name);
    Require(encoder && decoder, "requested encoder and decoder registration");
    Require(encoder->id == decoder->id, "encoder/decoder codec identity");
    audio.encoder = avcodec_alloc_context3(encoder);
    audio.decoder = avcodec_alloc_context3(decoder);
    audio.decoded = av_frame_alloc();
    audio.packet = av_packet_alloc();
    Require(audio.encoder && audio.decoder && audio.decoded && audio.packet, "audio object allocation");
    audio.encoder->sample_rate = Rate;
    audio.encoder->time_base = (AVRational){ 1, Rate };
    audio.encoder->bit_rate = 128000;
    audio.encoder->thread_count = 1;
    av_channel_layout_default(&audio.encoder->ch_layout, Channels);
    const enum AVSampleFormat* formats = NULL;
    int format_count = 0;
    Native(avcodec_get_supported_config(audio.encoder, encoder, AV_CODEC_CONFIG_SAMPLE_FORMAT, 0,
        (const void**)&formats, &format_count), "supported encoder sample formats");
    Require(formats && format_count > 0, "encoder exposes sample formats");
    audio.encoder->sample_fmt = formats[0];
    Native(avcodec_open2(audio.encoder, encoder, NULL), "open audio encoder without experimental mode");
    AVCodecParameters* parameters = avcodec_parameters_alloc();
    Require(parameters != NULL, "audio parameters allocation");
    Native(avcodec_parameters_from_context(parameters, audio.encoder), "copy audio encoder parameters");
    Native(avcodec_parameters_to_context(audio.decoder, parameters), "configure audio decoder");
    audio.decoder->thread_count = 1;
    Native(avcodec_open2(audio.decoder, decoder, NULL), "open audio decoder");
    char path[4096] = { 0 };
    if (container)
    {
        Require(snprintf(path, sizeof(path), "%s/%s-%s.%s", directory, encoder_name, decoder_name, container) < (int)sizeof(path), "output path fits");
        Require(avio_check(path, AVIO_FLAG_READ) < 0, "refuse to overwrite an existing test file");
        Native(avformat_alloc_output_context2(&audio.muxer, NULL, container, path), "allocate audio muxer");
        audio.stream = avformat_new_stream(audio.muxer, NULL);
        Require(audio.stream != NULL, "audio output stream allocation");
        Native(avcodec_parameters_copy(audio.stream->codecpar, parameters), "configure muxed audio stream");
        audio.stream->time_base = audio.encoder->time_base;
        Native(avio_open(&audio.muxer->pb, path, AVIO_FLAG_WRITE), "open audio output");
        Native(avformat_write_header(audio.muxer, NULL), "write audio header");
    }
    AVFrame* input = av_frame_alloc();
    Require(input != NULL, "input audio frame allocation");
    input->format = audio.encoder->sample_fmt;
    input->sample_rate = Rate;
    input->nb_samples = audio.encoder->frame_size ? audio.encoder->frame_size : 1024;
    Require(input->nb_samples <= Capacity, "bounded encoder frame size");
    Native(av_channel_layout_copy(&input->ch_layout, &audio.encoder->ch_layout), "input channel layout");
    Native(av_frame_get_buffer(input, 0), "input audio buffer allocation");
    SwrContext* converter = NULL;
    Native(swr_alloc_set_opts2(&converter, &input->ch_layout, (enum AVSampleFormat)input->format, Rate,
        &input->ch_layout, AV_SAMPLE_FMT_FLT, Rate, 0, NULL), "input sample converter allocation");
    Native(swr_init(converter), "input sample converter initialization");
    int64_t submitted = 0;
    while (submitted < Rate)
    {
        Native(av_frame_make_writable(input), "writable input audio");
        float samples[Capacity * Channels];
        for (int i = 0; i < input->nb_samples; ++i)
        {
            for (int channel = 0; channel < Channels; ++channel)
            {
                samples[i * Channels + channel] = (float)(0.4 * sin(Tau * (channel ? 880.0 : 440.0) * (double)(submitted + i) / Rate));
            }
        }
        const uint8_t* source[] = { (const uint8_t*)samples };
        int count = swr_convert(converter, input->extended_data, input->nb_samples, source, input->nb_samples);
        Require(count == input->nb_samples, "input conversion preserves count");
        input->pts = submitted;
        Native(avcodec_send_frame(audio.encoder, input), "submit audio frame");
        submitted += input->nb_samples;
        ReceivePackets(&audio, 0);
    }
    Native(avcodec_send_frame(audio.encoder, NULL), "flush audio encoder");
    ReceivePackets(&audio, 1);
    Native(avcodec_send_packet(audio.decoder, NULL), "flush audio decoder");
    ReceiveAudio(&audio, 1);
    Require(avcodec_receive_frame(audio.decoder, audio.decoded) == AVERROR_EOF, "stable decoder EOF");
    Require(audio.packets > 0 && audio.samples >= submitted && audio.samples <= submitted + 4096, "decoded duration including codec padding");
    for (int channel = 0; channel < Channels; ++channel)
    {
        double amplitude = 2.0 * hypot(audio.real[channel], audio.imaginary[channel]) / (double)audio.samples;
        double leakage = 2.0 * hypot(audio.other[channel], audio.other_imaginary[channel]) / (double)audio.samples;
        double rms = sqrt(audio.power[channel] / (double)audio.samples);
        printf("  %s channel=%d amplitude=%.6f rms=%.6f leakage=%.6f\n", encoder_name, channel, amplitude, rms, leakage);
        Require(amplitude > 0.30 && amplitude < 0.48 && rms > 0.20 && rms < 0.34, "decoded waveform amplitude and energy");
        Require(leakage < 0.04, "stereo frequencies remain in their own channels");
    }
    if (audio.muxer)
    {
        Native(av_write_trailer(audio.muxer), "flush audio container trailer");
        Native(avio_closep(&audio.muxer->pb), "close audio output");
        AVFormatContext* demuxer = NULL;
        Native(avformat_open_input(&demuxer, path, NULL, NULL), "reopen audio container");
        Native(avformat_find_stream_info(demuxer, NULL), "probe muxed audio");
        Require(demuxer->nb_streams == 1 && demuxer->streams[0]->codecpar->codec_id == encoder->id, "demuxed audio codec identity");
        int packets = 0, result;
        while ((result = av_read_frame(demuxer, audio.packet)) >= 0)
        {
            Require(audio.packet->size > 0, "demuxed nonempty audio packet");
            ++packets;
            av_packet_unref(audio.packet);
        }
        Require(result == AVERROR_EOF && packets > 0, "demux audio through stable EOF");
        avformat_close_input(&demuxer);
        avformat_free_context(audio.muxer);
    }
    printf("PASS %s -> %s: packets=%d samples=%" PRId64 " input=%" PRId64 " container=%s\n",
        encoder_name, decoder_name, audio.packets, audio.samples, submitted, container ? container : "packet round trip");
    swr_free(&converter);
    swr_free(&audio.converter);
    av_frame_free(&input);
    av_frame_free(&audio.decoded);
    av_packet_free(&audio.packet);
    avcodec_parameters_free(&parameters);
    avcodec_free_context(&audio.encoder);
    avcodec_free_context(&audio.decoder);
}

int main(int argc, char** argv)
{
    setvbuf(stdout, NULL, _IONBF, 0);
    if (argc != 2)
    {
        fprintf(stderr, "Usage: %s <new-existing-output-directory>\n", argv[0]);
        return 2;
    }
    av_log_set_level(AV_LOG_ERROR);
    Require(avcodec_version() == LIBAVCODEC_VERSION_INT, "loaded avcodec matches pinned headers");
    Require(avformat_version() == LIBAVFORMAT_VERSION_INT, "loaded avformat matches pinned headers");
    Require(swresample_version() == LIBSWRESAMPLE_VERSION_INT, "loaded swresample matches pinned headers");
    RoundTrip("aac", "aac", "adts", argv[1]);
    RoundTrip("libopus", "opus", "opus", argv[1]);
    RoundTrip("libopus", "libopus", "ogg", argv[1]);
    RoundTrip("libmp3lame", "mp3float", "mp3", argv[1]);
    RoundTrip("libmp3lame", "mp3", NULL, argv[1]);
    const char* pcm[] = { "alaw", "bluray", "dvd", "f32be", "f32le", "f64be", "f64le", "mulaw",
        "s8", "s8_planar", "s16be", "s16be_planar", "s16le", "s16le_planar", "s24be", "s24daud",
        "s24le", "s24le_planar", "s32be", "s32le", "s32le_planar", "s64be", "s64le", "u8",
        "u16be", "u16le", "u24be", "u24le", "u32be", "u32le" };
    for (size_t i = 0; i < sizeof(pcm) / sizeof(pcm[0]); ++i)
    {
        char name[64];
        snprintf(name, sizeof(name), "pcm_%s", pcm[i]);
        RoundTrip(name, name, strcmp(name, "pcm_s16le") == 0 ? "wav" : NULL, argv[1]);
    }
    // The legacy Archimedes VIDC codec is registered but not part of this signal
    // fidelity test: pinned upstream's encoder has a distorted companding table.
    Require(avcodec_find_encoder_by_name("pcm_vidc") && avcodec_find_decoder_by_name("pcm_vidc"), "legacy VIDC registration");
    printf("PASS: %d checks; 35 real stereo audio round trips, encoder/decoder drain, waveform/channel identity, audio containers; VIDC registration only.\n", Checks);
    return 0;
}
