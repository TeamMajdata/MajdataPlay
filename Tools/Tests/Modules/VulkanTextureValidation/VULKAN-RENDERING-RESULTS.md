# Vulkan 渲染路径续查

日期：2026-11-22。延续 [上一轮 FFmpeg 排查](<RESULTS.md>)。原Build、运行源码及用户既有修改保持不变，所有运行配置变化仅在`.work/MainSynthetic`。

**当前最强线索是“持续更新的纹理同时用于原RawImage绘制”的交互。** 匹配144Hz、同一辅助库、双纹理分配、配置及144循环/60上传条件下，改为显示另一张固定纹理的三局均完整；恢复显示更新目标后约104.5秒再次发生驱动153及窗口不响应。它支持优先研究Unity/Vulkan资源更新与绘制/驱动/捕获交互，尚未证明具体GPU竞态或单方责任。

关闭Graphics Jobs、实际移除原生pacing、关闭MRP Native RenderPass分别仍有故障，均不是可靠修复；静态首帧及普通持续上传也都曾连续正常，必须保留偶发性。已在无FFmpeg媒体会话/原生库/桥接条件复现，不应重新把解码器列为必要原因。

17时后的补充验证：OBS实际缺失时普通sampled路径第二局仍故障；退役槽1×1回读反馈调度有效运行但约58.8秒故障；进一步通过Unity实际graphics queue提交空批次+真实VkFence、仅成功完成后复用的调度首局完整，第二局约44.1秒仍故障。这些结果不支持把本机问题修成“多缓存几帧”或把现有等待方案当生产修复；真实同队列标记比区域回读强，但是否覆盖纹理最后实际采样、其他队列/后续使用仍需命令时序证据。当前结论是触发关联与非必要条件已收窄，最终责任点尚未定位；临时后端选择仍以已测试D3D12为主。

## 本轮基线与判据

基线副本已经绕过所有FFmpeg视频准备、移走原生桥接，以原Game RawImage/材质接收854×480合成纹理；保留MRP、Live2D、谱面、音频。原生FFmpeg库不加载的Vulkan条件曾在Game约10.8秒后记录nvlddmkm153，并在额外观察后窗口不响应；同副本D3D12完整到Result约167.5秒。

- Core合成入口SHA256：84BFFF325022DE2E86CE2B5757FBEF9BDB159C70FBAEE36DD9CEB2525AB423FF。
- 关闭视频准备后的FFmpeg托管DLL SHA256：1505F81968C01E3D91172AC14213AB1FACCA7A3654541FA629E0112D19EDF6B5。
- 实际Mono helper SHA256：596213271B8E911CAF2FD89D1B019B7E3C59D1BD3CB2EA12798D5416B9D852F8。
- 基线boot.config SHA256：2C8AAA2BE984C23A79EAB97D185D48D04A9241413ED2D61E4FC616797181E8FC，`gfx-enable-gfx-jobs=1`、`gfx-threading-mode=6`，实际引擎日志为`kGfxThreadingModeSplitJobs`。

主机仍为RTX2060SUPER、驱动610.88、Unity6000.3.17f1、Windows x64 Mono、实际1080×1440@约120Hz。Event153是系统驱动日志，不能单独定位进程/具体GPU engine；驱动事件后的Alive/Responding另行记录，进程被测试清理不计自然崩溃。阴性样本必须完整到Result且实际帧源/后端/线程模式符合。

## Graphics Jobs 对照

当前UnityPlayer二进制未发现旧说明中`gfx-disable-graphics-jobs`字符串，不假设该开关会生效。采用诊断副本`boot.config`的明确配置，并保留原字节备份`boot.graphicsjobs-baseline.config`。

普通渲染线程条件将两项引擎配置设为`gfx-enable-gfx-jobs=0`、`gfx-threading-mode=1`，其他boot项目和游戏设置不变。运行器增加`ExpectedThreadingMode`判据、每轮boot字节快照及前后哈希，实际模式不匹配即停止无效实验。

`vulkan-threaded-20261122-01`三局实际模式均为`kGfxThreadingModeThreaded`，boot前后哈希均为1B5338CCC75C466755CA4F470BB5BF8F3943B38165E47F2E78EDD6D8C737A61C。前两局各约167.4秒到Result；第三局Game01:20:43.7434后，01:23:05.6798858记录nvlddmkm153（70687），距Game约141.94秒，额外观察后Alive=true、Responding=false。故障前稳态约120fps/60次上传每秒。关闭Graphics Jobs改善了这一负载的循环频率，但不能作为消除驱动错误的修复，NativeGraphicsJobs不是该故障必要条件。

随后已按原字节及哈希恢复SplitJobs boot，才进入下一个插件对照，未同时关闭两者。

## 剩余原生 pacing 审查

托管`Rendering.VulkanLowLatencyVSync=false`只提前返回C#帧控制器；预加载原生插件没有启停接口，仍可改变设备、交换链和呈现。

- DLL `.meta`为isPreloaded=1，native UnityPluginLoad无条件注册初始化拦截。
- NVIDIA条件满足时仍协商VK_NV_low_latency2/timeline，交换链启用latency mode并调用vkSetLatencySleepModeNV，present仍编号/替换ID并发标记。
- 从启动即false、没有frame_begin调用时，pendingFrames为空，QueueSubmit原样转发，不应声称它一定加submission ID或执行latency sleep。
- 静态审查未证明当前false路径存在确定GPU同步/生命周期错误。查询resolver自身时未保持包装链是一项条件性缺口，但尚未证实实际调用路径，不作为TDR根因。
- x64 pacing DLL SHA256：4B47E1899E479427932C3A8D2A800B82D28E762C49E49250567E31D9C87E2F27，与现有构建清单匹配。

`vulkan-no-pacing-20261122-01`已在副本实际移走x64 pacing DLL，保留globalgamemanagers预加载记录和原SplitJobs boot；模块快照中无pacing/FFmpeg桥接/原生解码库，OBS/NVIDIA代理仍在。首局约167.4秒完整，第二局Game01:29:03.7582（运行器观测）后，01:29:57.1617055记录153（70688），约53.4秒，额外观察后Alive=true/Responding=false。故障前约120fps/60次上传每秒。移除插件改善了该负载的循环频率，但它也不是这类故障必要条件；不能据此推出NVIDIA/OBS或MRP已经排除。原DLL已保存在副本patch-evidence，原Build未改。

## MRP / UI / Live2D 审查

当前Game PV是BGManager的RawImage，Background子Canvas最终继承ScreenSpaceCamera根Canvas配置，不能仅凭子对象序列化项称其有效WorldSpace。Cubism扩展源码使用RenderSimple，非完整离屏合成路径；但Game全部Prefab、Single场景切换及模型实例来源审查没有证实Game存在活跃Cubism模型。此前“保留Live2D”仅表示保留原程序集/场景结构，不能当作游戏稳态有64次模型DrawMesh的证据。优先级因此先放在线程模式、Native RenderPass与RawImage持续Apply；只把List→Game模型销毁边界作为条件性关注点。

Game场景中UiPvBackdropBlur被显式禁用，不能把保存的Gaussian选项当成每帧模糊RT采集已在运行。Native RenderPass与Native Graphics Jobs是不同变量，不能相互替代。

MRP、RawSprite和实际可能参与的Cubism路径的静态审查均未证实与本次故障对应的资源提前释放/悬空GPU指针。

## Native RenderPass 独立对照

在刚复现故障的“无视频/无桥接/无原生pacing、SplitJobs”条件上，仅关闭MRP原生附件优化：实际MajdataPlay.MajRenderPipe.dll中MajRenderPipeline构造赋给_optimizeAttachments的参数加载改为false。全192方法复读，只有该构造的一条规范化指令变化（index21：ldarg.s optimizeAttachments→ldc.i4.0、Operand=null），引用、locals/EH/max-stack保持；不是改资产默认值，也不是把Intermediate输出误当作原生Pass关闭。

原MRP/source/backup SHA256为15B94D4B9C5E0E76D0522401C8F9CB96BD5A21055B08238040296B67F68B6D64，候选及已应用副本为7F30CBC5C3945FC03EEBD4DA2DF9FDEA2F5024FD56DE1FA77C4AA81051C5DF60。Core/FFmpeg/helper及原Build不变，唯一备份/candidate/新manifest保存在patch-evidence；只读预检与离线工具编译0警告0错误后，由父代理应用。

`vulkan-no-native-pass-20261122-01`第一局即复现：Game01:33:41.9131，01:34:39.2927778记录153（70689），约57.38秒；之后Alive=true/Responding=false。实际SplitJobs、boot哈希及两份设置前后相同，故障前稳态120fps/60次上传每秒。故Native RenderPass/附件优化也不是当前故障必要条件，不把其关闭称为修复。

## 首帧静态纹理控制

为了直接区分持续Apply和静态纹理绘制，helper新增只在Install读取的子进程MAJ_SYNTHETIC_STATIC开关。Static和Dynamic使用同一新DLL，静态模式保留完全相同RGBA32 854×480纹理、RawImage/材质/缩放和每帧统计回调，只跳过初始化之后的像素修改和Upload；不隐藏背景、不换图片、不改其他场景渲染。

新helper SHA256=5F9D9EFEAA7295C90FD8FF582401B9113A9FF583E864D6CBC4533B65D2500C80，程序集identity保持1.0.0.0，按实际Mono BCL引用独立编译0警告/0错误；原helper唯一备份和新manifest留在MainSynthetic。运行器记录子进程开关并恢复调用方环境，要求INSTALL static=True、PROGRESS持续出现且累计uploads始终1，才确认静态控制成立。

`vulkan-static-frame-20261122-01`首局约167.5秒完整，static=True且累计uploads始终1，无新增驱动事件；第2轮前台校验失败、未进入Game，不计稳定性样本。随后有超过13小时的会话间隔，下午的`vulkan-static-frame-20261122-02`也因前台激活失败、0按键而被排除。已增加只针对核对PID窗口的备用激活，临时输入队列在finally解除；进入Game后停止焦点操作。

下午`vulkan-static-frame-20261122-03`两局均完整到Result、无驱动事件，static=True/累计uploads=1条件通过；两份设置及boot前后SHA保持。连同凌晨首局累计3个有效静态完整样本，但不能合并为同一连续环境的3局。凌晨静态首局结束保存配置时FPSLimit由144变150，VSync仍开；已保留该配置快照，并在下午有效重测前恢复精确基线A89F6B...B703，未将有差异的轮次冒充严格单变量。

下午显卡WMI报告2560×1440/144Hz；游戏日志交换链仍为1080×1440，静态实测约144fps，区别于凌晨约120Hz/120fps。后续因果比较优先同一下午的相邻样本，不假定显示环境一直不变。

`vulkan-dynamic-return-20261122-01`用同一helper DLL、boot、设置和相同无pacing/关闭NativePass条件恢复持续Upload（MAJ_SYNTHETIC_STATIC=0），三局全部完整到Result，无新增驱动事件，实测约120fps/60次上传每秒。设置和boot前后哈希保持。因此当前没有形成静态/动态故障回归，不能称停止Apply已经修复或持续Apply单独足以触发故障。

## 上传目标与显示资源分离

新增MAJ_SYNTHETIC_DECOUPLE进程开关，采用同一helper DLL、两种模式都分配并一次初始化两张完全相同配置的854×480纹理。持续Upload始终写s_texture；普通模式RawImage固定显示s_texture，分离模式从初始化起固定显示另一张不再更新的s_displayTexture。两模式像素生成、deadline、Canvas相位和资源数量匹配，未使用未经fence证明的固定帧数ring。

日志增加displayInitialUploads=1、uploadTextureDisplayed及逐帧CPU对象bindingChecks/bindingMismatches；运行器核对绑定目标符合模式且mismatches始终0，不把它们叫作GPU采样计数。unused纹理的Apply不代表已观测到GPUcopy实际执行；即使分离条件正常，也只能收窄到可见资源更新/共同渲染条件，不直接证明GPU读写竞态。

helper初版经独立SDK9.0.200编译0警告0错误，五个实际Mono/Unity引用metadata身份一致；lead补充实际Player refresh/window/VSync/targetFrameRate日志后复编译并安装SHA256=2AB57708DB50238566D844F5AB663AEADD7FD6A6EA64DAD8F6F61F9E49F009F5。旧单纹理helper已唯一备份，源Build不改。

`vulkan-paired-sampled-20261122-01`首局即复现：Game运行器观测15:26:44.1781；15:27:31.4159开始7条GPUIDa00错误，随后15:27:33.612至34.272明确记录“Resetting TDR occurred / Reset TDR occurred / Restarting TDR occurred”（153总10条，记录70830–70839）。随后Alive=true/Responding=false。INSTALL decoupled=False、uploadTextureDisplayed=True，持续上传和bindingMismatches=0均成立；boot和设置哈希未变。此次事件文本直接支持驱动执行了TDR重置流程，比之前只有“Error occurred”的153更具体；仍不能据此定位具体engine或错误命令。限定时间窗无相关Application WER/Display4101记录，LiveKernelReports目录因系统访问权限不可读，本轮未读取dump。

`vulkan-paired-decoupled-20261122-01`用同一DLL切换分离条件，INSTALL decoupled=True/uploadTextureDisplayed=False，持续60次/秒、绑定检查0偏差，但约67秒后返回List而非Result，归类left-game-before-result，不能计完整通过，也无驱动事件。日志不能确定退出输入来源。测试副本先前TrackSkip/FastRetry开启，之后仅在副本统一关TrackSkip/FastRetry/FastPractice、GameplaySubScreenClickBehavior=None并保留原settings备份；原Build设置不变。后续两模式均使用这份shortcut控制，须与早先sampled样本的普通settings哈希区别说明。

`vulkan-paired-decoupled-20261122-02`按统一shortcut配置重跑，三局均完整Game→Result、无驱动事件，分别累计9983/9981/9981次上传，bindingMismatches始终0。新日志记录Unity的Screen.currentResolution刷新率：这三局均144.000Hz、稳态约144循环/60上传、窗口1080×1440。随后`vulkan-paired-sampled-return-20261122-01`仅恢复显示上传目标，首局Game日志15:41:18.1563，首条153在15:43:02.704302，约104.548秒后故障，后续Alive=true/Responding=false；6条153是同一故障观测，非6次独立复现。恢复sampled的Unity报告也是144Hz、同144循环/60上传、零绑定失配，三份已记录配置哈希与分离三局完全相同。因而先前120Hz sampled样本的刷新率混杂，在这次144Hz反向比较中已明显收窄；先前120Hz的明确TDR重置序列仍单列，不把其文本外推给本次只有Error occurred的153。

## 阶段结论与限制

在本机及当前完整场景副本中，**持续更新的纹理是否同时供原RawImage绘制，与故障出现呈较强关联**：分离显示资源时三局完整，恢复显示更新资源后在匹配144Hz/同配置条件再次出现驱动错误及窗口不响应。比单纯关闭FFmpeg、隐藏背景、换小视频或观察CPU上传耗时更有判别力。当前重点转向Unity/Vulkan动态纹理更新与Camera Canvas/RawImage绘制、NVIDIA驱动和剩余捕获层的交互。

仍未证明具体GPU race/非法内存读写或单方责任；未使用纹理可能走不同的延迟上传路径，Apply返回不证明两条件实际GPUcopy完全一致；CPU绑定检查不等于GPU采样完成。样本量有限且固定顺序，驱动重置/缓存状态及同GPU其他进程等仍未完全随机化；三局正常不是永久稳定性保证。无媒体会话、无FFmpeg桥接与原生pacing的条件已复现，所以它们不应重新成为首要必要原因。

Graphics Jobs、原生pacing与Native RenderPass分别关闭仍有正例，说明这些开关各自都不是必要条件；静态首帧三局与持续上传回开三局均曾正常，不能把任一阴性阶段单独称作修复。SRP/Shader的定向源码审查未证实背景材质实例索引/CB布局越界：材质本来不启用instancing，普通亮度读取无实例数组；未用静态的half或flexible-array占位大小认定bug。当前Game没有活跃Live2D模型的源码证据，稳态模型网格不是优先项。

临时使用仍优先既有D3D12验证路径；本轮的分离控制显示静态图像，不能直接作为正常视频播放修复发布。要做同步型资源复用实验，必须先验证实际Player保留GPU完成接口，并确保fence覆盖纹理最后一次真实绘制使用、通过后才复用；固定双/三帧ring不能替代该证据。源码播放器原有软件上传位于Update，已有故障正例，故也不能只因诊断helper在Canvas阶段上传就认定Canvas调用相位是唯一原因。

## GPU完成标记接口边界

只读Cecil检查原Build与诊断副本实际UnityEngine.CoreModule.dll字节一致（SHA256 F44AEF84B7A5CF5DF9F4D60C10509B0956F3715191C4755E28FFA34916B2092A）。GraphicsFence类型保留，但公开get_passed缺失；CommandBuffer.CreateGraphicsFence(GraphicsFenceType,SynchronisationStageFlags)、Graphics.ExecuteCommandBuffer、SystemInfo.supportsGraphicsFence存在。Graphics静态CreateGraphicsFence/WaitOnAsyncGraphicsFence不存在；RenderPipelineManager.endContextRendering公开add/remove保留，endFrameRendering公开accessor缺失。

所以当前裁剪Player不能直接实现以常规公开fence.passed为判据的安全资源复用。只保留创建接口并不足以证明GPU最后使用完成；没有写私有Unity事件、没有调用未知internal原生句柄来绕过，也没有把固定2/3帧ring称为修复。后续同步验证需正确版本的隔离Player构建保留所需API，并核对fence实际覆盖最后一次背景绘制使用。

### 退役纹理回读反馈调度

进一步只读确认当前Player保留CommandBuffer.RequestAsyncReadback(Texture,int mip,int x,int width,int y,int height,int z,int depth,Action<AsyncGPUReadbackRequest>)、回调request.done/hasError及SystemInfo.supportsAsyncGPUReadback；静态AsyncGPUReadback.Request(Texture)已裁剪，禁止直接引用Editor完整API。MRP源码的相机兼容分支在finally执行buffer+context.Submit，管线EndContextRendering在全部相机返回之后；这是CPU提交顺序事实，Submit本身不是GPU完成等待。

据此准备独立可选ReadbackGate调度控制：有界三槽，当前显示纹理不Apply；Canvas选择非当前且Available槽上传/绑定，旧显示槽退役；只在含实际背景rootCanvas.worldCamera的endContext回调，为退役槽排入1×1区域回读。回调仅原子发布done且无error的结果，主线程消费成功反馈后才允许复用；无可用槽则跳采样并记录backpressure。错误不放行，当前槽不提前拿“完成”证明，关闭会话先解除消费者并保留尚未完成的三槽资源，禁止新安装累积池。

这是“相机提交后区域回读反馈”实验，不是真正last-use fence证明。1×1回读完成只覆盖该请求，不保证此前所有只读采样或跨队列工作已结束；CPU对象解绑和相机列表匹配也不能完全证明引擎渲染快照已更新。可能影响负载/时相/资源数，故不将结果直接解释为原竞态已确认或生产修复。

已审核单Session有界状态机及全部清理路径，实际Player公开Canvas.rootCanvas/worldCamera/get_renderMode存在，筛选缓存的真实Main Camera，未借用Editor完整DLL或私有事件。独立SDK9.0.200 Helper编译0警告0错误，程序集身份1.0.0.0和实际5个Mono/Unity引用保持；安装后的helper SHA256=11BC52BA1829F095338FC56812D6FF7D7546CA4CC747189D1A2B676A95E5E6D7。旧2AB577...09F5已唯一备份。若场景卸载销毁旧camera而再无匹配context，最后显示槽保持Retired并最多保留该3槽到进程退出；拒绝新池，不声称一定能发出CLEANUP_COMPLETE。

`vulkan-readback-gate-20261122-01`有效运行仍复现：Game17:18:36.5057，INSTALL17:18:37.2011；17:19:35.3286655开始3条153（70846–70848），距Game约58.823秒，之后Alive=true/Responding=false，无VK_ERROR_DEVICE_LOST或helper FAIL。最后故障前PROGRESS在17:19:28.2251为readbackRequests3045/readbackCompleted3044/reuseAfterAck3043，readbackErrors、illegalReuse、unexpectedCurrentMarker、bindingMismatches均0，初始backpressure3、pendingPeak2；实际144循环/60上传，contextCameraMatched持续增加。运行器ReadbackGateConfirmed=true，六项产物状态/boot/两设置哈希前后相同。

这说明该有界回读反馈调度实际工作，但它没有消除故障，不能作为修复发布。也不能据此说所有同步因素已排除：区域回读不是真正覆盖全部最后使用的fence，跨队列/只读重叠及引擎渲染快照尚未通过GPU trace验证。本次3条驱动日志属一次故障观测，不计独立3次。

`vulkan-readback-gate-no-obs-20261122-01`仅在同Gate副本设置子进程DISABLE_VULKAN_OBS_CAPTURE=1，模块快照确认graphics-hook64.dll、两原生桥/pacing与FFmpeg解码库均缺失，NVIDIA nvspcap64仍在。Game17:20:45.7225→首条15317:20:58.1339853，约12.411秒，4条属于一次故障，随后Alive=true/Responding=false。ReadbackGateConfirmed仍true，最后记录requests465/completed464/reuseAfterAck464、errors0/illegal0/bindingMismatch0；实测约95循环/40上传，不能称仍达到60目标，也不能把更快故障认定OBS关闭使故障恶化。两份设置及全部关键产物哈希一致。至少OBS钩子不是此类故障必要条件，不能以此前“关后三局正常”宣称已修复；NVIDIA代理及其他剩余链路仍未完全排除。

`vulkan-sampled-no-obs-20261122-01`显式关闭新增Gate（同辅助DLL正常双纹理sampled路径），保持OBS缺失。首局完整，第二局Game17:25:28.0888→首条15317:26:23.3835779，约55.295秒，4条属于一次故障；随后Alive=true/Responding=false。ReadbackGate=false、正常FrameSource/Binding验证成立、Unity报告144Hz/稳态144循环60上传、0绑定失配；模块无OBS/FFmpeg原生库/原桥接/原pacing，只保留NVIDIA代理等剩余模块。关键二进制/两份设置/boot前后不变。因此普通更新并显示的故障也不需要OBS，不是只有新增回读调度才会失败；仍未独立移除或排除NVIDIA代理。

### 原生图形队列完成标记探针

为弥补1×1回读只覆盖请求本身的边界，准备仅诊断副本加载的原生探针：通过Unity render event与AccessQueue(flush=true)进入受Unity外部同步的图形队列回调，提交空VkSubmitInfo并附真实VkFence，主线程只查询该fence；资源退役后需成功查询并成功释放票据才复用。无loader/设备/交换链/present拦截，不添加低延迟或其他扩展。票据单调token+固定上限，Created取消后的晚到事件只查token，Queued/Pending不能释放；设备丢失/旧generation不授权纹理复用，shutdown不硬等且对未知inflight资源有界保留。

为复用该已构建副本现有native-loader预加载记录，新诊断二进制采用旧文件名MajdataPlayVulkanPacing.dll；它不是原pacing插件，必须以新源/导出/哈希清单区分，不能仅凭模块文件名声称原pacing恢复。原Build/Assets里的原DLL保持不变。真正fence成功证明相应同图形队列先前提交工作的完成，但仍需核对事件确实位于纹理最后实际使用之后；CPU绑定快照/多相机跨队列覆盖不能省略。

探针最终DLL SHA256=A8827B4468A2A4A1CEA534D362406016BCE0D0FE14C4A71131D402751961BCDF，仅9个导出（UnityPluginLoad/Unload及7个mf_*），无原majvk_*，无loader/WSI/扩展拦截。MSVC x64 C++17 /W4 /WX /MT编译0警告/0错误，纯CPU假driver状态测试通过，覆盖取消后晚到事件、queued/pending不释放、成功完成、容量限制、旧generation与shutdown拒绝driver查询，以及poll成功后释放前device-lost必须负值而不授权复用。源码/头文件/导出/工具链清单保存在native/.build/build-manifest.json，测试不冒充GPU验证。

managed复用同一有界Session，Cdecl/IntPtr ABI只在退役槽创建票据，再经CommandBuffer.IssuePluginEventAndData/Graphics.ExecuteCommandBuffer执行；成功poll且成功release后才Available，negative/拒绝保留隔离。Helper SHA256=269781F9A748152003F07B267C379CBA002CE7A51DE715AD9429D555FDEB5313，identity1.0.0.0、原5个实际Mono/Unity引用保持，0警告/0错误。已审核设备丢失释放不放行、token非解引用指针及未完成保留的边界，独立只读复核未发现pending/错误票据误当成功复用的确定缺陷。插件正常Unity render/device事件排序是前提，不声称支持任意并发interface销毁或生产级shutdown全清理。

`vulkan-native-queue-fence-20261122-01`在OBS缺失、原媒体/桥接/pacing缺失、NativePassoff、SplitJobs条件下实际生效：首局Game17:44:13.2342→Result17:47:00.6194，约167.385秒完整，通过9986次队列marker完成，累计9985次动态上传，CLEANUP_COMPLETE也被观测到。第二局Game17:47:20.4706→首条15317:48:04.5907303，约44.120秒后故障，4条是一次观测，之后Alive=true/Responding=false，无helper FAIL/VK_ERROR_DEVICE_LOST日志。NativeQueueFenceConfirmed=true，实际144Hz/144循环60上传，全部设置/boot/关键产物前后哈希一致。最后一次常规故障前采样17:47:52.1675有1847次请求/完成、1846次复用、0marker错误/illegal/绑定失配；事件后约19ms的进度17:48:04.6101为2350次完成/2349次复用，maxMarkerPollLatency约4074.8ms，不能把这个CPU轮询时差当作GPU执行时间或声称事件时间CPU立即停止。

**按此真实Unity图形队列完成作用域等待，仍未消除故障，不能把该方案作为修复。** 它比区域回读提供更强的同队列完成反馈，但尚未trace证明所有原RawImage实际采样/渲染快照都在相应marker之前，也不覆盖其他队列或后续使用。因此失败不能反向证明所有同步问题均已排除；此前显示更新资源的关联仍成立，具体责任点仍需GPU命令/故障信息或固定应用条件下的驱动分支对照。

`d3d12-paired-sampled-20261122-01`已用同一最新版helper、普通settings与boot，保持持续上传目标被显示，只切D3D12；两局均完整到Result，0驱动事件，CPU绑定无失配，Unity报告144Hz、稳态约144循环/60上传。它支持当前后端临时规避及实验副本正常完成能力，不是任意驱动/设备/长时场景的稳定性保证；后端改变也改变了相关捕获层/驱动路径。

本轮助手/Patcher离线编译成功、五个PowerShell脚本语法检查与git diff --check通过；原Build Core/FFmpeg/MRP与两原生插件、两份原配置哈希均保持，既有字体/ProjectSettings改动未触碰。没有修改生产播放器/渲染源码、子模块或主资产。所有诊断Player与相关GPU作业已结束，下一步只读检查真实GPU完成反馈途径及其在纹理最后使用之后的覆盖，不以固定帧数ring推断安全。

## 下一项取证：完整初始化捕获

已读取本机GFXReconstruct 1.0.5层manifest及旧`gc3-info.txt`：旧记录是trimmed frame range1200–6600，且旧回放在外部共享句柄属性查询失败，不能据此证明最后present就是错误源。当前副本没有FFmpeg外部共享句柄，适合重新尝试从startup捕获全部初始化和后续故障。

准备使用本机既有SDK捕获层，清除帧范围/触发器继承配置，FILE_TIMESTAMP=false、LZ4、逐包flush；不改系统注册表或全局环境。只启动一份Attempts=1的已有run-main子wrapper，按ParentProcessId+精确路径+creationtime+MAIN START交叉核对自己的Player，计划soft cap6GB、空闲保留30GB、总时限360秒；容量轮询可能短时超出，不称硬上限。若达限先停止自身wrapper防新spawn，再仅终止已核实直接child。抓取和回放会改变调度，因此无故障、文件未生成或层未加载均不能当作故障排除；抓取范围/层实际激活及可回放性需另验证。当前尚未执行新的GPU捕获或回放。

### 关键证据入口

- [普通渲染线程故障](<.work/vulkan-threaded-20261122-01/attempt-3/result.json>)。
- [原生pacing移除后的故障](<.work/vulkan-no-pacing-20261122-01/attempt-2/result.json>)。
- [Native RenderPass关闭后的故障](<.work/vulkan-no-native-pass-20261122-01/attempt-1/result.json>)。
- [下午静态帧完整样本](<.work/vulkan-static-frame-20261122-03/results.json>)与[持续上传回开三局](<.work/vulkan-dynamic-return-20261122-01/results.json>)。
- [双纹理sampled故障](<.work/vulkan-paired-sampled-20261122-01/attempt-1/result.json>)及[明确TDR重置事件序列](<.work/vulkan-paired-sampled-20261122-01/attempt-1/driver-events.xml>)。
- [分离资源三局完整](<.work/vulkan-paired-decoupled-20261122-02/results.json>)、[首局绑定与频率日志](<.work/vulkan-paired-decoupled-20261122-02/attempt-1/Player.log>)。
- [144Hz恢复sampled后故障](<.work/vulkan-paired-sampled-return-20261122-01/attempt-1/result.json>)、[日志](<.work/vulkan-paired-sampled-return-20261122-01/attempt-1/Player.log>)和[驱动事件](<.work/vulkan-paired-sampled-return-20261122-01/attempt-1/driver-events.xml>)。
- [同版sampled模式D3D12两局控制](<.work/d3d12-paired-sampled-20261122-01/results.json>)。
- [回读反馈调度有效但故障](<.work/vulkan-readback-gate-20261122-01/attempt-1/result.json>)与[OBS移除后的同调度故障](<.work/vulkan-readback-gate-no-obs-20261122-01/attempt-1/result.json>)。
- [无OBS普通显示上传路径的故障](<.work/vulkan-sampled-no-obs-20261122-01/attempt-2/result.json>)及[模块快照](<.work/vulkan-sampled-no-obs-20261122-01/attempt-2/modules.csv>)。
- [真实队列fence调度两局结果](<.work/vulkan-native-queue-fence-20261122-01/results.json>)、[第二局marker进度](<.work/vulkan-native-queue-fence-20261122-01/attempt-2/Player.log>)与[驱动事件](<.work/vulkan-native-queue-fence-20261122-01/attempt-2/driver-events.xml>)。
- [原生探针代码/构建及边界](<SyntheticFrameProbe/native/README.md>)、[新二进制来源清单](<SyntheticFrameProbe/native/.build/build-manifest.json>)及[安装身份记录](<.work/MainSynthetic/native-queue-marker-installed-20261122.json>)。
- [本轮完成反馈实验汇总表](<.work/completion-controls-summary-20261122.csv>)，DriverEventMessages是同一故障的消息数而非独立复现数。
- [原始结果自动汇总](<.work/rendering-summary-20261122-final.json>)，不含被排除轮次的通过推断。
