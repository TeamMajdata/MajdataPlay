# Frame Latency Validation

Compares render queue depth and display latency between graphics APIs (for example D3D11 and Vulkan on
Windows) with VSync enabled. Two independent measurements are used:

1. The in-game `FrameTimingDiagnostics` (Unity `FrameTimingManager`), which shows how long a frame waits
   inside the engine and the driver before the GPU finishes it.
2. [PresentMon](https://github.com/GameTechDev/PresentMon), which shows the presentation path chosen by
   Windows and the time from Present to the frame reaching the screen.

Neither measurement includes the display panel's own processing time. Results apply only to the tested
machine, driver, display mode and refresh rate.

## In-game diagnostics

Set the hidden debug option in `settings.json` and start the game:

```json
"Debug": {
    "FrameTimingDiagnostics": true
}
```

`ProjectSettings` enables **Frame Timing Stats**, so release Players report frame timings. While the option is
on, the runtime info overlay (`Debug.DisplayRuntimeInfo`) shows a second line, and every 5 seconds the log
receives an `Info` entry with the tag `FrameTimingDiagnostics`:

```text
API=Vulkan VSync=1 MaxQueuedFrames=2 TargetFPS=-1 Mode=ExclusiveFullScreen Refresh=60.00Hz Frames=300 | main=1.20ms presentWait=14.80ms render=0.90ms GPU=1.10ms | start->present=33.10ms start->GPUComplete=34.20ms (max 36.00ms) pacingWait=0.00ms
```

| Field | Meaning |
| --- | --- |
| `main`, `render`, `GPU` | Work time of the main thread, render thread and GPU for one frame. |
| `presentWait` | Time the main thread spent blocked waiting for the render thread or the swapchain. |
| `start->present` | From the main thread starting the frame to the render thread calling Present. |
| `start->GPUComplete` | From the main thread starting the frame to the GPU finishing it (`n/a` when the platform does not report it). |
| `pacingWait` | Time the main thread waited for low-latency VSync pacing at the very start of the frame, before time and input are sampled (zero when pacing is off). |

With VSync on, the actual work (`main`, `render`, `GPU`) is usually far below one refresh interval. The
difference between `start->GPUComplete` and that work, divided by the refresh interval, is the number of frames
queued inside the engine and driver. Compare that number between APIs. Frames queued after GPU completion,
waiting in the swapchain for scanout, are not visible here; PresentMon covers them.

The pacing wait happens before the game samples time and input, so it does not add display latency. When
`pacingWait` is not zero, the latency from sampling to Present is about `start->present` minus `pacingWait`.

## Low-latency VSync pacing (Vulkan)

With VSync on, the Vulkan FIFO swapchain lets the game run until every swapchain image is queued, so frames
are rendered about three refresh intervals before they are shown (D3D11 limits this queue with a waitable
swapchain). The hidden option `Debug.LowLatencyVSync` (default `true`) makes `LowLatencyVSync` wait at the
start of each frame so that frames are produced 0.1 % slower than the display refreshes, which keeps the queue
empty. It applies only on desktop with the Vulkan backend and VSync on. The display then shows one frame twice
about every 1000 frames (about 17 s at 60 Hz). If the reported refresh rate is higher than the real one, the
pacer measures the real period after 120 unpaced frames and logs `frame interval re-measured`.

To verify it, run Vulkan with VSync on twice, with `Debug.LowLatencyVSync` set to `false` and `true`, and compare
`start->present - pacingWait`. Also watch fast-moving notes for the periodic repeated frame.

### Present waiting (Windows players)

On Windows players whose GPU driver supports `VK_KHR_present_id` and `VK_KHR_present_wait`, the FFmpeg graphics
bridge tags every presentation with an id and `LowLatencyVSync` instead waits at the start of each frame until the
previous frame has been shown, like D3D11's waitable swapchain. The latency is then constant and there is no
periodic repeated frame. The log shows which mode is active:

- `Frame pacing started: each frame waits until the previous frame is shown (VK_KHR_present_wait)`
- `Present waiting unavailable (<reason>); using timer pacing` followed by `Frame pacing started: refresh ...`
- `Present waiting timed out|failed 30 times in a row ...; switching to timer pacing`

Set the hidden option `Debug.LowLatencyVSyncPresentWait` to `false` to force timer pacing for comparison. The
Editor always uses timer pacing, because its views present through several swapchains.

## Procedure

Use the same build, display, refresh rate, window mode and scene (a chart in the Game scene, or the List
scene) for every run. Let each run settle for at least 15 seconds before reading the numbers.

1. Turn on `Debug.FrameTimingDiagnostics` and make sure `Rendering.VSync` is `true`.
2. Start the Windows Player with each API in turn:

   ```powershell
   ./MajdataPlay.exe -force-d3d11
   ./MajdataPlay.exe -force-vulkan
   ```

3. In parallel with each run, capture 30 seconds with PresentMon (run as administrator):

   ```powershell
   PresentMon.exe --process_name MajdataPlay.exe --output_file d3d11.csv --timed 30 --terminate_after_timed
   ```

4. Record from the game log: the median of `start->present`, `start->GPUComplete` and `presentWait`.
5. Record from the PresentMon CSV: the `PresentMode` column, and the display latency column
   (`DisplayLatency` with the default metrics of PresentMon 2.x, `MsUntilDisplayed` with `--v1_metrics`).

### Optional experiments

Repeat the Vulkan run with one change at a time:

- `Debug.MaxQueuedFrames` set to `1`.
- A Development Player built with **Player Settings > Vulkan Settings > Late Acquire Next Image** enabled.
- Borderless window instead of exclusive fullscreen, to see whether `PresentMode` changes.

## Reading the results

| Observation | Likely cause |
| --- | --- |
| Vulkan `start->GPUComplete` is about one or more refresh intervals larger than D3D11, while `main`/`render`/`GPU` are similar | Deeper CPU/GPU queue: the swapchain back-pressure reaches the main thread later than the D3D11 waitable swapchain. |
| D3D11 shows a large `presentWait` at frame start, Vulkan shows little | D3D11 throttles the main thread before sampling input; Vulkan lets it run ahead. |
| PresentMon shows `Hardware: Independent Flip` for D3D11 but `Composed: Flip` or `Composed: Copy with GPU GDI` for Vulkan | The Vulkan swapchain goes through DWM composition, adding about one refresh interval. Check the driver's Vulkan present method setting. |
| `MaxQueuedFrames = 1` removes most of the difference | The engine-side frame queue is the main contributor. |

## Limitations

- The overlay and log only report on platforms where `FrameTimingManager.IsFeatureEnabled()` is true.
- `cpuTimeFrameComplete` is missing on some drivers; then only `start->present` is available.
- The EditMode tests in `Assets/Tests/EditMode/Game/Misc/Diagnostics/FrameTimingDiagnosticsTests.cs` cover the
  averaging and windowing logic only, not real driver or display behavior.
