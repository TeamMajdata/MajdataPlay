#include "VulkanPortable.h"
#include "VulkanVideoDecode.h"
#include "VideoConvertSpirv.h"
#include <algorithm>
#include <chrono>
#include <cstring>
#include <new>
#include <thread>
#include <vector>

int FfuEventId(int event);
namespace {
constexpr int SubmitEvent = 11;
std::atomic<int> status{100}, inFlight{0};
std::atomic<bool> extensions{false};
PFN_vkGetInstanceProcAddr loader = nullptr;
VkInstance loaderInstance = VK_NULL_HANDLE;
PFN_vkCreateDevice originalCreateDevice = nullptr;
PFN_vkEnumerateDeviceExtensionProperties enumerateExtensions = nullptr;
PFN_vkGetPhysicalDeviceFeatures2 getFeatures = nullptr;
PFN_vkGetPhysicalDeviceProperties getProperties = nullptr;
const char* const required[] = {
    VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME, VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME,
    VK_KHR_GET_MEMORY_REQUIREMENTS_2_EXTENSION_NAME, VK_KHR_BIND_MEMORY_2_EXTENSION_NAME,
    VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME,
#ifdef __ANDROID__
    VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME,
    VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_KHR_MAINTENANCE1_EXTENSION_NAME,
    VK_KHR_EXTERNAL_SEMAPHORE_EXTENSION_NAME, VK_KHR_EXTERNAL_SEMAPHORE_FD_EXTENSION_NAME,
#else
    VK_KHR_EXTERNAL_MEMORY_FD_EXTENSION_NAME, VK_EXT_EXTERNAL_MEMORY_DMA_BUF_EXTENSION_NAME,
    VK_EXT_IMAGE_DRM_FORMAT_MODIFIER_EXTENSION_NAME, VK_EXT_PHYSICAL_DEVICE_DRM_EXTENSION_NAME,
    VK_KHR_IMAGE_FORMAT_LIST_EXTENSION_NAME,
#endif
};
VKAPI_ATTR VkResult VKAPI_CALL CreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* original,
    const VkAllocationCallbacks* allocation, VkDevice* device) {
    extensions = false;
    uint32_t count = 0;
    if (!enumerateExtensions || enumerateExtensions(physical, nullptr, &count, nullptr) != VK_SUCCESS)
        return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
    std::vector<VkExtensionProperties> available(count);
    if (enumerateExtensions(physical, nullptr, &count, available.data()) != VK_SUCCESS)
        return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
    VkPhysicalDeviceProperties properties{};
    if (getProperties) getProperties(physical, &properties);
    auto availableName = [&available](const char* name) {
        return std::any_of(available.begin(), available.end(), [name](const VkExtensionProperties& e) { return !std::strcmp(e.extensionName, name); });
    };
    auto promoted = [&properties](const char* name) {
        if (properties.apiVersion >= VK_API_VERSION_1_2 && !std::strcmp(name, VK_KHR_IMAGE_FORMAT_LIST_EXTENSION_NAME)) return true;
        if (properties.apiVersion < VK_API_VERSION_1_1) return false;
        const char* const core11[] = {VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME, VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME,
            VK_KHR_GET_MEMORY_REQUIREMENTS_2_EXTENSION_NAME, VK_KHR_BIND_MEMORY_2_EXTENSION_NAME,
            VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME, VK_KHR_MAINTENANCE1_EXTENSION_NAME, VK_KHR_EXTERNAL_SEMAPHORE_EXTENSION_NAME};
        for (const char* core : core11) if (!std::strcmp(name, core)) return true;
        return false;
    };
#ifdef _WIN32
    // Windows' D3D11 importer negotiates its own external-memory extensions.
    // This shared compute renderer needs no Linux/Android import extensions.
    return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
#endif
    for (const char* name : required)
        if (!availableName(name) && !promoted(name))
            return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
    std::vector<const char*> names;
    if (original->enabledExtensionCount) names.assign(original->ppEnabledExtensionNames, original->ppEnabledExtensionNames + original->enabledExtensionCount);
    for (const char* name : required)
        if (availableName(name) && std::none_of(names.begin(), names.end(), [name](const char* e) { return !std::strcmp(e, name); })) names.push_back(name);
    VkDeviceCreateInfo info = *original;
    info.enabledExtensionCount = static_cast<uint32_t>(names.size()); info.ppEnabledExtensionNames = names.data();
#ifdef __ANDROID__
    VkPhysicalDeviceSamplerYcbcrConversionFeatures ycbcr{};
    ycbcr.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES;
    VkPhysicalDeviceFeatures2 features{}; features.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2; features.pNext = &ycbcr;
    if (!getFeatures) return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
    getFeatures(physical, &features);
    if (!ycbcr.samplerYcbcrConversion) return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
    bool alreadyPresent = false;
    for (auto* next = static_cast<const VkBaseInStructure*>(original->pNext); next; next = next->pNext) {
        if (next->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES) {
            alreadyPresent = true;
            if (!reinterpret_cast<const VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(next)->samplerYcbcrConversion)
                return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
        }
        if (next->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES) {
            alreadyPresent = true;
            if (!reinterpret_cast<const VkPhysicalDeviceVulkan11Features*>(next)->samplerYcbcrConversion)
                return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
        }
    }
    // Do not mutate Unity's feature chain or add a duplicate promoted feature.
    if (!alreadyPresent) { ycbcr.pNext = const_cast<void*>(info.pNext); info.pNext = &ycbcr; }
#endif
    VkResult result = FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, &info, allocation, device);
    extensions = result == VK_SUCCESS;
    if (result != VK_SUCCESS) return FfuVulkanVideoCreateDevice(originalCreateDevice, loader, loaderInstance, physical, original, allocation, device);
    return result;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL GetProc(VkInstance instance, const char* name) {
    auto function = FfuVulkanVideoInstanceProc(loader, instance, name);
    if (!std::strcmp(name, "vkCreateDevice") && function) {
        originalCreateDevice = reinterpret_cast<PFN_vkCreateDevice>(function);
        loaderInstance = instance;
        enumerateExtensions = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(loader(instance, "vkEnumerateDeviceExtensionProperties"));
        getProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(loader(instance, "vkGetPhysicalDeviceProperties"));
        getFeatures = reinterpret_cast<PFN_vkGetPhysicalDeviceFeatures2>(loader(instance, "vkGetPhysicalDeviceFeatures2"));
        if (!getFeatures) getFeatures = reinterpret_cast<PFN_vkGetPhysicalDeviceFeatures2>(loader(instance, "vkGetPhysicalDeviceFeatures2KHR"));
        return reinterpret_cast<PFN_vkVoidFunction>(CreateDevice);
    }
    return function;
}
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API Initialize(PFN_vkGetInstanceProcAddr value, void*) { loader = value; extensions = false; return GetProc; }
template<class T> T* Interface(IUnityInterfaces* interfaces) {
    const auto id = GetUnityInterfaceGUID<T>();
    return static_cast<T*>(interfaces->GetInterfaceSplit(id.m_GUIDHigh, id.m_GUIDLow));
}
#define DEVICE_FUNCTIONS(X) \
    X(CreateImage) X(DestroyImage) X(GetImageMemoryRequirements) X(AllocateMemory) X(FreeMemory) X(BindImageMemory) \
    X(CreateImageView) X(DestroyImageView) X(CreateShaderModule) X(DestroyShaderModule) X(CreateDescriptorSetLayout) \
    X(DestroyDescriptorSetLayout) X(CreatePipelineLayout) X(DestroyPipelineLayout) X(CreateComputePipelines) X(DestroyPipeline) \
    X(CreateDescriptorPool) X(DestroyDescriptorPool) X(AllocateDescriptorSets) X(UpdateDescriptorSets) \
    X(CreateCommandPool) X(DestroyCommandPool) X(AllocateCommandBuffers) X(BeginCommandBuffer) X(EndCommandBuffer) \
    X(CmdPipelineBarrier) X(CmdBindPipeline) X(CmdBindDescriptorSets) X(CmdPushConstants) X(CmdDispatch) X(CmdCopyImage) \
    X(CreateFence) X(DestroyFence) X(GetFenceStatus) X(QueueSubmit) X(CreatePipelineCache) X(DestroyPipelineCache)
struct Context final : FfuVkContext {
#define DECLARE(name) PFN_vk##name name = nullptr;
    DEVICE_FUNCTIONS(DECLARE)
#undef DECLARE
    VkShaderModule shader = VK_NULL_HANDLE;
    VkPipelineCache pipelineCache = VK_NULL_HANDLE;
    bool Load() {
        getDeviceProcAddr = reinterpret_cast<PFN_vkGetDeviceProcAddr>(InstanceProc("vkGetDeviceProcAddr"));
        if (!getDeviceProcAddr) return false;
#define LOAD(name) name = reinterpret_cast<PFN_vk##name>(Proc("vk" #name)); if (!name) return false;
        DEVICE_FUNCTIONS(LOAD)
#undef LOAD
        auto memoryProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(InstanceProc("vkGetPhysicalDeviceMemoryProperties"));
        auto queueProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(InstanceProc("vkGetPhysicalDeviceQueueFamilyProperties"));
        if (!memoryProperties || !queueProperties) return false;
        memoryProperties(instance.physicalDevice, &memory);
        uint32_t count = 0; queueProperties(instance.physicalDevice, &count, nullptr);
        std::vector<VkQueueFamilyProperties> queues(count); queueProperties(instance.physicalDevice, &count, queues.data());
        if (instance.queueFamilyIndex >= count || !(queues[instance.queueFamilyIndex].queueFlags & VK_QUEUE_COMPUTE_BIT)) return false;
        VkShaderModuleCreateInfo create{}; create.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        create.codeSize = sizeof(FfuVideoConvertSpirv); create.pCode = FfuVideoConvertSpirv;
        if (CreateShaderModule(instance.device, &create, nullptr, &shader) != VK_SUCCESS) return false;
        VkPipelineCacheCreateInfo cache{}; cache.sType = VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO;
        if (CreatePipelineCache(instance.device, &cache, nullptr, &pipelineCache) != VK_SUCCESS) {
            DestroyShaderModule(instance.device, shader, nullptr); shader = VK_NULL_HANDLE; return false;
        }
        return true;
    }
};
std::mutex contextMutex;
std::shared_ptr<Context> current;
struct Presenter { std::atomic<int> references{1}, error{0}; void Retain() { ++references; } void Drop() { if (--references == 0) delete this; } };
struct PortableJob {
    std::shared_ptr<Context> context;
    Presenter* presenter = nullptr;
    FfuVkSample sample;
    void* unityTexture = nullptr;
    UnityVulkanImage target{};
    VkImage rgba = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    VkImageView rgbaView = VK_NULL_HANDLE;
    VkDescriptorSetLayout descriptors = VK_NULL_HANDLE;
    VkDescriptorPool descriptorPool = VK_NULL_HANDLE;
    VkPipelineLayout layout = VK_NULL_HANDLE;
    VkPipeline pipeline = VK_NULL_HANDLE;
    VkCommandPool commandPool = VK_NULL_HANDLE;
    VkFence fence = VK_NULL_HANDLE;
    bool submitted = false;
    struct { float uv[4]; int info[4]; } parameters{};
    ~PortableJob() {
        auto& c = *context;
        std::lock_guard<std::recursive_mutex> lock(c.resources);
        if (c.active) {
            auto device = c.instance.device;
            if (fence) c.DestroyFence(device, fence, nullptr);
            if (commandPool) c.DestroyCommandPool(device, commandPool, nullptr);
            if (pipeline) c.DestroyPipeline(device, pipeline, nullptr);
            if (layout) c.DestroyPipelineLayout(device, layout, nullptr);
            if (descriptorPool) c.DestroyDescriptorPool(device, descriptorPool, nullptr);
            if (descriptors) c.DestroyDescriptorSetLayout(device, descriptors, nullptr);
            if (rgbaView) c.DestroyImageView(device, rgbaView, nullptr);
            if (rgba) c.DestroyImage(device, rgba, nullptr);
            if (memory) c.FreeMemory(device, memory, nullptr);
        }
        sample.owner.reset();
        if (presenter) presenter->Drop();
        --inFlight;
    }
};
std::mutex jobsMutex;
std::vector<PortableJob*> jobs;
VkResult Build(PortableJob& job, VkCommandBuffer& command) {
    auto& c = *job.context; auto device = c.instance.device;
#define TRY(expression) do { const VkResult r = (expression); if (r != VK_SUCCESS) return r; } while (0)
    VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    image.imageType = VK_IMAGE_TYPE_2D; image.format = VK_FORMAT_R8G8B8A8_UNORM;
    image.extent = {job.sample.width, job.sample.height, 1}; image.mipLevels = image.arrayLayers = 1;
    image.samples = VK_SAMPLE_COUNT_1_BIT; image.tiling = VK_IMAGE_TILING_OPTIMAL;
    image.usage = VK_IMAGE_USAGE_STORAGE_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    TRY(c.CreateImage(device, &image, nullptr, &job.rgba));
    VkMemoryRequirements requirements{}; c.GetImageMemoryRequirements(device, job.rgba, &requirements);
    const int type = c.MemoryType(requirements.memoryTypeBits); if (type < 0) return VK_ERROR_FEATURE_NOT_PRESENT;
    VkMemoryAllocateInfo memory{}; memory.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    memory.allocationSize = requirements.size; memory.memoryTypeIndex = static_cast<uint32_t>(type);
    TRY(c.AllocateMemory(device, &memory, nullptr, &job.memory)); TRY(c.BindImageMemory(device, job.rgba, job.memory, 0));
    VkImageViewCreateInfo view{}; view.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    view.image = job.rgba; view.viewType = VK_IMAGE_VIEW_TYPE_2D; view.format = image.format;
    view.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    TRY(c.CreateImageView(device, &view, nullptr, &job.rgbaView));
    const uint32_t second = job.sample.planeCount == 1 ? 0 : 1;
    VkDescriptorSetLayoutBinding bindings[3]{};
    for (int i = 0; i < 3; ++i) { bindings[i].binding = i; bindings[i].descriptorCount = 1; bindings[i].stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; }
    bindings[0].descriptorType = bindings[1].descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER;
    bindings[0].pImmutableSamplers = &job.sample.sampler[0]; bindings[1].pImmutableSamplers = &job.sample.sampler[second];
    bindings[2].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
    VkDescriptorSetLayoutCreateInfo descriptorInfo{}; descriptorInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    descriptorInfo.bindingCount = 3; descriptorInfo.pBindings = bindings;
    TRY(c.CreateDescriptorSetLayout(device, &descriptorInfo, nullptr, &job.descriptors));
    VkPushConstantRange push{}; push.stageFlags = VK_SHADER_STAGE_COMPUTE_BIT; push.size = sizeof(job.parameters);
    VkPipelineLayoutCreateInfo layout{}; layout.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    layout.setLayoutCount = 1; layout.pSetLayouts = &job.descriptors; layout.pushConstantRangeCount = 1; layout.pPushConstantRanges = &push;
    TRY(c.CreatePipelineLayout(device, &layout, nullptr, &job.layout));
    VkComputePipelineCreateInfo pipeline{}; pipeline.sType = VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO;
    pipeline.layout = job.layout; pipeline.stage.sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    pipeline.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT; pipeline.stage.module = c.shader; pipeline.stage.pName = "main";
    TRY(c.CreateComputePipelines(device, c.pipelineCache, 1, &pipeline, nullptr, &job.pipeline));
    VkDescriptorPoolSize sizes[2]{{VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, 2 * job.sample.combinedDescriptorCount}, {VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, 1}};
    VkDescriptorPoolCreateInfo pool{}; pool.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
    pool.maxSets = 1; pool.poolSizeCount = 2; pool.pPoolSizes = sizes;
    TRY(c.CreateDescriptorPool(device, &pool, nullptr, &job.descriptorPool));
    VkDescriptorSetAllocateInfo allocate{}; allocate.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    allocate.descriptorPool = job.descriptorPool; allocate.descriptorSetCount = 1; allocate.pSetLayouts = &job.descriptors;
    VkDescriptorSet descriptor = VK_NULL_HANDLE; TRY(c.AllocateDescriptorSets(device, &allocate, &descriptor));
    VkDescriptorImageInfo images[3]{{job.sample.sampler[0], job.sample.view[0], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL},
        {job.sample.sampler[second], job.sample.view[second], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL},
        {VK_NULL_HANDLE, job.rgbaView, VK_IMAGE_LAYOUT_GENERAL}};
    VkWriteDescriptorSet writes[3]{};
    for (int i = 0; i < 3; ++i) {
        writes[i].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET; writes[i].dstSet = descriptor;
        writes[i].dstBinding = i; writes[i].descriptorCount = 1; writes[i].descriptorType = bindings[i].descriptorType; writes[i].pImageInfo = &images[i];
    }
    c.UpdateDescriptorSets(device, 3, writes, 0, nullptr);
    VkCommandPoolCreateInfo commands{}; commands.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    commands.flags = VK_COMMAND_POOL_CREATE_TRANSIENT_BIT; commands.queueFamilyIndex = c.instance.queueFamilyIndex;
    TRY(c.CreateCommandPool(device, &commands, nullptr, &job.commandPool));
    VkCommandBufferAllocateInfo cb{}; cb.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    cb.commandPool = job.commandPool; cb.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY; cb.commandBufferCount = 1;
    TRY(c.AllocateCommandBuffers(device, &cb, &command));
    VkCommandBufferBeginInfo begin{}; begin.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO; begin.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    TRY(c.BeginCommandBuffer(command, &begin));
    VkImageMemoryBarrier barriers[3]{};
    for (uint32_t i = 0; i < job.sample.planeCount; ++i) {
        auto& b = barriers[i]; b.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        b.oldLayout = job.sample.initialLayout; b.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        b.srcQueueFamilyIndex = job.sample.foreignQueue; b.dstQueueFamilyIndex = c.instance.queueFamilyIndex;
        if (b.srcQueueFamilyIndex == VK_QUEUE_FAMILY_IGNORED) b.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        b.image = job.sample.image[i]; b.dstAccessMask = VK_ACCESS_SHADER_READ_BIT; b.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    }
    auto& output = barriers[job.sample.planeCount]; output.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    output.oldLayout = VK_IMAGE_LAYOUT_UNDEFINED; output.newLayout = VK_IMAGE_LAYOUT_GENERAL;
    output.srcQueueFamilyIndex = output.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    output.image = job.rgba; output.dstAccessMask = VK_ACCESS_SHADER_WRITE_BIT; output.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    c.CmdPipelineBarrier(command, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, 0, nullptr, 0, nullptr, job.sample.planeCount + 1, barriers);
    c.CmdBindPipeline(command, VK_PIPELINE_BIND_POINT_COMPUTE, job.pipeline);
    c.CmdBindDescriptorSets(command, VK_PIPELINE_BIND_POINT_COMPUTE, job.layout, 0, 1, &descriptor, 0, nullptr);
    c.CmdPushConstants(command, job.layout, VK_SHADER_STAGE_COMPUTE_BIT, 0, sizeof(job.parameters), &job.parameters);
    c.CmdDispatch(command, (job.sample.width + 7) / 8, (job.sample.height + 7) / 8, 1);
    output.oldLayout = VK_IMAGE_LAYOUT_GENERAL; output.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    output.srcAccessMask = VK_ACCESS_SHADER_WRITE_BIT; output.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
    c.CmdPipelineBarrier(command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &output);
    VkImageCopy copy{}; copy.srcSubresource = copy.dstSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1}; copy.extent = image.extent;
    c.CmdCopyImage(command, job.rgba, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, job.target.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
    for (uint32_t i = 0; i < job.sample.planeCount; ++i) {
        auto& b = barriers[i]; b.oldLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL; b.newLayout = job.sample.initialLayout;
        std::swap(b.srcQueueFamilyIndex, b.dstQueueFamilyIndex); b.srcAccessMask = VK_ACCESS_SHADER_READ_BIT; b.dstAccessMask = 0;
    }
    c.CmdPipelineBarrier(command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, 0, 0, nullptr, 0, nullptr, job.sample.planeCount, barriers);
    TRY(c.EndCommandBuffer(command));
    VkFenceCreateInfo fence{}; fence.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    TRY(c.CreateFence(device, &fence, nullptr, &job.fence));
    return VK_SUCCESS;
#undef TRY
}
void UNITY_INTERFACE_API OnQueue(int, void* pointer) {
    auto* job = static_cast<PortableJob*>(pointer);
    const auto context = job->context;
    auto& c = *context;
    std::lock_guard<std::mutex> lock(jobsMutex);
    std::lock_guard<std::recursive_mutex> resources(c.resources);
    if (!c.active) { job->presenter->error = 104; delete job; return; }
    if (job->sample.lock && !job->sample.lock(job->sample)) { job->presenter->error = 416; delete job; return; }
    VkCommandBuffer command = VK_NULL_HANDLE;
    VkResult result = Build(*job, command);
    if (result == VK_SUCCESS) {
        VkSubmitInfo submit{}; submit.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO; submit.commandBufferCount = 1; submit.pCommandBuffers = &command;
        const VkPipelineStageFlags stage = VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
        if (job->sample.acquireSemaphore) { submit.waitSemaphoreCount = 1; submit.pWaitSemaphores = &job->sample.acquireSemaphore; submit.pWaitDstStageMask = &stage; }
        VkTimelineSemaphoreSubmitInfo timeline{VK_STRUCTURE_TYPE_TIMELINE_SEMAPHORE_SUBMIT_INFO};
        if (job->sample.timeline) {
            timeline.waitSemaphoreValueCount = timeline.signalSemaphoreValueCount = 1;
            timeline.pWaitSemaphoreValues = &job->sample.waitValue; timeline.pSignalSemaphoreValues = &job->sample.signalValue;
            submit.pNext = &timeline; submit.signalSemaphoreCount = 1; submit.pSignalSemaphores = &job->sample.acquireSemaphore;
        }
        result = c.QueueSubmit(c.instance.graphicsQueue, 1, &submit, job->fence);
    }
    if (job->sample.unlock) job->sample.unlock(result == VK_SUCCESS);
    if (result != VK_SUCCESS) { job->presenter->error = result; delete job; return; }
    job->submitted = true; jobs.push_back(job);
}
}
int FfuVkContext::MemoryType(uint32_t bits) const {
    for (uint32_t i = 0; i < memory.memoryTypeCount; ++i)
        if ((bits & (1u << i)) && (memory.memoryTypes[i].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT)) return static_cast<int>(i);
    for (uint32_t i = 0; i < memory.memoryTypeCount; ++i) if (bits & (1u << i)) return static_cast<int>(i);
    return -1;
}
void FfuVkPreload(IUnityInterfaces* interfaces) {
    auto* unity = Interface<IUnityGraphicsVulkanV2>(interfaces);
    if (unity) unity->InterceptInitialization(Initialize, nullptr);
}
bool FfuVkInitialize(IUnityInterfaces* interfaces) {
    std::lock_guard<std::mutex> guard(contextMutex);
    auto* unity = Interface<IUnityGraphicsVulkanV2>(interfaces);
    status = 100;
    if (!unity) return false;
    auto c = std::make_shared<Context>(); c->unity = unity; c->instance = unity->Instance();
    if (!c->instance.device || !c->instance.graphicsQueue || !c->instance.getInstanceProcAddr || !c->Load()) { status = 101; return false; }
    UnityVulkanPluginEventConfig config{};
    config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission | kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;
    unity->ConfigureEvent(FfuEventId(SubmitEvent), &config);
    current = std::move(c); status = 0; return true;
}
std::shared_ptr<FfuVkContext> FfuVkCurrent() { std::lock_guard<std::mutex> guard(contextMutex); return current; }
int FfuVkStatus() { return status.load(); }
bool FfuVkExternalImportsAvailable() { return extensions.load(); }
void FfuVkSetStatus(int value) { status = value; }
void* FfuVkPresenterCreate() { return FfuVkCurrent() ? new (std::nothrow) Presenter() : nullptr; }
void FfuVkPresenterRelease(void* presenter) { if (presenter) static_cast<Presenter*>(presenter)->Drop(); }
int FfuVkPresenterError(void* presenter) { return presenter ? static_cast<Presenter*>(presenter)->error.load() : status.load(); }
void FfuVkPresenterSetError(void* presenter, int error) { if (presenter) static_cast<Presenter*>(presenter)->error = error; }
void* FfuVkPrepare(void* presenter, const FfuVkSample& sample, void* target, bool full, bool matrix709) {
    std::shared_ptr<Context> c;
    { std::lock_guard<std::mutex> guard(contextMutex); c = current; }
    if (!c || !c->active || c.get() != sample.context.get() || !presenter || !target || !sample.owner || !sample.width || !sample.height ||
        sample.planeCount < 1 || sample.planeCount > 2 || !sample.image[0] || !sample.view[0] || !sample.sampler[0]) return nullptr;
    if (inFlight.fetch_add(1) >= 24) { --inFlight; FfuVkPresenterSetError(presenter, 102); return nullptr; }
    auto* job = new (std::nothrow) PortableJob();
    if (!job) { --inFlight; return nullptr; }
    job->context = c; job->presenter = static_cast<Presenter*>(presenter); job->presenter->Retain();
    job->sample = sample; job->unityTexture = target;
    std::copy(sample.uvScaleOffset, sample.uvScaleOffset + 4, job->parameters.uv);
    job->parameters.info[0] = sample.planeCount; job->parameters.info[1] = full; job->parameters.info[2] = matrix709;
    return job;
}
void FfuVkSubmit(void* pointer) {
    if (!pointer) return;
    FfuVkPoll(false);
    auto* job = static_cast<PortableJob*>(pointer); auto& c = *job->context;
    if (!c.active || !c.unity->AccessTexture(job->unityTexture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &job->target) ||
        job->target.format != VK_FORMAT_R8G8B8A8_UNORM || !(job->target.usage & VK_IMAGE_USAGE_TRANSFER_DST_BIT) ||
        job->target.extent.width != job->sample.width || job->target.extent.height != job->sample.height) {
        job->presenter->error = 103; delete job; return;
    }
    c.unity->AccessQueue(OnQueue, FfuEventId(SubmitEvent), job, true);
}
void FfuVkCancel(void* pointer) { delete static_cast<PortableJob*>(pointer); }
void FfuVkPoll(bool drain) {
    std::lock_guard<std::mutex> lock(jobsMutex);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
    for (auto it = jobs.begin(); it != jobs.end();) {
        auto* job = *it; const auto context = job->context; auto& c = *context;
        std::lock_guard<std::recursive_mutex> resources(c.resources);
        VkResult result = c.active ? c.GetFenceStatus(c.instance.device, job->fence) : VK_ERROR_DEVICE_LOST;
        while (drain && result == VK_NOT_READY && std::chrono::steady_clock::now() < deadline) {
            std::this_thread::sleep_for(std::chrono::milliseconds(1)); result = c.GetFenceStatus(c.instance.device, job->fence);
        }
        if (result == VK_SUCCESS || result == VK_ERROR_DEVICE_LOST) { delete job; it = jobs.erase(it); }
        else { if (result != VK_NOT_READY) job->presenter->error = result; ++it; }
    }
}
int FfuVkPendingCount() { return inFlight.load(); }
void FfuVkShutdown() {
    FfuVkPoll(true);
    std::lock_guard<std::mutex> guard(contextMutex);
    if (current) {
        std::lock_guard<std::recursive_mutex> lock(current->resources);
        if (current->shader) current->DestroyShaderModule(current->instance.device, current->shader, nullptr);
        if (current->pipelineCache) current->DestroyPipelineCache(current->instance.device, current->pipelineCache, nullptr);
        current->shader = VK_NULL_HANDLE; current->active = false;
    }
    current.reset(); status = 100;
}
