// Host tests verify the portable ABI, incompatible-engine rejection, direct
// player pointer ownership and callback lifetime boundary without a decoder.
#include "Unity/IUnityGraphics.h"
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <thread>
#include <atomic>

struct libvlc_instance_t {};
struct libvlc_media_player_t { libvlc_instance_t* owner; };
static const char* engineVersion = "4.0.0-dev Otto Chriek";
static const char* engineChangeset = "4.0.0-dev-33583-gd94fd0473f";
static std::atomic<int> livePlayers{0};
static std::atomic<int> callbackCompletions{0};

extern "C"
{
    libvlc_media_player_t* libvlc_media_player_new(libvlc_instance_t* owner)
    { ++livePlayers; return new libvlc_media_player_t{owner}; }
    void libvlc_media_player_release(libvlc_media_player_t* player)
    {
        // Mirror LibVLC's join boundary. Release must finish outstanding native
        // work before callers can dispose their pinned callback state.
        std::thread callback([] { ++callbackCompletions; }); callback.join();
        delete player; --livePlayers;
    }
    const char* libvlc_get_version() { return engineVersion; }
    const char* libvlc_get_changeset() { return engineChangeset; }
    libvlc_media_player_t* libvlc_unity_media_player_new(libvlc_instance_t*);
    void libvlc_unity_media_player_release(libvlc_media_player_t*);
    int libvlc_unity_get_bridge_abi();
    int libvlc_unity_validate_libvlc();
    const char* libvlc_unity_get_last_error();
    int libvlc_unity_get_capabilities(libvlc_media_player_t*);
    void libvlc_unity_disable_gpu(libvlc_media_player_t*);
    void* libvlc_unity_get_render_context(libvlc_media_player_t*);
    void* libvlc_unity_get_texture_info(libvlc_media_player_t*, unsigned*, unsigned*, std::uint64_t*);
    void* libvlc_unity_get_texture(libvlc_media_player_t*, unsigned, unsigned, bool*);
    UnityRenderingEventAndData libvlc_unity_get_render_event_func();
    void SetPluginPath(const char*);
}

#define CHECK(condition) do { if (!(condition)) { std::fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #condition); return 1; } } while (false)

int main()
{
    CHECK(libvlc_unity_get_bridge_abi() == 1);
    CHECK(libvlc_unity_validate_libvlc() == 1);
    CHECK(libvlc_unity_media_player_new(nullptr) == nullptr);
    CHECK(std::strstr(libvlc_unity_get_last_error(), "null") != nullptr);
    libvlc_instance_t library;
    auto* player = libvlc_unity_media_player_new(&library);
    CHECK(player && player->owner == &library && livePlayers == 1);
    CHECK(libvlc_unity_get_capabilities(player) == 0);
    CHECK(libvlc_unity_get_render_context(player) == player);
    libvlc_unity_disable_gpu(player);
    unsigned width = 100, height = 100; std::uint64_t frame = 100;
    CHECK(libvlc_unity_get_texture_info(player, &width, &height, &frame) == nullptr);
    CHECK(width == 0 && height == 0 && frame == 0);
    bool updated = true;
    CHECK(libvlc_unity_get_texture(player, 100, 100, &updated) == nullptr && !updated);
    auto event = libvlc_unity_get_render_event_func(); CHECK(event != nullptr);
    auto* token = libvlc_unity_get_render_context(player);
    event(0, token); event(1, token); event(2, token);
    libvlc_unity_media_player_release(player);
    CHECK(livePlayers == 0 && callbackCompletions == 1);
    event(3, token); // A queued CPU event must not dereference the freed player.
    libvlc_unity_media_player_release(nullptr);

    engineVersion = "3.0.21 Vetinari";
    CHECK(libvlc_unity_validate_libvlc() == 0);
    CHECK(libvlc_unity_media_player_new(&library) == nullptr && livePlayers == 0);
    engineVersion = "4.0.0-dev"; engineChangeset = "future-incompatible-player-callback-ABI";
    CHECK(libvlc_unity_validate_libvlc() == 0);
    CHECK(std::strstr(libvlc_unity_get_last_error(), "d94fd0473f") != nullptr);
    engineChangeset = "4.0.0-dev-33583-gd94fd0473f";
    CHECK(libvlc_unity_validate_libvlc() == 1);
    SetPluginPath("/tmp/vlc-modules:/tmp/extra-vlc-modules");
#if defined(_MSC_VER)
    char* pluginPath = nullptr; size_t pathLength = 0;
    CHECK(_dupenv_s(&pluginPath, &pathLength, "VLC_PLUGIN_PATH") == 0 && pluginPath);
    CHECK(std::strcmp(pluginPath, "/tmp/vlc-modules:/tmp/extra-vlc-modules") == 0);
    std::free(pluginPath);
#else
    CHECK(std::strcmp(std::getenv("VLC_PLUGIN_PATH"), "/tmp/vlc-modules:/tmp/extra-vlc-modules") == 0);
#endif
    std::puts("PASS: portable bridge ABI, decoder pin, ownership, callbacks and stale render events");
    return 0;
}
