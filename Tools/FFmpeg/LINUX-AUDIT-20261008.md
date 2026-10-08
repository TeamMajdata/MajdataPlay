# Linux x64 FFmpeg/native audit — 2026-10-08

## 结论与范围

- 七个 FFmpeg 原生库与固定 `n9.0.1` / commit `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa` 匹配：完整运行时版本、绑定头文件、SHA256、ELF、SONAME、RUNPATH、动态依赖及 Unity importer 均通过；**不需要且未重编七库**。
- 原 bridge 的实际 Linux 构建输入 `CMakeLists.txt`、`VulkanPortable.cpp`、`VulkanPortable.h` 已变化（非 CRLF/LF 差异），因此单独重建并安装 bridge，ABI 仍为 4。`D3D11.cpp` / `VulkanInterop.cpp` 的 Windows-only 变化、测试和 README 不用于判定 Linux bridge stale。
- 三个依赖 `.copyright` 的初始 SHA 异常仅来自 Windows checkout 的 CRLF 转换，**不是 FFmpeg/native ABI 不匹配**。从原 Ubuntu 缓存 `.deb` 核对并恢复上游 LF 字节；没有修改 `dependency-manifest.json` 的 SHA 去迎合错误文件。
- 全部七库、三个运行时依赖的二进制、`build-manifest.json`、`dependency-manifest.json`、全部 `.meta` 字节及 GUID 均保持原样；未修改任何子模块内容/指针、共享构建或验证脚本逻辑、共同文档。后续有限授权仅将下列三个 shell 脚本恢复 LF，以排除 README 命令的复现障碍。
- 主 agent 独立为 `.copyright` 添加 LF Git 属性，本 agent 未修改 `.gitattributes`。

## 前置阅读与工具链

已读取仓库 AGENTS/README、FFmpeg runtime README、Tools/FFmpeg 和 Native 构建说明、runtime/editor `.asmdef`、FFmpegValidation README 相关 Linux/native/Unity 验证入口；检查作用域内无额外 AGENTS 覆盖。

主 agent 已初始化子模块。开始时执行 `git status --short`，工作区干净；后续其他平台/共享文件改动不由本 agent 编辑或清理。

- WSL 2：Ubuntu-24.04，Ubuntu 24.04.1 LTS，x86-64；内核 `6.18.40.1-microsoft-standard-WSL2`。
- GCC/G++ 13.3.0，Python 3.12.3，CMake 3.28.3，Ninja，GNU readelf/ldd。
- 隔离构建使用已有 `vaapi-packages/root` 的 pkgconf/VAAPI/DRM 和 `linux-packages/usr/bin` 的 patchelf/NASM。系统默认 PATH 会误找到 Windows Strawberry 的 pkg-config；没有使用它。
- 复用共享缓存只读：锁定 FFmpeg 源码、固定 Vulkan-Headers SDK 1.4.328.1（commit `19725e4d48082fe78e26622b15d3080ccd54112b`）及 Ubuntu 原始 `.deb`；没有并发修改源码、下载锁或缓存。
- 唯一新增构建/证据根：`Tools/FFmpeg/.build/linux-audit-20261008`（忽略目录）。

## 锁定版本与 ELF 审计

缓存源码 HEAD 与锁定 commit 一致；143 个公开绑定头文件逐文件比较通过（仅归一化 CRLF；排除生成的 `avconfig.h` / `ffversion.h`）。七库 `*_version()` 的 major/minor/micro **全部与对应 `version*.h` 的完整宏值一致**。

11 个库全部为 ELF64、little-endian、ET_DYN、`EM_X86_64 = 62`。所有 build/dependency/bridge manifest 的二进制 bytes 和 SHA256 匹配；所有插件 importer 含正确 Linux64/x86_64 设置，bridge 保持预加载。每个库的 `ldd` 都在清除 `LD_LIBRARY_PATH` 后检查，无 `not found`。

| 文件 | 完整版本 / ABI | SONAME | RUNPATH | 最大引用 GLIBC | DT_NEEDED |
| --- | --- | --- | --- | --- | --- |
| `libavcodec.so.63` | 63.1.101 | `libavcodec.so.63` | `$ORIGIN` | 2.38 | `libswresample.so.7, libavutil.so.61, libm.so.6, libmvec.so.1, libva.so.2, libgcc_s.so.1, libc.so.6, ld-linux-x86-64.so.2` |
| `libavdevice.so.63` | 63.1.101 | `libavdevice.so.63` | `$ORIGIN` | 2.4 | `libavformat.so.63, libavutil.so.61, libc.so.6` |
| `libavfilter.so.12` | 12.1.101 | `libavfilter.so.12` | `$ORIGIN` | 2.14 | `libavutil.so.61, libc.so.6` |
| `libavformat.so.63` | 63.1.101 | `libavformat.so.63` | `$ORIGIN` | 2.35 | `libavcodec.so.63, libavutil.so.61, libm.so.6, libc.so.6` |
| `libavutil.so.61` | 61.1.101 | `libavutil.so.61` | `$ORIGIN` | 2.35 | `libva-drm.so.2, libva.so.2, libm.so.6, libdrm.so.2, libc.so.6` |
| `libswresample.so.7` | 7.1.101 | `libswresample.so.7` | `$ORIGIN` | 2.29 | `libavutil.so.61, libm.so.6, libc.so.6` |
| `libswscale.so.10` | 10.1.101 | `libswscale.so.10` | `$ORIGIN` | 2.34 | `libavutil.so.61, libm.so.6, libc.so.6` |
| `libva.so.2` | dependency ABI 2 | `libva.so.2` | `$ORIGIN` | 2.38 | `libc.so.6` |
| `libva-drm.so.2` | dependency ABI 2 | `libva-drm.so.2` | `$ORIGIN` | 2.34 | `libva.so.2, libdrm.so.2, libc.so.6` |
| `libdrm.so.2` | dependency ABI 2 | `libdrm.so.2` | `$ORIGIN` | 2.38 | `libc.so.6` |
| `libFFmpegUnityBridge.so` | bridge ABI 4 | `libFFmpegUnityBridge.so` | `$ORIGIN` | 2.38 | `libavutil.so.61, libc.so.6, ld-linux-x86-64.so.2` |

**部署限制**：该库集引用 `GLIBC_2.38`，需要提供这些符号的 Linux 运行时；当前 Ubuntu WSL 验证通过不代表更旧发行版兼容。`libgcc_s.so.1`、glibc/loader/libm/libmvec 由宿主提供；随包 `libva` / `libva-drm` / `libdrm` 保持不变。GPU 专用驱动不打包。

## 版权来源与换行修复

原始包通过 `dpkg-deb --fsys-tarfile` 只读提取声明，逐字节比对已解包缓存和 manifest 的已有 `licenseSha256` 后才恢复文件。归一化前：libva 两份各 5689 bytes（118 CRLF），libdrm 15928 bytes（316 CRLF）；上游原始 LF 如下。

| 声明 | 原始包 | 恢复后 bytes | 保留的 manifest SHA256 |
| --- | --- | --- | --- |
| `libva.copyright` | `libva2_2.20.0-2ubuntu0.2_amd64.deb` | 5571 | `8d234a9a0e094a1c13a4e21e2a28856763a1890306e17679502c894606f8638c` |
| `libva-drm.copyright` | `libva-drm2_2.20.0-2ubuntu0.2_amd64.deb` | 5571 | `8d234a9a0e094a1c13a4e21e2a28856763a1890306e17679502c894606f8638c` |
| `libdrm.copyright` | `libdrm2_2.4.125-1ubuntu0.1~24.04.2_amd64.deb` | 15612 | `512c9a57fd8b449aa157351da9df07bf8e19f767e496a72205a4020268ddb2e0` |

缓存 `.deb` 自身的 SHA256/详细来源记录在本次忽略证据 `copyright-provenance.json`。这里验证了现有原始缓存包、解包内容及 manifest 一致；没有把重新计算的 CRLF 哈希写入清单。LF 恢复与 Git index 的原 LF 内容一致，因而这些文件的内容 diff 为空是正常现象。

## Bridge 重建与部署

仅调用 `Tools/FFmpeg/build.py` 的 `toolchain` / `bridge_plan` / `build_bridge`，不调用全量 FFmpeg 构建。为本次创建私有 include/lib prefix：include 来自已验证的绑定头文件，lib 链接当前 staged 库；不会修改子模块或已有 prefix。

- 编译输入：`Bridge.cpp`、`VulkanPlatform.cpp`、`VulkanPortable.cpp`、`VulkanVideoDecode.cpp`、`LinuxVaapi.cpp` 和其头文件/Unity SDK/SPIR-V/固定 Vulkan 头文件。
- CMake Release + Ninja，GCC/G++ 13.3.0，静态 C++/GCC bridge runtime，`$ORIGIN` RUNPATH。
- 实际 flags：`-Wall -Wextra -Werror -Wno-missing-field-initializers -Wno-deprecated-declarations`（与原交付 manifest 的 warning 设置一致）。额外尝试全量 `-Werror` 时，既有 Vulkan sType 聚合初始化和 FFmpeg 兼容队列钩子的弃用声明触发警告；未为这些试验改写共享源码，最终采用上述明确的 exclusions 后完整编译/链接通过。
- `bridge-manifest.json` 更新真实 build time/host/CMake 参数、原始和 LF-normalized source fingerprints、artifact SHA/bytes，并记录本次验证。不从 README/test-only/EOL 差异推断生产 bridge stale。
- 原 bridge：477616 bytes，SHA256 `afb9dc6757cbca5350c75131749486dbd15a8281af05ad3b3f92d2e121797015`。
- 新 bridge：477560 bytes，SHA256 `0db1c6a1a3ffef71f57df1c7cbcbc7de9d4a3c7cf2ff3149e1a0a5a40b59fb9f`。
- 安装后按初始 snapshot 确认十个 FFmpeg/依赖二进制和所有 `.meta` 完全未变。

精确 CMake 参数在交付 bridge manifest 和忽略证据 `bridge-plan.json` 中；完整输出在 `bridge-build.txt`。重建调用记录为本次缓存内 `rebuild-bridge.py` / `rebuild-bridge.sh`，未新增共用脚本。

## 实际验证结果

| 验证 | 实际结果 |
| --- | --- |
| `python Tools/FFmpeg/verify-artifacts.py --targets linux-x64 --skip-host-load`（Windows） | PASS：七库 SHA/ELF/importer/full version provenance、三个依赖/版权、bridge hash/fingerprint |
| `python3 Tools/FFmpeg/verify-artifacts.py --targets linux-x64`（WSL） | PASS：上述静态检查 + 真实七库精确 ABI/libversion 和 bridge ABI 4 加载；libdav1d/硬件配置/录制入口存在检查通过，不等同设备编码解码 |
| README `run-linux.sh`，未设置 `LD_LIBRARY_PATH` | **PASS，144 checks**；H.264 1920×1080，30 frames，RGBA 0–177，checksum `ae2450db4c195031`；avformat 绝对 dlopen，avcodec 实际来自 staged 目录 |
| `LoadLinuxBridge.py <staged bridge>`，未设置 `LD_LIBRARY_PATH` | PASS：ABI 4、缺失设备/非法帧防护、空 retirement，同目录 FFmpeg/VAAPI 依赖来源 |
| `FFmpegUnityVulkanLifetime` | PASS：inactive generation 保留 GPU owner；仅完成 fence/真实 device-lost 可退休；并发 shutdown/resource lock 无 ABBA deadlock/stale-generation packet |
| `FFmpegUnityVulkanNegotiation` | PASS：frame gate/递归 DPB locks；1000 contention skips 无 GPU submit；foreign pool 拒绝；feature/queue/fallback/真实 FFmpeg buffer 生命周期 |
| `FFmpegUnityVulkanCompute`，`VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json` | **PASS，llvmpipe LLVM 20.1.2**；256 nonblocking polls，late timeline 拒绝，NV12/RGB 像素，8 submits/16384 pixel bytes/4 locked timeline submits，owner retirement/cancel/24 cap/stale-device |
| `EncoderNativeSmoke.c`（真实 staged Linux 库） | **PASS，5803 checks**：MPEG4 CBR/VBR、60 帧/模式、像素方向/PTS/排空/worker 上限/独占输出；CBR measured filling |
| `NativeSoftwareEncoderSmoke.c`（真实 staged Linux 库） | **PASS，31820 checks**：x264/x265/AOM/VP9 四软件编码器，CBR/VBR 平坦/复杂共 16 文件，pixels/PTS/flush/workers/no-overwrite，非法 x265 参数拒绝；不把 VP9/AOM 单遍预算当瞬时硬上限 |
| 指定 Unity `6000.3.17f1` 隔离 Linux x64 Mono build | **PASS**，High stripping；11 个打包 native 文件 SHA256 全部与 staged 库一致 |
| WSLg 新 Player OpenGL Core smoke | **PASS，35 assertions，76 frames**；实际 renderer `llvmpipe (LLVM 20.1.2, 256 bits)`，OpenGL 4.5 / Mesa 25.2.8；`Software RGBA upload`，软件解码/实际纹理像素及普通生命周期验证 |
| WSLg 新 Player Vulkan smoke | **本环境失败/不可验证，不是 PASS**：Unity 在 playback 前拒绝唯一 llvmpipe `deviceType=4`；`Vulkan detection: 0` / `Forced renderer 21 is not supported`；runner exit 1 |

### 命令与 shell 换行条件

Windows build 命令：

```powershell
& Tools/Tests/FFmpegValidation/run-unity.ps1 `
  -UnityEditor 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe' `
  -Platform Linux -Backend Mono -Architecture x64 -Graphics glcore -BuildOnly `
  -WorkDirectory Tools/FFmpeg/.build/linux-audit-20261008/unity
```

初次验证时，Windows checkout 的共享 `.sh` 是 CRLF；直接 Bash 调用原 `run-linux.sh` 首先在 `pipefail\r` 处失败，未开始 native 检查。首次测试未编辑共享脚本，而是使用 `run-repo-shell.py` 将同一文件内容在内存转换 LF，以原文件路径作为 `$0`，原 `here`/repo/media/.work 语义不变后实际执行。Unity Player runner 在忽略目录使用 LF 等价副本，并显式提供新 Player 的绝对目录；这些先前结果和限制准确，保持原记录。随后已按有限授权完成下节 LF-only 修复，原脚本现在可以直接执行，不再需要此转换层。

对应首次验证的 WSL 调用记录（保留历史证据；修复后不再需要 LF shim）：

```bash
root="$PWD/Tools/FFmpeg/.build/linux-audit-20261008"
libs="$PWD/Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64"
TMPDIR="$root/tmp" env -u LD_LIBRARY_PATH python3 "$root/run-repo-shell.py"   "$PWD/Tools/Tests/FFmpegValidation/run-linux.sh" "$libs"
env -u LD_LIBRARY_PATH python3 Tools/FFmpeg/Native/tests/LoadLinuxBridge.py   "$libs/libFFmpegUnityBridge.so"
env -u LD_LIBRARY_PATH bash "$root/run-linux-player-lf.sh" glcore "$root/unity/Linux-x64-Mono"
env -u LD_LIBRARY_PATH bash "$root/run-linux-player-lf.sh" vulkan "$root/unity/Linux-x64-Mono"
```

C11 录制检查使用 `-std=c11 -Wall -Wextra -Werror`、绑定 include、当前 staged 库和 `-l:libavformat.so.63 -l:libavcodec.so.63 -l:libswscale.so.10 -l:libavutil.so.61 -lm`；完整编译/执行记录在 `validate-native.sh`，输出均在独立缓存。

## 复现障碍的有限修复 — 2026-10-08

按主 agent 后续明确授权，只将以下三个文件从 CRLF 恢复为 LF：

| 文件 | bytes 变化 | 实际改动 |
| --- | --- | --- |
| `Tools/FFmpeg/build.sh` | 148 → 144 | 去除 4 个 CRLF 中的 CR |
| `Tools/Tests/FFmpegValidation/run-linux.sh` | 923 → 904 | 去除 19 个 CRLF 中的 CR |
| `Tools/Tests/FFmpegValidation/run-linux-player.sh` | 1456 → 1421 | 去除 35 个 CRLF 中的 CR |

转换后字节逐一断言等于转换前 `bytes.replace(b'\r\n', b'\n')`，LF-normalized SHA256 完全一致，且没有残留 CR；未改变逻辑、缩进或其他脚本。主 agent 负责 `.gitattributes` 的 shell LF 规则，本 agent 没有编辑 `.gitattributes`、`build.py` 或 README。

WSL 直接对原始路径执行：

```bash
bash -n Tools/FFmpeg/build.sh
bash -n Tools/Tests/FFmpegValidation/run-linux.sh
bash -n Tools/Tests/FFmpegValidation/run-linux-player.sh
TMPDIR="$PWD/Tools/FFmpeg/.build/linux-audit-20261008/tmp" \
  env -u LD_LIBRARY_PATH bash Tools/Tests/FFmpegValidation/run-linux.sh
```

**三份 `bash -n` 均 PASS。直接原 `run-linux.sh` PASS，退出 0，144 checks**：staged avformat 绝对 dlopen、自身 `$ORIGIN` 依赖解析，H.264 1920×1080 / 30 frames，RGBA 0–177，checksum `ae2450db4c195031`，与先前结果一致。已修正按 README 直接运行的复现障碍；没有重跑 encoder 或 Unity Player 长测试，也没有变更先前结果及平台/硬件限制。

证据保存于本次忽略缓存：`shell-lf-provenance.json`、`shell-syntax-lf.txt`、`linux-native-direct-lf.txt`。三个脚本的 LF 内容与 Git index 一致，内容 diff 为空正常。

## 未验证限制

- **未验证**：Linux 物理 GPU 的 VAAPI/Vulkan Video 硬件码流解码、VAAPI/DRM PRIME DMA-BUF 到 Unity Vulkan 的原生互操作及真实硬件编码器。WSL 没有 `/dev/dri` render node；不得把 llvmpipe compute/软件 RGBA smoke 当硬解或物理 GPU sharing 通过。
- **未验证**：Unity Linux Vulkan 显示（本环境启动拒绝软件设备）；Unity Linux IL2CPP、完整主工程的 Editor import/Play Mode、真实 Linux 发行版/旧 glibc/驱动矩阵。隔离 Mono Player 的通过不覆盖这些层。
- **未验证**：性能、长时间播放/压力、网络/容器/全部解码器矩阵。原生录制检查不覆盖 Unity Camera/GPU readback 集成。

## 文件与最终检查

实际操作的受控文件：
- `Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64/libFFmpegUnityBridge.so`
- `Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64/bridge-manifest.json`
- `Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64/libva.copyright`
- `Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64/libva-drm.copyright`
- `Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux/x86_64/libdrm.copyright`
- `Tools/FFmpeg/LINUX-AUDIT-20261008.md`（本报告）。
- 后续仅换行授权：`Tools/FFmpeg/build.sh`、`Tools/Tests/FFmpegValidation/run-linux.sh`、`Tools/Tests/FFmpegValidation/run-linux-player.sh`。

三个 copyright 仅恢复原始 LF 字节，内容与 index 一致；预期 tracked 内容 diff 只有 bridge 二进制、bridge manifest 和本报告。所有新增 helper/构建/Player/媒体/日志在忽略缓存；`run-linux.sh` 原有忽略 `.work/linux-native.txt` 由正常测试入口写入。

证据索引：`baseline.json`、`header-source-evidence.json`、`copyright-provenance.json`、`toolchain-probe.txt`、`wsl-audit.json`、`*.readelf.txt`、`verify-linux-host.txt`、`linux-native.txt`、`linux-bridge-load.txt`、`vulkan-{test-build,lifetime,negotiation,compute}.txt`、`mpeg4-native.txt`、`software-encoder-native.txt`、`unity-packaging.json`、`unity-run-status.txt`、新 Player 的 `glcore-software.txt / glcore-software.log / glcore-software.txt.png` / `vulkan-software.log`。

最终执行 `git diff --check`、`git status --short` 及 Linux 范围最终 diff；没有编辑其他 agent 的平台文件、子模块或生成的根解决方案/工程文件。
