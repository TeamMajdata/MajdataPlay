// Android shell smoke: verifies native ABI/loading and device prerequisites.
// This test never maps an image or reads decoded pixels. Playback is covered
// separately by the Unity Android player test, which supplies a real Java VM.
#include <vulkan/vulkan.h>
#include <dlfcn.h>
#include <cstdio>
#include <cstring>
#include <vector>

int main() {
    void* bridge = dlopen("libFFmpegUnityBridge.so", RTLD_NOW | RTLD_LOCAL);
    if (!bridge) { std::fprintf(stderr, "FAIL bridge load: %s\n", dlerror()); return 1; }
    auto available = reinterpret_cast<int (*)()>(dlsym(bridge, "ffu_android_is_available"));
    auto abi = reinterpret_cast<int (*)()>(dlsym(bridge, "ffu_abi_version"));
    if (!available || available() != 1 || !abi || abi() != 3) {
        std::fprintf(stderr, "FAIL API26 image functions or bridge ABI3\n"); return 2;
    }
    void* codec = dlopen("libavcodec.so", RTLD_NOW | RTLD_LOCAL);
    auto find = codec ? reinterpret_cast<void* (*)(const char*)>(dlsym(codec, "avcodec_find_decoder_by_name")) : nullptr;
    if (!find) { std::fprintf(stderr, "FAIL codec library\n"); return 3; }
    for (const char* name : {"h264_mediacodec", "hevc_mediacodec", "vp9_mediacodec", "mpeg2_mediacodec"}) {
        if (!find(name)) { std::fprintf(stderr, "FAIL missing decoder %s\n", name); return 4; }
    }
    void* loader = dlopen("libvulkan.so", RTLD_NOW | RTLD_LOCAL);
    auto get = loader ? reinterpret_cast<PFN_vkGetInstanceProcAddr>(dlsym(loader, "vkGetInstanceProcAddr")) : nullptr;
    auto create = get ? reinterpret_cast<PFN_vkCreateInstance>(get(nullptr, "vkCreateInstance")) : nullptr;
    if (!create) { std::fprintf(stderr, "FAIL Vulkan loader\n"); return 5; }
    VkApplicationInfo app{}; app.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO; app.apiVersion = VK_API_VERSION_1_1;
    VkInstanceCreateInfo info{}; info.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO; info.pApplicationInfo = &app;
    VkInstance instance = VK_NULL_HANDLE;
    if (create(&info, nullptr, &instance) != VK_SUCCESS) { std::fprintf(stderr, "FAIL Vulkan 1.1 instance\n"); return 6; }
    auto enumerate = reinterpret_cast<PFN_vkEnumeratePhysicalDevices>(get(instance, "vkEnumeratePhysicalDevices"));
    auto extensions = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(get(instance, "vkEnumerateDeviceExtensionProperties"));
    auto properties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(get(instance, "vkGetPhysicalDeviceProperties"));
    auto features = reinterpret_cast<PFN_vkGetPhysicalDeviceFeatures2>(get(instance, "vkGetPhysicalDeviceFeatures2"));
    auto destroy = reinterpret_cast<PFN_vkDestroyInstance>(get(instance, "vkDestroyInstance"));
    uint32_t count = 0;
    if (!enumerate || !extensions || !properties || !features || !destroy || enumerate(instance, &count, nullptr) != VK_SUCCESS || count == 0) return 7;
    std::vector<VkPhysicalDevice> devices(count);
    if (enumerate(instance, &count, devices.data()) != VK_SUCCESS) return 8;
    for (auto device : devices) {
        VkPhysicalDeviceProperties props{}; properties(device, &props);
        uint32_t extensionCount = 0;
        if (extensions(device, nullptr, &extensionCount, nullptr) != VK_SUCCESS) continue;
        std::vector<VkExtensionProperties> names(extensionCount);
        if (extensions(device, nullptr, &extensionCount, names.data()) != VK_SUCCESS) continue;
        bool supported = true;
        for (const char* required : {"VK_ANDROID_external_memory_android_hardware_buffer", "VK_KHR_external_semaphore_fd", "VK_EXT_queue_family_foreign"}) {
            bool found = false;
            for (const auto& name : names) if (!std::strcmp(required, name.extensionName)) found = true;
            supported &= found;
        }
        VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr{};
        ycbcr.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES;
        VkPhysicalDeviceFeatures2 feature{}; feature.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2; feature.pNext = &ycbcr;
        features(device, &feature);
        if (supported && ycbcr.samplerYcbcrConversion) {
            std::printf("PASS Android native %zu-bit: ABI3, API26 dynamic symbols, 4 MediaCodec decoders; GPU %s, AHB+SYNC_FD+FOREIGN+YCbCr\n", sizeof(void*) * 8, props.deviceName);
            destroy(instance, nullptr);
            return 0;
        }
    }
    destroy(instance, nullptr);
    std::fprintf(stderr, "FAIL missing hardware device capabilities\n"); return 9;
}
