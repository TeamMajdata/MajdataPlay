# 构建记录（2026-10-03）

## 已生成产物

| 目标 | 实际编译工具链 | 产物 | 验证范围 |
| --- | --- | --- | --- |
| Windows x64 | 本机 WSL Ubuntu 24.04 的 MinGW-w64 GCC；NASM 2.16.01 | 7 个 DLL，`Native/Windows/x86_64` | PE x64、SHA256、直接加载全部版本函数；真实播放器测试另见 `Tools/Tests/FFmpegValidation` |
| Windows x86 | 本机 WSL Ubuntu 24.04 的 MinGW-w64 GCC；NASM 2.16.01 | 7 个 DLL，`Native/Windows/x86` | PE x86、SHA256；真实 32 位测试另见 `Tools/Tests/FFmpegValidation` |
| Linux x64 | 本机 WSL Ubuntu 24.04 的 GCC；NASM 2.16.01 | 7 个 `.so.<major>`，`Native/Linux/x86_64` | ELF x64、SHA256、全部版本函数直接加载；7 个 RUNPATH 均为字面值 `$ORIGIN` |
| Android ARM64 | 本机 WSL + 官方 NDK r27c / Clang，API 23 | 7 个无版本 `.so`，`Native/Android/arm64-v8a` | ELF64/AArch64、SONAME/NEEDED、SHA256、全部 PT_LOAD 对齐至少 16 KiB |
| Android ARMv7 | 同上，armv7-a / softfp / NEON | 7 个无版本 `.so`，`Native/Android/armeabi-v7a` | ELF32/ARM、SONAME/NEEDED、SHA256、全部 PT_LOAD 对齐至少 16 KiB |
| macOS x64/ARM64 | 用户提供的 Mac mini：Apple M4、Xcode 27、NASM 2.16.01 | 每架构 7 个 dylib + Metal 桥接，`Native/macOS/x86_64`、`arm64` | Mach-O CPU/平台/SHA256；ARM64 实际加载/解码 144 checks、VideoToolbox→Metal 231 checks PASS；x64 未运行 |
| iOS ARM64 | 同一 Mac mini 的 iPhoneOS 27 SDK | 7 个静态库 + Metal 桥接，`Native/iOS` | 全部 archive CPU/平台/SHA256；静态注册与系统框架实际链接 PASS；无实体 iOS 设备 |
| iOS 模拟器 ARM64/x64 | 同一 Mac mini 的 iPhoneSimulator 27 SDK | 每架构 7 个静态库 + Metal 桥接，`.build/artifacts/ios-simulator-*` | 两架构实际链接 PASS；ARM64 模拟器 Metal/软件解码 116 checks PASS，模拟器不提供本次 VideoToolbox 硬件解码 |

所有 FFmpeg 库从官方 `n9.0.1` commit `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa` 编译。构建前核对了绑定使用的 **143 个公开头文件**（CRLF 归一化后完全一致）。没有使用其他 FFmpeg 发布版改名充当匹配库。
每个目录的 `build-manifest.json` 保存具体配置和二进制 SHA256；`configure.txt` 中能看到 `x86 assembler nasm` / Android 架构等实际配置。桌面最终产物已启用 SIMD；早期用于验证的禁汇编版本已被替换。

Windows x86/x64 GPU 桥接是本机实际编译的独立产物 `FFmpegUnityBridge.dll`。其 `bridge-manifest.json` 保存桥接源码 hash、编译器、构建命令和真实 D3D11 GPU 测试；与 FFmpeg 本体的清单分开保存。

## 复验

最终拉回 Apple 产物后，在 Windows 上复验全部 10 个目标；Windows、WSL 和 Mac ARM64 分别实际加载本机 ABI：

```text
PASS android-arm64: 7 SHA256/machine/importer checks
PASS android-armv7: 7 SHA256/machine/importer checks
PASS ios-arm64: 7 SHA256/machine/importer checks
PASS linux-x64: 7 SHA256/machine/importer checks
PASS macos-arm64: 7 SHA256/machine/importer checks
PASS macos-x64: 7 SHA256/machine/importer checks
PASS win-x86: 7 SHA256/machine/importer checks
PASS win-x64: 7 SHA256/machine/importer checks
PASS ios-simulator-arm64: 7 SHA256/machine/importer checks
PASS ios-simulator-x64: 7 SHA256/machine/importer checks
PASS linux-x64: 3 runtime dependency/license checks
PASS 70 FFmpeg libraries; PE/ELF/Mach-O architecture, Apple platform and Android 16 KiB alignment verified.
```

在 Windows 上额外直接调用了全部 7 个 x64 库的版本函数；在 WSL 上额外直接调用了全部 7 个 Linux 库的版本函数，未设置 `LD_LIBRARY_PATH`。Linux 依赖通过库旁的 `$ORIGIN` 解析。Unity 已确认版本化 Linux `.so.<major>` 被识别为 `PluginImporter`。
Android `.meta` 使用 `Android: Android` 分组和各自 ARM64/ARMv7 CPU；仅 `GetCompatibleWithPlatform()` 为 true 不足以证明打包成功，APK 内库清单检查由播放器验证脚本负责。
Linux VAAPI/libdrm 已启用，附带 3 个直接运行时依赖、各自许可和哈希清单；全部设置 `$ORIGIN`，不依赖修改 `LD_LIBRARY_PATH`。这些二进制检查与 Android 真机、Unity Player 播放测试分别记录。Apple 原生实际测试与尚未完成的 Unity/实体设备验证详见 [APPLE-RESULTS.md](APPLE-RESULTS.md)。

## 主机与可重复运行

已有 Windows Git Bash 及另行解包的最新版 MSYS2 均出现 Windows `child_copy / fork` 故障；未修改系统安全策略。最终使用本机已存在的 WSL Ubuntu 24.04 及其 GCC/MinGW 工具链完成编译。
官方 Linux NDK r27c 下载并解包到项目忽略的 `.build/toolchains/android-ndk-r27c`；NASM 和 patchelf 从配置的 Ubuntu 包仓库下载后仅解包到 `.build/toolchains/linux-packages`，没有安装系统软件。WSL 构建缓存曾消失，因此这些可执行工具又保留了一份在项目缓存，脚本会自动发现。

Windows 的 WSL 一键入口（Apple 目标会明确显示 SKIP；Linux 需系统已安装或显式配置 VAAPI 开发依赖）：

```powershell
.\Tools\FFmpeg\build.ps1 -UseWsl -WslDistribution Ubuntu-24.04 -BuildRoot /tmp/majdata-ffmpeg-build
```

早期 `/tmp/majdata-ffmpeg-build` 记录在 Linux 启用 VAAPI 之前生成，当时该入口预检可发现 Windows 双架构、Linux 和 Android 双架构。最终版本显式要求 `pkg-config`、libva/libdrm 开发模块；当前机器直接运行上述入口的 `-Probe` 得到 Windows/Android 四个 READY，Linux 因未配置隔离依赖而 SKIP，不能把早期预检结果当作最终版本的无条件复现承诺。

最终 Linux FFmpeg 在 `/tmp/majdata-ffmpeg-vulkan` 重新构建，清单包含 `--enable-vaapi --enable-libdrm`。依赖下载后解包到项目 `.build/toolchains/vaapi-packages/root`，通过其 `pkg-config` 包装器、`PKG_CONFIG_PATH`、`PKG_CONFIG_SYSROOT_DIR` 和构建进程内的 `LD_LIBRARY_PATH` 提供；桥接随后从最终库独立构建并 stage。完整隔离环境命令见 [README.md](README.md#工具链矩阵)。2026-10-03 已用该环境重新执行 `--targets linux-x64 --probe --require-all`，输出 libva pkg-config API 版本 `1.20.0`、libdrm `2.4.125` 和 `READY linux-x64`，未重新编译或覆盖最终二进制。

首次构建或缓存被清理后会重新获取已锁定的 FFmpeg 源码；不会静默改用新的版本。依赖缓存不由脚本自动下载，必须按 README 准备。
Linux 成品在 stage 阶段用 `patchelf --set-rpath '$ORIGIN'` 处理并逐库读回验证，文件 hash 在处理后计算。构建脚本默认同时构建支持平台的原生桥接；iOS 不允许省略它。

完整 Unity 画面、时间轴、速率、预载、生命周期与图形 API 结果由本次主测试记录提供；此文件仅陈述原生库构建和二进制复验事实。播放器按项目需求不输出音轨。
