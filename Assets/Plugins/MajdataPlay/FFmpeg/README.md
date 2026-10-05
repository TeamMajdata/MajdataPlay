# FFmpeg Video Player

将 **FFmpeg Video Player** 挂在 GameObject 上，填写 Source，在 Play Mode Inspector 中可以预载、播放、暂停、停止和拖动时间轴。组件只播放视频；音轨由 MajdataPlay 现有音频系统处理。

运行时程序集为 `MajdataPlay.FFmpeg`，引用项目已接入的 `FFmpeg.AutoGen`、`MajdataPlay.Diagnostics` 和 `Unity.RenderPipelines.Core.Runtime`。本项目绑定的原生 ABI 是 FFmpeg **9.0.1**；请使用 [Tools/FFmpeg](../../../../Tools/FFmpeg) 的构建脚本，不能混用其他主版本的库。Mono / IL2CPP 使用同一套直接 P/Invoke 和带 `MonoPInvokeCallback` 的静态回调。

支持的全部软件解码格式、平台硬件解码矩阵及实际 Player 限制见 [当前 FFmpeg 解码格式支持](../../../../Tools/FFmpeg/CODEC-SUPPORT.md)。

## 使用

```csharp
using MajdataPlay.FFmpeg;
using UnityEngine;
using UnityEngine.UI;

public sealed class VideoExample : MonoBehaviour
{
    [SerializeField] FFmpegVideoPlayer _player;
    [SerializeField] RawImage _image;

    async void Start()
    {
        // 分辨率变化和硬件回退时纹理可能更换，所以应持续监听。
        _player.TextureChanged += (_, texture) => _image.texture = texture;
        // 播放器已通过 MajDebug 记录错误；ErrorReceived 可用于更新自己的 UI。
        _player.PreferredDecoderType = VideoDecoderType.Hardware;
        _player.PreferNativeTextures = true;
        await _player.PreloadAsync(System.IO.Path.Combine(Application.persistentDataPath, "movie.mp4"));
        _player.SetRate(1.25f);
        await _player.SeekAsync(3.5); // seconds，精确解码到目标所在帧
        _player.Play();
    }
}
```

可以在 Inspector 指定 Target Renderer（默认材质属性 `_MainTex`），或 Target Texture（已有 RenderTexture）。不指定输出对象时，直接使用 `Texture` / `TextureChanged`；`RawImage` 的 UV 不需要额外上下翻转。URP 材质可把 Texture Property 改为 `_BaseMap`。保持播放器组件启用，异步准备、显示与事件依赖 `Update`。

| API | 语义 |
| --- | --- |
| `Url`, `Preload(path)`, `PreloadAsync(path, token)` | 路径/URL；打开、读取信息并显示首帧，时间不推进 |
| `Prepare()`, `PrepareAsync(token)` | 使用已设置的 Url；异步完成意味着首帧已显示 |
| `Play()`, `Play(path)`, `Pause()`, `SetPause(bool)` | 基本控制；预载中调用 Play 会在准备后播放 |
| `Stop()` | 暂停并异步回到开头，保留已准备资源；不可 seek 的输入则关闭 |
| `Close()` | 取消工作、关闭媒体和释放显示资源；主线程不等待阻塞 I/O |
| `Time`, `Length` | 与 VLC 一致，毫秒；Time 可写 |
| `TimeSeconds`, `LengthSeconds`, `SeekAsync(double)` | 秒；SeekAsync 完成意味着目标帧已显示或到达输入结尾 |
| `SeekTo(TimeSpan)`, `Position` | 时间跳转；Position 是 0–1 的归一化位置 |
| `SetRate(float)`, `Rate` | 1/16–16 倍速；不支持倒放；倍速变化保持时间连续 |
| `Loop`, `NextFrame()` | 循环；暂停并向前显示一帧 |
| `State`, `IsPrepared`, `IsPlaying`, `IsBuffering`, `IsSeekable` | 状态；准备或缓冲期间时间不推进 |
| `Texture`, `Width`, `Height`, `FrameRate`, `CodecName` | 当前输出与媒体信息；CodecName 是视频编码，如 h264 |
| `CurrentBitRate`, `BitRate` | 当前画面附近约 1 秒的视频压缩码率估计、视频流平均码率，均为 bit/s；未知为 0，Inspector 自动换算单位 |
| `DecoderType`, `DecoderName`, `DecoderDevice` | 当前会话的实际解码后端类型、AVCodec 名称与设备描述；需在准备后读取 |
| `TransferMode`, `HardwareFallbackReason`, `LastError` | 实际纹理传输模式和诊断 |
| `PreferredDecoderType` | Software 使用 FFmpeg 软件解码；Hardware 优先硬件后端，依次尝试 GPU 共享、硬件解码加 CPU 上传、软件解码 |
| `PreferNativeTextures` | 默认 true；false 可直接选择硬件解码加 CPU 上传，便于兼容性排查 |
| `RequireHardwareDecoding` | 保留已有严格 GPU 模式语义，覆盖上述偏好；硬件共享不支持或运行中失败时报告错误，禁止 CPU 上传及软件回退 |
| `Prepared`, `Started`, `Paused`, `Stopped`, `EndReached`, `SeekCompleted` | 主线程事件 |
| `TextureChanged`, `FrameReady`, `TimeChanged`, `ErrorReceived` | 输出变化、帧序号、毫秒位置、错误信息 |

所有组件 API 在 Unity 主线程调用。控制时间使用单调时钟，不受 `Time.timeScale` 影响。连续 seek 只保留最后一次请求，旧 `SeekAsync` 被取消。更换 Url / Close / 销毁对象会取消尚未完成的任务；后台线程退出后关闭 FFmpeg。音轨数据包被跳过。预载不是将整个文件读入内存，帧队列默认仅 3 帧，Inspector 可调整到 1–8 帧。

Player 和解码线程共用解码会话持有的唯一单调播放时钟，时间读取、倍速和播放状态修改都在会话锁内完成，无需每帧同步两套时钟。播放期间，解码线程在新帧已经到期时淘汰它替代的旧帧，保留最新到期帧和未来帧。满队列时最多额外持有一张待入队候选帧，并按候选帧到期时间等待；不需要增大缓存才能跳过已过期画面。暂停、缓冲和 seek 时停止淘汰，准备首帧与逐帧播放仍按顺序取帧。主线程在同一队列锁内选择最新到期帧，避免与后台替换竞态。

倍速超过解码吞吐时，解码线程主动追赶播放时钟：显示区间已结束的帧在硬件下载、RGBA 转换或原生帧保留之前直接丢弃，只在队列为空时保留一张过期帧，保证画面继续前进；落后期间 codec 跳过非参考帧（`AVDISCARD_NONREF`），按时后恢复；落后超过约 0.25 秒实际时间时，对可 seek 输入向前跳到播放时钟之后的第一个关键帧，不解码中间区间，找不到后续关键帧时在下次显式 seek 前回到顺序解码。主线程播放期间的呈现频率不超过视频标称帧率（限制在 24–120 FPS 之间，含 25% 余量），因此高倍速下 CPU 上传 `LoadRawTextureData`/`Apply` 和 GPU 转换提交给 render thread 的工作量与 1 倍速相同，被限流跳过的到期帧由解码线程替换或丢弃。代价是高倍速时视频跳帧、卡顿，而不是拖慢游戏帧率。暂停、缓冲、seek、准备首帧与逐帧播放不丢帧。原生 GPU 路径（D3D12VA、Vulkan Video 等）仍在 GPU 队列上等待被呈现帧的解码完成，丢帧只能降低此类等待的频率，无法消除。

公开 API 遵循 [Microsoft .NET 命名约定](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/capitalization-conventions)：类型和成员使用 PascalCase，参数使用 camelCase。播放器、解码器、选项、帧对象及硬件会话接口均提供英文 XML 文档。旧的小驼峰成员已移除：`time` 改为 `TimeSeconds`，`texture`、`isPrepared`、`isPlaying`、`playbackSpeed` 分别使用 `Texture`、`IsPrepared`、`IsPlaying`、`Rate`。此调整需要更新调用代码，不影响已有场景的序列化字段。

解码与纹理偏好在下一次打开媒体时应用，修改设置不会重标记或中断当前会话。Inspector 的 Preferred Decoder Type 提供 Software/Hardware 选择，旧场景序列化的硬件偏好保持兼容。

AV1 软件解码使用构建时静态链接的 `libdav1d`（也兼容包含 `libaom-av1` 的同 ABI 构建）；硬件路径单独选择 FFmpeg 的 `av1` 或 Android 的 `av1_mediacodec`。FFmpeg 内置 `av1` 本身不能执行软件解码，不能把成功打开它视为软件回退成功。旧原生库未包含这些软件解码器时，会明确提示重建；仅更新 C# 代码无法补齐 AV1 解码能力。按 `Tools/FFmpeg` 重建对应平台库后，重启已加载旧库的 Unity Editor / Player。

项目原生库的 AV1 8-bit / 10-bit 软件路径覆盖 Windows x86/x64、Linux x64、Android ARMv7/ARM64、macOS x64/ARM64 和 iOS ARM64；构建脚本也支持 iOS 模拟器 ARM64/x64。10-bit 解码后仍通过现有 RGBA32 软件上传路径显示，不代表 HDR 输出或完整保留 10-bit 显示精度。各目标的实际运行范围见 [验证记录](../../../../Tools/Tests/FFmpegValidation/RESULTS.md)。

## 日志与性能记录

日志统一通过 `MajDebug` 输出，tag 固定为 `FFmpeg`，message 以 `[Player]`、`[Decoder]`、`[Session]` 或 `[Interop]` 标识组件。Debug 记录控制、seek 与资源生命周期；Info 记录视频编码、实际解码器、硬件 API/设备描述及纹理传输路径；Warning 记录回退及原因；Error 记录打开或播放失败。不会为每帧输出日志。日志落盘与过滤沿用项目的 `MajDebug.SetLogWriter` / `MinLogLevel` 配置。

设备描述区分 Unity 渲染 GPU 和解码设备：共享路径会注明是否匹配 Unity，独立硬件解码设备标为 FFmpeg/系统默认设备，不能将渲染 GPU 型号当成已查询到的独立解码设备型号。Android 的 `h264_mediacodec` 等名称是 FFmpeg 包装器名称；厂商 OMX/C2 组件名未通过其公共 API 暴露。`DecoderType=Hardware` 表示正在使用平台硬件解码后端；VideoToolbox 对 HEVC/ProRes 等编码允许系统内部选择实现，不能仅据后端类型断言所有编码都在物理硬件上执行。

启用 Unity Profiler 时，`UnityProfiler` 记录 `FFmpeg.Player.Update`、`Present`、`CpuUpload`，以及解码打开、读帧、seek、硬件帧下载、RGBA 转换和原生图像导入等作用域。调用处不再添加外围宏，由 `UnityProfiler` 与 Unity Profiler API 自身的 `Conditional` 控制采样调用。工作线程按 [Unity 的线程采样接口](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Profiling.Profiler.BeginThreadProfiling.html) 注册为 `FFmpeg / Decoder`，并在退出时注销；其记录可在 Profiler Timeline 查看。这些计时覆盖 CPU 调用，不代表 GPU 命令执行时长。

播放器会话按队列容量加 2 预分配呈现帧容器，供工作线程、队列和主线程流转；帧释放原生资源后归还本会话。稳定解码、RGBA 转换和呈现使用复用容器，`EAGAIN` 比较不装箱，Android 图像释放委托只在建立硬件会话时创建。这里的无分配指预热后的托管 GC 分配；FFmpeg 原生帧引用、像素缓冲区及驱动内部仍按各自生命周期分配和释放。

公开 `FFmpegVideoDecoder` 构造器与 `DecodedVideoFrame.CopyToSoftware()` 保留独立帧对象的所有权语义，返回对象仍会分配。内部复用帧不通过播放器事件暴露，避免已释放的外部旧引用影响后来租用的帧。打开、seek 日志、回退、纹理重建及调用方事件处理器不在稳定逐帧零 GC 范围内。

## 图形后端与纹理传输

五种请求的图形后端均有 Unity 纹理上传通用路径。硬件路径在运行时探测；`TransferMode` 反映实际路径。需要保证显示帧不经过 CPU 像素回读/上传时，请在 Preload/Prepare 前设置 `player.RequireHardwareDecoding = true`：这时驱动、格式或插件不满足条件会直接报错，不会自动上传 CPU 像素。FFmpeg 打开输入时仍可能为获取元数据执行软件探测解码，这些探测帧不用于显示。

| 后端 | 通用播放 | 可选原生路径 |
| --- | --- | --- |
| D3D11 | RGBA 上传 | 同 Unity device 的 D3D11VA 解码，视频处理器在 GPU 上转为 RGBA，无 CPU 回读；有 GPU 转换，不是严格零拷贝 |
| D3D12 | RGBA 上传 | Windows：优先在 Unity 的 D3D12 device 上进行 D3D12VA 解码，视频处理队列转为 RGBA，再由 Unity 队列复制至显示纹理；不支持时尝试同适配器 D3D11VA 共享路径；均无 CPU 回读 |
| OpenGL / OpenGL ES | RGBA 上传 | Windows OpenGL Core：WGL_NV_DX_interop2 共享 D3D11VA 转换后的 RGBA 纹理；Linux/Android 可使用 VAAPI/MediaCodec 硬件解码后 CPU 上传 |
| Vulkan | RGBA 上传 | Windows/Linux/Android：优先 Vulkan Video，在 Unity 的 VkDevice 上解码，直接采样 AVVkFrame 的 YUV 图像并在 GPU 上转换至显示纹理。不支持时依次尝试平台共享路径：Windows D3D11VA、Linux VAAPI DMA-BUF、Android MediaCodec AHardwareBuffer；均无 CPU 像素回读 |
| Metal | RGBA 上传 | VideoToolbox 的 NV12 CVPixelBuffer 通过 CVMetalTextureCache 零拷贝映射平面；着色器再在 GPU 转为 RGBA |

Metal 的平面映射和 Windows OpenGL 的纹理映射不复制像素，但最终 RGBA 显示包含 GPU 转换。Windows 四条硬件路径都避免 CPU 像素回读/上传；D3D12、Vulkan 仍有 GPU copy，不能将其称为严格的端到端零拷贝。外部纹理必须保留帧引用直到 GPU 完成；原生桥接承担此生命周期，不应自行释放返回的 Texture。

非严格模式下，共享不可用时仍先保留硬件解码：D3D11VA/VAAPI/VideoToolbox 在工作线程下载原始硬件帧并转为 RGBA；Android 重开无 Surface 的 MediaCodec 硬件 ByteBuffer 输出。Unity 主线程只上传已准备的 RGBA。该路径报告 `Hardware decode + CPU RGBA upload`，确实包含 CPU 像素传输；它失败后才改用 `Software RGBA upload`。恢复会保留播放/暂停状态与可 seek 输入的时间位置，输入打开及 I/O 仍在工作线程执行。

Windows/Linux/Android 的 Vulkan 桥接必须保持 PluginImporter 的 **Preload** 开启，以便在 Unity 创建设备前协商视频队列、扩展和功能；Apple 桥接使用显式注册。替换原生库或更改该设置后重启 Editor。Windows/Linux/Android 桥接 ABI 为 **4**，Apple 保持 **2**。D3D12 需要 Unity D3D12 V8 插件接口，原生解码要求设备支持相应视频解码与视频处理格式；OpenGL 需要 WGL_NV_DX_interop2；Windows Vulkan 的 D3D11VA 共享回退需要 Win32 外部内存与 keyed mutex。

Vulkan Video 路径要求 Vulkan 1.3、timeline semaphore、synchronization2、YCbCr 采样、对应编码的 video decode 扩展，以及可供 FFmpeg 独立使用的队列。插件保留 Unity 原有功能链，为 FFmpeg 分配独立队列，并用解码帧的 timeline semaphore 同步显示与帧复用。若驱动不接受扩展后的设备创建请求，会恢复 Unity 原本的设备请求，日志报告原因并尝试平台后端。当前原生 GPU 转换支持单图像 NV12/P010/P016 SDR 帧；不支持的布局、格式或 HDR 会触发明确的回退/错误。

原生解码器与图形 API 分开检测。只有 FFmpeg 编译支持、编码配置、GPU 与驱动同时满足条件才会使用 D3D12VA 或 Vulkan Video，不能由 Unity 使用 D3D12/Vulkan 推断硬解一定可用。原生后端初始化或播放中失败时先尝试上述平台硬解路径；非严格模式再允许硬解加 CPU 上传、最后软件解码。`RequireHardwareDecoding` 允许在无 CPU 像素传输的硬件后端之间回退。`DecoderDevice`、`TransferMode` 和日志会报告实际选中的路径；新路径分别报告 `D3D12VA native decode + GPU conversion (no CPU readback)` 和 `Vulkan Video native decode + GPU conversion (no CPU readback)`。VP8 等没有对应 FFmpeg 硬件配置的编码仍会按既定策略回退。

Linux 路径按 Vulkan 物理设备的 DRM render node 创建 VAAPI 解码设备，使用 `AV_HWFRAME_MAP_READ | AV_HWFRAME_MAP_DIRECT` 导出 DMA-BUF；解码完成同步在工作线程等待，不映射视频像素。需要系统提供对应显卡的 VAAPI 驱动、DRM render node 访问权限，以及 Vulkan DMA-BUF、DRM modifier、foreign queue 扩展。播放器随库打包 libva/libdrm，不打包显卡驱动。目前硬件转换支持 8-bit NV12 SDR BT.601/709；不支持的 P010/HDR 等格式明确失败，严格模式下不回读。

Android GPU 路径需要 API 26+、Vulkan 1.1 与 AHardwareBuffer/外部同步/YCbCr 采样能力。通过 JNI MediaCodec 的硬件 codec 选择器输出到 PRIVATE AImageReader，导入 AHardwareBuffer 和 acquire fence；帧被 GPU 使用完后才归还 reader。API 23–25 仍可加载 FFmpeg，并可尝试 MediaCodec ByteBuffer 加 CPU 上传，但不能开启 AHardwareBuffer 共享。首版 GPU 转换支持 SDR BT.601/709，HDR 不做隐式错误转换。APK 内输入文件需先提取；这只复制编码后的文件，不是解码像素回读。

原生互操作代码在 [Tools/FFmpeg/Native](../../../../Tools/FFmpeg/Native)，附有 Unity `PluginAPI` 和 Vulkan 头文件及许可。平台编译与实际设备验证范围见下方测试结果；尤其 Android/Linux Vulkan 的设备兼容性不能由交叉编译结果代替。另见 [Unity 外部纹理文档](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Texture2D.CreateExternalTexture.html) 和 [FFmpeg 硬件帧接口](https://ffmpeg.org/doxygen/trunk/hwcontext_8h.html)。

## 平台与输入边界

- Windows x86 / x64、Linux x64、Android ARMv7 / ARM64、macOS x64 / ARM64、iOS ARM64；使用各目标的原生库及正确 PluginImporter 设置。当前 Apple 产物最低 macOS 11 / iOS 15，构建需要 macOS 与 Xcode SDK。
- Android APK/JAR 中的 StreamingAssets 不是普通文件：先通过 UnityWebRequest 复制到 `persistentDataPath` 再打开。普通本地路径、`file://` 和 FFmpeg 构建启用的 URL 协议可以使用；HTTPS 能力取决于构建的 TLS 后端。
- 默认 I/O 超时 15 秒，探测大小、分析时长和图像大小均有上限。销毁对象通过 AVIO interrupt 中断阻塞 I/O；第三方协议若不检查该回调，工作线程仍需等它返回。
- 软件路径处理 BT.601 / BT.709 / BT.2020 矩阵和 full/limited range，输出 RGBA8；支持常见 90° 倍数旋转。没有 HDR 色调映射、字幕、DRM 或音轨输出。
- `BGManager` 使用此组件预载和播放谱面背景视频，通过 `TextureChanged` 更新 UI 纹理；保留原有音频同步方式。

## Camera 录制

`FFmpegCameraCapturer` 录制指定 Camera 的视频画面，无音频。它在 URP 显示输出中使用 `CameraCaptureBridge`，显式 RenderTexture 输出在整个 SRP rendering context 结束后复制，在 Built-in Render Pipeline 中使用 Camera 的 post-render 回调，不改变 Camera 的 `targetTexture` 或 `rect`。其他 SRP 的显示输出需要执行 `CameraCaptureBridge` 的捕获动作；显式 RenderTexture 输出需要触发 SRP end-camera 和 end-context 回调；URP Camera Stack 捕获 Base Camera 对应的最终合成画面。Screen Space Overlay Canvas 不属于 Camera 输出；需要录入 UI 时使用 Screen Space Camera 或 World Space Canvas。

```csharp
using MajdataPlay.FFmpeg;

var capturer = camera.gameObject.AddComponent<FFmpegCameraCapturer>();
capturer.TargetCamera = camera;
capturer.Options = new EncoderOptions
{
    Width = 1920,
    Height = 1080,
    FrameRate = 60,
    PreferredEncoderType = VideoEncoderType.Software,
    MaximumSoftwareThreads = 4,
    Format = VideoEncodingFormat.MPEG4,
    BitRate = 8_000_000,
    MaximumBitRate = 16_000_000,
    RateControlMode = VideoRateControlMode.VBR
};
await capturer.StartRecordingAsync(outputPath, cancellationToken);
// Pause keeps the encoder open and excludes the paused interval from the video.
capturer.PauseRecording();
capturer.ResumeRecording();
// Later, await finalization before reading or moving the file.
await capturer.StopRecordingAsync();
```

所有组件控制 API 在 Unity 主线程调用。编码与封装在一个后台线程执行，GPU 回读和编码共用 1–8 个固定槽位，默认 3 个；容量包含正在回读、排队和正在编码的帧。编码过慢时丢帧，`DroppedFrames` 记录省略的帧率间隔，PTS 保留实际单调时钟间隔，不压缩录制时间；不通过重复帧补齐 CFR。录制尺寸固定且必须为偶数，支持缩放 Camera 输出。需要异步 GPU 回读支持；捕获是 SDR sRGB RGBA8，转换为 limited-range YUV，矩阵/原色为 BT.709，transfer metadata 为 sRGB，没有 HDR tone mapping。

`FrameRate` 是采样上限和时间戳的时间基准。游戏 120 FPS、录制 30 FPS 时，约每四次 Camera 渲染采一帧；游戏 30 FPS、录制 60 FPS 时，只能获得约 30 个真实画面/秒，相邻 PTS 跨两个目标时间槽，视频保持实际经过时间。低帧率、渲染卡顿或缓冲池满会降低流畅度；CBR/VBR 是编码器采用的控制模式，缺帧和画面复杂度仍可能使实际码率偏离目标，应观察 `CurrentBitRate`。

`PauseRecording()` 停止新采集并冻结录制时钟，保留同一个输出文件、编码器和已提交缓冲区。暂停前的 GPU 回读与编码仍可排空，因此刚暂停时统计可能继续变化；恢复不把暂停时长计入视频 PTS 或丢帧计数。录制时钟不受 `Time.timeScale` 影响。Unity Editor 工具栏的 Pause 不会自动调用录制暂停，会留下真实时间间隔；需要排除这段时间时先使用 capturer Inspector 的 Pause。

`FFmpegCameraCapturerEditor` 在单选场景组件的 Play Mode Inspector 中提供 Start Recording、Pause/Resume 和 Stop Recording。开始时选择新文件路径，停止时异步等待排空和文件尾写入。面板显示实际编码器、软件/硬件、格式、CBR/VBR、当前/目标/最大码率、软件线程数、尺寸、提交帧数、丢帧、时长、输出路径及错误/回退原因；配置修改在下次录制生效。帧率分别显示下一次配置、当前会话时间基准、成功提交帧率和游戏帧率，后两者使用约 0.5 秒窗口估算；队列追赶时提交速率可能短暂高于目标。当前码率按最近约 1 秒媒体时间计算，暂停保留读数，不表示暂停期间仍持续产生该墙钟吞吐量。多选仅编辑配置，Prefab 资产不提供录制控制。

`Options` 在开始时复制。修改选项、Camera、缓冲容量或 `ReadbackOrientation` 在下次录制应用；当前编码器身份与模式不会随偏好变化。`ReadbackOrientation.Automatic` 按 Unity Blit 捕获纹理的 bottom-first 行序解释；自定义渲染管线输出方向不同时可以明确选择 `TopFirst` / `BottomFirst`。

| API | 语义 |
| --- | --- |
| `StartRecordingAsync(path, token)` | 创建新的本地文件，扩展名选择容器；已有文件不覆盖；完成时已打开编码器并开始捕获 |
| `PauseRecording()`, `ResumeRecording()` | 暂停/恢复当前会话；暂停时长不进入视频；不在对应状态时不做操作，失效或取消的会话不会恢复 |
| `StopRecording()` | 停止接收新帧，异步排空，不阻塞 Unity 主线程 |
| `StopRecordingAsync()` | 等待 GPU 请求、编码延迟包、文件尾和资源释放；编码/写入失败会抛出异常 |
| `State`, `IsRecording`, `IsPaused`, `CanStartRecording`, `OutputPath`, `LastError` | 生命周期和诊断；CanStartRecording 表示组件启用且旧会话/GPU 资源已关闭，不验证设置和路径 |
| `EncoderName`, `EncoderType`, `RateControlMode` | 实际 AVCodec、实际软件/硬件后端、实际 CBR/VBR；初始化前为 null |
| `EncodingFormat`, `Width`, `Height`, `FrameRate` | 当前或最近会话的固定录制格式/尺寸/时间基准 |
| `BitRate`, `MaximumBitRate`, `SoftwareThreadCount` | 会话目标、有效最大码率（bit/s）、实际 FFmpeg 软件 codec worker 数；硬件为 0 |
| `CurrentBitRate`, `BytesWritten`, `EncodedFrames` | 最近约 1 秒媒体时间的视频压缩码率、压缩字节数、成功提交帧数；不包含容器开销 |
| `HardwareFallbackReason`, `DroppedFrames`, `TimeSeconds` | 硬件偏好回退原因、丢帧计数、捕获持续时间（排除初始化、录制暂停和停止排空） |
| `Started`, `Stopped`, `ErrorReceived` | 主线程事件；Stopped 表示资源关闭，错误状态需同时检查 LastError |

CBR 设置目标码率以及相同的 min/max；VBR 使用独立目标和最大码率。VBV 后端通过约 1 秒缓冲限制，单帧和短时窗口仍会波动；libvpx/libaom 的 CBR 和单遍 VBR 均使用原生码率预算，复杂画面可能超出预算，也不保证低复杂度画面填满目标码率，`MaximumBitRate` 不表示每帧或瞬时负载硬上限。软件线程数限制 FFmpeg codec worker，不包含应用采集线程和驱动的辅助线程。硬件偏好仅回退到**同格式**的软件实现，不会偷偷改换编码格式；不支持请求的模式/码率约束时明确失败。

各平台构建配置包含 `libx264`（H.264）、`libx265`（HEVC/H.265）、`libvpx-vp9`（VP9）、`libaom-av1`（AV1）和 MPEG-4 Part 2 软件编码，以及录制 muxer；均支持选择 CBR/VBR。H.264 CBR 可以写入 MP4，x265 打开时检查参数是否被接受。构建配置、依赖许可及 NVENC/AMF/VAAPI/VideoToolbox 条件见 [Tools/FFmpeg](../../../../Tools/FFmpeg/README.md)。启用 x264/x265 后，FFmpeg 产物使用 GPLv3 或后续版本许可。历史播放专用或仅 MPEG4 的原生库仍需要替换，并重启已加载旧库的 Editor/Player。AMF 必须包含构建脚本应用的码率检查补丁，x265 必须包含参数检查补丁：原生配置中的能力标记必须匹配当前补丁；旧未检查的实现会明确拒绝。

iOS Player 的固定 VideoToolbox 包装器不能检查硬件独占选择，硬件偏好会明确回退到同格式软件编码并设置 `HardwareFallbackReason`，避免把系统选择结果误报为硬件。macOS 的 H.264 VideoToolbox 仍可强制硬件。

取消 token 作用于整个录制会话；取消或 I/O 错误可能留下未完成文件。正常停止、禁用和销毁会停止新捕获，并保留已提交 GPU 请求所需纹理直到回读结束，再释放后台资源。旧 GPU 请求尚未释放时会拒绝重启，以免连续取消累积多份缓冲池；先等待 `StopRecordingAsync()`，取消时也需等待该 Task 结束并处理取消异常。需要确保文件可播放时显式等待正常的 `StopRecordingAsync()`，不要在主线程同步等待 Task。硬件编码也经 CPU RGBA 回读/转换/上传，不能把 Hardware 状态解释为 GPU 零拷贝录制。

可独立使用 `FFmpegVideoEncoder` 编码 sRGB RGBA8 缓冲，但其 `Open` / `Encode` / `Complete` / `Dispose` 必须在同一个 owner 线程运行，调用 `Complete()` 后才有完整的输出文件。Mono/IL2CPP 均通过静态 AOT 回调写入 seekable 本地 FileStream，避免 Unicode 路径和覆盖已有文件的问题。

运行时程序集新增对已在项目内使用的 `Unity.RenderPipelines.Core.Runtime` 的引用，用于 URP 正确的最终 Camera 颜色捕获通道；没有新增或升级 Package Manager 包。

## 验证

[Tools/Tests/FFmpegValidation](../../../../Tools/Tests/FFmpegValidation) 包含真实 FFmpeg 解码/seek/EOF/取消/有界队列测试，以及隔离 Unity Player 验证脚本。构建成功、Player 启动成功、纹理互操作通过是不同验收项；实际结果见 [RESULTS.md](../../../../Tools/Tests/FFmpegValidation/RESULTS.md)。其他 OS 和移动设备的图形/驱动行为需要在目标设备复验。
