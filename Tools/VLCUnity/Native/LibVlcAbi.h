#pragma once

// Deliberately pinned to bundled LibVLC 4.0.0-dev-33583-gd94fd0473f.
// Newer LibVLC 4 snapshots changed media_player_new's ABI. Do not substitute
// their headers or DLLs without updating and validating these declarations.
#include <windows.h>
#include <cstddef>
#include <cstdint>

struct libvlc_instance_t;
struct libvlc_media_player_t;
struct SetupConfig { bool hardwareDecoding; };
struct SetupInfo { void* deviceContext; void* contextMutex; };
struct RenderConfig
{
    unsigned width, height, bitDepth;
    bool fullRange;
    int colorSpace, primaries, transfer;
    void* device;
};
struct OutputConfig
{
    union { int dxgiFormat; void* surface; } format;
    bool fullRange;
    int colorSpace, primaries, transfer, orientation;
};
static_assert(offsetof(RenderConfig, device) == 32, "LibVLC render ABI");
static_assert(offsetof(OutputConfig, orientation) == 24, "LibVLC output ABI");

using SetupCallback = bool (*)(void**, const SetupConfig*, SetupInfo*);
using CleanupCallback = void (*)(void*);
using WindowCallback = void (*)(void*, void*, void*, void*, void*, void*);
using UpdateCallback = bool (*)(void*, const RenderConfig*, OutputConfig*);
using SwapCallback = void (*)(void*);
using CurrentCallback = bool (*)(void*, bool);
using ProcCallback = void* (*)(void*, const char*);
using MetadataCallback = void (*)(void*, int, const void*);
using PlaneCallback = bool (*)(void*, size_t, void*);

struct LibVlcApi
{
    libvlc_media_player_t* (*create)(libvlc_instance_t*) = nullptr;
    void (*release)(libvlc_media_player_t*) = nullptr;
    bool (*output)(libvlc_media_player_t*, int, SetupCallback, CleanupCallback,
        WindowCallback, UpdateCallback, SwapCallback, CurrentCallback,
        ProcCallback, MetadataCallback, PlaneCallback, void*) = nullptr;

    bool Load()
    {
        HMODULE library = GetModuleHandleW(L"libvlc.dll");
        if (!library) return false;
        create = reinterpret_cast<decltype(create)>(GetProcAddress(library, "libvlc_media_player_new"));
        release = reinterpret_cast<decltype(release)>(GetProcAddress(library, "libvlc_media_player_release"));
        output = reinterpret_cast<decltype(output)>(GetProcAddress(library, "libvlc_video_set_output_callbacks"));
        return create && release && output;
    }
};
