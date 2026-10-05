# Apple 录制原生库重建与验证（2026-10-05）

## 四种软件视频编码器补全

本轮再次重建全部五个 Apple 目标，静态并入 x264 `0.165-stable`、x265 `4.1`、libvpx `1.17.0` 和 libaom `3.13.3`，统一提供 H.264/H.265/VP9/AV1 软件 CBR/VBR 编码。保留 MPEG4、H.264 VideoToolbox、全部既有解码器、dav1d 与 Metal 桥接。组合库为 GPL version 3 or later，随包保存四个依赖的版权、专利及适用第三方声明；系统 libc++ 不随包复制。

构建目录为 `/Users/codex/codex-work/majdata-full-encoding-apple-20261005`，未覆盖此前目录。环境为 Apple M4、macOS 27.0.1、Xcode 27.0（27A266a）、SDK 27、AppleClang 21.0.0、Python 3.14.5、Ninja 1.13.2。系统 CMake 4.4.2 继续用于图形桥接；固定 x265 4.1 使用官方 CMake 3.31.10 macOS universal2 wheel 中的专用工具，wheel SHA256 `ad697643a00d9ba85179590a383c4f7401169b55ebf4b8b2938daf28c6bdeb6d`，无系统软件安装。

两份检查补丁、固定 FFmpeg/依赖 commit 和 SHA256 见 [本轮构建记录](BUILD-RESULTS.md)。143 个 AutoGen 公开头文件逐一核对，七库主版本与桥接 ABI 2 不变。x264 ARM 汇编显式使用目标 SDK/平台的编译器汇编器；iOS x64 的 libaom 使用 Darwin Mach-O 汇编对象与明确 iPhoneSimulator SDK，避免混入 ELF 或 macOS device 对象。

| 目标 | 编译/链接 | 本轮实际运行 |
| --- | --- | --- |
| macOS ARM64 | 七 dylib + Metal 桥接 PASS | 四软件格式 × CBR/VBR × 简单/复杂场景：**31,820 checks PASS**；七库加载 **142**；真实 VT/Metal **231**；AV1 8/10-bit 各 **3570 PASS** |
| macOS x64 | 七 dylib、桥接及三种 C/C++ fixture 链接 PASS | 未验证：无 Rosetta，执行 errno 86 |
| iOS device ARM64 | 七静态归档、桥接及三种 fixture 完整链接 PASS | 未验证：没有实体设备 |
| iOS simulator ARM64 | 七静态归档、桥接及三种 fixture 完整链接 PASS | 四软件格式同一矩阵 **31,820 PASS**；AV1 8/10-bit 各 **3570 PASS**；Metal/软件回退 **116 PASS**，VT 硬解明确 SKIP |
| iOS simulator x64 | 七静态归档、桥接及三种 fixture 完整链接 PASS | 未验证：没有 x64 运行支持 |

全五目标 **35 库 + 5 桥接** 的哈希、CPU、SDK 平台、补丁、GPL profile 和许可证检查通过。macOS 两架构的动态依赖没有额外 x264/x265/vpx/aom dylib，仅使用交付 FFmpeg、系统框架及系统 libc++；iOS 链接测试只链接交付的七个归档和桥接，无需独立编码器归档。所有 ARM64 实际编码文件均逐帧解码核对像素/PTS/排空/worker 上限，未知及无效 x265 参数各实际返回 `EINVAL`。

本轮未运行 Apple Unity Player、实体 iOS 或 Apple x64 程序，也未新增 VideoToolbox 硬件编码实测。其 CBR 组合限制沿用下方历史结果。四种软件编码器的最大码率预算、CBR 填充差异及 ARM 性能限制见 [构建记录](BUILD-RESULTS.md)；短测试不能代表高分辨率实时性能。

## 较早的 MPEG4-only 录制构建

本轮按现有录制 profile 实际重建全部五个 Apple 目标：macOS ARM64/x64、iOS ARM64、iOS 模拟器 ARM64/x64。每个目标均重新编译七个 FFmpeg 库与 Metal 桥接，显式包含 `mpeg4`、`h264_videotoolbox` 编码器和 `mov/mp4/matroska/webm/avi` muxer，保留 dav1d 1.5.3 的 AV1 8/10-bit 软件解码。正式 macOS 两套及 iOS device 插件已更新；两套模拟器归档仅放在忽略的 `Tools/FFmpeg/.build/artifacts`。已有 44 个 Apple `.meta` GUID 均保留。

构建固定 FFmpeg `n9.0.1`、commit `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`，逐一比较 143 个现有公开绑定头文件，再应用已审查的 AMF 检查补丁。补丁 SHA256 为 `c0604b924b6ae1ee718c045d34f1448ebb78652ccacadfdb54799810c69e17f7`，版本标识为 `MajdataPlay-AMF-RC-v1-c0604b924b6a`；Apple 不编入 AMF，但仍记录相同源码 provenance。公开头文件、FFmpeg 主版本和 Apple 桥接 ABI 2 未改变。

远端使用独立目录 `/Users/codex/codex-work/majdata-recording-apple-20261005`。以 APFS clone 复制此前干净缓存，再同步当前构建工具、绑定头文件与验证源码，未覆盖远端既有工作区或修改子模块。环境为 Apple M4、macOS 27.0.1、Xcode 27.0（27A266a）、macOS/iPhoneOS/iPhoneSimulator 27 SDK、AppleClang 21.0.0、Python 3.14.5、CMake 4.4.2、Ninja 1.13.2、任务缓存 NASM 2.16.01/pkgconf 2.5.1、固定 Meson 1.9.1。复用现有依赖缓存，未安装系统软件。

| 目标 | 构建与链接 | 本轮实际运行 |
| --- | --- | --- |
| macOS ARM64 | 七个 dylib、Metal 桥接及编码检查链接 PASS | 全库加载/软件 H.264 144 checks；真实 VideoToolbox 解码与 Metal 231 checks；AV1 两种位深各 3570 checks；MPEG4 CBR/VBR 2021 checks；PASS。H.264 VT 编码见下述限制 |
| macOS x64 | 七个 dylib、Metal 桥接及编码检查链接 PASS | 未验证：无 Rosetta，执行返回 `Bad CPU type in executable`（errno 86） |
| iOS ARM64 | 七个静态库、Metal 桥接、Unity 注册入口、AV1 与编码检查链接 PASS | 未验证：没有实体 iOS 设备 |
| iOS 模拟器 ARM64 | 七个静态库、Metal 桥接、Unity 注册入口、AV1 与编码检查链接 PASS | iOS 27 模拟器 Metal/软件回退 116 checks；AV1 两种位深各 3570 checks；MPEG4 CBR/VBR 2021 checks；PASS。VideoToolbox 解码明确 SKIP，硬件编码未验证 |
| iOS 模拟器 x64 | 七个静态库、Metal 桥接、Unity 注册入口、AV1 与编码检查链接 PASS | 未验证：没有 x64 运行支持 |

`verify-artifacts.py` 对 35 个 FFmpeg 库和 5 个桥接执行 SHA256、CPU、Apple SDK 平台与清单检查，全部 PASS；在 macOS ARM64 另外实际加载七个 FFmpeg ABI、桥接 ABI 2、`libdav1d` 与录制入口。加载测试清除 `DYLD_LIBRARY_PATH`，先加载 avformat，验证 `@loader_path` 真正解析同目录依赖。dav1d 静态并入 `libavcodec`；iOS 测试只链接交付的七个归档及桥接，没有额外链接 dav1d。iOS x64 链接仍报告 159 个 NASM 对象缺少 `LC_BUILD_VERSION` 的平台标签提示；CPU 为 x86_64，其余对象平台检查及最终链接通过。

## 实际录制与 VideoToolbox CBR 限制

共享 [EncoderNativeSmoke.c](../Tests/FFmpegValidation/EncoderNativeSmoke.c) 以 C11 `-Wall -Wextra -Werror` 编译，分别录制 60 帧 128×64/30 fps 的 CBR/VBR 视频，验证实际编码器、软件线程上限 2、解码帧数、像素与方向、包含间隔的 PTS、flush/drain、尾部索引及独占输出不覆盖。macOS ARM64 与 iOS ARM64 模拟器实际 MPEG4 结果均为：VBR 最后一秒 3616 bit/s，CBR 199976 bit/s（目标 200000 bit/s），两种模式各 60 帧，2021 checks PASS。MPEG4 的平坦场景 CBR 目标填充断言也通过。

macOS M4 的 `h264_videotoolbox` 显式设置 `allow_sw=0`、`require_sw=0`，NV12 输入，实际硬件编码 60 帧后解码、PTS、像素、drain 与独占输出 2022 checks PASS。VBR 最后一秒 11744 bit/s（目标 200000、最大 400000）；CBR 最后一秒仅 **11072 bit/s**（目标和最大均 200000）。`constant_bit_rate=1` 与 DataRateLimits 参数被接受，但原始 CBR 目标 ±20% 填充检查 **FAIL**。后来的 2022 checks 仅确认配置接受、测得负载与 roundtrip，不能证明目标填充效果通过。

Apple [live streaming 官方示例](https://developer.apple.com/documentation/VideoToolbox/encoding-video-for-live-streaming) 将 ConstantBitRate 单独用于目标填充，而 AverageBitRate 与 DataRateLimits 配对。固定 FFmpeg 的 `videotoolboxenc.c` 在 CBR 设置 ConstantBitRate 后，仍根据 `rc_max_rate` 设置 DataRateLimits。独立、忽略目录中的诊断程序得到：

| 诊断差异 | CBR 最后一秒负载 | 结论 |
| --- | --- | --- |
| 当前正式参数组合 | 11072 bit/s | 接受 CBR 配置，目标填充未通过 |
| 仅设置 `realtime=1` | 11072 bit/s | 该提示未改善填充 |
| 启用 `AV_CODEC_FLAG_LOW_DELAY` | 编码器 open 失败 | M4 低延迟模式明确不支持 ConstantBitRate |
| 仅 CBR 设置 `rc_max_rate=0`，不设置 DataRateLimits | 204680 bit/s | 接近 200000 目标，支持参数冲突判断；该路径移除了独立最大码率限制，属于诊断，**不是当前组件或正式配置验证** |

本轮只重建现有原生 profile，没有为通过测试而修改生产编码器参数。后续修复须明确 CBR 与最大码率的约束关系或属性顺序，并独立验证；上述平坦场景上限检查不能替代高复杂度视频的最大码率验证。

`allow_sw=0` 对应的 RequireHardware 选择仅在 FFmpeg 的 `!TARGET_OS_IPHONE` 分支设置，因此 macOS 的本轮证据不能外推为 iOS 硬件编码选择通过。最终共享 C 检查在 iOS optional VT 请求时明确返回 2；ARM64 模拟器实测此保护分支通过，默认 MPEG4 测试不受影响。iOS H.264 VT 硬件编码仍需实体设备验证。

## 命令和证据

```bash
# 在上述独立远端目录执行；使用已存在的任务工具链和缓存
export PATH="/opt/homebrew/bin:$PWD/Tools/FFmpeg/.build/toolchains/pkgconf/bin:/Users/codex/codex-work/majdata-ffmpeg-20261003/Tools/FFmpeg/.build/toolchains/nasm/bin:$PATH"
/opt/homebrew/bin/python3 -u Tools/FFmpeg/build.py \
  --targets macos-arm64,macos-x64,ios-arm64,ios-simulator-arm64,ios-simulator-x64 \
  --jobs 8 --require-all
/opt/homebrew/bin/python3 Tools/FFmpeg/verify-artifacts.py
bash Tools/Tests/FFmpegValidation/run-apple-native.sh arm64 "$PWD/bg.mp4"
bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-arm64 "$PWD/bg.mp4"
bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-simulator-x64 "$PWD/bg.mp4"
FFMPEG_SIMULATOR_UDID=31D01781-B001-4DCA-8888-B5E7A8549DAC \
  bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-simulator-arm64 "$PWD/bg.mp4"

# 编码检查用同架构 install/include 和交付目录的实际版本化 dylib
native="$PWD/Assets/Plugins/MajdataPlay/FFmpeg/Native/macOS/arm64"
xcrun clang -arch arm64 -std=c11 -Wall -Wextra -Werror \
  -I"$PWD/Tools/FFmpeg/.build/install/macos-arm64/include" \
  Tools/Tests/FFmpegValidation/EncoderNativeSmoke.c \
  "$native/libavformat.63.dylib" "$native/libavcodec.63.dylib" \
  "$native/libswscale.10.dylib" "$native/libavutil.61.dylib" \
  -Wl,-rpath,"$native" -lm -o /absolute/ignored/path/EncoderNativeSmoke
/absolute/ignored/path/EncoderNativeSmoke /absolute/empty/output-directory
/absolute/ignored/path/EncoderNativeSmoke /absolute/another-empty/output-directory h264_videotoolbox
# AV1/iOS 编译、frameworks 与 simulator 签名/运行步骤见验证 README。
```

回传产物位于 `Tools/FFmpeg/.build/apple-recording-ready-20261005`，完整构建/链接/运行日志与诊断源码位于其中 `evidence`（66 个文件）。本轮实际运行的共享 C 源 SHA256 为 `e7f1025265e429e92fd7c26de83b662e14931c32116d7e8e930d65ea84b646b1`；当前 `bdfbb9c889f9c17d716db3a98f2f64af679a2d6aa41a7a990782ebb76ff7f895` 仅收窄 PASS 摘要措辞并增加 MPEG4 填充结果说明，测试逻辑未改变。保留原严格填充失败 `apple-recording-encode-h264_videotoolbox.log`、原始数值 probe `apple-recording-vt-cbr-probe.log`、最终 NV12 配置检查 `apple-recording-encode-vt-nv12-final.log` 与三个参数诊断日志。压缩包远端/本地 SHA256 均一致：

- 产物 `apple-recording-artifacts-20261005.tar.gz`：`00e3dcd3a323aaf38e3d1c9392b2a5216107571dedd832739918788ce1f30fb7`。
- 证据 `apple-recording-evidence-20261005.tar.gz`：`8c224c6804e31fe6f466698c8d212e03e23980704d9d20e441b917bfafdf6c5e`。

Apple Unity 6000.3.17f1 Editor/Player、Mono/IL2CPP 的相机录制集成尚未验证：远端没有对应 Unity Editor。实体 iOS、x64 实际运行以及当前 VT CBR 参数的目标填充仍未验证或未通过，如上分别记录。以下保留本轮之前的 AV1 与历史构建结果。

# Apple AV1 软件解码构建与验证（2026-10-05）

在用户授权的 `mac-mini` 上重新构建全部五个 Apple 目标，固定 FFmpeg `n9.0.1` / `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`，新增静态 dav1d 1.5.3（BSD-2-Clause）。每个目标显式设置 Meson `-Dbitdepths=8,16` 并检查 `CONFIG_8BPC` / `CONFIG_16BPC`；FFmpeg 配置检查 `CONFIG_LIBDAV1D_DECODER`。16BPC 路径包含 AV1 10-bit 解码。

使用独立目录 `/Users/codex/codex-work/majdata-av1-20261005`；复用之前干净的 FFmpeg 源码、NASM 和已存在的公开绑定头文件缓存，未修改原任务源码或系统配置。工具链为 Apple M4、Xcode 27.0、macOS/iPhoneOS/iPhoneSimulator 27 SDK、AppleClang 21.0.0、Python 3.14.5、CMake 4.4.2、Ninja 1.13.2、NASM 2.16.01。缺少的 [pkgconf 2.5.1](https://distfiles.ariadne.space/pkgconf/) 从上游源码构建到任务缓存，未系统安装。Meson 1.9.1 与 dav1d 源码按依赖 lock 校验 SHA256。

| 目标 | 构建/静态链接检查 | AV1 8-bit / 10-bit 实际运行 |
| --- | --- | --- |
| macOS ARM64 | 7 个 FFmpeg dylib + Metal 桥接；PASS | 两种位深各 3570 checks、90 帧；PASS |
| macOS x64 | 7 个 FFmpeg dylib + Metal 桥接；AV1 测试链接 PASS | 未验证：主机无 Rosetta，x64 进程不能启动 |
| iOS ARM64 | 7 个 FFmpeg 静态库 + Metal 桥接；AV1 和 Unity 注册入口链接 PASS | 未验证：没有实体 iOS 设备 |
| iOS 模拟器 ARM64 | 7 个 FFmpeg 静态库 + Metal 桥接；PASS | iOS 27 模拟器两种位深各 3570 checks、90 帧；PASS |
| iOS 模拟器 x64 | 7 个 FFmpeg 静态库 + Metal 桥接；AV1 和 Unity 注册入口链接 PASS | 未验证：主机无 x64 运行支持 |

macOS 的 dav1d 静态链接进 `libavcodec.63.dylib`，`otool -L` 确认不需要额外 dav1d 动态库。三种 iOS 输出均用 Apple `libtool -static` 把 dav1d 对象合并进 `libavcodec.a`；测试只链接交付的七个 FFmpeg 归档及桥接，没有额外传入 `libdav1d.a`。每个目标携带 dav1d 许可证和构建清单。 本轮正式 macOS/iOS 插件只更新七个 FFmpeg 库及其配置、清单与许可证；既有桥接库和桥接清单保持不变，FFmpeg ABI 与桥接 ABI 2 均未变化。下述桥接回归在独立目录重新构建的桥接上进行，AV1 软件解码测试本身不调用 GPU 桥接。`verify-artifacts.py` 检查 35 个 FFmpeg 库及 5 个桥接的文件哈希、CPU 和 Apple SDK 平台标记；macOS ARM64 另实际加载全部原生 ABI、桥接 ABI 2 和 `libdav1d`，全部 PASS。

原生 `Av1SoftwareSmoke.c` 用 `-std=c11 -Wall -Wextra -Werror` 编译，显式选择 `libdav1d`，验证 AV1 码流、解码位深、软件帧/无硬件上下文、RGBA 转换、PTS、seek 后像素复现及完整 EOF。macOS ARM64 与 iOS 模拟器 ARM64 的实际结果相同：

```text
PASS: AV1 8-bit; decoder=libdav1d; 128x96; frames=90; PTS=0.000000..2.966667; seek=1.500000; RGBA=13..207; checksum=195783e1377fa374; software=1; EOF=1; checks=3570
PASS: AV1 10-bit; decoder=libdav1d; 128x96; frames=90; PTS=0.000000..2.966667; seek=1.500000; RGBA=12..207; checksum=3b500ab327320b79; software=1; EOF=1; checks=3570
```

使用两段生成的测试图视频；8-bit SHA256 为 `66275404bb28e1e9f99a7c010b3729bbafc9ed64070bb99e784a671ef4d35761`，10-bit 为 `aa9fe8355b87a5d053b09e685a0712439224027c27c7e1c6b2f5a41784e01244`。像素 checksum 用于同一运行内的 seek 一致性检查，不要求不同 CPU/SIMD 的 swscale 舍入结果完全相同。

对新库另运行已有 H.264 回归：macOS ARM64 的加载/软件 RGBA 测试 144 checks、真实 VideoToolbox + Metal 测试 231 checks 均 PASS；iOS ARM64 模拟器的 Metal/软件回退测试 116 checks PASS。模拟器 VideoToolbox 不可用时明确报告 SKIP，此回退测试不构成实体设备硬件解码证据。

构建与验证命令：

```bash
# 在独立 Mac 目录中；任务缓存的 pkgconf/NASM、既有 CMake/Ninja 在 PATH
python3 Tools/FFmpeg/build.py --targets macos-arm64,ios-arm64,ios-simulator-arm64,macos-x64,ios-simulator-x64 --jobs 8 --require-all
python3 Tools/FFmpeg/verify-artifacts.py
# 按 Tools/Tests/FFmpegValidation/README.md 编译 Av1SoftwareSmoke.c，链接各自交付库
Av1SoftwareSmoke test-av1.mp4 test-av1-10bit.mp4
# ARM64 模拟器：对编译好的 iOS simulator executable 临时签名后运行
codesign --force --sign - Av1SoftwareSmoke
xcrun simctl spawn 31D01781-B001-4DCA-8888-B5E7A8549DAC /absolute/path/Av1SoftwareSmoke /absolute/path/test-av1.mp4 /absolute/path/test-av1-10bit.mp4
# 现有 Metal/静态 Unity 注册回归；media 为远端既有 H.264 fixture
bash Tools/Tests/FFmpegValidation/run-apple-native.sh arm64 "$media"
bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-arm64
bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-simulator-x64
FFMPEG_SIMULATOR_UDID=31D01781-B001-4DCA-8888-B5E7A8549DAC bash Tools/Tests/FFmpegValidation/run-ios-native.sh ios-simulator-arm64 "$media"
```

回传产物和完整构建/测试证据位于忽略目录 `Tools/FFmpeg/.build/av1-all-ready/Apple`，其中 `artifacts` 包含 macOS/iOS 交付目录及两个模拟器目录，`evidence` 保存日志和配置。远端与本地压缩包 SHA256 已一致校验。集成到正式插件目录时保留已有 `.meta` GUID；模拟器静态库继续存放于 `.build/artifacts`，避免 Unity 同时导入 device 与 simulator 归档。

本次仍未验证：Apple Unity 6000.3.17f1 Player/Mono/IL2CPP 的 AV1 播放、实体 iOS 设备、x64 运行、原始用户视频及所有真实编码参数组合。远端未安装对应 Unity Editor；这些原生/模拟器结果不得外推为上述集成验证通过。以下保留此前 2026-10-03 的历史构建记录。

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
