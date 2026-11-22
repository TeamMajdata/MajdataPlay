# 显卡崩溃与 FFmpegVideoPlayer 续查

日期：2026-11-21 至 2026-11-22。源码快照：`dev`，`55797433e`。主仓库运行代码及原 Build 保持不变；诊断入口和设置变化仅用于 `.work/` 隔离副本。

后续已完成Graphics Jobs、原生pacing、Native RenderPass及纹理资源分离对照，最新结论见 [Vulkan渲染路径续查](<VULKAN-RENDERING-RESULTS.md>)：三项开关各自关闭仍有故障；“更新资源是否同时用于RawImage绘制”取得匹配144Hz/配置的分离与恢复绑定证据，但尚未定位具体GPU同步责任点。本报告保留为前期FFmpeg排除证据。

## 当前判断

本轮已获得较强的排除证据：在保留实际游戏场景、用合成纹理替代视频、跳过所有 FFmpegVideoPlayer 视频准备并移除副本图形桥接后，仍出现同类 NVIDIA 驱动153事件，随后窗口响应检查失败。日志无任何媒体解码会话，模块快照无 avcodec/avformat/avutil/swscale/FFmpegUnityBridge，且已验证替代入口确实执行和持续上传。

因此，FFmpeg媒体解码、D3D11VA硬解、D3D11/Vulkan共享及FFmpegUnityBridge并非这类故障发生的必要条件。不应继续把主嫌集中在播放器解码器或keyed mutex链；优先检查完整Vulkan渲染场景、Unity/驱动及剩余插件交互。

这不等于证明整个托管播放器无bug或每次同类症状必然同一根因：托管组件/生命周期仍存在，合成内容、CPU负载、纹理类型与Canvas上传相位也改变。尚未定位具体GPU致错命令、驱动/Unity内部责任点或WDDM复位机制。当前源码审查没有找到与本轮故障建立因果关系的帧所有权或正常共享链缺陷。

独立纯Unity六组有限时长测试均未复现，支持“完整游戏渲染结构是需要进一步保留的变量”，不能把阴性结果当作任何层已被绝对排除。OBS禁用后三局及恢复后三局均正常，未建立其修复或根因结论。

## 已读取的上一会话证据

- `E:/_Game/majp-test/vkdiag/TDR-investigation.md`：包含原始实验矩阵和 dump 摘要。
- `E:/_Game/majp-test/vkdiag/soak-fixed-surface.csv`：44 轮，20 次标记 TDR、24 次 clean。重新查询相同时间窗的 Windows System 日志，得到 20 条 nvlddmkm 事件，与表一致。
- `E:/_Game/majp-test/vkdiag/soak.csv`：随后 25 轮均 clean，累计 Game 时间 4441.1 秒，约 74 分钟。17:47–19:51 时间窗未发现 nvlddmkm 事件。
- `Build/Logs/MajPlayRuntime.log` 与 `.old`：最新两轮仍是 H.264 854×480、D3D11VA 与 Vulkan 共享复制，未禁用 FFmpeg。
- 留存的 `Build/system-settings.json` 为 VSync=false、FPSLimit=60、RecordMode=Disable。历史 CSV 没有每轮设置快照或实测帧率，因此这是对低帧率规避的支持证据，不能把当前配置当成每轮精确的历史证明。
- `gpuav-report.md` 显示 layer asserted=False；不能把其中零 GPU-AV 命中解释为 GPU 辅助校验已通过。

旧报告的移除桥、软件解码、禁用录制、无 PV 等结果已作为历史实验记录读取。本次没有重新执行这些旧实验。根目录 `Logs` 中的旧 T3 日志和当前 dev 源码版本不同：当前源码不存在 VulkanUploadBridge/MajdataPlayTextureUpload，不能混用其行号和实现。

## 对旧结论的校正

1. 最后停在 nvoglv64 的等待或 capture 最后一条是 vkQueuePresentKHR，只能证明错误/等待在此暴露；先前的 GPU 工作仍可能是原因。
2. sync validation 没有输出，不能排除所有应用同步、扩展互操作、Unity 内部或驱动问题；还须确认校验层实际启用及覆盖范围。
3. 替换上传实现后仍崩，不能单独证明 Unity 上传实现无问题。两个实现仍共享 Unity 设备、队列和呈现；必须确认替代路径没有回退。
4. 高温、带宽或显存不足没有直接支持证据，但低温、低平均功耗也不能绝对排除硬件/供电异常。
5. nvlddmkm Event 153 本身不是“驱动已损坏”或具体故障引擎的判据。本次运行器单独记录它，结合实际进程完成状态和 VK_ERROR_DEVICE_LOST 判断，不把单个事件自动归因给播放器。
6. 当前 Game PV 使用 `BGManager.cs:35/62/228–239` 的 RawImage，并由 `Game.unity:9059–9060` 引用；`BackgroundVideoController` 的 SpriteRenderer 是另一条全局背景路径。旧报告把 Game PV 描述为 SpriteRenderer，不能直接沿用到当前 dev。空场景全屏 quad 对照不覆盖这个 Canvas/RawImage 集成。

## 当前源码审查

### 软件 RGBA 路径

- `FFmpegVideoPlayer.cs:907–944`：主线程创建 RGBA32、无 mipmap 纹理，938–939 行执行 LoadRawTextureData(IntPtr, size) 和 Apply(false, false)。帧由调用方 using 持有到同步 CPU 拷贝结束。源不必保留到 GPU 完成，因为 LoadRawTextureData 已复制进 Unity 的 CPU 纹理存储。
- `VideoFrameConverter.cs:144–196`：验证尺寸，checked(width*height*4) 分配独立输出；swscale 使用对齐 staging，再复制为 tight RGBA。854 像素行宽本身不需要匹配 Vulkan buffer row pitch，上传布局由 Unity 负责。
- `DecodedVideoFrame.cs:149–181`、`VideoDecodeSession.cs:248–280/514–532`：出队后转移帧所有权，关闭只清理仍在队列的帧。后台 decoder 退出后释放，没有观察到关闭回收已交给主线程的独立像素缓冲的路径。
- `VideoBandExecutor.cs`：等待行带复制完成后才发布帧，没有发现工作线程仍写已发布像素的确定路径。

### D3D11VA / Vulkan 路径

- `VulkanVideoDecode.cpp:297/328`：405 表示无法安全扩展 feature chain，放弃原生 Vulkan Video；`VulkanInterop.cpp:97–98` 的外部共享路径仍可能成功。不能把 405 等同于关闭硬解。
- `FFmpegUnityBridge.dll.meta:9` 为 isPreloaded=1，`Bridge.cpp:18–29` 在 UnityPluginLoad 时安装 Vulkan 初始化拦截并注册设备事件，不依赖托管解码偏好。单独设 Software 不等于卸载桥接，也不能排除其设备初始化影响；历史“移除桥”和“软件解码”是两组实验，应分别解释，不合并成未做过的联合隔离。
- `VulkanInterop.cpp:375/389–391/245–252`：生产者非阻塞获取 key0，Flush 后成功释放 key1；Vulkan 提交等待 key1、释放 key0。GPU 队列仍有互斥等待，不能只凭 BeginWrite 零超时声称没有等待，但未找到正常链漏 key 的证据。
- `VulkanInterop.cpp:407/438–445`：Surface 被 job 保持到 fence 完成或设备丢失。Unity target 则由 AccessTexture/AccessQueue 纳入当前帧资源跟踪；仅凭 Job 没有 C# 对象引用，不能直接判定 target 提前释放。

发现两项与当前持续播放因果关系未确立的缺口，暂不作为 TDR 修复修改：

- `VulkanInterop.cpp:438–446`：fence 查询/等待除成功和 DEVICE_LOST 外的其他负错误未传播，可能掩盖首个失败并保留 job。
- `VulkanInterop.cpp:449–458` 与 Surface 析构：如果设备关闭时仍存活的 Surface 尚未释放，active=false 后其 Vulkan 子对象不再显式释放。这是条件性关闭清理缺口，当前是否触发需运行证据；不能解释成已经找到持续播放根因。

## 尚未隔离的捕获模块

本次 Vulkan 静态对照的已加载模块中，出现 `C:/ProgramData/obs-studio-hook/graphics-hook64.dll` 和 `C:/Windows/System32/nvspcap64.dll`，没有 FFmpeg/avcodec/avutil。NVIDIA DLL 的 FileDescription 为 NVIDIA Game Proxy，版本 11.0.8.299。

重新读取旧现场 `cdb-hang-11540.modules.md:57/94` 和 `cdb-hang-26188.modules.md:57/96`，两次硬解/软解崩溃中均有相同名称及版本的捕获模块。这说明此前禁用游戏录制没有形成“无捕获钩子”的完整隔离；DLL 加载本身不证明当时正在捕获，更不证明它就是根因。

机器注册了 OBS 的 Vulkan implicit layer。已读 `C:/ProgramData/obs-studio-hook/obs-vulkan64.json`：层名 VK_LAYER_OBS_HOOK，library_path 为 graphics-hook64.dll，disable_environment 为 DISABLE_VULKAN_OBS_CAPTURE=1。运行器新增 `-DisableObsCapture`，仅在启动自己的子进程时设置该环境变量，并立即恢复调用方原值；不修改系统注册表、不关闭用户 OBS/NVIDIA 应用。需要通过每轮 modules.csv 检查钩子是否真正不再加载，不能只凭设置变量声称已移除。NVIDIA Game Proxy 不受该 OBS 开关控制。

## 新的无 FFmpeg 对照

工具：本目录 `build.ps1`、`ProbeBuild.cs`、`RenderProbe.cs`、`RenderProbe.shader`、`run-matrix.ps1`。

- 指定 Unity 6000.3.17f1，Windows x64 Mono，Gamma，Graphics Jobs 开启，正常 Player 渲染循环。
- 全新隔离工程，不复制游戏、FFmpeg、原生桥接或 pacing 插件；Built-in 管线、相机、全屏 unlit quad。
- 常驻 pinned byte[]、IntPtr 重载、RGBA32、无 mipmap、Bilinear，每秒约 30 次改变像素并上传。
- 静态条件只初次上传；动态条件持续上传。每 10 秒记录实际 Update 频率、上传数和 CPU 上传调用时间；Update 频率不等于实测 vkQueuePresentKHR 调用率。
- 运行器逐例保存日志、开始/结束时间和 nvlddmkm 事件，只终止自己启动的 PID。驱动事件出现后停止矩阵，保留首个失败条件。
- 构建和运行产物均在 `.work/`，不写入主游戏 Build 或 Assets。

### 构建与运行结果

构建通过：Unity 6000.3.17f1_cf0352b38e81，独立 Windows x64 Mono Player。build.log 有当次成功标记，C# 和诊断 shader 编译通过。`.work/build-manifest.json` 记录源码与产物 SHA256。本次 probe 的 UnityPlayer.dll SHA256 为 b3938532fd45862e2683be7bfd9a7af6c238330c5b0c1b88470eef8840026644，与当前主游戏 Build 中同文件相同。

实际环境：RTX 2060 SUPER、驱动 32.0.16.1088，桌面/窗口 1080×1440、119.998 Hz；命令行请求的 1080×1920 在 borderless 模式没有成为实际交换链尺寸。主游戏留存 Player.log 也记录 1080×1440。VSync=1 的 probe 实测 Update 约 120 FPS，不能当成旧报告所述的 144 Hz 条件。线程模式为 NativeGraphicsJobsSplitThreading，与主游戏 kGfxThreadingModeSplitJobs 一致。

本轮六组完整对照均正常 DONE、进程退出码 0、无 VK_ERROR_DEVICE_LOST/Crash 标记、无新增 nvlddmkm 事件，每组持续约 180 秒：

| 后端 | VSync | 实测 Update 频率 | 上传目标 | 累计上传（含首帧） | 结果 |
| --- | --- | --- | --- | --- | --- |
| Vulkan 静态 | 1 | 约 120 FPS | 仅一次 | 1 | 完成 |
| Vulkan 动态 | 1 | 约 120 FPS | 30/s | 5400 | 完成 |
| D3D12 动态 | 1 | 约 120 FPS | 30/s | 5400 | 完成 |
| Vulkan 动态 | 0 | 约 60 FPS | 30/s | 5400 | 完成 |
| Vulkan 动态 | 0 | 约 144 FPS | 30/s | 5400 | 完成 |
| Vulkan 动态 | 1 | 约 120 FPS | 60/s | 10798 | 完成 |

表中频率是实测 Update 循环，非 GPU 执行或 present 次数。60/s 条件根据现用故障视频的 ffprobe 元数据补充：H.264、854×480、60/1 FPS，SourceVideoSHA256=3F62CF63822C2813E8434B37514EFE5C785D70D70CA5391CD838D8CF32B4C8B9。旧报告中的 30 FPS 描述不能作为当前视频参数。

证据目录为本模块 `.work/matrix-20261121-2305/`、`.work/matrix-high-20261121/`、`.work/matrix-upload60-20261121/`，包含 matrix.csv、原始引擎日志、模块快照与每例事件 XML。

另执行 10 秒 OBS 开关验证：`.work/obs-disable-check-20261121/`。进程正常结束，模块快照中 graphics-hook64.dll 消失，nvspcap64.dll 仍在；这验证了进程级禁用方法，不属于长时稳定性证据。

## 主游戏实际场景对照

`.work/MainPlayer` 为完全复制的独立主游戏副本，739 文件、691190209 字节；没有使用 junction/hardlink，不改源 Build。只复制现用目标曲和皮肤，过滤出厂曲、历史数据库、缓存与录制。逐文件复制 SHA256 与源配置前后校验已通过。独立配置为 RecordMode=Disable、PreferredDecoder=Hardware、VSync=true、FPSLimit=144、Vulkan pacing=false、键盘输入、关闭触摸板/LED、关闭在线业务。

关闭在线业务不等于绝对无网络：现有 RuntimeInfoDisplayer/VersionChecker 仍会独立执行 GitHub 版本检查；关闭触摸板和 LED 也不阻止现有初始化枚举设备。本次未更改系统防火墙、注册表或用户捕获应用。

主对照由 `run-main.ps1` 仅控制自身子进程，进入 Game 后停止导航。FFmpeg模式要求到达Result且实际后端、H.264 854×480、所选软/硬解日志匹配；合成模式要求INSTALL和实际PROGRESS、无目标H.264打开并到达Result。导航失败/超时/辅助库错误/设备丢失分开记录，驱动事件样本保留观测而不冒充正常完成。

- `main-baseline-01`：无效样本。新档默认 Easy(0)，而目标只有 inote_2–5；Game 很快返回 List。人工停止该测试副本，未计为稳定运行或 GPU 崩溃。已保留数据库备份，并通过现有 Runtime/config.json 迁移机制选择 Master(4)；实际启动日志和只读数据库查询确认导入成功。准备脚本同步增加 Master 种子，运行器增加提前离开 Game 的无效检测。
- `main-baseline-02`：有效故障复现。2026-11-21 23:33:58.6425 加载 Game；23:34:09.3780088 出现 nvlddmkm Event 153，记录号 70671，GPUID a00，距 Game 约 10.74 秒。使用主游戏 2.1.3、H.264 854×480、D3D11VA、Vulkan 共享复制，捕获到 OBS Graphics Hook 1.8.3.0 与 NVIDIA Game Proxy 11.0.8.299。两份设置文件前后 SHA256 均相同。运行器在发现事件后终止自己的进程，因此 ExitCode=-1 是清理结果；不能把它当作游戏自行崩溃，也没有声称本轮日志已出现 VK_ERROR_DEVICE_LOST（实际未出现该标记）。
- `main-no-obs-01`：三局全部到达 Result，各约 167.5 秒，均 0 条新增驱动事件。每局模块快照无 graphics-hook64.dll，仍有 FFmpegUnityBridge 与 nvspcap64.dll；日志始终为 D3D11VA→Vulkan 共享，两个设置文件 SHA256 与基线相同。
- `main-obs-restored-01`：恢复 OBS 加载后三局也全部到达 Result，无新增驱动事件；模块快照确认 OBS hook 已回来。没有获得“恢复钩子即故障复发”的反向证据，因此不能称禁用 OBS 已修复或认定它是根因。OBS 仍可能是概率/交互因素，但当前数据不支持确定归因。
- `main-software-no-bridge-01`：2026-11-22 的联合排除实验。仅在测试副本将 PreferredDecoder 改 Software，并移走其 x86_64/FFmpegUnityBridge.dll；保留 OBS/NVIDIA hook。日志确认 H.264 854×480、device=Software、Software RGBA upload；模块无 FFmpegUnityBridge、无 nvrtum64。Game 00:06:23.56 后，00:07:21.1592079 出现同类 nvlddmkm153（记录号70673，GPUIDa00）。额外观察5秒后进程仍存活，Responding=false，随后测试清理；未记录到 VK_ERROR_DEVICE_LOST。这说明当前故障不需要 D3D11VA 硬解或桥接 DLL，不能用单一 D3D11/Vulkan 共享竞态解释全部样本。实验后测试副本的 DLL 和硬解配置已按备份及原哈希恢复。
- 实际场景合成帧使用 `.work/MainSynthetic`，只替换 BGManager.SetMovieAsync，保留原 RawImage/材质/缩放、MRP、Live2D、音频与Master谱面。原Core及备份一致、全方法体比较仅一个方法变化，未改原Build/Assets。全局菜单VP8及桥接预加载仍在，不能称为全进程无FFmpeg；Canvas上传相位、CPU负载和CurrentSec诊断文字差异须保留。
- `main-synthetic-01`：辅助库netstandard2.1依赖在实际裁剪Player中缺失，未安装，人工停止并排除。辅助库已改为直接引用实际Player Mono BCL，验证全部5个引用及59类型/64成员；只在副本换helper，Core不再改。`main-synthetic-02`：停在Title未进Game，导航失败，排除；已增加前台归属/导航计数及80ms短按以匹配GetAsyncKeyState轮询。
- `main-synthetic-03`：INSTALL已成功，目标H.264未打开；Game00:40:48.6045、INSTALL49.3247，00:40:56.3957769记录153（70680），随后Alive=true、Responding=false。故障早于首个10s PROGRESS，缺少实际上传率证据，不能宣称已验证60Hz。该样本保留但标注观测不足。
- `main-synthetic-04`：首次进度改为1秒、后续10秒，其他helper行为不变。Game00:46:35.2677、INSTALL36.1212；37.1176已记录47次上传，实测约45.964次/秒；47.7652累计470次，窗口平均约39.726次/秒，低于目标60Hz。00:46:45.9625838出现153（70685），后续观察Alive=true、Responding=false，无VK_ERROR_DEVICE_LOST日志；运行器FrameSourceConfirmed=true。全程无目标H.264 decoder-open，说明谱面视频解码/硬件共享不是此场景故障的必要条件；但全局FFmpeg背景与预加载桥接尚在，不能外推为整个FFmpeg库已排除。

- `main-synthetic-no-video-01`：核心联合隔离正例。除Core单方法合成帧入口外，只把副本MajdataPlay.FFmpeg.dll的PrepareAsync(CancellationToken)替换为实际Player mscorlib Task.CompletedTask，复读全部方法体确认仅此处新增变化；副本桥接DLL另移到可恢复备份。Game00:52:55.7135，INSTALL56.4235，首次实测约44.730次/秒、累计46；00:53:08.0903累计484、窗口平均41.069次/秒。00:53:06.562065记录153（70686），后续Alive=true/Responding=false。全程FFmpegEvidence为空，无VP8或H.264 decoder-open；完整模块快照无avcodec、avformat、avutil、swscale、FFmpegUnityBridge，且Vulkan external_memory_win32/win32_keyed_mutex未启用。说明媒体解码及FFmpeg原生共享路径不是这种故障的必要条件。托管FFmpeg程序集/空闲生命周期仍在，不能说完全删除整个FFmpeg组件。该进程仍加载原pacing DLL、OBS与NVIDIA代理，未据此排除这些层。
- `main-synthetic-no-video-d3d12-01`：同一已关闭全部视频准备/移走桥接的副本，仅命令行切为D3D12，Game00:56:47.0804→Result00:59:34.5485，167.4681秒完整通过，无新增驱动事件。首次预热后约120fps、实测约60次上传/秒，退出Game时CLEANUP累计9985次。没有媒体decoder日志及原生FFmpeg库/桥接；仍有NVIDIA代理和原pacing DLL，OBS Vulkan layer在该后端未加载。因此该控制支持替代入口可完成完整游戏、故障更集中于本机Vulkan路径，但一个D3D12阴性样本不等于所有D3D12场景稳定，后端改变也伴随相关层/驱动/着色器路径变化。

### 因果边界

nvlddmkm153 是系统级驱动错误日志，事件里的 PID4 是内核发出者，不能直接映射为游戏 PID，也不单独证明某个 WDDM 引擎复位或永久挂起。首个硬解基线事件发生即清理，未确认自然崩溃/挂起；后一个软件无桥接样本增加了等待和 Responding=false 观察，可证明该时点窗口不响应，但仍不能仅据这一点判定永久挂起或具体GPU致错调用。固定测试顺序、驱动恢复/缓存状态、实际捕获活动及同GPU其他进程仍可能影响偶发概率。

### 解释边界

独立纯Unity场景的阴性结果不证明FFmpeg有错，因为它缺少主游戏MRP、Live2D、RawImage/Canvas及完整帧内结构。本次已进一步在实际游戏场景做合成源替代，并在无媒体解码/桥接条件下复现同类驱动错误与窗口不响应；它支持这些视频路径不是必要条件，但不能由此锁定具体剩余模块，也不保证不同条件下同类症状一定同根。

## 验证层级和后续范围

本轮完成：指定Unity版本的独立Player构建；实际Windows x64 Mono/RTX2060SUPER/610.88实机；完整游戏副本硬解、软件无桥接、OBS开关、Game合成帧、全部视频准备关闭与D3D12控制；工具离线编译/元数据与方法体校验。五个PowerShell脚本语法检查及最终git diff --check通过，所有诊断Player已清理。原Build两个配置文件哈希与复制前一致，原Core/FFmpeg/FFmpegUnityBridge哈希保持；既有字体与项目设置改动未触碰。没有在主项目做Editor PlayMode、IL2CPP、其他GPU/驱动、真实144Hz桌面或启用录制的全场景验证。实测上传率与目标频率分开记录，不把CPU调用时间当GPU执行时间。

后续应以已保留的无媒体解码合成副本为Vulkan基线，优先单独隔离剩余原生pacing预加载、OBS/NVIDIA捕获交互及Graphics Jobs/MRP/RawImage/Live2D等渲染组成。需要实际激活并确认的GPU校验、受限大小的GPU/ETW时序或驱动故障标记来定位具体命令；当前没有足够证据修改播放器逻辑作为TDR修复。`launch-without-obs.ps1`只作为实验开关，不是已证明有效的修复。

### 关键原始证据

- [原始硬解基线结果](<.work/main-baseline-02/attempt-1/result.json>)。
- [OBS禁用三局](<.work/main-no-obs-01/results.json>) 与 [恢复加载三局](<.work/main-obs-restored-01/results.json>)。
- [软件解码且无桥接的故障结果](<.work/main-software-no-bridge-01/attempt-1/result.json>)。
- [Game合成帧有效故障结果](<.work/main-synthetic-04/attempt-1/result.json>)。
- [全部视频准备关闭且无桥接的故障结果](<.work/main-synthetic-no-video-01/attempt-1/result.json>)、[上传日志](<.work/main-synthetic-no-video-01/attempt-1/Player.log>)、[模块快照](<.work/main-synthetic-no-video-01/attempt-1/modules.csv>)、[驱动事件](<.work/main-synthetic-no-video-01/attempt-1/driver-events.xml>)。
- [同副本D3D12完整控制结果](<.work/main-synthetic-no-video-d3d12-01/attempt-1/result.json>)。
- [Core单方法替代清单](<.work/MainSynthetic/patch-manifest.json>)、[实际Mono ABI修正清单](<.work/MainSynthetic/helper-abi-fix-20261122-003103-740.json>)、[早期进度修订清单](<.work/MainSynthetic/helper-progress-1s-20261122-004445-170.json>)、[全部视频准备关闭清单](<.work/MainSynthetic/disable-video-prepares-20261121-165126-864-8512a178d3c84863a0588d6e60800c22.json>)。
- [诊断工具使用说明](<README.md>) 与 [实际场景辅助库说明](<SyntheticFrameProbe/README.md>)。

## 权限检查记录

本次起初 workspace-write 沙箱在命令启动前报 grantWrite/Win32 5。按 diagnose-windows-sandbox-acl 技能，已授权的非受限检查在仓库根目录补充当前用户 FullControl，并重读确认 WRITE_DAC/WRITE_OWNER 均可用；内容和所有者未改变。扫描 2000 项后截断，没有声称扫描完整目录。随后用户切换为完全权限，后续命令可运行；没有以此声称受限模式已重新验证。

恢复记录：`E:/repos/MajdataPlay/MajdataPlay-permission-recovery/acl-report-9f6f814288e44b5cae37a993328ec97c.jsonl`。

如需撤销本次权限修改，技能打印的恢复命令为：

```powershell
pwsh -NoProfile -File 'E:\repos\MajdataPlay\MajdataPlay-permission-recovery\acl-backup-1e7833685c9940f89031f02790eb73fe.json.ps1' -Path 'E:\repos\MajdataPlay\MajdataPlay' -AllowRoot 'E:\repos\MajdataPlay\MajdataPlay' -Restore 'E:\repos\MajdataPlay\MajdataPlay-permission-recovery\acl-backup-1e7833685c9940f89031f02790eb73fe.json'
```
