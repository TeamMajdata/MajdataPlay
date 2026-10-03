# 本机验证结果

执行日期：2026-10-03。Windows / Unity 6000.3.17f1 / AMD Radeon RX 580 2048SP；Linux 使用本机 WSL Ubuntu 24.04；Android 真机为 Mi MIX 2S / Android 15 API 35 / Adreno 630 / Vulkan 1.1.128；Apple 构建测试使用用户提供的 Mac mini M4。FFmpeg 库均为本次从锁定源码编译的产物。Windows/Apple 图形桥接 ABI 为 **2**，Linux/Android 为 **3**。

## 本次日志、解码偏好与 CPU 上传回退验证

2026-10-03 更新：运行时已引用真实 `MajdataPlay.Diagnostics`，新增首选解码器类型、硬件解码加 CPU 上传回退、四级日志和 Profiler 作用域。本轮未改动原生库；下方此前的完整平台矩阵保留原验证范围，不将其视为本轮托管改动在所有设备上的重测。

| 本轮目标 | CPU 硬解上传 | 偏好切换与原生纹理失败恢复 | 严格 GPU 模式 | 原有控制回调故障恢复 |
| --- | --- | --- | --- | --- |
| Windows x64 Mono，D3D12 / Vulkan | 各 24 assertions PASS | 各 41 assertions PASS | 各 20 assertions PASS | D3D12，52 assertions PASS |
| Windows x64 IL2CPP，D3D12 / Vulkan | 各 24 assertions PASS | 各 41 assertions PASS | 各 20 assertions PASS | D3D12，52 assertions PASS |
| Android ARM64 IL2CPP，Vulkan | MediaCodec ByteBuffer 实际像素/seek 通过 | 与软件/硬件偏好、共享失败恢复合计 58 assertions PASS、342 帧 | 同次运行先验证原生 GPU 共享与 300 帧持续播放 | — |
| Android ARMv7 IL2CPP，Vulkan | MediaCodec ByteBuffer 实际像素/seek 通过 | 同一真机合计 58 assertions PASS、340 帧 | 同上 | — |

Windows 共 14 个独立场景通过。两种 Android ABI 都在无 Surface 的 `h264_mediacodec` 硬件解码后得到 CPU 像素，并以 `Hardware decode + CPU RGBA upload` 呈现；故障注入后继续播放和 seek，最后重新打开媒体恢复 AHardwareBuffer 共享。最终报告的 `fallback=` 为空是因为最后一次重新打开已恢复原生路径，测试过程中的故意回退与 Warning 完整保留在日志中。

真实 MajDebug 日志验证 `Debug`、`Info`、`Warning`、`Error` 四等级与 `[FFmpeg][component]` 标签；记录 h264 编码、实际软件/硬件后端、`h264_mediacodec` / D3D11VA、设备选择及 Unity 渲染 GPU/驱动、纹理传输路径。Error 来自故障恢复测试主动注入的终止错误。预载首帧回调中 Pause 的检查也通过：首帧可完成准备，无需丢弃后等待第二帧。

真实 FFmpeg 解码测试扩展至 **335 assertions PASS**，新增独立 D3D11VA 设备下载、下载后的 seek、硬件身份与 CPU 像素并存、原生映射失败仅尝试一次后保留硬解，以及严格模式禁止下载。**13 组**真实程序集宏编译通过，覆盖九组平台/IL2CPP 组合和四组 Editor/Debug/Profiler 组合（含 Inspector 与 iOS 后处理）。Profiler 工作线程已配对注册/注销；未将宏编译当作 Profiler 界面的采样录制验证。

证据位于 `.work/windows-decoder-diagnostics-matrix.json`、各 Windows 目录的 `*-hardware-cpu.*` / `*-decoder-preference.*` / `*-recovery.*`、两种 Android 目录的 `vulkan-hardware-device.txt` 及 `.diagnostics.log`，以及 `.work/DecoderTransportManagedReview/`。本轮没有补做 Linux VAAPI 物理 GPU 或 Apple Unity Player 实机验证，相关限制仍适用。

## 此前的 Windows Unity 硬件播放矩阵

下列 **16 组**均已真实启动 Player，包含纹理像素读回、时间轴和生命周期检查。每组 **18 assertions 通过**；要求真实硬件路径，软件回退即失败。

| 架构 / 脚本后端 | D3D11 | D3D12 | OpenGL Core | Vulkan |
| --- | --- | --- | --- | --- |
| x64 Mono | PASS | PASS | PASS | PASS |
| x86 Mono | PASS | PASS | PASS | PASS |
| x64 IL2CPP | PASS | PASS | PASS | PASS |
| x86 IL2CPP | PASS | PASS | PASS | PASS |

所有最终报告的 `fallback=` 均为空。实际传输模式分别为：

```text
D3D11 GPU conversion (no CPU readback)
D3D12 GPU conversion + shared-resource copy (no CPU readback)
OpenGL shared RGBA + GPU conversion (no CPU readback)
Vulkan GPU conversion + shared-resource copy (no CPU readback)
```

这些路径均避免 CPU 像素回读/上传，但包含 GPU 颜色转换；D3D12/Vulkan 还包含 GPU copy，不能称为严格的端到端零拷贝。四种后端同一时间点的 32×32 RGB 测试图与软件参考图比较，平均绝对差为 **0.006836/255**，单通道最大差为 **1/255**。此比较只覆盖测试素材，不能代替所有色彩空间的验证。

检查覆盖首帧预载、真实纹理像素、播放时间推进、倍速、暂停、seek、单帧步进、停止回零、连续 seek、循环、关闭及取消。额外的故障注入验证在 **D3D11 Mono、OpenGL Mono、Vulkan IL2CPP** 上各 **50 assertions 通过**：在纹理清空回调中 Pause、SeekAsync、Close，以及终止错误回调重新 Play，确认替换会话能继续工作且旧会话不会覆盖新状态。

Player 均开启 High managed stripping。Win64 IL2CPP 为 Release；Win32 IL2CPP 为 Debug C++。本机 Unity/MSVC 的 Win32 Release 引擎初始化崩溃已在空工程重现，不能将 Debug 结果扩展为 Release 验证。并行构建/播放时还出现过一次 Win32 Vulkan 的 Unity GfxDevice 内存不足；结束并行构建后单独重跑 Mono、IL2CPP 均通过，该压力场景不作为稳定性通过记录。

## 软件路径与其他平台

| 目标 | 结果与实际范围 |
| --- | --- |
| Windows x64 Mono，四种图形 API | 软件 RGBA 上传播放，各 16 assertions 通过 |
| Windows x86 Mono / IL2CPP，D3D11 | 软件 RGBA 上传播放，各 16 assertions 通过 |
| Linux x64 Mono / OpenGL Core | WSLg 中实际 Player 播放，16 assertions 通过；Mesa llvmpipe 软件光栅器，不代表 Linux 物理 GPU 驱动已验证 |
| Linux x64 / Vulkan | 当前 WSL 仅暴露 llvmpipe，Unity 在引擎启动阶段报告 `Forced renderer 21 is not supported`；未进入播放器测试，未验证 |
| Android ARM64 IL2CPP | APK 包含七个 FFmpeg ELF64/AArch64 库与 ABI3 桥接；真机严格硬件持续播放 33 assertions PASS、332 帧，无软件回退 |
| Android ARMv7 IL2CPP | APK 包含七个 FFmpeg ELF32/ARM 库与 ABI3 桥接；同一真机实际 32 位硬件持续播放 33 assertions PASS、332 帧，无软件回退 |
| macOS ARM64 / Metal | M4 原生解码加载 144 checks、真实 H.264 VideoToolbox → Metal / GPU 帧生命周期 231 checks PASS |
| macOS x64 | 原生 FFmpeg 与 Metal 桥接已构建；Mac 没有 Rosetta，未运行 x64 二进制 |
| iOS ARM64 / 模拟器 ARM64、x64 | 三套 FFmpeg + Metal 桥接构建及静态链接 PASS；ARM64 模拟器实际 Metal 与显式软件回退 116 checks PASS，无实体 iOS 硬解证据 |

Linux 已实现 VAAPI → DRM_PRIME/DMA-BUF → Vulkan，无 CPU 像素映射；由于 WSL 没有 `/dev/dri`，尚不能验证真实 VAAPI 导出与设备导入。共用 Vulkan GPU 转换层已在 llvmpipe 的实际 VkDevice 上验证两平面 NV12 / 单平面 RGB 共 8 次延迟提交、16384 字节像素、GPU fence 退休、取消、24 包上限及旧设备拒绝。

Android 已在两种 ABI 的实际 Unity Player 验证 MediaCodec → PRIVATE AImageReader → AHardwareBuffer → Vulkan，传输模式为 `Android MediaCodec AHardwareBuffer + Vulkan GPU conversion (no CPU readback)`，各 33 项断言、332 帧且 `fallback=` 为空。除基础 API/输出检查外，包含至少 300 帧连续播放、10 次连续跳转、有界帧队列和关闭后纹理/状态清空检查；持续播放覆盖帧回收，但未直接检查关闭后的原生待释放数量。这项短时持续测试不代表长时间稳定性测试。原生能力测试也在两种 ABI 上实际加载 FFmpeg/桥接并检查 4 个 MediaCodec decoder 和 AHB/SYNC_FD/FOREIGN/YCbCr 支持。

Apple 详情见 [APPLE-RESULTS.md](../../FFmpeg/APPLE-RESULTS.md)。官方 Unity Editor 安装包的区域重定向返回 404，未进行 Apple Unity Player 构建/运行；模拟器的显式软件回退不等价于实体 iOS VideoToolbox 验证。

## 解码、原生互操作与构建

- 真实 H.264 1920×1080、110.444 秒、29.970 fps：新增严格硬件失败与 native image 单次释放检查后 **305 assertions 通过**；此前 VP8 1080×1080、10 秒、60 fps 完整 **300 assertions 通过**。
- 覆盖元数据、PTS、前后 seek、seek 到结尾时的实际最后一帧、延迟帧排空、取消、有界队列，以及 RGBA/YUV 格式、full/limited range、四方向旋转、裁剪和尺寸边界。
- swscale 先写入 FFmpeg 对齐且带行尾填充的 AVFrame，再复制逻辑像素到 Unity RGBA，避免 SIMD 写越紧密排列的缓冲区。新增 **180 项 SIMD 尾部尺寸检查**，修复后 H.264 完整 300 项在五个独立进程中重复通过。
- D3D11 Win32/Win64 原生真实 GPU 检查通过，黑白像素为 0/255，验证 AVFrame 在 GPU 完成前存活。接口查询统一使用 `GetInterfaceSplit`，避免 Win32 MinGW/MSVC 非平凡 GUID 按值 ABI 不一致。
- D3D12 Win32/Win64 共享纹理和双向 fence 原生测试通过；最终生产代码链接测试覆盖连续四帧、取消及 AVFrame 生命周期。首轮 Unity 暴露的两个编译单元同名 `Packet` 析构冲突已通过唯一类型名修复，并经过两架构 Mono/IL2CPP 重测。
- WGL Win32/Win64 测试覆盖自有、可变借用及 Unity 风格不可变借用纹理，各十轮所有权切换、NV12 GPU 转换、错误上下文与注销重试。主线程退休收集器保证关闭最后一个播放器后仍能释放已注销的 Unity GL 纹理。
- Vulkan Win32/Win64 原生检查各 **64 帧、786432 字节精确比对通过**，覆盖自动退休及异步提交前释放调用方资源。实际 Unity 已确认当前 DLL 名称加 `isPreloaded: 1` 能够完成初始化拦截，无需改名。
- Linux 清除 `LD_LIBRARY_PATH`，先绝对路径加载 avformat 再解析依赖，**144 checks 通过**；H.264 实际解码 30 帧，七个库均来自产物目录且 RUNPATH 为 `$ORIGIN`。
- 70 个 FFmpeg 库通过 SHA256、PE/ELF/Mach-O 架构与 Apple SDK 平台检查（含忽略目录中的两套 iOS 模拟器库）；Linux 三个 VAAPI/DRM 运行依赖及许可检查通过。Android 两种架构 FFmpeg 和桥接所有 PT_LOAD 段至少 16 KiB 对齐。详见 [BUILD-RESULTS.md](../../FFmpeg/BUILD-RESULTS.md)。
- 新桥接已用 WSL 默认 MinGW GCC 13 的 x86/x64 工具链实际 CMake 编译链接通过；另以 MSVC Win32 编译并进行 D3D11 GPU 检查通过。没有将这些附加检查产物覆盖最终 DLL。
- Windows/Linux/macOS/Android 普通及 IL2CPP、iOS IL2CPP 共九组宏编译通过；Editor Inspector 和 iOS 后处理也使用实际 Unity/Xcode 托管 API 编译通过。宏编译不等价于设备运行。

播放器只输出视频；未进行音频输出或音画同步测试。现有 BGManager 和场景未自动替换。

## 重复执行与证据

命令见 [README.md](README.md)。可重复测试源码纳入仓库；生成的隔离项目、程序、日志和图片位于忽略的 `.work/`：

- `x64-Mono/`、`x64-IL2CPP/`、`x86-Mono/`、`x86-IL2CPP/`：各 API 的 `*-hardware.txt` / `.log` / `.txt.png`，以及单独的 `*-recovery.*`。
- `Linux-x64-Mono/`：Linux Player、OpenGL 通过记录与 Vulkan 启动限制日志。
- `Android-arm64-IL2CPP/`、`Android-armv7-IL2CPP/`：构建、打包、APK 与 `vulkan-hardware-device.*` 真机结果。
- `vulkan-portable-native.txt`、`linux-bridge-load.txt`：公共 Vulkan GPU 转换与 Linux ABI3 加载结果。
- `AppleRemoteEvidence/`：macOS / iOS 原生远端验证日志。
- `ManagedVulkanReview/`：加入 Linux/Android GPU 路径后的九组平台宏编译。
- `ManagedFinalReview/`：平台宏、Editor/iOS 后处理编译与 H.264 300 项日志。
- `linux-native.txt`：Linux 库加载、依赖与真实解码结果。
- `gcc-bridge-review/`：一键脚本实际选择的 WSL GCC x86/x64 桥接构建日志。

原生 DLL 的确切二进制与源码 SHA256、工具链、原生检查结果保存在各 Windows 目录的 `bridge-manifest.json`。构建缓存和测试输出不是发布依赖；运行产物及导入设置在 `Assets/Plugins/FFmpeg/Native/`。
