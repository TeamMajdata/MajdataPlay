#include "Bridge.h"
#include "VulkanPortable.h"
extern "C" {
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_drm.h>
}
#include <dirent.h>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <unistd.h>
#include <fcntl.h>
#include <cstring>
#include <string>
#include <vector>
#include <limits>

namespace {
constexpr uint32_t Fourcc(char a, char b, char c, char d) { return uint32_t(a) | uint32_t(b) << 8 | uint32_t(c) << 16 | uint32_t(d) << 24; }
constexpr uint64_t InvalidModifier = (uint64_t(1) << 56) - 1;
std::mutex pathMutex;
std::string renderNode;
struct ImportedPlanes {
    std::shared_ptr<FfuVkContext> context;
    AVFrame* mapped = nullptr;
    VkImage images[2]{};
    VkDeviceMemory memory[2]{};
    VkImageView views[2]{};
    VkSampler sampler = VK_NULL_HANDLE;
    ~ImportedPlanes() {
        std::lock_guard<std::recursive_mutex> lock(context->resources);
        if (context->active) {
            const auto device = context->instance.device;
            auto destroyView = reinterpret_cast<PFN_vkDestroyImageView>(context->Proc("vkDestroyImageView"));
            auto destroyImage = reinterpret_cast<PFN_vkDestroyImage>(context->Proc("vkDestroyImage"));
            auto freeMemory = reinterpret_cast<PFN_vkFreeMemory>(context->Proc("vkFreeMemory"));
            auto destroySampler = reinterpret_cast<PFN_vkDestroySampler>(context->Proc("vkDestroySampler"));
            for (int i = 0; i < 2; ++i) {
                if (views[i]) destroyView(device, views[i], nullptr);
                if (images[i]) destroyImage(device, images[i], nullptr);
                if (memory[i]) freeMemory(device, memory[i], nullptr);
            }
            if (sampler) destroySampler(device, sampler, nullptr);
        }
        av_frame_free(&mapped);
    }
};
bool ImportPlane(ImportedPlanes& owner, const AVDRMFrameDescriptor& drm, int index, int width, int height, int& error) {
    auto& c = *owner.context; const auto device = c.instance.device;
#define LOAD(name) auto name = reinterpret_cast<PFN_vk##name>(c.Proc("vk" #name)); if (!name) { error = 205; return false; }
    LOAD(CreateImage) LOAD(GetImageMemoryRequirements) LOAD(GetMemoryFdPropertiesKHR) LOAD(AllocateMemory)
    LOAD(BindImageMemory) LOAD(CreateImageView)
#undef LOAD
    auto getImageProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceImageFormatProperties2>(c.InstanceProc("vkGetPhysicalDeviceImageFormatProperties2"));
    if (!getImageProperties) getImageProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceImageFormatProperties2>(c.InstanceProc("vkGetPhysicalDeviceImageFormatProperties2KHR"));
    auto getFormatProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceFormatProperties2>(c.InstanceProc("vkGetPhysicalDeviceFormatProperties2"));
    if (!getFormatProperties) getFormatProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceFormatProperties2>(c.InstanceProc("vkGetPhysicalDeviceFormatProperties2KHR"));
    if (!getImageProperties || !getFormatProperties) { error = 205; return false; }
    const auto& layer = drm.layers[index];
    if (layer.nb_planes < 1 || layer.nb_planes > AV_DRM_MAX_PLANES) { error = 206; return false; }
    const int objectIndex = layer.planes[0].object_index;
    if (objectIndex < 0 || objectIndex >= drm.nb_objects) { error = 206; return false; }
    const auto& object = drm.objects[objectIndex];
    if (object.fd < 0 || !object.size || object.format_modifier == InvalidModifier) { error = 207; return false; }
    VkSubresourceLayout layouts[AV_DRM_MAX_PLANES]{};
    for (int i = 0; i < layer.nb_planes; ++i) {
        const auto& p = layer.planes[i];
        if (p.object_index != objectIndex || p.offset < 0 || p.pitch <= 0 || static_cast<size_t>(p.offset) >= object.size) { error = 206; return false; }
        layouts[i].offset = static_cast<VkDeviceSize>(p.offset); layouts[i].rowPitch = static_cast<VkDeviceSize>(p.pitch);
    }
    const VkFormat format = index == 0 ? VK_FORMAT_R8_UNORM : VK_FORMAT_R8G8_UNORM;
    VkDrmFormatModifierPropertiesListEXT modifierList{}; modifierList.sType = VK_STRUCTURE_TYPE_DRM_FORMAT_MODIFIER_PROPERTIES_LIST_EXT;
    VkFormatProperties2 properties{}; properties.sType = VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2; properties.pNext = &modifierList;
    getFormatProperties(c.instance.physicalDevice, format, &properties);
    std::vector<VkDrmFormatModifierPropertiesEXT> modifiers(modifierList.drmFormatModifierCount);
    modifierList.pDrmFormatModifierProperties = modifiers.data(); getFormatProperties(c.instance.physicalDevice, format, &properties);
    bool supported = false;
    for (const auto& modifier : modifiers)
        if (modifier.drmFormatModifier == object.format_modifier && modifier.drmFormatModifierPlaneCount == static_cast<uint32_t>(layer.nb_planes) &&
            (modifier.drmFormatModifierTilingFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)) supported = true;
    if (!supported) { error = 207; return false; }
    VkPhysicalDeviceImageDrmFormatModifierInfoEXT modifier{}; modifier.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_DRM_FORMAT_MODIFIER_INFO_EXT;
    modifier.drmFormatModifier = object.format_modifier; modifier.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    VkPhysicalDeviceExternalImageFormatInfo external{}; external.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO;
    external.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT; external.pNext = &modifier;
    VkPhysicalDeviceImageFormatInfo2 query{}; query.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2;
    query.pNext = &external; query.format = format; query.type = VK_IMAGE_TYPE_2D;
    query.tiling = VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT; query.usage = VK_IMAGE_USAGE_SAMPLED_BIT; query.flags = VK_IMAGE_CREATE_ALIAS_BIT;
    VkExternalImageFormatProperties sharing{}; sharing.sType = VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES;
    VkImageFormatProperties2 resultProperties{}; resultProperties.sType = VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2; resultProperties.pNext = &sharing;
    VkResult result = getImageProperties(c.instance.physicalDevice, &query, &resultProperties);
    if (result != VK_SUCCESS || !(sharing.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT)) { error = 207; return false; }
    VkImageDrmFormatModifierExplicitCreateInfoEXT explicitLayout{}; explicitLayout.sType = VK_STRUCTURE_TYPE_IMAGE_DRM_FORMAT_MODIFIER_EXPLICIT_CREATE_INFO_EXT;
    explicitLayout.drmFormatModifier = object.format_modifier; explicitLayout.drmFormatModifierPlaneCount = layer.nb_planes; explicitLayout.pPlaneLayouts = layouts;
    VkExternalMemoryImageCreateInfo externalImage{}; externalImage.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
    externalImage.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT; externalImage.pNext = &explicitLayout;
    VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO; image.pNext = &externalImage;
    image.flags = VK_IMAGE_CREATE_ALIAS_BIT; image.imageType = VK_IMAGE_TYPE_2D; image.format = format;
    image.extent = {static_cast<uint32_t>(width), static_cast<uint32_t>(height), 1}; image.mipLevels = image.arrayLayers = 1;
    image.samples = VK_SAMPLE_COUNT_1_BIT; image.tiling = VK_IMAGE_TILING_DRM_FORMAT_MODIFIER_EXT; image.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
    result = CreateImage(device, &image, nullptr, &owner.images[index]);
    if (result != VK_SUCCESS) { error = result; return false; }
    VkMemoryRequirements requirements{}; GetImageMemoryRequirements(device, owner.images[index], &requirements);
    VkMemoryFdPropertiesKHR fdProperties{}; fdProperties.sType = VK_STRUCTURE_TYPE_MEMORY_FD_PROPERTIES_KHR;
    result = GetMemoryFdPropertiesKHR(device, VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT, object.fd, &fdProperties);
    int type = c.MemoryType(requirements.memoryTypeBits & fdProperties.memoryTypeBits);
    if (result != VK_SUCCESS || type < 0 || requirements.size > object.size) { error = 208; return false; }
    // Vulkan consumes this duplicate only on successful allocation. The DRM
    // AVFrame continues owning the original descriptors and decoder surface.
    int duplicate = fcntl(object.fd, F_DUPFD_CLOEXEC, 0);
    if (duplicate < 0) { error = 208; return false; }
    VkMemoryDedicatedAllocateInfo dedicated{}; dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO; dedicated.image = owner.images[index];
    VkImportMemoryFdInfoKHR imported{}; imported.sType = VK_STRUCTURE_TYPE_IMPORT_MEMORY_FD_INFO_KHR;
    imported.handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_DMA_BUF_BIT_EXT; imported.fd = duplicate;
    if (sharing.externalMemoryProperties.externalMemoryFeatures & VK_EXTERNAL_MEMORY_FEATURE_DEDICATED_ONLY_BIT) imported.pNext = &dedicated;
    VkMemoryAllocateInfo allocate{}; allocate.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; allocate.pNext = &imported;
    allocate.allocationSize = object.size; allocate.memoryTypeIndex = static_cast<uint32_t>(type);
    result = AllocateMemory(device, &allocate, nullptr, &owner.memory[index]);
    if (result != VK_SUCCESS) { close(duplicate); error = result; return false; }
    result = BindImageMemory(device, owner.images[index], owner.memory[index], 0);
    if (result != VK_SUCCESS) { error = result; return false; }
    VkImageViewCreateInfo view{}; view.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    view.image = owner.images[index]; view.viewType = VK_IMAGE_VIEW_TYPE_2D; view.format = format;
    view.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    result = CreateImageView(device, &view, nullptr, &owner.views[index]);
    if (result != VK_SUCCESS) { error = result; return false; }
    return true;
}
}

bool FfuLinuxInitialize() {
    std::lock_guard<std::mutex> lock(pathMutex); renderNode.clear();
    auto c = FfuVkCurrent(); if (!c) return false;
    auto properties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties2>(c->InstanceProc("vkGetPhysicalDeviceProperties2"));
    if (!properties) properties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties2>(c->InstanceProc("vkGetPhysicalDeviceProperties2KHR"));
    if (!properties) { FfuVkSetStatus(200); return false; }
    VkPhysicalDeviceDrmPropertiesEXT drm{}; drm.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DRM_PROPERTIES_EXT;
    VkPhysicalDeviceProperties2 info{}; info.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2; info.pNext = &drm;
    properties(c->instance.physicalDevice, &info);
    if (!drm.hasRender) { FfuVkSetStatus(200); return false; }
    DIR* directory = opendir("/dev/dri");
    if (directory) {
        while (auto* entry = readdir(directory)) {
            if (std::strncmp(entry->d_name, "renderD", 7)) continue;
            std::string path = std::string("/dev/dri/") + entry->d_name;
            struct stat details{};
            if (!stat(path.c_str(), &details) && S_ISCHR(details.st_mode) && major(details.st_rdev) == drm.renderMajor && minor(details.st_rdev) == drm.renderMinor) {
                renderNode = std::move(path); break;
            }
        }
        closedir(directory);
    }
    if (renderNode.empty()) { FfuVkSetStatus(200); return false; }
    AVHWDeviceType type = AV_HWDEVICE_TYPE_NONE; bool vaapi = false;
    while ((type = av_hwdevice_iterate_types(type)) != AV_HWDEVICE_TYPE_NONE) if (type == AV_HWDEVICE_TYPE_VAAPI) vaapi = true;
    if (!vaapi) { renderNode.clear(); FfuVkSetStatus(201); return false; }
    return true;
}
FFU_EXPORT AVBufferRef* FFU_CALL ffu_vulkan_acquire_decode_device(int, int) {
    std::lock_guard<std::mutex> lock(pathMutex);
    if (renderNode.empty() || !FfuVkCurrent()) return nullptr;
    AVBufferRef* device = nullptr;
    int result = av_hwdevice_ctx_create(&device, AV_HWDEVICE_TYPE_VAAPI, renderNode.c_str(), nullptr, 0);
    if (result < 0) { FfuVkSetStatus(202); return nullptr; }
    return device;
}
FFU_EXPORT int FFU_CALL ffu_vulkan_map_frame(const AVFrame* frame, AVFrame** output) {
    if (!output) return -203;
    *output = nullptr;
    if (!frame || frame->format != AV_PIX_FMT_VAAPI || !frame->hw_frames_ctx || frame->width <= 0 || frame->height <= 0) return -203;
    auto* context = reinterpret_cast<AVHWFramesContext*>(frame->hw_frames_ctx->data);
    if (context->sw_format != AV_PIX_FMT_NV12 || frame->colorspace == AVCOL_SPC_BT2020_NCL || frame->colorspace == AVCOL_SPC_BT2020_CL ||
        frame->color_trc == AVCOL_TRC_SMPTE2084 || frame->color_trc == AVCOL_TRC_ARIB_STD_B67 || frame->crop_left || frame->crop_top) return -204;
    AVFrame* mapped = av_frame_alloc(); if (!mapped) return AVERROR(ENOMEM);
    mapped->format = AV_PIX_FMT_DRM_PRIME;
    // READ synchronizes the VA decoder; DIRECT forbids copying to CPU storage.
    // av_hwframe_map retains the original surface until this frame is released.
    int result = av_hwframe_map(mapped, frame, AV_HWFRAME_MAP_READ | AV_HWFRAME_MAP_DIRECT);
    if (result >= 0) result = av_frame_copy_props(mapped, frame);
    if (result < 0) { av_frame_free(&mapped); return result; }
    *output = mapped; return 0;
}
FFU_EXPORT void* FFU_CALL ffu_vulkan_prepare(void* presenter, const AVFrame* frame, void* target) {
    auto c = FfuVkCurrent();
    if (!c || !frame || frame->format != AV_PIX_FMT_DRM_PRIME || !frame->data[0] || frame->crop_left || frame->crop_top) {
        FfuVkPresenterSetError(presenter, 203); return nullptr;
    }
    const auto& drm = *reinterpret_cast<const AVDRMFrameDescriptor*>(frame->data[0]);
    if (drm.nb_objects < 1 || drm.nb_objects > AV_DRM_MAX_PLANES || drm.nb_layers != 2 ||
        drm.layers[0].format != Fourcc('R','8',' ',' ') || drm.layers[1].format != Fourcc('G','R','8','8')) {
        FfuVkPresenterSetError(presenter, 204); return nullptr;
    }
    std::unique_lock<std::recursive_mutex> lock(c->resources);
    if (!c->active) return nullptr;
    auto owner = std::make_shared<ImportedPlanes>(); owner->context = c; owner->mapped = av_frame_clone(frame);
    if (!owner->mapped) { FfuVkPresenterSetError(presenter, AVERROR(ENOMEM)); return nullptr; }
    int error = 0;
    if (!ImportPlane(*owner, drm, 0, frame->width, frame->height, error) ||
        !ImportPlane(*owner, drm, 1, (frame->width + 1) / 2, (frame->height + 1) / 2, error)) {
        FfuVkPresenterSetError(presenter, error); return nullptr;
    }
    auto createSampler = reinterpret_cast<PFN_vkCreateSampler>(c->Proc("vkCreateSampler"));
    if (!createSampler) { FfuVkPresenterSetError(presenter, 205); return nullptr; }
    VkSamplerCreateInfo sampler{}; sampler.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
    // Nearest does not require a modifier-specific linear-filter feature. The
    // following Unity conversion/output draw performs normal display scaling.
    sampler.magFilter = sampler.minFilter = VK_FILTER_NEAREST; sampler.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
    sampler.addressModeU = sampler.addressModeV = sampler.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
    VkResult result = createSampler(c->instance.device, &sampler, nullptr, &owner->sampler);
    if (result != VK_SUCCESS) { FfuVkPresenterSetError(presenter, result); return nullptr; }
    FfuVkSample sample{}; sample.context = c; sample.width = frame->width; sample.height = frame->height; sample.planeCount = 2; sample.owner = owner;
    for (int i = 0; i < 2; ++i) { sample.image[i] = owner->images[i]; sample.view[i] = owner->views[i]; sample.sampler[i] = owner->sampler; }
    lock.unlock();
    return FfuVkPrepare(presenter, sample, target, frame->color_range == AVCOL_RANGE_JPEG,
        frame->colorspace == AVCOL_SPC_BT709 || (frame->colorspace == AVCOL_SPC_UNSPECIFIED && frame->height >= 720));
}
