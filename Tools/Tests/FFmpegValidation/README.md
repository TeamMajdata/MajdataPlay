# FFmpeg 播放器验收

这些测试使用真实 FFmpeg 库和 Unity 程序集，不使用模拟解码器。运行前先构建 `Tools/FFmpeg`。

## 托管与真实解码

```powershell
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4"
```

需要 .NET 9 SDK，默认 Unity Editor 路径为 `C:/Program Files/Unity Editors/6000.3.17f1/Editor`；其他安装路径可用 `-p:UnityEditor=...`。此命令在 Windows x64 进程中运行；不带两个参数只运行不依赖 native 的时钟测试。

覆盖：真实视频元数据、RGBA 解码、PTS、前后 seek、EOF 延迟帧排空、预取消、有界预载、连续 seek、快速关闭，以及人工 AVFrame 的像素级上下方向、四方向旋转、非方形尺寸、YUV limited/full range、动态像素格式、裁剪与超限拒绝。

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
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture arm64
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture armv7
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Linux -Backend Mono -Architecture x64 -Graphics glcore
```

隔离项目、构建输出、日志、结果、32×32 纹理读回图均保存在忽略的 `.work/`。脚本不修改主项目场景或 Player Settings。测试包括首帧预载、实际纹理像素、播放、倍速、暂停、seek、步进、停止回零、连续 seek、循环、关闭/取消。图形测试会启动隐藏的 Player，GPU 后端仍需本机驱动支持；不能加 `-nographics`。`-Hardware` 是尝试硬件路径，必须检查报告中的 `TransferMode` 与 fallback，软件回退成功不代表硬件互操作成功。`-RequireHardware` 隐含 `-Hardware` 并增加硬件路径断言，发生软件回退即失败；首次使用该选项需要重新构建包含新断言的测试 Player。

`-Media` 可替换素材，需使用可 seek、时长大于 2 秒、首秒处有非均匀画面的文件。默认使用项目的 H.264 背景视频。`-SkipBuild` 只用于相同后端、架构和最新源码已完成构建的情况。

`-TestRecovery` 隐含 `-RequireHardware`，通过反射注入托管层的 GPU 失败通知，不损坏驱动资源。它检查纹理清空回调中的 Pause、SeekAsync、Close，以及终止错误后的重新 Play；报告保存为独立 `*-recovery.*`。日志中的 `Expected recovery test terminal failure` 是该测试主动触发的预期错误，以最终断言报告判断结果。

Android 分支构建 APK，并检查七个 FFmpeg `.so` 和 GPU 桥接的存在、目录、ELF 位数/机器架构。随后连接 API 26+ Vulkan 真机运行：

```powershell
./Tools/Tests/FFmpegValidation/run-android-player.ps1 -Apk ./Tools/Tests/FFmpegValidation/.work/Android-arm64-IL2CPP/VideoSmoke.apk -Serial <adb设备编号> -Adb <adb.exe路径>
```

测试 APK 自动提取编码后的 StreamingAssets 文件并开启严格硬件模式。除基本 API 与纹理检查外，还播放至少 300 帧并连续 seek 10 次；素材应长于 12 秒。测试期间请保持应用未被冻结、屏幕解锁且在前台。`-SkipInstall` 可直接复用已安装的同一 APK。脚本仅安装/更新专用包 `net.majdata.ffmpegplayer.validation`，结果保存在 APK 旁的 `vulkan-hardware-device.txt`。测试中的 `ReadPixels` 仅验证输出；播放器的硬件路径不调用它。

Linux 分支从 Windows Editor 构建独立 Linux x64 Mono Player，需要安装 Linux Build Support。随后在 Linux 桌面或 WSLg 中运行：

```bash
bash Tools/Tests/FFmpegValidation/run-linux-player.sh glcore
bash Tools/Tests/FFmpegValidation/run-linux-player.sh vulkan
bash Tools/Tests/FFmpegValidation/run-linux-player.sh vulkan /absolute/path/to/Linux-Player --hardware
```

脚本清除 `LD_LIBRARY_PATH`，保留实际 GPU 渲染和像素检查，不能使用 `-nographics`。第二个参数指定 Player 目录，第三个参数 `--hardware` 强制 VAAPI/DMA-BUF 路径。需要对应 GPU 的 VAAPI 驱动和 DRM render node。WSLg 软件光栅器上的通过记录不能替代 Linux 物理 GPU 验证；当前本机 Unity 拒绝 llvmpipe Vulkan 设备，具体限制保留在 RESULTS 中。

Win32 IL2CPP 使用 Debug C++ 配置，原因是项目已有 FFmpeg.AutoGen 验证中记录的本机 Unity 6000.3.17f1 / MSVC 14.51 Release 引擎初始化崩溃；Win64 IL2CPP 使用 Release。所有 Player 都开启 High managed stripping。

本机执行结果另见 [RESULTS.md](RESULTS.md)。未执行的目标必须保留“未验证”，不能从托管编译或原生库构建成功推断设备播放成功。

## Linux 原生库加载与真实解码

```bash
bash Tools/Tests/FFmpegValidation/run-linux.sh
```

需要 Linux x64（也可 WSL）、C 编译器、`readelf` 和 `ldd`。可依次传入绝对库目录和视频路径。测试程序仅链接 `libdl`，不在链接阶段依赖 FFmpeg；首先用绝对路径 `dlopen libavformat.so.63`，由产物自身的 `$ORIGIN` RUNPATH 解析 avcodec/avutil，然后加载其余全部七库。运行时明确移除 `LD_LIBRARY_PATH`，并检查实际加载的 avcodec 来源，避免系统安装或测试环境掩盖缺失依赖。

2026-10-03 本机 WSL 结果：**PASS，144 checks**，H.264 1920×1080 实际解码 30 帧并缩放到 RGBA32，像素亮度范围 0–177，校验和 `ae2450db4c195031`。七个 staged ELF 库均检查 `$ORIGIN`。证据保存到忽略的 `.work/linux-native.txt`；程序源为 [LinuxNativeSmoke.c](LinuxNativeSmoke.c)。这项结果证明 Linux 库的加载、解封装、解码和像素转换，不代表 Linux Unity 图形显示已经实机验证。
