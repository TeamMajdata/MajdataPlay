# Unity Vulkan queue-fence diagnostic marker

This is a new Windows x64 diagnostic binary, **not the original pacing implementation**. Its output filename is deliberately `MajdataPlayVulkanPacing.dll` solely so an isolated, already-built Player can reuse its existing native-loader registration. Building does not install that DLL anywhere. Never overwrite an Assets plug-in or original Build with it.

The library retrieves the existing Unity Vulkan V2 instance and graphics queue through `GetInterfaceSplit`. It installs **no loader, device, swapchain, present or Vulkan function interceptor**, requests no extension/feature, and implements no NVIDIA/AMD low-latency behavior. Only Unity lifecycle callbacks and one reserved plug-in render event are registered.

## Build only

```powershell
./build.ps1
```

This uses the existing Visual Studio 2022 Community x64 MSVC compiler and local CMake, C++17, `/W4 /WX /MT`, plus the repository's actual Unity and Vulkan headers. No package download, generated managed project, Editor or GPU test is involved. Output and hashes are in `.build/Release/MajdataPlayVulkanPacing.dll` and `.build/build-manifest.json`. Runtime loading, real queue ordering and GPU completion remain **unverified until the lead's controlled Player test**. No compilation result establishes that the existing loader name will load this replacement correctly.

## ABI 1

All `mf_*` exports use C linkage and `__cdecl`; Unity lifecycle/render callbacks use `UNITY_INTERFACE_API` from the actual headers. Windows x64 pointer/handle widths are used; no C `long` or managed structure layout is exposed.

| Export | Contract |
| --- | --- |
| `int mf_abi_version()` | 1 |
| `int mf_status()` | 0 while the existing Vulkan device/functions/interface are available; negative error otherwise |
| `int mf_event_id()` | Reserved event identifier; -1 before registration |
| `UnityRenderingEventAndData mf_render_callback()` | Function pointer for `CommandBuffer.IssuePluginEventAndData` |
| `void* mf_ticket_create()` | Owned opaque token, or null for unavailability/allocation failure/capacity; maximum four resident tickets |
| `int mf_ticket_poll(void*)` | 0 not yet complete, 1 only after actual `vkGetFenceStatus==VK_SUCCESS`, or a negative native/Vulkan error |
| `int mf_ticket_release(void*)` | 1 after safe destruction/cancellation, 0 when pending/invalid/stale; negative on loss discovered by its completion recheck |

Native errors are -10001 invalid token, -10002 unavailable, -10003 inactive/old generation, -10004 duplicate/wrong event and -10005 internal failure. Vulkan failures preserve their negative `VkResult`, including -4 device loss. `mf_status` also preserves a fence-creation error. A null create must be treated as backpressure/error, not permission to reuse a texture.

Tickets are integer tokens carried as opaque pointers and never dereferenced. Tokens increase monotonically and are not reused; registry lookup occurs under one mutex. A cancelled Created ticket can disappear before a deferred render callback arrives: the callback then finds no token and does nothing, without dereferencing freed storage. Created tickets must either be submitted exactly once or released. A render event changes Created to Queued before calling `AccessQueue`, so owner release cannot destroy a ticket between render and a deferred queue callback. Queued/Submitted tickets remain in the bounded registry. Duplicate submission is an error and does not resubmit the fence.

The render event is configured outside render passes and with EnsurePreviousFrameSubmission. It calls Unity `AccessQueue(..., flush=true)` outside the registry mutex because Unity may run the queue callback synchronously. Unity's header explicitly guarantees that all current-frame Unity command buffers are submitted before this callback when flush is true. The callback, which supplies external synchronization for Unity's graphics queue, submits one empty `VkSubmitInfo` with a new unsignaled `VkFence` using the actual queue's `vkQueueSubmit`. No command pool/buffer, texture, semaphore, private queue or transfer is created. The Vulkan fence signal covers earlier commands in submission order on that queue; this is a graphics-queue completion marker, not a timestamp or proof of completion on independent queues.

**Placement is part of the experiment.** Managed code must retire/unbind a texture, then issue this event only after the commands containing its last actual graphics-queue use have been recorded/submitted, and must never submit a new use afterward. A marker at an arbitrary Canvas callback or before later camera draws does not prove last use. The library has no texture knowledge and cannot validate this requirement. A successful marker does not prove correct image layouts, cross-queue dependencies or a driver free of faults.

## Cancellation, device loss and shutdown

`mf_ticket_release` destroys a Created ticket's fence before any queue admission, or a Submitted fence only after querying success under the same state gate. Pending queue work is never waited on or destroyed. For `VK_ERROR_DEVICE_LOST`, it retires host ownership but returns the negative `VK_ERROR_DEVICE_LOST`, retaining the fence and registry slot; **this includes loss between an earlier successful poll and release, and must never authorize texture reuse**. Other failed submissions are retained conservatively.

All device callbacks, polls, creation, queue submission and release share the state mutex. The handoff from render event to `AccessQueue` necessarily occurs outside that mutex to permit synchronous queue callbacks. It relies on Unity's normal ordering of render events, interface lifetime and device lifecycle callbacks; arbitrary concurrent device/interface destruction during that handoff is not supported or claimed safe. Shutdown marks the device inactive before further queries can run; old-generation tickets subsequently return a negative status without calling the old device. Shutdown performs no `vkDeviceWaitIdle` and does not destroy fences whose in-flight state is unknown. Those tickets/fences remain bounded by the same four resident slots until process teardown; repeated device loss/reset can exhaust capacity and explicitly prevent new tickets. This is intentional diagnostic retention, not production-complete shutdown cleanup. Dynamic plug-in unloading while Unity still has callbacks queued is outside the supported experiment: keep the DLL loaded until the diagnostic Player exits.

## Validation limits

Build, exported-symbol inspection and `MarkerStateTests.exe` are non-GPU validation only. The state test links this implementation against fake Vulkan/interface function pointers; it covers create/cancel, a late render event after cancellation, token non-reuse, deferred queue ownership, pending release refusal, successful completion retirement, four-ticket capacity and old-generation rejection without device calls after shutdown. It loads no Vulkan driver and creates no real device. The lead must independently verify the loaded module hash, ABI/status, actual Vulkan backend, marker admission/completion counters, no texture reuse before successful poll/release, bounded backpressure and normal Game-to-Result behavior. No original low-latency `majvk_*` exports are provided, so managed code must use only `mf_*` for this marker binary. Missing original pacing ABI is expected in the isolated experiment; do not label this DLL as the original pacing module in results.
