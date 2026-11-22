# Vulkan 捕获、独立回放与同步校验

日期：2026-11-22。本报告延续[渲染路径续查](<VULKAN-RENDERING-RESULTS.md>)，仅使用隔离副本，原Build/运行源码/用户设置保持不变。

## 结论先行

已从完整初始化取得可读、未裁剪的故障捕获。原流尾部直接记录两次vkQueueSubmit返回VK_ERROR_DEVICE_LOST；独立GFXReconstruct回放也触发新的设备丢失与驱动153，进程中没有UnityPlayer、Mono或FFmpeg原生库。这比只观察游戏挂起更接近可移交的GPU API工作负载，但回放含虚拟交换链适配，不能把其失败位置直接当作原应用致错GPU指令。

实际启用核心与同步校验后，发现可与原捕获初帧对应的索引缓冲RAW、初始交换链清除/布局WAW同步缺口；同时存在回放器注入的虚拟交换链WAR和设备丢失返回分歧之后的命令池错误，必须分开。初始化同步缺口与后续TDR的因果关系尚未建立。正在直接对原诊断Player启用同套校验且关闭GFX捕获，以排除仅回放适配产生的诊断。

## 完整初始化捕获

[首例结果](<.work/full-startup-capture-20261122-01/capture-result.json>)：

- 工具为本机既有GFXReconstruct1.0.5，Vulkan头1.4.363；实际捕获层模块路径/哈希、CIM父子关系与MAIN START PID均核对。
- 普通sampled合成源，OBS禁用，回读和原生fence调度关闭；没有FFmpeg媒体会话/原桥接。当前同名native DLL只是mf_*诊断探针初始化，不执行其marker。
- Game日志18:10:53.8294，首条nvlddmkm153在18:12:56.2810944，约122.452秒；事件后进程仍活且窗口不响应，随后由运行器清理。
- 捕获文件1853570378 bytes，LZ4，18925帧，info退出0、stderr空、无trim；保留从startup的初始化。
- SHA256：C06754D89BC8037192AB32925F66664FD48C91F03BBF70D2D29ED76A1773B452。
- [info](<.work/full-startup-capture-20261122-01/startup.gfxr.info.txt>)标注Target API1.1.0、RTX2060SUPER、Windows驱动32.0.16.1088；其“Used resolutions2560×1440”不替代应用日志的1080×1440窗口与Unity报告144Hz，不能只据此推断每帧交换链实际范围。

执行首版监督脚本SHA9DC257887BB12189911578710B3C1A70EAC47338538CFE506C15F27ECC22ADE7，soft cap6GiB、reserve30GiB、总360秒。实测发现活跃writer独占文件，目录长度可缓存0；root另核对磁盘净消耗约1.727GiB，结束体积低于预算。不能把此例说成原目录长度上限已实证有效。新脚本补充净卷空间差OR保护/PeakDiskConsumption及自身SHA记录，其他进程写盘可导致假阳性、释放可抵消增长，仍是soft guard而非硬配额；旧结果不被覆盖。

## 尾部实际返回与纹理上传

只用CPU转换最后7帧（零基18918–18924，约544KB JSONL），并补前9帧18910–18918与初帧0；没有整份捕获展开或导出全部二进制。

| source event | 调用 | 实际捕获返回 |
| --- | --- | --- |
| 4117244 | queue6，vkQueueSubmit(CB611/595,fence19097，无wait) | VK_ERROR_DEVICE_LOST |
| 4117245 | vkGetFenceStatus(fence19097) | VK_ERROR_DEVICE_LOST |
| 4117261 | queue6，vkQueueSubmit(CB624,fence622，wait122/color-output，signal119) | VK_ERROR_DEVICE_LOST |
| 4117262 | vkGetFenceStatus(fence622) | VK_ERROR_DEVICE_LOST |
| 4117264 | vkQueuePresentKHR，swapchain1418/index0 | VK_SUCCESS |

最后EndMarker18925是元数据，非“API没有返回”。不能把Present作为根因；原应用EngineFailureInLog=false只是没有文本，并不能否认捕获中的设备丢失。

854×480三次上传关联到image19047、19003、19098。新建19047/19098的UNDEFINED→TRANSFER_DST、srcAccess0属于正常初始化；三次copy后均有TRANSFER_WRITE→SHADER_READ、TRANSFER→VERTEX/FRAGMENT/COMPUTE及对应布局。19047销毁前相关fence查询均成功，未证实仍pending即销毁。19003的创建/view/descriptor与多次复写已由前9帧补齐，但是否覆盖先前所有采样完成不能只由UNDEFINED判断。

详细事件与资源关联见[尾部审查](<.work/full-startup-capture-20261122-01/tail-review.md>)、[数据](<.work/full-startup-capture-20261122-01/tail-review-details.json>)。

## 独立回放

### captured交换链模式：无效控制

[首回放](<.work/replay-full-startup-20261122-01/result.json>)反复报告acquire图像索引与捕获不同，root已核对PID/路径后主动停止。无驱动事件，ExitCode=-1是人工终止，既不算自然崩溃，也不算稳定通过。另有应用进程名不同的驱动行为提示；[人工复核](<.work/replay-full-startup-20261122-01/manual-review.json>)保留原因。

### 虚拟交换链、应用同名：独立设备丢失正例

[第二回放](<.work/replay-virtual-appname-20261122-01/result.json>)使用同字节工具副本命名MajdataPlay.exe，单独输出目录及依赖搜索路径；虚拟交换链映射、捕获present-mode，无额外同步/skip-failed选项，OBS和再次捕获禁用。

回放18:24:28开始，约18:25:47发生新4条153；日志FATAL明确：API index2048509/thread2/vkGetFenceStatus实际返回VK_ERROR_DEVICE_LOST，而源捕获此调用原为VK_SUCCESS。回放不能继续，root在检测事件时清理；检查点Responding=true，不能外推为已永久挂起或自然崩溃。模块无UnityPlayer/Mono/FFmpeg/原pacing/OBS，仍有NVIDIA驱动和nvspcap64。

这证明独立回放GPU工作负载也能引发设备丢失，但失败比原捕获更早，且虚拟交换链、过程调度及工具注入不同；不能称逐帧完全等价或把错误暴露点当最初致错点。[回放日志](<.work/replay-virtual-appname-20261122-01/replay.log>)和[驱动事件](<.work/replay-virtual-appname-20261122-01/driver-events.xml>)。

## 校验回放及消息来源

[校验控制](<.work/replay-virtual-appname-validation-20261122-01/result.json>)同虚拟交换链/同名，另启用Core+Synchronization并标注工具注入命令。实际SDK校验层模块与CURRENT-VALIDATION-ENABLED信息均确认，非仅设置开关；未请求GPU-assisted。

此轮无新增153、自然exit0，日志End timer(Frame18926)与完整18925帧相符，约133.318秒。但存在校验错误，不能把exit0说成同步通过，也不能称校验修复了故障。计划中的measurements.json未生成，实际完成证据来自日志timer而非不存在文件。

1. **可关联原捕获的index RAW。** event1102在CB134复制14→buffer407/936bytes；1118绑定、1119 DrawIndexed468×UINT16；第二条1131复制14→buffer546/12bytes，1135绑定、1136 DrawIndexed6。CB138在1183被CB564执行，1194同Submit提交[134,564]。初帧55个vkCmdPipelineBarrier均仅image barrier，global/buffer memory barrier为0，无Events/Barrier2，唯一QueueWaitIdle1011早于copy；renderpass547无dependency。源流未见TRANSFER_WRITE→INDEX_READ内存依赖，image barrier与仅CB顺序不能提供该依赖；尺寸高度吻合校验两条RAW。回放wrapped假句柄尚未直接映射captured ID，且初帧缺口不等于已证明后续TDR根因。
2. **可关联原捕获的startup WAW。** image110经1171转TRANSFER_DST、1172 ClearColorImage；1186在renderpass549中从UNDEFINED隐式转COLOR，外部依赖仅fragment/shader-read→color-write，未覆盖TRANSFER clear；中间屏障不是针对110。与启动WAW吻合，仍需直接原进程验证。
3. **工具虚拟交换链WAR。** 带[GFXR Replay: Blit virtual image into swapchain image]标签的消息来自适配命令，不能直接算原应用bug；重复上限抑制消息不等于后续无hazard。
4. **失配之后的尾部命令池错误。** source4117244返回DEVICE_LOST而本次回放返回SUCCESS之后，原清理/重用路径在回放仍有pending工作，产生Frame18925 ResetCommandPool错误。这是返回分歧下游，不能反推为原设备丢失前的根因。

详细[校验来源审查](<.work/full-startup-capture-20261122-01/validation-review.md>)与[校验日志](<.work/replay-virtual-appname-validation-20261122-01/replay.log>)。

## 直接原进程校验

`direct-core-sync-validation-20261122-01`对同一诊断Player启用Core+Synchronization，GFXRECON_DISABLE=1且无Replay。SDK校验层模块路径、启用状态、独立日志均确认；模块中没有GFXReconstruct层或OBS。原进程日志直接报告相同936/12bytes index RAW，以及startup clear/layout WAW和acquire→TRANSFER layout WAR，说明这些不只是GFX虚拟适配造成。

该轮Game运行器观测18:44:06.6363，首条15318:46:30.4483481，约143.812秒后故障，随后Alive=true/Responding=false；4条153属一次观测。核心/同步状态打印均true，报告14条hazard，三类RAW/WAR/WAW；两配置/boot/所有诊断产物前后哈希保持。没有请求GPU-assisted，也没有设置会阻止非法命令到达driver的VK_DBG_LAYER_ACTION_FAIL。直接源码可关联的缺口与原进程校验相互支持，比仅回放消息更强；但尚未证明这些初始化缺口导致该时点TDR，需要窄修正/反向对照而非直接归因。

证据：[验证结果](<.work/direct-core-sync-validation-20261122-01/validation-result.json>)、[原进程校验日志](<.work/direct-core-sync-validation-20261122-01/validation.log>)、[应用故障结果](<.work/direct-core-sync-validation-20261122-01/run/attempt-1/result.json>)、[驱动事件](<.work/direct-core-sync-validation-20261122-01/run/attempt-1/driver-events.xml>)。

下一项正在准备仅诊断native缓冲依赖对照：只在CopyBuffer后补充transfer-write→future-read的内存可见性，先确认936/12 index RAW是否消失；交换链image clear/acquire缺口暂不同时改。它不修改原Build/Assets，也不预称生产修复。
