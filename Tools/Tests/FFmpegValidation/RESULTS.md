# 本机验证结果

最近验证日期：2026-10-05；此前跨平台矩阵执行于 2026-10-03。Windows / Unity 6000.3.17f1 / AMD Radeon RX 580 2048SP；Linux 使用本机 WSL Ubuntu 24.04；Android 真机为 Mi MIX 2S / Android 15 API 35 / Adreno 630 / Vulkan 1.1.128；Apple 构建测试使用用户提供的 Mac mini M4。当前 Windows/Linux/Android 图形桥接 ABI 为 **4**，Apple 保持 **2**；下方 ABI2/3 的记录为此前版本实测。

## 2026-10-05：播放会话逐帧 GC 分配

`ReadFrame` 的 EAGAIN 检查改用平台 errno 的负值，避开 AutoGen 泛型宏装箱。播放器内部按队列容量加 2 预分配帧容器，硬件图像释放委托按会话缓存。公开 decoder 和 `CopyToSoftware()` 仍返回独立对象；本次零 GC 结论限于播放器内部复用路径，不表示 FFmpeg 原生内存也不分配。

| 验证 | 结果 |
| --- | --- |
| .NET 9 / Windows x64，`--allocations` | **379 assertions PASS**；分配计数器正向校准、帧所有权、异常释放、容量 1/8 会话播放/连续 seek/关闭 |
| 帧池租用/归还 | 4096 次 **0 B** 托管分配 |
| RGBA / YUV 转换（含四方向旋转） | 各 512 帧 **0 B** 托管分配 |
| 软件解码 / D3D11VA CPU 上传 / D3D11VA 原生帧 | 各 120 帧 **0 B** 托管分配 |
| Unity Editor Mono x64 原生 Profiler | 正向校准捕获 1 次数组 `GC.Alloc`；上述帧池、转换、三种解码路径在相同批次内均 **0 次 GC.Alloc**，140 项断言及校准通过 |
| 原有真实 FFmpeg 回归 | **412 assertions PASS**；解码、seek/EOF、取消、有界队列及像素转换 |
| Unity 6000.3.17f1 x64 Mono / D3D11 Player | 软件 **27**、严格 GPU **31**、硬解 CPU 上传 **31 assertions PASS**，含实际纹理和播放控制 |

托管测试命令见 README 的“每帧托管分配与帧所有权”，日志为 `.work/gc-free/managed-allocations.log`。Unity Player 使用 `run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -WorkDirectory Tools/Tests/FFmpegValidation/.work/gc-free` 构建；同一 Player 以 `-SkipBuild -RequireHardware` 和 `-SkipBuild -HardwareCpuUpload` 分别验证另两条路径。报告位于 `.work/gc-free/x64-Mono/d3d11-{software,hardware,hardware-cpu}.txt`。

相邻热路径静态检查包括播放器 Update/Present、呈现器命令缓冲区、Vulkan 互操作、码率窗口、时钟、UnityProfiler 和 AutoGen 矩阵/数组传参，未发现其他稳定逐帧托管分配。首次初始化、非阻塞 I/O 的首次 WaitHandle、日志、错误/回退、纹理重建和调用方事件处理器不属于该结论。本轮未验证主项目场景、IL2CPP、Android、Apple、Linux 或其他图形后端；未更改原生库、子模块及资产 GUID。

Unity Mono 的 `GC.GetAllocatedBytesForCurrentThread()` 连已知数组分配也返回 0，未通过正向校准，其零值结果不作证据。改用启用的 `ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 65536, StartImmediately | CollectOnlyOnCurrentThread)`，不启用逐帧合计，通过采样数量差核验同步测量循环；检查有效性、运行状态及缓冲区未溢出。该 marker 的单位为耗时，不能将采样值解释为分配字节。隔离工程的测试副本只将分配读数换为 recorder 事件数并省略会话压力检查，后者由 .NET 测试和 Player 覆盖。使用 `-batchmode -nographics -quit -executeMethod FFmpegAllocationProbe.Run` 执行；测试副本、probe、报告与日志分别保存在 `.work/gc-free/Project-x64-native-video/Assets/Plugins/FFmpeg/Runtime/`、`.work/gc-free/mono-profiler-allocations.txt` 及 `mono-profiler-allocations-editor.log`，未修改正式程序集。

## 2026-10-05：AV1 软件解码与硬件失败恢复

旧原生库仅包含依赖硬件的 `av1` 解码器。使用 128×96 / 30 fps / 3 秒的 AV1 样本，软件模式首帧准确复现 `Send video packet: Function not implemented (-40)`。修复后软件模式明确选择 `libdav1d`（兼容 `libaom-av1`），硬件模式保留原生 `av1` / MediaCodec 候选；Windows x64 原生库加入静态 dav1d 1.5.3，FFmpeg 保持固定 9.0.1 ABI。

| 验证 | 结果 |
| --- | --- |
| 新代码 + 旧库，`--av1-unavailable` | **41 assertions PASS**；打开阶段明确报告缺少 AV1 软件解码器 |
| 新库，8-bit / 10-bit AV1，`--av1` | 各 **134 assertions PASS**；真实软件像素、seek/EOF、会话、硬件候选和严格模式 |
| 新库，原 H.264 托管/真实解码回归 | **412 assertions PASS**；D3D11VA 硬解和 CPU 下载仍通过 |
| Unity x64 Mono / Vulkan，8-bit / 10-bit AV1，硬件偏好 | 各 **25 assertions PASS**；硬件失败后回退 `libdav1d / Software RGBA upload`，包含实际纹理和完整播放控制 |
| Unity x64 Mono / D3D11，8-bit AV1，软件偏好 | **27 assertions PASS** |
| Unity x64 Mono / Vulkan，原 H.264 回归（更新原生库前） | **29 assertions PASS**；保持 GPU 纹理路径 |

托管命令与素材生成方法见 README 的 AV1 专节。证据为 `.work/av1/{baseline-old-libraries,missing-software,av1-8bit-dav1d,av1-10bit-dav1d,h264-dav1d-regression}.log`。

Unity 6000.3.17f1 使用 `run-unity.ps1 -Backend Mono -Architecture x64 -Graphics vulkan -Hardware -WorkDirectory Tools/Tests/FFmpegValidation/.work/av1/unity-h264` 编译本次源码并完成 H.264 验证。随后复制该 Player 至独立 `.work/av1/unity/x64-Mono`，替换已校验的新 DLL 和 AV1 素材，以 `-SkipBuild -Media <AV1素材> -WorkDirectory Tools/Tests/FFmpegValidation/.work/av1/unity` 运行；托管程序集 SHA256 与本次编译产物一致。8-bit 报告保存在该 Player 的 `8bit-evidence/`，10-bit 报告为 `vulkan-hardware.txt` 及对应日志。软件偏好使用 `-Graphics d3d11` 且不带 `-Hardware`。

Vulkan 日志仍可看到 RX 580 两次拒绝 AV1 硬件格式的工作线程错误，随后成功重开 `decoder=libdav1d, device=Software`，没有终止播放错误。这证明完整软件恢复，**不代表 AV1 硬解通过**。以上是 Windows x64 首轮修复验证，其他目标的后续结果如下；未验证用户原始 1080p 视频，也未对主项目做完整导入/场景测试。主项目用户资产未被测试脚本修改。

## 2026-10-05：跨平台 AV1 8-bit / 10-bit 软件解码

同一固定 FFmpeg 9.0.1 ABI 加入静态 dav1d 1.5.3。构建脚本显式选择低位深和高位深实现，并检查 `CONFIG_8BPC` / `CONFIG_16BPC`；10-bit 使用后者。原生验证程序为 `Av1SoftwareSmoke.c`，两段 128×96 / 30 fps / 3 秒测试图分别为 8-bit 和 10-bit AV1；各素材完整解码 90 帧，每个位深各 **3570 checks**，覆盖实际组件位深、软件 context、RGBA 像素变化、PTS、EOF 排空和前后 seek 的帧数/像素复现。

| 目标 | 原生 AV1 8-bit / 10-bit | Unity Player AV1 8-bit / 10-bit |
| --- | --- | --- |
| Windows x64 | 各 3570 checks PASS；真实 64 位进程 | 前述 Mono / Vulkan 硬件回退，各 25 assertions PASS |
| Windows x86 | 各 3570 checks PASS；真实 32 位进程 | IL2CPP / D3D11 软件上传，各 27 assertions PASS |
| Linux x64 | 各 3570 checks PASS；WSL Ubuntu 24.04 | Mono / OpenGL Core 软件上传，各 27 assertions PASS |
| Android ARM64 | 各 3570 checks PASS；Mi MIX 2S 真实 64 位进程 | IL2CPP / Vulkan 软件上传，各 29 assertions PASS |
| Android ARMv7 | 各 3570 checks PASS；同一真机真实 32 位进程 | IL2CPP / Vulkan 软件上传，各 29 assertions PASS |
| macOS ARM64 | 各 3570 checks PASS；Mac mini M4 | 未验证 |
| macOS x64 | 构建、AV1 测试链接、架构检查 PASS；无 Rosetta，未运行 | 未验证 |
| iOS ARM64 | 构建、AV1 测试静态链接、SDK 平台检查 PASS；无实体设备，未运行 | 未验证 |
| iOS 模拟器 ARM64 | 各 3570 checks PASS；iOS 27 模拟器 | 未验证 |
| iOS 模拟器 x64 | 构建、AV1 测试静态链接、SDK 平台检查 PASS；未运行 | 未验证 |

所有原生验证以 `-std=c11 -Wall -Wextra -Werror` 编译。iOS 测试只链接 stage 后的七个 FFmpeg 静态库及现有系统依赖，没有单独输入 `libdav1d.a`，验证它已合并进 `libavcodec.a`。正式插件保留原有桥接库和已有 `.meta`。Apple 的额外 H.264 / Metal 原生回归及独立桥接产物测试范围见 [APPLE-RESULTS.md](../../FFmpeg/APPLE-RESULTS.md)。

最终正式库及两个模拟器包共 **10 个目标 / 70 个 FFmpeg 库**，均在构建清单中包含 `libdav1d`。Windows 与 WSL（清除 `LD_LIBRARY_PATH`）分别执行 `python Tools/FFmpeg/verify-artifacts.py` / `python3 Tools/FFmpeg/verify-artifacts.py` 全部 PASS，包含 SHA256、架构、Apple SDK 平台、Android 16 KiB 对齐、运行依赖/许可和本机 ABI / 解码器导出。全部 **129 个已有 `.meta`** 与更新前逐字节相同；新增 dav1d 许可和 `.meta` 成对交付，许可固定 LF，避免 Git 换行转换影响清单哈希。

Unity 使用固定 **6000.3.17f1** 和 High managed stripping。Win32 IL2CPP 继续使用现有 Debug C++ 设置；Linux 在 WSLg 的 llvmpipe 上显示真实纹理并检查播放控制，不能外推为物理 GPU 测试。10-bit 软件帧转换到现有 RGBA32 上传路径，这些结果不表示 HDR 或 10-bit 显示输出已支持。

Windows x86、Linux 使用以下隔离验证命令构建并运行 8-bit；保存证据后，将 Player 内 `StreamingAssets/test.mp4` 换成 10-bit 样本，复用同一 Player（Windows 加 `-SkipBuild`）运行第二遍：

```powershell
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x86 -Graphics d3d11 -Media Tools/Tests/FFmpegValidation/.work/av1/test-av1.mp4 -WorkDirectory Tools/Tests/FFmpegValidation/.work/av1-all/unity
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Platform Linux -Backend Mono -Architecture x64 -Graphics glcore -Media Tools/Tests/FFmpegValidation/.work/av1/test-av1.mp4 -WorkDirectory Tools/Tests/FFmpegValidation/.work/av1-all/unity
wsl -d Ubuntu-24.04 -- bash Tools/Tests/FFmpegValidation/run-linux-player.sh glcore Tools/Tests/FFmpegValidation/.work/av1-all/unity/Linux-x64-Mono
```

Android 每种 ABI 编译一个 APK，以 `run-android-player.ps1 -Av1Software -Media <素材>` 在测试应用目录上传素材并用 Intent 选择 AV1 软件检查；第二个位深加 `-SkipInstall` 复用同一个 APK。测试明确要求 `CodecName=av1`、`DecoderName=libdav1d`、`DecoderType=Software` 和 `Software RGBA upload`，并保留实际纹理 PNG。默认硬件压力测试入口保持原行为；软件模式允许 GLES3，记录的本机实测使用 Vulkan。

证据：`.work/av1-all/{win32,win64}/native-av1.log`、`.work/av1-all/unity/{x86-IL2CPP,Linux-x64-Mono}/`（`8bit-evidence/` 保留第一轮）、`.work/av1-all/android-*-native/{native-av1.log,unity-8bit.txt,unity-10bit.txt}` 及旁边的诊断/纹理图、`Tools/FFmpeg/.build/av1-all-evidence/`、`Tools/FFmpeg/.build/av1-all-ready/Apple/evidence/`。未对主项目场景、用户原始视频、Apple Unity Player 或全部编码参数组合进行验证。

## 实时视频码率

2026-10-04：Inspector 同时显示 `Bitrate (current)` 和 `Bitrate (average)`。实时码率按当前显示帧之前约 1 秒的媒体时间估算视频包字节量，随帧传递；预读和倍速不放大数值，暂停保留读数，跳转、关闭和硬解恢复清空旧值。

- 托管与真实 FFmpeg 验证 **412 assertions PASS**，包含 31 项码率窗口边界测试，以及软件/硬解帧、前后 seek 和 EOF 末帧快照；证据 `.work/current-bitrate-decoder.log`。
- Runtime 与 Editor 开启 `ENABLE_PROFILER` 编译通过，**0 errors**；证据 `.work/current-bitrate-editor-build.log`。
- Unity 6000.3.17f1 Windows x64 Mono / D3D11：软件播放 **27 assertions PASS**，原生 GPU 硬解播放 **31 assertions PASS**，硬解故障恢复及回调重入 **66 assertions PASS**。覆盖预载、暂停、倍速、seek、关闭与恢复期间的码率状态；证据 `.work/x64-Mono/d3d11-{software,hardware,recovery}.txt`。

本次没有改动原生库，也未重跑其他图形 API、IL2CPP 或移动/Apple/Linux 平台。当前码率是压缩视频负载的局部估计，不包含音频、容器及网络传输开销。

## TryPresent 缓存与解码 Profiler

2026-10-04：D3D11 复用相同尺寸/格式的视频处理器、枚举器和当前输出视图，每个提交仍独立持有 COM/AVFrame 引用和完成查询。托管层缓存 GPU copy target 的原生指针，在尺寸变化或纹理丢失后重建；软/硬解送包、取帧、排空及呈现阶段增加独立 CPU 标记。

- 同机、相同 O3 编译的原生 smoke，优化前后交替运行五轮，每轮预热后对 32×32 NV12 帧执行 256 次 `prepare/cancel`，取每轮平均值的中位数：x64 **262.179 → 4.559 µs**，x86 **478.918 → 5.780 µs**。这仅测量原生准备/取消的 CPU 时间，未包含 Unity 命令录制、提交、真实视频解码或 GPU 执行，不能直接当作整个 `TryPresent` 的新耗时。
- x64/x86 原生 GPU smoke 均通过，覆盖 NV12 array slice、limited/full range 像素、同尺寸输出替换、取消、32→64×48→32 尺寸切换、多个待提交帧及 presenter 先释放后的安全回收。
- 最新源码与新 x64 bridge 的隔离 Unity Mono Player：软件 D3D11 **20 assertions PASS**；严格硬件 D3D11 / OpenGL 各 **24 assertions PASS**，D3D12 / Vulkan 各 **25 assertions PASS**。新增输出/copy target 主动释放后步进并验证实际像素；D3D12 / Vulkan 实际使用 D3D11VA 共享路径，无 CPU 像素回读，未验证原生 D3D12VA / Vulkan Video 解码。
- 真实 FFmpeg 解码测试 **361 assertions PASS**；启用 `ENABLE_PROFILER` 编译 **0 errors**。未录制 Unity Profiler Timeline；此次未重跑 IL2CPP、Apple、Android 或 Linux 设备矩阵。

证据：`Tools/FFmpeg/.build/native-video-bridge-win-{x64,x86}/D3D11-cache-comparison.log`、`.work/decode-profiler-enabled-build.log` 与 `.work/x64-Mono/` 下本轮软件/硬件报告。Profiler 标记的阅读方式见 README 的 CPU Profiler 章节。

## D3D12VA 与 Vulkan Video 接入验证

2026-10-04 更新：Windows x86/x64 的 FFmpeg 启用 D3D12VA 与 Vulkan，Linux x64 和 Android ARM64/ARMv7 启用 Vulkan。实际库的 H.264/HEVC/AV1/VP9 硬件配置已检查；FFmpeg 仍锁定 n9.0.1，Vulkan-Headers 与 Windows D3D12 头文件依赖记录版本和哈希。Apple 原生产物保持原版本。

- 托管真实 FFmpeg 测试 **361 assertions PASS**，新增原生设备创建失败后切换 D3D11VA 的严格/非严格模式测试，检查实际硬件类型、帧存储与 seek。13 组平台、IL2CPP、Editor/Profiler 宏编译全部通过；严格英文 XML 文档检查通过，共 **128 个条目**。
- Windows x64 **Mono / IL2CPP × D3D12 / Vulkan** 严格 GPU 播放各 **20 assertions PASS**。D3D12 实际先尝试 D3D12VA，受下述限制后重新打开 D3D11VA；Vulkan 报告缺少 video queue/decode queue 扩展（状态 403），走 D3D11VA 共享路径。两者均无 CPU 像素回读，不算原生新后端解码成功。
- Windows x64 Mono / D3D12：硬件 CPU 上传 **24 assertions PASS**，软件/硬件偏好及 GPU→CPU 恢复 **37 assertions PASS**，回调重入故障恢复 **52 assertions PASS**。
- Windows x86 Mono 的 D3D12 / Vulkan 严格 GPU 播放各 **20 assertions PASS**；x64 IL2CPP / D3D12 偏好与硬件 CPU 恢复 **37 assertions PASS**。x86/x64 原生 D3D12 人工帧、D3D11 回归及 Vulkan 协商测试均通过。
- Android ARM64 / ARMv7 IL2CPP 最终 APK 真机均 **54 assertions PASS、341 帧**，包括持续播放、10 次连续 seek、偏好切换、MediaCodec ByteBuffer CPU 上传与 GPU 恢复。新后端报告 Vulkan 1.3 要求不满足（状态 401），实际显示路径为 MediaCodec AHardwareBuffer，保持无 CPU 像素回读。ARMv7 的 Windows Ninja 路径长度问题已通过缩短隔离工程目录解决。
- D3D12 新转换器的实际 GPU 测试：**100 帧、四次像素参考比较通过**，覆盖 NV12 limited/full range、尺寸变化、取消、AVFrame/帧池引用与 drain；启用 D3D12 debug layer 后没有警告或错误。测试使用人工填充的 D3D12 硬件帧，其上传和读回仅在测试中执行，不能代替视频码流硬件解码验证。
- Vulkan 协商测试通过，覆盖功能链保留、独立队列映射、实际 FFmpeg AVBuffer 引用计数与延迟销毁 VkDevice/VkInstance。llvmpipe 上的实际 Vulkan compute 测试通过，覆盖 8 次 GPU 提交、16384 字节像素、4 次 timeline semaphore 锁定/等待/信号、资源退休、取消、有界队列与旧设备拒绝。
- 最终五个桥接库的二进制及全部源码哈希与各自 manifest 一致；Linux ABI4 桥接在清空 `LD_LIBRARY_PATH` 后加载、依赖定位及缺失设备的安全拒绝检查通过。Windows / WSL 产物验证均通过：70 个 FFmpeg 库、10 个桥接产物；本轮重建其中 35 个 FFmpeg 库及 5 个桥接库，其余 Apple 产物未变。

**硬件限制：** RX 580 报告的 D3D12 视频解码 Tier 1 尚未由锁定 FFmpeg 版本实现，实际 H.264 原生 D3D12VA 测试返回 **SKIP 77**，不是通过。本机 Windows 驱动未暴露 Vulkan Video 扩展，Android Adreno 630 仅 Vulkan 1.1，WSL 没有支持视频解码的物理 Vulkan GPU。因此新 D3D12VA/Vulkan Video 的完整码流→显示正向测试仍需支持的设备；不把编译、人工帧或旧后端回退作为该项验收通过。可在支持的 Windows 设备运行 `run-unity.ps1 -Graphics d3d12|vulkan -RequireNativeDecoder`，它会拒绝将旧后端回退判为成功。

托管证据：`.work/native-video-decoder-tests.log`、`.work/NativeVideoManagedReview/`、`.work/NativeVideoApiReview/`；Unity 证据：`.work/x64-Mono/`、`.work/x64-IL2CPP/`、`.work/x86-Mono/`、`.work/Android-arm64-IL2CPP/`、`.work/Android-armv7-IL2CPP/` 内本轮报告及 diagnostics 日志。旧章节保留历史测试范围，不表示本次重新运行完整跨平台矩阵。

## 删除兼容解码偏好属性

2026-10-04 后续更新：移除 `PreferHardwareDecoding`，测试调用统一使用 `PreferredDecoderType`，并删除四项旧布尔属性映射断言，保留修改偏好后实际会话身份不变的检查。更新后的 `UnitySmoke.cs` 已与最新 Runtime、真实 Unity/AutoGen/Diagnostics 依赖一起编译通过；严格 XML 文档检查通过，现为 **128 个条目**。本次仅执行编译验证，证据在 `.work/DecoderPreferenceApiReview/`；下方 41/58 等播放断言数量保留为删除兼容性断言之前的历史实测记录，没有据此宣称新版 Player 已重跑。

## API 命名、XML 文档与 Profiler 调整验证

2026-10-04 更新：秒单位时间属性改为 `TimeSeconds`，测试调用已同步更新；移除旧小写别名，公共 API 使用 PascalCase 并补充英文 XML 文档。调用处不再使用额外的 Profiler 条件编译包装。本轮未修改原生代码，也未重新执行此前全部设备矩阵。

- 独立编译实际 FFmpeg Runtime 程序集，引用隔离 Unity 工程生成的真实 `FFmpeg.AutoGen`、`MajdataPlay.Diagnostics` 依赖。开启 XML 文档输出并将 `CS1591`、XML 格式、参数与引用相关警告设为错误：**PASS，129 个 XML 条目，无缺失公共成员文档或 XML 警告**；仍有两个既有的 Unity 序列化字段 `CS0649` 提示。
- 九组平台/IL2CPP 与四组 Editor/Debug/Profiler 宏组合共 **13 组编译 PASS**，包含未定义 `ENABLE_PROFILER` 和启用它的组合，以及实际 Inspector/iOS 后处理源码。
- 无 `ENABLE_PROFILER` 的 .NET 验证进程运行真实 FFmpeg，**335 assertions PASS**，移除外层条件编译后未出现 Unity 原生 Profiler 调用异常。
- Windows x64 Mono / D3D12 重新构建并运行首选解码器与硬件 CPU 回退测试，**41 assertions PASS**；Windows x64 IL2CPP / Vulkan 重新构建并运行故障恢复测试，**52 assertions PASS**。后者实际经过使用 `TimeSeconds` 的恢复后跳转检查。两组最终均恢复原生 GPU 显示路径，`fallback=` 为空。

文档检查与汇总在 `.work/ApiDocumentationReview/`；宏编译证据在 `.work/DecoderTransportManagedReview/`；真实解码日志为 `.work/api-rename-native-tests.log`；Unity 报告为 `.work/x64-Mono/d3d12-decoder-preference.txt` 和 `.work/x64-IL2CPP/vulkan-recovery.txt`。本轮没有录制或人工检查 Profiler 窗口，启用 Profiler 的编译通过不等同于界面采样验证。

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

原生 DLL 的确切二进制与源码 SHA256、工具链、原生检查结果保存在各 Windows 目录的 `bridge-manifest.json`。构建缓存和测试输出不是发布依赖；运行产物及导入设置在 `Assets/Plugins/MajdataPlay/FFmpeg/Native/`。
