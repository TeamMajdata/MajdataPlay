# FFmpeg 播放器验收

这些测试使用真实 FFmpeg 库、Unity 程序集及项目的 Diagnostics/ZString 源码，不使用模拟解码器或日志桩。运行前先构建 `Tools/FFmpeg`。

## 托管与真实解码

```powershell
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4"
```

需要 .NET 9 SDK，默认 Unity Editor 路径为 `C:/Program Files/Unity Editors/6000.3.17f1/Editor`；其他安装路径可用 `-p:UnityEditor=...`。此命令在 Windows x64 进程中运行，新增硬件下载检查需要支持 H.264 D3D11VA 的实际 GPU；不带两个参数只运行不依赖 native 的时钟、码率统计与帧池生命周期/分配测试。.NET 工程编译真实 Diagnostics/ZString 并引用项目 PolySharp 分析器，不定义 `ENABLE_PROFILER`，因而不调用 Unity 原生 profiler；日志保留在真实 MajDebug 队列，未调用 Unity 初始化或连接未初始化的 Unity logger。

覆盖：真实视频元数据、RGBA 解码、PTS、前后 seek、EOF 延迟帧排空、预取消、有界预载、连续 seek、快速关闭，以及人工 AVFrame 的像素级上下方向、四方向旋转、非方形尺寸、YUV limited/full range、动态像素格式、裁剪与超限拒绝。硬件测试创建独立 D3D11VA 设备，在没有 Unity 纹理互操作回调的情况下解码、下载 RGBA、跳转并检查真实像素，断言 `HardwareDecoded` 与 CPU 像素存储同时成立。

原生后端回退测试检查 H.264 的 D3D12VA/Vulkan 硬件配置，并注入首选设备获取失败，验证严格 GPU 模式和允许 CPU 上传模式均可尝试下一硬件后端、得到正确的实际设备身份并继续 seek。此测试验证回退链，不代表已在对应原生后端完成视频解码。

### 每帧托管分配与帧所有权

```powershell
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4" --allocations
```

`--allocations` 用真实 FFmpeg 库分别验证人工 RGBA/YUV 帧转换、软件解码、D3D11VA 下载并转换为 RGBA，以及 D3D11VA 原生帧。先正向校验 `GC.GetAllocatedBytesForCurrentThread()` 能观测已知数组分配，再对每条路径预热并断言一批帧的托管分配总量为 **0 B**；转换测量 512 帧，解码测量最多 120 帧。素材需可 seek、至少含 64 帧且长于 2 秒，以覆盖会话压力检查。没有支持该素材的 D3D11VA GPU 时，可改用 `--allocations-software` 只运行转换和软件解码；该模式也可使用 AV1 素材。

不带参数的默认测试检查预分配帧池的固定容量、同时持有的帧互不别名、重复 Dispose、释放回调抛异常后归还、跨线程归还、元数据重置与 4096 次租用/归还的 0 B 分配。真实解码测试额外检查解码器关闭后尚未归还的 CPU/原生帧仍可释放、归还后池容量完整，以及公开解码器返回的独立帧不会复用旧对象，因此迟到的重复 Dispose 不会影响后续帧。容量 1/8 的实际会话还覆盖持有显示帧时持续取帧、连续 seek、关闭与释放。播放器内部池化帧遵循独占租用约定：Dispose 后不可继续使用旧引用。

测量只覆盖调用线程的稳态托管分配，不包含初始化、seek 调用、错误诊断、native 缓冲区分配或 Unity 渲染线程。此 .NET 9 结果不能替代 Unity Mono/IL2CPP 的 Profiler 与目标平台验证；启用 Profiler 后应继续检查 `ReadFrame`、`PreparePresentationFrame` 和实际呈现路径的 `GC.Alloc`。

### AV1 软件解码与硬件不可用回退

使用独立 FFmpeg 命令行生成 3 秒的 AV1 测试素材，再用项目的真实原生库运行专用验证；素材和日志留在忽略的 `.work/`：

```powershell
New-Item -ItemType Directory -Path Tools/Tests/FFmpegValidation/.work/av1 -Force | Out-Null
ffmpeg -hide_banner -y -f lavfi -i 'testsrc2=size=128x96:rate=30:duration=3' `
  -an -c:v libaom-av1 -cpu-used 8 -crf 40 -g 30 -pix_fmt yuv420p `
  Tools/Tests/FFmpegValidation/.work/av1/test-av1.mp4
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/FFmpegValidation/.work/av1/test-av1.mp4 --av1
```

生成素材的命令行工具需要 `libaom-av1` 编码器；验证加载的项目 DLL 需要 `libdav1d` 或 `libaom-av1` 软件解码器。仅有名为 `av1` 的原生解码器不能提供 CPU 软件解码。也可替换为大于 2 秒、可 seek 且画面非均匀的 AV1 素材。

验证高位深解码时，将生成命令的 `-pix_fmt` 改为 `yuv420p10le`，输出另存为 `test-av1-10bit.mp4`，再对该文件运行相同 `--av1` 命令；8-bit 和 10-bit 均需通过。

`--av1` 跳过要求真实 H.264 硬解的测试，覆盖实际 AV1 软件解码器身份、非均匀 RGBA 像素、PTS、前后及末尾 seek、EOF、取消、有界会话和快速关闭。另注入 D3D11VA 设备获取失败，检查允许回退时重新选择软件解码器并继续解码/seek，严格 GPU 模式则拒绝软件回退。测试检查原生 `av1` 候选仍暴露 D3D11VA/D3D12VA/Vulkan 配置，并检查设备获取回调确实被调用，防止默认解码器改为 `libdav1d` 后丢失硬件候选。

此专用模式不需要支持 AV1 的 GPU；通过不代表真实 AV1 硬件解码或 Unity 纹理显示已验证。Unity 显示可继续用 `run-unity.ps1 -Media <AV1素材>` 验证软件模式，以及 `-Hardware` 验证播放器的实际硬件尝试/软件回退；必须检查报告中的实际解码器和传输方式。

可用独立工作目录避免与其他隔离 Unity 验证占用同一工程；`-WorkDirectory` 接受绝对路径或相对仓库根目录的路径，省略时仍使用原 `.work/`：

```powershell
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics vulkan `
  -Hardware -Media Tools/Tests/FFmpegValidation/.work/av1/test-av1.mp4 `
  -WorkDirectory Tools/Tests/FFmpegValidation/.work/av1/unity
```

保留旧版未含 AV1 软件库的 DLL 时，可将第三个参数换成 `--av1-unavailable`，检查直接软件模式及硬件设备初始化失败后，都在 `Open` 阶段以包含 `libdav1d`/`libaom-av1` 的明确错误拒绝。此模式只验缺失能力时的诊断，不代表 AV1 播放通过；正常 `--av1` 模式始终要求真实解码成功。

## Inspector 码率

`Bitrate (current)` / `CurrentBitRate` 根据最近约 1 秒媒体时间内的视频压缩包字节数估算，单位为 bit/s（界面自动换算 bps/kbps/Mbps）。数据随实际显示帧更新，后台预读不提前改变读数，倍速不乘码率，暂停保留当前值，seek/关闭清零。`Bitrate (average)` / `BitRate` 保留 FFmpeg 报告的视频流平均码率；未知值显示 `Unknown`。

统计不包含音频、容器或网络协议开销。窗口不足 1 秒时使用已有媒体跨度；窗口边缘按数据包持续时间比例分摊字节。缺少 PTS 时使用 DTS，两者均缺少或包时长未知时按帧率估算时间，因此实时值是局部估计。固定容量统计覆盖乱序 PTS、预读到的未来帧、VFR 和 seek；极端输入超过容量时暂报未知，待不完整窗口移出后恢复。测试检查这些计算边界，以及真实软件/硬件帧携带码率、跳转后历史重置与末帧保留。

## CPU Profiler

在开启 Profiler 的 Editor 或 Development Player 中，CPU Timeline 的 `FFmpeg / Decoder` 工作线程包含：

- `FFmpeg.Decoder.Software.SendPacket` / `ReceiveFrame` / `Drain`：软件解码调用。
- `FFmpeg.Decoder.Hardware.SendPacket` / `ReceiveFrame` / `Drain`：硬件解码调用；硬解加 CPU 上传也归入此组。
- `FFmpeg.Decoder.ReadPacket`：解封装和输入读取；`DownloadHardwareFrame`：硬件帧下载。
- `FFmpeg.Decoder.ScaleRGBA` / `CopyRGBA`：颜色转换，以及翻转/旋转和像素拷贝。

解码工作可能发生在送包和取帧两处。硬解标记包含提交及等待的 CPU 耗时，不代表 GPU 异步解码时长；一次调用也不一定产出一帧。`ReadFrame` 保留作为读取到可呈现帧的总耗时。

主线程的 `FFmpeg.Interop.TryPresent` 下可分别查看 `PrepareD3D11` / `PrepareD3D12VA` / `PrepareVulkan` / `PrepareMetal`、`RecordCommands` 和 `SubmitCommands`。`PrepareD3D11` 包含 D3D11 解码经其他图形后端共享的路径。比较稳定播放时的样本，单独看首帧或纹理重建时的 `CreateCopyTarget`，避免将初始化成本算入每帧成本。

## Unity Player

```powershell
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d12 -SkipBuild
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics vulkan -SkipBuild
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics glcore -SkipBuild
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -Hardware -SkipBuild
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics d3d11
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x86 -Graphics d3d11
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics d3d12 -RequireHardware
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics vulkan -RequireHardware -SkipBuild
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics glcore -RequireHardware -SkipBuild
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -TestRecovery
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -HardwareCpuUpload
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -TestDecoderPreference -SkipBuild
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture arm64
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture armv7
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Linux -Backend Mono -Architecture x64 -Graphics glcore
```

隔离项目、构建输出、日志、结果、32×32 纹理读回图均保存在忽略的 `.work/`。脚本不修改主项目场景或 Player Settings。测试包括首帧预载、实际纹理像素、播放、倍速、暂停、seek、步进、停止回零、连续 seek、循环、关闭/取消。图形测试会启动隐藏的 Player，GPU 后端仍需本机驱动支持；不能加 `-nographics`。`-Hardware` 是尝试硬件路径，必须检查报告中的 `TransferMode` 与 fallback，软件回退成功不代表硬件互操作成功。`-RequireHardware` 隐含 `-Hardware` 并增加硬件路径断言，发生软件回退即失败；首次使用该选项需要重新构建包含新断言的测试 Player。

硬件呈现测试还会在完成像素读回后主动释放输出纹理及存在的 GPU copy target，再步进一帧并检查恢复后的实际像素，以覆盖原生纹理指针缓存失效后的重建。

`-Media` 可替换素材，需使用可 seek、时长大于 2 秒、首秒处有非均匀画面的文件。默认使用项目的 H.264 背景视频。`-SkipBuild` 只用于相同后端、架构和最新源码已完成构建的情况。

Windows D3D12/Vulkan 可添加 `-RequireNativeDecoder`，在严格 GPU 模式之外检查实际 `DecoderDevice` 和 `TransferMode` 确实使用 D3D12VA/Vulkan Video；回退到 D3D11VA 会失败，不能冒充原生解码通过。原生后端的设备限制和本机验证结果见 RESULTS。Windows 的 `-NativeDirectory <包含八个DLL的目录>` 可在隔离工程中测试刚编译的库，同时保留项目的 PluginImporter 设置。

`-TestRecovery` 隐含 `-RequireHardware`，通过反射注入托管层的 GPU 失败通知，不损坏驱动资源。它检查纹理清空回调中的 Pause、SeekAsync、Close，以及终止错误后的重新 Play；报告保存为独立 `*-recovery.*`。日志中的 `Expected recovery test terminal failure` 是该测试主动触发的预期错误，以最终断言报告判断结果。

`-HardwareCpuUpload` 设置硬件解码偏好和 `PreferNativeTextures=false`，在完整控制/像素测试中强制检查实际 `DecoderType=Hardware` 且传输方式为 `Hardware decode + CPU RGBA upload`。`-TestDecoderPreference` 通过 `PreferredDecoderType` 选择软件或硬件解码，检查修改偏好不会改变已打开会话的实际解码器身份、重新打开后的选型，以及原生纹理失败后保留硬件解码、转 CPU 上传继续播放和跳转。后者通过 `RecoverHardwarePlayback(Exception)` 注入真实恢复流程的失败通知。报告分别为 `*-hardware-cpu.*` 和 `*-decoder-preference.*`；这两个选项不能与禁止 CPU 上传的 `-RequireHardware` 同时使用。

隔离 Unity 工程会复制项目的 Diagnostics、完整 ZString、Unsafe 6.1.2 程序集与 PolySharp Roslyn 分析器，保留 `.asmdef` GUID 和 `csc.rsp`，并引入 ZString 引用的 UGUI/TextMeshPro 包。实际 MajDebug 日志写入报告旁的 `.diagnostics.log`，测试检查其中有解码器、设备和传输模式记录；Android runner 也会拉回该日志。首次新增这些依赖或修改测试源码后必须重新构建，不能使用旧 Player 的 `-SkipBuild` 结果。

Android 分支构建 APK，并检查七个 FFmpeg `.so` 和 GPU 桥接的存在、目录、ELF 位数/机器架构。随后连接 API 26+ Vulkan 真机运行：

```powershell
./Tools/Tests/FFmpegValidation/run-android-player.ps1 -Apk ./Tools/Tests/FFmpegValidation/.work/Android-arm64-IL2CPP/VideoSmoke.apk -Serial <adb设备编号> -Adb <adb.exe路径>
```

测试 APK 自动提取编码后的 StreamingAssets 文件，先开启严格硬件模式，播放至少 300 帧并连续 seek 10 次；随后关闭严格模式，验证软件/硬件偏好、MediaCodec ByteBuffer 的 CPU 上传、共享失败恢复与日志，最后重新打开原生 GPU 路径。素材应长于 12 秒。测试期间请保持应用未被冻结、屏幕解锁且在前台。`-SkipInstall` 可直接复用已安装的同一 APK。脚本仅安装/更新专用包 `net.majdata.ffmpegplayer.validation`，结果保存在 APK 旁的 `vulkan-hardware-device.txt`。测试中的 `ReadPixels` 仅验证输出；播放器的 GPU 共享路径不调用它。

Android AV1 软件验证可用同一 APK 先后运行 8-bit 和 10-bit 素材，无需为换素材重建；每种 ABI 仍需分别构建/安装。以下可选入口通过 Intent extras 关闭硬件和长时间压力模式，复用普通播放/暂停/seek/循环/纹理像素检查，并额外断言 `av1`、`libdav1d`、软件解码与 RGBA 上传。默认硬件验证流程保持不变。

```powershell
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture arm64 -Graphics vulkan
./Tools/Tests/FFmpegValidation/run-android-player.ps1 -Apk Tools/Tests/FFmpegValidation/.work/Android-arm64-IL2CPP/VideoSmoke.apk `
  -Av1Software -Media Tools/Tests/FFmpegValidation/.work/av1/test-av1.mp4
# 保存上一轮证据后复用同一安装：仅更换测试应用自身 files 目录里的素材。
./Tools/Tests/FFmpegValidation/run-android-player.ps1 -Apk Tools/Tests/FFmpegValidation/.work/Android-arm64-IL2CPP/VideoSmoke.apk `
  -SkipInstall -Av1Software -Media Tools/Tests/FFmpegValidation/.work/av1/test-av1-10bit.mp4
```

将 `arm64` 改为 `armv7` 可验证另一个 ABI。报告、真实 Diagnostics 日志及纹理读回图保存在 APK 旁的 `av1-software-device.txt`、`.diagnostics.log`、`.png`；下一次运行会覆盖，需按位深另存证据。报告记录实际图形 API，软件模式允许 Vulkan 或 OpenGLES3，不能把 OpenGLES3 成功记为 Vulkan 验证。原生 `Av1SoftwareSmoke.c` 检查实际像素格式位深，此 Unity 检查额外覆盖 IL2CPP 绑定、生命周期与真实纹理内容；两层结果应分别记录。

Linux 分支从 Windows Editor 构建独立 Linux x64 Mono Player，需要安装 Linux Build Support。随后在 Linux 桌面或 WSLg 中运行：

```bash
bash Tools/Tests/FFmpegValidation/run-linux-player.sh glcore
bash Tools/Tests/FFmpegValidation/run-linux-player.sh vulkan
bash Tools/Tests/FFmpegValidation/run-linux-player.sh vulkan /absolute/path/to/Linux-Player --hardware
```

脚本清除 `LD_LIBRARY_PATH`，保留实际 GPU 渲染和像素检查，不能使用 `-nographics`。第二个参数指定 Player 目录，第三个参数 `--hardware` 强制 VAAPI/DMA-BUF 路径。需要对应 GPU 的 VAAPI 驱动和 DRM render node。WSLg 软件光栅器上的通过记录不能替代 Linux 物理 GPU 验证；当前本机 Unity 拒绝 llvmpipe Vulkan 设备，具体限制保留在 RESULTS 中。

Win32 IL2CPP 使用 Debug C++ 配置，原因是项目已有 FFmpeg.AutoGen 验证中记录的本机 Unity 6000.3.17f1 / MSVC 14.51 Release 引擎初始化崩溃；Win64 IL2CPP 使用 Release。所有 Player 都开启 High managed stripping。

本机执行结果另见 [RESULTS.md](RESULTS.md)。未执行的目标必须保留“未验证”，不能从托管编译或原生库构建成功推断设备播放成功。

## 原生库加载与真实解码

### 跨平台 AV1 8-bit / 10-bit 软件解码

[Av1SoftwareSmoke.c](Av1SoftwareSmoke.c) 是不依赖 Unity 或 GPU 的 C11 验证程序，可用各目标编译器链接固定 FFmpeg 9.0.1 的头文件与库。它一次接收两个素材，依次要求实际解码为 8-bit 和 10-bit；可复用上方生成的 `test-av1.mp4`、`test-av1-10bit.mp4`。素材需可 seek、长于 2 秒且每帧画面非均匀。

```bash
# Linux 示例：prefix 必须来自当前 Tools/FFmpeg 构建，不能使用系统 FFmpeg。
ffmpeg_prefix=/absolute/path/to/pinned/ffmpeg/prefix
cc -std=c11 -Wall -Wextra -Werror \
  -I"$ffmpeg_prefix/include" Tools/Tests/FFmpegValidation/Av1SoftwareSmoke.c \
  -L"$ffmpeg_prefix/lib" -Wl,-rpath,"$ffmpeg_prefix/lib" \
  -lavformat -lavcodec -lswscale -lavutil -lm -o /tmp/av1-software-smoke
/tmp/av1-software-smoke \
  Tools/Tests/FFmpegValidation/.work/av1/test-av1.mp4 \
  Tools/Tests/FFmpegValidation/.work/av1/test-av1-10bit.mp4
```

程序显式打开 `libdav1d`，检查编译头文件与加载库的版本一致、真实 AV1 帧的像素格式组件位深、无硬件 context、RGBA 像素变化和单调 PTS。它读完整视频并排空延迟帧、确认 EOF 稳定，向前 seek 到视频中点，再回零解码，要求完整帧数、PTS 范围和 RGBA 校验和重现。每个位深独立打印 `PASS`、帧数、实际 seek 时间、像素范围、校验和及检查数；任一素材失败退出码为 1，参数错误为 2。

Windows 使用对应 x86/x64 MinGW 编译器和 import libraries，运行时选择同架构 DLL 目录；macOS 使用对应架构 clang 和 dylib。iOS Simulator 可将程序链接到对应 simulator 的静态库后通过 `simctl spawn` 运行，`libavcodec.a` 须为 stage 后已合并 dav1d 的归档，并附加 FFmpeg 所需的 Apple frameworks/system libraries。iOS 设备需要签名的可运行测试载体，不能把 simulator 成功记作设备成功。

Android 使用对应 ARMv7/ARM64 NDK 编译器，保持与原生库相同 API/ABI；将测试程序、两个素材及同架构 `.so` 推到独立 `/data/local/tmp/` 测试目录，通过该目录的 `LD_LIBRARY_PATH` 运行，无需安装或替换正式应用。该检查验证原生库的软件解码与像素转换，不验证 Unity 生命周期、托管 ABI、纹理显示或硬件解码；动态库搜索路径的独立验证仍使用下面的 Linux 测试。

### Linux 加载路径检查

```bash
bash Tools/Tests/FFmpegValidation/run-linux.sh
```

需要 Linux x64（也可 WSL）、C 编译器、`readelf` 和 `ldd`。可依次传入绝对库目录和视频路径。测试程序仅链接 `libdl`，不在链接阶段依赖 FFmpeg；首先用绝对路径 `dlopen libavformat.so.63`，由产物自身的 `$ORIGIN` RUNPATH 解析 avcodec/avutil，然后加载其余全部七库。运行时明确移除 `LD_LIBRARY_PATH`，并检查实际加载的 avcodec 来源，避免系统安装或测试环境掩盖缺失依赖。

2026-10-03 本机 WSL 结果：**PASS，144 checks**，H.264 1920×1080 实际解码 30 帧并缩放到 RGBA32，像素亮度范围 0–177，校验和 `ae2450db4c195031`。七个 staged ELF 库均检查 `$ORIGIN`。证据保存到忽略的 `.work/linux-native.txt`；程序源为 [LinuxNativeSmoke.c](LinuxNativeSmoke.c)。这项结果证明 Linux 库的加载、解封装、解码和像素转换，不代表 Linux Unity 图形显示已经实机验证。
