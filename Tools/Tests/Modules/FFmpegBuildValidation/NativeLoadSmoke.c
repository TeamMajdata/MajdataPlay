/* Load the real staged Windows DLLs without a development-toolchain search path.
 * Compile with the target compiler and the pinned FFmpeg binding headers.
 * argv[1] is the absolute directory containing the eight staged native DLLs.
 * This checks loader closure, exact FFmpeg versions, codec registration, bridge
 * ABI and Unity exports; it is not a decode, encode or Unity/GPU integration test.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <libavcodec/version.h>
#include <libavdevice/version.h>
#include <libavfilter/version.h>
#include <libavformat/version.h>
#include <libavutil/version.h>
#include <libswresample/version.h>
#include <libswscale/version.h>

/* Exact version function and expected version for one FFmpeg component. */
struct LibraryCheck
{
    const char *file;
    const char *function;
    unsigned int expected;
};

/* FFmpeg's public cdecl version and codec-lookup signatures. */
typedef unsigned int (__cdecl *VersionFunction)(void);
typedef const void *(__cdecl *CodecFunction)(const char *name);
typedef int (__cdecl *BridgeAbiFunction)(void);

/* Load an absolute DLL, admitting only colocated DLLs and Windows system DLLs. */
static HMODULE LoadStaged(const char *directory, const char *name)
{
    char path[32768];
    if (snprintf(path, sizeof(path), "%s/%s", directory, name) >= (int)sizeof(path))
    {
        fprintf(stderr, "DLL path too long\n");
        return NULL;
    }
    HMODULE module = LoadLibraryExA(path, NULL, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module)
    {
        fprintf(stderr, "LoadLibraryEx %s failed: %lu\n", name, GetLastError());
    }
    return module;
}

/* Return zero only when all staged library and bridge loader checks pass. */
int main(int argc, char **argv)
{
    if (argc != 2)
    {
        fprintf(stderr, "Usage: NativeLoadSmoke.exe <absolute staged directory>\n");
        return 2;
    }
    const struct LibraryCheck checks[] = {
        {"avutil-61.dll", "avutil_version", LIBAVUTIL_VERSION_INT},
        {"swresample-7.dll", "swresample_version", LIBSWRESAMPLE_VERSION_INT},
        {"swscale-10.dll", "swscale_version", LIBSWSCALE_VERSION_INT},
        {"avcodec-63.dll", "avcodec_version", LIBAVCODEC_VERSION_INT},
        {"avformat-63.dll", "avformat_version", LIBAVFORMAT_VERSION_INT},
        {"avfilter-12.dll", "avfilter_version", LIBAVFILTER_VERSION_INT},
        {"avdevice-63.dll", "avdevice_version", LIBAVDEVICE_VERSION_INT}
    };
    HMODULE codec = NULL;
    for (size_t index = 0; index < sizeof(checks) / sizeof(checks[0]); ++index)
    {
        const struct LibraryCheck *check = &checks[index];
        HMODULE module = LoadStaged(argv[1], check->file);
        if (!module)
        {
            return 1;
        }
        VersionFunction version = (VersionFunction)(void *)GetProcAddress(module, check->function);
        if (!version || version() != check->expected)
        {
            fprintf(stderr, "Unexpected version/export in %s\n", check->file);
            return 1;
        }
        printf("PASS %s exact version %u.%u.%u\n", check->file,
               check->expected >> 16, (check->expected >> 8) & 255, check->expected & 255);
        if (strcmp(check->function, "avcodec_version") == 0)
        {
            codec = module;
        }
    }
    CodecFunction decoder = (CodecFunction)(void *)GetProcAddress(codec, "avcodec_find_decoder_by_name");
    CodecFunction encoder = (CodecFunction)(void *)GetProcAddress(codec, "avcodec_find_encoder_by_name");
    if (!decoder || !encoder || !decoder("libdav1d"))
    {
        fprintf(stderr, "Missing codec lookup exports or libdav1d decoder\n");
        return 1;
    }
    const char *software[] = {"mpeg4", "libx264", "libx265", "libvpx-vp9", "libaom-av1"};
    for (size_t index = 0; index < sizeof(software) / sizeof(software[0]); ++index)
    {
        if (!encoder(software[index]))
        {
            fprintf(stderr, "Missing encoder %s\n", software[index]);
            return 1;
        }
    }
#if defined(__aarch64__)
    const char *unsupported[] = {"h264_nvenc", "hevc_nvenc", "av1_nvenc", "h264_amf", "hevc_amf", "av1_amf"};
    for (size_t index = 0; index < sizeof(unsupported) / sizeof(unsupported[0]); ++index)
    {
        if (encoder(unsupported[index]))
        {
            fprintf(stderr, "Unsupported ARM64 encoder %s was enabled\n", unsupported[index]);
            return 1;
        }
    }
#endif
    HMODULE bridge = LoadStaged(argv[1], "FFmpegUnityBridge.dll");
    if (!bridge)
    {
        return 1;
    }
    BridgeAbiFunction abi = (BridgeAbiFunction)(void *)GetProcAddress(bridge, "ffu_abi_version");
    if (!abi || abi() != 4 || !GetProcAddress(bridge, "UnityPluginLoad") || !GetProcAddress(bridge, "UnityPluginUnload"))
    {
        fprintf(stderr, "Missing Unity lifecycle exports or bridge ABI 4\n");
        return 1;
    }
    printf("PASS libdav1d + five software encoders; staged bridge ABI 4 and Unity lifecycle exports\n");
    return 0;
}
