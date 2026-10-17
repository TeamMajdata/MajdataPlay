# Android MSAA 屏幕 Resolve 调查与修复

更新日期：2026-10-17。本文前半部分真机矩阵采样于 2026-10-16，使用旧版完整 viewport：显式显示缓冲句柄修复只在 Android Player / Vulkan 的直接屏幕 Resolve 分支中，每帧使用 `Display.main.colorBuffer`，当时未新增单采样颜色 RT / FinalCopy。该次调查结束时六份预设均关闭直接 Resolve，属于历史配置；之后独立渲染菜单覆盖运行参数。2026-10-17 前一轮根据手机偏移回归收紧直出资格并重做 Copy 矩形定位，本轮再取消主相机的长屏自动 viewport 偏移，详见“当前实现”及分开的两轮验证记录；没有将旧真机矩阵或 RT 代屏幕测试计为本轮手机通过。菜单设置见 [README](../README.md#游戏设置与运行时覆盖)。

## 历史真机证据与边界（2026-10-16）

前一轮已在 Xiaomi Pad 5 Pro（elish / Adreno 650、Vulkan 1.1.0、驱动 512.502.0、Gamma、1600x2560）复现并缩小原黑屏问题。该设备不是此前报告的 Pad 6S Pro，不能把这组历史实测写成 Pad 6S Pro 或 Xiaomi 15 已回归通过，也不覆盖本次手机长屏偏移修复。

主游戏原构建的 Medium 实际 2x、High 实际 4x，在 100% 且 `directResolve=True` 时黑屏或显示残留画面；同进程同档改为 90% 后，1440x2304 场景颜色恢复，`directResolve=False`、`intermediateColor=True`，实际 MSAA 与颜色 / 深度 memoryless 标记均保留。所有附件均 `prepared=True`，未报告 Begin / EndRenderPass 错误。这排除了“设备完全不支持这些 MSAA / memoryless 附件”的解释，但缩放同时改变尺寸，单靠该对照不能锁定唯一原因。

随后在同设备、同 1600x2560 输出的独立 Android Probe 中，只替换 Resolve 目标做了对照：

| 模式 | 路径 | 观察 |
| --- | --- | --- |
| 0 | 1x 原生 Pass 直接绘制屏幕 | 正常，作为方向和动态图形基线。 |
| 1 | 4x 原生 Resolve 到普通 1x RT，再全屏复制 | 能输出新画面；ProbeCopy 尚未校正方向，有翻转，只作为输出可用性对照。 |
| 2 | 自建 MSAA RT，仅将 `CameraTarget` 用作 Resolve 目标 | 没有显示新模式图案，保留旧画面；不能把残留帧误认成当前模式正常。 |
| 6 | 相同附件，每帧以 `new RenderTargetIdentifier(Display.main.colorBuffer)` 作为 Resolve 目标 | 正常显示新模式 6 图案，方向与模式 0 相同，动态图条持续变化；冷启动也正常。 |

模式 6 的 2x / 4x 请求分别取得实际 2x / 4x，8x 请求由设备降为实际 4x；系统屏幕请求保持 1x，颜色为 `RenderTextureMemoryless.MSAA`、深度为 `Depth`，不使用单采样中间颜色 RT 或 FinalCopy。实际样本数由日志核验，不能把 8x 请求写成硬件实际 8x。

独立 Probe 已确认这个句柄替换在该设备有效，证据位于工程外 `mrp-direct-resolve-repair` 的 `probe-00-*`、`probe-01-copy.*`、`probe-02-*` 与 `probe-06-*` 日志、配置及截图。

随后将当时生产 MRP Runtime 接入独立 Android Fixture，在同设备的 1600x2560、Gamma、100% / 实际 4x、完整 viewport 下恢复了直接屏幕输出。日志确认 `directResolve=True`、`intermediateColor=False`、`resolveTarget=DisplayColorBuffer`，颜色 / 深度 memoryless 标记保留。普通几何、A2C、深度前后覆盖、Stencil Equal 形状、真实 UGUI World / Camera Canvas、Overlay 半透明 Panel 与 UGUI Mask 均能显示。2x 直接输出也已取得有效画面；生产 Runtime 仍用 CameraTarget 作为唯一 Resolve 目标时的全黑对照保留。

生产 Runtime Fixture 的历史补充矩阵如下。程序为独立包名的 Development / IL2CPP / ARM64 构建，使用 Unity 6000.3.17f1；六份 Runtime 源码的 SHA256 与 2026-10-16 主工程快照一致。矩阵使用完整 viewport，验证当时的输出和附件路径，不覆盖本次长屏平移分支，也不用于比较 Release 游戏的功耗。

| 配置 | 实际场景附件 / 输出 | 观察 |
| --- | --- | --- |
| 100% / 1x | 直接屏幕绘制 | 几何与三种 Canvas 正常。 |
| 100% / 2x、4x，关闭直接 Resolve | 相同 MSAA + 普通 1x RT + FinalCopy | 正常，保留作为同尺寸参考。 |
| 100% / 2x、4x，开启直接 Resolve | 显式显示缓冲，颜色与深度 memoryless，`intermediateColor=False` | 正常，Overlay Panel 与 UGUI Mask 均可见。 |
| 100% / 请求 8x，开启直接 Resolve | 设备实际 4x | 正常，静态截图与实际 4x 直出完全一致。 |
| 75% / 4x，开启直接 Resolve | 1200x1920 场景颜色，自动使用普通 Resolve RT + FinalCopy | 正常，未强行直出到尺寸不同的屏幕。 |
| 100% / 4x 直出，冷启动与 Home 后恢复 | 每帧重新取得显示缓冲 | 正常；排除动态图条后，与重启前 / 恢复前静态画面分别为零 RGBA 像素差。 |

4x 直接输出与关闭直出的普通 RT + FinalCopy 参考并非逐像素一致：剔除上方动态图区域（y < 140）后，比较 3,872,000 个像素，共 8740 个像素变化（约 0.226%），最大单通道差 62/255，变化局限于旋转白色几何、细线边缘与 A2C 圆；所选 Depth / Stencil / Canvas 区域没有差异。关闭直出和开启直出各自重复的静态区域均为零差异，加入 Overlay / Mask 后的 4x 跨路径差异仍为 8740 / 62。

2x 跨路径共有 9969 个静态像素变化，最大单通道差 128/255，包含少量其它几何边界点；不能把 4x 的所选区域零差异推广到 2x 全部边缘。单独限定实际 Overlay 半透明面板与圆 Mask 的子区域 `[450,2300,1150,2490)`，2x / 4x 均为零像素差。所有统计采用 PNG 原尺寸、零阈值，没有对齐、缩放或放宽阈值。工程外 `pixel-validation.json`、`PixelValidation.md` 和 `pixel-qa/` 保留统计、原图与展示增强差图；`final-validation.json` 记录源码匹配、11 组输出日志及生命周期对照。

白色 255 与背景 9 的 4x 覆盖平均每变一个样本约差 61.5，与观察到的 9 / 70 / 132 / 193 / 255 级相符。屏幕 / RT 方向及其有效子像素覆盖、A2C 随 framebuffer 坐标变化的掩码是可能机制，尚未捕获实际 viewport / sample-location 状态，不能认定其中某项为唯一原因，也没有放宽阈值把差异计作完全一致。

这些是独立 Fixture 的生产 Runtime 和屏幕输出证据，没有重新构建并回归完整主游戏，不覆盖真实视频、Live2D、所有场景初始化或全部 Canvas / Stencil 用法。iOS Metal、用户此前的 Pad 6S Pro / Xiaomi 15 未在本轮重新实测，功耗、GPU 频率和实际带宽也未测量。

截图差异和未经核对归属的 SurfaceFlinger 计数不足以证明完全没有 Present。本轮定位到“仅 Resolve 目标的 CameraTarget 别名接法”与“显式显示缓冲句柄”的差异；Unity 内部的目标解析、同步、Acquire 或呈现标记具体哪一环异常，公开源码和当前证据尚不能区分。不将它归因为已确认的 SoC 功能缺失或某个确定的驱动 Bug。

## 当前实现

`MajCameraAttachments` 只缓存平台 / 图形 API 是否属于 Android Player Vulkan。真正的显示缓冲在每次 `BeginScene` 中重新取得，不缓存 RenderBuffer 跨帧或交换链重建；Editor、iOS Metal 及其它后端保留原来的 CameraTarget Resolve 接法。

直接路径由调用端与 `MajCameraRenderer.CanResolveToScreen` 共同筛选：独立主显示器 Game 相机、100% / MSAA、原生附件路径、完整零原点 viewport、相机逻辑尺寸与物理屏幕尺寸一致、系统多采样缓冲能力、Vulkan 或 iOS Player Metal、Gamma、无需 Display Blit / sRGB Blit、HDR 输出关闭。ScreenPosCameraMover 已取消按长宽比自动添加 y=.12 / .22，初始化统一使用 (0,0,1,1)；长屏本身不再强制走 Copy，100% 且上述条件满足时可保留直接 Resolve、原生 Resolve-only 和支持时的 memoryless。100% / 1x 的 CameraTarget 直绘保留，不按设备或 SoC 名称一刀切禁用。

取消自动上移会改变长屏的默认构图。手动显示设置 MainScreenScale / MainScreenOffset 及保存值仍保留，通过相机 Transform / 正交 Size 和 Canvas 变换调整布局，不改变 rect；它们不是渲染分辨率 RenderScale，不会单独强制 Copy。RenderScale 非 100% 仍需中间颜色与 Copy。MRP 也继续保留非原点 rect 的兜底：其它调用者真正设置 x/y 偏移时不尝试 MSAA 直接屏幕 Resolve，改为 Resolve → 普通 1x RT → FinalCopy，符合条件的原生场景 Pass、Resolve-only、memoryless MSAA 与深度仍保留。

主屏物理尺寸取 `Screen.width / Screen.height`，与相机 `pixelWidth / pixelHeight` 的逻辑尺寸分开。原生附件只有在两者一致且归一化 rect 宽高为 1 时启用；缩放后的场景附件仍按逻辑尺寸计算，不改变 Camera / Canvas / 输入。需要中间颜色的场景先在本地 0～附件宽高绘制，Copy 使用整屏零原点 viewport，交给 `MajFinalCopyMapping` 和 Shader 在裁剪空间应用一次原逻辑 pixelRect。Y 平移按 `GL.GetGPUProjectionMatrix(Matrix4x4.identity, false).m11` 的符号处理，原 Game 源 UV 不变；不手动翻转相机投影。局部 UV 只保留 0～1，配合未覆盖区域的整屏清除，避免越界填充、残留或将图像压入屏幕交集。

宽或高不为 1、逻辑尺寸与物理尺寸不一致、Gizmos 等条件保留兼容附件路径；缩放、强制兼容输出和其它不能直接 Resolve 的情况仍用中间颜色与 Copy，D3D11 / GLES3 等后端筛选不变。图形输出模式是偏好，不保证当前帧一定直出；只读“当前输出路径”显示实际提交分支。上述变化尚不能证明 Unity / 驱动内部的唯一根因，也不是“检测到黑屏后自动回退”。

Android 重建 MSAA 附件时输出一条 `[MRP.Attachments]`，新增 `resolveTarget=DisplayColorBuffer / CameraTarget / IntermediateColor`，其余设备、API、格式、尺寸、请求 / 实际 MSAA、memoryless 与屏幕请求字段保留；稳定配置不新增逐帧日志。`prepared=True` 仍只代表自建资源准备通过，不代表屏幕执行结果已被自动验证。

RT 创建失败、描述不符、memoryless 颜色普通重试等既有回退保持不变。没有新增“检测到黑色即回退”的 GPU 结果检测；RenderBuffer 也没有被当作一个可公开查询全部交换链能力的对象。真实屏幕格式、样本数和目标生命周期的完整核验仍存在边界，不能用不透明句柄非零伪装成可靠能力检查。功耗、GPU 活跃时间和实际外部带宽尚未测量，省去中间 RT / FinalCopy 是渲染流程变化，不是本轮已量化的节能结果。

## 本轮全屏布局调整的验证边界（2026-10-17）

本次取消的是 ScreenPosCameraMover 的自动 viewport 偏移，没有删除 MRP 的偏移输出兜底或修改手动显示位置参数。Windows / Android 的 Editor、Player 四组共 8 个程序集编译通过；工程外将实际 ScreenPosCameraMover.cs 与最小 Unity / DisplayOptions stubs 组合执行，3 组共 739 项检查通过，覆盖 Awake / LateUpdate、延迟设置、旧 .12 / .22 归零、手调开关 / 缩放 / 偏移以及恢复和稳定状态。这些仅验证控制流 / 赋值，不是 Unity 视觉、触摸、Canvas 或屏幕 Present 测试。MRP 渲染逻辑未变，本轮没有重跑下一节保留的 420 次 GPU 捕获；上一轮编译、逻辑 / UI 检查与 GPU 结果不计作本次重新通过，手机实际全屏直接 Resolve 仍待用户回归。

## 前一轮移动偏移修复验证（2026-10-17，历史）

用户随后在手机观察到 MSAA 下移、兼容 Copy 上移和 RenderScale 切换时位置不一致。此前代码将同一 pixelRect 直接用于 CameraTarget、普通 RT 与 Resolve 到显示缓冲的原生 Pass，也未区分相机逻辑输出和物理屏幕尺寸；本轮按上节拆开这些坐标语义。公开 Camera 接口没有提供本轮 Android 设备上超出屏幕的 pixelRect 是否裁切的实测结果，不能把“设备一定裁掉了 pixelHeight”作为已证明原因。

此前 60 次长屏 D3D11 离屏检查属于旧版本：它们以显式 RT 替代屏幕绑定，手动按指定宽高构造 viewport，并采用纹理输出的 UV 方向，没有覆盖 Android 实际 camera.pixelRect / pixelHeight 与 Screen 尺寸、Game 原始 UV、backbuffer viewport / Y 方向或 Present。旧 Pad 5 Pro 真机矩阵又只覆盖完整 viewport。两者都不能替代本次手机验证，也不能排除此次回归。

本轮 Windows / Android 的 Editor、Player 四组共 8 个程序集编译通过；实际新程序集的 2034 项纯逻辑检查、设置 UI 的 1053 项断言通过。隐藏隔离 Editor 在 Windows D3D11 和 Vulkan 各完成 210 次 GPU 捕获，共 420 次：1080×2400 / 1220×2712，viewport y=0 / ±.12 / ±.22，50% / 95% / 100% / 105% / 150%，1x / 4x，覆盖原生附件和普通 Store 的 RT+Copy。每个后端的 100 对原生 / Store 输出全图零阈值一致，100% / 1x Copy 与原 pixelRect 直绘基准全图零阈值一致，留白内侧严格清空；其它缩放的预选梯度位置最大通道差 1/255，在预先保留的 2/255 采样误差内，不能据此称缩放后全图一致。测试使用当前生产附件代码和 FinalCopy Shader，仅把最终 CameraTarget 绑定替换为独立 RT，按纹理目标方向取 GPU 投影符号及源 UV；没有验证 Game 屏幕的原始 UV、真实 Screen / camera.pixelRect getter、Android CameraTarget / Display / Present、主游戏 Canvas 或功耗。手机画面仍由用户后续复测。原始日志、PNG 和对照结果保存在工程外 `mrp-mobile-viewport-repair`，未覆盖旧失败证据。

## 首次调查记录（历史）

最初用户报告 Pad 6S Pro 在 100% 的 Medium / High 等档位整屏黑，Low 可恢复，Xiaomi 15 没有同样现象。当时暂时将 Medium 的直接 Resolve 关闭，用户随后改成 4x，并反馈 Medium 正常、High 黑屏。这是当时构建的对照，不能描述为当前预设配置。

首次静态调查时，本机只发现 MIX 2S，尚无目标平板日志或 GPU 抓帧；该阶段“没有设备证据”的限制已经由上述 Pad 5 Pro 实测补充。Pad 6S Pro 本身仍未在本轮连接验证。

当时确认的检查不足仍需区分事实与假设：

- 自建颜色格式取自 `SystemInfo.GetGraphicsFormat(DefaultFormat.LDR)`，未读取真实交换链 view 格式；Resolve 要求源目标格式相同、目标 1x。现在显式句柄能正常输出，不等于原故障已证明是 RGBA / BGRA 或 UNorm / sRGB 不匹配。
- `supportsMultisampledBackBuffer` 不保证任意独立 MSAA RT 到任意屏幕目标组合都正确；`Screen.msaaSamples` 也是请求值。
- 当前 `vulkanEnablePreTransform=0`、HDR 关闭，没有证据将其认定为本次首因。
- Pad 6S Pro 的 Snapdragon 8 Gen 2 / Adreno 740 没有被证明缺失 Vulkan 标准的颜色 Resolve 功能；不能按 SoC 型号直接做黑名单。

## 官方依据与已知问题范围

- [Display.colorBuffer](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Display-colorBuffer.html) 和 [6000.3 Display 绑定源码](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/6000.3/Runtime/Export/Graphics/Display.bindings.cs)：获取显示颜色 RenderBuffer；[RenderTargetIdentifier 构造器](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.RenderTargetIdentifier-ctor.html) 接收 RenderBuffer。
- [ConfigureResolveTarget](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rendering.AttachmentDescriptor.ConfigureResolveTarget.html) 与 [Vulkan VkSubpassDescription](https://docs.vulkan.org/refpages/latest/refpages/source/VkSubpassDescription.html)：Resolve 的格式、尺寸与样本约束。
- [Vulkan 标准样本位置](https://docs.vulkan.org/spec/latest/chapters/primsrast.html) 与 [Alpha-to-Coverage 规则](https://docs.vulkan.org/spec/latest/chapters/fragops.html)：标准 4x 集合在 Y 镜像后不同；A2C 的覆盖算法可以随 framebuffer 坐标变化。这些规则支持可能的边缘差异机制，不是本次已抓帧确认的底层状态。
- [Unity 6000.3 官方 URP 原生 Pass 源码](https://raw.githubusercontent.com/Unity-Technologies/Graphics/6000.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/NativeRenderPass.cs) 使用同一句柄的 LoadStore / Resolve 双面，这是另一种系统 MSAA 接法；本次修复仍保留系统 1x 与独立 MSAA 附件，没有改用该方案。
- [UUM-98671](https://issuetracker.unity.com/issues/2141/the-player-view-flickers-or-goes-black-after-minimizing-and-reopening-when-running-a-release-build-on-adreno-gpus) 的触发步骤不同；[6000.1.0f1](https://unity.com/releases/editor/whats-new/6000.1.0f1) 的 UUM-95529 Adreno Vulkan MSAA Resolve workaround 早于本项目版本，均不能直接当作本次已确认原因。
- [Khronos MSAA 示例](https://docs.vulkan.org/samples/latest/samples/performance/msaa/README.html) 展示同一 subpass 内 transient MSAA 附件 Resolve 到 swapchain 的合法用法，不支持“移动端一律不能直接 Resolve”的结论。
