# 缓冲可见性缺口与窄修正对照

日期：2026-11-22。延续[捕获与校验报告](<CAPTURE-RESULTS.md>)。仅使用隔离副本，原Build/运行源码/用户设置不变。

> **后续更新（同日 20:30–21:06）**：完成窄修正实现、交错对照与原始路径验证。
> 关键反证：关/窄交替六局 **全部完整通过、0 驱动错误**，故障在该时段未能复现。因此先前“开屏障通过、关屏障故障”的关联被时间/状态混淆，**不能声称修复有效**。可信结论仅剩：该索引缓冲同步缺口真实存在于正式运行路径，且窄修正能消除对应诊断。详见文末「三步验证结果」。

## 结论先行

原捕获初帧存在可定位的同步缺口：两个索引缓冲（936 与 12 字节）经 `vkCmdCopyBuffer` 写入后，由 secondary command buffer 的 `vkCmdDrawIndexed` 读取，之间没有任何 transfer-write → index-read 的内存依赖；初帧 55 个 pipeline barrier 全为 image barrier，无 global/buffer memory barrier，也无 Events/Barrier2。该缺口在原进程直接 Khronos 核心/同步校验（无 GFX 捕获、无回放）中同样报告，不是回放适配产物。

在此基础上做了一个**窄对照**：仅在 `vkCmdCopyBuffer` 之后追加“整个目标缓冲 TRANFER_WRITE → MEMORY_READ、dst ALL_COMMANDS”的屏障，其余渲染、图像布局、队列与开关完全不动。

- 开启该屏障后，校验日志中原先的 936/12 字节索引 RAW **消失**，说明注入命令确实进入调用链并覆盖了该依赖。
- 同一 120 Hz 条件下，该屏障开启时连续 4 次完整跑完谱面（1 次校验轮 + 3 次常规轮），0 次驱动错误。
- 关闭该屏障后，第 1 局完整、第 2 局出现驱动错误（8 条 153）。

因此这条同步缺口与故障复现呈强相关，且是当前唯一“修改后诊断消失、故障同时未再出现”的单一变量。但**尚不能称已定因或已修复**：

1. 注入的屏障 dstStage 取 `ALL_COMMANDS`，会额外串行化该队列上的大量后续工作；因此它产生的是“整体更保守的序列化”，不能把改善单独归因于索引可见性本身。
2. 样本量有限（开 4/4 通过、关 1 通过 1 故障），非随机化、固定顺序，驱动复位与缓存状态未控制。
3. 该修正作用于整个进程所有 buffer copy，是诊断假设，不是生产修复；未验证性能代价、其他 GPU/驱动、IL2CPP 或移动平台。
4. 同类症状未必同一根因；尚未确认原始正式 Build（无合成 helper）是否也命中这条路径。

## 缺口来源证据

- 源事件（完整初始化捕获，SHA256 C06754D89BC8037192AB32925F66664FD48C91F03BBF70D2D29ED76A1773B452）：
  - event1102，CB134，`vkCmdCopyBuffer` buffer14→buffer407，936 字节；
  - event1118/1119，secondary CB138 绑定 407 并 `vkCmdDrawIndexed` 468×UINT16=936；
  - event1131，buffer14→buffer546，12 字节；event1135/1136 绑定并 `DrawIndexed` 6×UINT16=12；
  - CB138 由 CB564 在 event1183 执行，event1194 同一 Submit 提交 `[134,564]`，唯一等待的 acquire 信号量仅覆盖 COLOR_ATTACHMENT_OUTPUT。
- 校验侧消息（同一缺陷）：`SYNC-HAZARD-READ-AFTER-WRITE`，`VK_ACCESS_2_INDEX_READ_BIT` 与 `VK_ACCESS_2_TRANSFER_WRITE_BIT` 冲突，region 恰好 936 与 12 字节。
- 详见[尾部审查](<.work/full-startup-capture-20261122-01/tail-review.md>)与[校验来源审查](<.work/full-startup-capture-20261122-01/validation-review.md>)。

注意事项：回放用假句柄未直接映射到 captured ID，上述关联基于大小、命令形态与同批次结构，属高度吻合而非句柄级证明。映射校验器中的 GFX 虚拟交换链 WAR 属工具注入，已单列。

## 探针实现与边界

新诊断探针源码与说明见 [native-buffer-barrier](<SyntheticFrameProbe/native-buffer-barrier/README.md>)：

- 仅在子进程设置 `MAJ_SYNTHETIC_BUFFER_BARRIER=1` 时注册一个 `AddInterceptInitialization`（优先级 0x40000000），包装 GIPA/GDPA 与 `vkCmdCopyBuffer`；`vkCmdPipelineBarrier` 通过原始链解析，保证校验层仍能看到注入命令。
- 原 copy 先以完全相同的参数转发，再追加屏障；不创建 Vulkan 对象、不提交自有工作、不改设备/交换链/特性/队列。
- GIPA/GDPA 自查询保持返回包装器，避免缓存内层 resolver 造成绕过。
- 关闭时不安装任何拦截或设备回调；缺入口点时 `bb_status` 负值上报，受管侧直接判无效而不是声称已修补。
- MSVC x64 C++17 `/W4 /WX /MT` 构建 0 警告 0 错误；纯 CPU 假函数测试覆盖参数保持、copy 先于 barrier、屏障字段、禁用模式、缺函数上报、resolver 自查询与关闭后不注入。

## 实机对照

探针开启（`bufferBarrier=True`，`bufferCopies == bufferBarriers`，`bufferBarrierErrors=0`）：

| 轮次 | 结果 | 驱动事件 |
| --- | --- | --- |
| 直接校验轮（`direct-sync-buffer-barrier-20261122-02`） | 完整到 Result，约 167.4 秒 | 0 |
| 常规三局（`vulkan-buffer-barrier-20261122-01`） | 三局均 `result` | 0 / 0 / 0 |

三局均为 Unity 报告 119.998 Hz、实测约 120 循环与 60 次上传/秒，两份设置与全部关键产物哈希前后一致。

探针关闭（`bufferBarrier=False`，同设置、同线程模式、同 120 Hz）：

| 轮次 | 结果 | 驱动事件 |
| --- | --- | --- |
| 第 1 局 | `result` | 0 |
| 第 2 局 | `driver-153`，窗口不响应 | 8 条（同一次故障） |

同一副本在此前基线（无任何修正）于 120 Hz 下的故障记录包括 10.8 秒、47 秒、55 秒与 143 秒等，说明该条件下故障率较高但并非必然。

### 一次被排除的无效轮

首轮校验对照因 `validate-live` 只设置了父进程环境变量、而 `run-main` 用自身开关默认值覆盖子进程环境，导致探针实际未启用；该轮已人工终止并排除，不作为阴性证据。修正为显式传递开关后重跑才得到上表结果。

## 尚未完成

- 需要一个“只补索引缓冲可见性、不额外全局串行化”的窄修正版本，以区分“修好这条依赖”和“多加了一次全队列等待”。
- 需要确认原始正式 Build（未替换 helper/未禁用视频）是否同样命中该路径；目前证据来自已移除 FFmpeg 视频会话的诊断副本。
- 需要更多轮次与交错顺序（开/关交替）以给出故障率区间，而非仅方向性判断。
- 图像侧 WAW（clear → render pass 隐式布局转换）与 acquire→TRANSFER 布局 WAR 仍未修正，本轮刻意只动缓冲依赖一项。

## 三步验证结果

按上一版“尚未完成”逐项执行。

### 第 1 步：只补索引/顶点可见性的窄修正

新增 [native-buffer-barrier-v2](<SyntheticFrameProbe/native-buffer-barrier-v2/README.md>)，同一二进制内提供两种模式，由 `MAJ_SYNTHETIC_BUFFER_BARRIER` 选择：

| 值 | 模式 | 注入内容 |
| --- | --- | --- |
| 未设置/0 | 关闭 | 不安装拦截或设备回调 |
| 1 | **窄** | 仅当目标缓冲以 `INDEX_BUFFER`/`VERTEX_BUFFER` 用途创建：`TRANSFER_WRITE`→`INDEX_READ`/`VERTEX_ATTRIBUTE_READ`，dst 阶段仅 `VERTEX_INPUT` |
| 2 | 宽 | 每次 copy：`TRANSFER_WRITE`→`MEMORY_READ`，dst 阶段 `ALL_COMMANDS`（上一版粗控制） |

缓冲用途通过包装 `vkCreateBuffer`/`vkDestroyBuffer` 记录（小锁，驱动调用在锁外且始终先原样转发）。ABI 升至 2，新增 `bb_skipped_calls`。MSVC `/W4 /WX` 构建 0 警告 0 错误，纯 CPU 测试覆盖：模式选择、copy 先于 barrier、窄模式范围/阶段/访问字段、跳过非索引顶点缓冲、销毁后用途遗忘、关闭后不注入。

实机确认窄模式真实生效（`bufferBarrierMode=1`，`bufferCopies=5712`、`bufferBarriers=5710`、`bufferSkipped=2`、`bufferBarrierErrors=0`）。

### 第 2 步：交错顺序对照

关/窄交替各三局，同副本、同 120 Hz、同线程模式：

| 轮次 | 模式 | 结果 | 驱动事件 |
| --- | --- | --- | --- |
| 1 | 关闭 | `result` | 0 |
| 2 | 窄 | `result` | 0 |
| 3 | 关闭 | `result` | 0 |
| 4 | 窄 | `result` | 0 |
| 5 | 关闭 | `result` | 0 |
| 6 | 窄 | `result` | 0 |

**六局全部通过，故障在该时段未复现。** 直接后果：

- 不能声称窄（或宽）修正降低了故障率——交错对照没有给出任何差异。
- 先前“宽屏障 4/4 通过、关闭 1 通过 1 故障”的对比，与该时段前后的故障分布一致；屏障实验恰好落在故障高发期之后，属典型时间混淆。
- 结合此前同条件基线既有 10.8 秒故障、也有多局连续通过，该故障在当前环境下呈强时间/状态依赖，单次或小样本对比都不足以定因。

### 第 3 步：原始游戏路径验证

对 `.work/MainPlayer`（原始未打补丁 Core/FFmpeg/MRP、原始原生插件、真实 H.264 视频与硬解）直接运行核心/同步校验，无 GFX 捕获、无回放、无缓冲探针注入：

- 日志确认 `Opened codec=h264 ... device=D3D11VA`、`Texture transfer=Vulkan GPU conversion + shared-resource copy`，运行器判定 `HardwarePrepared=True`、`NativeGpuTransfer=True`。
- 校验同样报告 936 字节与 12 字节的 `SYNC-HAZARD-READ-AFTER-WRITE`：`vkCmdDrawIndexed` 读取 `INDEX_READ` 与先前的 `TRANSFER_WRITE` 冲突。
- 该轮 `result`、0 驱动事件、配置与产物哈希未变。

**结论：该索引缓冲同步缺口存在于正式运行路径**，不是合成 helper、也不只是已打补丁诊断副本的产物。它独立于是否崩溃成立。

### 三步之后的最终判断

- 已确证：Unity Vulkan 渲染路径中存在真实的索引/顶点缓冲内存可见性缺口，在源捕获、独立回放、进程内校验、以及原始未修改游戏路径中一致出现，且可被窄修正消除。
- 未证明：该缺口是 TDR 的原因。交错对照未能复现故障，样本与时间窗口不足以支撑因果或修复结论。
- 因此不建议据此修改生产代码；如需继续，必须先建立**可重复触发的失败基线**（明确触发条件，如驱动/温度/刷新率/内容组合），再做 A/B。当前不具备该条件。

### 复现与证据

- 窄模式三局：`.work/vulkan-bb-narrow-20261122-01/`
- 交错六局：`.work/alt-20261122/`（含 `alternating-summary.json`）
- 原始路径校验：`.work/validate-original-path-20261122-01/`
- 探针构建：`SyntheticFrameProbe/native-buffer-barrier-v2/.build/build-manifest.json`

## 回放侧复现与工具污染排除（21:20–21:50）

### 现象：同一份捕获，回放约 50% 概率设备丢失

对同一 `startup.gfxr` 反复做虚拟交换链回放（120 Hz、应用同名、无校验层）：

| 运行 | 结果 | 失败位置（API index） |
| --- | --- | --- |
| 01 | 完整 18925 帧通过 | — |
| 02 | 完整通过 | — |
| 03 | 设备丢失 | 599513 |
| 04 | 设备丢失 | 1710801 |
| 05 | 设备丢失 | 3061318 |
| 06 | 通过（16373 帧提前结束，非设备丢失） | — |

三次失败位置**互不相同**，说明不是某条固定命令，而是累积/时序相关。

### 排除工具自身同步缺陷

校验层曾报告回放器注入的虚拟交换链 blit 缺少 acquire 同步（`vkCmdPipelineBarrier[GFXR Replay: Blit virtual image into swapchain image]`）。用 gfxrecon 自带的 `--vssb`（跳过该 blit）重跑六次：

| 运行 | 结果 | 失败位置 |
| --- | --- | --- |
| 1 | 设备丢失 | 563063 |
| 2 | 通过 | — |
| 3 | 通过 | — |
| 4 | 通过 | — |
| 5 | 设备丢失 | 1894021 |
| 6 | 设备丢失（9 条 153） | 2008801 |

**3/6 仍失败，比率与跳过前一致。** 因此回放器注入的 blit 不是设备丢失的原因，应用自身的命令流即可触发。这同时说明：回放可作为约 50% 复现率、单次约 170 秒的受控测量平台。

证据：`.work/vssb-20261122/`、`.work/observe-replay-*`。

### 该复现平台的意义与限制

- 优点：不依赖键盘导航、不受前台窗口影响、单次时间短、可重复。
- 限制：仍是**回放**，其设备丢失与被测进程内时序不完全等同；回放失败点在流中位置随机，不能直接指向具体致错命令。
- 用途：作为 A/B 测量工具检验“修复是否降低设备丢失率”，而不是作为根因定位本身。

## 同步层 A/B 尝试（未成功，不用于任何结论）

为在回放中直接检验“补上索引/顶点可见性是否降低设备丢失率”，新增了一个诊断用 Vulkan **显式层**（[native-sync-layer](<SyntheticFrameProbe/native-sync-layer/README.md>)）：

- 源码 `SyncLayer.cpp`（约 450 行）实现 `vkNegotiateLoaderLayerInterfaceVersion`、GIPA/GDPA 链、`vkCmdCopyBuffer` 后追加窄屏障、`vkCreateBuffer`/`vkDestroyBuffer` 用途记录，模式由 `MAJ_SYNC_LAYER_MODE` 控制（0 透传 / 1 窄 / 2 宽）。
- MSVC `/W4 /WX` 构建 0 警告，纯 CPU 假函数自测全部通过，导出符号经 dumpbin 核对。
- 与仓库实际头文件版本不符之处已记录：vendored Vulkan-Headers 实为 **1.3.275**，没有 `vk_layer.h`，因此 loader/层 ABI 结构在源文件内本地声明并加 `static_assert`。

**实机加载失败**：在 gfxrecon-replay 中通过 `VK_LAYER_PATH` + `VK_INSTANCE_LAYERS` 载入后，loader 日志确认“Insert instance layer”与库加载成功，但进程随即在 API index 2 处返回 `VK_ERROR_INITIALIZATION_FAILED`，loader 随即 “Unloading layer library”。放宽对下一层入口点的自检后现象不变，说明失败发生在更早的 `VkLayerInstanceCreateInfo` 链查找环节，本层未正确消费 loader 提供的 link info。

因此：**该层的回放 A/B 未执行**，`--vssb` 基线（无层）与带层运行之间没有任何可比数据。层代码保留在分支上供后续修正，**不构成修复证据**，也不应据此修改生产渲染代码。

## 生产分支

已创建分支 `fix/vulkan-buffer-visibility-sync`（基于 `dev` 的 `55797433e`），仅作为后续修复的承载点；用户原有的 4 处未提交改动与诊断目录未被触碰，**本轮没有向生产渲染代码提交任何行为改动**——因为在无法复现、且同步缺口与 TDR 的因果关系未证实的前提下修改渲染路径不符合仓库“行为修复需可验证”的要求。

## 浸泡基线：故障仍在持续复现（21:56–22:31）

先前“连续 10 局通过”使怀疑环境已稳定。22:00 起用**未修改**副本连跑 12 局完整谱面（120 Hz、无修正、无 OBS），故障重新出现：

| 局 | 结果 | 驱动事件 | 游戏内时长 |
| --- | --- | --- | --- |
| 1 | 驱动错误 | 6 条 | 约 68 秒 |
| 2–6 | 完整通过 | 0 | 约 168 秒 |
| 7 | 驱动错误 | 1 条 | 约 69 秒 |
| 8–12 | 完整通过 | 0 | 约 168 秒 |

**基线故障率 2/12 ≈ 16.7%**（小样本，95% 区间约 2%–48%）。两次故障都发生在开局 68–69 秒，比早前记录的 47／55／143 秒更早且更集中，可作为一个待查的复现特征。

方法学修正：此前把“一段时间没复现”当作环境已稳定是错的。间歇性故障必须用固定局数的连续浸泡来量化，单次或少数几次通过不能作为稳定性证据。

证据：`.work/soak-20261122/`（含 `soak-summary.json`）。

## A/B 对照：未修正 vs 窄修正（22:32–23:09）

同一副本、同一 120 Hz、同线程模式、同 12 局，唯一差异是窄屏障是否注入。窄组每局日志均确认 `bufferBarrierMode=1`、`bufferBarrierStatus=0`，探针真实生效。

| 组 | 局数 | 故障 | 故障率 |
| --- | --- | --- | --- |
| 未修正（基线） | 12 | 2 | 16.7% |
| 窄修正 | 12 | 0 | 0% |

两次基线故障都在开局 68–69 秒；窄组 12 局全部完整跑完谱面（每局约 168 秒）。

### 统计判读

Fisher 精确检验单侧 p ≈ 0.24（24 局中 2 次故障全部落在基线组的概率 = C(12,2)/C(24,2) ≈ 0.239）。**这个样本量下差异不具统计显著性**，只能说方向与假设一致，不能据此宣称修复有效。

要把 16.7% 与 0% 的差异检验到 p<0.05，需要更大样本：若真实故障率确为 1/6 与 0，大约每组 30–40 局才能稳定区分。

### 目前可以负责地说的结论

1. 索引/顶点缓冲可见性缺口是**真实存在**的 Vulkan 规范违规，在源捕获、独立回放、进程内校验、原始未修改游戏路径中一致出现，并可由窄修正精确消除。
2. 该缺口与设备丢失**方向一致但因果未证实**：12/12 对 2/12 的差异在统计上不显著。
3. 因此**不应**把窄屏障作为生产修复提交。生产分支 `fix/vulkan-buffer-visibility-sync` 保持空白，仅作为后续承载点。
4. 若要继续，最有价值的两件事：把样本量提到每组 30 局以上；以及修好 Vulkan 层以便用约 170 秒/次、约 50% 复现率的回放平台做高通量验证。

## 归属判定：缺口属于 Unity 引擎内部路径（23:23）

用只启动、不导航、不进入谱面的观察（`.work/owner-startuponly-20261122-05`）：

- 后端确认 `Graphices: ... - Vulkan`（需 `-force-vulkan`，该副本默认后端是 Direct3D12）。
- 场景仅 `Init → Title`，`SyntheticVideoProbe` 行数为 **0**，即合成 helper 从未安装。
- 校验日志仍报告 `SYNC-HAZARD-READ-AFTER-WRITE`，`INDEX_READ_BIT` vs `TRANSFER_WRITE_BIT`，缓冲 region 恰为 **936 与 12 字节**，与谱面内捕获到的完全一致。

结论：这两个索引缓冲的 `vkCmdCopyBuffer → vkCmdDrawIndexed` 无内存依赖，**在没有任何游戏内容、也没有我们的合成代码时就已存在**。因此它是 Unity 引擎自身的 UI/渲染内部动态缓冲上传路径产生的，不是 MajdataPlay 的 Mesh 代码，也不是诊断 helper 的产物。

由此得到的取舍：

- **C# 侧无法修复**：该命令流由引擎产生，MajdataPlay 没有插入屏障的接口。
- **钩子是唯一能在运行时"补"的手段**，但要为引擎的缺陷承担全局 Vulkan 拦截的跨平台与层级冲突风险。
- **正确路径是上报 Unity**（附捕获与校验日志），生产侧默认不做改动。

已知限制：本判定证明缺口在引擎内部路径产生，但不能证明它导致 TDR；归属与因果是两个独立问题。
