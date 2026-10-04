# FFmpeg 构建记录

## 2026-10-05：Windows x86、Linux 和 Android 的 AV1 8/10-bit 软件解码

在已交付的 Windows x64 基础上，使用 WSL Ubuntu 24.04 完成另外四个非 Apple 目标的 28 个 FFmpeg 库。仍使用固定 FFmpeg `n9.0.1` commit `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`，构建前核对 143 个绑定公开头文件；没有更换绑定 ABI、GPU 桥接或既有硬件后端。Apple 本次补全结果见 [APPLE-RESULTS.md](APPLE-RESULTS.md)。

| 目标 | 实际工具链与架构 | 本次构建与二进制检查 |
| --- | --- | --- |
| Windows x86 | MinGW-w64 GCC 13-win32、NASM，固定 D3D12/Vulkan 头文件 | 7 DLL：PE i386、SHA256；D3D12VA / Vulkan 的 H.264、HEVC、AV1、VP9 配置门禁通过 |
| Linux x64 | GCC、NASM，隔离 libva/libdrm 开发依赖 | 7 SO：ELF64 x86-64、SHA256、`$ORIGIN`；实际加载全部 ABI、`libdav1d` 与四种 Vulkan 解码配置；保留 VAAPI |
| Android ARM64 | NDK r27c / Clang、API 23、AArch64 | 7 SO：ELF64 AArch64、SHA256、全部 PT_LOAD 至少 16 KiB；保留 MediaCodec / Vulkan |
| Android ARMv7 | 同一 NDK，armv7-a / softfp / NEON、API 23 | 7 SO：ELF32 ARM、EABI5 soft-float 调用约定、SHA256、全部 PT_LOAD 至少 16 KiB；保留 MediaCodec / Vulkan |

四个目标均从已校验 SHA256 的 dav1d 1.5.3 源码构建 PIC 静态库，并链接进 avcodec。逐一保存并检查 dav1d 的实际 `config.h`：`CONFIG_8BPC=1` 和 `CONFIG_16BPC=1`，后者包含 10-bit 路径；FFmpeg 的 `CONFIG_LIBDAV1D_DECODER=1` 也通过检查。x86/x64 保留 NASM，Android 保留 ARM 汇编优化。逐库对比原有 PE imports / ELF NEEDED，**28 个库均没有新增动态依赖**，不需要另行部署 dav1d 动态库。Linux 的三个随附运行时及其许可证仍由原有隔离依赖缓存提供。

实际构建使用 `build.py --targets win-x86,linux-x64,android-arm64,android-armv7 --jobs 16 --without-bridge --require-all` 的隔离包装器，保留既有 ABI 4 桥接。FFmpeg 源码在 WSL 本地 `/tmp/majdata-av1-all/source` 检出同一固定 commit，避免 NTFS 上的源码状态差异；构建中间文件也置于该 Linux 缓存。每个目标完成后立即将产物写入 `.build/av1-all-ready`，并将安装前缀、配置、dav1d 宏、完整日志及 provenance 持久保存到 `.build/av1-all-evidence`。最终 `build-summary.json` 四项均为 `built`，进程退出码 0。常规重建仍按 [README.md](README.md) 设置 WSL 的隔离 VAAPI 环境及 NDK，再运行同样的目标参数。

四个目标均已使用 `Av1SoftwareSmoke.c` 严格警告编译并运行：Windows x86、WSL Linux x64，以及 Mi MIX 2S 真机的 Android ARM64 / ARMv7，AV1 8-bit 和 10-bit 各通过 3570 项检查、90 帧，包含明确选择 `libdav1d`、实际解码位深、无硬件上下文、RGBA 像素转换、PTS、前后 seek 后像素复现及完整 EOF。Linux 在不设置 `LD_LIBRARY_PATH` 的条件下直接加载本次库并通过。Unity Mono/IL2CPP Player 的独立运行结果见 [播放器验证记录](../Tests/FFmpegValidation/RESULTS.md)，不能仅由编译与二进制校验推断其运行结果。原始 1080p 视频、其他设备和 GPU 驱动能力不由这些短测试样本覆盖。

## 2026-10-05：AV1 软件解码首轮（Windows x64）

Windows x64 使用 WSL Ubuntu 24.04、MinGW-w64 GCC 13-win32、NASM 及既有固定 D3D12/Vulkan 头文件重新编译七个 FFmpeg 库。dav1d 1.5.3 官方源码归档和 Meson 1.9.1 wheel 均经过 `dependencies.lock.json` 的 SHA256 校验；dav1d 以 PIC 静态库构建，FFmpeg 配置显式启用 `--enable-libdav1d`。FFmpeg commit、143 个绑定头文件和七个库的 ABI 保持原版本。

隔离产物先输出到忽略的 `.build/av1-ready/Windows/x86_64`，未在构建期间覆盖 Unity 正在使用的插件。Windows 直接加载验证通过全部七个库的版本函数、PE x64/哈希/importer 检查，以及 `avcodec_find_decoder_by_name("libdav1d")`。H.264 / HEVC / AV1 / VP9 的 D3D12VA 和 Vulkan Video 硬件配置仍全部存在。逐库 PE import 与旧库一致，没有新增 dav1d、libgcc 或 libwinpthread 动态依赖；原样复制的既有桥接加载后仍为 ABI 4。新 `avcodec-63.dll` SHA256 为 `a352ab150df09971835e10192aae80959d994a61ded9610dd9c12818fd1f7c2d`。

本节记录的是当日首轮 Windows x64 修复，当时实际原生构建和加载验证仅覆盖 Windows x64，其他目标尚未运行。这一历史范围已由上方后续四目标记录及 APPLE-RESULTS.md 中的 Apple 补全记录扩展；实体 iOS 设备播放仍未验证。Unity/Player 播放、seek、循环和回退结果另见 `Tools/Tests/FFmpegValidation/RESULTS.md`，不能由本节的 ABI/导出检查推断通过。

通过真实解码与隔离 Unity Player 验证后，七个 DLL、清单及许可证已应用到正式 Windows x64 插件目录；已有 `.meta`/GUID 和 GPU 桥接保持不变，新增 dav1d 许可证与 `.meta` 成对交付。旧文件备份在忽略的 `.build/av1-backup-20261004-170403`。应用后执行 `python Tools/FFmpeg/verify-artifacts.py`，全部 70 个既有/更新 FFmpeg 库、许可证和桥接检查通过，其中 Windows x64 实际加载并确认 `libdav1d`；其他平台的通过仅指原有产物完整性，不表示已加入或验证 AV1 软件解码。

## 2026-10-04：原生 D3D12VA / Vulkan Video

重新编译并交付了 Windows x64/x86、Linux x64、Android ARM64/ARMv7 的全部 35 个 FFmpeg 库，输出目录已跟随项目移动到 `Assets/Plugins/MajdataPlay/FFmpeg/Native`，已有插件 GUID 保留。Apple 的 35 个 device/macOS/simulator 库保持此前产物。

| 目标 | 本次实际工具链 | 新增且核验的后端 |
| --- | --- | --- |
| Windows x64、x86 | WSL Ubuntu 24.04 MinGW-w64 GCC 13-win32、NASM；固定 LLVM-MinGW 20260922 中的最小 D3D12 头文件覆盖 | H.264 / HEVC / AV1 / VP9 的 D3D12VA 与 Vulkan Video |
| Linux x64 | WSL GCC、NASM，原有隔离 libva/libdrm 开发依赖 | H.264 / HEVC / AV1 / VP9 的 Vulkan Video；保留 VAAPI |
| Android ARM64、ARMv7 | NDK r27c、API 23、16 KiB ELF LOAD 对齐 | H.264 / HEVC / AV1 / VP9 的 Vulkan Video；保留 MediaCodec |

Vulkan-Headers 固定 SDK 1.4.328.1 / commit `19725e4d48082fe78e26622b15d3080ccd54112b`。Windows 使用的 `d3d12.h` 和 `d3d12video.h` 均按 `dependencies.lock.json` 的 SHA256 校验；未覆盖 GCC 原配的 CRT 头文件与链接库。完整 LLVM-MinGW 的 Windows/Linux/macOS 官方压缩包也已锁定 SHA256，正常构建可自动下载，已有工具链可显式提供。

FFmpeg 编译前逐项确认请求的硬件后端未被 configure 静默禁用。Windows x64 和 Linux x64 另外实际加载新库，通过 `avcodec_get_hw_config()` 确认四种编码格式的对应原生硬件配置已经导出。完整产物验证通过 70 个 FFmpeg 库的哈希、架构和元数据检查，包括新的 Android 16 KiB 对齐以及 Linux 原生 ABI 与随附依赖许可检查。五个新增 Vulkan 构建目录携带 Khronos 署名、Apache-2.0 全文和许可 SHA256。

本机 MSYS2 的简单命令可执行，但 FFmpeg configure 的子进程树仍遇到 `child_copy / dofork` 错误，因此 Windows 库最终由 WSL GCC 编译；没有修改系统安全设置。x64 初次从 WSL stage 时遇到目标文件占用，随后用 Windows 进程复制保留的完整 prefix 成功。WSL 临时缓存自动消失后，x64 清单从新库的 `avutil_configuration()` 恢复准确参数，并再次实际查询硬件配置；清单中的 `stagingRecovery` 明确记录该过程。新脚本会在 stage 前将构建证据保存在安装 prefix，并原子写入清单，避免临时文件映射造成元数据写入失败。

这些结果证明原生后端编译和部署完整，不能据此认定所有 GPU 都能硬解。本机 RX 580 的 D3D12 Video Decode Tier 1 不满足当前固定 FFmpeg D3D12VA 实现的 Tier 2 要求；实际播放器回退、GPU 帧呈现与设备限制的验证另见 `Tools/Tests/FFmpegValidation/RESULTS.md`。本机 WSL 仍没有真实 DRM 渲染设备，Android 的 Vulkan Video 能力也由真机驱动决定。

## 2026-10-03：首次全平台构建

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

首次构建或缓存被清理后会重新获取已锁定的 FFmpeg 源码；不会静默改用新的版本。当时的依赖缓存需手工准备；2026-10-04 的脚本已增加固定 Vulkan-Headers / LLVM-MinGW 自动下载，其余 SDK 与 VAAPI 依赖仍按 README 准备。
Linux 成品在 stage 阶段用 `patchelf --set-rpath '$ORIGIN'` 处理并逐库读回验证，文件 hash 在处理后计算。构建脚本默认同时构建支持平台的原生桥接；iOS 不允许省略它。

完整 Unity 画面、时间轴、速率、预载、生命周期与图形 API 结果由本次主测试记录提供；此文件仅陈述原生库构建和二进制复验事实。播放器按项目需求不输出音轨。
