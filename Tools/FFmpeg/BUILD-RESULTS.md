# FFmpeg 构建记录

## 2026-10-05：补齐四种软件视频编码器

重新构建全部十个目标，统一包含 **H.264 `libx264`、H.265/HEVC `libx265`、AV1 `libaom-av1`、VP9 `libvpx-vp9`**。四个依赖以 PIC 静态库并入 avcodec；iOS 静态归档也包含其对象，无需额外部署四个编码器的动态库。MPEG4、各平台已有硬件 encoder、muxer、dav1d 和解码后端保留。下方 MPEG4-only 记录为同日较早构建的历史结果。

| 目标 | 当前交付 |
| --- | --- |
| Windows x86 / x64 | 每架构七 DLL + ABI 4 桥接；C++ 与线程运行库静态链接，PE imports 无额外 libstdc++/libgcc/libwinpthread DLL |
| Linux x64 | 七 SO + ABI 4 桥接；静态 libstdc++，FFmpeg 依赖使用 `$ORIGIN`；保留既有 libva/libdrm 依赖包 |
| Android ARMv7 / ARM64 | NDK r27c、API 23；每 ABI 七 SO + ABI 4 桥接，PT_LOAD 至少 16 KiB；静态 libc++/libc++abi/unwind，无 libc++_shared.so |
| macOS x64 / ARM64 | 每架构七 dylib + ABI 2 Metal 桥接；依赖 Apple 系统 libc++ |
| iOS device ARM64 | 七静态归档 + ABI 2 Metal 桥接；完整链接验证 |
| iOS simulator ARM64 / x64 | 各七静态归档 + ABI 2 Metal 桥接；独立保存于忽略的 `.build/artifacts/ios-simulator-*` |

仍固定 FFmpeg `n9.0.1` / `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`，逐一核对 143 个 AutoGen 公开头文件。新依赖固定官方 Git commit：x264 `0.165-stable` / `b35605ace3ddf7c1a5d67a2eb553f034aef41d55`、x265 `4.1` / `1d117bed4747758b51bd2c124d738527e30392cb`、libvpx `v1.17.0` / `6df3ec34557879fff673706f4a1d9fbd0f3a6f0e`、libaom `v3.13.3` / `92d4c37fbdd08944a0e721bbaeb13318f10aebb0`。配置/源码树摘要、实际编译器、SDK、静态库和许可证哈希均记录于各 `build-manifest.json`。

启用 x264/x265 后组合 FFmpeg 构建为 **GPL version 3 or later**（启用 GPL 与 version3、禁用 nonfree）。随包交付四个依赖的版权、专利和适用第三方许可证，以及 Windows/Linux/Android 被静态链接的编译器运行库声明；这些声明有独立哈希和 `.meta`。Apple 的 libc++ 为系统依赖。上游源码地址及固定 commit 位于 lock 和清单中。

保留 AMF 检查补丁，并新增 [x265 参数拒绝补丁](patches/x265-parameter-check.patch)，SHA256 为 `5b457330f94aa63e539798ae28b3ffcd05388a58a29ba49546da79b944e0f0de`。完整版本标识为 `MajdataPlay-AMF-RC-v1-c0604b924b6a_MajdataPlay-X265-Params-v1-5b457330f94a`；公开 ABI 不变。未知或无效的 x265 参数现在返回 `EINVAL`，托管包装器要求该标记后才使用 strict-cbr。

`FFmpegVideoEncoder` 同步启用四种格式的 CBR/VBR：x264 使用兼容 MP4 的 VBR HRD 信令、相等 VBV 目标/最大值与独立 filler 实现 CBR；x265 显式设置 strict-cbr 并关闭额外 worker pool；VP9/AV1 显式选择实际单次 VBR 或 CBR，禁用 CRF、lookahead 和丢帧。软件 codec worker 会按原生库上限限制，x265 至多 16，libvpx/libaom 至多 64，其余至多 128，且不超过用户设置。

iOS Player 的固定 VideoToolbox 包装器不能验证 hardware-only 选择，因此硬件偏好现在公开明确原因并回退到同格式软件，避免将系统选择结果误报为物理硬件；macOS 保留硬件强制选择。`UNITY_IOS` 分支通过独立托管编译（零错误），仅为编译证据，不代替实体 iOS 运行。

| 本轮实际运行 | 结果 |
| --- | --- |
| Windows x86、Windows x64、Linux x64 | 每目标四种格式 × 两模式 × 两类画面，共 16 文件，**31,820 checks PASS** |
| Android ARMv7、ARM64 / Mi MIX 2S 真机 | 每 ABI 同一 16 文件矩阵，**31,820 checks PASS**；ELF 依赖闭包与 16 KiB 对齐通过 |
| macOS ARM64、iOS simulator ARM64 | 每目标同一 16 文件矩阵，**31,820 checks PASS**；AV1 8/10-bit 各 3570 checks，Metal 回归通过，见 [Apple 记录](APPLE-RESULTS.md) |
| Windows x64 `.NET 9` | 四种软件编码 **4184 assertions PASS**，MPEG4/AMF 硬件与会话回归 **839 PASS**，H.264 播放 **446 PASS**；独立项目编译无错误 |
| Unity 6000.3.17f1 x64 / D3D11 Camera | Mono 与 IL2CPP 实际 `libx264` H.264 CBR，各 **416 checks PASS**，覆盖 GPU 回读、暂停/恢复、PTS 和原生回调 |

原生矩阵使用 [NativeSoftwareEncoderSmoke.c](../Tests/FFmpegValidation/NativeSoftwareEncoderSmoke.c) 和共享 fixture，以严格 C 警告构建。实际解码验证 60 帧、像素/方向、PTS 间隔、flush、线程设置和独占文件所有权；简单画面与复杂运动突发都覆盖。Windows 运行使用只含交付库及系统目录的 PATH；Linux 清除 `LD_LIBRARY_PATH` 后运行，防止开发工具链掩盖缺失动态依赖。

CBR 表示编码器实际采用的模式，不能将它理解为所有编码器都会填充到精确目标。x264/x265 检查所有连续包窗口满足 `最大码率 × 时长 + VBV 容量 + 1 KiB` 的缓冲预算；x264 简单画面的 filler 也有独立检查。**libvpx/libaom 的两种模式均为码率预算，复杂画面可能超出设定值，简单画面不会强制填充**；测试记录实际负载，不以短样本声称硬上限或所有场景恒定填充。ARM 上 libvpx/libaom 当前采用可移植实现、x265 关闭汇编，x264/dav1d 保留对应可用汇编；高分辨率实时性能未验证。

实际构建使用 WSL Ubuntu 24.04 的独立持久缓存 `/home/lezi/.cache/majdata-ffmpeg-full-encoding-20261005` 和 SSH Mac mini 的独立目录 `/Users/codex/codex-work/majdata-full-encoding-apple-20261005`。Windows/Linux 使用 GCC 13，Android 使用 NDK r27c，Apple 使用 Xcode 27 SDK。x265 固定版本使用 CMake 3.x；Apple 从已校验的官方 CMake 3.31.10 wheel 提取专用工具，不修改系统 CMake 4。

八套正式插件与两套独立 simulator 包完成替换后，`python Tools/FFmpeg/verify-artifacts.py` 对 **70 个 FFmpeg 库、10 个桥接和三个既有 Linux 运行时** 全部通过。全部十目标 decoder/hwaccel 名称集合与旧配置逐项相同，**149 个原有 `.meta` 文件字节和 GUID 保持不变**；新增许可证/补丁资产均有配对 `.meta`。正式替换、最终 verifier、软件矩阵与 Unity 原始日志保留在忽略的 `.build/full-encoding-20261005`；完整命令见 [验证记录](../Tests/FFmpegValidation/RESULTS.md)。

主工程随后使用 Unity **6000.3.17f1** 完成 batchmode 导入和脚本编译，退出码 **0**，没有 C# 编译错误；未修改正式场景或 Player Settings。原始日志为 `main-unity-import.log`。

未验证：macOS x64/iOS x64 simulator 运行（Mac 没有 Rosetta 或 x64 runtime）、实体 iOS、Apple Unity Player、Android Unity Camera、Linux VAAPI/NVENC 和其他 GPU 的硬件编码。编译/链接通过不代替上述运行证据；此前 Apple VideoToolbox CBR 与 DataRateLimits 组合限制仍见下方历史记录及 Apple 说明。本轮没有构建完整多平台发布包。

## 2026-10-05（较早）：MPEG4-only 录制原生库重建

按现有录制 profile 重建全部十个目标，共 **70 个 FFmpeg 库和 10 个图形桥接**。八套正式 Unity 插件已更新到 `Assets/Plugins/MajdataPlay/FFmpeg/Native`，两个 iOS 模拟器包保留在忽略的 `.build/artifacts/ios-simulator-*`，不与 device 插件同时导入。固定 FFmpeg `n9.0.1` / `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`、143 个 AutoGen 公开头文件及七库 ABI 不变；Windows/Linux/Android 桥接保持 ABI 4，Apple 保持 ABI 2。

| 目标 | 构建环境与产物 |
| --- | --- |
| Windows x86/x64 | WSL Ubuntu 24.04、MinGW-w64 GCC 13-win32、NASM；每架构七 DLL + 桥接 |
| Linux x64 | WSL GCC 13.3.0、NASM、隔离 libva/libdrm；七 SO + 桥接，依赖按 `$ORIGIN` 从同目录解析 |
| Android ARMv7/ARM64 | WSL NDK r27c / Clang、API 23；每 ABI 七 SO + 桥接，全部 PT_LOAD 至少 16 KiB |
| macOS x64/ARM64 | SSH `mac-mini`、Apple M4、Xcode 27 / macOS 27 SDK；每架构七 dylib + Metal 桥接 |
| iOS device ARM64 | 同一 Mac 的 iPhoneOS 27 SDK；七静态库 + Metal 桥接，静态注册入口实际链接 |
| iOS simulator ARM64/x64 | iPhoneSimulator 27 SDK；每架构七静态库 + Metal 桥接，链接与 SDK 平台标记检查 |

全部目标包含 MPEG4 软件 CBR/VBR 和 MOV/MP4/MKV/WebM/AVI muxer；Windows 另包含 NVENC/AMF H.264/HEVC/AV1，Linux 另包含 NVENC H.264/HEVC/AV1 与 VAAPI H.264/HEVC/VP9/AV1，Apple 另包含 H.264 VideoToolbox。保持 dav1d 1.5.3 静态 AV1 8/10-bit 解码；没有增加 x264/x265/libvpx/libaom 软件编码器。各目标 decoder/hwaccel 名称集合与重建前完全相同，新增编码入口没有扩大解码矩阵。

Windows 七个 FFmpeg DLL 的 PE imports 与旧库完全相同；桥接由原 LLVM-MinGW UCRT 改用与本轮 FFmpeg 相同的 GCC 13-win32，CRT imports 改为系统 `msvcrt.dll`，没有新增需随包部署的动态依赖。两种架构均通过真实 Unity GPU 播放与 Camera 录制验证。

各清单记录实际编译命令、时间、二进制 SHA256、录制 profile、依赖许可证和检查补丁。AMF 补丁 SHA256 为 `c0604b924b6ae1ee718c045d34f1448ebb78652ccacadfdb54799810c69e17f7`，能力标记为 `--extra-version=MajdataPlay-AMF-RC-v1-c0604b924b6a`；交付原始补丁与 `.meta`。Windows 附带 AMF/NVIDIA 头文件许可证，Linux 附带 NVIDIA 头文件许可证。**136 个原有 `.meta` 的字节和 GUID 全部保持不变**，新增资产与 `.meta` 配对。

| 实际运行验证 | 结果与范围 |
| --- | --- |
| Windows x64，正式 DLL 的 .NET 9 录制/播放 | **838 / 446 assertions PASS**；MPEG4 CBR/VBR、实际 H.264 AMF CBR/VBR、完整封装/解码/取消/文件所有权；原有 H.264 解码、seek、队列与回退回归 |
| Windows x86 原生录制 | **2021 checks PASS**；MPEG4 CBR/VBR 各 60 帧、线程上限 2、像素、跳帧 PTS、排空、禁止覆盖 |
| Unity 6000.3.17f1 Camera | x86 Mono Built-in 软件与 AMF 硬件各 **197 checks PASS**；x64 IL2CPP URP + Camera Stack + AMF 硬件 **246 checks PASS** |
| Unity Windows 原生桥接 | x86/x64 Mono D3D11VA 严格 GPU 播放各 **31 assertions PASS**，实际 GPU 纹理转换，无 CPU 视频回读 |
| Linux x64 原生 | 加载/解码 **144 checks PASS**、桥接 ABI/保护分支通过；MPEG4 录制 **2021 checks PASS**，清除 `LD_LIBRARY_PATH` 后仍从交付目录加载 |
| Android ARM64/ARMv7 真机 | 每 ABI MPEG4 录制 **2021 checks PASS**、全部原生库/ABI 4/录制入口检查 **54 checks PASS**；AV1 8-bit/10-bit 各 **3570 checks / 90 帧 PASS** |
| macOS ARM64 / iOS simulator ARM64 | MPEG4 录制各 **2021 checks PASS**；各自 AV1 8/10-bit、H.264/Metal 回归通过，详细数据见 [Apple 记录](APPLE-RESULTS.md) |

跨平台原生录制使用新增 [EncoderNativeSmoke.c](../Tests/FFmpegValidation/EncoderNativeSmoke.c)，按各自 C 编译器以 `-std=c11 -Wall -Wextra -Werror` 构建（Android 使用 `-std=gnu11`）；Android 另使用 [AndroidNativeSmoke.c](../Tests/FFmpegValidation/AndroidNativeSmoke.c) 直接加载全部八个插件与检查设备前置条件。共同 MPEG4 CBR 最后一秒为 **199,976 bit/s**，目标 200,000；同一平坦 fixture 的 VBR 为 3,616 bit/s。Windows AMF 正式库原生录制 VBR 为 12,448、CBR 为 200,000 bit/s。本机 RX 580 没有 NVIDIA 编码器；NVENC 入口导出不等于设备运行通过。

Apple H.264 VideoToolbox 已实际禁止 macOS 软件回退并完成两模式逐帧解码，但当前 FFmpeg 同设 ConstantBitRate 与 DataRateLimits 时 CBR 为 **11,072 bit/s**，未通过原始目标填充断言。移除最大码率设置的独立诊断得到 204,680 bit/s；它不代表当前组件参数组合通过，也未应用到正式代码。具体限制、官方依据与诊断证据见 [APPLE-RESULTS.md](APPLE-RESULTS.md)。

本次仍未验证 Linux VAAPI/NVENC 硬件录制（WSL 无 `/dev/dri`）、Android Unity/MediaCodec GPU 帧、Apple Unity Player、实体 iOS、Apple x64 运行及其他硬件编码格式。Android Mi MIX 2S / API 35 / Adreno 630 仅提供 Vulkan 1.1.128，不能将其前置检查外推为 Vulkan Video 1.3 运行通过。没有进行完整多平台发布构建。

实际构建分别使用当前 `build.py` 的 `--targets win-x64,win-x86 --jobs 8 --with-bridge --require-all`、`--targets linux-x64 --jobs 4 --require-all`、`--targets android-arm64,android-armv7 --jobs 4 --require-all`，以及 Mac 上的 `--targets macos-arm64,macos-x64,ios-arm64,ios-simulator-arm64,ios-simulator-x64 --jobs 8 --require-all`。WSL 各任务使用独立源码/构建缓存并设置已有 VAAPI/NDK/头文件环境；Android 改为持久缓存以避免短 WSL 会话清空 `/tmp`。Windows 先校验独立 ready 目录，Unity 占用解除后完整替换正式插件并复验。

最终执行 `python Tools/FFmpeg/verify-artifacts.py`，十目标 **70 库 + 10 桥接** 的 SHA256、CPU/SDK、许可证、补丁及 importer 全部通过，Windows x64 实际加载七库与 ABI 4 桥接；Linux、Android、Mac/模拟器的运行证据单独列于上表。`git diff --check` 通过。运行命令及原始日志位置见 [FFmpeg 验证结果](../Tests/FFmpegValidation/RESULTS.md)；以下保留此前播放专用构建的历史记录。

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
