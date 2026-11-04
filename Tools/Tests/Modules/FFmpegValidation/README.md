# FFmpeg 播放器验收

这些测试使用真实 FFmpeg 库、Unity 程序集及项目的 Diagnostics/ZString 源码，不使用模拟解码器或日志桩。运行前先构建 `Tools/FFmpeg`。

## 托管与真实解码

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4"
```

需要 .NET 9 SDK，默认 Unity Editor 路径为 `C:/Program Files/Unity Editors/6000.3.17f1/Editor`；其他安装路径可用 `-p:UnityEditor=...`。此命令在 Windows x64 进程中运行，新增硬件下载检查需要支持 H.264 D3D11VA 的实际 GPU；不带参数只运行不依赖 native 的时钟、码率统计、帧池生命周期/分配、编码选项和 opaque content 输入归一化测试。`<native-directory> --native-content` 是不需要媒体或 GPU 的 Windows 原生协议专用模式。.NET 工程编译真实 Diagnostics/ZString 并引用项目 PolySharp 分析器，不定义 `ENABLE_PROFILER`，因而不调用 Unity 原生 profiler；日志保留在真实 MajDebug 队列，未调用 Unity 初始化或连接未初始化的 Unity logger。

覆盖：真实视频元数据、RGBA 解码、PTS、前后 seek、EOF 延迟帧排空、预取消、有界预载、连续 seek、快速关闭，以及人工 AVFrame 的像素级上下方向、四方向旋转、非方形尺寸、YUV limited/full range、动态像素格式、裁剪与超限拒绝。硬件测试创建独立 D3D11VA 设备，在没有 Unity 纹理互操作回调的情况下解码、下载 RGBA、跳转并检查真实像素，断言 `HardwareDecoded` 与 CPU 像素存储同时成立。

### Android SAF native content 输入回归

[NativeContentChecks.cs](NativeContentChecks.cs) 在默认托管入口反射调用 `FFmpegVideoPlayer` 的 private static `NormalizeSource(string)`，不构造 MonoBehaviour、不调用 Unity 原生 API 或 Android JNI。断言以 `content:` 开头的输入（协议识别大小写不敏感）保持原始字符串，包括 escaped document ID 的 `%2F` / `%3A` 和转义大小写、authority 大小写、query、URI 内的引号及尾部字符；本地路径、`file://`、HTTP/HTTPS 仍保持既有 trim / 包装引号处理和 file URI 转本地路径行为。测试 URI 均为合成字符串，不访问真实文档，也不声称所有合成 URI 都能被 Android provider 接受。

另直接调用 `MajdataPlay.FFmpeg.Interop.AndroidContentInterop.Initialize()`，断言本工程的 `UNITY_EDITOR` 分支立即抛出 `PlatformNotSupportedException`；Android 初始化代码由条件编译排除，此检查不加载 native、不触碰 JNI 或 Unity 原生 API。

```powershell
# 仅托管检查，不加载 FFmpeg native、不调用 Unity/JNI。
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj
# 仅加载 Windows FFmpeg 并检查输入协议，不需要媒体、GPU 或 Unity Player。
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 --native-content
```

所有 Windows native 模式在配置 DLL resolver 后调用 `NativeContentChecks.RunWindows()`：通过生产 `FFmpegVideoDecoder.SupportsInputProtocol` 的 `avio_enum_protocols` input 枚举，交替重复断言 `file` 存在、`content` 不存在，防止枚举游标或上次结果污染后续查询；同时断言默认软件解码器 `Open(content URI)` 在 Windows 提供明确的 `NotSupportedException` / content protocol 诊断，而不是交给未知 native 协议或依赖硬件初始化。专用模式输出中的 `Android JNI/device playback NOT VERIFIED` 仅表示这个 Windows 检查入口不提供设备运行时证据；Windows 通过不能记作 Windows 支持 SAF。

Android 的 `android_content` 构建选项对应 `CONFIG_ANDROID_CONTENT_PROTOCOL`，FFmpeg 运行时的协议条目名是 `content`。`Native/Android/armeabi-v7a` 与 `arm64-v8a` 应分别留存三层证据：`configure.txt` 的 Enabled protocols 中 `android_content`（构建配置）、实际 `libavformat.so` 的 content protocol descriptor / protocol table 条目（ELF 结构静态审查），以及目标进程的 `avio_enum_protocols(input)` 与真实 provider 打开/解码（设备运行时）。实际 ELF descriptor/table 审查比仅看到配置或协议名字符串提供更强的静态证据，但仍不能代替 JNI、授权和设备解码验证。托管/Windows 回归入口不加载 Android ELF，也不重复原生审查；两 ABI 的实际静态审查与设备运行结果另见 [RESULTS.md](RESULTS.md)。

输入实现直接把已授权 `content://` URI 交给 FFmpeg native；不经过 Storage facade、不创建完整文件缓存。provider 可能不能 seek，MP4 探测、seek/回零/loop 不保证可用；原生阻塞 provider 调用若不检查 interrupt，取消/超时只能在它返回后被观察。授权由调用方已有的 SAF 流程提供，播放器不新增权限、不绕过 DRM。软件解码和硬件回退重新打开都必须初始化同一 JNI/应用上下文，不得依赖此前成功的硬件初始化。

APK/JAR StreamingAssets 仍须先提取为本地编码文件；普通 `UnitySmoke` / `run-android-player.ps1` 使用提取后的素材，不提供 SAF URI，因此这些本地媒体 smoke 的通过不能替代 content 路径验收。下面的独立 SAF APK 使用真实 picker 和 native content，不改变普通 StreamingAssets 输入约定。

#### 真机 SAF APK 复现（独立测试应用）

使用 Unity **6000.3.17f1** 及其 Android SDK/NDK/OpenJDK、API 36 wrapper 生成环境和已构建的对应 ABI FFmpeg 原生库；连接已授权 ADB、支持待测 ABI 的 Android 设备，并保持屏幕解锁、测试应用在前台。该隔离场景的测试入口为 `MajdataPlay.FFmpeg.Validation.AndroidContentDeviceSmoke`；普通 `UnitySmoke` 在该场景中跳过运行。以下命令从仓库根目录执行，`-BuildOnly` 只构建，不代表实机播放通过；示例显式使用 `-DisableBurst`，保存结果时也应记录这个构建条件。

隔离工程会把 Java binding 输出到自己的 `Tools/AndroidJavaWrapper/src`，连同生产 `.e.cs` 扩展一起编译独立 csproj，再把 `MajdataPlay.Platform.Android.Runtime.dll` 安装到隔离工程的 `Assets/Plugins/MajdataPlay/Platform/Android`；Unity 通过 DLL 引用这些 wrapper。

```powershell
# ARM64：只构建独立 content APK，不自动运行普通 Android 媒体 smoke。
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -TestAndroidContent -BuildOnly `
  -Platform Android -Backend IL2CPP -Architecture arm64 -DisableBurst `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/android-content

# 将 YOUR_ADB_SERIAL 替换为 adb devices 列出的设备编号；-Media 可选。
./Tools/Tests/Modules/FFmpegValidation/run-android-content.ps1 `
  -Apk Tools/Tests/Modules/FFmpegValidation/.work/android-content/Android-arm64-IL2CPP/VideoSmoke.apk `
  -Architecture arm64 -Serial 'YOUR_ADB_SERIAL' `
  -Media "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4"

# ARMv7：为另一 ABI 单独构建，再 install -r 同一个独立测试包。
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -TestAndroidContent -BuildOnly `
  -Platform Android -Backend IL2CPP -Architecture armv7 -DisableBurst `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/android-content
./Tools/Tests/Modules/FFmpegValidation/run-android-content.ps1 `
  -Apk Tools/Tests/Modules/FFmpegValidation/.work/android-content/Android-armv7-IL2CPP/VideoSmoke.apk `
  -Architecture armv7 -Serial 'YOUR_ADB_SERIAL'
```

也可以省略 runner 的 `-Media`，在 picker 中选择设备上已有、可读取的测试视频。`-Apk` 必须对应刚构建的 content APK，`-Architecture` 接受 `arm64` / `armv7`，`-Serial` 明确指定目标设备；非默认 Unity 安装可为 runner 传 `-Adb <adb.exe路径>`。初次选取加运行默认限时 300 秒，可用 `-TimeoutSeconds 600` 留出人工操作时间。使用足够长、已知可解码的测试视频；seekable fixture 必须长于 1 秒，loop 会依据实际选中视频的元数据 seek 到 EOF 前约 0.6 秒，不等待整段视频播放完毕。不能根据 `-Media` 上传文件推断用户实际选中的文档、时长或帧率，以本轮报告元数据为准。

Windows Android 构建若遇到长路径导致的 Ninja `manifest dirty`，可对这个独立测试工作目录使用已配置好的短路径映射（例如 `-WorkDirectory R:\`），并相应调整 runner 的 `-Apk` 路径。短路径只是构建规避，不是 SAF 运行时证据；不要改用旧 APK 的报告代替重新构建结果。

**数据与授权边界：** 专用 application ID 是 `net.majdata.ffmpegcontent.validation`，Java Activity 是 `net.majdata.ffmpegplayer.validation.AndroidContentDeviceActivity`。runner 只安装/更新这个独立测试包、force-stop 该包并删除该包上一轮的 `ffmpeg-content-smoke.txt`；不覆盖主游戏应用数据。指定 `-Media` 时推送到随机命名的 `Download/MajdataFFmpegContentValidation-<id>/test.mp4`，不使用用户原文件作为覆盖目标；测试素材保留在设备上，脚本不删除选中的文档，也不撤销其 grant。测试不会申请广泛存储权限、复制完整媒体到播放器缓存或绕过 DRM。

手机首次出现真实 `ACTION_OPEN_DOCUMENT` / `video/*` picker 时，请按 runner 输出的目录选取 `test.mp4`。Activity 保存原始 `Uri.toString()`，仅在结果实际授予 read 和 persistable flags 时尝试持久化 read grant；若持久化被拒绝，同轮仍可使用 transient read grant，并报告 `persistedReadGrant=false`。同包名、同签名的 `install -r`、切换 ABI 或重启可复用测试应用自己的 SharedPreferences 选择，但仅当系统仍列出匹配的 persisted read grant；否则重新弹出 picker。取消 picker 明确记为 FAIL，等待期间报告为 WAITING，不能当作通过。复用 grant 只证明权限仍在，不证明文档没有被移动/删除或 provider 仍可读；软件 Preload 仍会验证实际可打开性。`-Media` 不强制清除旧选择或重新弹 picker，有有效旧 grant 时会继续测试原选中的文档；如需换素材，应先显式清除这个独立测试应用的数据再重新选取，不要清除主游戏数据。

**运行内容与证据：**

- 先枚举当前进程实际加载的 `avio_enum_protocols(input)`，必须存在 `content`；报告记录 `processABI`、`processBits=64/32`、`IntPtr.Size`、Android API、Unity 版本、`backend=IL2CPP` / `ENABLE_IL2CPP` 和实际 graphics backend。
- **软件先行**：未经硬件 warm-up，调用生产 `FFmpegVideoPlayer.PreloadAsync(content URI)`，断言实际软件解码、元数据、首帧 Texture 与 `FrameReady`；随后检查真实帧/时间推进、Pause 冻结和原始 opaque URI 保持不变。fixture 不手动初始化 native JNI/context，因此覆盖生产软件 Prepare 的初始化入口。
- **seekable 输入**：严格检查前后 `SeekAsync`、近 EOF 的 `EndReached` 后 loop 回到开始并继续呈现、Stop 的异步回零与继续 seek；Close 后要求 session 释放且不再呈现 stale frame。非 seekable 输入将 seek/rewind/loop 标为 `SKIP_NONSEEKABLE_PROVIDER`，并检查 Stop 会关闭输入；不能把该 SKIP 记作 seek/loop 通过。不能打开或不能解码的 provider 仍是 FAIL，不会借助缓存回退掩盖。
- **硬件偏好 + CPU 纹理**：软件 session 完成后，以相同 URI 设置 `PreferredDecoderType=Hardware`、`PreferNativeTextures=false` 再次 Preload、播放、暂停和关闭，播放后记录实际 `DecoderType` / `DecoderName` / `TransferMode`、`FrameNumber` 与 `IsSeekable`。实际软件 fallback 明确标为 `SKIP_SOFTWARE_FALLBACK`；只有实际硬件解码才记硬件 PASS。这个 CPU RGBA 上传检查不等于 GPU 零复制或 native texture 共享验证，也不保证平台 codec 的底层物理实现。

应用将进度和最终状态写到 `Application.persistentDataPath/ffmpeg-content-smoke.txt`，终态首行以 `PASS:` 或 `FAIL:` 开头；成功后保留进程，供 ADB 采集 PID 日志及下一轮 force-stop。runner 删除旧报告后再启动，并把终态报告和当前进程 logcat 保存为 APK 旁的 `content-arm64-device.txt` / `content-armv7-device.txt` 及其 `.logcat.txt`；它还要求报告中的目标位数、IL2CPP 与 `contentProtocol=True` 匹配。重新运行会覆盖该 ABI 的报告，请先按设备/ABI/时间保存证据及 runner 输出的 APK SHA256。总体 PASS 可包含明确的硬件或 seek/loop SKIP，必须逐项报告，不能把 SKIP 冒充通过。fixture 自身不输出选择 URI、provider 异常 message 或 stack trace；发布完整 logcat 前仍应检查其他生产/native/system 日志，避免暴露文档 URI。

provider/native 操作若阻塞且不响应 interrupt，测试会请求关闭并观察原任务，但并不提供硬抢占保证；runner 超时也不证明 native 已退出。Java 分析、托管脚本编译、IL2CPP/APK 构建和静态协议证据必须与运行时结论分开记录，不能从任一编译成功推断实机播放通过。

**验证边界：Windows 托管/native 层本身不验证 Android SAF/JNI 或 Android Player 运行时；ARM64 / ARMv7 的真机结果另见 [RESULTS.md](RESULTS.md)。** 本 README 只说明能力、复现入口和限制，不维护设备通过计数或固定实测素材。picker 取消/失败、重启后的 grant 复用、各 ABI 的实际 decoder/transfer/metadata，以及 APK 原生库和 Runtime 源码 snapshot 一致性等证据，应在 RESULTS 中按运行分别记录。

这个 CPU 上传 smoke 不验证 GPU native texture / 零复制；不主动撤销 grant，也不保证所有设备、provider 或文档均可解码、seek 或 loop。非 seekable 输入的 seek/loop SKIP、transient grant 的有限生命周期与 provider 阻塞限制必须保留，不能用一个设备/ABI 的通过外推其他能力。

### Windows 解码超载与工作线程同步

先按 [原生桥接说明](../../../FFmpeg/Native/README.md) 重建包含 GPU 完成查询的同 ABI 桥接，将新桥接和七个匹配的 FFmpeg DLL 放入独立测试目录；不要覆盖正在运行的 Editor 已加载的库。Windows D3D11VA 原生纹理路径可运行专用托管检查：

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Tools/Tests/Modules/FFmpegValidation/.work/video-overload/native-x64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4" --decode-sync
```

此模式要求实际 D3D11VA GPU，禁止软件或 CPU 上传回退；检查生产 completion query 的复用、单线程 codec、真实硬件帧及下载像素、seek/preroll、取消、16x 有界后台会话与设备引用归还。它不会初始化 Unity 渲染设备，也不验证游戏 FPS。

Unity 的 `-TestDecodeOverload` 使用正常 Player 渲染循环，关闭 VSync 并限制 60 FPS，依次测量 **1x、2x、3x、16x**。每档先测暂停视频时的同场景基线，再播放相同素材；各阶段预热 0.4 秒，测量最长 3 秒，并按素材长度缩短以避免 EOF。素材必须可 seek、至少 20 秒；`-RequireHardware` 允许 D3D12/Vulkan 回退到 Windows D3D11VA 共享路径，但禁止 CPU 像素上传。

检查播放阶段至少保留暂停基线的 **95% FPS**，更新间隔 p95/p99 分别不得超过基线 2/4 ms，最大间隔不得超过 50 ms 或基线加 5 ms 的较大值。FrameTimingManager 在有数据时检查渲染线程 p99 不超过半个目标帧预算（60 FPS 时为 8.333 ms）或暂停基线加 4 ms 的较大值；缺失的 CPU/GPU 计时明确记录为 `unavailable`，不将零当成通过。播放时钟不得倒退，推进倍速误差不超过 2%；实际呈现 PTS 推进误差不超过 10%，相对播放时钟的墙钟滞后或超前均不得超过 150 ms，呈现间隔不得达到 100 ms。低吞吐时允许丢弃过期视频帧，但不允许通过冻结时钟或改写 PTS 掩盖落后。

日志记录完整指标与实际硬件后端，报告旁的 `.timing.tsv` 保存各阶段 FPS 与相对暂停基线的百分比变化、p95/p99/max 更新间隔、CPU 主/渲染线程及 GPU 计时和各自样本数、播放时钟/呈现 PTS 推进、呈现频率/最大间隔、墙钟滞后/超前、buffering 更新数及 catch-up seek/过期帧丢弃计数。Frame Timing Stats 与诊断反射所需的 session 元数据保留只在隔离工程启用。可追加 `-PlaybackTargetFrameRate 240` 在较高游戏帧率下检查吞吐，报告另存为 `*-240fps.*`；暂停基线须达到请求值的 90%，同样检查播放时至少保留基线 95% FPS。此测试量化视频增加的帧时间变化，不能证明任意场景、驱动及外部系统负载下绝对零抖动。

用户的高吞吐回归样例实际位于 `MaiCharts/FFmpeg Test/H264@1080p/pv.mp4`（H.264、1920×1080、120 FPS、约 79 秒），可对同一已构建 Player 顺序运行三种图形 API：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 `
  -Graphics d3d11 -RequireHardware -TestDecodeOverload -DisableBurst `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/render-isolation `
  -Media "MaiCharts/FFmpeg Test/H264@1080p/pv.mp4"
# 仅在生产源码、测试源码、原生 DLL 与素材均未变化时复用构建：
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 `
  -Graphics d3d12 -RequireHardware -TestDecodeOverload -SkipBuild `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/render-isolation `
  -Media "MaiCharts/FFmpeg Test/H264@1080p/pv.mp4"
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 `
  -Graphics vulkan -RequireHardware -TestDecodeOverload -SkipBuild `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/render-isolation `
  -Media "MaiCharts/FFmpeg Test/H264@1080p/pv.mp4"
```

普通 seek、step、loop 与 GPU 像素烟测可追加 `-RegularPlayer`，使用与性能检查相同的正常渲染循环；该选项本身不启用倍速性能阶段。未加此选项的普通烟测沿用 batchmode。

也可用 4K 素材让 16x 的请求明显超过解码吞吐：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 `
  -Graphics d3d12 -RequireHardware -TestDecodeOverload `
  -NativeDirectory Tools/Tests/Modules/FFmpegValidation/.work/video-overload/native-x64 `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/video-overload/unity `
  -Media Tools/Tests/Modules/FFmpegValidation/.work/video-overload/test-4k30.mp4
```

素材和 GPU 不同会影响测量；检查日志中的实际解码器，D3D11VA 回退不能算作原生 D3D12VA/Vulkan Video 验证。原生 GPU 未就绪帧的提交前复查还需通过对应桥接测试验证。正常 Pixel 检查与这些同步回归共同验证画面和资源生命周期，不能仅以 `FrameReady` 计数断言每个 packet 都更新了像素。

### Windows 高帧率视频与实际倍速

`-TestPlaybackThroughput` 在正常 60 FPS Player 渲染循环中依次测量 1x、2x、3x。需要可 seek、至少 8 秒、GPU 可解码的素材；120 FPS 素材用于覆盖高帧率回归。素材必须在当前机器及负载下有至少 3x 的真实解码余量；应先用不丢帧的解码吞吐测量确认，不能仅凭 GPU 型号推断。每档预热后观测 2 秒，检查播放时钟（允许 2% 偏差）与实际呈现 PTS（允许 10% 偏差）的推进倍速、呈现频率（至少达到预期值的 80%）和最大呈现间隔（不得达到 100ms）。日志同时记录 Unity 更新 FPS、画面滞后、buffering 更新数及实际解码设备；仅有 Unity FPS 正常不再视为视频流畅。

`FrameReady` 的第二个参数是帧序号，不是 PTS；测试读取已呈现帧的时间戳。正常 smoke 的 GPU 像素、seek 与生命周期检查仍会继续执行。此模式在 60 FPS 渲染循环中呈现最新应显示画面，不能据此声称 360 个画面/秒均被提交或显示。

可生成非均匀的本地 H.264 120 FPS 素材后运行（所有生成物保留在忽略的 `.work/`）：

```powershell
ffmpeg -f lavfi -i "testsrc2=size=1280x720:rate=120" -t 24 -an `
  -c:v libx264 -preset ultrafast -crf 23 -g 120 -bf 2 -pix_fmt yuv420p `
  Tools/Tests/Modules/FFmpegValidation/.work/test-720p120.mp4

./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 `
  -Graphics d3d11 -RequireHardware -TestPlaybackThroughput `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/high-frame-rate `
  -Media Tools/Tests/Modules/FFmpegValidation/.work/test-720p120.mp4
```

分别改用 `-Graphics d3d12` / `vulkan` 验证对应渲染路径；RX 580 上实际解码器可能仍是 D3D11VA，不应记作原生 D3D12VA / Vulkan Video 验证。如隔离工程遇到无关 Burst AOT 的 ShaderLibrary 解析失败，可追加 `-DisableBurst` 仅在该次隔离构建的命令行禁用 Burst，不修改主工程或全局设置；验证结果应记录该条件。

### Vulkan 2x 长时呈现回归

针对 `FFmpegVideoPlayer` 在 Vulkan 下以 2x 播放 H.264 1080p 的隔离回归，现有 `run-unity.ps1` 保留普通 smoke 入口，并增加 `-DoubleRateSeconds`，不创建第二套播放器测试。`-DoubleRateSeconds` 的范围为 1–60 秒，默认 3 秒；大于 3 秒时自动移除 `-batchmode`，使用正常 Player 渲染循环。长测不会因为接近 EOF 而静默缩短：指定 20/30 秒时，素材必须分别至少提供约 41/61 秒可用媒体时间。Player 运行超时为 `max(120, DoubleRateSeconds + 90)` 秒，不包含 Unity 构建时间；30 秒回归的运行 timeout 为 120 秒。

用户报告样例的准确路径为 `MaiCharts/FFmpeg Test/H264@1080p/pv.mp4`。RX 580 2048SP（驱动 31.0.21910.5）没有 Vulkan Video 时，默认 Vulkan 路径应重点观察实际日志中的 D3D11VA 解码器、worker-completed RGBA 传输和 Vulkan 呈现；不要把它记为 Vulkan Video 原生解码。扩展 2x 测试在 `-Hardware` 下还会断言实际 `DecoderType=Hardware`，因此软件回退不能把这次硬解回归伪装成通过。

使用新构建的隔离 Player：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 `
  -Graphics vulkan -Hardware -DoubleRateSeconds 30 `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/vulkan-2x-rx580 `
  -Media "MaiCharts/FFmpeg Test/H264@1080p/pv.mp4"
```

如果已用完全匹配的源码、原生 DLL、图形 API 和 fixture 构建过 Player，可跳过构建并只运行 2x 回归：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 `
  -Graphics vulkan -Hardware -DoubleRateSeconds 30 -SkipBuild `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/vulkan-2x-rx580 `
  -Media "MaiCharts/FFmpeg Test/H264@1080p/pv.mp4"
```

长测断言包括：连续墙钟 2x 播放、播放时钟和已呈现 `_lastFrameEnd` 单调推进、100 ms 呈现冻结、150 ms wall-clock lag/lead、有界呈现队列以及高帧率源的过期帧丢弃而非冻结；最后一次重复 seek 还要连续观测约 1 秒，loop 回零后连续观测约 1 秒，close/重开/取消 seek 后禁止 stale frame 继续呈现。报告旁生成 `.double-rate.tsv`，同时保留实际解码器、设备、传输方式、Vulkan API、GPU、驱动、discarded/catch-up 计数和 fallback 原因。扩展回归如果没有该 evidence 文件，会拒绝使用旧版短 smoke 结果冒充长测。

所有隔离工程、Player、日志和 evidence 仍限于忽略的 `.work/`；`-SkipBuild` 只复用相同 `WorkDirectory` 下的已构建 Player，不会重新生成 Player。若 Unity 隔离构建遇到无关 Burst ShaderLibrary 解析问题，可仅在构建命令追加 `-DisableBurst`，并在结果中记录该条件。

### Windows Vulkan Player 启动与窗口变化

`ValidateVulkanPlayerStartup.ps1` 验证已构建的主游戏 Player，而不是空场景 smoke。先把 `MajdataPlay.exe`、`UnityPlayer.dll`、`WinPixEventRuntime.dll`、`MonoBleedingEdge/`、`MajdataPlay_Data/` 及测试用 `settings.json` 副本放入忽略的 `Tools/Tests/Modules/FFmpegValidation/.work/player-crash/`；桥接 DLL 应已更新为待验收构建。Player 会在隔离目录生成运行数据；不要直接使用日常游戏目录。

```powershell
./Tools/Tests/Modules/FFmpegValidation/ValidateVulkanPlayerStartup.ps1 `
  -PlayerDirectory Tools/Tests/Modules/FFmpegValidation/.work/player-crash `
  -Attempts 10 -SecondsPerAttempt 8 -MinimizeRestore -Label acquire-fix
```

脚本限制输入目录位于上述 `.work/` 下，仅控制和终止自己启动的进程；每次窗口操作前核对 HWND 所属 PID。每轮使用 Vulkan 正常渲染循环，并异步请求窗口尺寸变化和两次最小化/恢复。日志、桥接 SHA256、启动存活情况、Title 加载情况、请求次数和设置文件前后哈希保存在 `.work/vulkan-startup/<label-guid>/`，崩溃、请求失败或设置文件变化使脚本返回非零。请求计数不等于 Unity 实际重建交换链的次数，有限次数通过也不能证明偶发崩溃已经消除。

原生 `FFmpegUnityVulkanNegotiation` 的 Windows 测试另行确定性检查 Unity 的实例/设备查询路径：仅把已取得图像的 `VK_SUBOPTIMAL_KHR` 当作成功，保持索引与同步参数、其他返回码、呈现、FFmpeg 原始 loader 及设备销毁链。该兼容处理针对 Unity 6000.3.17f1 在 acquisition 内更新交换链后解引用空图像的已观察路径；不将其他图像表损坏或驱动错误视为修复成功。

### 解码线程过期帧淘汰

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4" --eviction
```

使用可 seek、长于 2 秒且帧率不低于 24 FPS 的真实素材。容量 1/3/8 的会话覆盖有界预载、不取帧时后台自动追赶与定时唤醒、预配置倍速、共享时钟的时间连续性、暂停与关闭时冻结时间、seek 立即设置目标时间、保留未来帧、连续 seek、持有呈现帧时的池容量与关闭资源归还；EOF 检查保留最后一帧直到呈现并支持回零。测试使用真实 FFmpeg 解码，`--eviction` 不要求硬件后端，也不代表 Unity 呈现或实机验证。默认真实解码模式同样运行这些检查。

### 硬件拒绝后的多线程软件解码

```powershell
# 生成 D3D11VA 没有解码 profile 的 H.264 High 4:4:4 Predictive 素材（需要 ffmpeg CLI 含 libx264）。
ffmpeg -f lavfi -i "testsrc2=size=320x240:rate=30" -t 3 -c:v libx264 -pix_fmt yuv444p -g 15 -bf 2 `
  Tools/Tests/Modules/FFmpegValidation/.work/h264-yuv444p.mp4
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/Modules/FFmpegValidation/.work/h264-yuv444p.mp4 --hardware-rejected
```

Windows D3D11 原生纹理路径为了让 worker 侧 GPU 完成标记覆盖每次提交，会把 codec 固定为单线程。GPU 不支持的码流（如 H.264 4:4:4 / 10-bit、缺少对应解码单元的 VP9/AV1/HEVC）在 `get_format` 中拿不到硬件像素格式时，解码器必须以配置的 `ThreadCount` 重新打开软件解码器，而不是继续在单线程 codec 上软解。此模式需要实际 D3D11VA GPU 与包含 GPU 完成查询和 D3D11 staging mapper 的 `FFmpegUnityBridge`；素材必须可 seek、至少 2 秒、恒定帧率且容器时长精确（帧数检查按 `round(duration × fps)` 计算，请使用上面的生成命令），并且 D3D11VA 无法解码。用可硬解的素材运行会在首帧的前置条件检查处明确失败，不能记作通过。

检查内容：纯软件解码器按 `ThreadCount` 报告 frame 线程并完整输出全部帧；强制单线程的同步 codec（分别只带 GPU 同步，以及同时带生产 D3D11 frame mapper；当前进程无法创建独立 GPU staging 时 mapper 变体输出 `SKIP` 并说明原因，不能记作通过）被拒绝后重新打开为 4 线程软件解码器，首帧仍从码流起点开始、帧数与 `duration × fps` 一致、`HardwareFallbackReason` 记录 reopen、GPU 同步对象与 mapper 随旧 codec 释放、seek 仍可用、关闭后设备引用归还；`ThreadCount = 1` 的配置保持原有的同上下文软件回退而不重开；D3D11VA CPU 上传配置本来就保留多线程 codec，不重开；`RequireHardwareDecoding` 与配置了 `FallbackHardwareOptions` 的会话以 `NotSupportedException` 报告拒绝原因，交由播放器选择下一传输方式，不在解码器内重开。默认真实解码模式另断言默认软件解码器报告 `min(处理器数, 8)` 个线程且线程模式不为 none（H.264 为 frame，libdav1d 为 codec）。

此 .NET 9 检查不启动 Unity，也不覆盖 D3D12/Vulkan 渲染器下先经 `FallbackHardwareOptions` 多次重开再到软解的播放器链路，也不覆盖码流中途更换参数集被拒绝的情形（该情形会丢弃旧 codec 中尚未输出的重排帧）；Unity Player 与移动平台的实际表现仍需按下文 Player 流程验证。

### 多线程 RGBA 转换

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4" --conversion-threads
```

CPU 上传路径的 `VideoFrameConverter` 用 `sws_alloc_context` 设置 `SwsContext` 的尺寸、格式、range、`flags` 与 `threads` 后 `sws_init_context`，再以 `sws_frame_start` + `sws_send_slice(0, 高)` + `sws_receive_slice(0, 高)` + `sws_frame_end` 让 libswscale 按 slice 多线程转换到 64 字节对齐的 RGBA staging 帧；任一步失败时释放上下文、本帧回退到原单次 `sws_scale`，下一帧重建上下文，同一尺寸/格式再次失败则持续回退直到尺寸或格式变化。之后的垂直翻转或四分之一旋转复制按 16 行对齐的输出行带（0°/180° 为源行，90°/270° 为源列）分给首次多带转换时才创建的后台线程，每个线程只写完整的输出行；90°/270° 在行带内按 32×32 像素块转置（块内由 Burst 编译的 `VideoPixelKernels` 按 4×4 向量转置），使连续源行写入同一输出行的连续像素（`FFmpeg Video Band N`），调用线程也参与，并等待所有已领取的行带完成后才返回或重抛异常。线程数由 `DecoderOptions.ConversionThreadCount` 决定（包含解码 worker，钳制到 1–16）：默认在逻辑处理器不超过 4 个时为 1，否则为 `clamp(处理器数 - 2, 1, 4)`，与 codec 的 `ThreadCount = min(处理器数, 8)` 共存；libswscale 与复制两阶段先后执行，不同时占用。`DecodedVideoFrame.CopyToSoftware` 仍为单线程。

此模式不需要 GPU，检查内容：
- 行带执行器：20000 次交替发布两个不同 job、2–24 个行带，每个行带只属于当前 job 且恰好执行一次，返回前全部完成；失败的行带在所有已领取行带完成后重抛，之后执行器仍可用；单行带内联运行不建线程，多行带后才启动线程；Dispose 后拒绝新 job。
- 逐字节一致：`yuv420p`、`yuv420p10le`、`nv12`、`p010le`、`yuv444p`，高度 513/1079/1081（每线程至少 64 行且最后一带不均匀）与宽度 1–65、宽度 513/1079/1081 与高度 1/2/3/15/16/17/33/65（旋转后的行带同样不均匀），以及 1920×1081，luma 横向渐变、chroma 纵向渐变，limited/full 两种 range、0/90/180/270 四种旋转；2–8 个线程（含奇数）的输出与 1 线程逐字节相同，并通过初始化后的 `SwsContext.threads` 断言 libswscale 实际使用 N 个线程、输出行不少于 128 时复制至少使用 2 个行带；抽样宽度另与独立的单次 `sws_scale` 结果逐像素比对方向与颜色。
- 分配：用 `GC.GetTotalAllocatedBytes(precise: true)` 统计包括行带线程在内的整个进程，4 线程转换 1280×721 的 `yuv420p`/`nv12`/`p010le` 帧，预热后 256 帧的托管分配为 **0 B**（`--allocations` 与 `--allocations-software` 也运行此项）。
- 解码器：`ConversionThreadCount = 1` 与 `4` 的软件解码器对真实素材前 24 帧输出相同像素，实际 libswscale 线程数分别为 1 与 4；0、负数和 64 被钳制到 1–16。
- 输出 1080p 与 4K 的 `yuv420p`、`nv12` 在 0°/90° 下 1/2/4/8 线程的每帧耗时，仅供参考，不作为断言。

Windows x64 Release 参考结果（16 逻辑处理器，默认 4 线程）：1080p `yuv420p` 每帧约 5.3 ms → 2.4 ms，`nv12` 约 9.8 ms → 4.0 ms；4K `yuv420p` 约 20.7 ms → 11.4 ms，`nv12` 约 39.8 ms → 17.4 ms。90° 旋转在默认 4 线程下：1080p `yuv420p` 约 6.0 ms → 4.2 ms，`nv12` 约 8.1 ms → 5.7 ms；4K `yuv420p` 约 21.3 ms → 17.0 ms，`nv12` 约 30.9 ms → 20.9 ms（与按源行分带、逐像素列写的上一实现交替运行两次的平均值；本机波动约 ±15%）。

此 .NET 9 检查只覆盖 Windows x64 原生库与 CoreCLR；Android、iOS、macOS、Linux 以及 Unity Mono/IL2CPP Player 中的线程行为、分配与耗时未验证，需要按下文 Player 流程另行确认。

### Camera 录制的托管与真实编码验证

不带参数的默认测试还验证 `EncoderOptions` 的尺寸、帧率、最大软件线程数、目标/最大码率和枚举边界，以及构造时的设置快照。`CaptureTimelineChecks` 用 9000 次带抖动的模拟渲染检查 Camera 帧时间戳分配：60 Hz 渲染在 40 种相位下帧开始计时（±1 ms 抖动）不丢帧、渲染时计时（±3 ms）每次最多因首帧重新对齐丢 1 帧；复现旧版逐帧向下取整在帧边界相位的成对丢帧（同一组渲染下报告约 2200 帧丢失）；59.94/60.06 Hz 只跳过或舍弃真实多出/缺少的帧；144 Hz 每帧取最近渲染、30 Hz 保留全部渲染、200 ms 卡顿后重新对齐，且时间戳与时钟偏差不超过 0.75 帧。编译 Camera 组件需主项目已用指定 Unity Editor 完成导入，以提供 `Library/ScriptAssemblies` 中的 `Unity.Collections` 与 `Unity.RenderPipelines.Core.Runtime`；测试只引用这些程序集，不调用 Unity 的 Camera 或 GPU API。

使用启用了编码器和输出封装器的同 ABI FFmpeg 原生库运行编码专用模式，第二个参数是输出目录，无需提供外部视频：

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/Modules/FFmpegValidation/.work/encoding --encode
```

此模式枚举实际可用的视频编码器，使用真实 `mpeg4` 软件编码器生成 64×48 与需 SIMD 行尾补齐的 78×48 MP4，再用项目真实解码器检查红/蓝上下方向、可选垂直翻转、带缺帧间隔的 PTS、完整帧数、延迟排空与稳定 EOF。额外检查设置快照、实际软件线程数不超过上限、实际编码器/Software/VBR 身份、实时压缩码率非零、硬件偏好回退原因、预取消不会生成文件、取消和打开失败后的文件句柄释放，以及创建输出时不会覆盖已有文件。MPEG4 CBR 另编码并解码 60 帧平坦画面，要求实际压缩码率接近目标，验证原生填充包产生了恒定码率；可支持 CBR 的最大码率会收紧至目标值。若库还包含 `libx264`，同时执行 Matroska 容器中 H.264 软件 CBR 的真实编码和排空验证；否则只验证该软件 CBR 请求被明确拒绝，不能将其记为 H.264 CBR 编码通过。多线程检查以 `MaximumSoftwareThreads = 4` 录制 320×192 的 60 帧运动画面：`mpeg4` 必须报告 4 个 codec worker 且 `SoftwareThreadingMode` 为 `slice`（原生上下文 `active_thread_type` 含 `FF_THREAD_SLICE`），`libx264`（若存在）报告 `codec` 模式并保留 4 个 worker；两者的 RGBA→YUV 颜色转换器（Burst 编译的 `CaptureFrameConverter`，按行对分带）都必须报告 4 个线程，输出逐帧解码并检查颜色方向。此 .NET 进程不加载 Burst，转换器运行同一份托管代码；Burst 编译结果与 libswscale 的颜色一致性由 Unity Test Framework 的 `CaptureFrameConverterTests` 以 `Run-UnityTests.ps1 -BurstSynchronousCompilation` 验证。单线程快照另断言 `none` 模式与 1 个转换线程。

完整软件编码 profile 使用 `--encode-software`，要求四种软件编码器全部存在，并实际编码、解码四种格式的 CBR/VBR 平坦与复杂画面，共 16 个文件。包含 H.264 CBR MP4、x265 原生非法参数拒绝、线程上限、`codec` 线程模式与 2 线程颜色转换器、libvpx/libaom 包装器已启用 `row-mt`、设置快照、并发诊断读取、PTS 间隔、排空和文件所有权验证。输出目录需为本次验证准备的新目录：

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/Modules/FFmpegValidation/.work/software-recording-new --encode-software
```

将 `--encode` 换为 `--encode-hardware` 会在上述检查之外，强制要求实际 H.264 硬件编码器完成 VBR 和 CBR 两种 60 帧录制，逐帧解码检查像素方向、PTS 和帧数，并检查实际后端/模式/码率。此模式需要受支持的 GPU、驱动和对应 FFmpeg 编码器；软件回退即失败。未执行或失败时不能宣称真实硬件编码通过。

AMF 录制必须加载带 `MajdataPlay-AMF-RC-v1-<补丁哈希前12位>` 版本标记的原生构建，以确认驱动接受了码率模式、VBV、target/peak bitrate 与 HRD/filler。原生补丁会检查每项 SetProperty，并在 Init 后回读最终值；详见 [AMF 补丁说明](../../../FFmpeg/patches/README.md)。用旧的、仍有 AMF 编码器但不带该标记的 fixture 验证拒绝路径：

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Tools/Tests/Modules/FFmpegValidation/.work/recording-final-native `
  Tools/Tests/Modules/FFmpegValidation/.work/encoding-unchecked-amf --encode-unchecked-amf
```

此专用拒绝模式需要旧 AMF 构建，并且本机没有可用的其他 H.264 编码实现；本轮使用 RX 580 与不包含 libx264 的旧录制库。它检查 CBR/VBR 诊断中的补丁标记、不将 H.264 替换为 MPEG4、不创建新输出，以及已有输出字节不被覆盖。新版库应使用 `--encode-hardware` 验证正常打开与实际编码。

真实编码模式也覆盖后台录制会话：GPU 等待读回与已完成队列共享固定容量，失败读回归还原缓冲区；后完成的早期帧不得被晚期帧超越；Stop 等待未完成读回并排空，取消则在 GPU 回调未到达时完成关闭，迟到回调不会重新启动会话。生成的视频还会检查缺帧间隔和完整封装尾部。

当前正式原生库已包含录制能力。缺失能力模式须使用保留的历史播放专用库；它们没有任何编码器，不能将以下目录替换为当前正式插件目录：

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  <历史播放专用Windows-x64原生库目录> `
  Tools/Tests/Modules/FFmpegValidation/.work/encoding-unavailable --encode-unavailable
```

所有素材保存在输出目录的独立随机子目录，运行不会覆盖已有文件。上述 .NET 9 检查只验证托管配置和 FFmpeg 编码/封装/解码，不能替代 Unity Camera 最终渲染、异步 GPU 回读、场景销毁、实际硬件编码器、Mono/IL2CPP 或移动平台验证。

### 录制音视频合成

`--mux` 验证 `FFmpegMediaMuxer`：先用真实 `mpeg4` 编码 2 秒 64×48@30 的 Matroska 视频（与 capturer 临时文件同一容器），再与 2 秒 48 kHz 立体声正弦 float PCM 合成。

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/Modules/FFmpegValidation/.work/muxing --mux
```

检查项：Matroska 输出恰有一条按包复制的视频流（60 包）和一条音频流；库含 AAC encoder 时音频必须为 `aac`，否则为 `pcm_s16le` 并逐字节核对样本值与声道顺序；音视频起点同为 0、时长均约 2 秒、包按时间交错。MP4 在有 AAC 时必须写出 AAC，无 AAC 时接受 mp4 muxer 存 PCM 或明确要求 AAC 的 `NotSupportedException`。音频延迟（`audioDelay`，录制时用于把采集到的音频放回实际被听到的时间）以 250 ms 分别写出 Matroska 与（有 AAC 时）MP4：两条流起点仍为 0、音频时长延长 250 ms；用 libavcodec 解码（遵循 MP4 edit list）后，相对视频起点的首个可闻样本无延迟时在 0、有延迟时在 250 ms，容差 ±25 ms（覆盖 AAC priming 与 pre-echo）；PCM 时逐字节核对前导静音与随后的原始样本。另验证空 PCM 输出纯视频、尾部不完整帧被丢弃、预取消、缺失视频与不可 seek 输出的拒绝。不带参数的默认测试包含 float→16-bit 的舍入/限幅/NaN 和参数校验（含负值与超过 1 分钟的音频延迟）。此模式不覆盖 Bass DSP 采集、Unity Camera 或游戏内 `FFmpegRecorder` 生命周期。

原生后端回退测试检查 H.264 的 D3D12VA/Vulkan 硬件配置，并注入首选设备获取失败，验证严格 GPU 模式和允许 CPU 上传模式均可尝试下一硬件后端、得到正确的实际设备身份并继续 seek。此测试验证回退链，不代表已在对应原生后端完成视频解码。

### 每帧托管分配与帧所有权

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  "Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4" --allocations
```

`--allocations` 用真实 FFmpeg 库分别验证人工 RGBA/YUV 帧转换、软件解码、D3D11VA 下载并转换为 RGBA，以及 D3D11VA 原生帧。先正向校验 `GC.GetAllocatedBytesForCurrentThread()` 能观测已知数组分配，再对每条路径预热并断言一批帧的托管分配总量为 **0 B**；转换测量 512 帧，解码测量最多 120 帧；多线程转换另用 `GC.GetTotalAllocatedBytes(precise: true)` 统计包括行带线程在内的整个进程（见“多线程 RGBA 转换”）。素材需可 seek、至少含 64 帧且长于 2 秒，以覆盖会话压力检查。没有支持该素材的 D3D11VA GPU 时，可改用 `--allocations-software` 只运行转换和软件解码；该模式也可使用 AV1 素材。

不带参数的默认测试检查预分配帧池的固定容量、同时持有的帧互不别名、重复 Dispose、释放回调抛异常后归还、跨线程归还、元数据重置与 4096 次租用/归还的 0 B 分配。真实解码测试额外检查解码器关闭后尚未归还的 CPU/原生帧仍可释放、归还后池容量完整，以及公开解码器返回的独立帧不会复用旧对象，因此迟到的重复 Dispose 不会影响后续帧。容量 1/8 的实际会话还覆盖持有显示帧时持续取帧、连续 seek、关闭与释放。播放器内部池化帧遵循独占租用约定：Dispose 后不可继续使用旧引用。

测量只覆盖调用线程的稳态托管分配，不包含初始化、seek 调用、错误诊断、native 缓冲区分配或 Unity 渲染线程。此 .NET 9 结果不能替代 Unity Mono/IL2CPP 的 Profiler 与目标平台验证；启用 Profiler 后应继续检查 `ReadFrame`、`PreparePresentationFrame` 和实际呈现路径的 `GC.Alloc`。

相机录制编码线程的稳态分配使用 `--encode-allocations`，输出目录需为本次验证准备的新目录：

```powershell
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/Modules/FFmpegValidation/.work/encode-allocations --encode-allocations
```

该模式用 libx264 以 320×192@60 VBR 写入 Matroska（与 capturer 临时文件同一容器），按帧修改像素并间或跳过时间戳模拟丢帧，预热 120 帧后测量 300 帧：在调用线程上用 `GC.GetAllocatedBytesForCurrentThread()` 断言 `FFmpegVideoEncoder.Encode` 连同封装写包的托管分配为 **0 B**，并要求测量期间确有压缩包写出；再驱动生产 `VideoEncodeSession`（`TryReserve`/`CompleteReadback`，由其编码线程执行 `TakeNext`、`Encode` 与 `Publish`），因编码线程无法从外部测量，改用 `GC.GetTotalAllocatedBytes(precise: true)` 统计整个进程，要求 300 帧合计少于 300 B，以容忍运行时后台的一次性分配，但足以发现每帧分配。此前每个写出的视频包都会拼接 `"Mux encoded video packet I/O"` 诊断字符串（Unity Mono 上为 78 B，CoreCLR 上为 80 B），修复后两项均为 0 B。该结果来自 .NET 9，Unity Mono/IL2CPP 仍需在 Profiler 中确认编码线程的 `GC.Alloc`。

### AV1 软件解码与硬件不可用回退

使用独立 FFmpeg 命令行生成 3 秒的 AV1 测试素材，再用项目的真实原生库运行专用验证；素材和日志留在忽略的 `.work/`：

```powershell
New-Item -ItemType Directory -Path Tools/Tests/Modules/FFmpegValidation/.work/av1 -Force | Out-Null
ffmpeg -hide_banner -y -f lavfi -i 'testsrc2=size=128x96:rate=30:duration=3' `
  -an -c:v libaom-av1 -cpu-used 8 -crf 40 -g 30 -pix_fmt yuv420p `
  Tools/Tests/Modules/FFmpegValidation/.work/av1/test-av1.mp4
dotnet run --project Tools/Tests/Modules/FFmpegValidation/FFmpegValidation.csproj -- `
  Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/x86_64 `
  Tools/Tests/Modules/FFmpegValidation/.work/av1/test-av1.mp4 --av1
```

生成素材的命令行工具需要 `libaom-av1` 编码器；验证加载的项目 DLL 需要 `libdav1d` 或 `libaom-av1` 软件解码器。仅有名为 `av1` 的原生解码器不能提供 CPU 软件解码。也可替换为大于 2 秒、可 seek 且画面非均匀的 AV1 素材。

验证高位深解码时，将生成命令的 `-pix_fmt` 改为 `yuv420p10le`，输出另存为 `test-av1-10bit.mp4`，再对该文件运行相同 `--av1` 命令；8-bit 和 10-bit 均需通过。

`--av1` 跳过要求真实 H.264 硬解的测试，覆盖实际 AV1 软件解码器身份、非均匀 RGBA 像素、PTS、前后及末尾 seek、EOF、取消、有界会话和快速关闭。另注入 D3D11VA 设备获取失败，检查允许回退时重新选择软件解码器并继续解码/seek，严格 GPU 模式则拒绝软件回退。测试检查原生 `av1` 候选仍暴露 D3D11VA/D3D12VA/Vulkan 配置，并检查设备获取回调确实被调用，防止默认解码器改为 `libdav1d` 后丢失硬件候选。

此专用模式不需要支持 AV1 的 GPU；通过不代表真实 AV1 硬件解码或 Unity 纹理显示已验证。Unity 显示可继续用 `run-unity.ps1 -Media <AV1素材>` 验证软件模式，以及 `-Hardware` 验证播放器的实际硬件尝试/软件回退；必须检查报告中的实际解码器和传输方式。

可用独立工作目录避免与其他隔离 Unity 验证占用同一工程；`-WorkDirectory` 接受绝对路径或相对仓库根目录的路径，省略时仍使用原 `.work/`：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics vulkan `
  -Hardware -Media Tools/Tests/Modules/FFmpegValidation/.work/av1/test-av1.mp4 `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/av1/unity
```

保留旧版未含 AV1 软件库的 DLL 时，可将第三个参数换成 `--av1-unavailable`，检查直接软件模式及硬件设备初始化失败后，都在 `Open` 阶段以包含 `libdav1d`/`libaom-av1` 的明确错误拒绝。此模式只验缺失能力时的诊断，不代表 AV1 播放通过；正常 `--av1` 模式始终要求真实解码成功。

## Inspector 码率

`Bitrate (current)` / `CurrentBitRate` 根据最近约 1 秒媒体时间内的视频压缩包字节数估算，单位为 bit/s（界面自动换算 bps/kbps/Mbps）。数据随实际显示帧更新，后台预读不提前改变读数，倍速不乘码率，暂停保留当前值，seek/关闭清零。`Bitrate (average)` / `BitRate` 保留 FFmpeg 报告的视频流平均码率；未知值显示 `Unknown`。

统计不包含音频、容器或网络协议开销。窗口不足 1 秒时使用已有媒体跨度；窗口边缘按数据包持续时间比例分摊字节。缺少 PTS 时使用 DTS，两者均缺少或包时长未知时按帧率估算时间，因此实时值是局部估计。固定容量统计覆盖乱序 PTS、预读到的未来帧、VFR 和 seek；极端输入超过容量时暂报未知，待不完整窗口移出后恢复。测试检查这些计算边界，以及真实软件/硬件帧携带码率、跳转后历史重置与末帧保留。

## CPU Profiler

在开启 Profiler 的 Editor 或 Development Player 中，CPU Timeline 的 `FFmpeg / Decoder` 工作线程包含：

- `FFmpeg.Decoder.Software.SendPacket` / `ReceiveFrame` / `Drain`：软件解码调用。
- `FFmpeg.Decoder.Hardware.SendPacket` / `ReceiveFrame` / `Drain`：硬件解码调用；硬解加 CPU 上传也归入此组。
- `FFmpeg.Decoder.ReadPacket`：解封装和输入读取；`DownloadHardwareFrame`：硬件帧下载。
- `FFmpeg.Decoder.ScaleRGBA` / `CopyRGBA`：颜色转换，以及翻转/旋转和像素拷贝。

解码工作可能发生在送包和取帧两处。硬解标记包含提交及等待的 CPU 耗时，不代表 GPU 异步解码时长；一次调用也不一定产出一帧。`ReadFrame` 保留作为读取到可呈现帧的总耗时。

主线程的 `FFmpeg.Interop.TryPresent` 下可分别查看 `PrepareD3D11` / `PrepareD3D12VA` / `PrepareVulkan` / `PrepareMetal`、`RecordCommands` 和 `SubmitCommands`。`PrepareD3D11` 包含 D3D11 解码经其他图形后端共享的路径。比较稳定播放时的样本，单独看首帧或纹理重建时的 `CreateCopyTarget`，避免将初始化成本算入每帧成本。

## Unity Player

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d12 -SkipBuild
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics vulkan -SkipBuild
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics glcore -SkipBuild
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -Hardware -SkipBuild
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics d3d11
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x86 -Graphics d3d11
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics d3d12 -RequireHardware
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics vulkan -RequireHardware -SkipBuild
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend IL2CPP -Architecture x64 -Graphics glcore -RequireHardware -SkipBuild
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -TestRecovery
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -HardwareCpuUpload
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 -TestDecoderPreference -SkipBuild
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture arm64
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture armv7
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Platform Linux -Backend Mono -Architecture x64 -Graphics glcore
```

隔离项目、构建输出、日志、结果、32×32 纹理读回图均保存在忽略的 `.work/`。脚本不修改主项目场景或 Player Settings。测试包括首帧预载、实际纹理像素、播放、倍速、暂停、seek、步进、停止回零、连续 seek、循环、关闭/取消。图形测试会启动隐藏的 Player，GPU 后端仍需本机驱动支持；不能加 `-nographics`。`-Hardware` 是尝试硬件路径，必须检查报告中的 `TransferMode` 与 fallback，软件回退成功不代表硬件互操作成功。`-RequireHardware` 隐含 `-Hardware` 并增加硬件路径断言，发生软件回退即失败；首次使用该选项需要重新构建包含新断言的测试 Player。

倍速检查将 Unity 临时限制为 60 FPS，测量 0.7 秒内的真实时钟推进（2x，容差 10%）和每次更新的最大画面落后（小于 0.2 秒），并在日志中记录实际更新帧率。可用 120 FPS 素材、默认 3 帧队列验证高视频帧率下的后台淘汰；无需把缓存调大。该检查使用小素材时的通过结果不代表高分辨率解码吞吐已通过。

硬件呈现测试还会在完成像素读回后主动释放输出纹理及存在的 GPU copy target，再步进一帧并检查恢复后的实际像素，以覆盖原生纹理指针缓存失效后的重建。

`-Media` 可替换素材，需使用可 seek、时长大于 2 秒、首秒处有非均匀画面的文件。默认使用项目的 H.264 背景视频。`-SkipBuild` 只用于相同后端、架构和最新源码已完成构建的情况。

Windows D3D12/Vulkan 可添加 `-RequireNativeDecoder`，在严格 GPU 模式之外检查实际 `DecoderDevice` 和 `TransferMode` 确实使用 D3D12VA/Vulkan Video；回退到 D3D11VA 会失败，不能冒充原生解码通过。原生后端的设备限制和本机验证结果见 RESULTS。Windows 的 `-NativeDirectory <包含八个DLL的目录>` 可在隔离工程中测试刚编译的库，同时保留项目的 PluginImporter 设置。

`-TestRecovery` 隐含 `-RequireHardware`，通过反射注入托管层的 GPU 失败通知，不损坏驱动资源。它检查纹理清空回调中的 Pause、SeekAsync、Close，以及终止错误后的重新 Play；报告保存为独立 `*-recovery.*`。日志中的 `Expected recovery test terminal failure` 是该测试主动触发的预期错误，以最终断言报告判断结果。

`-HardwareCpuUpload` 设置硬件解码偏好和 `PreferNativeTextures=false`，在完整控制/像素测试中强制检查实际 `DecoderType=Hardware` 且传输方式为 `Hardware decode + CPU RGBA upload`。`-TestDecoderPreference` 通过 `PreferredDecoderType` 选择软件或硬件解码，检查修改偏好不会改变已打开会话的实际解码器身份、重新打开后的选型，以及原生纹理失败后保留硬件解码、转 CPU 上传继续播放和跳转。后者通过 `RecoverHardwarePlayback(Exception)` 注入真实恢复流程的失败通知。报告分别为 `*-hardware-cpu.*` 和 `*-decoder-preference.*`；这两个选项不能与禁止 CPU 上传的 `-RequireHardware` 同时使用。

隔离 Unity 工程会复制项目的 Diagnostics、完整 ZString、Unsafe 6.1.2 程序集与 PolySharp Roslyn 分析器，保留 `.asmdef` GUID 和 `csc.rsp`，并引入 ZString 引用的 UGUI/TextMeshPro 包。实际 MajDebug 日志写入报告旁的 `.diagnostics.log`，测试检查其中有解码器、设备和传输模式记录；Android runner 也会拉回该日志。首次新增这些依赖或修改测试源码后必须重新构建，不能使用旧 Player 的 `-SkipBuild` 结果。

Android 分支构建 APK，并检查七个 FFmpeg `.so` 和 GPU 桥接的存在、目录、ELF 位数/机器架构。随后连接 API 26+ Vulkan 真机运行：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-android-player.ps1 -Apk ./Tools/Tests/Modules/FFmpegValidation/.work/Android-arm64-IL2CPP/VideoSmoke.apk -Serial <adb设备编号> -Adb <adb.exe路径>
```

测试 APK 自动提取编码后的 StreamingAssets 文件，先开启严格硬件模式，播放至少 300 帧并连续 seek 10 次；随后关闭严格模式，验证软件/硬件偏好、MediaCodec ByteBuffer 的 CPU 上传、共享失败恢复与日志，最后重新打开原生 GPU 路径。素材应长于 12 秒。测试期间请保持应用未被冻结、屏幕解锁且在前台。`-SkipInstall` 可直接复用已安装的同一 APK。脚本仅安装/更新专用包 `net.majdata.ffmpegplayer.validation`，结果保存在 APK 旁的 `vulkan-hardware-device.txt`。测试中的 `ReadPixels` 仅验证输出；播放器的 GPU 共享路径不调用它。

Android AV1 软件验证可用同一 APK 先后运行 8-bit 和 10-bit 素材，无需为换素材重建；每种 ABI 仍需分别构建/安装。以下可选入口通过 Intent extras 关闭硬件和长时间压力模式，复用普通播放/暂停/seek/循环/纹理像素检查，并额外断言 `av1`、`libdav1d`、软件解码与 RGBA 上传。默认硬件验证流程保持不变。

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture arm64 -Graphics vulkan
./Tools/Tests/Modules/FFmpegValidation/run-android-player.ps1 -Apk Tools/Tests/Modules/FFmpegValidation/.work/Android-arm64-IL2CPP/VideoSmoke.apk `
  -Av1Software -Media Tools/Tests/Modules/FFmpegValidation/.work/av1/test-av1.mp4
# 保存上一轮证据后复用同一安装：仅更换测试应用自身 files 目录里的素材。
./Tools/Tests/Modules/FFmpegValidation/run-android-player.ps1 -Apk Tools/Tests/Modules/FFmpegValidation/.work/Android-arm64-IL2CPP/VideoSmoke.apk `
  -SkipInstall -Av1Software -Media Tools/Tests/Modules/FFmpegValidation/.work/av1/test-av1-10bit.mp4
```

将 `arm64` 改为 `armv7` 可验证另一个 ABI。报告、真实 Diagnostics 日志及纹理读回图保存在 APK 旁的 `av1-software-device.txt`、`.diagnostics.log`、`.png`；下一次运行会覆盖，需按位深另存证据。报告记录实际图形 API，软件模式允许 Vulkan 或 OpenGLES3，不能把 OpenGLES3 成功记为 Vulkan 验证。原生 `Av1SoftwareSmoke.c` 检查实际像素格式位深，此 Unity 检查额外覆盖 IL2CPP 绑定、生命周期与真实纹理内容；两层结果应分别记录。

Linux 分支从 Windows Editor 构建独立 Linux x64 Mono Player，需要安装 Linux Build Support。随后在 Linux 桌面或 WSLg 中运行：

```bash
bash Tools/Tests/Modules/FFmpegValidation/run-linux-player.sh glcore
bash Tools/Tests/Modules/FFmpegValidation/run-linux-player.sh vulkan
bash Tools/Tests/Modules/FFmpegValidation/run-linux-player.sh vulkan /absolute/path/to/Linux-Player --hardware
```

脚本清除 `LD_LIBRARY_PATH`，保留实际 GPU 渲染和像素检查，不能使用 `-nographics`。第二个参数指定 Player 目录，第三个参数 `--hardware` 强制 VAAPI/DMA-BUF 路径。需要对应 GPU 的 VAAPI 驱动和 DRM render node。WSLg 软件光栅器上的通过记录不能替代 Linux 物理 GPU 验证；当前本机 Unity 拒绝 llvmpipe Vulkan 设备，具体限制保留在 RESULTS 中。

Win32 IL2CPP 使用 Debug C++ 配置，原因是项目已有 FFmpeg.AutoGen 验证中记录的本机 Unity 6000.3.17f1 / MSVC 14.51 Release 引擎初始化崩溃；Win64 IL2CPP 使用 Release。所有 Player 都开启 High managed stripping。

本机执行结果另见 [RESULTS.md](RESULTS.md)。未执行的目标必须保留“未验证”，不能从托管编译或原生库构建成功推断设备播放成功。

## 原生库加载与真实解码

### 跨平台 AV1 8-bit / 10-bit 软件解码

[Av1SoftwareSmoke.c](Av1SoftwareSmoke.c) 是不依赖 Unity 或 GPU 的 C11 验证程序，可用各目标编译器链接固定 FFmpeg 9.0.1 的头文件与库。它一次接收两个素材，依次要求实际解码为 8-bit 和 10-bit；可复用上方生成的 `test-av1.mp4`、`test-av1-10bit.mp4`。素材需可 seek、长于 2 秒且每帧画面非均匀。

```bash
# Linux 示例：prefix 必须来自当前 Tools/FFmpeg 构建，不能使用系统 FFmpeg。
ffmpeg_prefix=/absolute/path/to/pinned/ffmpeg/prefix
cc -std=c11 -Wall -Wextra -Werror \
  -I"$ffmpeg_prefix/include" Tools/Tests/Modules/FFmpegValidation/Av1SoftwareSmoke.c \
  -L"$ffmpeg_prefix/lib" -Wl,-rpath,"$ffmpeg_prefix/lib" \
  -lavformat -lavcodec -lswscale -lavutil -lm -o /tmp/av1-software-smoke
/tmp/av1-software-smoke \
  Tools/Tests/Modules/FFmpegValidation/.work/av1/test-av1.mp4 \
  Tools/Tests/Modules/FFmpegValidation/.work/av1/test-av1-10bit.mp4
```

程序显式打开 `libdav1d`，检查编译头文件与加载库的版本一致、真实 AV1 帧的像素格式组件位深、无硬件 context、RGBA 像素变化和单调 PTS。它读完整视频并排空延迟帧、确认 EOF 稳定，向前 seek 到视频中点，再回零解码，要求完整帧数、PTS 范围和 RGBA 校验和重现。每个位深独立打印 `PASS`、帧数、实际 seek 时间、像素范围、校验和及检查数；任一素材失败退出码为 1，参数错误为 2。

Windows 使用对应 x86/x64 MinGW 编译器和 import libraries，运行时选择同架构 DLL 目录；macOS 使用对应架构 clang 和 dylib。iOS Simulator 可将程序链接到对应 simulator 的静态库后通过 `simctl spawn` 运行，`libavcodec.a` 须为 stage 后已合并 dav1d 的归档，并附加 FFmpeg 所需的 Apple frameworks/system libraries。iOS 设备需要签名的可运行测试载体，不能把 simulator 成功记作设备成功。

Android 使用对应 ARMv7/ARM64 NDK 编译器，保持与原生库相同 API/ABI；将测试程序、两个素材及同架构 `.so` 推到独立 `/data/local/tmp/` 测试目录，通过该目录的 `LD_LIBRARY_PATH` 运行，无需安装或替换正式应用。该检查验证原生库的软件解码与像素转换，不验证 Unity 生命周期、托管 ABI、纹理显示或硬件解码；动态库搜索路径的独立验证仍使用下面的 Linux 测试。

### 跨平台原生音频编解码检查

`AudioCodecSmoke.c` 使用实际交付库执行 35 组 48 kHz 双声道编码→解码往返：AAC、Opus（分别使用内置与 libopus decoder）、MP3（定点与浮点 decoder），以及 30 种 PCM。输入左右声道分别为 440 Hz / 880 Hz；检查采样率、声道、样本数量、有限值、波形频率/幅度/能量、声道串扰、编码器与解码器排空及稳定 EOF。另将 AAC/ADTS、Opus/Ogg、MP3、PCM/WAV 实际封装后重新解封装，检查音轨身份与数据包。VIDC 的上游压扩表产生失真，因此只检查该旧式入口注册，不能将其记为波形验证通过。

输出目录必须已存在，且没有同名测试文件；程序拒绝覆盖。原生库与头文件必须来自本次固定构建，不使用系统 FFmpeg。

```bash
cc -std=c11 -Wall -Wextra -Werror -I"$ffmpeg_prefix/include" \
  Tools/Tests/Modules/FFmpegValidation/AudioCodecSmoke.c \
  -L"$ffmpeg_prefix/lib" -Wl,-rpath,"$ffmpeg_prefix/lib" \
  -lavformat -lavcodec -lswresample -lavutil -lm -o /tmp/audio-codec-smoke
mkdir -p /tmp/new-audio-codec-output
/tmp/audio-codec-smoke /tmp/new-audio-codec-output
```

Linux 交付目录使用版本化库名：`-l:libavformat.so.63 -l:libavcodec.so.63 -l:libswresample.so.7 -l:libavutil.so.61`。Windows 使用对应架构的 MinGW 和 import libraries，将测试程序放在本次 staged DLL 同目录；macOS 将程序放到 staged dylib 同目录，以匹配 `@loader_path`。Android 使用相同 ABI 的 NDK 编译并在设备上执行。iOS device/simulator 链接 stage 后已合并外部 codec 的静态归档以及 Apple 系统框架；Simulator 签名后由 `simctl spawn` 运行。编译外架构测试程序不等于已执行；本测试不验证扬声器、麦克风、Unity 音频或实时录制吞吐。结果见 `Tools/FFmpeg/AUDIO-RESULTS.md`。

### 跨平台原生录制检查

[EncoderNativeSmoke.c](EncoderNativeSmoke.c) 是不依赖 Unity 的 C11 录制检查。使用同架构的固定 FFmpeg 头文件和库编译，输出目录需提前创建且不含已有同名录制文件：

```bash
cc -std=c11 -Wall -Wextra -Werror -I"$ffmpeg_prefix/include" \
  Tools/Tests/Modules/FFmpegValidation/EncoderNativeSmoke.c \
  -L"$ffmpeg_prefix/lib" -Wl,-rpath,"$ffmpeg_prefix/lib" \
  -lavformat -lavcodec -lswscale -lavutil -lm -o /tmp/encoder-native-smoke
mkdir -p /tmp/encoder-recording-output
/tmp/encoder-native-smoke /tmp/encoder-recording-output
# 对支持 H.264 VideoToolbox 的 macOS 设备单独检查硬件实现：
/tmp/encoder-native-smoke /tmp/encoder-recording-output h264_videotoolbox
```

默认使用 `mpeg4`，CBR/VBR 各录制与解码 60 帧，检查实际 encoder、软件线程上限、target/min/max/VBV、RGBA 上下方向、保留时间间隔的 PTS、帧数、排空、EOF 和独占创建后的输出字节保留。MPEG4 CBR 会额外要求平坦画面最后一秒的填充码率接近目标。可选 `h264_videotoolbox` 仅用于 macOS，明确禁止软件回退，检查 constant-bit-rate 模式选项并打印实际码率；固定 iPhoneOS 包装器没有应用相同的 RequireHardware 标记，因此该可选硬件测试在 iOS 明确拒绝，默认 MPEG4 检查不受影响。此硬件测试保留最大码率检查，但没有验证恒定目标填充。初次 Apple 实测的 CBR payload 为 11,072 bit/s，目标为 200,000 bit/s，未通过最初的目标 ±20% 填充断言；接受配置与可解码不等于证明目标填充成立。具体设备、NV12 复测与限制由 Apple 验证结果记录。

Linux 交付目录只保留版本化 `.so`，直接链接交付目录时使用 `-l:libavformat.so.63 -l:libavcodec.so.63 -l:libswscale.so.10 -l:libavutil.so.61`，或用原始 prefix 的无版本链接并指向交付库运行。Windows 需要同架构 import libraries 和 DLL；Android 使用同 ABI NDK 编译器，把程序、同架构 `.so` 推到独立 `/data/local/tmp/` 目录并设置该目录的 `LD_LIBRARY_PATH`；iOS Simulator 链接静态库时还需 Apple frameworks。该检查不代表 Unity Camera、GPU 读回、托管绑定或其他硬件编码器已验证。

[NativeSoftwareEncoderSmoke.c](NativeSoftwareEncoderSmoke.c) 用相同编译参数验证完整软件编码 profile；它包含同目录的 `EncoderNativeSmoke.c`，只需编译前者。必须提供全新的现有输出目录。检查 `libx264`、`libx265`、`libaom-av1`、`libvpx-vp9`，每种格式分别生成 CBR/VBR 的平坦和复杂画面，共 16 个文件；逐一验证实际软件编码器、线程上限、像素方向、PTS 间隔、帧数、排空、EOF 和禁止覆盖。H.264 CBR 直接写 MP4 并检查 filler；x265 的未知参数和非法 `strict-cbr` 必须在原生打开时返回 `EINVAL`。测试打印复杂画面的码率，不把 VP9/AV1 的单遍 VBR 预算或低复杂度画面当成瞬时硬上限证明。

```bash
cc -std=c11 -Wall -Wextra -Werror -I"$ffmpeg_prefix/include" \
  Tools/Tests/Modules/FFmpegValidation/NativeSoftwareEncoderSmoke.c \
  -L"$ffmpeg_prefix/lib" -Wl,-rpath,"$ffmpeg_prefix/lib" \
  -lavformat -lavcodec -lswscale -lavutil -lm -o /tmp/software-encoder-smoke
mkdir -p /tmp/software-recording-output
/tmp/software-encoder-smoke /tmp/software-recording-output
```

[AndroidNativeSmoke.c](AndroidNativeSmoke.c) 单独检查真实 Android 设备上的七个 FFmpeg 库与 ABI 4 桥接加载、固定版本及补丁标记、MPEG4/muxer/libdav1d/MediaCodec/Vulkan Video 构建入口，以及 AImage/AHardwareBuffer 动态符号和物理 GPU 的 AHB/SYNC_FD/FOREIGN/YCbCr 前置能力。使用对应 NDK 编译器的 `-std=gnu11 -Wall -Wextra -Werror`、固定 FFmpeg/Vulkan include 目录和 `-ldl` 编译，刻意不链接 FFmpeg；运行参数为交付库的绝对目录，并将同目录加入 `LD_LIBRARY_PATH`。该程序没有 Java VM 或 Unity 图形设备，不能证明实际 MediaCodec/Vulkan Video 解码或 GPU 图像导入已经通过。

### Linux 加载路径检查

```bash
bash Tools/Tests/Modules/FFmpegValidation/run-linux.sh
```

需要 Linux x64（也可 WSL）、C 编译器、`readelf` 和 `ldd`。可依次传入绝对库目录和视频路径。测试程序仅链接 `libdl`，不在链接阶段依赖 FFmpeg；首先用绝对路径 `dlopen libavformat.so.63`，由产物自身的 `$ORIGIN` RUNPATH 解析 avcodec/avutil，然后加载其余全部七库。运行时明确移除 `LD_LIBRARY_PATH`，并检查实际加载的 avcodec 来源，避免系统安装或测试环境掩盖缺失依赖。还检查 Linux QSV 的 H.264/HEVC/AV1/VP9 编码器注册、当前码率控制补丁标记、VAAPI/libdrm 子设备配置，以及全部九个 QSV 解码器保持禁用；随后实际解码媒体并转换 RGBA。这些检查不创建 Intel 编码会话，不代表 QSV 硬件 CBR/VBR 编码已验证。

2026-10-03 本机 WSL 结果：**PASS，144 checks**，H.264 1920×1080 实际解码 30 帧并缩放到 RGBA32，像素亮度范围 0–177，校验和 `ae2450db4c195031`。七个 staged ELF 库均检查 `$ORIGIN`。证据保存到忽略的 `.work/linux-native.txt`；程序源为 [LinuxNativeSmoke.c](LinuxNativeSmoke.c)。这项结果证明 Linux 库的加载、解封装、解码和像素转换，不代表 Linux Unity 图形显示已经实机验证。

2026-10-31 补齐 QSV 编码交付后复验 **161 checks PASS**，30 帧解码的像素校验和保持一致；四个 QSV 编码器注册及码率检查标记通过，全部 QSV 解码器仍禁用。另通过沿用桥接 ABI 4 加载、四种软件视频编码器 CBR/VBR、MPEG4 和 35 组音频往返。命令、计数与 QSV 无 Intel 实机的限制见 [同日 Linux 构建记录](../../../FFmpeg/BUILD-RESULTS.md)。

## Camera 录制 Player

当前正式原生插件已包含录制 encoder/muxer，以下命令直接使用这些插件。测试使用独立的忽略目录，不修改主工程场景、Player Settings；历史播放专用库仅用于缺少能力诊断：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -TestCameraCapture `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/camera-builtin
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -CaptureUrp `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/camera-urp
# 验证用户报告的 H.264 软件 CBR + MP4 组合：
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Backend Mono -Architecture x64 -Graphics d3d11 `
  -TestCameraCapture -CaptureFormat H264 -CaptureRateControl CBR `
  -WorkDirectory Tools/Tests/Modules/FFmpegValidation/.work/camera-h264-cbr
```

`NativeDirectory` 是显式可选覆盖；已有正式库具备录制配置时省略。覆盖目录需包含七个 FFmpeg DLL；没有提供 `FFmpegUnityBridge.dll` 时保留项目的同 ABI 桥接。`-CaptureHardware` 额外要求真实 H.264 硬件 CBR，不能用软件回退通过。`-BuildOnly` 只构建，不声称 GPU/编码运行验证通过；`-SkipBuild` 必须使用与当前 fixture/pipeline 相匹配的已构建 Player。
软件 Camera 测试可以用 `-CaptureFormat MPEG4|H264|HEVC|AV1|VP9` 和 `-CaptureRateControl CBR|VBR` 指定格式和模式；默认仍为 MPEG4/VBR，硬件测试固定 H.264 CBR。

`-TestCaptureThroughput` 只运行丢帧测量（加 `-CaptureHardware` 时首选硬件 H.264，否则 libx264）：按游戏场景配置以 1080×1920@60、H.264 VBR 8/16 Mbps 各录 12 秒，分别使用 3 / 6 个槽位、60 Hz 节拍与不限帧率，每帧渲染前随机执行 0–6 ms 脚本负载以模拟游戏帧内抖动。结果写入报告旁的 `.throughput.txt`：渲染/提交帧数、`SourceGapFrames`、`BufferOverflowFrames`、槽位峰值，以及旧版逐帧向下取整时间戳在同一组渲染时刻上会报告的丢帧和被丢弃的重复渲染。断言 6 个槽位时缓冲区溢出不超过提交帧数的 2%。工作目录路径需较短（例如 `$env:LOCALAPPDATA\Temp\fvc`）：隔离工程中生成的 Android wrapper 路径在 OneDrive 等长目录下会超过 Windows 260 字符限制，导致 PowerShell 无法清理。

Camera fixture 通过真实离屏 Camera.Render / URP Standard RenderRequest 驱动相机，避免 Windows 隐藏窗口对显示渲染的抑制。覆盖颜色上下方向、Caller-owned RenderTexture 保留、实时编码身份/模式/码率、设置快照、停止尾部排空、重复录制、禁用后释放及预取消；还检查旧启动异常不会停止新会话，以及取消后未完成的 GPU 回读会阻止过早重启。输出红色上半、蓝色下半的 MP4，再逐帧真实解码确认；URP 额外使用 Overlay Camera 绘制绿色标记，并检查最终录制包含该标记。该测试不等于主工程显示 Camera、UI、自定义 SRP、所有图形 API 或其他目标平台全部通过。

暂停回归在继续真实 Camera 渲染时检查采集停止、已有工作排空、录制时钟/丢帧冻结、同一文件和会话恢复、暂停时长不进入解码 PTS；还覆盖重复暂停/恢复、暂停后停止与取消、取消不能立即恢复、暂停源 Camera 销毁，以及 URP 暂停期间显示输出切换为显式 RenderTexture。帧率差异回归关闭相机自动渲染，以显式渲染控制源帧率，分别测试采集 120 FPS/渲染约 15 FPS 与采集 5 FPS/渲染约 30 FPS；检查没有重复补帧、较低录制帧率限制采样、PTS 保留实际时长。`.timing.txt` 保存实际渲染数、解码帧数、录制时钟、首末 PTS 和最大 PTS 间隔，不能把配置时间基准当作实际视频帧率。

## Camera 录制 Inspector

先用上一节 Built-in 命令准备隔离工程，再运行真实 Editor GUI 验证：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-camera-editor.ps1
```

脚本默认使用 Unity 6000.3.17f1，复用 `.work/camera-builtin/Project-x64-native-video`；`-ProjectDirectory` 只能指定本验证目录 `.work` 内的隔离工程。它使用普通 Editor 事件循环，在 `EditorWindow.OnGUI` 的真实 Layout/Repaint 中绘制 Unity 自动选择的生产 `FFmpegCameraCapturerEditor`，执行录制、Pause/Resume handler、FPS 采样、停止与多选检查。开始/停止调用 runtime API，文件选择窗口和鼠标按钮点击未自动化。窗口关闭、域重载和异步观察器的生命周期也需结合源码审查；这些检查不替代主工程场景和手动 Inspector 交互。

报告与完整 Editor 日志保存在隔离工程同级的 `inspector/`，所有输出仍在忽略目录。脚本不会启动或修改主 Unity 工程。

## Android MediaCodec 原生硬件编码

先按 `Tools/FFmpeg/README.md` 重建 `android-arm64,android-armv7`。在 Windows 上使用固定 Unity 6000.3.17f1 的 Android NDK、SDK 和 OpenJDK，以及已授权的 ADB 实机：

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-android-encoders.ps1 -Architecture arm64
./Tools/Tests/Modules/FFmpegValidation/run-android-encoders.ps1 -Architecture armv7
# 多台设备时添加 -Serial；其他编辑器安装路径可用 -UnityEditor 指定。
```

脚本以 `-Wall -Wextra -Werror` 编译 `AndroidEncoderSmoke.c`，复用 `EncoderNativeSmoke.c` 的真实像素、时间戳、封装与资源清理断言；编译 Java 硬件选择器为 dex，通过 `app_process64` / `app_process32` 在设备 VM 中运行，不安装 APK。所有库均从指定架构的正式 Unity 插件目录复制到唯一的 `/data/local/tmp/majdata-encoder-*` 路径；输出和完整日志拉取到忽略的 `.work/android-encoders/`，超时由设备端 `timeout` 终止本次进程。

测试要求 Android API 29+，以 `MediaCodecInfo.isHardwareAccelerated()`、`isSoftwareOnly()` 和能力查询确认硬件组件，再通过 FFmpeg `codec_name` 显式打开该组件，不允许按 MIME 选到软件实现。该要求只属于设备验证入口；交付原生库仍以 API 23 为最低版本。每个 ABI 必须注册 H.264、HEVC、VP8、VP9、MPEG4、AV1 六个 MediaCodec 包装器；H.264 硬件 VBR 的四种 JNI/NDK、平坦/复杂画面组合必须全部成功，CBR 及其余格式按设备能力报告 SKIP，已宣称支持的组合运行失败会使整个验证失败。

每个可用格式组合覆盖 JNI / NDK、请求 CBR / VBR、平坦 / 复杂画面：320×192、30 fps、60 帧，时间轴中保留一个缺帧间隔；验证实际压缩包、MP4 或 WebM 索引、相同格式的软件解码、全部 60 帧的像素/方向/PTS、编码及解码 EOF 排空、重复 EOF，以及排他创建拒绝覆盖原输出。

MediaCodec 模式与目标码率为请求值。实测码率会记录在日志中，但这些检查不证明驱动回读、最大码率/VBV 限制或恒定填充。Surface 输入、Unity Camera/渲染集成、API 23–28 和其他设备需另行验证。托管录制器按所请求的 CBR/VBR 模式选择经 `MediaCodecList` 确认的硬件组件（最大码率不受保证）；选择逻辑由 `Assets/Tests` 的 `MediaCodecEncoderSelectorTests` 在 Editor 中覆盖，Unity Player 内的实际 MediaCodec 录制通过下节的设备回归单独验证。不支持所请求模式的设备仍回退同格式软件编码。实机结果见 [RESULTS.md](RESULTS.md)。

### Android 托管编码器 CBR/VBR 回归

下面的隔离 Unity Player 直接运行生产 `FFmpegVideoEncoder`，要求实际选择 `h264_mediacodec`、无软件回退，并检查 FFmpeg 私有 `bitrate_mode` 值为 CBR=2 / VBR=1。覆盖 320×192@30 和问题日志中的 1016×1524@120；每组 60 帧，调用 `Complete` 排空并以生产软件解码器逐帧检查尺寸、颜色、PTS 缺帧间隔和稳定 EOF。CBR 设备必须声明支持该模式；该专用回归不会把软件回退视为通过。

```powershell
./Tools/Tests/Modules/FFmpegValidation/run-unity.ps1 -Platform Android -Backend IL2CPP -Architecture arm64 -Graphics vulkan -TestAndroidEncoder -WorkDirectory "$env:LOCALAPPDATA/MajdataPlay.EncoderValidation" -BuildOnly
./Tools/Tests/Modules/FFmpegValidation/run-android-encoder-player.ps1 -Apk "$env:LOCALAPPDATA/MajdataPlay.EncoderValidation/Android-arm64-IL2CPP/VideoSmoke.apk"
```

运行前须解锁设备并保持屏幕亮起，否则 Unity 可能无法进入测试脚本。Player 使用独立包名 `net.majdata.ffmpegencoder.validation`。ADB 脚本保存设备报告、Diagnostics 日志和实际 MP4 到 APK 旁的 `encoder-device-*`。这是生产托管编码/转换/封装与 IL2CPP 的实机验证，输入为合成 CPU RGBA；Camera 渲染、GPU 回读、音频混流和持续 120 fps 吞吐需单独验证。

设备只支持 HEVC/CBR 时，可使用 `-Format HEVC -StandardSizeOnly` 验证硬件 CBR。对于已知 H.264 不支持 CBR 的设备，`-Format H264 -StandardSizeOnly -ExpectCbrSoftwareFallback` 要求 CBR 明确以“bitrate mode unsupported”为原因回退至 libx264，VBR 仍必须使用硬件；避免把未知原因回退计为通过。
