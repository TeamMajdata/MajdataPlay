# Apple 构建与原生验证（2026-10-03）

在用户提供的 `mac-mini` 上实际完成五个 Apple 目标的 FFmpeg 和 Metal 桥接构建，并已把产物、Unity `.meta`、许可证与哈希清单回传到本仓库。所有操作使用独立目录 `/Users/codex/codex-work/majdata-ffmpeg-20261003`，未改动远端既有项目。

## 环境与产物

- Apple M4 / ARM64 / 10 cores；macOS 27.0.1。
- Xcode 27.0（27A266a），macOS/iPhoneOS/iPhoneSimulator 27 SDK；CMake 4.4.2、Ninja 1.13.2。
- NASM 2.16.01 从官方源码编译到任务缓存，用于 x64 汇编；未安装到系统目录。
- FFmpeg `n9.0.1`，commit `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`，与现有绑定 143 个公开头文件匹配。
- macOS 最低版本 11.0；iOS 最低版本 15.0，与当前 Xcode libc++ 最低支持版本一致。

| 目标 | 本仓库输出 | 构建与验证 |
| --- | --- | --- |
| macOS ARM64 | `Assets/Plugins/MajdataPlay/FFmpeg/Native/macOS/arm64` | 7 dylib + Metal 桥接；实际 ARM64 运行通过 |
| macOS x64 | `Assets/Plugins/MajdataPlay/FFmpeg/Native/macOS/x86_64` | 7 dylib + Metal 桥接；架构/平台/哈希通过，主机无 Rosetta，未运行 |
| iOS ARM64 | `Assets/Plugins/MajdataPlay/FFmpeg/Native/iOS` | 7 静态库 + Metal 桥接；全部链接通过，未在实体设备运行 |
| iOS 模拟器 ARM64 | `Tools/FFmpeg/.build/artifacts/ios-simulator-arm64` | 7 静态库 + Metal 桥接；全部链接与模拟器运行通过 |
| iOS 模拟器 x64 | `Tools/FFmpeg/.build/artifacts/ios-simulator-x64` | 7 静态库 + Metal 桥接；全部链接通过，未运行 |

模拟器库位于忽略的构建产物目录，避免同时导入 Unity device 库引起重复符号；需要留存或交付模拟器包时应单独保存该目录。各目录的 `build-manifest.json`、`bridge-manifest.json` 记录实际配置、构建主机、时间和文件 SHA256。macOS dylib 使用 `@loader_path` 解析同目录依赖。iOS 桥接包含 `ffu_register_ios`，测试确实链接了静态 Unity 注册入口。

## 实际测试

macOS ARM64 使用仓库真实 H.264 视频 `Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4`：

```text
PASS: macOS 144 checks; codec=h264; 1920x1080; frames=30; RGBA range=0..182; checksum=d7ee38d817b380a7
Metal device: Apple M4
VideoToolbox: codec=h264 frames=30 output range=0..255
PASS: Metal NV12 texture sampling, GPU frame lifetime and real video decode through VideoToolbox; 231 checks
```

加载测试清除 `DYLD_LIBRARY_PATH`，先加载 avformat，以验证其余同目录依赖真正可解析。解码测试验证 1920×1080 的 30 帧及 RGBA 数据。Metal 测试创建实际 IOSurface/NV12 纹理，由 Metal compute shader 采样亮暗像素，并验证原始 `AVFrame` 释放后，桥接仍保留帧到 GPU 完成。实际视频部分强制 VideoToolbox 硬件解码；macOS 测试没有使用软件回退掩盖硬件失败。

iPhoneOS ARM64、iPhoneSimulator ARM64/x64 三套 SDK 均实际链接全部 7 个 FFmpeg 静态库、Metal 桥接、静态 Unity 注册入口及所需系统 frameworks，全部 PASS。x64 NASM 对象不带 `LC_BUILD_VERSION`，链接器会报告平台标签提示；它们的 CPU 为 x86_64，其余对象的平台标记已检查，最终链接成功。

使用官方 `xcodebuild -downloadPlatform iOS -architectureVariant arm64` 安装 iOS 27（24A434）模拟器运行时，在独立的 `FFmpeg-Validation` 设备运行：

```text
Metal device: Apple iOS simulator GPU
SKIP: simulator VideoToolbox hardware decode unavailable; checking actual software fallback instead.
Software fallback: codec=h264 frames=30 output range=0..182
PASS: Metal NV12 texture sampling, GPU frame lifetime and real video decode (simulator permits an explicit software decoder fallback); 116 checks
```

此结果验证模拟器上的实际 Metal 纹理与资源生命周期，以及真实视频的软件解码；不证明 iOS 实体设备 VideoToolbox 或 Unity Player 路径已通过。

## 复现

在 macOS 仓库目录运行，NASM/CMake/Ninja 已在 PATH：

```bash
bash Tools/FFmpeg/build.sh --targets macos-x64,macos-arm64,ios-arm64,ios-simulator-arm64,ios-simulator-x64
python3 Tools/FFmpeg/verify-artifacts.py
bash Tools/Tests/FFmpegValidation/run-apple-native.sh arm64
bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-arm64
bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-simulator-x64
FFMPEG_SIMULATOR_UDID=<已启动的专用模拟器UUID> bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-simulator-arm64
```

原生测试源码为 `AppleLoaderSmoke.c` 和 `Native/tests/MetalSmoke.mm`。测试可接收第二个参数覆盖视频路径。运行记录位于 `Tools/Tests/FFmpegValidation/.work/Apple-*`；本次回传的完整日志保存在本地 `.work/AppleRemoteEvidence`。`verify-artifacts.py` 已检查回传的 70 个 FFmpeg 库，包含 Mach-O CPU、静态 archive 对象平台和所有文件哈希。

## 当前未验证范围

远端没有 Unity Editor。尝试官方 Unity CLI 安装同版 6000.3.17f1 的 ARM64 Editor、iOS 和 Mac IL2CPP 支持时，官方下载地址重定向到 `download.unitychina.cn` 后返回 HTTP 404；该账号的 CLI 也未登录。已下载的官方 CLI 只保存在任务目录，没有更改其他用户的 Hub 或项目。因而本次未进行 Apple Unity Mono/IL2CPP Player 构建与运行。

远端没有 Rosetta，x64 可执行文件返回 `Bad CPU type in executable`。无实体 iOS 设备，未进行设备安装、应用签名或实体 VideoToolbox 运行测试。上述环境限制不影响已生成的五套二进制，但应在正式分发前补齐对应 Player 与设备验证。
