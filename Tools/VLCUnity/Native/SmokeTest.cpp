// Hardware smoke test with the bundled LibVLC and real D3D devices. This checks
// native decoding and surface sharing independently of the Unity editor. Unity
// command-buffer integration still requires the Unity validation harness.
#include "RenderingPlugin.cpp"
#include <cstdio>
#include <cstdlib>

namespace
{
UnityGfxRenderer testRenderer = kUnityGfxRendererD3D11;
ComPtr<ID3D11Device> test11;
ComPtr<ID3D12Device> test12;
ComPtr<ID3D12CommandQueue> testQueue;
IUnityGraphics testGraphics{};
IUnityGraphicsD3D11 testGraphics11{};
IUnityGraphicsD3D12v7 testGraphics12{};
IUnityInterface* UNITY_INTERFACE_API GetTestInterface(UnityInterfaceGUID guid)
{
    if (guid == GetUnityInterfaceGUID<IUnityGraphics>()) return &testGraphics;
    if (guid == GetUnityInterfaceGUID<IUnityGraphicsD3D11>() && testRenderer == kUnityGfxRendererD3D11) return &testGraphics11;
    if (guid == GetUnityInterfaceGUID<IUnityGraphicsD3D12v7>() && testRenderer == kUnityGfxRendererD3D12) return &testGraphics12;
    return nullptr;
}
UnityGfxRenderer UNITY_INTERFACE_API GetTestRenderer() { return testRenderer; }
void UNITY_INTERFACE_API RegisterTestEvent(IUnityGraphicsDeviceEventCallback) {}
ID3D11Device* UNITY_INTERFACE_API GetTestDevice11() { return test11.Get(); }
ID3D12Device* UNITY_INTERFACE_API GetTestDevice12() { return test12.Get(); }
ID3D12CommandQueue* UNITY_INTERFACE_API GetTestQueue() { return testQueue.Get(); }
void UNITY_INTERFACE_API ConfigureTestEvent(int, const UnityD3D12PluginEventConfig*) {}

template<class T> T Proc(HMODULE module, const char* name)
{
    auto proc = reinterpret_cast<T>(GetProcAddress(module, name));
    if (!proc) { std::fprintf(stderr, "Missing %s\n", name); std::exit(2); }
    return proc;
}

bool CheckPixels(Context& context)
{
    // Test-only GPU readback verifies that the external shared surface contains
    // decoded pixels; production RenderingPlugin.cpp has no staging textures.
    auto& surface = *context.current;
    if (context.backend == D3D11)
    {
        D3D11_TEXTURE2D_DESC desc{}; surface.consumer11->GetDesc(&desc);
        desc.Usage = D3D11_USAGE_STAGING; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        desc.BindFlags = desc.MiscFlags = 0;
        ComPtr<ID3D11Texture2D> staging;
        if (FAILED(test11->CreateTexture2D(&desc, nullptr, &staging))) return false;
        unity11Context->CopyResource(staging.Get(), surface.consumer11.Get());
        context.End();
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(unity11Context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped))) return false;
        bool varied = false;
        auto* pixels = static_cast<const unsigned char*>(mapped.pData);
        for (unsigned y = 0; y < desc.Height && !varied; y += 4)
            for (unsigned x = 0; x < desc.Width * 4; x += 16)
                if (pixels[y * mapped.RowPitch + x] != pixels[0]) { varied = true; break; }
        unity11Context->Unmap(staging.Get(), 0);
        return varied;
    }
    D3D12_RESOURCE_DESC source = surface.consumer12->GetDesc();
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};
    UINT64 size;
    test12->GetCopyableFootprints(&source, 0, 1, 0, &footprint, nullptr, nullptr, &size);
    D3D12_HEAP_PROPERTIES heap{}; heap.Type = D3D12_HEAP_TYPE_READBACK;
    D3D12_RESOURCE_DESC desc{}; desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Width = size; desc.Height = 1; desc.DepthOrArraySize = 1; desc.MipLevels = 1; desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    ComPtr<ID3D12Resource> readback;
    ComPtr<ID3D12CommandAllocator> allocator;
    ComPtr<ID3D12GraphicsCommandList> commands;
    if (FAILED(test12->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
        D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&readback))) ||
        FAILED(test12->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator))) ||
        FAILED(test12->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator.Get(), nullptr, IID_PPV_ARGS(&commands)))) return false;
    D3D12_TEXTURE_COPY_LOCATION from{}; from.pResource = surface.consumer12.Get(); from.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    D3D12_TEXTURE_COPY_LOCATION to{}; to.pResource = readback.Get(); to.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT; to.PlacedFootprint = footprint;
    // Simultaneous-access resource implicitly promotes COMMON -> COPY_SOURCE,
    // then decays to COMMON when the queue completes these commands.
    commands->CopyTextureRegion(&to, 0, 0, 0, &from, nullptr);
    if (FAILED(commands->Close())) return false;
    ID3D12CommandList* lists[]{commands.Get()}; testQueue->ExecuteCommandLists(1, lists);
    context.End();
    void* pixels = nullptr;
    D3D12_RANGE range{0, static_cast<SIZE_T>(size)};
    if (FAILED(readback->Map(0, &range, &pixels))) return false;
    auto* bytes = static_cast<unsigned char*>(pixels);
    bool varied = false;
    for (unsigned y = 0; y < surface.height && !varied; y += 4)
        for (unsigned x = 0; x < surface.width * 4; x += 16)
            if (bytes[y * footprint.Footprint.RowPitch + x] != bytes[0]) { varied = true; break; }
    D3D12_RANGE written{0, 0}; readback->Unmap(0, &written);
    return varied;
}
}

int main(int argc, char** argv)
{
    if (argc != 4) { std::fprintf(stderr, "Usage: SmokeTest.exe <vlc-directory> <video> <11|12>\n"); return 2; }
    testRenderer = std::atoi(argv[3]) == 12 ? kUnityGfxRendererD3D12 : kUnityGfxRendererD3D11;
    SetDllDirectoryA(argv[1]);
    HMODULE library = LoadLibraryA("libvlc.dll");
    if (!library) { std::fprintf(stderr, "Load libvlc failed %lu\n", GetLastError()); return 2; }
    if (testRenderer == kUnityGfxRendererD3D11)
    {
        if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0,
            D3D11_SDK_VERSION, &test11, nullptr, nullptr))) return 2;
    }
    else
    {
        D3D12_COMMAND_QUEUE_DESC queue{};
        if (FAILED(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&test12))) ||
            FAILED(test12->CreateCommandQueue(&queue, IID_PPV_ARGS(&testQueue)))) return 2;
    }
    testGraphics.GetRenderer = GetTestRenderer; testGraphics.RegisterDeviceEventCallback = RegisterTestEvent;
    testGraphics.UnregisterDeviceEventCallback = RegisterTestEvent;
    testGraphics11.GetDevice = GetTestDevice11;
    testGraphics12.GetDevice = GetTestDevice12; testGraphics12.GetCommandQueue = GetTestQueue;
    testGraphics12.ConfigureEvent = ConfigureTestEvent;
    IUnityInterfaces testInterfaces{}; testInterfaces.GetInterface = GetTestInterface;
    UnityPluginLoad(&testInterfaces);
    const char* options[]{"--no-audio", "--no-osd", "--no-video-title-show", "--verbose=-1"};
    auto newVlc = Proc<libvlc_instance_t* (*)(int, const char* const*)>(library, "libvlc_new");
    auto freeVlc = Proc<void (*)(libvlc_instance_t*)>(library, "libvlc_release");
    auto newMedia = Proc<void* (*)(const char*)>(library, "libvlc_media_new_path");
    auto freeMedia = Proc<void (*)(void*)>(library, "libvlc_media_release");
    auto setMedia = Proc<void (*)(libvlc_media_player_t*, void*)>(library, "libvlc_media_player_set_media");
    auto play = Proc<int (*)(libvlc_media_player_t*)>(library, "libvlc_media_player_play");
    libvlc_instance_t* instance = newVlc(4, options);
    if (!instance) { std::fprintf(stderr, "libvlc_new failed\n"); return 2; }
    auto* player = libvlc_unity_media_player_new(instance);
    auto* renderContext = libvlc_unity_get_render_context(player);
    RenderEvent(0, renderContext);
    const int backend = libvlc_unity_get_capabilities(player);
    std::printf("Backend: %d\n", backend); std::fflush(stdout);
    if (backend != (testRenderer == kUnityGfxRendererD3D11 ? D3D11 : D3D12)) return 3;
    void* media = newMedia(argv[2]); setMedia(player, media); freeMedia(media);
    if (play(player) != 0) return 3;
    unsigned frames = 0, width = 0, height = 0;
    std::uint64_t previous = 0;
    bool pixels = false;
    const auto deadline = GetTickCount64() + 15000;
    while (GetTickCount64() < deadline && frames < 20)
    {
        RenderEvent(0, renderContext);
        std::uint64_t version;
        if (libvlc_unity_get_texture_info(player, &width, &height, &version) && version != previous)
        {
            previous = version; ++frames;
            auto context = FindPlayer(player);
            if (context->Begin()) pixels = CheckPixels(*context) || pixels;
        }
        Sleep(10);
    }
    std::printf("Frames: %u, size: %ux%u, varied pixels: %s\n", frames, width, height, pixels ? "yes" : "no");
    libvlc_unity_media_player_release(player); RenderEvent(3, renderContext);
    freeVlc(instance); UnityPluginUnload();
    return frames >= 10 && pixels ? 0 : 1;
}
