#include "PortableAndroidJava.h"
#include <jni.h>
#include <atomic>
#include <cstdio>
#include <mutex>
#include <unordered_map>

namespace
{
std::atomic<JavaVM*> javaVm{nullptr};
std::mutex javaMutex;
jclass windowClass = nullptr;
jmethodID windowConstructor = nullptr;
std::unordered_map<libvlc_media_player_t*, jobject> playerWindows;

class ScopedEnv
{
public:
    ScopedEnv() : vm(javaVm.load())
    {
        if (!vm) return;
        const auto result = vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);
        if (result == JNI_EDETACHED)
        {
            if (vm->AttachCurrentThread(&env, nullptr) == JNI_OK) attached = true;
            else env = nullptr;
        }
        else if (result != JNI_OK) env = nullptr;
    }
    ~ScopedEnv() { if (attached) vm->DetachCurrentThread(); }
    JNIEnv* Get() const { return env; }
private:
    JavaVM* vm = nullptr;
    JNIEnv* env = nullptr;
    bool attached = false;
};

void MissingHelper(const char* reason)
{
    std::fprintf(stderr, "[VLCUnity] Android MediaCodec context unavailable: %s. "
        "Package the matching LibVLC AAR Java helpers; GPU texture sharing alone "
        "does not establish hardware or zero-CPU-copy decoding.\n", reason);
}
}

extern "C" JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM* vm, void*)
{
    JNIEnv* env = nullptr;
    if (!vm || vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) != JNI_OK)
        return JNI_ERR;
    std::lock_guard<std::mutex> guard(javaMutex);
    javaVm = vm;
    // System.loadLibrary invokes this method with the application's class
    // loader. Cache the class now: FindClass on attached decoder threads would
    // otherwise use the boot class loader and fail to find AAR classes.
    jclass localClass = env->FindClass("org/videolan/libvlc/AWindow");
    if (!env->ExceptionCheck() && localClass)
    {
        windowConstructor = env->GetMethodID(localClass, "<init>",
            "(Lorg/videolan/libvlc/AWindow$SurfaceCallback;)V");
        if (!env->ExceptionCheck() && windowConstructor)
            windowClass = static_cast<jclass>(env->NewGlobalRef(localClass));
    }
    if (localClass) env->DeleteLocalRef(localClass);
    if (env->ExceptionCheck()) env->ExceptionClear();
    if (!windowClass) MissingHelper("org.videolan.libvlc.AWindow or its constructor was not found");
    // LibVLC's own JNI_OnLoad is invoked once by the preceding managed
    // System.loadLibrary("vlc"). Calling it again here can initialize duplicate
    // Java/native global state, so this bridge never forwards JNI_OnLoad.
    return JNI_VERSION_1_6;
}

extern "C" JNIEXPORT void JNICALL JNI_OnUnload(JavaVM* vm, void*)
{
    std::lock_guard<std::mutex> guard(javaMutex);
    JNIEnv* env = nullptr;
    if (vm && vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK)
    {
        if (windowClass) env->DeleteGlobalRef(windowClass);
        // An active player pins its Java helper until native release. In a
        // correct shutdown all players are released before library unload.
        if (!playerWindows.empty())
            std::fprintf(stderr, "[VLCUnity] Android library unloaded with active players.\n");
    }
    windowClass = nullptr; windowConstructor = nullptr; javaVm = nullptr;
}

void PortableAndroidAttachPlayer(libvlc_media_player_t* player, PortableAndroidSetContext setter)
{
    if (!player) return;
    ScopedEnv scoped;
    JNIEnv* env = scoped.Get();
    if (!env) { MissingHelper("JNI_OnLoad has not received the application JavaVM"); return; }
    if (!setter) { MissingHelper("the matching engine does not export libvlc_media_player_set_android_context"); return; }
    std::lock_guard<std::mutex> guard(javaMutex);
    if (!windowClass || !windowConstructor) return; // JNI_OnLoad already logged the cause.
    jobject localWindow = env->NewObject(windowClass, windowConstructor, nullptr);
    if (env->ExceptionCheck())
    {
        env->ExceptionClear();
        if (localWindow) env->DeleteLocalRef(localWindow);
        MissingHelper("AWindow construction failed on the calling thread");
        return;
    }
    jobject globalWindow = localWindow ? env->NewGlobalRef(localWindow) : nullptr;
    if (localWindow) env->DeleteLocalRef(localWindow);
    if (env->ExceptionCheck()) env->ExceptionClear();
    if (!globalWindow) { MissingHelper("AWindow global reference allocation failed"); return; }
    playerWindows.emplace(player, globalWindow);
    // In the pinned VLC revision, modules/video_output/android/utils.c calls
    // RegisterNatives for AWindow's native mouse/window callbacks itself.
    // A separate Java LibVLC instance or libvlcjni.so is not needed for them.
    setter(player, globalWindow);
}

void PortableAndroidReleasePlayer(libvlc_media_player_t* player)
{
    // Called only after libvlc_media_player_release has joined MediaCodec and
    // video-output callbacks, so they can no longer access the AWindow object.
    ScopedEnv scoped;
    std::lock_guard<std::mutex> guard(javaMutex);
    auto found = playerWindows.find(player);
    if (found == playerWindows.end()) return;
    if (scoped.Get()) scoped.Get()->DeleteGlobalRef(found->second);
    playerWindows.erase(found);
}
