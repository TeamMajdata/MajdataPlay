// Native Apple integration test, independent of a Unity license. CPU reads below
// validate completed GPU output; the production bridge never reads these pixels.
#import <Metal/Metal.h>
#import <CoreVideo/CoreVideo.h>
#include <TargetConditionals.h>
#include "../Bridge.h"
#include "IUnityGraphicsMetal.h"
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <vector>
extern "C" {
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/hwcontext.h>
#include <libswscale/swscale.h>
}

static int checks = 0, released = 0;
static id<MTLDevice> device;
static id<MTLCommandQueue> queue;
static id<MTLCommandBuffer> current;
static id<MTLComputePipelineState> pipeline;
static IUnityGraphics graphics{};
static IUnityGraphicsMetalV2 metal{};
static IUnityInterfaces interfaces{};
#if TARGET_OS_IPHONE
extern "C" void ffu_register_ios();
extern "C" void UnityRegisterPlugin(void (*load)(IUnityInterfaces*), void (*)()) { load(&interfaces); }
#endif
#define CHECK(value, message) do { ++checks; if (!(value)) { std::fprintf(stderr, "FAIL: %s\n", message); return false; } } while (0)
static UnityGfxRenderer Renderer() { return kUnityGfxRendererMetal; }
static void Register(IUnityGraphicsDeviceEventCallback) {}
static int Reserve(int) { return 100; }
static id<MTLDevice> Device() { return device; }
static id<MTLCommandQueue> Queue() { return queue; }
static id<MTLCommandBuffer> Current() { return current; }
static void EndEncoder() {}
static id<MTLCommandBuffer> Commit() {
    if (current.status == MTLCommandBufferStatusNotEnqueued || current.status == MTLCommandBufferStatusEnqueued) [current commit];
    return current;
}
static IUnityInterface* InterfaceSplit(unsigned long long high, unsigned long long low) {
    UnityInterfaceGUID id(high, low);
    if (id == GetUnityInterfaceGUID<IUnityGraphics>()) return &graphics;
    if (id == GetUnityInterfaceGUID<IUnityGraphicsMetalV2>()) return &metal;
    return nullptr;
}
static void FreePixelBuffer(void*, uint8_t* value) { CVPixelBufferRelease(reinterpret_cast<CVPixelBufferRef>(value)); ++released; }
static bool Initialize() {
    device = MTLCreateSystemDefaultDevice();
    CHECK(device != nil, "physical Metal device available");
    queue = [device newCommandQueue];
    graphics.GetRenderer = Renderer;
    graphics.RegisterDeviceEventCallback = Register;
    graphics.UnregisterDeviceEventCallback = Register;
    graphics.ReserveEventIDRange = Reserve;
    metal.MetalDevice = Device; metal.CommandQueue = Queue;
    metal.CurrentCommandBuffer = Current; metal.CommitCurrentCommandBuffer = Commit;
    metal.EndCurrentCommandEncoder = EndEncoder;
    interfaces.GetInterfaceSplit = InterfaceSplit;
#if TARGET_OS_IPHONE
    ffu_register_ios();
#else
    UnityPluginLoad(&interfaces);
#endif
    CHECK((ffu_capabilities() & FfuMetalPlaneZeroCopy) != 0, "bridge advertises real Metal capability");
    NSString* source = @"#include <metal_stdlib>\nusing namespace metal;\n"
        "kernel void convert(texture2d<float,access::read> y [[texture(0)]],"
        "texture2d<float,access::read> uv [[texture(1)]],texture2d<float,access::write> out [[texture(2)]],"
        "constant float2& mode [[buffer(0)]],uint2 p [[thread_position_in_grid]]) {"
        "if(p.x>=out.get_width()||p.y>=out.get_height())return;"
        "float l=y.read(p).r; float2 c=uv.read(p/2).rg-float2(0.5);"
        "if(mode.x<0.5){l=(l-16.0/255.0)*(255.0/219.0);c*=255.0/224.0;}"
        "float3 rgb=mode.y>0.5?float3(l+1.5748*c.y,l-0.187324*c.x-0.468124*c.y,l+1.8556*c.x):"
        "float3(l+1.402*c.y,l-0.344136*c.x-0.714136*c.y,l+1.772*c.x);out.write(float4(rgb,1),p);}";
    NSError* error = nil;
    id<MTLLibrary> library = [device newLibraryWithSource:source options:nil error:&error];
    if (!library) std::fprintf(stderr, "%s\n", error.description.UTF8String);
    CHECK(library != nil, "compile real Metal NV12 conversion shader");
    pipeline = [device newComputePipelineStateWithFunction:[library newFunctionWithName:@"convert"] error:&error];
    CHECK(pipeline != nil, "create GPU shader pipeline");
    std::printf("Metal device: %s\n", device.name.UTF8String);
    return true;
}
static bool Render(AVFrame* frame, bool lifetimePattern, int& minimum, int& maximum) {
    FfuMetalPlanes planes{};
    CHECK(ffu_metal_prepare(frame, &planes) != 0, "bridge creates NV12 plane textures from retained CVPixelBuffer");
    CHECK(planes.luma && planes.chroma && planes.packet, "plane handles and retained packet exist");
    const int width = frame->width, height = frame->height;
    av_frame_unref(frame);
    if (lifetimePattern) CHECK(released == 0, "input frame survives decoder reference release before GPU sampling");
    auto luma = (__bridge id<MTLTexture>)planes.luma;
    auto chroma = (__bridge id<MTLTexture>)planes.chroma;
    MTLTextureDescriptor* descriptor = [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatRGBA8Unorm
        width:width height:height mipmapped:NO];
    descriptor.storageMode = MTLStorageModeShared;
    descriptor.usage = MTLTextureUsageShaderWrite;
    id<MTLTexture> output = [device newTextureWithDescriptor:descriptor];
    CHECK(output != nil, "allocate shader output");
    current = [queue commandBuffer];
    id<MTLComputeCommandEncoder> encoder = [current computeCommandEncoder];
    [encoder setComputePipelineState:pipeline];
    [encoder setTexture:luma atIndex:0]; [encoder setTexture:chroma atIndex:1]; [encoder setTexture:output atIndex:2];
    float mode[2] = {float(planes.fullRange), float(planes.matrix709)};
    [encoder setBytes:mode length:sizeof(mode) atIndex:0];
    [encoder dispatchThreads:MTLSizeMake(width,height,1) threadsPerThreadgroup:MTLSizeMake(16,16,1)];
    [encoder endEncoding];
    ffu_render_callback()(ffu_event_id(FfuCompleteMetal), planes.packet);
    if (lifetimePattern) CHECK(released == 0, "render event does not recycle uncommitted decoder frame");
    [current commit]; [current waitUntilCompleted];
    CHECK(current.status == MTLCommandBufferStatusCompleted, "GPU sampling completed without an error");
    std::vector<uint8_t> pixels(size_t(width) * height * 4);
    [output getBytes:pixels.data() bytesPerRow:width*4 fromRegion:MTLRegionMake2D(0,0,width,height) mipmapLevel:0];
    for (size_t i = 0; i < pixels.size(); i += 4) {
        int value = (int(pixels[i]) + pixels[i+1] + pixels[i+2]) / 3;
        minimum = std::min(minimum, value); maximum = std::max(maximum, value);
    }
    ffu_render_callback()(ffu_event_id(FfuDrain), nullptr);
    if (lifetimePattern) {
        CHECK(released == 1, "frame retires only after actual Metal command completion");
        CHECK(pixels[(width*8+8)*4] >= 240 && pixels[(width*24+8)*4] <= 15, "NV12 video-range white/black shader conversion");
    }
    return true;
}
static bool Synthetic() {
    NSDictionary* attributes = @{(__bridge NSString*)kCVPixelBufferMetalCompatibilityKey:@YES,
        (__bridge NSString*)kCVPixelBufferIOSurfacePropertiesKey:@{}};
    CVPixelBufferRef buffer = nullptr;
    CHECK(CVPixelBufferCreate(kCFAllocatorDefault, 32, 32, kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange,
        (__bridge CFDictionaryRef)attributes, &buffer) == kCVReturnSuccess, "allocate IOSurface-backed NV12 buffer");
    CVPixelBufferLockBaseAddress(buffer, 0);
    for (int y=0; y<32; ++y) std::memset(static_cast<uint8_t*>(CVPixelBufferGetBaseAddressOfPlane(buffer,0))+
        y*CVPixelBufferGetBytesPerRowOfPlane(buffer,0), y<16?235:16, 32);
    for (int y=0; y<16; ++y) std::memset(static_cast<uint8_t*>(CVPixelBufferGetBaseAddressOfPlane(buffer,1))+
        y*CVPixelBufferGetBytesPerRowOfPlane(buffer,1), 128, 32);
    CVPixelBufferUnlockBaseAddress(buffer, 0);
    AVFrame* frame = av_frame_alloc();
    frame->width=32; frame->height=32; frame->format=AV_PIX_FMT_VIDEOTOOLBOX;
    frame->colorspace=AVCOL_SPC_BT709; frame->color_range=AVCOL_RANGE_MPEG;
    frame->data[3]=reinterpret_cast<uint8_t*>(buffer);
    frame->buf[0]=av_buffer_create(reinterpret_cast<uint8_t*>(buffer),1,FreePixelBuffer,nullptr,0);
    int minimum=255, maximum=0;
    bool ok=Render(frame,true,minimum,maximum);
    av_frame_free(&frame);
    return ok;
}
static AVPixelFormat HardwareFormat(AVCodecContext*, const AVPixelFormat* formats) {
    for (; *formats != AV_PIX_FMT_NONE; ++formats) if (*formats == AV_PIX_FMT_VIDEOTOOLBOX) return *formats;
    return AV_PIX_FMT_NONE;
}
static bool Decode(const char* path, bool hardware = true) {
    AVFormatContext* input=nullptr;
    CHECK(avformat_open_input(&input,path,nullptr,nullptr)>=0 && avformat_find_stream_info(input,nullptr)>=0, "open real media");
    const AVCodec* codec=nullptr;
    int stream=av_find_best_stream(input,AVMEDIA_TYPE_VIDEO,-1,-1,&codec,0);
    CHECK(stream>=0 && codec, "find real video codec");
    AVCodecContext* decoder=avcodec_alloc_context3(codec);
    CHECK(avcodec_parameters_to_context(decoder,input->streams[stream]->codecpar)>=0, "configure real decoder");
    if (hardware) {
        CHECK(av_hwdevice_ctx_create(&decoder->hw_device_ctx,AV_HWDEVICE_TYPE_VIDEOTOOLBOX,nullptr,nullptr,0)>=0, "create VideoToolbox hardware device");
        decoder->get_format=HardwareFormat;
    }
    CHECK(avcodec_open2(decoder,codec,nullptr)>=0, "open video decoder");
    AVFrame* frame=av_frame_alloc(); AVPacket* packet=av_packet_alloc();
    int frames=0, minimum=255, maximum=0;
    while(frames<30) {
        int result=avcodec_receive_frame(decoder,frame);
        if(result>=0) {
            if (hardware) {
                CHECK(frame->format==AV_PIX_FMT_VIDEOTOOLBOX && frame->data[3], "decoder emitted a native VideoToolbox frame");
                if(!Render(frame,false,minimum,maximum)) return false;
            } else {
                uint8_t pixels[32*32*4]; uint8_t* output[4]={pixels,nullptr,nullptr,nullptr}; int strides[4]={128,0,0,0};
                SwsContext* scaler=sws_getContext(frame->width,frame->height,static_cast<AVPixelFormat>(frame->format),
                    32,32,AV_PIX_FMT_RGBA,SWS_BILINEAR,nullptr,nullptr,nullptr);
                CHECK(scaler && sws_scale(scaler,frame->data,frame->linesize,0,frame->height,output,strides)==32, "simulator software fallback decodes visible RGBA");
                sws_freeContext(scaler);
                for(size_t p=0;p<sizeof(pixels);p+=4) { int v=(pixels[p]+pixels[p+1]+pixels[p+2])/3; minimum=std::min(minimum,v); maximum=std::max(maximum,v); }
                av_frame_unref(frame);
            }
            ++frames; continue;
        }
        if(result==AVERROR_EOF) break;
        CHECK(result==AVERROR(EAGAIN), "hardware decoder normal backpressure");
        do { result=av_read_frame(input,packet); if(result<0 || packet->stream_index==stream) break; av_packet_unref(packet); } while(true);
        if(result<0) { CHECK(avcodec_send_packet(decoder,nullptr)>=0, "drain hardware decoder"); }
        else {
            int sent=avcodec_send_packet(decoder,packet);
#if TARGET_OS_SIMULATOR
            if(sent<0 && hardware && frames==0) {
                std::puts("SKIP: simulator VideoToolbox hardware decode unavailable; checking actual software fallback instead.");
                av_frame_free(&frame); av_packet_free(&packet); avcodec_free_context(&decoder); avformat_close_input(&input);
                return Decode(path,false);
            }
#endif
            CHECK(sent>=0, "submit real encoded packet"); av_packet_unref(packet);
        }
    }
    CHECK(frames>=10 && maximum-minimum>15, "real decoded Metal output is visible and nonuniform");
    std::printf("%s: codec=%s frames=%d output range=%d..%d\n",hardware?"VideoToolbox":"Software fallback",codec->name,frames,minimum,maximum);
    av_frame_free(&frame); av_packet_free(&packet); avcodec_free_context(&decoder); avformat_close_input(&input);
    return true;
}
int main(int argc,char** argv) {
    @autoreleasepool {
        if(argc!=2) { std::fprintf(stderr,"Usage: MetalSmoke video.mp4\n"); return 2; }
        bool success=Initialize() && Synthetic() && Decode(argv[1]);
        UnityPluginUnload();
        if(success) std::printf("PASS: Metal NV12 texture sampling, GPU frame lifetime and real video decode%s; %d checks\n",
            TARGET_OS_SIMULATOR?" (simulator permits an explicit software decoder fallback)":" through VideoToolbox",checks);
        return success?0:1;
    }
}
