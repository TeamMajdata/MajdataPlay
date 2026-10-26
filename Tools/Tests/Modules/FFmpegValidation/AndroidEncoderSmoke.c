// JNI entry points for the physical-device Android MediaCodec encoder regression.
// Reuse the portable fixture's pixel, PTS-gap, muxing, drain and ownership checks.
#include <jni.h>
#include <libavcodec/jni.h>
#define ENCODER_SMOKE_WIDTH 320
#define ENCODER_SMOKE_HEIGHT 192
#define main PortableEncoderSmokeMain
#include "EncoderNativeSmoke.c"
#undef main

JNIEXPORT jboolean JNICALL
Java_net_majdata_ffmpegvalidation_AndroidEncoderSmoke_checkProfile(JNIEnv* env, jclass type) {
    (void)type;
    JavaVM* vm = NULL;
    if (!Check((*env)->GetJavaVM(env, &vm) == JNI_OK, "obtain the device Java VM")
        || !Native(av_jni_set_java_vm(vm, NULL), "initialize FFmpeg JNI")
        || !Check(avcodec_version() == LIBAVCODEC_VERSION_INT
                  && avformat_version() == LIBAVFORMAT_VERSION_INT
                  && avutil_version() == LIBAVUTIL_VERSION_INT,
                  "staged FFmpeg matches the fixed public headers")) return JNI_FALSE;
    static const char* names[] = {"h264_mediacodec", "hevc_mediacodec", "vp8_mediacodec",
                                  "vp9_mediacodec", "mpeg4_mediacodec", "av1_mediacodec"};
    for (unsigned int index = 0; index < sizeof(names) / sizeof(names[0]); ++index) {
        const AVCodec* codec = avcodec_find_encoder_by_name(names[index]);
        if (!Check(codec != NULL && (codec->capabilities & AV_CODEC_CAP_HARDWARE),
                   "all six MediaCodec encoder wrappers are compiled in")) return JNI_FALSE;
        printf("REGISTERED: %s\n", codec->name);
    }
    printf("Native FFmpeg %s; pointer bits=%zu\n", av_version_info(), sizeof(void*) * 8);
    fflush(stdout);
    return JNI_TRUE;
}

JNIEXPORT jint JNICALL
Java_net_majdata_ffmpegvalidation_AndroidEncoderSmoke_roundtrip(
        JNIEnv* env, jclass type, jstring output_directory, jstring encoder_name,
        jstring component_name, jboolean cbr, jboolean ndk, jboolean complex) {
    (void)type;
    const char* directory = (*env)->GetStringUTFChars(env, output_directory, NULL);
    if (!directory) return -1;
    const char* name = (*env)->GetStringUTFChars(env, encoder_name, NULL);
    if (!name) {
        (*env)->ReleaseStringUTFChars(env, output_directory, directory);
        return -1;
    }
    const char* component = (*env)->GetStringUTFChars(env, component_name, NULL);
    if (!component) {
        (*env)->ReleaseStringUTFChars(env, encoder_name, name);
        (*env)->ReleaseStringUTFChars(env, output_directory, directory);
        return -1;
    }
    const int before = Checks;
    const AVCodec* codec = avcodec_find_encoder_by_name(name);
    char file[PATH_MAX];
    const int length = snprintf(file, sizeof(file), "%s/%s-%s-%s-%s.%s", directory, name,
                                ndk ? "ndk" : "jni", cbr ? "cbr" : "vbr", complex ? "complex" : "flat",
                                codec && (codec->id == AV_CODEC_ID_VP8 || codec->id == AV_CODEC_ID_VP9)
                                    ? "webm" : "mp4");
    AndroidCodecName = component;
    AndroidNdkCodec = ndk == JNI_TRUE;
    ComplexPattern = complex == JNI_TRUE;
    printf("HARDWARE: wrapper=%s component=%s API=%s mode=%s scene=%s\n", name, component,
           ndk ? "NDK" : "JNI", cbr ? "CBR" : "VBR", complex ? "complex" : "flat");
    fflush(stdout);
    const int success = Check(codec != NULL, "requested encoder remains available")
        && Check(length > 0 && length < (int)sizeof(file), "recording output path fits")
        && Encode(file, name, cbr == JNI_TRUE) && Decode(file, codec->id) && CheckNoOverwrite(file);
    AndroidCodecName = NULL;
    (*env)->ReleaseStringUTFChars(env, component_name, component);
    (*env)->ReleaseStringUTFChars(env, encoder_name, name);
    (*env)->ReleaseStringUTFChars(env, output_directory, directory);
    fflush(stdout);
    return success ? Checks - before : -1;
}

JNIEXPORT jboolean JNICALL
Java_net_majdata_ffmpegvalidation_AndroidEncoderSmoke_rejectMissingComponent(
        JNIEnv* env, jclass type, jboolean ndk) {
    (void)env;
    (void)type;
    const AVCodec* codec = avcodec_find_encoder_by_name("h264_mediacodec");
    AVCodecContext* context = avcodec_alloc_context3(codec);
    if (!Check(codec && context, "allocate rejected-component context")) {
        avcodec_free_context(&context);
        return JNI_FALSE;
    }
    context->width = Width;
    context->height = Height;
    context->time_base = (AVRational){1, FrameRate};
    context->framerate = (AVRational){FrameRate, 1};
    context->pix_fmt = AV_PIX_FMT_NV12;
    context->bit_rate = TargetBitRate;
    AndroidCodecName = "majdata.validation.missing.encoder";
    AndroidNdkCodec = ndk == JNI_TRUE;
    const int configured = ConfigureHardware(context, codec->name, 0);
    const int result = configured ? avcodec_open2(context, codec, NULL) : 0;
    AndroidCodecName = NULL;
    avcodec_free_context(&context);
    return configured && Check(result < 0, "missing explicit component fails without software fallback")
        ? JNI_TRUE : JNI_FALSE;
}
