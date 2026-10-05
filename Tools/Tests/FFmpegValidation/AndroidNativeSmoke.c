// Android shell validation: all staged libraries, ABI4, native codec profile,
// and physical-device MediaCodec/Vulkan image-import prerequisites.
// Build with the matching NDK C compiler, -std=gnu11 -Wall -Wextra -Werror,
// pinned FFmpeg/Vulkan include directories and -ldl. Do not link FFmpeg.
// Run: LD_LIBRARY_PATH=<staged-directory> android-native-smoke <staged-directory>
// This does not create a Java VM, decode hardware frames, or exercise Unity rendering.
#define _GNU_SOURCE
#include <dlfcn.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <vulkan/vulkan.h>
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>

#define CHECK(condition, message) do { \
    if (!(condition)) { fprintf(stderr, "FAIL: %s\n", message); return 1; } \
    ++checks; \
} while (0)
#define LOAD(handle, name) \
    __typeof__(&name) p_##name = (__typeof__(&name))dlsym(handle, #name); \
    CHECK(p_##name != NULL, "required export " #name)

static void* Open(const char* directory, const char* name) {
    char path[PATH_MAX];
    const int length = snprintf(path, sizeof(path), "%s/%s", directory, name);
    if (length < 0 || length >= (int)sizeof(path)) return NULL;
    void* handle = dlopen(path, RTLD_NOW | RTLD_LOCAL);
    if (!handle) fprintf(stderr, "FAIL: dlopen %s: %s\n", path, dlerror());
    return handle;
}

int main(int argc, char** argv) {
    int checks = 0;
    if (argc != 2) {
        fprintf(stderr, "Usage: %s <absolute-staged-library-directory>\n", argv[0]);
        return 2;
    }
    char directory[PATH_MAX];
    CHECK(realpath(argv[1], directory) != NULL, "staged directory exists");
    const char* names[] = {"libavformat.so", "libavcodec.so", "libavutil.so", "libavdevice.so",
                          "libavfilter.so", "libswresample.so", "libswscale.so", "libFFmpegUnityBridge.so"};
    void* libraries[8];
    for (int index = 0; index < 8; ++index) {
        libraries[index] = Open(directory, names[index]);
        CHECK(libraries[index] != NULL, "all seven FFmpeg libraries and the bridge resolve dependencies");
    }
    LOAD(libraries[0], avformat_version);
    LOAD(libraries[1], avcodec_version);
    LOAD(libraries[2], avutil_version);
    LOAD(libraries[2], av_version_info);
    LOAD(libraries[1], avcodec_configuration);
    LOAD(libraries[1], avcodec_find_decoder_by_name);
    LOAD(libraries[1], avcodec_find_encoder_by_name);
    LOAD(libraries[1], avcodec_get_hw_config);
    LOAD(libraries[0], av_guess_format);
    CHECK(p_avcodec_version() == LIBAVCODEC_VERSION_INT && p_avformat_version() == LIBAVFORMAT_VERSION_INT
          && p_avutil_version() == LIBAVUTIL_VERSION_INT, "loaded FFmpeg exactly matches the fixed public headers");
    Dl_info resolved;
    CHECK(dladdr((void*)p_avcodec_version, &resolved) != 0
          && !strncmp(resolved.dli_fname, directory, strlen(directory)), "avcodec is loaded from the staged directory");
    CHECK(strstr(p_av_version_info(), "MajdataPlay-AMF-RC-v1-c0604b924b6a") != NULL,
          "loaded source includes the fixed checked patch marker");
    CHECK(strstr(p_avcodec_configuration(), "--disable-gpl") != NULL
          && strstr(p_avcodec_configuration(), "--disable-nonfree") != NULL, "native profile keeps LGPL configuration");
    const AVCodec* software = p_avcodec_find_decoder_by_name("libdav1d");
    CHECK(software && !(software->capabilities & AV_CODEC_CAP_HARDWARE), "libdav1d supplies actual software AV1 decoding");
    const AVCodec* encoder = p_avcodec_find_encoder_by_name("mpeg4");
    CHECK(encoder && encoder->id == AV_CODEC_ID_MPEG4 && !(encoder->capabilities & AV_CODEC_CAP_HARDWARE),
          "recording profile supplies the built-in MPEG4 software encoder");
    const char* containers[] = {"mov", "mp4", "matroska", "webm", "avi"};
    for (int index = 0; index < 5; ++index) CHECK(p_av_guess_format(containers[index], NULL, NULL) != NULL,
                                               "all requested recording muxers are present");
    const char* mediated[] = {"h264_mediacodec", "hevc_mediacodec", "vp9_mediacodec", "mpeg2_mediacodec"};
    for (int index = 0; index < 4; ++index) CHECK(p_avcodec_find_decoder_by_name(mediated[index]) != NULL,
                                               "requested MediaCodec decoder is present");
    const char* native_vulkan[] = {"h264", "hevc", "av1", "vp9"};
    for (int index = 0; index < 4; ++index) {
        const AVCodec* decoder = p_avcodec_find_decoder_by_name(native_vulkan[index]);
        CHECK(decoder != NULL, "native codec candidate exists");
        int found = 0;
        for (int entry = 0; entry < 128; ++entry) {
            const AVCodecHWConfig* hardware = p_avcodec_get_hw_config(decoder, entry);
            if (!hardware) break;
            if (hardware->device_type == AV_HWDEVICE_TYPE_VULKAN) found = 1;
        }
        CHECK(found, "H264/HEVC/AV1/VP9 expose their compiled Vulkan Video backend");
    }
    int (*abi)(void) = (int (*)(void))dlsym(libraries[7], "ffu_abi_version");
    int (*available)(void) = (int (*)(void))dlsym(libraries[7], "ffu_android_is_available");
    int (*status)(void) = (int (*)(void))dlsym(libraries[7], "ffu_vulkan_video_status");
    CHECK(abi && abi() == 4, "loaded bridge reports ABI4");
    CHECK(available && available() == 1, "API26 AImage/AHardwareBuffer functions resolve dynamically");
    CHECK(status && status() == 400, "native Vulkan Video requires Unity preload before device negotiation");

    void* loader = dlopen("libvulkan.so", RTLD_NOW | RTLD_LOCAL);
    PFN_vkGetInstanceProcAddr get = loader ? (PFN_vkGetInstanceProcAddr)dlsym(loader, "vkGetInstanceProcAddr") : NULL;
    PFN_vkCreateInstance create = get ? (PFN_vkCreateInstance)get(VK_NULL_HANDLE, "vkCreateInstance") : NULL;
    CHECK(create != NULL, "system Vulkan loader resolves");
    VkApplicationInfo app = {0};
    app.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    app.apiVersion = VK_API_VERSION_1_1;
    VkInstanceCreateInfo info = {0};
    info.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    info.pApplicationInfo = &app;
    VkInstance instance = VK_NULL_HANDLE;
    CHECK(create(&info, NULL, &instance) == VK_SUCCESS, "physical device supports a Vulkan1.1 instance");
    PFN_vkEnumeratePhysicalDevices enumerate = (PFN_vkEnumeratePhysicalDevices)get(instance, "vkEnumeratePhysicalDevices");
    PFN_vkEnumerateDeviceExtensionProperties extensions = (PFN_vkEnumerateDeviceExtensionProperties)get(instance, "vkEnumerateDeviceExtensionProperties");
    PFN_vkGetPhysicalDeviceProperties properties = (PFN_vkGetPhysicalDeviceProperties)get(instance, "vkGetPhysicalDeviceProperties");
    PFN_vkGetPhysicalDeviceFeatures2 features = (PFN_vkGetPhysicalDeviceFeatures2)get(instance, "vkGetPhysicalDeviceFeatures2");
    PFN_vkDestroyInstance destroy = (PFN_vkDestroyInstance)get(instance, "vkDestroyInstance");
    CHECK(enumerate && extensions && properties && features && destroy, "physical device queries resolve");
    uint32_t count = 0;
    CHECK(enumerate(instance, &count, NULL) == VK_SUCCESS && count > 0, "a physical Vulkan GPU is available");
    VkPhysicalDevice* devices = calloc(count, sizeof(*devices));
    CHECK(devices != NULL, "physical device query buffer allocation");
    CHECK(enumerate(instance, &count, devices) == VK_SUCCESS, "enumerate physical Vulkan devices");
    int supported_device = 0;
    for (uint32_t device_index = 0; device_index < count; ++device_index) {
        VkPhysicalDeviceProperties props;
        properties(devices[device_index], &props);
        uint32_t extension_count = 0;
        CHECK(extensions(devices[device_index], NULL, &extension_count, NULL) == VK_SUCCESS,
              "enumerate GPU extension count");
        VkExtensionProperties* values = calloc(extension_count, sizeof(*values));
        CHECK(values != NULL, "GPU extension query buffer allocation");
        CHECK(extensions(devices[device_index], NULL, &extension_count, values) == VK_SUCCESS, "enumerate GPU extensions");
        const char* required[] = {"VK_ANDROID_external_memory_android_hardware_buffer",
                                 "VK_KHR_external_semaphore_fd", "VK_EXT_queue_family_foreign"};
        int supports_import = 1;
        for (int item = 0; item < 3; ++item) {
            int found = 0;
            for (uint32_t entry = 0; entry < extension_count; ++entry) {
                if (!strcmp(required[item], values[entry].extensionName)) found = 1;
            }
            supports_import &= found;
        }
        free(values);
        VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr = {0};
        ycbcr.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES;
        VkPhysicalDeviceFeatures2 feature = {0};
        feature.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
        feature.pNext = &ycbcr;
        features(devices[device_index], &feature);
        if (supports_import && ycbcr.samplerYcbcrConversion) {
            supported_device = 1;
            printf("GPU %s Vulkan %u.%u.%u: AHB/SYNC_FD/FOREIGN/YCbCr supported; Vulkan Video runtime needs1.3+.\n",
                   props.deviceName, VK_API_VERSION_MAJOR(props.apiVersion), VK_API_VERSION_MINOR(props.apiVersion),
                   VK_API_VERSION_PATCH(props.apiVersion));
        }
    }
    free(devices);
    destroy(instance, NULL);
    CHECK(supported_device, "physical GPU meets the MediaCodec AHardwareBuffer importer prerequisites");
    printf("PASS: %d checks; Android %zu-bit all8 libraries, ABI4, fixed recording/decoder profile and physical GPU prerequisites.\n",
           checks, sizeof(void*) * 8);
    for (int index = 7; index >= 0; --index) dlclose(libraries[index]);
    dlclose(loader);
    return 0;
}
