# Graphics State Collection 替代 Shader Warmup 审计

审计日期：2026-10-23。目标版本为 `ProjectSettings/ProjectVersion.txt` 的 Unity **6000.3.17f1**。本次只检查 API、现有渲染代码和构建流程，没有接入采集、预热或回退实现，没有生成 `.graphicsstate` 资源，也没有进行 Player、GPU 或设备测试。

## 结论

`UnityEngine.Experimental.Rendering.GraphicsStateCollection`（下称 GSC）值得作为后续替代方案：它可以保存实际绘制遇到的 Shader 变体及相关图形状态，在加载阶段预先创建对应的 GPU 程序 / PSO。MRP 有 MSAA、原生 RenderPass、不同顶点布局与 Stencil 状态，仅列出 Shader 名称不足以覆盖这些组合。[Unity GSC API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.Rendering.GraphicsStateCollection.html)

推荐方向是“按目标平台和 Graphics API 采集实际使用的有限集合，在加载界面按需预热”，而不是枚举所有 Shader / 所有状态。正式替换仍需解决采集覆盖、构建时变体保留、数据版本管理和真机验证。它仍是 **Experimental API**；当前没有证据证明替换 API 就能修复此前的原生崩溃。

本轮清理调试功能后，生产入口恢复为 `SceneSwitcher.SwitchSceneCoreAsync` 在进入 Game 前直接调用 `Shader.WarmupAllShaders()`。本报告描述未来选择，不表示已更改该入口。

## 与现有预热的区别

| 方式 | 描述 | 对本项目的影响 |
| --- | --- | --- |
| `Shader.WarmupAllShaders()` | 对当前已加载 Shader 的全部变体执行预热。 | 范围较宽，且调用发生在 Game 场景加载前，不能据此保证之后动态加载的资源已经覆盖。 |
| `ShaderVariantCollection` | 维护指定 Shader / Pass / keyword 组合。 | 可以缩小范围，但不完整描述实际顶点、附件与原生 subpass 状态。 |
| `GraphicsStateCollection` | 将变体与实际图形状态关联后预热。 | 更适合 Vulkan / Metal / D3D12 的实际 PSO，覆盖情况取决于采集场景。 |

Unity 6000.3 手册将旧预热 API 列为 D3D11 / OpenGL 等后端的可选方案，并说明在 Vulkan、D3D12、Metal 上缺少图形状态可能创建不匹配的 PSO。预热的目标是减少首次使用时的编译停顿，不会降低稳定运行中的像素填充量、纹理带宽或常态 GPU 功耗。[其他 Shader 预热方式](https://docs.unity3d.com/6000.3/Documentation/Manual/shader-prewarm-other.html)

## 本机 API 与平台范围

本机通过只读反射检查以下程序集，确认类型和成员真实存在，没有实例化 Unity 对象或调用原生预热：

```text
C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll
```

确认的成员包括 `BeginTrace`、`EndTrace`、`SaveToFile`、`LoadFromFile`、`WarmUp`、`WarmUpProgressively`、`variantCount`、`totalGraphicsStateCount`、`completedWarmupCount`、`isWarmedUp`，以及平台、API、Quality 名称和版本字段。`GraphicsState` 包含 `vertexAttributes`、`attachments`、`subPasses`、`sampleCount`、`renderState` 等字段。官方 6000.3 [C# 绑定源码](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Runtime/Export/Graphics/GraphicsStateCollection.bindings.cs) 也显示这些入口最终进入 Unity 原生实现；绑定源码不是原生实现的完整公开源码。

| 后端 | 官方定义的能力 | 接入时的边界 |
| --- | --- | --- |
| Android Vulkan | 支持 GSC 的图形状态 / PSO 路径。 | 必须在 Android 实际绘制路径采集并检查能力，不能用 Windows Vulkan 结果代替。 |
| iOS / macOS Metal | 支持 GSC 的图形状态 / PSO 路径。 | 与 Vulkan 分开采集；本项目 iOS 直接 Resolve 和 macOS 路径也不相同。 |
| Windows D3D12 | 支持 GSC 的图形状态 / PSO 路径。 | 只为实际启用 D3D12 的构建准备对应数据。 |
| Windows D3D11 | API 可用，自动回退为传统 Shader 预热。 | 不能宣称取得与 Vulkan 相同的完整 PSO 状态收益。 |
| OpenGL / GLES / WebGL | API 可用，自动回退为传统 Shader 预热。 | 与 Vulkan 的优化目标和验证分别处理。 |

这些平台行为来自 [PSO tracing 项目要求](https://docs.unity3d.com/6000.3/Documentation/Manual/shader-pso-introduction.html)，自动回退在 Unity 6.1 及以后提供。API 的存在不代表当前设备能记录完整状态；`BeginTrace()` 的返回值需要检查，图形状态仅在 `SystemInfo.supportsParallelPSOCreation` 为真时采集。[BeginTrace API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.Rendering.GraphicsStateCollection.BeginTrace.html)

## MRP 需要覆盖的状态

以下为生产源码审计推论；尚未取得 GSC 文件或统计真实去重后的 PSO 数量。

| 当前路径 / 资源 | 需要覆盖的原因 | 生产证据 |
| --- | --- | --- |
| MSAA 关闭及实际 2x / 4x / 8x | 样本数属于图形状态；请求 8x 可能由设备降为 4x。 | `Runtime/MajCameraRenderer.cs`、`MajCameraAttachments.cs`。 |
| 普通屏幕直绘、MSAA 直接 Resolve | 原生附件与 subpass 配置不同；Android Vulkan 使用每帧取得的显示缓冲句柄。 | `MajCameraAttachments.BeginScene` / `GetScreenResolveTarget`。 |
| RT + FinalCopy、录制、兼容路径 | 中间颜色 Resolve 后还有独立的单采样 Copy Pass；录制可能强制进入该路径。 | `MajCameraRenderer.RenderNative` / `RenderIntermediateCompatible` / `RecordCaptureActions`。 |
| 深度、Stencil、透明与 A2C | Render state 与颜色 / 深度格式会影响状态组合；只采集无 Mask 菜单不够。 | `MajCameraAttachments`、项目 UI / Opaque / Notes Shader。 |
| UGUI、TMP、RawSprite、SpriteRenderer、Live2D、粒子 | 各自有不同的顶点输入、关键字与材质状态。 | `RawSpriteResources.BuildSimpleMesh` 明确使用 Float32 Position / UV 和 UNorm8 Color；FinalCopy 则使用无顶点缓冲的 `SV_VertexID` 三角形。 |
| 相机 UI 与 Overlay UI | Overlay 在场景绘制结束后由引擎处理，不属于场景 MSAA 附件。 | `MajCameraRenderer.RenderNative` 的结束恢复与项目 Canvas。 |

Unity 的 [GraphicsState 定义](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.Rendering.GraphicsStateCollection.GraphicsState.html) 对应这些采集维度。不要手写一个“默认顶点布局 + 默认 RenderPass”来代表全部绘制；也不要为每张封面或每个音符创建一个集合。

当前原生场景 Pass 只有一个 subpass，`inputs` 显式为 `new AttachmentIndexArray(0)`；FinalCopy 是普通纹理采样。对 `Assets/Rendering/Shaders`、MRP 库与 Live2D Shader 搜索，未发现 `FRAMEBUFFER_INPUT`、`LOAD_FRAMEBUFFER` 或 `subpassInput` 声明。这说明当前这些生产源码没有要求 input attachment，不说明全量 Warmup 遍历到的所有内置 / 包 / 编辑器变体也都没有要求。

此前“Shader requires 1 input attachments, but the subpass only has 0”与此差异有关联的可能性，但没有具体 Shader / Pass 与可重现样本，不能将其认定为唯一崩溃原因。GSC 能携带实际 subpass 状态，是评估理由，不能当作修复证据。

### 画质档与动态资源

游戏画质来自运行时 `RenderingOptions` / MRP 覆盖，不等同于 Unity Quality 列表。GSC 的 `qualityLevelName` 无法单独区分节能、性能、美观、精致与自定义设置；后续采集清单应记录实际 MSAA、目标格式、直出 / Copy、录制状态和相关关键字。

RenderScale 主要改变附件尺寸及是否经过 Copy，不意味着 10%～200% 每个步长都要有独立集合。纹理精度是采样偏移，常规封面 / 皮肤换图也通常不改变 PSO；只有改变 Shader、关键字、顶点布局、混合 / Stencil / 附件等状态时才需要新增组合。以上需要用采集结果验证去重情况，不能提前承诺集合规模。

`CustomSkin` 运行时构建纹理 / 图集，`RawSpriteResources` 从源材质克隆并绑定纹理；未知皮肤图片本身不要求提前穷举。仍要覆盖 Simple / Sliced / Tiled、翻转、裁剪、实例化与混合等真实绘制模式。TMP 的动态字形生成、字体纹理上传、视频解码与纹理上传也不会被 GSC 代替；它只针对相应绘制使用的程序与图形状态。

## 采集、CI 与资源维护建议

1. 将采集作为独立开发流程，在 Development Player 上使用实际 MRP 从启动到设置、选曲、游玩、结算完整绘制。至少遍历无 MSAA、4x MSAA、可用直出、强制 Copy、录制、UI Mask / TMP / Live2D / 不同音符与皮肤绘制模式。按平台和 Graphics API 分别保存；不把每台设备都强制变成不同集合。[Unity 采集流程](https://docs.unity3d.com/6000.3/Documentation/Manual/shader-pso-trace.html)
2. `.graphicsstate` 保存的是用于后续预热的集合资料，不能当成一份在所有 GPU 上直接使用的驱动二进制缓存。应在目标设备加载和预热；Shader、Unity 版本、布局或管线路径变更时重新采集 / 补采，并为资源附带项目版本与内容标识。官方允许在已有集合上继续 Trace，补充未出现的组合。
3. 现有 `.github/workflows/main.yml` 负责构建与打包，没有 GSC 采集或资源打包步骤。Linux CI 构建 Android 包不等于在 Android Vulkan 上运行采集；Editor / `-nographics` 的导入成功也不等于 PSO 已被实机预热。后续需要先取得目标 Player 采集的资源，再由 CI 校验版本、资源存在与构建可加载性。
4. 单独审计 shader stripping。收集到组合或 `.graphicsstate` 文件不应直接视为“目标构建已保留全部变体”的证明；须检查 Unity 构建结果并验证运行时缺失行为。新增预热资源有明确项目用途后才纳入版本控制，临时日志、设备资料和驱动缓存继续留在工程外。
5. 文件获取可用原生 `SaveToFile` / `LoadFromFile` 或 `SendToEditor`，结果均要检查；Android 不能假设包内 StreamingAssets 是普通可直接传入的磁盘路径。未来如需托管复制，使用项目 `MajdataPlay.IO.Storage.FileSystem`，并遵守平台资源访问与主线程生命周期边界。[SaveToFile](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.Rendering.GraphicsStateCollection.SaveToFile.html)、[LoadFromFile](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.Rendering.GraphicsStateCollection.LoadFromFile.html)

## 预热时机与验证门槛

后续可在 Init 或 Game 加载界面选择与实际平台 / API / 路径匹配的集合。通过 `WarmUpProgressively` 分批调度并观察 `JobHandle` / 完成计数，比每次进歌再次遍历全部 Shader 更可控；“分批”控制数量，不是保证每批耗时相同。资源未加载、集合损坏 / 过期、平台不匹配和不支持完整状态采集时，都需要清楚定义继续按需创建程序的行为，不能静默反复调用同一个失败入口。[Unity PSO 预热流程](https://docs.unity3d.com/6000.3/Documentation/Manual/shader-prewarm.html)

验收至少包括：目标机首次冷启动、重复进歌、画质 / 输出切换、录制前后、Home 恢复以及资源重载；确认绘制正常，并比较加载耗时、首次音符 / 特效帧耗时、预热内存峰值及运行中 `Shader.CreateGPUProgram` / PSO 创建的残留。Release 预热和 Development 采集分开测量，不用 Development 的功耗代表正式发布功耗。

旧崩溃栈位于 Unity 原生 Shader pass / keyword 转换相关代码。GSC 同样最终进入原生 Shader / PSO 创建，公开 C# 绑定无法证明绕开同一段关键词处理；没有稳定复现就无法做修复前后对照。因此本次只能得出“技术路线更准确、值得隔离试验”，不能得出“旧崩溃已定位或保证不再发生”。

## 本次验证记录

- 已读当前 SceneSwitcher、MRP 相机 / 附件、FinalCopy、RawSprite 资源构建、动态皮肤与 CI 源码；未修改它们。
- 已对安装的 6000.3.17f1 CoreModule 做只读 API 反射；确认类型和所列方法存在。
- 已核对 Unity 6000.3 官方手册、API 文档与公开 C# 绑定；在线 6000.3 文档会持续更新，不等同于固定补丁版原生实现的测试结果。
- 未开始 Trace / WarmUp，未接入 GSC，未测试数据跨构建兼容性、真实图形状态覆盖、Android / iOS / Windows Player 或节能收益。
