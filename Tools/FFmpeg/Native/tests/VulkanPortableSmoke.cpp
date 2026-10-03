// Real Vulkan compute/copy test for the shared Android/Linux queue layer.
// Sources are initialized with GPU clears; CPU mapping is test-only readback.
// Does not claim VAAPI/AHardwareBuffer hardware support on a software ICD.
#include <vulkan/vulkan.h>
#include "../VulkanPortable.h"
#include <cstdio>
#include <cstdlib>
#include <vector>
#include <cstring>
#define CHECK(call) do { VkResult r=(call); if(r!=VK_SUCCESS){ std::printf("FAIL %d Vulkan=%d\n",__LINE__,r); std::exit(1); } } while(0)
int FfuEventId(int event) { return event+240; }
static UnityVulkanInstance instance{};
static IUnityGraphicsVulkanV2 unity{};
static VkImage output;
static int released=0, submitted=0;
static UnityRenderingEventAndData deferred=nullptr;
static void* deferredData=nullptr;
static UnityVulkanInstance UNITY_INTERFACE_API Instance() { return instance; }
static void UNITY_INTERFACE_API Configure(int id,const UnityVulkanPluginEventConfig* config) {
    if(id!=251 || config->renderPassPrecondition!=kUnityVulkanRenderPass_EnsureOutside) std::exit(1);
}
static bool UNITY_INTERFACE_API Access(void* texture,const VkImageSubresource*,VkImageLayout layout,VkPipelineStageFlags,VkAccessFlags,UnityVulkanResourceAccessMode,UnityVulkanImage* value) {
    if(texture!=&output || layout!=VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL) return false;
    *value={}; value->image=output; value->format=VK_FORMAT_R8G8B8A8_UNORM; value->extent={32,16,1};
    value->usage=VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT; return true;
}
static void UNITY_INTERFACE_API Queue(UnityRenderingEventAndData callback,int,void* data,bool flush) {
    if(!flush || deferred) std::exit(1);
    deferred=callback; deferredData=data; ++submitted;
}
static IUnityInterface* UNITY_INTERFACE_API Lookup(unsigned long long high,unsigned long long low) {
    const auto id=GetUnityInterfaceGUID<IUnityGraphicsVulkanV2>();
    return id.m_GUIDHigh==high && id.m_GUIDLow==low ? &unity : nullptr;
}
struct Images {
    VkImage images[2]{}; VkImageView views[2]{}; VkDeviceMemory memories[2]{}; VkSampler sampler{};
    ~Images() { for(int i=0;i<2;++i) { if(views[i]) vkDestroyImageView(instance.device,views[i],nullptr); if(images[i])vkDestroyImage(instance.device,images[i],nullptr); if(memories[i])vkFreeMemory(instance.device,memories[i],nullptr); }
        if(sampler)vkDestroySampler(instance.device,sampler,nullptr);
        ++released; }
};
static uint32_t Memory(uint32_t bits,VkMemoryPropertyFlags flags=0) {
    VkPhysicalDeviceMemoryProperties memory{}; vkGetPhysicalDeviceMemoryProperties(instance.physicalDevice,&memory);
    for(uint32_t i=0;i<memory.memoryTypeCount;++i) if((bits&(1u<<i)) && (memory.memoryTypes[i].propertyFlags&flags)==flags) return i;
    std::exit(1);
}
static void MakeImage(VkFormat format,int width,int height,VkImageUsageFlags usage,VkImage& image,VkDeviceMemory& memory) {
    VkImageCreateInfo info{}; info.sType=VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO; info.imageType=VK_IMAGE_TYPE_2D;
    info.format=format; info.extent={uint32_t(width),uint32_t(height),1}; info.mipLevels=info.arrayLayers=1;
    info.samples=VK_SAMPLE_COUNT_1_BIT; info.tiling=VK_IMAGE_TILING_OPTIMAL; info.usage=usage;
    CHECK(vkCreateImage(instance.device,&info,nullptr,&image)); VkMemoryRequirements req{}; vkGetImageMemoryRequirements(instance.device,image,&req);
    VkMemoryAllocateInfo allocate{}; allocate.sType=VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO; allocate.allocationSize=req.size; allocate.memoryTypeIndex=Memory(req.memoryTypeBits);
    CHECK(vkAllocateMemory(instance.device,&allocate,nullptr,&memory)); CHECK(vkBindImageMemory(instance.device,image,memory,0));
}
static VkCommandPool pool;
static VkCommandBuffer Begin() {
    VkCommandBufferAllocateInfo allocate{}; allocate.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    allocate.commandPool=pool; allocate.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY; allocate.commandBufferCount=1; VkCommandBuffer command;
    CHECK(vkAllocateCommandBuffers(instance.device,&allocate,&command)); VkCommandBufferBeginInfo info{}; info.sType=VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    CHECK(vkBeginCommandBuffer(command,&info)); return command;
}
static void End(VkCommandBuffer command) {
    CHECK(vkEndCommandBuffer(command)); VkSubmitInfo submit{}; submit.sType=VK_STRUCTURE_TYPE_SUBMIT_INFO; submit.commandBufferCount=1; submit.pCommandBuffers=&command;
    CHECK(vkQueueSubmit(instance.graphicsQueue,1,&submit,VK_NULL_HANDLE)); CHECK(vkQueueWaitIdle(instance.graphicsQueue)); vkFreeCommandBuffers(instance.device,pool,1,&command);
}
static void Barrier(VkCommandBuffer command,VkImage image,VkImageLayout from,VkImageLayout to) {
    VkImageMemoryBarrier b{}; b.sType=VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER; b.oldLayout=from; b.newLayout=to;
    b.srcQueueFamilyIndex=b.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED; b.image=image; b.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
    b.srcAccessMask=from==VK_IMAGE_LAYOUT_UNDEFINED ? 0 : VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT;
    b.dstAccessMask=VK_ACCESS_MEMORY_READ_BIT|VK_ACCESS_MEMORY_WRITE_BIT;
    vkCmdPipelineBarrier(command,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,0,0,nullptr,0,nullptr,1,&b);
}
int main() {
    VkApplicationInfo app{}; app.sType=VK_STRUCTURE_TYPE_APPLICATION_INFO; app.apiVersion=VK_API_VERSION_1_2;
    VkInstanceCreateInfo create{}; create.sType=VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO; create.pApplicationInfo=&app;
    CHECK(vkCreateInstance(&create,nullptr,&instance.instance)); uint32_t count=0; CHECK(vkEnumeratePhysicalDevices(instance.instance,&count,nullptr));
    std::vector<VkPhysicalDevice> devices(count); CHECK(vkEnumeratePhysicalDevices(instance.instance,&count,devices.data())); if(!count)return 77;
    instance.physicalDevice=devices[0]; VkPhysicalDeviceProperties properties{}; vkGetPhysicalDeviceProperties(devices[0],&properties);
    std::printf("Vulkan compute device: %s\n",properties.deviceName);
    VkPhysicalDeviceTimelineSemaphoreFeatures timelineFeature{};timelineFeature.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_TIMELINE_SEMAPHORE_FEATURES;
    VkPhysicalDeviceFeatures2 featureQuery{};featureQuery.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;featureQuery.pNext=&timelineFeature;
    vkGetPhysicalDeviceFeatures2(devices[0],&featureQuery);if(!timelineFeature.timelineSemaphore)return 77;
    vkGetPhysicalDeviceQueueFamilyProperties(devices[0],&count,nullptr); std::vector<VkQueueFamilyProperties> queues(count); vkGetPhysicalDeviceQueueFamilyProperties(devices[0],&count,queues.data());
    for(uint32_t i=0;i<count;++i) if((queues[i].queueFlags&(VK_QUEUE_GRAPHICS_BIT|VK_QUEUE_COMPUTE_BIT))==(VK_QUEUE_GRAPHICS_BIT|VK_QUEUE_COMPUTE_BIT)){instance.queueFamilyIndex=i;break;}
    float priority=1; VkDeviceQueueCreateInfo q{};q.sType=VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;q.queueFamilyIndex=instance.queueFamilyIndex;q.queueCount=1;q.pQueuePriorities=&priority;
    VkDeviceCreateInfo dc{};dc.sType=VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;dc.pNext=&timelineFeature;dc.queueCreateInfoCount=1;dc.pQueueCreateInfos=&q;
    CHECK(vkCreateDevice(devices[0],&dc,nullptr,&instance.device));vkGetDeviceQueue(instance.device,instance.queueFamilyIndex,0,&instance.graphicsQueue);instance.getInstanceProcAddr=vkGetInstanceProcAddr;
    VkSemaphoreTypeCreateInfo timelineType{};timelineType.sType=VK_STRUCTURE_TYPE_SEMAPHORE_TYPE_CREATE_INFO;timelineType.semaphoreType=VK_SEMAPHORE_TYPE_TIMELINE;
    VkSemaphoreCreateInfo semaphoreInfo{};semaphoreInfo.sType=VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;semaphoreInfo.pNext=&timelineType;
    VkSemaphore timelineSemaphore;CHECK(vkCreateSemaphore(instance.device,&semaphoreInfo,nullptr,&timelineSemaphore));
    uint64_t timelineValue=0;int locks=0,unlocks=0;
    VkCommandPoolCreateInfo pc{};pc.sType=VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;pc.queueFamilyIndex=instance.queueFamilyIndex;CHECK(vkCreateCommandPool(instance.device,&pc,nullptr,&pool));
    unity.Instance=Instance;unity.ConfigureEvent=Configure;unity.AccessTexture=Access;unity.AccessQueue=Queue;
    IUnityInterfaces interfaces{};interfaces.GetInterfaceSplit=Lookup;
    if(!FfuVkInitialize(&interfaces)){std::printf("FAIL init %d\n",FfuVkStatus());return 1;}
    void* presenter=FfuVkPresenterCreate();VkDeviceMemory outputMemory;
    MakeImage(VK_FORMAT_R8G8B8A8_UNORM,32,16,VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT,output,outputMemory);
    auto command=Begin();Barrier(command,output,VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);End(command);
    constexpr size_t bytes=32*16*4;
    VkBuffer readback;VkDeviceMemory readMemory;VkBufferCreateInfo bi{};bi.sType=VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;bi.size=bytes;bi.usage=VK_BUFFER_USAGE_TRANSFER_DST_BIT;
    CHECK(vkCreateBuffer(instance.device,&bi,nullptr,&readback));VkMemoryRequirements req{};vkGetBufferMemoryRequirements(instance.device,readback,&req);
    VkMemoryAllocateInfo ma{};ma.sType=VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;ma.allocationSize=req.size;ma.memoryTypeIndex=Memory(req.memoryTypeBits,VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT|VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    CHECK(vkAllocateMemory(instance.device,&ma,nullptr,&readMemory));CHECK(vkBindBufferMemory(instance.device,readback,readMemory,0));
    for(int iteration=0;iteration<8;++iteration){
        bool nv12=(iteration%2)==0;auto owner=std::make_shared<Images>();FfuVkSample sample{};sample.width=32;sample.height=16;sample.planeCount=nv12?2:1;
        sample.context=FfuVkCurrent();sample.foreignQueue=VK_QUEUE_FAMILY_IGNORED;sample.owner=owner;
        if(!nv12) {
            sample.lock=[&](FfuVkSample& current){++locks;current.timeline=true;current.acquireSemaphore=timelineSemaphore;current.waitValue=timelineValue;current.signalValue=timelineValue+1;return true;};
            sample.unlock=[&](bool submitted){++unlocks;if(submitted)++timelineValue;};
        }
        VkSamplerCreateInfo si{};si.sType=VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;si.magFilter=si.minFilter=VK_FILTER_NEAREST;si.addressModeU=si.addressModeV=si.addressModeW=VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        CHECK(vkCreateSampler(instance.device,&si,nullptr,&owner->sampler));
        command=Begin();
        for(uint32_t i=0;i<sample.planeCount;++i){VkFormat format=nv12?(i?VK_FORMAT_R8G8_UNORM:VK_FORMAT_R8_UNORM):VK_FORMAT_R8G8B8A8_UNORM;
            MakeImage(format,i?16:32,i?8:16,VK_IMAGE_USAGE_SAMPLED_BIT|VK_IMAGE_USAGE_TRANSFER_DST_BIT,owner->images[i],owner->memories[i]);
            VkImageViewCreateInfo vi{};vi.sType=VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;vi.image=owner->images[i];vi.viewType=VK_IMAGE_VIEW_TYPE_2D;vi.format=format;vi.subresourceRange={VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};
            CHECK(vkCreateImageView(instance.device,&vi,nullptr,&owner->views[i]));Barrier(command,owner->images[i],VK_IMAGE_LAYOUT_UNDEFINED,VK_IMAGE_LAYOUT_GENERAL);
            VkClearColorValue color{};color.float32[0]=nv12?(i?128.f/255:64.f/255):51.f/255;color.float32[1]=nv12?128.f/255:102.f/255;color.float32[2]=153.f/255;color.float32[3]=1;
            vkCmdClearColorImage(command,owner->images[i],VK_IMAGE_LAYOUT_GENERAL,&color,1,&vi.subresourceRange);
            sample.image[i]=owner->images[i];sample.view[i]=owner->views[i];sample.sampler[i]=owner->sampler;}
        End(command);
        auto actualContext=sample.context; sample.context=std::make_shared<FfuVkContext>();
        if(FfuVkPrepare(presenter,sample,&output,true,true)) { std::puts("FAIL stale device generation accepted"); return 1; }
        sample.context=actualContext;
        void* cancelled=FfuVkPrepare(presenter,sample,&output,true,true); if(!cancelled)return 1;
        FfuVkCancel(cancelled);
        if(iteration==0) {
            void* limitPresenter=FfuVkPresenterCreate(); std::vector<void*> held;
            for(int n=0;n<24;++n) { void* p=FfuVkPrepare(limitPresenter,sample,&output,true,true); if(!p)return 1; held.push_back(p); }
            if(FfuVkPrepare(limitPresenter,sample,&output,true,true) || FfuVkPresenterError(limitPresenter)!=102)return 1;
            for(void* p:held) FfuVkCancel(p);
            FfuVkPresenterRelease(limitPresenter);
        }
        void* packet=FfuVkPrepare(presenter,sample,&output,true,true);sample.owner.reset();owner.reset();
        if(!packet || released!=iteration)return 1;
        FfuVkSubmit(packet);if(!deferred || released!=iteration)return 1;
        auto callback=deferred;deferred=nullptr;callback(251,deferredData);FfuVkPoll(true);
        uint64_t actualTimeline=0;CHECK(vkGetSemaphoreCounterValue(instance.device,timelineSemaphore,&actualTimeline));
        if(actualTimeline!=timelineValue || locks!=unlocks || locks!=(iteration+1)/2) { std::puts("FAIL frame lock/timeline protocol");return 1; }
        if(released!=iteration+1 || FfuVkPresenterError(presenter)){std::printf("FAIL lifetime/error %d %d\n",released,FfuVkPresenterError(presenter));return 1;}
        command=Begin();Barrier(command,output,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL);
        VkBufferImageCopy copy{};copy.imageSubresource={VK_IMAGE_ASPECT_COLOR_BIT,0,0,1};copy.imageExtent={32,16,1};
        vkCmdCopyImageToBuffer(command,output,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,readback,1,&copy);Barrier(command,output,VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL);End(command);
        unsigned char* mapped;CHECK(vkMapMemory(instance.device,readMemory,0,bytes,0,reinterpret_cast<void**>(&mapped)));
        for(size_t i=0;i<bytes;i+=4)for(int ch=0;ch<4;++ch){int expected=ch==3?255:nv12?64:(ch+1)*51;if(std::abs(int(mapped[i+ch])-expected)>1){std::printf("FAIL pixel %zu %d actual=%d expected=%d\n",i,ch,mapped[i+ch],expected);return 1;}}
        vkUnmapMemory(instance.device,readMemory);
    }
    FfuVkPresenterRelease(presenter);FfuVkShutdown();vkDestroySemaphore(instance.device,timelineSemaphore,nullptr);vkDestroyBuffer(instance.device,readback,nullptr);vkFreeMemory(instance.device,readMemory,nullptr);
    vkDestroyImage(instance.device,output,nullptr);vkFreeMemory(instance.device,outputMemory,nullptr);vkDestroyCommandPool(instance.device,pool,nullptr);vkDestroyDevice(instance.device,nullptr);vkDestroyInstance(instance.instance,nullptr);
    std::printf("PASS: shared Vulkan compute NV12/RGB, %d deferred submissions, %zu pixel bytes, %d locked timeline submissions, GPU-fence owner retirement, cancellation, 24-packet cap, stale-device rejection\n",submitted,bytes*8,locks);
}
