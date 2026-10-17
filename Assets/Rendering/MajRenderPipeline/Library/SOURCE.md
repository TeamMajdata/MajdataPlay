# Unity 源码来源与迁移边界

本目录从项目当前安装的 Unity Graphics 17.3.0 提取传统实例化、UnityPerDraw 布局、精度定义及全屏三角形接口，并添加 MRP 注释和最小纹理接口。

原始来源：Unity-Technologies/Graphics，URP ShaderLibrary/UnityInput.hlsl、Core ShaderLibrary/UnityInstancing.hlsl、Common.hlsl 及 Runtime/Utilities/Blit.hlsl。版权归 Unity Technologies ApS；采用原包的 Unity Companion License，见 LICENSE-Unity.md。未将这些来源误标为 MIT。

保留的 UnityPerDraw 字段（包含 lightmap / SH 占位）用于遵守引擎 SRP Batcher 上传布局，不表示 MRP 实现灯光、阴影或 GI。传统 GPU Instancing 同理保留引擎 ABI。DOTS Instancing 显式不支持。

所有实际 include 只指向本目录或 Unity Editor 内置 HLSLSupport.cginc。没有复制 URP Lit、Lighting、Shadows、Color 或完整 Common 库；UI 的裁剪、颜色和透明逻辑仍在各自原 Shader 中。

通用 MajBlit 保留原 Blitter 的 API UV 方向和 _BlitScaleBias。MRP 自己的 MajFinalCopy 具有独立相机目标方向约定，不应相互替换。
