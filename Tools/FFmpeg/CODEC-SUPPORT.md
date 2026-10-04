# 当前 FFmpeg 解码格式支持

本文说明 MajdataPlay 当前交付的 FFmpeg 原生库具备哪些软件解码器、平台硬件解码入口，以及 `FFmpegVideoPlayer` 实际接入的范围。清单核对日期为 **2026-10-05**，对应固定 **FFmpeg n9.0.1**、commit `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa` 和静态 **dav1d 1.5.3**，不是系统安装的 `ffmpeg.exe` 或上游默认构建的能力。

完整软件格式清单在本文后半部分，按视频及图像、音频、字幕分别列出。先阅读平台汇总和限制，可避免把容器、解码器实现、硬件 API 与实际设备能力混为一谈。

## 支持范围和统计口径

- **软件解码支持**：当前构建编入相应 CPU 解码实现，不需要对应的视频硬解单元；仍受具体 profile、压缩变体、位深、尺寸和有效码流限制。
- **硬件解码支持**：FFmpeg 编入对应后端或系统包装器。设备、操作系统、驱动和当前码流还必须同时满足要求；表中列出不等于设备实测通过。
- **播放器支持**：`FFmpegVideoPlayer` 目前只取视频流，跳过非视频数据包，不输出音频或渲染字幕。本文音频、字幕部分描述原生库能力，不代表组件已提供这些播放功能。
- **格式和容器分开**：H.264、AV1、AAC 是编码格式；MP4、MKV、MOV、WebM 等是容器。能解封装文件不等于其中每条流都能解码；存在 parser 也不等于存在 decoder。
- **完整清单按 AVCodecID 去重**，同时保留可查询的 decoder 名称。比如 `aac` 与 `aac_fixed` 是同一编码格式的两种实现；图像、未压缩视频、PCM 和历史游戏格式也纳入统计。

各平台共同配置了 **478 个 decoder 实现**。其中 `av1` 是硬件入口，AV1 的软件实现是 `libdav1d`；另有 `anull`、`vnull`、`wrapped_avframe` 三个内部用途条目。将这四项单列后，得到 **474 个通用软件 decoder 实现，对应 464 个格式条目**：

| 类别 | 格式条目数 | 软件 decoder 实现数 | 本文清单范围 |
| --- | ---: | ---: | --- |
| 视频及图像 | 242 | 243 | 动态视频、静态图像、未压缩像素和历史格式 |
| 音频 | 202 | 209 | 压缩音频、PCM、ADPCM 等；组件不播放音频 |
| 字幕 | 20 | 22 | 文本和位图字幕；组件不渲染字幕 |
| 合计 | 464 | 474 | 所有目标共有的软件实现，不含平台包装器 |

软件列表的类型、格式名和描述来自交付的 Windows x64 `avcodec-63.dll` 的 `av_codec_iterate()`、`av_codec_is_decoder()`、`avcodec_descriptor_get()`；再与十个目标的 `configure.txt` 做名称集合交叉核对。配置符号与公开 decoder 名称有少量拼写差异，例如 `fourxm` → `4xm`、`cscd` → `camstudio`；下方完整表使用公开 API 名称。

## 平台和架构汇总

同一行的架构具有相同 decoder 和硬件后端配置集合，具体设备能力仍可能不同。iOS 模拟器包位于忽略的 `.build/artifacts/ios-simulator-*`，不与 device 插件同时导入 Unity。

| 平台及架构 | decoder 实现总数 | 共同软件格式 | 平台额外 decoder | 已编译视频硬件 API |
| --- | ---: | --- | --- | --- |
| Windows x86、x64 | 478 | 上述 464 项 | 无 | D3D11VA、D3D12VA、DXVA2、Vulkan Video |
| Linux x64 | 478 | 同上 | 无 | VAAPI、Vulkan Video |
| Android ARMv7、ARM64 | 489 | 同上 | 11 个 MediaCodec 包装器 | MediaCodec、Vulkan Video |
| macOS x64、ARM64 | 493 | 同上 | 15 个 AudioToolbox 包装器 | VideoToolbox |
| iOS ARM64 | 493 | 同上 | 15 个 AudioToolbox 包装器 | VideoToolbox |
| iOS 模拟器 ARM64、x64 | 493 | 同上 | 15 个 AudioToolbox 包装器 | VideoToolbox；编译存在不代表模拟器提供硬解 |

这些数量覆盖所有媒体类型，不能称为 478、489 或 493 种视频格式。MediaCodec / AudioToolbox 包装器出现在 `Enabled decoders`，不全在 `Enabled hwaccels` 中。

## 常用视频编码速查

下表的“硬件入口”只表示本项目某个平台编入了该入口，具体平台见下一节。软件完整列表并不限于下表。

| 编码格式 | 软件实现 | 当前已编译硬件入口 |
| --- | --- | --- |
| AV1 | `libdav1d`；8-bit、10-bit 已实际验证 | D3D11VA、D3D12VA、DXVA2、Vulkan、VAAPI、VideoToolbox、MediaCodec |
| H.264 / AVC | `h264` | 同上 |
| H.265 / HEVC | `hevc`；不包含原生软件 SCC profile | 同上 |
| H.266 / VVC | `vvc` | 无 |
| VP9 | `vp9` | D3D11VA、D3D12VA、DXVA2、Vulkan、VAAPI、VideoToolbox、MediaCodec |
| VP8 | `vp8` | VAAPI、MediaCodec；没有本构建的 Vulkan VP8 入口 |
| MPEG-1 Video | `mpeg1video` | VideoToolbox |
| MPEG-2 Video | `mpeg2video`、`mpegvideo` | D3D11VA、D3D12VA、DXVA2、VAAPI、VideoToolbox、MediaCodec |
| MPEG-4 Part 2 | `mpeg4` | VAAPI、VideoToolbox、MediaCodec |
| H.263 | `h263` | VAAPI、VideoToolbox |
| VC-1、WMV3 | `vc1`、`wmv3` | D3D11VA、D3D12VA、DXVA2、VAAPI |
| MJPEG | `mjpeg` | VAAPI |
| Apple ProRes | `prores` | VideoToolbox |
| Apple ProRes RAW | `prores_raw`，含 CPU 解码实现 | VideoToolbox |
| FFV1、HuffYUV、Ut Video | `ffv1`、`huffyuv`、`utvideo` | 无 |
| Theora、AVS1-P2 | `theora`、`cavs` | 无 |

AV1 原生 `av1` 和软件 `libdav1d` 不能互换解释。播放器在软件模式显式选择 `libdav1d`；硬件模式单独选择 `av1` 或 `av1_mediacodec`。代码兼容 `libaom-av1` 作为替代软件实现，但**当前交付库没有编入 `libaom-av1`**。

## 视频硬件解码完整矩阵

下表完整列出本构建的视频硬件后端对应编码格式。Windows 配置中的 `d3d11va` 和 `d3d11va2` 是两种 FFmpeg 接口条目，合并计作同一个 D3D11VA 后端，不能重复算成额外编码格式。

| 后端 | 适用平台 | 已编译的全部视频编码格式 | FFmpegVideoPlayer 接入状态 |
| --- | --- | --- | --- |
| D3D11VA | Windows x86/x64 | AV1、H.264、HEVC、MPEG-2 Video、VC-1、VP9、WMV3 | Windows 平台默认或回退后端 |
| D3D12VA | Windows x86/x64 | AV1、H.264、HEVC、MPEG-2 Video、VC-1、VP9、WMV3 | Unity 使用 D3D12 且桥接能力满足时优先尝试 |
| DXVA2 | Windows x86/x64 | AV1、H.264、HEVC、MPEG-2 Video、VC-1、VP9、WMV3 | 库已编译，但当前 Player 不选择此后端 |
| Vulkan Video | Windows x86/x64、Linux x64、Android ARMv7/ARM64 | AV1、H.264、HEVC、VP9 | Unity 使用 Vulkan 且设备协商成功时优先尝试 |
| VAAPI | Linux x64 | AV1、H.263、H.264、HEVC、MJPEG、MPEG-2 Video、MPEG-4 Part 2、VC-1、VP8、VP9、WMV3 | Linux 平台默认或回退后端 |
| VideoToolbox | macOS x64/ARM64、iOS device/simulator | AV1、H.263、H.264、HEVC、MPEG-1 Video、MPEG-2 Video、MPEG-4 Part 2、ProRes、ProRes RAW、VP9 | Apple 平台后端；GPU 呈现另受 Metal 桥接限制 |
| MediaCodec | Android ARMv7/ARM64 | AV1、H.264、HEVC、MPEG-2 Video、MPEG-4 Part 2、VP8、VP9 | Android 平台默认或回退后端，选择对应 `*_mediacodec` |

本构建没有 CUDA/NVDEC/CUVID、QSV、VDPAU、AMF 或 V4L2 M2M 解码入口，也没有 Apple Vulkan Video 后端。这不表示对应厂商 GPU 一定不能使用 D3D11VA、D3D12VA、VAAPI 等已列出的系统接口。

FFmpeg 在 VideoToolbox 的 HEVC、ProRes、ProRes RAW 路径允许系统选择实现，调用 VideoToolbox 或拿到硬件帧句柄不能单独证明使用了物理硬解单元。MediaCodec 和 AudioToolbox 也应按实际系统实现与运行结果判断，不能从包装器名称推断某个芯片支持某个 profile。

## 平台包装器完整列表

这些是已有编码格式的平台实现，不应再次累加到共同软件格式数中。系统 API 可用性和是否提供相应 codec 由设备决定。

### Android MediaCodec

| 媒体类型 | 编码格式 | decoder 名称 |
| --- | --- | --- |
| 视频 | AV1 | `av1_mediacodec` |
| 视频 | H.264 / AVC | `h264_mediacodec` |
| 视频 | H.265 / HEVC | `hevc_mediacodec` |
| 视频 | MPEG-2 Video | `mpeg2_mediacodec` |
| 视频 | MPEG-4 Part 2 | `mpeg4_mediacodec` |
| 视频 | VP8 | `vp8_mediacodec` |
| 视频 | VP9 | `vp9_mediacodec` |
| 音频 | AAC | `aac_mediacodec` |
| 音频 | AMR-NB | `amrnb_mediacodec` |
| 音频 | AMR-WB | `amrwb_mediacodec` |
| 音频 | MP3 | `mp3_mediacodec` |

### Apple AudioToolbox

AudioToolbox 是系统音频解码接口，下面的 15 项不能等同于 15 种物理硬件解码能力。当前 `FFmpegVideoPlayer` 不调用这些音频解码器。

| 编码格式 | decoder 名称 |
| --- | --- |
| AAC | `aac_at` |
| AC-3 | `ac3_at` |
| QuickTime IMA ADPCM | `adpcm_ima_qt_at` |
| Apple Lossless | `alac_at` |
| AMR-NB | `amr_nb_at` |
| E-AC-3 | `eac3_at` |
| Microsoft GSM | `gsm_ms_at` |
| iLBC | `ilbc_at` |
| MPEG Audio Layer I | `mp1_at` |
| MPEG Audio Layer II | `mp2_at` |
| MPEG Audio Layer III | `mp3_at` |
| PCM A-law | `pcm_alaw_at` |
| PCM μ-law | `pcm_mulaw_at` |
| QDesign Music 2 | `qdm2_at` |
| QDesign Music | `qdmc_at` |

字幕没有本构建的硬件解码包装器，完整软件字幕格式见后文。

## 位深和 GPU 显示限制

编码格式支持与 GPU 帧呈现支持是两层能力。10-bit 码流可以软件解码，但不代表硬件路径、当前桥接或显示输出同时保留 10-bit 精度。

| Player 帧传输路径 | 当前实现接收的主要帧格式 | 说明 |
| --- | --- | --- |
| 软件解码或硬解后 CPU 上传 | 可由 swscale 转换的 CPU 像素格式 | 输出为 RGBA32；不是 HDR 输出，也没有 HDR tone mapping |
| D3D11VA 及其 D3D12/Vulkan/WGL 共享回退 | NV12 | 当前桥接不接受 P010；限制裁剪和 BT.2020 |
| 原生 D3D12VA | NV12、P010 | 仍拒绝不支持的裁剪、BT.2020、PQ/HLG |
| 原生 Vulkan Video | NV12、P010、P016 | 还要求兼容的单图像布局、图像层数、同步和颜色信息 |
| VAAPI → DMA-BUF → Vulkan | NV12，兼容的双层导出布局 | 当前桥接不接受 P010，另检查 modifier、裁剪和颜色信息 |
| VideoToolbox → Metal | 8-bit NV12，两平面 full/video range | 不能把 VideoToolbox 的所有输出格式视为 Metal 共享可用 |
| MediaCodec → AHardwareBuffer → Vulkan | 平台外部 YCbCr 格式，或 RGBA/BGRA UNORM | 不是固定 NV12-only；仍需匹配设备格式、同步和颜色信息 |

Vulkan Video 还要求本项目所需的 Vulkan 版本、逐编码 video decode 扩展、可用队列、YCbCr 与同步能力。仅支持 Vulkan 图形 API 不等于支持 Vulkan Video。

普通硬件偏好可依次尝试原生硬件、平台硬件、硬件解码后 CPU 上传和软件解码。`RequireHardwareDecoding=true` 保留严格 GPU 模式，禁止 CPU 上传和软件回退；设置此选项不会让不支持的设备获得新能力。实际状态应读取 `DecoderType`、`DecoderName`、`DecoderDevice`、`TransferMode` 和 `HardwareFallbackReason`。

实现依据为 [后端选择](../../Assets/Plugins/MajdataPlay/FFmpeg/Runtime/Interop/FFmpegVideoPlayer.Hardware.cs)、[解码器](../../Assets/Plugins/MajdataPlay/FFmpeg/Runtime/Decoding/FFmpegVideoDecoder.cs)、[CPU 转换](../../Assets/Plugins/MajdataPlay/FFmpeg/Runtime/Decoding/VideoFrameConverter.cs) 和各原生桥接：[D3D11](Native/D3D11.cpp)、[D3D12](Native/D3D12.cpp)、[Vulkan Video](Native/VulkanVideoDecode.cpp)、[VAAPI](Native/LinuxVaapi.cpp)、[Metal](Native/Metal.mm)、[Android](Native/AndroidMediaCodec.cpp)。

## 当前构建未包含或仅部分支持的格式

下列是容易与完整 FFmpeg 发行版混淆的项目，不是对所有不支持格式的穷举。

| 格式或实现 | 当前状态 |
| --- | --- |
| PNG、APNG、OpenEXR、ZMBV、TechSmith TSCC、ZLIB 视频 | 没有注册相应 decoder；`tscc2` 是另一项已存在的解码器 |
| JPEG XL、JPEG XS、AVS2、AVS3、EVC、LC3 | 没有相应 decoder；即使有 parser 或 demuxer，也不能据此解码 |
| `libaom-av1`、`libvpx`、`libjxl` 等外部实现 | 当前唯一外部编解码库是 dav1d；原生 VP8/VP9 等软件实现仍存在 |
| CamStudio，公开名称 `camstudio`，配置名称 `cscd` | LZO 路径存在；本构建无 zlib，zlib 压缩变体不可用 |
| TIFF | decoder 存在；本构建无 zlib/LZMA，Deflate/LZMA 压缩变体不可用 |
| Sorenson SVQ3 | decoder 存在；需要 zlib 解压的压缩水印变体不可用 |
| HEVC | 原生软件 decoder 不实现 SCC profile，另有多层码流等细分限制；不能概括为支持所有 HEVC profile |
| ProRes RAW | 有 CPU 解码实现；仍有限定的版本和 Bayer 模式，并非所有 RAW 数据布局都支持 |

这里的部分限制来自固定源码，而非格式名称推测：例如 [CamStudio](https://github.com/FFmpeg/FFmpeg/blob/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa/libavcodec/cscd.c)、[TIFF](https://github.com/FFmpeg/FFmpeg/blob/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa/libavcodec/tiff.c)、[SVQ3](https://github.com/FFmpeg/FFmpeg/blob/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa/libavcodec/svq3.c)、[HEVC](https://github.com/FFmpeg/FFmpeg/blob/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa/libavcodec/hevc/hevcdec.c)、[ProRes RAW](https://github.com/FFmpeg/FFmpeg/blob/bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa/libavcodec/prores_raw.c)。其他 decoder 也可能对 profile、色度采样、压缩工具、尺寸或异常码流有进一步限制；本清单不把注册视为全变体认证。

## 已完成的运行验证

本文列出的是当前构建能力全集，并未逐个格式生成素材进行播放测试。已有实测范围和命令见 [FFmpegValidation 结果](../Tests/FFmpegValidation/RESULTS.md)、[验证说明](../Tests/FFmpegValidation/README.md) 及 [Apple 记录](APPLE-RESULTS.md)。

- AV1 8-bit / 10-bit：Windows、Linux、Android 两种 ABI、macOS ARM64 和 iOS ARM64 模拟器已实际解码；Windows、Linux 和 Android 另通过对应 Unity Player 验证。
- H.264：已有 Windows、Android 和 macOS 的平台硬件路径验证，具体图形 API、架构、后端及历史版本范围以结果文档为准。
- macOS x64、iOS device ARM64、iOS 模拟器 x64 的新 AV1 库已构建、链接；没有相应运行证据。Apple Unity Player AV1 播放尚未验证。
- 原生 D3D12VA、Vulkan Video 的编译及接口检查，不等于现有设备已完成这些后端的实际码流硬解；不能把 D3D11VA 等回退结果记成原生后端成功。

## 软件格式完整清单

以下三表覆盖所有目标共同编入的 **464 个普通格式条目**，以格式名排序。格式名是 `AVCodecDescriptor.name`，实现名是 `AVCodec.name`；英文说明保留库自身描述，便于与 FFmpeg 工具和 API 对照。已在前文指出的变体限制继续适用。

### 视频和图像软件解码完整列表

共 242 个格式条目。

| 格式名 | 格式说明 | 软件 decoder 名称 |
| --- | --- | --- |
| `012v` | Uncompressed 4:2:2 10-bit | `012v` |
| `4xm` | 4X Movie | `4xm` |
| `8bps` | QuickTime 8BPS video | `8bps` |
| `aasc` | Autodesk RLE | `aasc` |
| `agm` | Amuse Graphics Movie | `agm` |
| `aic` | Apple Intermediate Codec | `aic` |
| `alias_pix` | Alias/Wavefront PIX image | `alias_pix` |
| `amv` | AMV Video | `amv` |
| `anm` | Deluxe Paint Animation | `anm` |
| `ansi` | ASCII/ANSI art | `ansi` |
| `apv` | Advanced Professional Video | `apv` |
| `arbc` | Gryphon's Anim Compressor | `arbc` |
| `argo` | Argonaut Games Video | `argo` |
| `asv1` | ASUS V1 | `asv1` |
| `asv2` | ASUS V2 | `asv2` |
| `aura` | Auravision AURA | `aura` |
| `aura2` | Auravision Aura 2 | `aura2` |
| `av1` | Alliance for Open Media AV1 | `libdav1d` |
| `avrn` | Avid AVI Codec | `avrn` |
| `avrp` | Avid 1:1 10-bit RGB Packer | `avrp` |
| `avs` | AVS (Audio Video Standard) video | `avs` |
| `avui` | Avid Meridien Uncompressed | `avui` |
| `bethsoftvid` | Bethesda VID video | `bethsoftvid` |
| `bfi` | Brute Force & Ignorance | `bfi` |
| `binkvideo` | Bink video | `binkvideo` |
| `bintext` | Binary text | `bintext` |
| `bitpacked` | Bitpacked | `bitpacked` |
| `bmp` | BMP (Windows and OS/2 bitmap) | `bmp` |
| `bmv_video` | Discworld II BMV video | `bmv_video` |
| `brender_pix` | BRender PIX image | `brender_pix` |
| `c93` | Interplay C93 | `c93` |
| `cavs` | Chinese AVS (Audio Video Standard) (AVS1-P2, JiZhun profile) | `cavs` |
| `cdgraphics` | CD Graphics video | `cdgraphics` |
| `cdtoons` | CDToons video | `cdtoons` |
| `cdxl` | Commodore CDXL video | `cdxl` |
| `cfhd` | GoPro CineForm HD | `cfhd` |
| `cinepak` | Cinepak | `cinepak` |
| `clearvideo` | Iterated Systems ClearVideo | `clearvideo` |
| `cljr` | Cirrus Logic AccuPak | `cljr` |
| `cllc` | Canopus Lossless Codec | `cllc` |
| `cmv` | Electronic Arts CMV video | `eacmv` |
| `cpia` | CPiA video format | `cpia` |
| `cri` | Cintel RAW | `cri` |
| `cscd` | CamStudio | `camstudio` |
| `cyuv` | Creative YUV (CYUV) | `cyuv` |
| `dds` | DirectDraw Surface image decoder | `dds` |
| `dfa` | Chronomaster DFA | `dfa` |
| `dirac` | Dirac | `dirac` |
| `dnxhd` | VC3/DNxHD | `dnxhd` |
| `dpx` | DPX (Digital Picture Exchange) image | `dpx` |
| `dsicinvideo` | Delphine Software International CIN video | `dsicinvideo` |
| `dvvideo` | DV (Digital Video) | `dvvideo` |
| `dxtory` | Dxtory | `dxtory` |
| `dxv` | Resolume DXV | `dxv` |
| `escape124` | Escape 124 | `escape124` |
| `escape130` | Escape 130 | `escape130` |
| `ffv1` | FFmpeg video codec #1 | `ffv1` |
| `ffvhuff` | Huffyuv FFmpeg variant | `ffvhuff` |
| `fic` | Mirillis FIC | `fic` |
| `fits` | FITS (Flexible Image Transport System) | `fits` |
| `flic` | Autodesk Animator Flic video | `flic` |
| `flv1` | FLV / Sorenson Spark / Sorenson H.263 (Flash Video) | `flv` |
| `fmvc` | FM Screen Capture Codec | `fmvc` |
| `fraps` | Fraps | `fraps` |
| `frwu` | Forward Uncompressed | `frwu` |
| `gdv` | Gremlin Digital Video | `gdv` |
| `gem` | GEM Raster image | `gem` |
| `gif` | CompuServe GIF (Graphics Interchange Format) | `gif` |
| `h261` | H.261 | `h261` |
| `h263` | H.263 / H.263-1996, H.263+ / H.263-1998 / H.263 version 2 | `h263` |
| `h263i` | Intel H.263 | `h263i` |
| `h263p` | H.263+ / H.263-1998 / H.263 version 2 | `h263p` |
| `h264` | H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 | `h264` |
| `hap` | Vidvox Hap | `hap` |
| `hdr` | HDR (Radiance RGBE format) image | `hdr` |
| `hevc` | H.265 / HEVC (High Efficiency Video Coding) | `hevc` |
| `hnm4video` | HNM 4 video | `hnm4video` |
| `hq_hqa` | Canopus HQ/HQA | `hq_hqa` |
| `hqx` | Canopus HQX | `hqx` |
| `huffyuv` | HuffYUV | `huffyuv` |
| `hymt` | HuffYUV MT | `hymt` |
| `idcin` | id Quake II CIN video | `idcinvideo` |
| `idf` | iCEDraw text | `idf` |
| `iff_ilbm` | IFF ACBM/ANIM/DEEP/ILBM/PBM/RGB8/RGBN | `iff` |
| `imm4` | Infinity IMM4 | `imm4` |
| `imm5` | Infinity IMM5 | `imm5` |
| `indeo2` | Intel Indeo 2 | `indeo2` |
| `indeo3` | Intel Indeo 3 | `indeo3` |
| `indeo4` | Intel Indeo Video Interactive 4 | `indeo4` |
| `indeo5` | Intel Indeo Video Interactive 5 | `indeo5` |
| `interplayvideo` | Interplay MVE video | `interplayvideo` |
| `ipu` | IPU Video | `ipu` |
| `jpeg2000` | JPEG 2000 | `jpeg2000` |
| `jpegls` | JPEG-LS | `jpegls` |
| `jv` | Bitmap Brothers JV video | `jv` |
| `kgv1` | Kega Game Video | `kgv1` |
| `kmvc` | Karl Morton's video codec | `kmvc` |
| `lagarith` | Lagarith lossless | `lagarith` |
| `lead` | LEAD MCMP | `lead` |
| `loco` | LOCO | `loco` |
| `m101` | Matrox Uncompressed SD | `m101` |
| `mad` | Electronic Arts Madcow Video | `eamad` |
| `magicyuv` | MagicYUV video | `magicyuv` |
| `mdec` | Sony PlayStation MDEC (Motion DECoder) | `mdec` |
| `media100` | Media 100i | `media100` |
| `mimic` | Mimic | `mimic` |
| `mjpeg` | Motion JPEG | `mjpeg` |
| `mjpegb` | Apple MJPEG-B | `mjpegb` |
| `mmvideo` | American Laser Games MM Video | `mmvideo` |
| `mobiclip` | MobiClip Video | `mobiclip` |
| `motionpixels` | Motion Pixels video | `motionpixels` |
| `mpeg1video` | MPEG-1 video | `mpeg1video` |
| `mpeg2video` | MPEG-2 video | `mpeg2video`、`mpegvideo` |
| `mpeg4` | MPEG-4 part 2 | `mpeg4` |
| `msa1` | MS ATC Screen | `msa1` |
| `msmpeg4v1` | MPEG-4 part 2 Microsoft variant version 1 | `msmpeg4v1` |
| `msmpeg4v2` | MPEG-4 part 2 Microsoft variant version 2 | `msmpeg4v2` |
| `msmpeg4v3` | MPEG-4 part 2 Microsoft variant version 3 | `msmpeg4` |
| `msp2` | Microsoft Paint (MSP) version 2 | `msp2` |
| `msrle` | Microsoft RLE | `msrle` |
| `mss1` | MS Screen 1 | `mss1` |
| `mss2` | MS Windows Media Video V9 Screen | `mss2` |
| `msvideo1` | Microsoft Video 1 | `msvideo1` |
| `mszh` | LCL (LossLess Codec Library) MSZH | `mszh` |
| `mts2` | MS Expression Encoder Screen | `mts2` |
| `mv30` | MidiVid 3.0 | `mv30` |
| `mvc1` | Silicon Graphics Motion Video Compressor 1 | `mvc1` |
| `mvc2` | Silicon Graphics Motion Video Compressor 2 | `mvc2` |
| `mvdv` | MidiVid VQ | `mvdv` |
| `mxpeg` | Mobotix MxPEG video | `mxpeg` |
| `notchlc` | NotchLC | `notchlc` |
| `nuv` | NuppelVideo/RTJPEG | `nuv` |
| `paf_video` | Amazing Studio Packed Animation File Video | `paf_video` |
| `pam` | PAM (Portable AnyMap) image | `pam` |
| `pbm` | PBM (Portable BitMap) image | `pbm` |
| `pcx` | PC Paintbrush PCX image | `pcx` |
| `pfm` | PFM (Portable FloatMap) image | `pfm` |
| `pgm` | PGM (Portable GrayMap) image | `pgm` |
| `pgmyuv` | PGMYUV (Portable GrayMap YUV) image | `pgmyuv` |
| `pgx` | PGX (JPEG2000 Test Format) | `pgx` |
| `phm` | PHM (Portable HalfFloatMap) image | `phm` |
| `photocd` | Kodak Photo CD | `photocd` |
| `pictor` | Pictor/PC Paint | `pictor` |
| `pixlet` | Apple Pixlet | `pixlet` |
| `ppm` | PPM (Portable PixelMap) image | `ppm` |
| `prores` | Apple ProRes (iCodec Pro) | `prores` |
| `prores_raw` | Apple ProRes RAW | `prores_raw` |
| `prosumer` | Brooktree ProSumer Video | `prosumer` |
| `psd` | Photoshop PSD file | `psd` |
| `ptx` | V.Flash PTX image | `ptx` |
| `qdraw` | Apple QuickDraw | `qdraw` |
| `qoi` | QOI (Quite OK Image) | `qoi` |
| `qpeg` | Q-team QPEG | `qpeg` |
| `qtrle` | QuickTime Animation (RLE) video | `qtrle` |
| `r10k` | AJA Kona 10-bit RGB Codec | `r10k` |
| `r210` | Uncompressed RGB 10-bit | `r210` |
| `rawvideo` | raw video | `rawvideo` |
| `rl2` | RL2 video | `rl2` |
| `roq` | id RoQ video | `roqvideo` |
| `rpza` | QuickTime video (RPZA) | `rpza` |
| `rtv1` | RTV1 (RivaTuner Video) | `rtv1` |
| `rv10` | RealVideo 1.0 | `rv10` |
| `rv20` | RealVideo 2.0 | `rv20` |
| `rv30` | RealVideo 3.0 | `rv30` |
| `rv40` | RealVideo 4.0 | `rv40` |
| `rv60` | RealVideo 6.0 | `rv60` |
| `sanm` | LucasArts SANM/SMUSH video | `sanm` |
| `scpr` | ScreenPressor | `scpr` |
| `sga` | Digital Pictures SGA Video | `sga` |
| `sgi` | SGI image | `sgi` |
| `sgirle` | SGI RLE 8-bit | `sgirle` |
| `sheervideo` | BitJazz SheerVideo | `sheervideo` |
| `simbiosis_imx` | Simbiosis Interactive IMX Video | `simbiosis_imx` |
| `smackvideo` | Smacker video | `smackvid` |
| `smc` | QuickTime Graphics (SMC) | `smc` |
| `smvjpeg` | Sigmatel Motion Video | `smvjpeg` |
| `snow` | Snow | `snow` |
| `sp5x` | Sunplus JPEG (SP5X) | `sp5x` |
| `speedhq` | NewTek SpeedHQ | `speedhq` |
| `sunrast` | Sun Rasterfile image | `sunrast` |
| `svq1` | Sorenson Vector Quantizer 1 / Sorenson Video 1 / SVQ1 | `svq1` |
| `svq3` | Sorenson Vector Quantizer 3 / Sorenson Video 3 / SVQ3 | `svq3` |
| `targa` | Truevision Targa image | `targa` |
| `targa_y216` | Pinnacle TARGA CineWave YUV16 | `targa_y216` |
| `tgq` | Electronic Arts TGQ video | `eatgq` |
| `tgv` | Electronic Arts TGV video | `eatgv` |
| `theora` | Theora | `theora` |
| `thp` | Nintendo Gamecube THP video | `thp` |
| `tiertexseqvideo` | Tiertex Limited SEQ video | `tiertexseqvideo` |
| `tiff` | TIFF image | `tiff` |
| `tmv` | 8088flex TMV | `tmv` |
| `tqi` | Electronic Arts TQI video | `eatqi` |
| `truemotion1` | Duck TrueMotion 1.0 | `truemotion1` |
| `truemotion2` | Duck TrueMotion 2.0 | `truemotion2` |
| `truemotion2rt` | Duck TrueMotion 2.0 Real Time | `truemotion2rt` |
| `tscc2` | TechSmith Screen Codec 2 | `tscc2` |
| `txd` | Renderware TXD (TeXture Dictionary) image | `txd` |
| `ulti` | IBM UltiMotion | `ultimotion` |
| `utvideo` | Ut Video | `utvideo` |
| `v210` | Uncompressed 4:2:2 10-bit | `v210` |
| `v210x` | Uncompressed 4:2:2 10-bit | `v210x` |
| `vb` | Beam Software VB | `vb` |
| `vble` | VBLE Lossless Codec | `vble` |
| `vbn` | Vizrt Binary Image | `vbn` |
| `vc1` | SMPTE VC-1 | `vc1` |
| `vc1image` | Windows Media Video 9 Image v2 | `vc1image` |
| `vcr1` | ATI VCR1 | `vcr1` |
| `vixl` | Miro VideoXL | `xl` |
| `vmdvideo` | Sierra VMD video | `vmdvideo` |
| `vmix` | vMix Video | `vmix` |
| `vmnc` | VMware Screen Codec / VMware Video | `vmnc` |
| `vp3` | On2 VP3 | `vp3` |
| `vp4` | On2 VP4 | `vp4` |
| `vp5` | On2 VP5 | `vp5` |
| `vp6` | On2 VP6 | `vp6` |
| `vp6a` | On2 VP6 (Flash version, with alpha channel) | `vp6a` |
| `vp6f` | On2 VP6 (Flash version) | `vp6f` |
| `vp7` | On2 VP7 | `vp7` |
| `vp8` | On2 VP8 | `vp8` |
| `vp9` | Google VP9 | `vp9` |
| `vqc` | ViewQuest VQC | `vqc` |
| `vvc` | H.266 / VVC (Versatile Video Coding) | `vvc` |
| `wbmp` | WBMP (Wireless Application Protocol Bitmap) image | `wbmp` |
| `webp` | WebP | `webp` |
| `webp_anim` | Animated WebP | `webp_anim` |
| `wmv1` | Windows Media Video 7 | `wmv1` |
| `wmv2` | Windows Media Video 8 | `wmv2` |
| `wmv3` | Windows Media Video 9 | `wmv3` |
| `wmv3image` | Windows Media Video 9 Image | `wmv3image` |
| `wnv1` | Winnov WNV1 | `wnv1` |
| `ws_vqa` | Westwood Studios VQA (Vector Quantized Animation) video | `vqavideo` |
| `xan_wc3` | Wing Commander III / Xan | `xan_wc3` |
| `xan_wc4` | Wing Commander IV / Xxan | `xan_wc4` |
| `xbin` | eXtended BINary text | `xbin` |
| `xbm` | XBM (X BitMap) image | `xbm` |
| `xface` | X-face image | `xface` |
| `xpm` | XPM (X PixMap) image | `xpm` |
| `xwd` | XWD (X Window Dump) image | `xwd` |
| `y41p` | Uncompressed YUV 4:1:1 12-bit | `y41p` |
| `ylc` | YUY2 Lossless Codec | `ylc` |
| `yop` | Psygnosis YOP Video | `yop` |
| `yuv4` | Uncompressed packed 4:2:0 | `yuv4` |

### 音频软件解码完整列表

共 202 个格式条目。

| 格式名 | 格式说明 | 软件 decoder 名称 |
| --- | --- | --- |
| `8svx_exp` | 8SVX exponential | `8svx_exp` |
| `8svx_fib` | 8SVX fibonacci | `8svx_fib` |
| `aac` | AAC (Advanced Audio Coding) | `aac`、`aac_fixed` |
| `aac_latm` | AAC LATM (Advanced Audio Coding LATM syntax) | `aac_latm` |
| `ac3` | ATSC A/52A (AC-3) | `ac3`、`ac3_fixed` |
| `acelp.kelvin` | Sipro ACELP.KELVIN | `acelp.kelvin` |
| `adpcm_4xm` | ADPCM 4X Movie | `adpcm_4xm` |
| `adpcm_adx` | SEGA CRI ADX ADPCM | `adpcm_adx` |
| `adpcm_afc` | ADPCM Nintendo Gamecube AFC | `adpcm_afc` |
| `adpcm_agm` | ADPCM AmuseGraphics Movie AGM | `adpcm_agm` |
| `adpcm_aica` | ADPCM Yamaha AICA | `adpcm_aica` |
| `adpcm_argo` | ADPCM Argonaut Games | `adpcm_argo` |
| `adpcm_ct` | ADPCM Creative Technology | `adpcm_ct` |
| `adpcm_dtk` | ADPCM Nintendo Gamecube DTK | `adpcm_dtk` |
| `adpcm_ea` | ADPCM Electronic Arts | `adpcm_ea` |
| `adpcm_ea_maxis_xa` | ADPCM Electronic Arts Maxis CDROM XA | `adpcm_ea_maxis_xa` |
| `adpcm_ea_r1` | ADPCM Electronic Arts R1 | `adpcm_ea_r1` |
| `adpcm_ea_r2` | ADPCM Electronic Arts R2 | `adpcm_ea_r2` |
| `adpcm_ea_r3` | ADPCM Electronic Arts R3 | `adpcm_ea_r3` |
| `adpcm_ea_xas` | ADPCM Electronic Arts XAS | `adpcm_ea_xas` |
| `adpcm_g722` | G.722 ADPCM | `g722` |
| `adpcm_g726` | G.726 ADPCM | `g726` |
| `adpcm_g726le` | G.726 ADPCM little-endian | `g726le` |
| `adpcm_ima_acorn` | ADPCM IMA Acorn Replay | `adpcm_ima_acorn` |
| `adpcm_ima_alp` | ADPCM IMA High Voltage Software ALP | `adpcm_ima_alp` |
| `adpcm_ima_amv` | ADPCM IMA AMV | `adpcm_ima_amv` |
| `adpcm_ima_apc` | ADPCM IMA CRYO APC | `adpcm_ima_apc` |
| `adpcm_ima_apm` | ADPCM IMA Ubisoft APM | `adpcm_ima_apm` |
| `adpcm_ima_cunning` | ADPCM IMA Cunning Developments | `adpcm_ima_cunning` |
| `adpcm_ima_dat4` | ADPCM IMA Eurocom DAT4 | `adpcm_ima_dat4` |
| `adpcm_ima_dk3` | ADPCM IMA Duck DK3 | `adpcm_ima_dk3` |
| `adpcm_ima_dk4` | ADPCM IMA Duck DK4 | `adpcm_ima_dk4` |
| `adpcm_ima_ea_eacs` | ADPCM IMA Electronic Arts EACS | `adpcm_ima_ea_eacs` |
| `adpcm_ima_ea_sead` | ADPCM IMA Electronic Arts SEAD | `adpcm_ima_ea_sead` |
| `adpcm_ima_iss` | ADPCM IMA Funcom ISS | `adpcm_ima_iss` |
| `adpcm_ima_moflex` | ADPCM IMA MobiClip MOFLEX | `adpcm_ima_moflex` |
| `adpcm_ima_mtf` | ADPCM IMA Capcom's MT Framework | `adpcm_ima_mtf` |
| `adpcm_ima_oki` | ADPCM IMA Dialogic OKI | `adpcm_ima_oki` |
| `adpcm_ima_qt` | ADPCM IMA QuickTime | `adpcm_ima_qt` |
| `adpcm_ima_rad` | ADPCM IMA Radical | `adpcm_ima_rad` |
| `adpcm_ima_smjpeg` | ADPCM IMA Loki SDL MJPEG | `adpcm_ima_smjpeg` |
| `adpcm_ima_ssi` | ADPCM IMA Simon & Schuster Interactive | `adpcm_ima_ssi` |
| `adpcm_ima_wav` | ADPCM IMA WAV | `adpcm_ima_wav` |
| `adpcm_ima_ws` | ADPCM IMA Westwood | `adpcm_ima_ws` |
| `adpcm_ima_xbox` | ADPCM IMA Xbox | `adpcm_ima_xbox` |
| `adpcm_ms` | ADPCM Microsoft | `adpcm_ms` |
| `adpcm_mtaf` | ADPCM MTAF | `adpcm_mtaf` |
| `adpcm_psx` | ADPCM Playstation | `adpcm_psx` |
| `adpcm_sanyo` | ADPCM Sanyo | `adpcm_sanyo` |
| `adpcm_sbpro_2` | ADPCM Sound Blaster Pro 2-bit | `adpcm_sbpro_2` |
| `adpcm_sbpro_3` | ADPCM Sound Blaster Pro 2.6-bit | `adpcm_sbpro_3` |
| `adpcm_sbpro_4` | ADPCM Sound Blaster Pro 4-bit | `adpcm_sbpro_4` |
| `adpcm_swf` | ADPCM Shockwave Flash | `adpcm_swf` |
| `adpcm_thp` | ADPCM Nintendo THP | `adpcm_thp` |
| `adpcm_thp_le` | ADPCM Nintendo THP (Little-Endian) | `adpcm_thp_le` |
| `adpcm_vima` | LucasArts VIMA audio | `adpcm_vima` |
| `adpcm_xa` | ADPCM CDROM XA | `adpcm_xa` |
| `adpcm_xmd` | ADPCM Konami XMD | `adpcm_xmd` |
| `adpcm_yamaha` | ADPCM Yamaha | `adpcm_yamaha` |
| `adpcm_zork` | ADPCM Zork | `adpcm_zork` |
| `alac` | ALAC (Apple Lossless Audio Codec) | `alac` |
| `amr_nb` | AMR-NB (Adaptive Multi-Rate NarrowBand) | `amrnb` |
| `amr_wb` | AMR-WB (Adaptive Multi-Rate WideBand) | `amrwb` |
| `apac` | Marian's A-pac audio | `apac` |
| `ape` | Monkey's Audio | `ape` |
| `aptx` | aptX (Audio Processing Technology for Bluetooth) | `aptx` |
| `aptx_hd` | aptX HD (Audio Processing Technology for Bluetooth) | `aptx_hd` |
| `atrac1` | ATRAC1 (Adaptive TRansform Acoustic Coding) | `atrac1` |
| `atrac3` | ATRAC3 (Adaptive TRansform Acoustic Coding 3) | `atrac3` |
| `atrac3al` | ATRAC3 AL (Adaptive TRansform Acoustic Coding 3 Advanced Lossless) | `atrac3al` |
| `atrac3p` | ATRAC3+ (Adaptive TRansform Acoustic Coding 3+) | `atrac3plus` |
| `atrac3pal` | ATRAC3+ AL (Adaptive TRansform Acoustic Coding 3+ Advanced Lossless) | `atrac3plusal` |
| `atrac9` | ATRAC9 (Adaptive TRansform Acoustic Coding 9) | `atrac9` |
| `avc` | On2 Audio for Video Codec | `on2avc` |
| `binkaudio_dct` | Bink Audio (DCT) | `binkaudio_dct` |
| `binkaudio_rdft` | Bink Audio (RDFT) | `binkaudio_rdft` |
| `bmv_audio` | Discworld II BMV audio | `bmv_audio` |
| `bonk` | Bonk audio | `bonk` |
| `cbd2_dpcm` | DPCM Cuberoot-Delta-Exact | `cbd2_dpcm` |
| `comfortnoise` | RFC 3389 Comfort Noise | `comfortnoise` |
| `cook` | Cook / Cooker / Gecko (RealAudio G2) | `cook` |
| `derf_dpcm` | DPCM Xilam DERF | `derf_dpcm` |
| `dfpwm` | DFPWM (Dynamic Filter Pulse Width Modulation) | `dfpwm` |
| `dolby_e` | Dolby E | `dolby_e` |
| `dsd_lsbf` | DSD (Direct Stream Digital), least significant bit first | `dsd_lsbf` |
| `dsd_lsbf_planar` | DSD (Direct Stream Digital), least significant bit first, planar | `dsd_lsbf_planar` |
| `dsd_msbf` | DSD (Direct Stream Digital), most significant bit first | `dsd_msbf` |
| `dsd_msbf_planar` | DSD (Direct Stream Digital), most significant bit first, planar | `dsd_msbf_planar` |
| `dsicinaudio` | Delphine Software International CIN audio | `dsicinaudio` |
| `dss_sp` | Digital Speech Standard - Standard Play mode (DSS SP) | `dss_sp` |
| `dst` | DST (Direct Stream Transfer) | `dst` |
| `dts` | DCA (DTS Coherent Acoustics) | `dca` |
| `dvaudio` | DV audio | `dvaudio` |
| `eac3` | ATSC A/52B (AC-3, E-AC-3) | `eac3` |
| `evrc` | EVRC (Enhanced Variable Rate Codec) | `evrc` |
| `fastaudio` | MobiClip FastAudio | `fastaudio` |
| `flac` | FLAC (Free Lossless Audio Codec) | `flac` |
| `ftr` | FTR Voice | `ftr` |
| `g723_1` | G.723.1 | `g723_1` |
| `g728` | G.728 | `g728` |
| `g729` | G.729 | `g729` |
| `gremlin_dpcm` | DPCM Gremlin | `gremlin_dpcm` |
| `gsm` | GSM | `gsm` |
| `gsm_ms` | GSM Microsoft variant | `gsm_ms` |
| `hca` | CRI HCA | `hca` |
| `hcom` | HCOM Audio | `hcom` |
| `iac` | IAC (Indeo Audio Coder) | `iac` |
| `ilbc` | iLBC (Internet Low Bitrate Codec) | `ilbc` |
| `imc` | IMC (Intel Music Coder) | `imc` |
| `interplay_dpcm` | DPCM Interplay | `interplay_dpcm` |
| `interplayacm` | Interplay ACM | `interplayacm` |
| `mace3` | MACE (Macintosh Audio Compression/Expansion) 3:1 | `mace3` |
| `mace6` | MACE (Macintosh Audio Compression/Expansion) 6:1 | `mace6` |
| `metasound` | Voxware MetaSound | `metasound` |
| `misc4` | Micronas SC-4 Audio | `misc4` |
| `mlp` | MLP (Meridian Lossless Packing) | `mlp` |
| `mp1` | MP1 (MPEG audio layer 1) | `mp1`、`mp1float` |
| `mp2` | MP2 (MPEG audio layer 2) | `mp2`、`mp2float` |
| `mp3` | MP3 (MPEG audio layer 3) | `mp3`、`mp3float` |
| `mp3adu` | ADU (Application Data Unit) MP3 (MPEG audio layer 3) | `mp3adu`、`mp3adufloat` |
| `mp3on4` | MP3onMP4 | `mp3on4`、`mp3on4float` |
| `mp4als` | MPEG-4 Audio Lossless Coding (ALS) | `als` |
| `msnsiren` | MSN Siren | `msnsiren` |
| `musepack7` | Musepack SV7 | `mpc7` |
| `musepack8` | Musepack SV8 | `mpc8` |
| `nellymoser` | Nellymoser Asao | `nellymoser` |
| `opus` | Opus (Opus Interactive Audio Codec) | `opus` |
| `osq` | OSQ (Original Sound Quality) | `osq` |
| `paf_audio` | Amazing Studio Packed Animation File Audio | `paf_audio` |
| `pcm_alaw` | PCM A-law / G.711 A-law | `pcm_alaw` |
| `pcm_bluray` | PCM signed 16\|20\|24-bit big-endian for Blu-ray media | `pcm_bluray` |
| `pcm_dvd` | PCM signed 20\|24-bit big-endian | `pcm_dvd` |
| `pcm_f16le` | PCM 16.8 floating point little-endian | `pcm_f16le` |
| `pcm_f24le` | PCM 24.0 floating point little-endian | `pcm_f24le` |
| `pcm_f32be` | PCM 32-bit floating point big-endian | `pcm_f32be` |
| `pcm_f32le` | PCM 32-bit floating point little-endian | `pcm_f32le` |
| `pcm_f64be` | PCM 64-bit floating point big-endian | `pcm_f64be` |
| `pcm_f64le` | PCM 64-bit floating point little-endian | `pcm_f64le` |
| `pcm_lxf` | PCM signed 20-bit little-endian planar | `pcm_lxf` |
| `pcm_mulaw` | PCM mu-law / G.711 mu-law | `pcm_mulaw` |
| `pcm_s16be` | PCM signed 16-bit big-endian | `pcm_s16be` |
| `pcm_s16be_planar` | PCM signed 16-bit big-endian planar | `pcm_s16be_planar` |
| `pcm_s16le` | PCM signed 16-bit little-endian | `pcm_s16le` |
| `pcm_s16le_planar` | PCM signed 16-bit little-endian planar | `pcm_s16le_planar` |
| `pcm_s24be` | PCM signed 24-bit big-endian | `pcm_s24be` |
| `pcm_s24daud` | PCM D-Cinema audio signed 24-bit | `pcm_s24daud` |
| `pcm_s24le` | PCM signed 24-bit little-endian | `pcm_s24le` |
| `pcm_s24le_planar` | PCM signed 24-bit little-endian planar | `pcm_s24le_planar` |
| `pcm_s32be` | PCM signed 32-bit big-endian | `pcm_s32be` |
| `pcm_s32le` | PCM signed 32-bit little-endian | `pcm_s32le` |
| `pcm_s32le_planar` | PCM signed 32-bit little-endian planar | `pcm_s32le_planar` |
| `pcm_s64be` | PCM signed 64-bit big-endian | `pcm_s64be` |
| `pcm_s64le` | PCM signed 64-bit little-endian | `pcm_s64le` |
| `pcm_s8` | PCM signed 8-bit | `pcm_s8` |
| `pcm_s8_planar` | PCM signed 8-bit planar | `pcm_s8_planar` |
| `pcm_sga` | PCM SGA | `pcm_sga` |
| `pcm_u16be` | PCM unsigned 16-bit big-endian | `pcm_u16be` |
| `pcm_u16le` | PCM unsigned 16-bit little-endian | `pcm_u16le` |
| `pcm_u24be` | PCM unsigned 24-bit big-endian | `pcm_u24be` |
| `pcm_u24le` | PCM unsigned 24-bit little-endian | `pcm_u24le` |
| `pcm_u32be` | PCM unsigned 32-bit big-endian | `pcm_u32be` |
| `pcm_u32le` | PCM unsigned 32-bit little-endian | `pcm_u32le` |
| `pcm_u8` | PCM unsigned 8-bit | `pcm_u8` |
| `pcm_vidc` | PCM Archimedes VIDC | `pcm_vidc` |
| `qcelp` | QCELP / PureVoice | `qcelp` |
| `qdm2` | QDesign Music Codec 2 | `qdm2` |
| `qdmc` | QDesign Music | `qdmc` |
| `qoa` | QOA (Quite OK Audio) | `qoa` |
| `ra_144` | RealAudio 1.0 (14.4K) | `real_144` |
| `ra_288` | RealAudio 2.0 (28.8K) | `real_288` |
| `ralf` | RealAudio Lossless | `ralf` |
| `rka` | RKA (RK Audio) | `rka` |
| `roq_dpcm` | DPCM id RoQ | `roq_dpcm` |
| `s302m` | SMPTE 302M | `s302m` |
| `sbc` | SBC (low-complexity subband codec) | `sbc` |
| `sdx2_dpcm` | DPCM Squareroot-Delta-Exact | `sdx2_dpcm` |
| `shorten` | Shorten | `shorten` |
| `sipr` | RealAudio SIPR / ACELP.NET | `sipr` |
| `siren` | Siren | `siren` |
| `smackaudio` | Smacker audio | `smackaud` |
| `sol_dpcm` | DPCM Sol | `sol_dpcm` |
| `speex` | Speex | `speex` |
| `tak` | TAK (Tom's lossless Audio Kompressor) | `tak` |
| `truehd` | TrueHD | `truehd` |
| `truespeech` | DSP Group TrueSpeech | `truespeech` |
| `tta` | TTA (True Audio) | `tta` |
| `twinvq` | VQF TwinVQ | `twinvq` |
| `vmdaudio` | Sierra VMD audio | `vmdaudio` |
| `vorbis` | Vorbis | `vorbis` |
| `wady_dpcm` | DPCM Marble WADY | `wady_dpcm` |
| `wavarc` | Waveform Archiver | `wavarc` |
| `wavesynth` | Wave synthesis pseudo-codec | `wavesynth` |
| `wavpack` | WavPack | `wavpack` |
| `westwood_snd1` | Westwood Audio (SND1) | `ws_snd1` |
| `wmalossless` | Windows Media Audio Lossless | `wmalossless` |
| `wmapro` | Windows Media Audio 9 Professional | `wmapro` |
| `wmav1` | Windows Media Audio 1 | `wmav1` |
| `wmav2` | Windows Media Audio 2 | `wmav2` |
| `wmavoice` | Windows Media Audio Voice | `wmavoice` |
| `xan_dpcm` | DPCM Xan | `xan_dpcm` |
| `xma1` | Xbox Media Audio 1 | `xma1` |
| `xma2` | Xbox Media Audio 2 | `xma2` |

### 字幕软件解码完整列表

共 20 个格式条目。

| 格式名 | 格式说明 | 软件 decoder 名称 |
| --- | --- | --- |
| `ass` | ASS (Advanced SSA) subtitle | `ass`、`ssa` |
| `dvb_subtitle` | DVB subtitles | `dvbsub` |
| `dvd_subtitle` | DVD subtitles | `dvdsub` |
| `eia_608` | EIA-608 closed captions | `cc_dec` |
| `hdmv_pgs_subtitle` | HDMV Presentation Graphic Stream subtitles | `pgssub` |
| `jacosub` | JACOsub subtitle | `jacosub` |
| `microdvd` | MicroDVD subtitle | `microdvd` |
| `mov_text` | MOV text | `mov_text` |
| `mpl2` | MPL2 subtitle | `mpl2` |
| `pjs` | PJS (Phoenix Japanimation Society) subtitle | `pjs` |
| `realtext` | RealText subtitle | `realtext` |
| `sami` | SAMI subtitle | `sami` |
| `stl` | Spruce subtitle format | `stl` |
| `subrip` | SubRip subtitle | `srt`、`subrip` |
| `subviewer` | SubViewer subtitle | `subviewer` |
| `subviewer1` | SubViewer v1 subtitle | `subviewer1` |
| `text` | raw UTF-8 text | `text` |
| `vplayer` | VPlayer subtitle | `vplayer` |
| `webvtt` | WebVTT subtitle | `webvtt` |
| `xsub` | XSUB | `xsub` |

## 内部用途条目

这三项注册在 decoder 列表中，但不计入上面的普通媒体格式数量。

| decoder | 用途 |
| --- | --- |
| `anull` | 内部空音频 codec |
| `vnull` | 内部空视频 codec |
| `wrapped_avframe` | AVFrame/AVPacket 内部传递接口；不是可直接作为外部媒体文件输入的通用压缩格式 |

再加上单独列出的硬件入口 `av1`，上述 474 个软件实现与这三项正好对应共同配置的 478 个 decoder 实现。Android / Apple 的额外包装器已在前文逐项列全。

## 核对来源与更新方法

版本依据为 [ffmpeg.lock.json](ffmpeg.lock.json) 和 [dependencies.lock.json](dependencies.lock.json)。实际启用项来自每个目标的 `build-manifest.json` 与 `configure.txt`，不是从 FFmpeg 上游支持列表直接复制：

| 目标组 | 代表性配置 |
| --- | --- |
| Windows | [x86](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86/configure.txt)、[x64](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64/configure.txt) |
| Linux | [x64](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64/configure.txt) |
| Android | [ARMv7](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/Android/armeabi-v7a/configure.txt)、[ARM64](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/Android/arm64-v8a/configure.txt) |
| macOS | [x64](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/macOS/x86_64/configure.txt)、[ARM64](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/macOS/arm64/configure.txt) |
| iOS | [device ARM64](../../Assets/Plugins/MajdataPlay/FFmpeg/Native/iOS/configure.txt)；两个 simulator 配置位于本地 `.build/artifacts/ios-simulator-*/configure.txt` |

重建或升级后，应重新执行以下核对流程，再更新本文：

1. 按 [构建说明](README.md) 构建目标，执行 `python Tools/FFmpeg/verify-artifacts.py` 检查交付库和清单。
2. 提取各目标 `Enabled decoders`、`Enabled hwaccels` 的名称集合，检查平台与架构差异。不要把列排版顺序作为格式差异。
3. 在可加载目标库的进程里调用 `av_codec_iterate()` 和 `av_codec_is_decoder()` 枚举，使用 `avcodec_descriptor_get()` 取得格式及媒体类型，并按 codec ID 去重；用 `avcodec_get_hw_config()` 查看已导出的硬件配置。
4. 对照 Player 的后端选择与桥接帧格式限制；硬件纯入口和内部传递接口单独归类。`AV_CODEC_CAP_HARDWARE` 标志不能单独解决分类问题，当前 `av1` 就需要结合实现判断。
5. 用实际素材验证关心的位深、profile 和设备；把“已编译”“成功打开”“真正解码”“Player 已显示”分别记录。

API 布局以项目固定的 [codec.h](../../ThirdParty/FFmpeg.AutoGen/FFmpeg/include/libavcodec/codec.h) 和 [codec_desc.h](../../ThirdParty/FFmpeg.AutoGen/FFmpeg/include/libavcodec/codec_desc.h) 为准。枚举与硬件配置 API 的通用含义可参考 [FFmpeg 官方 API 文档](https://ffmpeg.org/doxygen/trunk/group__lavc__core.html)；官方网页会随上游更新，不作为本项目具体版本支持列表的替代。

本次核对使用的完整原始枚举与配置集合保存在忽略目录 `.build/codec-support/`，本文已包含全部格式清单，阅读文档不依赖这些本机缓存。
