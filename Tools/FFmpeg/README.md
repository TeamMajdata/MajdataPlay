# FFmpeg 原生库构建

这些脚本从 FFmpeg 源码构建当前项目的 Unity 原生插件，不下载现成 FFmpeg 二进制来冒充本机编译。

## 固定版本和 ABI

`ffmpeg.lock.json` 固定官方 `n9.0.1` 的 commit
`bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`。它与项目
`ThirdParty/FFmpeg.AutoGen/FFmpeg/include` 中的公开头文件逐文件匹配。
每次构建都会核对 commit、拒绝源码修改，并比较绑定头文件（仅归一化 CRLF；排除构建生成的 `avconfig.h`、`ffversion.h`）。
**不要用 FFmpeg 8.x，也不要只改原生库文件名**。库主版本为 avcodec 63、avdevice 63、avfilter 12、avformat 63、avutil 61、swresample 7、swscale 10。

## 一键命令

先安装本节下方列出的主机工具链，然后在仓库根目录执行：

```powershell
# Windows PowerShell：自动构建本机具备工具链的所有目标
.\Tools\FFmpeg\build.ps1
# 严格要求全部目标都有工具链：不可构建的目标会返回非零
.\Tools\FFmpeg\build.ps1 -RequireAll
# 只检查环境，不下载或编译
.\Tools\FFmpeg\build.ps1 -Probe
# 指定目标
.\Tools\FFmpeg\build.ps1 -Targets win-x64,win-x86,android-arm64,android-armv7 -Jobs 12
# WSL 入口；Linux 目标另需下方列出的 pkg-config / VAAPI / DRM 开发依赖
.\Tools\FFmpeg\build.ps1 -UseWsl -WslDistribution Ubuntu-24.04 -BuildRoot /tmp/majdata-ffmpeg-build
```

```bash
# Linux / macOS
bash Tools/FFmpeg/build.sh
bash Tools/FFmpeg/build.sh --targets linux-x64,android-arm64,android-armv7 --jobs 12
bash Tools/FFmpeg/build.sh --targets macos-x64,macos-arm64,ios-arm64,ios-simulator-arm64,ios-simulator-x64
bash Tools/FFmpeg/build.sh --probe --require-all
```

Windows 的已有 WSL Linux 也可运行同一个 `build.sh`。使用 WSL 时使用 Linux 版工具链和 NDK，不能把 Windows NDK 目录交给 Linux 编译器。
`-UseWsl` 不会安装 Linux 依赖，也不会自动启用本次缓存中单独解包的 VAAPI 工具链。若 WSL 系统没有可运行的 `pkg-config` 和 `libva` / `libdrm` 开发模块，Linux 目标会明确 SKIP；Windows/Android 仍按各自工具链继续。隔离依赖的运行方式见下方示例。
脚本默认跳过明确缺少 SDK 的目标，在 `.build/build-summary.json` 列出原因；编译错误始终返回非零。
`--require-all` / `-RequireAll` 让缺少 SDK 也返回非零，可用于 CI 矩阵防止漏包。
任何主机都不可能在没有对应 SDK 的情况下原生构建所有平台；Apple 目标需要 macOS + Xcode。

## 工具链矩阵

所有主机都需要 Python 3.9+、Git、Bash、GNU make、C/C++ 编译器。Linux 目标另需 `patchelf`、`pkg-config`、libva 与 libdrm 开发文件（Ubuntu/Debian 包名为 `libva-dev`、`libdrm-dev`）；脚本预检 `libva` / `libdrm` 模块并显式启用 VAAPI 和 DRM PRIME 导出。隔离工具链可通过 `PKG_CONFIG_PATH` / `PKG_CONFIG_SYSROOT_DIR` 提供依赖，运行时也需要对应的 libva/libdrm 与 GPU 驱动。stage 直接写入并读回 `$ORIGIN`，避免 configure/make 多重解析破坏相对库搜索路径。x86/x64 的汇编加速需要 NASM；缺少时脚本明确报告并禁用 x86 汇编，仍可编译。
脚本不会安装系统软件，也不会覆盖既有 ThirdParty 绑定。缓存/源码/中间产物位于忽略的 `.build/`。

| 目标 | Windows | Linux | macOS | 必需工具链 |
| --- | --- | --- | --- | --- |
| win-x86、win-x64 | 支持 | 支持 | 支持 | MinGW-w64 或对应主机版 llvm-mingw |
| linux-x64 | WSL 或交叉工具链 | 本机构建 | 交叉工具链 | glibc x64 sysroot + GCC/Clang |
| android-armv7、android-arm64 | 支持 | 支持 | 支持 | 对应主机版 Android NDK r27+ |
| macos-x64、macos-arm64 | 不支持 | 不支持 | 支持 | Xcode macOS SDK |
| ios-arm64、ios-simulator-arm64、ios-simulator-x64 | 不支持 | 不支持 | 支持 | Xcode iPhoneOS/Simulator SDK |

可设置以下环境变量：

- `FFMPEG_BASH`：Windows 上 MSYS2 `usr/bin/bash.exe` 的路径。也会检测项目 `.build/toolchains/msys64`、`C:/msys64`、Git Bash。某些 Windows 安全策略下 Git Bash/MSYS2 反复出现 `child_copy / fork` 错误，应使用已有 WSL；脚本不会修改系统安全策略。
- `FFMPEG_BUILD_ROOT`：默认 `Tools/FFmpeg/.build`；WSL 建议使用 Linux 文件系统内的专用临时目录以避免 `/mnt/c` 的编译性能损失。PowerShell 的 `-BuildRoot` 会传入对应 Windows/WSL 进程。
- `FFMPEG_MAKE`：GNU make 命令名，默认 `make`。不要使用 MSVC `nmake`。
- `LLVM_MINGW`：便携 llvm-mingw 根目录。也自动发现 `.build/toolchains/llvm-mingw-*`。
- `ANDROID_NDK_HOME` / `ANDROID_NDK_ROOT`：NDK 根目录；Windows 额外自动查找 Unity Hub 编辑器内置 NDK。
- `ANDROID_API`：默认 23；ARMv7 使用 softfp/NEON，ARM64 使用 AArch64。链接时指定 16 KiB 最大页大小。
- `LINUX_X64_CROSS_PREFIX` 与 `LINUX_X64_SYSROOT`：跨主机构建 Linux glibc x64 时一起设置。
- `MACOS_MIN`：默认 11.0。`IOS_MIN`：默认 15.0（与当前 Xcode libc++ 最低支持版本一致）。

发行版依赖示例（由开发者自行安装）：Ubuntu 的 `build-essential git python3 nasm patchelf mingw-w64 cmake pkg-config libva-dev libdrm-dev`；macOS 的 Xcode command line tools 和 GNU make/NASM；Windows 的 MSYS2 make/NASM 与 MinGW-w64 或 llvm-mingw。
NDK 使用 [Google 官方下载](https://developer.android.com/ndk/downloads)。交叉编译器可用 [llvm-mingw 官方发布](https://github.com/mstorsjo/llvm-mingw/releases)。FFmpeg 的主机约束参考 [官方平台文档](https://ffmpeg.org/platform.html)。

本次 Linux 最终产物使用独立缓存 `/tmp/majdata-ffmpeg-vulkan`。libva/libdrm 开发包、运行时与 pkgconf 从 Ubuntu 仓库下载后只解包到项目缓存，未系统安装。若保留了本次 `.build/toolchains/vaapi-packages/root` 和 `linux-packages`，可在 **WSL 的仓库根目录** 使用以下环境；此配置已实际预检为 `READY linux-x64`：

```bash
(
  ffmpeg_cache="$PWD/Tools/FFmpeg/.build"
  ffmpeg_deps="$ffmpeg_cache/toolchains/vaapi-packages/root"
  export FFMPEG_BUILD_ROOT=/tmp/majdata-ffmpeg-vulkan
  export PATH="$ffmpeg_deps/usr/bin:$ffmpeg_cache/toolchains/linux-packages/usr/bin:$PATH"
  export LD_LIBRARY_PATH="$ffmpeg_deps/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  export PKG_CONFIG="$ffmpeg_deps/usr/bin/pkg-config"
  export PKG_CONFIG_PATH="$ffmpeg_deps/usr/lib/x86_64-linux-gnu/pkgconfig"
  export PKG_CONFIG_SYSROOT_DIR="$ffmpeg_deps"
  bash Tools/FFmpeg/build.sh --targets linux-x64 --probe --require-all
  # 实际重建：将上行的 --probe 替换为 --jobs 12。
)
```

该示例只适用于已经解包好的本次缓存；全新环境应安装上面的开发依赖，或自行准备等价 sysroot/pkg-config 包装器。这里的 `LD_LIBRARY_PATH` 只供隔离构建工具与依赖运行，交付库使用 `$ORIGIN`，播放器不需要此环境变量。

## 配置与输出

构建所有内置解码器、解复用器、解析器、网络协议、swscale、swresample，以及七个绑定库。
关闭编码器、封装器、采集设备、滤镜实现、命令行程序、文档、调试符号和依赖自动探测。
不启用 GPL/nonfree/外部编解码库；AV1 等仅依赖外部库的解码器不自动加入。
Windows 显式启用 D3D11VA/DXVA2 和 Schannel；Apple 启用 VideoToolbox/AudioToolbox/SecureTransport；Android 启用 JNI/MediaCodec；Linux 启用 VAAPI/libdrm。
Linux/Android 默认没有外部 TLS 后端，支持本地文件和 HTTP；HTTPS 如有需求须将可审计的 TLS 库纳入构建配置和部署依赖。
图形 API 互操作由 `Native/` 的 Unity 原生桥接实现，启用硬件解码选项本身不代表纹理零拷贝已实现或经过设备验证。

输出位置：

| 目标 | Unity 插件目录 |
| --- | --- |
| Windows x86/x64 | `Assets/Plugins/FFmpeg/Native/Windows/x86`、`x86_64` |
| Linux x64 | `Assets/Plugins/FFmpeg/Native/Linux/x86_64` |
| Android ARMv7/ARM64 | `Assets/Plugins/FFmpeg/Native/Android/armeabi-v7a`、`arm64-v8a` |
| macOS x64/ARM64 | `Assets/Plugins/FFmpeg/Native/macOS/x86_64`、`arm64` |
| iOS device | `Assets/Plugins/FFmpeg/Native/iOS` |
| iOS simulator | `.build/artifacts/ios-simulator-*`（不与 device 同时导入） |

每个库有确定性 `.meta`，关闭 Any Platform，选择具体平台/CPU；只有对应桌面架构可在 Editor 使用。
每个目标同时输出 `build-manifest.json`（commit、命令、主机、UTC 时间、文件 SHA256）、`configure.txt` 和 LGPL 许可。
完整日志和供原生桥接使用的头文件/import libs 在 `.build/<target>/` 与 `.build/install/<target>/`。
不要对构建目录执行不经确认的通配符清理；增量重建会保留现有缓存。

## GPU 桥接

默认同时构建各目标的原生桥接；`Native/Unity` 已附带对应版本的官方 PluginAPI 头文件。
Windows 桥接 ABI 2 实现 D3D11、D3D12、OpenGL Core、Vulkan 的 GPU 互操作，Apple ABI 2 实现 VideoToolbox/CoreVideo→Metal。Linux/Android ABI 3 实现 VAAPI/DRM PRIME 与 MediaCodec/AHardwareBuffer 到 Vulkan 的互操作。所需 Vulkan 头文件与许可随源码附带，不依赖另装 Vulkan SDK。Windows、Linux、Android 桥接的 `.meta` 默认开启 Preload，以便在 Vulkan 设备创建前追加已探测支持的共享扩展；替换已加载的桥接后需重启 Unity。硬件路径仍要求驱动、设备和解码格式共同支持，实际验证范围见构建记录与播放器测试记录。
可用 `--unity-plugin-api <Editor/Data/PluginAPI>` / `-UnityPluginApi <path>` 显式选择 SDK。
桥接在 Windows、Apple、Linux 和 Android 目标上由同一脚本构建并按目标 stage。CMake 与目标编译器/SDK也必须就绪。Android 使用同一 NDK 的 `android.toolchain.cmake`，同步 ABI/API 设置并静态链接 C++ 运行库；Linux 复用选中的宿主或交叉 C/C++ 编译器与 sysroot。各平台可用的 GPU 路径以运行时能力检测和该平台测试结果为准。
Windows 桥接复用 FFmpeg 选中的目标 C/C++ 编译器、archiver、windres 及 PATH：选择 llvm-mingw 时桥接使用相同的 `<triple>-clang/clang++`，选择 MinGW GCC 时使用相同的 `<triple>-gcc/g++`。这在 Windows、Linux 和 macOS 主机上一致；一键脚本不假设已安装 MSVC。
CMake 默认优先 Ninja；没有 Ninja 时 Windows 使用 `mingw32-make` / `MinGW Makefiles`，Linux/macOS 使用 GNU make / `Unix Makefiles`。可以用 `FFMPEG_BRIDGE_GENERATOR` 显式选择这三种之一。缺少对应生成器工具或 C++ 编译器会在 `--probe` / `-Probe` 阶段给出明确原因。编译器/生成器配置不同会使用独立缓存，避免与旧 MSVC 缓存混用。
如果需要手动使用 MSVC，请按 `Native/` 的独立 CMake 流程构建；一键流程始终跟随 FFmpeg 的 MinGW/LLVM 工具链。
`--without-bridge` / `-WithoutBridge` 可仅构建桌面 FFmpeg 库；iOS 的 `__Internal` 符号要求桥接静态库，因此 iOS 不允许省略桥接。
也可使用 `Native/` 的独立 CMake 指令；硬件零拷贝限制和退回软件上传的行为见该目录文档。

## 验证与分发

本机实际构建结果见 `BUILD-RESULTS.md`，不要把脚本覆盖的平台矩阵当作全部平台已经真机验证。
使用项目的 native smoke/decode 测试以及 Unity 的 Mono/IL2CPP Player 验证版本、打开、解码、seek 和退出。
`python Tools/FFmpeg/verify-artifacts.py` 可复查所有已 stage 库的 SHA256、PE/ELF/Mach-O 目标架构、Apple device/simulator 平台标记、Android 16 KiB LOAD 对齐、Linux 运行时依赖及许可哈希，以及当前主机的 FFmpeg ABI 加载。
Linux stage 会附带 `libva.so.2`、`libva-drm.so.2`、`libdrm.so.2` 和对应许可，避免软件播放也因缺少这些直接依赖而无法加载 FFmpeg。厂商 GPU 驱动仍由目标系统提供。使用自定义 sysroot 时，开发文件和运行时库应属于同一套版本；许可默认从 sysroot 的发行版文档查找，也可通过 `FFMPEG_LINUX_RUNTIME_LICENSE_DIR` 提供 `libva.copyright`、`libva-drm.copyright`、`libdrm.copyright`。
Apple 五个目标的实际构建、原生 Metal/VideoToolbox 测试及当前限制见 [APPLE-RESULTS.md](APPLE-RESULTS.md)。
macOS 发布需要应用签名/公证；iOS 静态链接需遵守 LGPL 的重链接要求。
FFmpeg 源码固定在上述官方 commit，许可和构建配置随产物提供；正式分发须同时履行 [FFmpeg 官方许可要求](https://ffmpeg.org/legal.html)。
