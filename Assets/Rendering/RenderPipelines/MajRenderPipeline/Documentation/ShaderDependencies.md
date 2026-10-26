# Shader 静态依赖审计

## 2026-10-16 背景视频与 Cubism 合成解耦

BackgroundVideoController 已移除 Cubism interceptor 字段、场景反射列表和切换回调；Init 只删除 BG 上的 interceptor 组件及对应引用，保留 SpriteRenderer、材质、排序、Transform、VideoPlayer 和全部 Camera 文档。仅供该业务使用的 ContainsCubismComponentAttribute 与枚举标记已删除，枚举成员与顺序不变。背景按原 Queue 2000 正常绘制，当前小小蓝白的 64 个 Normal/Over 部件走 SDK 直绘，不再因视频排序创建 Common / Temporary / Mask 或执行 Common 全屏合成。

MRP 适配器只保留当前模型需要的 RenderSimple 直绘，不再包含 Common / Temporary / Mask 字段和创建 / 缓存 / 释放，也不接入高级合成或 interceptor。SDK 未用到的高级功能源码保留，但未作为当前 MRP 的回退；以后使用这些模型需另行适配。没有改 Shader、材质、纹理压缩或 RT 格式 / MSAA，也没有删除视频解码纹理和 MRP 必要场景附件。流程与验证边界见 [管线说明](../README.md#live2d-mrp-适配)。

24 项跨平台程序集编译、60 次隔离 GPU 绘制与 54 项非像素语义检查通过；当前 Gamma 的普通背景 / 模型组合 1x 一致，4x 最大仅 2 像素差 1 码值。Linear / MSAA 前景边界和自定义 UNorm8 目标的像素差异已单独保留，不能据绘制与资源检查通过宣称所有画面逐像素一致。未启动主工程 Play、Player 或移动真机。

## 2026-10-15 half 精度审计

全 Assets 共扫描 64 个文件：45 Shader、9 cginc、6 hlsl 和 4 ShaderGraph。6 个自定义 Shader 只把有界亮度 / 对比度、HSV 分段后的 RGB 中间结果改用 half；8 个 UI Shader 仅增加保留 float 的原因注释；Live2D / TMP 共 17 个文件、87 处 fixed 类型统一为 half。当前 HLSL / CG 有效代码中的 fixed 为 0；没有批量替换 float，也没有改变 ShaderGraph、贴图 / RT 格式或全局精度模型。

位置、UV、矩阵、时间、角度、遮罩 / SDF 边缘和原缓冲接口仍保持原精度，Live2D 复杂混合的小 alpha 反预乘仍使用 float。现代后端的 fixed / half 等价已用本机完整 HLSLSupport 预处理验证：21 组配置、147 对类型映射均相同；因此 SDK 清理本身不应计为性能优化。Properties、Shader 名和 meta / GUID 对照不变，缓冲字段的精度别名等价性与顺序保留。

6 个颜色候选的 Gamma / Linear 隔离 GPU 对照共 224 组通过，最大单通道差 1 个 RGBA8 码值；四后端 384 项 stage / keyword 编译通过。Live2D Mask / TMP 另完成 15 个 Shader、23 个 Pass（含 Surface 生成 Pass）的四后端 984 项代表编译，非空字节码且零警告 / 错误。另做约 2.58 亿个 IEEE FP16 数值采样，最大绝对差 0.001953125，无新增非有限值，alpha 表达式不变。桌面默认 half 不保证实际 FP16，后端编译也不是移动真机画面 / 功耗证明。精度边界与验证说明见 [管线说明](../README.md#shader-精度)。

## 2026-10-15 三个旧 CG Shader 本地 HLSL 迁移

BrightnessAdjust、RadialMask、TouchHoldMask 已迁移为 HLSLPROGRAM + MajCore，项目自定义 Shader 当前 23 / 23 均使用 HLSLPROGRAM。保留 Shader 名、Properties、meta / GUID、原采样与混合公式；矩阵使用本地 TransformObjectToHClip，颜色改为 half，PI 保留原 float 字面值精度。Radial 的 MPB / Props 属性和 TouchHold 的 UnityPerDrawSprite / PerDrawSprite 颜色 / Flip 数组保持原通道。

隔离 Windows D3D11 Editor 的 Gamma / Linear 与 Batcher 开关共 88 组旧新 GPU 对照逐字节一致，D3D / GLES3 / Vulkan / Metal 共 192 个代表编译通过；显式实例化与逐实例绘制参考也一致。TouchHoldMask 在关闭 Batcher 时 renderer alpha 不驱动覆盖，是旧 CG 和新 HLSL 共同的旧路径限制；开启时正常。本轮仅迁移并保持原行为，未扩大为 Sprite 上传路径修复，没有运行主项目 Play / Player 或移动真机。

## 2026-10-15 卸载 URP 后复核

manifest 和 packages-lock 已无 URP；ShaderGraph 17.3.0 仍安装，并保留其 Core RP 17.3.0 依赖。当前活动 Bee 的 140 个程序集包含 MajRenderPipe / Live2D.Cubism.MajRenderPipeline，没有 Universal 或 Live2D.Cubism.URP；SDK 的可选 URP Feature 已受包版本约束停编。卸包后的日志未发现 C# / Shader 编译错误，用户反馈 Play 画面正常；本轮未启动主项目 Play 或完整构建。

URP 卸载前自动重生的 GlobalSettings / 默认 Profile、GraphicsSettings 注册与 Addctive 的 AssetVersion 已在卸包后再次清理。Addctive 的原 URP Shader GUID 按保留主体要求保留，当前为闲置材质的未解析 Shader 引用；未启用 SampleScene、URP ShaderGraph 和隐藏旧预设等闲置残留仍单独列入清理边界，不能据主运行链正常宣称全 Assets 没有 Missing。

卸包刚完成时，23 个自定义 Shader 为 20 HLSLPROGRAM + 3 CGPROGRAM；后三个为 Common/BrightnessAdjust、Common/RadialMask、Notes/TouchHoldMask，Git 原版和命名前备份均已使用 CG。那次改名没有改变程序块类型；之后对这三者的独立迁移与验证见上方最新补充。

## 2026-10-15 资源清理与命名补充

GraphicsSettings 的 URP GlobalSettings 注册已清空。可见的 GlobalSettings / DefaultVolumeProfile 在本轮开始前已不存在；残留的隐藏 GlobalSettings / 默认 Profile 链已备份至工程外后移除，包内共享资源保留。14 份材质里的 URP Editor AssetVersion 子文档已清理，Material 主文档（含 Shader / 属性）和 meta / GUID 完整保留。

23 个项目自定义 Shader 统一到 `Maj/Common`、`Maj/UI`、`Maj/Notes`、`Maj/Effects`、`Maj/Unlit`；内部复制使用 `Hidden/Maj/FinalCopy`，生产 `Shader.Find` 同步。Shader 程序与 Properties 未改，44 个 Shader 声明名没有重复，Live2D / TMP SDK 名称保留。下方表格的显示名已同步当前命名，历史 JSON 仍保存当时的名称与数量，不能当作本轮重新生成的依赖快照。

另有 `Assets/Rendering/Materials/Addctive.mat` 引用 URP Particles/Lit，但全工程未发现材质 GUID 或按名称 / 路径加载引用；它不在 Resources / StreamingAssets，也没有 AssetBundle 标签。实际 Hold_Effect 使用 CircleMiss 材质。Addctive 主体按要求保留，暂未替换 Shader 或删除；URP 包尚在时，重新导入该 URP 材质可能由包的升级器再生成 AssetVersion。

## 2026-10-15 Live2D 迁移补充

Live2D Resources 的 7 个 URP/Core 依赖 Shader 已改用本地 `CubismMajCore.cginc` 和 MRP Library，保留原 Shader 名称、属性及资源 GUID；原本独立的第 8 个 Shader 保持原实现。对 Live2D Resources、Assets/Rendering/Shaders 和 Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline 再次扫描，URP/Core 包路径 include 均为 0。因此下方历史快照中剩余的 2 种场景 Live2D Shader 也已解除 Shader 库依赖；本轮未重新生成旧 JSON 的场景、材质与 Resources 数量快照。

SDK 主程序集现在只引用 Unity.InputSystem，渲染执行器和数据上下文不引用 RP 程序集。MRP 桥接单独引用 SDK 与 MRP；原 SDK URP Feature 保留 GUID 并移入按包版本启用的独立适配程序集。后续已移除业务 GameManager / 设置代码中的 URP 分支，并删除三个旧业务 Cubism RenderFeature 与旧 Renderer 对它们的引用。共享 Main Camera 的 URP AdditionalCameraData 及 Init 中对应的启用覆盖项已单独移除；原 Camera、参数、场景引用和 GlobalCamera 保留。GlobalSettings 清理及用户之后卸包的状态见上方补充；未启用样例等闲置资源仍待归档。实现与验证边界见 [管线说明](../README.md#live2d-mrp-适配)。

## 初次主程序迁移快照

以下保留初次项目 Shader 迁移时的静态结果，对应当时的 JSON 和断言；其中 Live2D 尚未迁移的描述仅代表该历史阶段。

### 迁移前后对比

| 静态依赖范围 | 迁移前 | 迁移后 |
|---|---:|---:|
| 14 个启用场景的全部 URP/Core Shader（含 Live2D） | 16 | 2 |
| 其中主程序 Shader（排除 Live2D） | 14 | 0 |
| 其中项目自定义 Shader | 13 | 0 |
| 其中 URP 自带 Shader | 1 | 0 |
| 对应主程序场景可达材质 | 27 | 0 |
| Test 场景 URP/Core Shader | 1 | 0 |
| Assets/Rendering 项目 Shader 的 URP/Core 依赖 | 18 | 0 |

四个 URP Unlit 材质均已切换到 Maj/Unlit，Shader GUID 为 87ecd25dd76d47958dfe85b30da89add；其中两个仍是原来的场景可达材质。场景可达材质数量仍为 49，没有新增未知 Shader 或重复 GUID。

Assets/Rendering/Shaders 与 Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline 内的 Shader/HLSL/CGInclude/Compute 对 URP/Core RP 的包路径 include 数为 0；14 个场景的主程序 Shader 依赖数也均为 0。

Live2D Resources 仍有 8 种 Shader，其中 7 种依赖 URP/Core，包含场景静态闭包中的 TransparentPicking、CompatibleBlend 2 种。因此完整场景静态依赖不是零，而是由 16 降到 2；主程序范围由 14 降到 0。首次分类表将 Live2D 单列，须同时看 JSON include 字段，不能把分类名称当成不依赖 URP 的证据。

旧 GlobalSettings 链还保留 57 种 URP、9 种 Core RP Shader。Live2D 的 URP C# 适配器也仍存在，因此这次主程序 Shader 移植不等于已经可以删除 URP 包。

迁移前 JSON/MD 的 SHA256 与首次快照完全一致。以上检查共 16 项，通过 16 项。这里只做静态检查，没有启动 Unity 或验证设备画面。


本报告只读扫描当前 BuildSettings 启用场景（含 Test）以及 Resources，递归读取序列化 GUID、材质子资产和 Shader include。静态可达不代表当前帧实际绘制。

## 分类计数

| 范围 | 分类 | Shader 数 |
|---|---|---:|
| scene | Live2D shader | 2 |
| scene | Project shader independent of URP/Core | 15 |
| scene | TMP shader | 2 |
| scene | Unity built-in shader | 1 |
| test | Project shader independent of URP/Core | 1 |
| test | TMP shader | 2 |
| resources | Live2D shader | 8 |
| resources | Project shader independent of URP/Core | 2 |
| resources | TMP shader | 3 |
| globalSettings | Core RP package shader | 9 |
| globalSettings | Project shader independent of URP/Core | 1 |
| globalSettings | URP package shader | 57 |

启用场景：14；场景静态可达材质（含嵌入子资产）：49；Resources 根文件：1025。

## 主程序 Shader 清单

| Shader | 类别 | 场景 | Test | Resources | 场景材质/总材质 | 路径 |
|---|---|---|---|---|---:|---|
| Maj/Common/BrightnessAdjust | Project shader independent of URP/Core | 否 | 否 | 否 | 0/0 | Assets/Rendering/Shaders/Common/BrightnessAdjust.shader |
| Maj/Common/BrightnessAdjustOpaque | Project shader independent of URP/Core | 是 | 否 | 否 | 1/2 | Assets/Rendering/Shaders/Common/BrightnessAdjustOpaque.shader |
| Maj/Common/Opaque | Project shader independent of URP/Core | 是 | 否 | 否 | 2/2 | Assets/Rendering/Shaders/Common/Opaque.shader |
| Maj/Common/OpaqueMask | Project shader independent of URP/Core | 否 | 否 | 否 | 0/0 | Assets/Rendering/Shaders/Common/OpaqueMask.shader |
| Maj/Common/RadialMask | Project shader independent of URP/Core | 是 | 否 | 否 | 1/1 | Assets/Rendering/Shaders/Common/RadialMask.shader |
| Maj/Effects/BrightnessAdjustAnimate | Project shader independent of URP/Core | 是 | 否 | 是 | 1/1 | Assets/Rendering/Shaders/BrightnessAdjustAnimate.shader |
| Maj/Effects/ColorAdjustEffect | Project shader independent of URP/Core | 是 | 否 | 否 | 2/2 | Assets/Rendering/Shaders/ColorAdjustEffect.shader |
| Maj/Effects/HoldShineShader | Project shader independent of URP/Core | 是 | 否 | 是 | 1/1 | Assets/Rendering/Shaders/HoldShineShader.shader |
| Hidden/Maj/FinalCopy | Project shader independent of URP/Core | 否 | 否 | 否 | 0/0 | Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime/Shaders/MajFinalCopy.shader |
| Maj/Unlit | Project shader independent of URP/Core | 是 | 是 | 否 | 2/4 | Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime/Shaders/MajUnlit.shader |
| Maj/Notes/FrontToBackComposite | Project shader independent of URP/Core | 否 | 否 | 否 | 0/0 | Assets/Rendering/Shaders/Notes/FrontToBackComposite.shader |
| Maj/Notes/FrontToBackSprite | Project shader independent of URP/Core | 否 | 否 | 否 | 0/0 | Assets/Rendering/Shaders/Notes/FrontToBackSprite.shader |
| Maj/Notes/Common | Project shader independent of URP/Core | 否 | 否 | 否 | 0/1 | Assets/Rendering/Shaders/Notes/Common.shader |
| Maj/Notes/TouchHoldMask | Project shader independent of URP/Core | 否 | 否 | 否 | 0/0 | Assets/Rendering/Shaders/Notes/TouchHoldMask.shader |
| Maj/UI/BlurCircleFade_Gaussian_Fixed | Project shader independent of URP/Core | 是 | 否 | 否 | 1/1 | Assets/Rendering/Shaders/UI/BlurCircleFade_Gaussian.shader |
| Maj/UI/Circle Mask | Project shader independent of URP/Core | 是 | 否 | 否 | 2/3 | Assets/Rendering/Shaders/UI/CircleMask.shader |
| Maj/UI/CircleMaskWithOutlineShadow | Project shader independent of URP/Core | 是 | 否 | 否 | 2/2 | Assets/Rendering/Shaders/UI/UICircleOutlineShadow.shader |
| Maj/UI/List/CoverShader | Project shader independent of URP/Core | 是 | 否 | 否 | 3/3 | Assets/Rendering/Shaders/UI/List/CoverShader.shader |
| Maj/UI/MajRadar | Project shader independent of URP/Core | 是 | 否 | 否 | 1/1 | Assets/Rendering/Shaders/UI/MajRadar.shader |
| Maj/UI/Majdata Triangle Grid Transition | Project shader independent of URP/Core | 是 | 否 | 否 | 1/1 | Assets/Rendering/Shaders/UI/TriangleGridTransition.shader |
| Maj/UI/Rect Mask | Project shader independent of URP/Core | 否 | 否 | 否 | 0/2 | Assets/Rendering/Shaders/UI/RectMask.shader |
| Maj/UI/Rounded Rect | Project shader independent of URP/Core | 是 | 否 | 否 | 7/10 | Assets/Rendering/Shaders/UI/RoundedRect.shader |
| Maj/Effects/MaimaiColorEffect | Project shader independent of URP/Core | 是 | 否 | 否 | 1/1 | Assets/Rendering/Shaders/MaimaiColorEffect.shader |

TMP 场景可达 TextMeshPro/Distance Field（16个材质子资产）和 TextMeshPro/Mobile/Distance Field（2个材质子资产），均不依赖 URP/Core。Unity 内置 Shader 以 fileID 10753 保留；此外引擎默认内置材质仍有隐式 Shader 依赖，不从 YAML 猜测。

## 主程序运行时引用补充

- `Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime/MajCameraRenderer.cs:51`：`_finalCopyMaterial = new Material(finalCopyShader)`；场景脚本可达=False；Editor 路径=False。
- `Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime/MajRenderPipelineAsset.cs:82`：`_finalCopyShader = Shader.Find("Hidden/Maj/FinalCopy");`；场景脚本可达=False；Editor 路径=False。
- `Assets/Scripts/Rendering/RawSpriteResources.cs:170`：`var material = new Material(source)`；场景脚本可达=False；Editor 路径=False。
- `Assets/Scripts/Scenes/Test/IOListener.cs:45`：`_materials[i] = new Material(Shader.Find("Sprites/Default"));`；场景脚本可达=True；Editor 路径=False。

## 边界

- Unity 内置材质的内部 Shader 依赖无法仅从 YAML 完整还原；保留 built-in fileID，不猜测数量。
- 运行时克隆已有材质不会自动增加 Shader 类型；外部动态加载仍需运行时确认。
- 下方原始快照生成时，Live2D 未纳入当时的项目 Shader 迁移，Resources 仍静态入包；此限制只描述历史快照。后续 Live2D 本地化和简单直绘的当前状态见文件顶部更新。
- 未解析 GUID 和 include 的逐项清单保留在 JSON。
- 主程序未运行，未修改项目资产。
