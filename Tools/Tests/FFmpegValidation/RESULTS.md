# 本机验证结果

最近验证日期：2026-10-05；此前跨平台矩阵执行于 2026-10-03。Windows / Unity 6000.3.17f1 / AMD Radeon RX 580 2048SP；Linux 使用本机 WSL Ubuntu 24.04；Android 真机为 Mi MIX 2S / Android 15 API 35 / Adreno 630 / Vulkan 1.1.128；Apple 构建测试使用用户提供的 Mac mini M4。当前 Windows/Linux/Android 图形桥接 ABI 为 **4**，Apple 保持 **2**；下方 ABI2/3 的记录为此前版本实测。

## 2026-10-05：四种软件编码器的最终原生库

全部十目标重新构建并交付 H.264/libx264、H.265/libx265、AV1/libaom-av1、VP9/libvpx-vp9，保留 MPEG4、已有硬件编码/解码和桥接 ABI。八套正式 Native 与两个独立 simulator 包已替换；**149 个既有 `.meta` 字节未变化**。固定版本、静态依赖与许可证见 [构建记录](../../FFmpeg/BUILD-RESULTS.md)，Apple SDK/链接与运行证据见 [Apple 记录](../../FFmpeg/APPLE-RESULTS.md)。

| 验证层 | 本轮最终产物结果 |
| --- | --- |
| `python Tools/FFmpeg/verify-artifacts.py` | **70 库 + 10 桥接 PASS**；各目标四个静态 PIC 编码器、GPL profile、双补丁、源码/许可证哈希、CPU/SDK/importer；实际 Windows x64 安全加载 ABI，Android 16 KiB 对齐 |
| `.NET 9` 独立托管检查 | **71 assertions PASS**；实际四软件格式完整编码 **4184 PASS**；MPEG4/真实 AMF 录制和后台会话 **839 PASS**；真实 H.264 播放/seek/回退 **446 PASS** |
| Windows x86、x64 | 每架构四格式 × CBR/VBR × 简单/复杂画面，16 文件、**31,820 checks PASS**；PATH 仅交付库与系统目录，未由开发工具链 DLL 提供依赖 |
| Linux x64 / WSL | 同一矩阵 **31,820 PASS**；清除 `LD_LIBRARY_PATH`，最终库无动态 libstdc++/libc++ 依赖，保留原有 libgcc_s/VAAPI 依赖 |
| Android ARM64、ARMv7 / 真机 | 每 ABI 同一矩阵 **31,820 PASS**，各 16 个结果文件回传；八库严格 ELF 依赖闭包通过，无 libc++_shared 或额外编译器 runtime SO |
| macOS ARM64、iOS simulator ARM64 | 每目标同一矩阵 **31,820 PASS**；AV1 8/10-bit 各 3570 PASS；Mac 真实 VT/Metal 231、simulator Metal/软件回退 116 PASS |
| Unity 6000.3.17f1 x64 Mono / Built-in / D3D11 | **416 Camera checks PASS**；实际 `libx264` 软件 H.264 CBR、GPU 回读/编码、暂停/恢复/停止/销毁和 PTS |
| 同一 Unity，x64 IL2CPP / Built-in / D3D11 | **416 Camera checks PASS**；同一 H.264 CBR 场景与真实原生回调，隔离工程编译和运行通过 |
| 主 Unity 工程导入/脚本编译 | Unity **6000.3.17f1** batchmode，退出码 **0**，无 C# 编译错误；没有修改正式场景或 Player Settings |
| `UNITY_IOS` 条件分支托管编译 | 独立验证项目 **0 errors**；仅编译证据，未作为 iOS Player/实体设备运行结果 |

最终 C fixture 以 C11 `-Wall -Wextra -Werror` 编译（Android GNU11），16 个文件分别逐帧解码验证 60 帧、像素/方向、跳帧 PTS、排空、线程上限与独占输出；还验证 x265 未知参数和无效值实际返回 `EINVAL`。x264/x265 使用完整 VBV 缓冲预算检查所有连续包窗口，x264 MP4 简单场景另外验证 CBR filler。libvpx/libaom 的 CBR/VBR 均使用原生码率预算，允许复杂画面超出目标，不以测试误称硬上限。详见 [NativeSoftwareEncoderSmoke.c](NativeSoftwareEncoderSmoke.c)。

本轮 FFmpeg 软件输出与以前 MPEG4-only 记录不同，托管包装器同时调整：H.264 CBR 可用于 MP4；checked x265 支持 strict-cbr；VP9/AV1 支持明确单次 VBR/CBR；worker 设置限制在 codec 可接受范围。两个原生补丁不改变公开 ABI。五个 WSL 与五个 Apple 目标的 decoder/hwaccel 名称集合与旧库逐项一致。

最终检查还修复 iOS 硬件身份边界：固定 VideoToolbox 包装器无法检查硬件独占选择，iOS Player 现在明确公开回退原因并使用同格式软件 encoder。macOS/EditorOSX 与其他平台选择顺序保留。使用独立测试项目 `-p:DefineConstants=UNITY_IOS` 编译该分支，未声称在实体设备运行过；日志为 `ios-managed-compile.log`。

```powershell
python Tools/FFmpeg/verify-artifacts.py
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-restore -v:q -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/FFmpegValidation/.work/full-software-validation --encode-software
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -TestCameraCapture -CaptureFormat H264 -CaptureRateControl CBR `
  -NativeDirectory Tools/FFmpeg/.build/full-encoding-ready/Windows/x86_64 `
  -WorkDirectory Tools/Tests/FFmpegValidation/.work/full-encoding-camera-final
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics d3d11 `
  -TestCameraCapture -CaptureFormat H264 -CaptureRateControl CBR `
  -NativeDirectory Tools/FFmpeg/.build/full-encoding-ready/Windows/x86_64 `
  -WorkDirectory Tools/Tests/FFmpegValidation/.work/full-encoding-camera-final
```

实际 Windows 托管运行使用已校验 ready 路径；正式替换后所有原生字节与 ready 相同，并在正式目录运行全矩阵 artifact verifier。原生矩阵的构建和运行命令保留于忽略的 `.build/compile-full-software-smokes.py`、`.build/full-encoding-20261005/run-windows-software-final.ps1`、`run-android-software.ps1`，Apple 命令随交付 evidence 保存。最终原始日志位于 `Tools/FFmpeg/.build/full-encoding-20261005/*-final.log`、`*-profile-verify.log`、`full-five-artifact-verify.log`、`formal-artifact-verify.log`；Apple 为 `.build/apple-full-encoding-ready-20261005/evidence`。Unity 报告位于 `.work/full-encoding-camera-final/x64-{Mono,IL2CPP}/d3d11-camera-builtin-software-H264-CBR.txt` 及对应录制/时序文件。

macOS x64、实体 iOS ARM64、iOS x64 simulator 仅编译/完整链接验证，运行**未验证**。本轮未验证 Android/Apple Unity Camera、Linux 硬件录制、NVIDIA GPU、其他分辨率的实时性能或完整发布包；不能从跨平台原生短 fixture 推断这些层已通过。

主工程 batch import 自动清空十处动态 TMP 字体缓存；这些文件在本轮开始和 import 前均干净、没有暂存改动，已保留清空版本备份并仅恢复这十处任务生成变化。用户原有 `FFmpegTest.unity` 改动保留；全局 `git diff --check` 仍报告该原有场景第 218 行空白，本轮范围的 diff-check 通过。

## 2026-10-05（较早）：Camera 暂停、帧率差异与自定义 Inspector

本轮修改 Camera 组件的暂停/恢复生命周期，添加 `FFmpegCameraCapturerEditor`；使用现有正式 Windows x64 原生库，没有修改原生 ABI、插件、子模块指针、主工程场景或 Player Settings。Unity 使用 **6000.3.17f1**，图形 API 为 D3D11，实际硬件编码器为 RX 580 的 `h264_amf`。

| 验证 | 结果 |
| --- | --- |
| `.NET 9 --encode-hardware` | **838 assertions PASS**；编译更新后的 Runtime，真实 MPEG4 软件 CBR/VBR、H.264 AMF 硬件 CBR/VBR 与后台会话回归 |
| Windows x64 Mono / Built-in / MPEG4 VBR | **415 checks PASS**；真实 Camera 渲染、GPU 回读、暂停/恢复、停止/取消/源销毁及解码 PTS |
| Windows x64 Mono / URP Stack / MPEG4 VBR | **540 checks PASS**；同上，加最终 Overlay 像素与暂停期间显示输出切换到显式 RenderTexture、渲染错误日志检查 |
| Windows x64 Mono / URP Stack / H.264 AMF CBR | **544 checks PASS**；同一已构建 Player 实际硬件编码，包含暂停与高/低配置帧率的 PTS 检查，禁止软件回退 |
| 隔离 Editor Play Mode / 生产 CustomEditor | **14 checks PASS**；真实 **215 Layout / 215 Repaint**，单对象录制/暂停/停止状态、Pause/Resume handler、多选与 FPS 采样；提交约 **31.13 FPS**，游戏约 **52.53 FPS**，短窗口提交速率可受队列追赶影响 |

三种 Player 录制的暂停恢复有效时长均约 **0.80 秒**，解码 PTS 为 **0..0.8**，最大相邻间隔为 **1/30 秒**，较长暂停未写入视频时间。控制源渲染时，配置 120 FPS 得到 Built-in **22 renders / 22 frames**、URP 软件和硬件各 **20 / 20**，首末 PTS 跨度约 **1.47 秒**；配置 5 FPS 时分别从 **38 / 34 / 35** 次源渲染得到 **8** 帧，PTS 为 **0..1.4**。结果验证录制帧率作为采样上限/时间基准，没有重复补帧，也没有把慢源画面压缩成短视频。

暂停测试等待真实 GPU/队列/编码 reservation 全部释放后再检查不新增帧，不假设固定 GPU 延迟；还验证立即重复暂停/恢复保持 PTS 单调，暂停时停止正常收尾，暂停后取消不能立即恢复，源 Camera 销毁会关闭会话。CBR 身份和 PTS 通过不表示所有缺帧场景都达到目标码率；本轮短窗口未增加恒定填充断言。

```powershell
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-restore -v:q -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/FFmpegValidation/.work/encoding-capturer-editor --encode-hardware
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -TestCameraCapture -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-builtin
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -CaptureUrp -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-urp
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -CaptureUrp -CaptureHardware -SkipBuild -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-urp
./Tools/Tests/FFmpegValidation/run-camera-editor.ps1
```

Player 报告、完整日志、实际编码信息和 `.timing.txt` 分别位于 `.work/camera-builtin/x64-Mono/d3d11-camera-builtin-software.*`、`.work/camera-urp/x64-Mono/d3d11-camera-urp-{software,hardware}.*`。Inspector 最终报告和日志位于 `.work/camera-builtin/inspector/camera-inspector.{txt,log}`。初始化失败尝试也保留：首次为 Unity Search 索引启动异常；后续为测试窗口恢复把 nullable error 字段变为空串造成的误判，已在 fixture 初始化时重置字段。日志监听限定实际 GUI/Camera 渲染及产品/fixture 调用栈，没有把 Unity 全局后台服务状态当成录制验收。

Inspector 开始/停止通过 runtime API，文件选择窗口与鼠标按钮点击未自动化；异步观察器和选择切换生命周期由源码审查覆盖。主工程场景/手动交互、此次暂停行为在 x86、IL2CPP、Linux、Android、macOS、iOS 和其他 GPU/API 上**未验证**；不能把此前原生库矩阵或本轮编译结果外推为这些运行验证通过。

结束时工作区另出现十处 TMP 字体资产改动，本轮未编辑或回退这些无关文件。

## 2026-10-05：各平台正式原生插件更新后的复验

本轮重建现有十个 FFmpeg 目标，八套正式插件已更新，两个 iOS 模拟器包独立保存。FFmpeg n9.0.1 ABI、公开绑定头文件和 136 个既有 `.meta` 保持不变；完整工具链、录制入口、许可证及平台结果见 [构建记录](../../FFmpeg/BUILD-RESULTS.md) 与 [Apple 记录](../../FFmpeg/APPLE-RESULTS.md)。下方较早的隔离录制构建与播放专用库记录是历史结果。

| 检查 | 当前新库结果 |
| --- | --- |
| `python Tools/FFmpeg/verify-artifacts.py` | 十目标 70 库、10 桥接，架构/SDK/哈希/importer/许可证/补丁全部 PASS；Windows x64 实际加载 ABI |
| 正式 Windows x64 `.NET --encode-hardware` | **838 assertions PASS**；MPEG4 软件 CBR/VBR 与 H.264 AMF 硬件 CBR/VBR、回退/模式身份、排空、取消及文件所有权 |
| 正式 Windows x64 `.NET` H.264 播放 | **446 assertions PASS**；真实解码/seek、硬件 CPU transport、有界队列与取消 |
| Windows x86 `EncoderNativeSmoke.c` | **2021 checks PASS**；实际 MPEG4 CBR/VBR 各 60 帧，worker 上限 2、像素/PTS、排空与独占输出 |
| Unity x86 Mono Camera / D3D11 | Built-in 软件与实际 H.264 AMF 硬件各 **197 checks PASS** |
| Unity x64 IL2CPP Camera / D3D11 | URP Camera Stack + 实际 H.264 AMF 硬件 **246 checks PASS** |
| Unity x86/x64 Mono 严格 GPU 播放 | 各 **31 assertions PASS**；D3D11VA 解码与 GPU 纹理转换，无 CPU 视频回读 |
| Linux 原生 | 加载/解码 **144 checks PASS**；桥接 ABI 4/无效参数保护 PASS；MPEG4 编码 **2021 checks PASS**，不依赖外部 `LD_LIBRARY_PATH` |
| Android 真机 ARM64/ARMv7 | 每 ABI MPEG4 编码 **2021 checks PASS**；八库/ABI 4/编码入口/设备前置 **54 checks PASS**；AV1 8/10-bit 各 **3570 checks PASS** |
| Mac M4 / iOS ARM64 模拟器 | MPEG4 各 **2021 checks PASS**；H.264/Metal、AV1 8/10-bit 回归见 Apple 记录 |

Windows 使用 Unity **6000.3.17f1**。x64 Camera 先使用已校验 ready 目录；Windows 正式库替换完成后字节与 ready 完全一致，正式目录另复跑 .NET 录制及播放。x86 Camera 与两架构 GPU 播放直接从正式 Native 复制插件。没有构建或修改主工程场景/Player Settings，也没有用根目录生成的 `.csproj` 做 .NET 构建。

```powershell
python Tools/FFmpeg/verify-artifacts.py
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-build -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/FFmpegValidation/.work/encoding-deployed --encode-hardware
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-build -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4"
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics d3d11 `
  -CaptureUrp -CaptureHardware -NativeDirectory Tools/FFmpeg/.build/recording-all-ready/Windows/x86_64 `
  -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-urp-il2cpp
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x86 -Graphics d3d11 `
  -TestCameraCapture -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-rebuilt-x86
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x86 -Graphics d3d11 `
  -CaptureHardware -SkipBuild -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-rebuilt-x86
# 对 x86 和 x64 各运行一次，用当前正式插件验证图形桥接：
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -RequireHardware
```

Windows 原始日志为 `.work/encoding-deployed.log`、`.work/decoding-deployed.log`、`.work/encoding-native-win-x86.log`、`.work/camera-rebuilt-{x86,x86-hardware,x64-il2cpp}.log` 与 `.work/playback-rebuilt-{x86,x64}.log`。Linux 为 `.work/linux-native.txt`、`.work/linux-encoder-recording.log`；Android 构建及运行证据归档在 `Tools/FFmpeg/.build/android-recording-evidence`，Apple 在 `.build/apple-recording-ready-20261005/evidence`。Windows DLL 占用解除后已完整替换；早期默认沙箱阻止 Unity UPM IPC 的构建尝试，在允许本地 IPC 后重跑通过。原 ZString nullable/ref-safety 与播放器未赋值字段编译警告仍存在，没有新增 C# 实现改动。

当前 macOS H.264 VT 的配置与 60 帧 roundtrip 通过，但 CBR + DataRateLimits 组合的 200,000 目标填充未通过，实际仅 11,072 bit/s。移除该独立最大率设置的诊断结果不能当作正式参数组合通过。Linux 无 `/dev/dri`、Android Vulkan 仅 1.1.128，故本轮没有验证 Linux 硬件录制或 Android Vulkan Video 1.3/Unity/MediaCodec GPU 帧。实体 iOS、Apple x64 运行、Apple Unity Player 及其他硬件编码格式仍未验证；不由原生编译、加载或低复杂度 fixture 结果推断通过。

## 2026-10-05：Camera 录制的托管配置与真实 FFmpeg 编码

使用固定 FFmpeg 9.0.1 ABI 的隔离 Windows x64 录制构建；本机实际 GPU 为 AMD Radeon RX 580 2048SP，驱动 31.0.21910.5。没有覆盖 `Assets/Plugins/MajdataPlay/FFmpeg/Native` 中原有播放专用 DLL，也没有修改 FFmpeg.AutoGen 子模块。测试产物与日志保存在忽略的 `.work/` 中。

| 验证 | 结果 |
| --- | --- |
| 默认 .NET 9 托管测试 | **71 assertions PASS**；原时钟、码率和帧池，加上编码选项边界与快照验证 |
| 原播放专用 DLL，`--encode-unavailable` | **78 assertions PASS**；实际编码器清单为空，打开 MP4 明确报告缺少输出封装器并要求重建录制库 |
| 隔离 MPEG4/NVENC 构建，`--encode` | **468 assertions PASS**；真实软件 VBR/CBR 编码、封装、RGBA 解码、PTS/帧数、延迟排空、取消和文件所有权 |
| AMF 检查补丁前的隔离构建，`--encode-hardware` | **838 assertions PASS**；全部软件检查，加上实际 `h264_amf / Hardware` 的 VBR 与 CBR 各 60 帧录制及逐帧像素/PTS/帧数验证 |
| 旧 AMF 构建，`--encode-unchecked-amf` | **85 assertions PASS**；CBR/VBR 均明确要求检查补丁的版本标记，拒绝未检查驱动属性的实现、不替换 H.264 格式、不创建或覆盖输出 |
| 当前带 AMF 检查补丁的构建，`--encode-hardware` | **838 assertions PASS**；驱动参数设置与初始化后回读通过，实际软件/硬件 CBR/VBR 编码、解码及所有原生/后台会话回归通过 |

软件 VBR 64×48 的正常/翻转录制与 78×48 的未对齐行宽检查均通过；`SoftwareThreadCount=1`，设置变更不会改变当前会话，跳过的 frame index 保留实际时间间隔。MPEG4 软件 CBR 的最后一秒为 **199,976 bit/s**，目标为 **200,000 bit/s**；恒定平坦画面仍产生目标附近的原生填充数据，60 帧均可正确解码。实际 AMD H.264 VBR 为 **12,448 bit/s**，CBR 为 **200,000 bit/s**，同样使用 200,000 的目标与 400,000 的配置上限；CBR 的有效上限收紧至目标。

AMF 构建的编码器清单为 `h263, mpeg4, av1_nvenc, av1_amf, h264_amf, h264_nvenc, hevc_amf, hevc_nvenc`。本机 NVENC 候选因 `Cannot load nvcuda.dll` 被拒绝，随后成功打开 AMD AMF；因此上述硬件结果只代表 H.264 AMF，不代表 NVIDIA、HEVC 或 AV1 的硬件编码已通过。录制库没有 `libx264`；其软件 H.264 CBR 请求被明确拒绝，没有以 MPEG4 代替请求的编码格式。

后台会话检查固定容量内的 GPU 读回、完成队列、缓冲区归还与有序编码；较晚帧的读回先完成时，仍须等待较早帧。Stop 等待未完成 GPU reservation 并排空，取消则无需等 GPU callback 即可释放 FFmpeg 文件句柄，迟到 callback 不会重开会话。预取消不创建文件，失败的打开不覆盖已有字节，正常/取消结束后可独占打开输出文件。

以下是 AMF 检查补丁前执行的原始命令；第二条原 DLL 测试不依赖任何编码器。AMF 库从隔离构建复制七个 FFmpeg DLL 和清单到专用 fixture 后运行，避免后续构建替换已加载的文件。新版编码器会拒绝这些旧 AMF fixture，硬件正例需要重建含检查补丁的库：

```powershell
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-build
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-build -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/FFmpegValidation/.work/encoding-unavailable --encode-unavailable
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-build -- `
  Tools/FFmpeg/.build/recording-ready/Windows/x86_64 `
  Tools/Tests/FFmpegValidation/.work/encoding --encode
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-restore -v:q -- `
  Tools/Tests/FFmpegValidation/.work/recording-amf-native `
  Tools/Tests/FFmpegValidation/.work/encoding-hardware --encode-hardware
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-build -- `
  Tools/Tests/FFmpegValidation/.work/recording-final-native `
  Tools/Tests/FFmpegValidation/.work/encoding-final-hardware --encode-hardware
```

证据为 `.work/encoding-{managed,unavailable,native,hardware}.log`，视频各自保存于 `.work/encoding*/<随机子目录>/`。第四条还编译当前 Camera、后台会话和编码器全部生产 C#，0 errors；既有 ZString nullable/ref-safety 与播放器未赋值字段警告仍存在。此 .NET 9 结果不代表 Unity Camera 最终渲染、GPU 读回、Mono/IL2CPP 或其他目标平台已验证；Unity Camera Player 结果另行记录。

检查补丁前的最终原生构建额外禁用了 AMF 解码包装器，以维持已有解码范围；复制至 `.work/recording-final-native` 后再次执行上述最后一条硬件模式，仍为 **838 assertions PASS**，实际编码器与各码率一致。此轮只针对解码范围变化进行确认，日志为 `.work/encoding-final-hardware.log`；不代表 AMF 驱动属性的 Set/Get 检查已经通过。

随后使用当前编码器与该旧 fixture 检查拒绝路径，**85 assertions PASS**，日志为 `.work/encoding-unchecked-amf.log`。诊断包含 `--extra-version=MajdataPlay-AMF-RC-v1-c0604b924b6a`，对应补丁 SHA256 `c0604b924b6ae1ee718c045d34f1448ebb78652ccacadfdb54799810c69e17f7`。两种码率模式均没有生成输出，重复打开已有文件仍保留原字节；请求格式保持 H.264。实际命令：

```powershell
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-restore -v:q -- `
  Tools/Tests/FFmpegValidation/.work/recording-final-native `
  Tools/Tests/FFmpegValidation/.work/encoding-unchecked-amf --encode-unchecked-amf
```

最终 Windows x64 构建保存到 `.work/recording-checked-native`，七个 DLL 的 SHA256、架构、ABI、原有解码器、录制 encoder/muxer、许可与 patch/marker 均通过验证，仍禁用 AMF 解码包装器。原生配置包含上述精确能力标记，补丁字节与完整 SHA256 一致。实际 H.264 AMF VBR 仍为 **12,448 bit/s**，CBR 为 **200,000 bit/s**，MPEG4 CBR 为 **199,976 bit/s**；本轮日志为 `.work/encoding-checked-hardware.log`，使用当前生产 C# 与已检查的驱动包装器执行：

```powershell
dotnet run --project Tools/Tests/FFmpegValidation/FFmpegValidation.csproj --no-restore -v:q -- `
  Tools/Tests/FFmpegValidation/.work/recording-checked-native `
  Tools/Tests/FFmpegValidation/.work/encoding-checked-hardware --encode-hardware
```

## 2026-10-05：Unity Camera 真实录制

Unity **6000.3.17f1** 的隔离 Windows x64 Player 使用当前带 AMF 检查补丁的 `.work/recording-checked-native`。测试通过真实 Camera.Render 或 URP 17.3 Standard RenderRequest 渲染 caller-owned RenderTexture，录制后逐帧重新解码 MP4；没有修改主工程场景或 Player Settings。

| Player / 管线 / 图形 API | 实际编码器 | 结果 |
| --- | --- | --- |
| Mono / Built-in / D3D11 | `mpeg4 / Software / VBR` | **197 checks PASS** |
| Mono / Built-in / OpenGL Core | `mpeg4 / Software / VBR` | **197 checks PASS** |
| Mono / Built-in / D3D11 | `h264_amf / Hardware / CBR` | **197 checks PASS** |
| Mono / URP Camera Stack / D3D11 | `h264_amf / Hardware / CBR` | **226 checks PASS** |
| Mono / URP Camera Stack / OpenGL Core | `mpeg4 / Software / VBR` | **222 checks PASS** |
| IL2CPP Release / URP Camera Stack / D3D11 | `h264_amf / Hardware / CBR` | **254 checks PASS** |

覆盖真实红/蓝像素与上下方向、帧数及单调 PTS、编码器身份与实际模式、实时码率、有效 CBR 上限、软件 worker 上限、设置快照、重复开始/停止、尾部排空、禁用后 GPU 资源释放、原 Camera 输出保留和预取消。另在同一 Unity 帧等待旧打开失败的 worker 结束、立即开启新会话，确认旧 continuation 不会停止或污染新录制；在 GPU 回读尚未完成时取消，确认重启被拒绝、文件未创建，等待 GPU 释放后的新录制正常完成。URP Base Camera 排除标记层，Overlay Camera 独立绘制绿色标记，逐帧像素检查确认录制包含完整叠加；显式目标的捕获在 end-context 后执行。断言数随实际捕获帧数变化。

复现最终 URP 检查的命令：

```powershell
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -CaptureUrp -CaptureHardware -NativeDirectory Tools/Tests/FFmpegValidation/.work/recording-checked-native `
  -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-urp
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics glcore `
  -CaptureUrp -SkipBuild -NativeDirectory Tools/Tests/FFmpegValidation/.work/recording-checked-native `
  -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-urp
./Tools/Tests/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics d3d11 `
  -CaptureUrp -CaptureHardware -NativeDirectory Tools/Tests/FFmpegValidation/.work/recording-checked-native `
  -WorkDirectory Tools/Tests/FFmpegValidation/.work/camera-urp-il2cpp
```

Built-in 使用 `-TestCameraCapture`，硬件模式再加 `-CaptureHardware`，工作目录为 `.work/camera-builtin`。报告和编码器元数据位于这些目录的 `x64-Mono/` 或 `x64-IL2CPP/` 下，文件名为 `*-camera-*.txt` / `.txt.metadata.txt`；实际 Diagnostics 日志、原生输出和 Player 日志均保存在忽略目录内。

Windows 隐藏 Player 会抑制普通显示 Camera 的自动渲染，因此使用显式离屏请求作为可复现的 GPU 验证。这些结果不等价于主工程显示输出的 CameraCaptureBridge、UI、HDR、XR 或自定义 SRP 已验证。主工程 Play Mode、Windows x86、D3D12/Vulkan、Linux、macOS、Android/iOS 的相机录制，以及 NVENC/VAAPI/VideoToolbox、HEVC/VP9/AV1 编码均**未验证**；此前的播放/解码矩阵不能用于推断录制成功。

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
