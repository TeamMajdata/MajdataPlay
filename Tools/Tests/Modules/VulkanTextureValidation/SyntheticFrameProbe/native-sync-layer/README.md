# Diagnostic explicit Vulkan sync layer (`VK_LAYER_MAJDATAPLAY_SYNC`)

A minimal explicit Vulkan layer for the MajdataPlay Vulkan texture/startup investigation. It
exists for one purpose: while a gfxrecon capture is replayed, append an explicit
`TRANSFER_WRITE -> VERTEX_INPUT` buffer memory dependency after qualifying `vkCmdCopyBuffer`
calls, so a replay **with** injection can be compared against a replay of the same capture
**without** it. The A/B comparison is meant to test whether the already observed "index buffer
copied, then read by `vkCmdDrawIndexed` with no transfer-write -> index-read dependency" gap
correlates with the device-loss events.

It is a diagnostic instrument. It is not a production fix, and it is deliberately not part of
the game build.

## Contents

| File | Role |
| --- | --- |
| `SyncLayer.cpp` | The layer implementation and the `msl_*` diagnostic exports. |
| `CPUtests.cpp` | Host-only state tests: the layer translation unit compiled against fake entry points. |
| `CMakeLists.txt` | Produces `VkLayer_majdataplay_sync.dll` and `SyncLayerStateTests.exe`. |
| `build.ps1` | Configure, build, run the state tests, stage the manifest and write `.build/build-manifest.json`. |
| `VkLayer_majdataplay_sync.json` | Layer manifest (`VK_LAYER_MAJDATAPLAY_SYNC`, `GLOBAL`, api version 1.1.0). |

## Build

```powershell
./build.ps1
```

Uses the existing Visual Studio 2022 Community x64 MSVC toolset and the local CMake. No Vulkan
SDK, no `vulkan-1.lib`, no download. Outputs:

```text
.build/Release/VkLayer_majdataplay_sync.dll
.build/Release/VkLayer_majdataplay_sync.json   <- staged next to the DLL for VK_LAYER_PATH
.build/Release/SyncLayerStateTests.exe
.build/build-manifest.json                     <- source/DLL SHA256, exports, toolchain
```

`build.ps1` also runs the host-only state tests. They link no Vulkan library and create no
Vulkan object.

## Enabling it in gfxrecon-replay

Both the DLL and the JSON manifest must be in one directory, and that directory is what
`VK_LAYER_PATH` points at. The manifest uses a relative `library_path`, so the two files cannot
be separated.

```powershell
$layerDir = '<repo>/Tools/Tests/Modules/VulkanTextureValidation/SyntheticFrameProbe/native-sync-layer/.build/Release'
$env:VK_LAYER_PATH      = $layerDir
$env:VK_INSTANCE_LAYERS = 'VK_LAYER_MAJDATAPLAY_SYNC'
$env:MAJ_SYNC_LAYER_MODE = '1'          # 0 = passthrough control, 1 = narrow, 2 = broad
$env:VK_LOADER_DEBUG    = 'error,warn,layer'   # confirms that the loader picked the layer up
gfxrecon-replay.exe <capture>.gfxr
```

Keep the rest of the replay environment identical between the A and B runs. Set
`VK_INSTANCE_LAYERS` to a single name; do not mix this layer with a capture layer in the same
run.

To rule the layer out entirely, either leave `VK_INSTANCE_LAYERS` unset or set
`MAJDATAPLAY_SYNC_DISABLE=1` (the manifest's `disable_environment`).

## Modes (`MAJ_SYNC_LAYER_MODE`, read once at load)

| Value | Mode | Injected dependency |
| --- | --- | --- |
| unset / `0` | passthrough | nothing. `vkCmdCopyBuffer` still passes through the wrapper, is forwarded with the original arguments and is counted, but no barrier is recorded. |
| `1` | narrow | only when the destination buffer was created with `INDEX_BUFFER` and/or `VERTEX_BUFFER` usage: one `VkBufferMemoryBarrier` with `srcAccessMask=TRANSFER_WRITE`, `dstAccessMask=INDEX_READ\|VERTEX_ATTRIBUTE_READ`, `srcQueueFamilyIndex=dstQueueFamilyIndex=QUEUE_FAMILY_IGNORED`, `buffer=dstBuffer`, `offset=0`, `size=WHOLE_SIZE`, recorded as `vkCmdPipelineBarrier(cb, TRANSFER, VERTEX_INPUT, 0, 0, nullptr, 1, &barrier, 0, nullptr)`. |
| `2` | broad | every `vkCmdCopyBuffer`, same barrier except `dstAccessMask=MEMORY_READ` and destination stage `ALL_COMMANDS`. This is the coarse control: it serialises far more work than the dependency requires. |

Buffer usage is recorded by wrapping `vkCreateBuffer` / `vkDestroyBuffer` in a
`std::mutex` + `std::unordered_map<VkBuffer, VkBufferUsageFlags>`. The driver calls are always
outside the lock, and the copy is always forwarded unchanged **before** the barrier is
recorded.

The barrier is recorded through `vkCmdPipelineBarrier` resolved from the **next** layer's
`vkGetDeviceProcAddr`, not through a locally cached driver pointer, so later layers (including
a validation layer) see the injected command exactly as if the application had recorded it.

## Diagnostic exports (C, `__cdecl`)

| Export | Meaning |
| --- | --- |
| `uint64_t msl_copy_calls()` | intercepted `vkCmdCopyBuffer` calls, all modes |
| `uint64_t msl_barrier_calls()` | appended barriers, all modes |
| `uint64_t msl_skipped_calls()` | mode 1 copies skipped because the destination had no index/vertex usage (or no recorded usage) |
| `uint64_t msl_errors()` | internal failures: missing link information, unresolved entry point, recursive resolver |
| `int msl_mode()` | effective mode, `0`/`1`/`2` |

They are plain exports next to the Vulkan entry points, so a host can `LoadLibrary` the DLL and
read the counters without a Vulkan loader. Note that the loader itself already holds the DLL,
so the counters of a `LoadLibrary` in another process are not the replay's counters.

## Layer interface notes

- Exports `vkNegotiateLoaderLayerInterfaceVersion`, `vkGetInstanceProcAddr`,
  `vkGetDeviceProcAddr`; the manifest points at all three.
- `vkNegotiateLoaderLayerInterfaceVersion` fills `pfnGetInstanceProcAddr` and
  `pfnGetDeviceProcAddr`, leaves `pfnGetPhysicalDeviceProcAddr` empty, and reports interface
  version 2 to a version-2 loader. A loader that offers version 1 is answered with version 1 so
  the layer never writes the version-2-only member of a shorter structure.
- `vkCreateInstance` / `vkCreateDevice` consume the loader's `VK_LAYER_LINK_INFO` entry and keep
  its `pfnNextGetInstanceProcAddr` (`pNextGIPA`) / `pfnNextGetDeviceProcAddr` (`pNextGDPA`).
  A create call without link information fails closed with `VK_ERROR_INITIALIZATION_FAILED`
  instead of returning an object the layer cannot forward.
- The wrapped device commands are also resolved eagerly once the layer owns a real device, so
  the copy wrapper never needs to map a command buffer back to a device.
- Self queries (`GIPA`/`GDPA` for their own names, and any wrapped name) always return the
  wrapper. A lower resolver is never cached, and a lower resolver that hands back one of this
  layer's own entry points is counted as an error instead of being cached (that would make the
  wrapper call itself).
- The repository's `Tools/FFmpeg/Native/ThirdParty/Vulkan-Headers` copy has no `vk_layer.h`, so
  the loader/layer ABI structures used here (`VkLayerInstanceCreateInfo`,
  `VkLayerInstanceLink`, `VkNegotiateLayerInterface`) are declared locally with compile-time
  layout assertions.

## Limits and boundaries

- **The injected barrier changes scheduling.** It adds a dependency, may serialise work and
  changes the timing of the replay. A run that no longer reproduces the fault proves only that
  the extra dependency is correlated with the fault; it is not a production fix justification.
  Mode `2` is much coarser than the real dependency and can hide unrelated races.
- The image-side gaps are not addressed: swapchain clear -> render-pass implicit layout
  transition (WAW) and acquire -> `TRANSFER` layout transition (WAR) remain untouched.
- Mode `1` only matches buffers created with `INDEX_BUFFER` / `VERTEX_BUFFER` usage. A buffer
  used as an index source without that usage flag is skipped, and mode `2` is the fallback
  control for that case.
- Single instance / single device is assumed: the next-layer resolvers and the wrapped device
  entry points are stored once. gfxrecon-replay creates one of each; do not use this layer in a
  multi-device application.
- The mode is read once, on the first entry point that runs. Changing the environment variable
  after the layer was initialised has no effect.
- The layer records one barrier per qualifying copy on the recording thread only; it does not
  synchronise the application's own threads.
- `vkGetPhysicalDeviceProcAddr` is not implemented. The loader falls back to
  `vkGetInstanceProcAddr` for physical-device level entry points, which is sufficient because
  this layer intercepts no physical-device function.
- No capture/replay, Player, Unity Editor or GPU run was performed for this revision. Only the
  host-only state tests were executed; runtime behaviour in `gfxrecon-replay` is unverified.

## Host-only state tests

`SyncLayerStateTests` compiles the layer against fake entry points and checks:

- negotiation: interface version 2 reported, GIPA/GDPA filled, physical-device resolver null,
  null structure rejected, version-1 loader echoed as version 1;
- mode 0: the original copy is forwarded unchanged, no barrier is recorded, counters stay
  consistent, unknown GIPA/GDPA names are forwarded;
- mode 1: index-only and vertex-only destinations are injected, the original copy is recorded
  before the barrier, the barrier targets the destination buffer with the exact access masks,
  stages, queue families, offset and `WHOLE_SIZE`; a uniform destination is skipped and
  counted; an unrecorded destination is skipped;
- mode 2: every copy is injected with `ALL_COMMANDS` / `MEMORY_READ`;
- usage lifetime: recorded on create, forgotten on destroy, destroyed buffer skipped;
- self queries return the wrapper and never replace the cached inner entry points;
- failures: unresolved chain, missing link information, missing copy/barrier entry point and a
  recursive resolver are reported through `msl_errors()`.
