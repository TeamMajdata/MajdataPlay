## 2026-10-18 传感器边框透明度（当前）

保留 5 份静态窄边 Mesh、34 个可视绑定与原碰撞数据；颜色仍为闲置红 / 触发绿。Shader 恢复 Transparent 队列（3000）、标准 Alpha Blend、ZWrite Off、ZTest LEqual，不再使用下一节历史记录中的 Opaque 状态。

调试菜单紧跟「显示传感器」新增「传感器透明度」，保存为 Debug.SensorOpacity。默认 20，范围 10～50，步长 10，菜单显示百分比；值越大越不透明，实际 alpha 为值 / 100。旧配置缺项默认 20%；外部输入按范围与档位规范化。红绿两态使用同一 alpha，调节即时更新，稳定状态不重复写 MPB；隐藏时仍禁用 Renderer 并跳过颜色更新。四种语言及模板键已补齐。

本轮 16 项程序集编译通过；四后端 8 个 Shader stage 和静态资源读回共 97 项检查通过，透明队列读回为 3000；实际新编译 Windows / Android DLL 各 25 项默认值、范围、步长和 JSON 保存读取检查通过；实际控制器源码加窄 stub 的 Editor / Player 各 24 项状态 / alpha / 缓存检查通过。未运行主工程 Play、Player 或真机，不宣称已验证最终混合画面或功耗。

## 2026-10-18 静态烘焙传感器边框（当前）

边框最终顶点已烘焙到 `Assets/Models/JudgeAreaBorders/A～E.asset`，固定对象空间宽度 0.02。Init 的 29 个独立 MeshFilter 和 FBX 实例中的 5 个 MeshFilter override 均替换为这 5 份共享资源，Renderer 材质也统一为 JudgeAreas。MeshCollider 继续使用原始完整 FBX 分区，Camera / Canvas / Transform / 输入索引未改。

未触发红色，触发绿色；关闭显示传感器仍禁用 Renderer。已移除运行时几何生成、网格所有权清理和 Shader 宽度偏移；材质 Border Width 不再是生效参数。Shader 只变换位置并输出颜色，使用 Geometry 队列 2000、Blend Off、ZWrite Off、ZTest LEqual。Opaque 队列使其早于后续透明音符，关闭深度写入避免边框给后续元素制造深度遮挡；实际与背景 / Live2D / 所有 UI 的画面仍由用户 Play 观察。

本轮 16 项程序集编译通过；隔离 headless Unity 的四后端 8 个 Shader stage 编译、5 份原生 Mesh 资源顶点 / 拓扑读回及 Opaque queue 共 97 项检查通过；实际控制器源码加窄 stub 的 Editor / Player 各 20 项红绿切换、关闭 Renderer、缓存写入和输入映射检查通过。静态验证 34 个可视绑定，原 29 份显式 Collider 和全部 Camera / Canvas / Transform 文档保持不变。未运行主工程 Play、Player 或真机，没有实测 GPU 时间或功耗。下节运行时生成 / 黑色触发 / 动态宽度记录为已替代的历史方案。

## 2026-10-18 传感器向内边框

`Assets/Models/JudgeAreas.mat` 使用新 `Maj/Debug/SensorBorder`。Init 的 DummyTouchPanelRenderer 指向此材质，原 Sensor.mat 不改。未触发红色，触发黑色，关闭显示传感器时禁用 Renderer；碰撞网格、GameObject 和输入索引不变。

五种可读平面网格各构建一次边框并共享：焊接导入时分裂的顶点，去掉内部三角形边，沿单轮廓生成外内顶点带。派生网格 UV0 存单位宽度向内偏移；原 FBX / UV / Collider 不改。顶点 Shader 用 Border Width（对象单位，默认 0.02，范围 0.001～0.05）调宽；片元只输出颜色，无纹理采样、无循环求边距、无透明内部。宽度随对象与相机缩放，不是固定屏幕像素。几何边缘使用已有 MSAA，不增加无 MSAA 的软边方案。

组件拥有派生 Mesh，销毁时恢复 MeshFilter 并释放，不释放源网格。只支持实际模型的单轮廓 XY 平面，异常时禁用对应可视绘制，保留输入。新材质需配合派生边框网格，直接给完整原网格换 Shader 不会自动产生边框。

16 项程序集编译、8 组四后端 Shader stage 编译通过。隔离 headless Unity 导入真实 FBX，复用生产构建算法并在 Upload 前捕获数据，5 网格 / 5 档宽度与分裂顶点去内部边共 18940 项断言通过。默认宽度的几何覆盖面积约为原分区 8%～16%，不能等同 GPU 时间或功耗。未运行主工程 Play、完整触发交互或真机测试。

> 2026-10-18 VSync 条件修正：仅 `(UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR` 隐藏输入；Windows Player 与 Editor（包括 Android 构建目标）均保留常规区垂直同步。Editor 启动和切画质同步应用保存值。16 项程序集编译、4 组实际 DLL 可见性检查、1212 项菜单断言通过；未运行主工程 Play 或 Player。

> 2026-10-18 最新菜单可见性：普通档位仅显示画质、帧率、桌面垂直同步和图形兼容模式；两种分辨率缩放与全部实时调试信息只在自定义且非兼容模式显示。兼容模式保留帧率、桌面 VSync 和兼容开关。自定义中，系统缩放不支持的平台仍显示「不支持」并拒绝调节。以下旧记录中普通档可见的描述已被本条替代。本轮设置回归 1212 项断言通过，未运行主游戏或真机。

> 2026-10-18 菜单修订：系统缩放不再按平台隐藏；Windows / Editor / 尚未接入的 iOS 显示「不支持」，拒绝左右调节，不改保存值、不提交请求。复用现有四语言「不支持」标签。当前 16 项程序集编译、1228 项设置 UI 断言通过；未运行主游戏或真机。Unity 的 Display.SetRenderingResolution 支持 Android / iOS，项目目前仅接入 Android Player，不能将此限制描述为 Windows 硬件不具备缩放能力。

## 2026-10-18 实时尺寸诊断与帧率设置

渲染菜单常规区现有帧率限制、桌面垂直同步，以及只读的当前输出路径、系统缩放状态、实际显示缓冲和实际场景分辨率。无需进入自定义；兼容模式也保留常用控制和诊断。系统缩放输入仅 Android Player 可调；Windows / Editor / iOS 保留该行并显示「不支持」，拒绝调节且保留保存值；移动端沿用原行为，不提供不起作用的 VSync 输入。

FPSLimit / VSync 现在保存到 Rendering。旧 Display 键逐项迁移；如果新旧两边都有键，新 Rendering 值优先，JSON 顺序不影响结果。画质预设不覆盖帧率或垂直同步；录制器及启动配置统一读取新位置。

系统缩放「已生效」仅表示实际显示缓冲尺寸匹配请求；尚未匹配显示「请求未确认」，恢复未匹配显示「正在恢复」，其它平台显示「不支持」。当前尺寸只发布成功提交的主屏相机结果：显示缓冲为实际目标大小，场景为实际绘制 viewport / 场景附件大小，附件回退时使用原输出大小。主屏不可用时显示 `--`。不变尺寸复用文本，不每帧拼接字符串；动态尺寸数字预热字体。

本轮 Windows / Android、Editor / Player 共 16 项程序集编译通过；设置 UI 回归 1226 项断言；实际新编译 Windows / Android DLL 各 36 项 JSON 迁移 / 平台可见性检查；实际系统缩放源码加窄 stub 的 Android / Windows 状态检查 52 / 6 项通过。未操作主工程 Play、设备或 APK，不代表原生 API、触摸、画面与功耗已通过。

## 2026-10-18 两种独立缩放

- 自定义菜单「渲染精度」：10%～200%，步长 5%；非 100% 使用 MRP RT＋Copy。实际附件仍受硬件最大纹理尺寸限制。
- 「系统分辨率缩放（实验）」：10%～100%，步长 5%，默认 100%，独立保存到 settings.json，普通预设也可见，不自动切换自定义。仅 Android Player 生效；100% 恢复接管前缓冲，低于 100% 请求原生宽高的对应比例。暂不开放高于原生的系统缓冲请求。
- 两项可叠乘：系统 75%、场景 50% 时，场景约为原生宽高的 37.5%，最终输出缓冲仍为 75%；文字和 Overlay 也可能受系统缩放影响。兼容模式暂时恢复系统缓冲，保留保存值，关闭兼容后恢复请求。
- Android Blit Type 从 Always 改 Auto（2），需重新打包；Unity 文档说明 Vulkan 忽略此设置，Auto 也不保证无内部复制。实际尺寸、输入对齐、MSAA、恢复和功耗待真机验证。

下文固定 75% / 节能自动启用 / Always 的实验记录为历史，不代表当前控制方式。


# MajRenderPipeline

面向 Unity 6000.3.17f1 的最小无光照管线。目标是在同等实际分辨率、抗锯齿和帧率下减少 GPU 附件读写、功耗与带宽；不提供 Lit、灯光、阴影或后处理。

当前架构、旧路径的问题、优化前后流程及 2026-10-16 Frame Debugger 审计见 [MRP 渲染架构与优化审计](Documentation/MRP-Architecture-and-Optimization.md)。下文保留实现细节和各阶段验证记录。

2026-10-17 已对当前未提交的 C#、Shader / include、配置与资源迁移做一次全面复审，范围和修复见[全面复审记录](Documentation/MRP-Architecture-and-Optimization.md#2026-10-17-未提交代码全面复审)。本次修复游玩 BGA 的资源 / Media 引用释放、旋转视频反复重建、显示切换和过期异步准备，补齐两份 UI Shader 的运行时 AlphaClip 变体，以及 A8 连按被校准 / 退出按下打断的边界。没有为此增加场景 RT、合成 Pass 或更改视频时钟；各项验证与真机限制分列于架构文档，旧 GPU 记录不当作本次重新执行。

2026-10-17 前的直接 Resolve 调查已在 Pad 5 Pro / Adreno 650 的 Vulkan 真机验证显式显示缓冲句柄恢复输出，保留 memoryless MSAA 和无中间颜色 RT / FinalCopy。那一阶段的独立 Fixture 使用旧版完整 viewport，通过 2x / 4x、冷启动、后台恢复与 Canvas / Mask，4x 与复制参考仍有稳定边缘差异（8740 像素、最大 62/255）。这是历史实测，不能沿用为后续菜单、动态参数覆盖、超采样或长屏平移的真机验证。2026-10-17 前一轮手机回归修复区分逻辑尺寸与物理屏幕，重做 Copy 定位，并保留偏移 viewport 的兼容兜底；随后本轮取消 ScreenPosCameraMover 的自动 .12 / .22 上移，使长屏也默认使用完整 viewport，100% 且后端条件满足时可直接 Resolve。各轮验证在下文分开记录，不沿用为本轮设备通过。设备证据见 [Android MSAA 屏幕 Resolve 调查与修复](Documentation/Android-MSAA-Compatibility.md)。

## 2026-10-18 Android 节能显示缓冲 75%（实验）

仅 Android Player 的“节能”档请求 `Display.main.SetRenderingResolution`，宽高取原生 `systemWidth / systemHeight` 的 75%，按当前屏幕方向排列并向下取整。例如 1440×3200 请求 1080×2400，输出像素为原生的 56.25%。MRP 自身的 RenderScale 仍为 100%、MSAA 关闭、纹理采样 bias +1；不把显示缓冲再乘一次场景 75%。Editor、Windows、iOS 维持原行为，菜单和 settings.json 没有新增字段。

MRP 的 Android 主屏目标、附件尺寸、viewport、FinalCopy 映射和 Shader 尺寸参数统一使用实际读回的 `Display.renderingWidth / renderingHeight`，归一化 Camera.rect 只映射一次。API 请求值不冒充已落地尺寸；未获得有效 rendering 尺寸时暂用 Screen 值。普通全屏 / 1x / 100% 条件满足时，仍直接写当前 CameraTarget，不新增 MRP 中间颜色 RT 或 FinalCopy。离屏、其它 Display、Preview 及非 Android 路径保留原像素域。

切到其它档位、进入自定义或启用兼容模式时恢复接管前的 rendering 尺寸，而不是强制面板原生值。捕获基准在快速来回切档时保留，防止异步恢复期间重复乘 75%；屏幕方向改变会交换基准宽高，后台恢复时可重新请求。稳定帧不反复设置缓冲。切换本身由引擎异步应用，过渡帧可能短暂使用旧显示尺寸与新场景比例，待读回恢复后兼容模式才稳定为原缓冲的 50%。

相机布局、Canvas、触摸坐标 / 半径、音频和判定时钟均未写入补偿比例。等比变化时现有 Expand 布局公式保持比例；真正的 Android 原生输入、触摸半径和 Overlay 集成仍需设备验证，不能仅用数值公式证明。

**当前 `androidBlitType: 0` 是 Always。** 此实验未修改该兼容设置；Unity 仍可在 MRP 之外离屏再复制到显示 framebuffer。MRP 的 Direct 状态只说明没有它自己的 FinalCopy，不能宣称全链路零拷贝。请求后一次 `[MRP.DisplayResolution]` 日志记录请求、实际、Screen、原生尺寸及 requiresBlit，`confirmed` 只代表尺寸读回一致。API 被拒绝或未生效时沿用设备实际尺寸，不自动伪装为另一种缩放实现。参见 [显示分辨率 API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Display.SetRenderingResolution.html)和 [Android Blit Type](https://docs.unity3d.com/6000.3/Documentation/Manual/class-PlayerSettingsAndroid.html)。

本次最终源码按 Windows / Android × Editor / Player 编译 MRP、Cubism SDK、桥接和业务四程序集，共 16 项通过，既有设置菜单 1065 项回归也重新通过。实际辅助类、映射函数、RenderingOptions 及抽取的 GameManager / Renderer 片段配窄 stubs，四配置共 131 项通过；包含原生与恢复基准不同、启动无尺寸、异步与快速切档、方向、后台恢复、拒绝 / 无效请求、稳定帧不重设、4x 档位不变及 RT 不重复缩放。这些不构成 Unity 原生显示 API 或设备画面通过。

验证记录见[架构文档](Documentation/MRP-Architecture-and-Optimization.md#2026-10-18-节能显示缓冲实验)。本次未操作主工程 Play、启动 Player、安装 APK 或测量手机画面 / 功耗；需重新打包 Android，在节能与性能间对比并测试传感器边缘。

## Code review 顺序

1. `Runtime/MajRenderPipelineAsset.cs`：预设默认比例、运行时覆盖、MSAA 与拷贝 Shader 引用。
2. `Runtime/MajRenderScale.cs`：实际比例的尺寸取整、Auto 过滤、Mip Bias；`Runtime/MajFinalCopyMapping.cs`：物理输出尺寸、逻辑矩形与 Copy 裁剪空间映射的纯函数。
3. `Runtime/MajRenderPipeline.cs`：相机调度、回调、Overlay UI 归属与全局状态恢复；`Runtime/MajAndroidDisplayResolution.cs`：Android 节能显示缓冲实验的请求、读回与恢复。
4. `Runtime/MajCameraRenderer.cs`：目标选择、缩放与非缩放路径、Shader globals、最终拷贝。
5. `Runtime/MajCameraAttachments.cs`：RT/设备能力缓存、原生和兼容附件、Load/Store/Resolve/Discard。
6. `Runtime/Shaders/`：场景颜色拷贝/放大，以及保留原材质属性的 MRP Unlit。
7. `Library/`：项目 Shader 共用的无光照 HLSL 基础库。
8. `Editor/`：Scene View 几何/Gizmos 与 MRP Unlit 材质状态编辑器。

`Runtime/MajdataPlay.MajRenderPipe.asmdef` 没有 URP/Core RP/Live2D 程序集引用。管线只选择 `SRPDefaultUnlit` Pass，沿用原生 RendererList 的 SortingLayer、CanvasOrder、透明排序、cullingMask 与 Instancing；不额外选择 Lit Pass。

`Editor/MajdataPlay.MajRenderPipe.asmref` 将编辑器代码并入同一个程序集，保持 `MajCameraRenderer` 的两个 partial 文件为同一类型。这里的 Editor 目录负责代码整理，Player 排除依靠每个编辑器文件完整的 `#if UNITY_EDITOR`；不能删掉条件编译后仅依赖目录名。移动现有脚本、Shader 和资产时保留原 `.meta`，不改变场景、材质或 Quality 的资源 GUID。

## Frame Debugger 命名

复用的 CommandBuffer 命名为 `MRP.RenderCamera`，Unity 为每次 Execute 自动配对采样范围；原先跨 Execute 的手动外层 sample 已移除。原生 Pass 的 UTF-8 名为 `MRP.Scene` / `MRP.FinalCopy`，静态编码并保留末尾 NUL。Editor / Development 内另标记 `MRP.Opaque`、`MRP.Skybox`、`MRP.CameraExtension`（当前为 Live2D）、`MRP.Transparent`、`MRP.FinalCopy`，每段在同一次 Execute 内通过 finally 配对结束。Release 不调用这些细分采样方法，未增加 Execute / Submit、RT 或绘制次数。

早期命名审计的 23 个项目 Shader Pass 均已命名；该轮仅给此前缺失名称的 11 个补上 Name，已有名称、LightMode、HLSL、属性、Stencil 和 GUID 保留。`Canvas.RenderSubBatch` / `Draw Mesh` 等引擎事件类型名、`Batch cause: Unknown reason`、MRP 范围外的 `<unknown scope>` 不由这些接口命名。兼容路径可能显示多个同名 `MRP.RenderCamera` 分组，表示同一缓冲的多次执行，不是多台相机或多张 RT。工具最终是否展示原生 Pass 名称取决于 Unity / 图形后端。

Windows 游玩视频的现有外部纹理 / RT 分别命名为 `Maj VLC Video Frame` / `Maj Video BGA`，仍用原 Graphics.Blit。命名修改通过 8 组管线源码编译与 4 组完整业务源码编译；11 个 Shader 去掉新增 Name 行后与修改前逐字节一致，meta 不变。没有重新运行 Unity 或捕获 Frame Debugger，实际显示需刷新捕获确认。

## Shader 精度

### 2026-10-17 Break 图集 UV 精度修正（当前）

手机截图中橙色 Break 的块状 / 断续边缘，对应到实际路径为 RuntimeDatabase → BreakMaterial → BrightnessAdjustAnimate；普通音符则使用 float2 UV 的 Maj/Sprites/Default。Break、HoldShineShader、ColorAdjustEffect 和 Notes/Common 的 v2f.uv 遗留为 half2，输入 appdata 已经是 float2。该 half2 在旧 URP 源码及此次 mip bias 改动前都已存在，并非前一轮 fixed → half 时新增；宏内再转 float4 无法恢复插值前后已经丢失的精度。性能档 bias 0 也会经过此路径，不能将截图直接归因于节能档的 +1 偏移。

这四处 UV 改为 float2；颜色仍用 half，时间 / 相位仍用 float，Properties、材质、meta / GUID、实例化和常量缓冲布局均不变。没有改采样次数、纹理 / RT 格式、过滤、图集尺寸或正常 LOD + bias 规则。皮肤图集上限是 min(8192, SystemInfo.maxTextureSize)，本次未测截图设备的实际打包尺寸；以 UV 0.5～1 范围的 binary16 为例，4096 / 8192 图集的相邻可表示值可相差 2 / 4 个纹素，足以使本应连续的采样出现块状跳变。默认 Tap 普通 / Break 均为 122×122，Slide 均为 70×94，未发现这两种 Break 源图被单独缩小。参见 [Unity 的精度与纹理坐标说明](https://docs.unity3d.com/cn/2022.3/Manual/SL-DataTypesAndPrecision.html)。

本轮旧新四 Shader 的 D3D / GLES3 / Vulkan / Metal、Vertex / Fragment 共 64 项代表编译成功且字节码非空。GLES3 导出明确由 mediump vec2 UV 改为 highp vec2；Metal VS / PS 的 half2 接口改为 float2；Vulkan UV 原有 RelaxedPrecision 标记移除，OpTypeFloat 本来就是 32 位，不能只看类型判断原先是否允许降精度。编译 New 与生产修改去掉注释 / 空白后语义一致；逐字节逆向检查确认生产只改四处 UV 类型和注释，meta 不变。本轮没有运行主工程、桌面 GPU 像素对照、手机或功耗测试；这些证据定位并修正了移动后端的精度风险，之后用户反馈修复后正常；这不是本次全面复审重新运行的手机画面，也不扩展为其它机型或功耗的通过结论。

### 2026-10-15 颜色精度审计（历史）

不能将全部 `float` 替换成 `half`。2026-10-15 审计覆盖 Assets 内 64 个 Shader 源文件 / ShaderGraph（45 Shader、9 cginc、6 hlsl、4 ShaderGraph），只缩减有界颜色中间结果，不改变贴图、RT 格式、采样次数或原有材质接口。

- Common 的 BrightnessAdjust / BrightnessAdjustOpaque：实例与材质中的 `_Brightness` 仍按原 float 布局上传，只在片元读取后转 half。
- Notes/Common、BrightnessAdjustAnimate、HoldShineShader：亮度 / 对比度系数改为 half；时间、相位和 sin 输入保持 float，常数饱和度 / 对比度 1 可精确表示。
- MaimaiColorEffect：色相分段完成后的有界 RGB 中间结果使用 half；UV、角度、时间分段、径向边界和 alpha 保持原 float 运算。
- Live2D / TMP 的 17 个文件统一 87 处 fixed 类型为 half。本机 Unity 6.3 的现代目标后端已把二者映射到相同精度，这是等价写法清理，不产生额外性能收益。
- UI 的 8 个 Shader 只补充精度保留注释。位置 / 矩阵、完整分辨率 UV、SDF / 导数 / AA / 遮罩边缘、角度 / floor / progress、Live2D 小 alpha 反预乘与复杂混合保留 float。UnityPerDraw、实例数组、材质缓冲的原字段类型与顺序保留。

ShaderGraph 序列化精度、Unity 内置 / Packages 源码和全局 Shader Precision Model 未调整。`half` 不保证所有平台都使用 16bit ALU；Unity 也可能忽略浮点字面量的 `h` 后缀，精度应以表达式类型和目标后端代码为准，不能仅凭源码中的 half 数量估算收益。half 参数也不会自动缩小 CPU 可见常量缓冲、纹理或 RT 的内存 / 带宽。参见 [Unity 16bit 精度说明](https://docs.unity3d.com/6000.3/Documentation/Manual/SL-Use16BitPrecisionInShaders.html)。

本轮保留 Properties、Shader 名称、meta / GUID、常量与实例缓冲布局。隔离 D3D11 Editor 的 Gamma / Linear、Batcher 开关和真实实例化共 224 组旧新 GPU 对照，最大 RGBA8 单通道差 1，实例绘制与逐实例参考差 0；四后端代表 stage / keyword 共 384 项编译通过。Live2D Mask 与 TMP 的修改和 include 依赖另覆盖 15 个 Shader、19 个 SubShader、23 个 Pass（含 2 个 Surface Shader 生成的 5 个 Pass），四后端另有 984 项代表 stage / pass / keyword 编译通过；上述编译均取得非空字节码，无警告或错误。另以 IEEE binary16 舍入、融合 / 非融合运算模型采样约 2.58 亿个标量结果，当前 LDR 输入范围内最大绝对差 0.001953125（约 0.498 个 8bit 码值），直接量化到 UNORM8 或经 Linear → sRGB 再量化，最大均只差 1；alpha 不变，无新增非有限值。这是数值采样与离屏图像证据，不是所有输入的穷举证明，也不替代 Android / iOS 真机画面与功耗测试；没有操作主工程 Play 或运行 Player。

## 保留的编辑器质量资产

资产位于 `Presets/`。初始五档从原 URP 资产复制；下表记录当前配置，包括之后在 Inspector 中调整的 Low 和单独的 Very High，不能再把所有档位都视为原 URP 数值。

| MRP 资产 | 对应旧 URP | 默认 RenderScale | MSAA | Unity Quality 绑定 |
| --- | --- | --- | --- | --- |
| MRP_UltraLow | URP_0_VLQ | 50% | Disabled / 1x | 无，保留预设 |
| MRP_Low | URP_1_LQ；当前已调整 | 100% | Disabled / 1x | Default，索引 0 |
| MRP_Medium | URP_2_Medium | 100% | 2x | 无，保留预设 |
| MRP_High | URP_3_HQ | 100% | 4x | 无，保留预设 |
| MRP_VeryHigh | 当前独立配置 | 100% | 8x | 无，保留预设 |
| MRP_Ultra | URP_4_UHQ | 100% | 8x | 无，保留预设 |

共同开启附件优化、SRP Batcher、GPU Instancing，关闭 Dynamic Batching。HDR、灯光、阴影和可采样 Depth/Opaque Texture 不在管线中生成。

Unity Quality 已合并为唯一 Default（索引 0），保留原 Low block 除名称外的全部参数，并继续引用 MRP_Low；各平台默认索引也统一为 0。六份 MRP 预设及 GUID 没有删除或重写，但其余五份不再绑定 Unity Quality。预设默认值不是新的游戏画质档位；功耗比较必须记录实际运行时比例、样本数和质量采样 bias。

MRP 固定关闭各向异性过滤，不提供该画质选项：Default 使用 `anisotropicTextures: 0`（Disable），管线启用时也显式应用全局 Disable，覆盖源纹理的 Aniso 设置。现有 `activeQualityLevelChanged` 防御逻辑保留，供外部代码改变 Unity Quality 时维持约束；游戏渲染菜单不再切换 Unity Quality。没有逐帧查询或反复改写导入设置。MRP 自建 RT 显式使用 `anisoLevel = 0`，FinalCopy 只按 Auto 选择 Point/Bilinear。退出 MRP 时取消订阅并恢复接管前的全局过滤设置；实际功耗收益仍需同条件设备对照。

Graphics 默认回退仍指向 `Presets/MRP_Low.asset`。游戏启动和修改渲染设置时使用 Unity Default 基线，再独立覆盖场景参数；不使用旧六档枚举索引 Unity Quality。早期独立的 `Presets/MajRenderPipeline.asset` 已不存在。

## 游戏设置与运行时覆盖

设置分类顺序为显示设定 → 渲染设定 → 音量设定。普通模式只显示渲染质量和图形兼容模式；画质只在四个预设名之间循环，不拼接参数，也不会循环选到自定义。新用户默认“性能”。

| 游戏档位 | 场景比例 | MSAA 请求 | 纹理质量采样 bias | 输出偏好 |
| --- | --- | --- | --- | --- |
| 节能 | 100% | 关闭 | +1，偏向更粗一层 mip | 直接输出 |
| 性能 | 100% | 关闭 | 0，默认采样精度 | 直接输出 |
| 美观 | 100% | 4x | 0，默认采样精度 | 直接输出 |
| 精致 | 150% | 4x | 0，默认采样精度 | 兼容输出（RT + Copy） |
| 自定义 | 50%–150%，步长 5% | 用户选择关闭 / 2x / 4x / 8x | 低 +2 / 中 +1 / 高 0 | 100% 时可选择；其它比例强制 RT + Copy |

高级入口仅在渲染质量行、兼容模式关闭时生效：连续按下十次 8 号键 / A8 区，保留当前参数进入自定义，立即应用并通过现有保存入口写入 `settings.json`。入口直接读取按下边沿，不再使用约 0.2 秒延迟且会合并快速点击的动画完成事件。按钮与触摸聚合为一轮按压，直到两路都释放才接受下一次；同帧或跨帧重叠不会重复计数，长按不会累计。换项、切档、离页及其它导航 / 调节输入仍清零计数，原来的 1 号键 / A1 校准入口保留原行为。已保存的自定义下次仍显示全部细项；选择普通预设后立即隐藏，自定义也再次从正常循环中排除，需要重新使用隐藏入口。

自定义时显示渲染质量、抗锯齿、纹理精度、输出模式、兼容开关、渲染分辨率及只读的当前输出路径。比例显示百分数，非 100% 时强制切为兼容输出并拒绝直接输出；回到 100% 后可手动重新选择直接输出。兼容模式开启后只显示兼容开关，有效值临时变为 50% / MSAA 关闭 / 质量 mip bias +2 / RT + Copy；原档位与自定义参数不变，关闭后恢复。纹理精度现在控制已接入 Shader 的自动 LOD 采样偏移，不是 Half / Quarter Resolution 资源限制，也不是 Mesh LOD；没有 mip 的纹理不会因此增加新的 mip 层。

“当前输出路径”读取 `MajRenderDiagnostics.OutputStatus`，显示最近一次主显示器 Game 相机正常提交后的实际分支：直接绘制、MSAA 直接 Resolve，或 RT+Copy，并标明缩放、兼容输出、MSAA 直出不可用、附件兼容路径等原因。附件失败或屏幕缓冲切换而退回直绘时也单独标记。状态只读且 `[JsonIgnore]`，不保存到 settings.json、不修改输出偏好、不触发应用或保存设置。它报告 CPU 已提交的路径，不是 GPU 像素正确性或黑屏检测。新的渲染帧没有主屏提交时标为不可用；同一帧额外的 Scene View、Preview 或离屏 context 不覆盖已完成的主屏结果。

`RenderingOptions` 保存到 `GameSetting.Rendering`，新配置默认使用性能档 / 100% / MSAA 关闭。旧 JSON 的 `Display.RenderQuality / RenderScale` 由私有只写属性接收，不再属于公开设置或重新保存；旧六档枚举仅在内部迁移。没有实际旧字段的空配置同样使用当前默认值，避免套入旧桌面 Medium 而变成 Custom / 2x。只有部分旧字段时，缺项仍按旧平台默认补齐。旧 2x / 8x 或非默认比例保留为自定义，匹配新档的 100% VeryLow / Low / High 分别成为节能 / 性能 / 美观。新旧字段并存时有效的新分类优先，空的新对象也优先；JSON 的细项顺序不会改变预设结果。

`GameManager.ApplyRenderingSettings` 固定 `QualitySettings.globalTextureMipmapLimit = 0`，并通过 MRP 运行时接口覆盖 RenderScale / MSAA / 输出模式和纹理质量采样 bias。覆盖均非序列化，不写预设资产；已有管线下次绘制读取新值，仅在尺寸 / 样本 / 目标改变时重建附件。质量 bias 变化不重新上传或缩小纹理，也不改 `Texture.mipMapBias`。桌面 VSync 仍以独立显示选项为准。直接输出是偏好，后端不支持时仍自动使用兼容路径；显式选兼容输出则在 100% / 1x 时也插入颜色 RT + Copy。

### 2026-10-17 纹理采样 bias 与单一 Unity Quality（当前）

纹理低 / 中 / 高分别传入 `_MajTextureMipBias = +2 / +1 / 0`，节能使用 +1，兼容模式使用 +2，其余预设为 0。接入点在自动 LOD 采样：现代本地 MRP 采样宏同时保留原 RenderScale 的 `_GlobalMipBias` 补偿并叠加质量 bias；原先没有比例补偿的 legacy 采样只加入质量 bias。全局资源 mip limit 保持 0，不再通过反复更改资源限制实现这三个档位。高档 0 仍由纹理投影尺寸自动选择 mip，不表示永远强制采样原始 mip 0。

这项改动只影响带 mip 且 Shader 已接入的采样。当前接入范围为项目自定义的相关自动 LOD 采样、Live2D 主图及 TMP_Sprite 图像采样；实际 EmojiOne 图集带 mip。全部 TMP SDF 字体以及未发现资源引用的三份 TMP Bitmap Shader 不修改。共享采样宏也可能用于视频或 Composite RT，但只有一层 mip 的纹理最终仍采样 LOD 0，不能因此降低纹理精度；FinalCopy 文件及采样不改。外部输入是否带 mip、最终 Shader 是否已接入，以及其它内置或第三方 Shader，仍需按实际绘制链确认，不能称所有纹理统一生效。普通 Bilinear 是单层 mip 内的双线性过滤，不提供相邻 mip 之间的连续插值；[Trilinear](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FilterMode.Trilinear.html) 才提供级间混合，当前没有为此批量修改导入过滤方式。

主业务场景 / Prefab 原先使用的默认 UI 与游戏 SpriteRenderer 材质已改为共享 `Maj/UI/Default` / `Maj/Sprites/Default`，让这一范围的图像也走接入的本地采样。RuntimeDatabase 的普通音符默认材质入口同步替换，因此使用此入口的外部皮肤不因来源在工程外而自动排除；仍取决于纹理是否有 mip 和最终材质。静态迁移记录为 91 份文件、1568 处引用，保留 Sprite、fileID 与 meta；未改 unused 场景和未配置的历史 Prefab，也不覆盖用户自行提供的任意 Shader。该记录不是当前帧或主游戏视觉验证。

质量采样 bias 不删除或阻止上传高分辨率 mip，因此不减少这部分纹理驻留容量；可能改变采样工作与缓存压力，实际带宽和功耗必须测量。它与 Unity 的 Global Mipmap Limit 不同，后者会限制最高 mip 的上传，并可能在运行时切换时重新上传受影响纹理。参见 [Unity Mipmap Limit](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QualitySettings-globalTextureMipmapLimit.html)。

本轮通过 Windows / Android 的 Editor、Player 四组共 8 个程序集源码编译；真实生产程序集 / Newtonsoft 配置回归分别通过 6924 / 6243 项，设置 UI harness 通过 13 组 / 1057 项。四语各 383 条键合法无重复，template 键表未变。Quality 静态对照确认唯一 Default 除名称外保留旧 Low 的全部 52 个字段，平台默认索引统一为 0；资源迁移逐字节逆向校验保留原 Sprite、fileID、meta 与其它属性。

隔离 Unity Editor 在桌面 D3D11 的 Gamma / Linear 下执行 574 次绘制、322 次读回，167 组全图零容差对照均无像素差：彩色 mip 链验证自动 LOD 0.5 / 1.5 / 2.5 上叠加 +1 / +2 与 UV 频率乘 2 / 4 的隐式参考一致，单 mip 不受影响；新增默认 Sprite / UI 在 bias 0 时与本版 Unity 内置 Shader 对照一致，包含 tint、flip、外置 alpha、PixelSnap、UI 裁切 / 柔边、AlphaClip 与顶点色处理的代表组合。26 个 Shader 的 D3D / GLES3 / Vulkan / Metal 共 432 项代表 stage / pass / keyword 编译成功且字节码非空。这些不是全部变体或移动设备实绘。初次 UNorm 低 alpha 对照没有可见差异的诊断仍保留，补充浮点目标多次绘制确认 AlphaClip 实际改变像素后，新旧输出仍一致，未放宽阈值。

本轮没有运行主工程 Play、完整 Player 构建或真机，也没有验证实际 CanvasRenderer / SpriteRenderer 集成、屏幕 Present、功耗、带宽或 GPU 频率。以下旧质量清理和此前 GPU 记录不计为本轮验证。

前一轮旧代码清理（历史）删除了无绑定的 `EngineEnumSettingEnumerator`、旧 RenderScale 枚举器分支与 `GameManager.ApplyRenderScale`，以及 Display 的旧画质 / 比例公开属性和重复默认常量。FPS 限制与 VSync 继续使用现行枚举器，VSync 默认仍为 true。该轮当时保留索引 1 的 Low 引擎基线，尚未合并质量资产或改变纹理参数语义；这不是当前 Default / 采样 bias 改动的验证。

该历史清理通过 Windows / Android 的 Editor、Player 四组共 8 个程序集编译。实际生产程序集与真实 Newtonsoft 的 Windows / Android 两轮分别通过 6813 / 6132 项逻辑检查，包含既有参数 / 状态 / 比例回归，以及空配置默认、部分旧字段的平台补齐、新旧对象顺序、字符串 / 数字旧画质、只读旧键不再保存等迁移检查。设置 UI 的 13 组 / 1054 项断言通过，旧比例枚举器断言改为实际仍使用的 FPS 限制。上述为当时源码、配置读写和控制流验证，没有运行主工程 Play、完整 Player 构建或设备画面 / 功耗测试。

场景比例支持 50%–150%，超采样按硬件纹理尺寸上限同比例限制，使用 Bilinear 缩小且不施加负 mip bias。150% 的附件大于屏幕，现有直接 MSAA Resolve 不负责缩放，因此精致档需要中间 RT + Copy；Screen Space Overlay 继续按原生屏幕分辨率由引擎绘制。Camera 投影、布局与输入坐标不随比例修改。

此前高级菜单改动通过 Windows / Android 的 Editor、Player 四组生产源码编译；实际新程序集的 57 项参数、JSON 迁移 / 顺序、自定义持久化及缩放数值检查通过。设置 UI 外部 harness 当时扩展至 11 组 / 802 项断言，覆盖 A8 十次解锁、A1 校准保留、按钮 / 触摸去重、长按不重复累计、导航打断、分辨率步长 / 上下限、输出限制、隐藏 / 恢复、池和四语标签；此前 43 / 688 项记录属于更早版本。

2026-10-17 输出状态项改动通过 Windows / Android 的 Editor、Player 四组共 8 个程序集编译。新生产程序集的 101 项设置 / JSON / 状态生命周期与 Copy 原因优先级检查通过；UI 外部 harness 更新为 12 组 / 934 项断言，覆盖十种状态自动刷新、只读输入、稳定状态不重写文本、同帧 context 保留 / 新帧失效、四语切换与预热及自定义 / 普通 / 兼容模式的 7 / 2 / 1 项可见性。未运行主工程 Play 或本次 GPU 绘制，不将此前 GPU / 真机结果计作此状态项的端到端验证。

随后修正快速 A8 输入与 5% 比例：使用真实 `InputControlState` 复现 60Hz 下十次快速按下仅产生两次旧动画完成通知；新入口改按下边沿及两通道释放锁存，按钮 / 触摸均在第十次解锁，不修改全局输入或 A1 校准。UI harness 扩展至 13 组 / 1043 项断言；实际新生产程序集通过 190 项逻辑检查，包含 50%～150% 全部 21 档的有效比例、尺寸递增和直接输出规则。1080x2400 的 95% / 100% / 105% 分别得到 1026x2280 / 1080x2400 / 1134x2520，未再吸附。Windows / Android 的 Editor、Player 四组 8 个程序集编译通过；以上为源码与外部 harness 检查，未重测主工程 Play、实际手机输入或 GPU 附件。

## Opaque 裁切材质的最小 A2C

当前实际 Opaque / AlphaTest 裁切材质统一采用标准最小 Alpha-to-Coverage：`Maj/Common/Opaque` 覆盖普通 UI、RingLine 与列表等级底板；`Maj/UI/List/CoverShader` 覆盖列表封面、缩略图与圆形封面。两者固定 `AlphaToMask On`，保留 Blend Off、ZWrite On 和原队列，不增加贴图采样、绘制 Pass 或 RT。纯实心背景继续输出 Alpha=1，不因背景贴图或视频的 Alpha 打孔。

实际场景附件为多采样时，Common/Opaque 直接返回原贴图 × 顶点/材质颜色的 RGBA；CoverShader 返回原 RGB × Brightness 和现有 `textureAlpha × circleAlpha`。不再围绕 cutoff 做 fwidth / coverage 重映射，也不在 MSAA 分支做 0.5 硬裁切；原有圆形形状本身的导数 AA / Feather 保留。因此 `_Cutoff` 现在仅在无 MSAA 时生效，MSAA 下的低 Alpha、软边与淡出直接决定样本覆盖，外观可能与旧的硬裁切阈值不同。

`_MajAlphaToMaskAvailable` 仍由 MRP 按已创建附件的实际采样数设置，三条相机路径提交前复位为 0，避免后续引擎 Overlay 误用场景状态；1x 或未知系统目标保留原 clip 和 Alpha=1。没有新增材质开关或 Shader 关键词变体。原 UnityPerMaterial / Instancing 布局、材质 GUID 与场景引用保留；先前为 RingLine 增加的材质资产仍有效，但其专用关键词与开关字段已清除，行为与原共享材质一致。

2026-10-16 当前直接 Alpha 版本通过隐藏 Gamma / D3D11 隔离验证：26 次真实 Cull / RendererList / Resolve 绘制、29 项检查全部通过。两种 Shader 的 1x 与原硬裁切逐像素 RGBA 一致；4x 形成部分样本覆盖，透明孔、前后层深度顺序与列表 Brightness 实例化对照通过。MSAA 下改变 cutoff 为 0.1 / 0.9 不改变 Common/Opaque 输出，确认没有残留阈值重映射；原共享材质与保留的 RingLine 材质结果一致。两种 Shader 的 D3D11 / Vulkan / GLES3 / Metal 基础 Vertex / Fragment 共 16 项代表编译通过。测试手动驱动 availability，全局运行时接线、真正 Canvas 发射、屏幕直出与 Android / iOS 真机未在本轮验证；早期 RingLine 专用导数 A2C 记录属于历史版本。

## 透明圆形 UI 的描边与阴影

`Maj/UI/CircleMaskWithOutlineShadow`（`UICircleOutlineShadow.shader`）保留 Transparent / ZWrite Off，使用预乘 Alpha 输出和 `Blend One OneMinusSrcAlpha`。主体与描边先按互补的圆形覆盖率合为一层，再整体覆盖阴影，RGB 与 Alpha 使用一致的 source-over 计算，避免软边重复乘 Alpha、内接缝漏出背景。描边宽度为 0 时覆盖率明确为 0。

圆周使用 `fwidth(dist)` 调整抗锯齿宽度，1x 与 MSAA 目标均可使用；阴影保留原 Softness，并至少使用一个像素的过渡，避免零 Softness 退化。此透明 Shader 不启用 A2C，避免在普通透明混合之外再次衰减覆盖率。材质属性、Stencil、深度和原有主体 Tint 语义保持，仍为一次纹理采样、一个 Pass，没有新增 RT。

当前引用是以完整 0～1 UV 绘制的圆形背景。此修改没有新增 SpriteAtlas 子区域到局部圆形 UV 的换算，也没有改变阴影／描边独立于主体 Tint 的既有行为。正常的黑色软阴影仍会保留。

2026-10-16 修复版本通过隐藏 D3D11 / Gamma 离屏 GPU 验证：40 次绘制、38 项检查，覆盖 1x / 4x、30 / 150 像素、零宽描边、半透明 Over、内接缝 Alpha、正常软阴影和零 Softness 的有限值。旧版黑边和重复乘 Alpha 的失败对照均已复现并保留；D3D11 / Vulkan / GLES3 / Metal 的 Vertex / Fragment 共 8 项代表编译通过。本轮没有运行主场景 Canvas 或 Android / iOS 真机，跨后端编译不等于跨设备画面验证。

## RenderScale 流程

- 只缩放场景附件，不改 `Screen.SetResolution`、Camera 输出矩形、游戏坐标、音符/判定时钟。
- 比例限制在 50%～150%，每个 5% 档位都按所选数值计算，不再沿用旧 URP 的 100% 附近吸附。此前 105% 的浮点差值可能略小于 5%，被误当作 100%；本轮移除该吸附，95% / 105% 均保留实际比例。
- 场景尺寸按相机逻辑输出宽高计算，为 `Max(1, (int)(逻辑输出尺寸 * 比例))`，向下取整。例如逻辑输出 1080x1920 的 50% 是 540x960，75% 是 810x1440。主屏 Copy Pass 的物理尺寸单独取 `Screen.width / Screen.height`，不把相机 `pixelWidth / pixelHeight` 当作物理 backbuffer 尺寸。
- 附件缓存按实际整数宽高比较，不把比例量化为 10% 或 20% 档位；只有整数像素取整或硬件纹理尺寸上限可能让相邻比例得到相同尺寸。100% 且附件尺寸等于输出时仍可直接输出，其余比例仍走实际缩放与 Copy，不修改 Camera 投影或输入坐标。
- `_ScreenParams` 保持输出尺寸，`_ScaledScreenParams` / `_ScreenSize` 使用场景尺寸；Mip Bias 对齐旧 URP。Overlay 前恢复全分辨率参数。
- 旧 URP 的 Upscaling Filter 为 Auto：整数倍放大且输出宽高整除时用 Point，否则 Bilinear。因此偶数尺寸的 50% 是 Point，75% 是 Bilinear。

仅缩放安全独立的主显示器 Game 相机，且必须完整清颜色。独立资格按同一 Display 的屏幕 Game 相机判断，额外的 RT / Preview / 其它 Display 相机不会让主相机的 50% / 75% 变回 100%；相机按 depth 排序。原生附件要求归一化 viewport 的宽高均为 1，且相机逻辑输出尺寸与物理屏幕尺寸一致。ScreenPosCameraMover 已取消按长宽比自动设置 y=.12 / .22 的旧逻辑，初始化统一使用 (0,0,1,1) 完整 viewport；长屏本身不再触发偏移 MSAA 的 Copy。手动显示设置 MainScreenScale / MainScreenOffset 继续通过相机 Transform / 正交 Size 和 Canvas 变换调整布局，不改变 rect，也不等于 RenderScale，因此不会单独强制 Copy。RenderScale 非 100% 或明确选择兼容输出仍需场景颜色 RT + Copy。

MRP 仍保留外部非原点 viewport 的兼容处理：100% / 1x 按原 pixelRect 直接绘制；偏移 MSAA 在本地场景附件绘制，Resolve 到普通 1x RT，再由 FinalCopy 定位。缩放和强制兼容输出同样在最终 Copy 应用一次逻辑矩形。Copy 的实际 viewport 固定为整张物理输出、零原点，Shader 将完整源图映射到原逻辑矩形，裁掉超出源 UV 的片元，并清除未覆盖的屏幕边缘；不把完整图像压入屏幕交集。宽或高不为 1、逻辑与物理尺寸不一致等情况保留兼容附件路径。Scene View、Preview、Reflection 保持 100%；现有 targetTexture、共享屏幕的相机叠加、XR 和动态分辨率保留原目标尺寸，属于本版兼容边界。

## 附件流程

| 场景条件 | 场景颜色 | 场景 Depth/Stencil | 最终输出 |
| --- | --- | --- | --- |
| 100% + 1x，原生路径 | CameraTarget：Clear → Store | Clear → DontCare | 直接输出，无中间颜色与 FinalCopy |
| 100% + 2x/4x/8x，完整零原点 viewport，允许直接 Resolve | MSAA RT：Clear → Resolve 到屏幕显示缓冲，不 Store 多采样结果 | Clear → DontCare | 无单采样中间颜色 RT、无 FinalCopy |
| 小于 100% + 1x，原生路径 | 单采样场景 RT：Clear → Store | Clear → DontCare | 按 Auto 过滤放大到原输出尺寸 |
| 2x/4x/8x，有缩放、viewport 偏移或不允许直接 Resolve | MSAA RT：Clear → Resolve 到 1x RT，只 Resolve，不 Store 多采样结果 | Clear → DontCare | 单采样 Copy；偏移时仍保留符合条件的原生 / memoryless 附件 |
| 关闭附件优化但有缩放 / MSAA、小视口需中间颜色，或 Game View Gizmos | 普通场景 RT：Clear → Store，按需显式 Resolve | Store，保留到 Gizmos 完成 | 普通拷贝/放大，RenderScale / MSAA 仍生效 |

不透明、Skybox、透明对象及 Camera/World Space Canvas 在同一场景目标中绘制。Stencil 保留到透明 UI 完成，原生 Pass 结束时才丢弃；支持时使用 memoryless 深度。兼容路径保留深度供 Gizmos 使用。

原生 Pass 的 MSAA 颜色在设备支持时请求 `RenderTextureMemoryless.MSAA`，只在 Pass 结束时 Resolve，不 Load / Store 多采样内容；不额外设置 `Color` 标记。100% 直接 Resolve 到屏幕与缩放后 Resolve 到普通 1x RT 都可使用；兼容路径跨 Pass 保存颜色，仍用普通 RT。若创建失败、实际样本数/格式不匹配或 memoryless 标记未生效，释放该附件并以相同尺寸、格式和 MSAA 重试普通 RT；同一配置缓存拒绝结果，避免逐帧试错。普通附件也创建失败时保留原兼容绘制回退。单采样 Resolve 结果始终不是 memoryless，供 FinalCopy 读取。

最终屏幕请求 1x，MSAA 只在场景附件中完成，避免屏幕再次保存/解析多采样颜色。支持该请求的平台即使 `Screen.msaaSamples` 为 0（跟随 Quality），也显式请求 1；首次改变样本数的一帧使用原分辨率兼容路径，退出时原样恢复请求，包括 0 的跟随语义。`Screen.msaaSamples` 不是硬件附件的实测结果。

**Optimize Attachments** 控制原生附件丢弃，不再顺带关闭显式 MSAA。关闭优化后，有缩放或 MSAA 时使用普通附件与 FinalCopy；100% / 1x 才保留原 CameraTarget 路径。独立主相机显式关闭 MSAA 时仍请求系统 1x，避免恢复跟随 Quality 后重新启用其旧 antiAliasing 值。

**Resolve To Screen** 的资产默认值当前开启，游戏运行时由输出模式覆盖；只有允许直出，且为 100%、独立 Game 相机、原生附件路径、完整零原点 viewport、逻辑输出与物理屏幕尺寸一致时才会尝试。非零 x/y 偏移不尝试 MSAA 直接屏幕 Resolve，改为 Resolve → 1x RT → FinalCopy，MSAA 画质请求与符合条件的 memoryless MSAA / 深度仍保留。必须满足 `SystemInfo.supportsMultisampledBackBuffer`，后端限定 Vulkan / iOS Player 的 Metal、LDR Gamma，并排除 Display.requiresBlitToBackbuffer、requiresSrgbBlitToBackbuffer 和 HDR 输出。Linear、GLES3 / GLCore / D3D11 / D3D12 及其它平台的 Metal 保留 Resolve 到普通单采样 RT + FinalCopy，MSAA 档位不变。本机 Windows D3D11 / Editor 的回退属于历史验证；代码没有对 Windows / Editor Vulkan 额外做平台排除，仍按上述能力条件筛选。不能用“能够 Resolve 到普通 RT”推断“能够 Resolve 到屏幕”：Windows D3D11 旧分支曾在 Player.log 中触发 multisampled texture targeting back buffer is not supported，导致场景 Pass 被拒绝和画面残留。直接路径仍检查实际 MSAA RT 格式，设备出现异常时可单独关闭此选项；条件筛选不替代各 Vulkan / iOS Metal 环境的实际画面验证。

Android Player Vulkan 的直接路径每次 `BeginScene` 取 `Display.main.colorBuffer` 并构造显式 RenderTargetIdentifier；只缓存平台 / API 判断，不缓存跨帧显示缓冲。其它后端保留 CameraTarget 句柄。历史 Pad 5 Pro 独立 Probe 和当时生产 Runtime 的完整 viewport Android Fixture 同尺寸对照已确认该替换恢复屏幕输出；它不新增 RT、额外绑定、Draw 或 Submit，也不改变创建失败回退。Fixture 的 4x 直出已显示普通几何、A2C、前后深度覆盖、Stencil Equal 形状、真实 UGUI Camera / World Canvas、Overlay 半透 Panel 与 UGUI Mask。首轮直接 / 拷贝路径的差异集中于几何和 A2C 边缘，所选深度 / Stencil / Canvas ROI 无差异，不能将这项历史结果扩大为当前长屏平移 viewport、全图逐像素一致或完整游戏回归。仍不能据此宣称所有 Android / iOS 设备或真实屏幕格式已被完整验证。

直接路径的颜色输出在场景 Pass 结束时已写入屏幕，不再分配 MRP Scene Color，也不执行全屏三角形；深度/Stencil 仍保持到所有透明内容完成。当前 MRP 没有可采样场景颜色或读取背景的后处理；若以后添加这类效果，应显式要求中间颜色并禁用直接路径，不能从屏幕读回代替。

FinalCopy 的 Game 屏幕输出保留原始源 UV 和恒等 UV scale/bias；targetTexture / Scene View 使用纹理目标方向，在 TopLeft API 上单独翻转源 UV。输出矩形与源方向分开处理：Copy 使用整屏零原点 viewport，`MajFinalCopyMapping` 将原逻辑 pixelRect 归一化为顶点裁剪空间的 scale / bias，Y 平移仅按 `GL.GetGPUProjectionMatrix(Matrix4x4.identity, false).m11` 的符号转换到屏幕 GPU 方向。该符号不翻转整张源图，也不写回相机投影。Fragment 按未翻转的局部 UV 裁剪到 0～1，避免负偏移或超出屏幕时 Clamp 采样把边缘拉进留白；有未覆盖区域时先清整张输出。场景仍使用原相机矩阵，在本地 RT 中不另加平移，避免矩形被重复应用。Screen Space Overlay 由 Unity 在 SRP 后绘制一次，保持完整输出分辨率；Camera/World Space Canvas 随场景缩放。

### 2026-10-17 传感器调试显示开关

`DummyTouchPanelRenderer` 的显示逻辑统一到 Editor / Player：开启 `Debug.DisplaySensor` 后，未触发分区使用黑色 alpha 0.1，触发使用 alpha 0.4。原 Player 分支的 0 / 0.3 已移除。关闭时禁用 34 个分区的 MeshRenderer，构造时也默认禁用，不再靠 alpha 0 隐藏网格；Collider、GameObject 及碰撞体索引映射保持启用，输入查表和触摸不因调试显示关闭而停止。独立 IO 测试页的红蓝显示不在该开关范围内。

开关状态不变且关闭时直接返回；Renderer.enabled 只在开关变化时更新，MaterialPropertyBlock 只在颜色变化时更新，并保留已有的其它属性。本轮 Windows / Android Editor、Player 共 8 个程序集编译通过；实际组件源码与窄 Unity / 输入 stubs 的 Editor、Player 各 20 项、共 40 项检查通过，覆盖默认关闭、淡色 / 触发 / 释放、重新开启、稳定帧不重复写入、35 位原始输入中额外位跳过及 Collider / 索引保留。未运行主工程、Frame Debugger、设备或功耗测试；不将减少的 Renderer 绘制资格描述为已测量的 GPU 毫秒或功耗降幅。

### 2026-10-17 长屏相机与传感器对齐

主 UI 的 Canvas 使用 1080×1920 / Expand，主屏 1080×1080 区域锚定底部；传感器、音符和触摸查表使用固定世界坐标。此前取消长屏 viewport 上移后，相机仍固定正交 Size 9.6 / Y 4.2，导致 1080×2400 时世界分区比 UI 大 25%、中心偏上 135 像素。这是相机与 UI 的适配比例不同；Editor 桌面分辨率覆盖是独立问题。

`ScreenPosCameraMover` 现在使用逻辑屏幕尺寸，与 Canvas Expand 同比例计算运行时正交范围。令 `f = min(W/1080, H/1920)`、序列化基线为 `B`，适配范围为 `S = B × (H/1920) / f`。关闭手调时使用 Size=S、Y=原始Y+(S−B)；开启手调时使用 Size=S/MainScreenScale、Y=1.5+2.7×MainScreenOffset+(S−B)，保留既有 UI 围绕屏幕中心的缩放语义。默认基线 B=9.6，1080×1920 仍为 Size 9.6 / Y 4.2，1080×2400 则为 Size 12 / Y 6.6。屏幕尺寸改变会重新应用相机参数，稳定尺寸不重复写入。

只修改相机布局组件，保留完整 rect，不改变 MRP 场景附件 / RenderScale / MSAA 或直接输出资格。传感器根、固定世界坐标查表、触摸半径和输入时间源不改；输入继续通过同一 Camera.ScreenToWorldPoint 做逆投影。传感器查表初始化后保持原世界位置，不通过独立移动传感器补偿。运行中切换方向 / Game View 比例时，Canvas 手调中心及副屏角点缓存仍有各自的旧刷新边界，本轮不将它们扩展成整体动态布局重构。

本轮 Windows / Android Editor、Player 共 8 个程序集编译通过。隔离 Unity 使用真实 Camera、ScreenSpaceCamera Canvas / CanvasScaler、修改前后实际 CameraMover 和实际 CanvasMover，设置接口用窄 stub，逻辑尺寸由已安装的 ScreenSimulation 提供，目标为同尺寸 RT。20 条前后结果中，修复后 10 组全部通过，覆盖 1080×1920、1080×2400、1220×2712、1920×1080，以及手调 0.8 / 1.2；中心和对角点最大误差小于 0.000159 像素，UI 中心逆投影最大误差小于 0.000001151 世界单位。原始测试因未让 Canvas 经相机帧更新而保留旧世界变换的失败记录仍保留；补充实际 Camera.Render 更新后再测，不修改比较阈值。这是原生几何 / 坐标验证，不是 MRP 像素或手机 Present、实际触摸命中、功耗测试；主工程 Play 和手机仍需用户复测。

组件控制流另用实际源码与窄 Unity / 设置 stubs 验证，5 组 / 50 个固定屏幕启动组合、1494 项检查通过：常规屏、长屏、平板、宽屏，手动缩放 / 偏移、延迟设置、关闭恢复、尺寸变化后的相机重应用及投影 / 逆映射均覆盖；稳定帧不重复写相机，也不写入保存设置。动态尺寸检查只证明相机侧重新应用，不证明未改的 Canvas 中心和副屏角点缓存已经刷新。

### 2026-10-17 Editor 长宽屏拉伸修复

`GameManager.ApplyScreenConfig` 原来的桌面条件编译也包含 Windows / macOS Editor，会把保存的桌面 `Display.Resolution`（本次为 1080×1920）写入 Game View / Device Simulator。Simulator 的内部渲染尺寸可以被这个请求改成 9:16，但外层设备屏幕仍按所选设备的原始比例铺满显示，造成长屏纵向、宽屏横向拉伸。现仅在桌面 Player 应用窗口、全屏和分辨率配置；Editor 尺寸交给 Game View / Simulator，FPS 设置仍执行。没有修改 MRP 附件、MSAA、RenderScale、相机投影或 Canvas 布局。

本轮 Windows / Android Editor、Player 共 8 个程序集编译通过。提取实际修改前后方法体、以窄 Unity 接口 stubs 在 Windows / macOS / Android 的 Editor、Player 六组执行 72 个场景、239 项断言：Editor 不再写屏幕配置，FPS 保留；桌面 Player 每例调用行为与修改前一致，Android 仍只设置 FPS。隔离 Unity 的真实 ScreenSimulation / DeviceView 网格观测确认：1080×2400 设备被请求 1080×1920 后，内部尺寸改变、外层网格仍为 1080×2400；1920×1080 设备同理，足以解释拉伸方向。隔离无窗口 RenderView 尝试未完成画面验证，原始错误日志保留，不能算作 GPU 或主游戏通过。用户随后重新运行主工程 Play，报告画面已恢复；没有本轮真机或功耗结论。

### 2026-10-17 全屏布局调整（历史验证，布局规则保留）

取消 ScreenPosCameraMover 对长屏 viewport 的自动 .12 / .22 上移，主相机初始化归零为完整 viewport。100% 且满足后端、显示缓冲和附件条件时，长屏也可使用 MSAA 直接 Resolve，保留原生 Resolve-only 和支持时的 memoryless；100% / 1x 继续直接绘制。默认构图将回到未自动上移的位置，这是一项布局变化，不承诺与旧默认位置相同。现有手动 MainScreenScale / MainScreenOffset 及保存值保留，它们不改变 viewport；RenderScale 非 100% 仍走 RT + Copy。MRP 的偏移 viewport 兜底没有删除，供其它调用者改变 rect 时使用。

该布局阶段 Windows / Android 的 Editor、Player 四组共 8 个程序集编译通过。工程外将实际 ScreenPosCameraMover.cs 与最小 Unity / DisplayOptions stubs 组合执行，3 组共 739 项检查通过，覆盖 Awake / LateUpdate、延迟设置、旧 .12 / .22 归零、手调开关 / 缩放 / 偏移以及恢复和稳定状态；这些是控制流与赋值检查，不是 Unity 视觉、实际触摸、Canvas 布局或屏幕 Present 验证。MRP 渲染逻辑未在该布局阶段改变，没有重跑下一节的 420 次 GPU 捕获；这些记录均早于当前纹理采样 bias / 单一 Quality 改动，不能作为本轮重新通过，手机实际画面仍待用户复测。

### 2026-10-17 手机偏移修复与验证状态

本节保留取消自动 viewport 偏移前的上一轮修复与验证；其中源码编译、2034 / 1053 项检查和 420 次 GPU 捕获不计为本次全屏布局调整重新通过。

用户报告此前长屏路径在手机上出现 MSAA 下移、兼容 Copy 上移及比例变化时位置不一致。本轮修复针对输出坐标空间的混用：相机逻辑尺寸与物理屏幕尺寸分开；Copy 的矩形映射移入 Shader；非原点 viewport 不再直接 MSAA Resolve 到屏幕。没有修改 Camera / Canvas / 输入，也没有按 SoC 型号添加黑名单；尚不能据此认定 Unity 或驱动内部某一环为已证实根因。偏移 MSAA 会增加一个可采样的 1x Resolve RT 和 Copy，但保留原生 Pass 的 Resolve-only、深度丢弃及支持时的 memoryless 优势。完整 viewport 的直接 Resolve 和 100% / 1x 无中间颜色路径保留。

本轮 Windows / Android 的 Editor、Player 四组共 8 个程序集编译通过；实际新程序集的 2034 项纯逻辑检查、设置 UI 的 1053 项断言通过。隐藏隔离 Editor 在 Windows D3D11 和 Vulkan 各完成 210 次 GPU 捕获，共 420 次：1080×2400 / 1220×2712，viewport y=0 / ±.12 / ±.22，50% / 95% / 100% / 105% / 150%，1x / 4x，覆盖原生附件和普通 Store 的 RT+Copy。每个后端的 100 对原生 / Store 输出全图零阈值一致，100% / 1x Copy 与原 pixelRect 直绘基准全图零阈值一致，留白内侧严格清空；其它缩放的预选梯度位置最大通道差 1/255，在预先保留的 2/255 采样误差内，不能据此称缩放后全图一致。测试使用当前生产附件代码和 FinalCopy Shader，仅把最终 CameraTarget 绑定替换为独立 RT，按纹理目标方向取 GPU 投影符号及源 UV；没有验证 Game 屏幕的原始 UV、真实 Screen / camera.pixelRect getter、Android CameraTarget / Display / Present、主游戏 Canvas 或功耗。手机画面仍由用户后续复测。原始日志、PNG 和对照结果保存在工程外 `mrp-mobile-viewport-repair`，未覆盖旧失败证据。

### 此前长屏离屏验证（历史）

2026-10-17 此前版本的长屏改动通过 Windows / Android 的 Editor / Player 四组源码编译，共 8 个程序集。该历史独立隐藏 D3D11 Editor GPU 检查共 60 次绘制：1080x2400、1220x2712，viewport y=.12 / .22，50% / 100% / 150%，1x / 4x，包含普通 Store、原生附件与 100% 原生直接输出。底部留白内侧无旧帧残留，选取的梯度位置误差为零；24 对原生 RT+Copy / 普通 Store+Copy 全图零阈值一致，4 对 1x 原生直绘与参考一致。4x 直接输出与 Copy 分别有 211 / 195 / 11 / 339 个像素不同，最大通道差 1/255，保留原图及差图，不计作逐像素一致。这些是修改前快照的结果，不算本次修复重新通过。

这 60 项历史 GPU 检查以显式输出 RT 替代 CameraTarget / Display，手动按给定宽高构造 viewport，最终拷贝采用纹理输出方向；没有读取 Android 实际 camera.pixelRect / pixelHeight 与 Screen 尺寸的关系，也没有覆盖 Game 屏幕的原始 UV、真实 backbuffer viewport / Y 方向或 Present。这正是本次手机回归的覆盖缺口。它们还未验证真实相机剔除、Canvas 或主游戏初始化。D3D11 不支持本项目的屏幕直接 Resolve 筛选，测试中的普通 RT Resolve 没有放宽生产后端门槛；本机未实际使用 memoryless，不能据此证明手机片上存储、长屏直出或功耗收益。

RT、CommandBuffer、NativeArray、Material 以及格式/MSAA 能力查询在配置稳定时复用；过滤变化只更新 sampler，不重建纹理。RT 丢失后重新准备，创建失败缓存输入条件以避免逐帧分配抖动。释放管线时统一释放并还原全局状态。

## Live2D MRP 适配

SDK 的中立数据与执行器 `CubismRenderingContext` / `CubismRenderingExecutor` 只依赖引擎类型；完整 SDK 执行器源码保留，但当前 MRP 只调用它的 `RenderSimple`。`Live2D.Cubism.MajRenderPipeline` 适配程序集反向引用 SDK 和 MRP；MRP 本体仍不引用 SDK、URP 或 Core RP。原 URP Feature 保留 GUID，位于独立且按包版本启用的 `Live2D.Cubism.URP` 程序集，卸载 URP 时不参与编译。

部分中立事件 / interceptor 保留 SDK 原有的 URP 命名空间，供 SDK 与可选适配器继续引用；命名空间不代表包依赖。SDK 的可选 URP Feature 在重建、释放时配对 Dispose，避免执行器持有的 Mesh / Material 遗留；三个旧业务 Cubism Feature 已删除，MRP 不调用它们。interceptor 编辑模式初始化直接取 sharedMaterial，运行时仍使用原 material 行为。

xxlb 的 MeshRenderer 使用 Picking 占位材质，实际绘制材质位于 DrawMaterial；直接跑普通 RendererList 不能替代 SDK 绘制。适配器在 Cull 前按当前相机过滤模型并暂时设置 `forceRenderingOff`，防止自动和手动画两次；在相机提交后的 finally 恢复原值，不改模型材质、enabled、layer 或判定时间源。

2026-10-16 起，背景视频不再挂接 Cubism interceptor。Init 的 BG 使用原 SpriteRenderer、BG 排序层及 Queue 2000 材质，在不透明阶段正常绘制；随后 SDK 直接绘制 Live2D，再绘制普通透明队列 / UI。BackgroundVideoController 不再按场景启用拦截器或关闭背景 Renderer，原播放 / 暂停与 Hide / Show 行为保留；仅供该分支使用的场景标签属性也已移除，场景枚举名称 / 数值保持不变。

- 当前桥接仅面向工程实际使用的 Normal/Over、无遮罩、无 Offscreen 模型；小小蓝白的 64 个部件全部满足这一条件。适配器不再包含 Common / Temporary / Mask 字段、RT 创建 / 缓存 / 释放、高级合成分支或 interceptor 调度，`RequiresIntermediateTarget` 恒为 false。未来换用遮罩 / 高级混合模型时需要另行适配，这些能力没有作为 MRP 回退预备。
- 无可绘制 Cubism 组时不添加绘制命令；可见模型按 SDK 内部排序直接 DrawMesh 到当前场景目标，不切换 RT、不复制、不因 Cubism 中断 Native Pass。相机视锥、layer、active、enabled、forceRenderingOff 和 opacity 过滤仍保留。
- 不创建 Cubism 工作 RT，也不执行 Common 的全屏合成；VideoPlayer 的解码纹理与 MRP 因 RenderScale / MSAA 需要的场景附件继续保留。当前模型绘制不会改变这些附件的分辨率、格式、MSAA 或 Resolve 策略。
- 绘制点与当前旧 URP 资产一致：opaque / Skybox 之后、普通透明队列之前。当前简单直绘不切换 RT，也不重新 Load 场景颜色或 Stencil；保持调用者当前 viewport、投影与尺寸 globals，Overlay 继续由引擎最后绘制。相机渲染器中仅在扩展显式请求中间目标时才执行恢复逻辑，当前 Cubism 桥接不走该分支。
- 不修改 Camera 本体的投影、viewport 或坐标换算；模型使用调用者当前目标与相机参数。

本轮最简直绘验证在隐藏 D3D11 Editor 中运行，未操作主项目 Play / Player。当前生产 MRP、SDK、桥接与完整业务程序集使用实际源文件通过 Windows / Android / iOS 的 Editor / Player 六组编译，共 24 项。GPU 使用与生产原文一致的相机渲染器与桥接器，通过实际 targetTexture 兼容路径比较原完整合成与新直绘：三种输出尺寸、1x / 4x，覆盖真实 64 部件模型、背景、透明 UI、额外前景深度试件、隐藏和无模型；60 次绘制及 54 项非像素语义断言通过，无 GPU 异常。这里改变的是离屏输出尺寸，没有放宽生产的相机资格条件来模拟屏幕 RenderScale。

当前工程为 Gamma：1x 旧新结果逐像素一致；去除额外前景试件后的背景 / 模型 / UI 组合，4x 的 50% / 75% 尺寸一致，100% 仅 2 个像素差 1 个 8bit 码值。Linear 使用屏幕代表性的 sRGB 目标时，1x 也一致，4x 不同像素少于约 0.1%，最大 RGB 差 12～15 码值、平均差小于 0.008 码值；alpha 均一致。额外不透明前景与模型相交的 MSAA 边缘也有差异：移除 Common 会改变提前 Resolve 再混合的覆盖计算，不能宣称所有遮挡边界逐像素一致。自定义 Linear UNorm8 输出还存在逐次颜色量化差异，原始失败对照保留，没有放宽阈值计为像素通过。原生屏幕路径、实际视频解码和移动真机功耗未在本轮运行；无效的原生离屏目标诊断不计为验证通过。

## URP 移除边界

资产仍返回 `UniversalPipeline` SubShader 标签，以兼容现有无光照 Shader；标签字符串不会创建 URP，也不是包引用。项目自定义 Shader 的 URP/Core include 已迁移到 `Library/`，MRP Unlit 代替直接使用的 URP Unlit，并保留 `_BaseMap`、`_BaseColor` 与透明混合等材质属性。库保留所需矩阵、UnityPerDraw ABI、传统实例化和采样约定，不移植 Lit、灯光、阴影或 DOTS 路径。

原命名整理阶段的 23 个项目 Shader 菜单名统一使用 `Maj/Common`、`Maj/UI`、`Maj/Notes`、`Maj/Effects` 和 `Maj/Unlit`；当前新增的默认 UI / Sprite 副本使用 `Maj/UI/Default` 与 `Maj/Sprites/Default`。内部 FinalCopy 使用 `Hidden/Maj/FinalCopy`，`MajRenderPipelineAsset.OnValidate` 的 `Shader.Find` 同步更新。只改变 Shader 声明名及对应字符串查找，保留 Shader 程序、Properties、文件位置和 `.meta` / GUID；现有材质按 GUID 继续引用同一 Shader。Live2D / TMP 的 SDK Shader 名称保留，以维持其自身查找约定。

初次迁移前，14 个启用场景的主程序/游戏内容静态可达 13 种依赖 URP 库的项目 Shader 和 1 种 URP Unlit，共 27 个材质；全量项目自定义 Shader 中共有 18 种包依赖。这部分在迁移后不再引用 URP/Core Shader 库。计入旧 Live2D SDK 时，场景初始完整统计为 16 种；项目 Shader 迁移后曾剩 2 种 SDK Shader 依赖，本次 Live2D 移植已将 SDK 全部 7 个包依赖 Shader 本地化。静态引用包含未激活对象和跨场景资源，不能等同于某一帧绘制数量。TMP 与 Unity 内置 Shader 继续使用引擎库。

逐 Shader、材质数量与场景/Resources 引用清单见 [ShaderDependencies](Documentation/ShaderDependencies.md)。

Live2D 的 7 个 URP 依赖 Shader 已改用本地 CubismMajCore / MRP Library，保留原 Shader 名称、属性和资源 GUID。SDK 主程序集不再引用任何 RP 程序集；MRP 与 Cubism 桥接也不需要 URP。GameManager / 画质设置中的 URP 分支已移除，三个旧业务 Cubism RenderFeature 及对应 meta 已删除；隐藏旧 Renderer 中的 Before Feature 子资产、列表和索引映射也同时清除，避免留下 Missing Script。当前 MRP 的设置、画质切换和 Live2D 绘制继续走原 MRP 分支。

共享 `Main Camera.prefab` 已单独移除 UniversalAdditionalCameraData，以及 Init 中仅针对该 Data 的启用覆盖项。Camera fileID、参数、AudioListener、ScreenPosCameraMover、Prefab / Scene meta 和原 Canvas 相机绑定全部保留；删除前后的四个非 Data 组件文档逐一相同。GlobalCamera 仍由 Init 场景添加并设置主相机。URP 的 Inspector 删除菜单会同时提议删除 Camera，不能使用该菜单完成单独清理。

Init 的主相机实例不能整体 Revert：`GlobalCamera` 是该实例额外添加的组件，不在公共 Prefab 中；撤掉后，相机在 Init 切换到 Title 时会被卸载。其他场景使用同一 Prefab 搭配 `PreviewOnlyCamera`，因此不要把 `GlobalCamera` 直接移入公共 Prefab。主相机应继承原 Camera（正交、Size 9.6、Depth -1），保留 GlobalCanvas 对它的引用，而不是再添加默认 Camera。

GraphicsSettings 中的 URP GlobalSettings 注册已清空。原可见 GlobalSettings / DefaultVolumeProfile 与其隐藏备份链均已备份到工程外后清理，其余隐藏旧管线预设保留。URP 包仍安装时，包内 Postprocessor 在域重载或导入 GraphicsSettings 时无条件 Ensure，因此可见资源和注册曾自动重生；确认用户卸载 URP 后，已再次清理重生的两份资产及 meta 和注册。包内共享资源没有手动删除。14 份材质中的 URP Editor AssetVersion 子文档已清理，保留 Material 主体、Shader、属性与 GUID。

`Assets/Rendering/Materials/Addctive.mat` 仍保存原 URP Particles/Lit 的 Shader GUID，卸包后成为未解析的 Shader 引用。全项目未发现其序列化引用、Resources / StreamingAssets 归属、AssetBundle 标签或代码按名加载；实际 `Hold_Effect` 粒子 Prefab 使用内置 Shader 的 `CircleMiss.mat`。按保留材质主体的要求，只删除 Addctive 的迁移标记，未更换其 Shader 或删除材质。卸包前，URP MaterialPostprocessor 确实在重新导入时又生成过 AssetVersion；卸包后已再次清掉该标记，当前材质主体与本次清理前保持相同。

URP 已从 manifest 和 packages-lock 移除；ShaderGraph 17.3.0 仍保留，并通过依赖保留 Core RP 17.3.0。主游戏 / MRP / 中立 Live2D Shader 没有 URP/Core 包路径 include。此前卸包检查的 Bee 编译产物有 MRP / Live2D MRP，已无 Universal 或 Live2D.Cubism.URP 程序集；SDK 的 `URP/Feature` 可选薄适配已由包版本约束停编。`URP/RenderingInterceptor` 保留于 SDK 完整 / 可选路径，当前 MRP 和背景业务已不调用它；不因目录名含 URP 就认定它有包依赖。未启用 SampleScene 的 CameraData / Light2D、未使用的 Addctive / URP Graph、隐藏旧预设和 URPProjectSettings 仍有遗留资源引用，尚未将这些闲置资源全部归档；不能把主程序脱离 URP 等同于全 Assets 零 Missing。`Library/` 的来源和使用条款见其许可证与说明。

23 个项目自定义 Shader 现已全部使用 HLSLPROGRAM。BrightnessAdjust、RadialMask、TouchHoldMask 在 Git 原版中原本使用 CGPROGRAM / UnityCG.cginc，后续已单独迁移到本地 MajCore：矩阵改为 TransformObjectToHClip，颜色由 fixed 改为 half，径向角度继续使用 float 及相同精度的 PI 常量；保持 Properties、Shader 名、采样方式、UV、Blend 和资源 GUID。Brightness 的 _Color 放入 UnityPerMaterial，_Brightness 仍通过 Props；Radial 的整组 Props 保留，用于 RawSpriteRenderer 的 MPB 颜色 / 进度；TouchHoldMask 保留 UnityPerDrawSprite 与 PerDrawSprite 的 SpriteRenderer 颜色 / Flip ABI，_RendererColor.a 仍为遮罩进度。没有改为乘顶点 alpha，也没有额外应用 Flip。该迁移不等于新增 SRP Batcher 优化或已测得功耗收益。

这三个 Shader 在隔离 Windows D3D11 Editor 的 Gamma / Linear、SRP Batcher 开关组合下，共 88 组旧 CG / 新 HLSL GPU A/B 逐字节一致；四种后端 D3D / GLES3 / Vulkan / Metal 的普通与 INSTANCING_ON 顶点 / 片元代表编译共 192 项通过。显式 DrawMeshInstanced 还与两个单独 DrawMesh 的相同矩阵 / 颜色 / 进度参考逐像素一致。保留的旧行为限制：TouchHoldMask 在 SRP Batcher 关闭的 SpriteRenderer 路径中，旧版与新版的 renderer alpha 都不驱动覆盖；开启时 alpha 0 / .25 / .5 / 1 的覆盖递增正常。当前实际 TouchHoldMask.mat 引用的是 RadialMask，旧 TouchHoldMask Shader 暂未发现资源引用。此次未运行主项目 Play、Player 或移动设备，移动端结果仅为编译验证。

迁移不改变视频解码、判定、对象池和游戏时间源。最初保留的背景视频拦截回调已在 2026-10-16 简化中移除，背景恢复普通 SpriteRenderer 绘制，Live2D 使用 SDK 简单直绘。

## Android 指定纹理压缩

Assets/Models/xxlb/小小蓝白.4096/texture_00.png、texture_01.png 和 Assets/Sprites/Game/EffectSprites/EffectAtlas.spriteatlasv2 配置显式 Android ASTC 4x4 平台覆盖。只改变 Android GPU 格式，保留原 Max Size 2048、透明通道、已有 Mipmap 设置及其他平台；图集继续不生成 Mipmap。细线、透明边缘与模型颜色需在设备画面中复核。

ASTC 4x4 的块为 16 字节 / 16 像素，基础容量约为 RGBA32 的 1/4，实际尺寸仍由纹理导入 / 图集打包结果决定。压缩比例不是逐帧带宽或功耗降低比例；不支持 ASTC 的设备可能解压到无压缩格式。外部皮肤继续走原运行时加载和打包路径，不受这三项导入配置影响。

## 验证与设备对照

2026-10-16 用户确认当前主游戏的视频与 Live2D 画面正常；最新最简直绘的隔离结果见上方 Live2D 节。以下是此前各阶段的历史记录，其中复杂合成、遮罩和 interceptor 测试不代表当前 MRP 仍接入这些能力，也不构成当前设备功耗数据。

布局回归的隔离 D3D11 Editor 验证通过 17 次 GPU 请求、57 个 UI / 世界几何 / Sprite 的 CPU-GPU 中心及尺寸对照、51 次屏幕射线与世界平面的反交。固定 SRP Batcher=true、Dynamic Batching=false；1080x1920、相机 Y4.2 / ortho9.6 的完整目标最大误差为 0.000061 像素，偏移 viewport 0 / .12 / .22 的复杂 1x / 4x 及视频绘制中心误差小于 .53 像素。真实 Sprite（980x980、PPU100、父世界 scale .01、子 localScale110）自动绘制和 interceptor 显式绘制均为 1078x1078 像素。旧版模型出视锥导致 BGA 漏画已在隔离测试重现并验证修复。这些测试显式驱动隔离 Editor 的 Canvas 布局，未复现用户主项目的所有界面缩放问题，不能代替其实际 Play 初始化上下文检查。

随后主项目的只读 Play 快照定位到另一项场景配置问题：实际主相机为新加的默认 Camera（透视、Size 5、Depth 0），Mover 的初始化缓存与相机一致，并未丢失。用户改回正交 / Size 9.6 后确认缩放恢复。之后 Revert 的 Title 快照显示 Camera.main=null、SceneSwitcher.MainCamera 已销毁、GlobalCamera 不存在；画面中的 UI 已转为 Overlay。恢复 Init 的原 Camera 场景绑定、Canvas 引用及实例上的 GlobalCamera 后，用户反馈恢复；新的 Game 快照确认主相机在 DontDestroyOnLoad、正交 9.6 / Depth -1、世界原点投到 (540,540)，GlobalCamera 与 Mover 缓存同一 Camera，Main_Display 世界中心回到 (0,0)。这些是本次再次移除 Data 之前的主项目 Editor Play 数据；Data 删除后的画面仍待重新载入 Init 回归。

后续五文件 URP 依赖清理使用主项目真实 Bee 编译配置，Windows / Android 的 Editor / Player 四组各编译 440 个业务源码，均通过；进一步剔除 RP / ShaderGraph / Live2D URP 薄适配等程序集引用后，四组再次通过，共 8 项最终编译、0 错误。三个已删除 Feature 的名称、脚本 GUID 与旧 Renderer 子资产 fileID 在 Assets（含隐藏目录）、Packages、ProjectSettings 中均无残留引用。此次为真实源码离线编译，没有启动 Unity 完整构建或游戏画面回归；不改变前一次 Live2D GPU 验证的适配器和 Shader 源码。

2026-10-15 Live2D 适配在隔离 Unity 6000.3.17f1 / Windows D3D11 Editor 中验证，未启动主工程、Player 或可见测试窗口。Gamma / Linear 各通过 32 次真实 GPU 请求、37 项深入断言，未出现渲染错误；覆盖实际 xxlb 的 64 个 drawable、普通 Native Pass 1x / 4x、复杂合成保留 4x、正常 / 反向遮罩、背景视频所用 interceptor 的合成 Sprite 绘制、隐藏 / 相机层 / 原 forceRenderingOff、禁用自动 Renderer 后显式绘制、128→256 工作 RT 重建，以及跨相机状态恢复。生产 MajCameraRenderer 真实 Cull / RendererList / Submit 路径同样参与验证；同帧上方不透明红标、SDK 模型和下方透明绿标的方向对照通过，Gamma 整图零差，Linear 复杂合成平均通道差小于 0.0003。

本地化 Live2D Shader 与原 URP Shader 在两种色彩空间合计 398 组 A/B、796 次 GPU 请求中逐像素相同。D3D / GLES3 / Vulkan / Metal 每种色彩空间的 192 个代表 Shader stage 均取得非空平台字节码。Windows / Android / iOS 的 Editor / Player 六组配置，分别编译 MRP、SDK 主程序集、MRP 桥接、URP 薄适配与三个业务 wrapper，共 30 项程序集编译通过；MRP / SDK 主程序集的编译引用不含 RP / Core / ShaderGraph 程序集。该轮验证时，210 个生产源码文件与隔离测试快照的 SHA256 一致。

后续资源 / 命名清理通过 14 份材质主体与 meta 不变、23 个 Shader 除声明名外原文与 meta 不变、44 个 Shader 声明名无重复、旧名称无生产字符串引用，以及 GlobalSettings / Profile GUID 无残留引用的静态检查；FinalCopy 的字符串查找已同步。该轮没有修改 Shader 程序，没有重复 GPU 验证，也没有启动主项目 Play 或完整构建。

当时完整桥接的验证限制：真实 xxlb 模型没有原生遮罩或 Offscreen 层级，遮罩使用实际 SDK Renderer 加隔离合成 fixture；Offscreen 的 Shader 数学与真实 RT 池分别验证，尚无实际嵌套 Offscreen 模型端到端覆盖。Scene View 只测 SceneView 类型相机的离屏请求，未测真实编辑器窗口合成。移动端编译与 Shader 字节码不代表 IL2CPP / Xcode 构建或真机 GPU 验证，也没有测当时游戏功耗、频率或带宽。当前最简桥接不接入模型遮罩 / 高级混合，不应把它们列为已经支持的回归项。

以下保留此前附件优化、纹理压缩与 Shader 库迁移阶段的验证记录。

Windows MSAA 重影修复以实际 Player.log 的 back-buffer MSAA 附件不支持错误，以及用户关闭 Resolve To Screen 后重影消失为依据；现在能力检查会让该平台自动保留普通 Resolve RT + FinalCopy。六组 Windows / Android / iOS Editor、Player C# 编译通过（iOS 为静态编译）。隔离 D3D11 Editor 通过公开 SubmitRenderRequest 取得真实 context 并提交 GPU 绘制，生产 BeginScene 的 1/2/4/8x MSAA 保持实际样本数，Resolve 到 1x RT 后使用 FinalCopy 输出到离屏目标；连续六帧、三个位置移动及半透明重复绘制没有旧位置残留，故意不清除的负对照能够检出残影。实测 supportsMultisampledBackBuffer 与 CanResolveToScreen 均为 false。此检查有真实 GPU 像素读回，但不是屏幕 Present 测试；此前仅附件描述/命令录制的检查未覆盖系统 back-buffer 的执行限制。

iOS Metal / memoryless MSAA 扩展通过 Windows / Android Editor、Player 及 iOS 的两组静态编译检查；iOS Player 使用已安装 iOS 模块的 arm64 IL2CPP UnityEngine 元程序集，不代表 Xcode / IL2CPP 构建完成。隔离 Windows D3D11 Editor 的 Gamma / Linear 各 48 组附件矩阵分别通过 1021 / 1028 项检查，包含 8 种受控创建结果、普通 RT 重试、失败清理、构造失败及拒绝缓存。本机不支持 memoryless，失败分支通过工厂控制验证，普通回退 RT 由 Unity 实际创建；尚未验证 Metal 真机的直接 Resolve、实际片上存储或功耗 / 带宽收益。本轮没有启动测试 Player。

直接 Resolve / ASTC 本轮通过 Windows / Android 的 Editor、Player 四组源码编译。隔离 Unity 的 Gamma 附件矩阵 48 组 / 827 项、Linear 回退矩阵 48 组 / 832 项通过，覆盖实际 RT 创建、缓存切换、Resolve 目标、Stencil 附件和直接路径不分配单采样颜色 RT。独立 Windows 验证 Player 构建成功，0 错误、0 警告。

Android ASTC 导入 / 打包检查 14 项通过：两张 Live2D 主图各为 2048x2048、12 mip、5,592,432 字节；EffectAtlas 保留 10 个 Sprite，打为一页 1024x1024、1 mip、1,048,576 字节。三项共约 11.67 MiB，同尺寸 / mip 的 RGBA32 为约 46.67 MiB。Windows Editor 软件解压后的 Live2D 静态边缘 alpha 平均误差约 1.26% / 1.15%，不是无损，也不代替 Android 设备显示验证。

该早期阶段的屏幕像素验证未完成：隐藏运行的测试 Player 没有触发任何相机绘制，不能把全黑截图计作通过；本轮新增的 Pad 5 Pro 独立 Probe 结果单独记录于 Android MSAA 调查文档，不倒算为该早期测试通过。直接路径的方向、Stencil 和 Overlay 由正常可见的 Player / 游戏构建复核；测试未修改主项目场景，也没有对 Android 功耗、频率或实际带宽作测量。

RenderScale 实现阶段已通过 Windows/Android 的 Editor、Player 四组源码编译；隔离 Unity 项目中的 98 项断言通过，覆盖五个真实预设加载、运行时覆盖与默认值隔离、尺寸/过滤/Mip Bias、实际 RT 创建与附件描述。该阶段的隔离小型 Windows Player 构建成功，0 errors、0 warnings。

Shader 库迁移后另验证了 Runtime/Editor 的实际程序集归属、六个管线资产加载、原 Shader 名称/属性与四个迁移材质的参数，以及 Unlit 编辑器的混合/裁剪状态。23 个 Rendering Shader 在 D3D、GLES3、Vulkan 的普通/声明支持的实例化和透明裁剪代表组合完成源码编译；移动片元还通过外部输出模式取得非空编译结果。SRP 环境初始化后，MRP Unlit 的 SRP Batcher 兼容查询返回 `0 / OK`。这些是编译与兼容信息，不是实际批处理命中率或帧耗时。

原 Shader 库迁移阶段的这些检查没有运行完整游戏、提交相机绘制或测试 Android 设备，不能证明最终画面、实际带宽或功耗已通过。尤其需要复核缩放后的画面方向、Camera Canvas 与 Overlay UI，以及不同设备支持的真实 MSAA 样本数。

功耗对照使用相同 Release 构建，关闭 Development Build、Autoconnect Profiler 和 Script Debugging，并统一实际分辨率、MSAA、帧率、谱面/BGA、亮度与热状态。先对照 50%/75%/100% 实际附件尺寸、UI 清晰度、Stencil 与画面方向，再比较稳定区间功耗和带宽。
