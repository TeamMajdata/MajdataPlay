Shader "Hidden/Maj/FinalCopy"
{
    // 复制/放大场景单采样颜色；MSAA 由前一个 Pass resolve，此处不处理深度或后处理。
    // 不声明 RenderPipeline 标签，也不引用 URP/Core，供 MRP 独立使用。
    SubShader
    {
        Pass
        {
            Name "FinalCopy"

            Cull Off
            ZTest Always
            ZWrite Off
            Blend Off

            HLSLPROGRAM
            // SV_VertexID 与整数位运算要求现代图形 API；支持 D3D11/Vulkan/GLES3。
            #pragma target 3.5
            #pragma vertex MajFinalCopyVertex
            #pragma fragment MajFinalCopyFragment

            // Unity Editor 自带的基础纹理/采样宏，没有渲染管线包依赖。
            #include "HLSLSupport.cginc"

            // 参数均由 CommandBuffer 设置为全局量，不放入 Properties，
            // 避免 Material 的默认值覆盖调用端的 SetGlobalTexture/SetGlobalVector。
            UNITY_DECLARE_TEX2D(_MajCameraColorTexture);
            float4 _MajFinalCopyScaleBias;
            // xy 为逻辑输出矩形在物理屏幕中的比例，zw 为 raw clip-space 平移。
            float4 _MajFinalCopyViewport;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
            };

            Varyings MajFinalCopyVertex(uint vertexID : SV_VertexID)
            {
                Varyings output;

                // DrawProcedural(Triangles, 3, 1)：生成覆盖整个 viewport 的单个三角形。
                // UV 顶点为 (0,0)、(2,0)、(0,2)，对应 clip 顶点 (-1,-1)、(3,-1)、(-1,3)。
                // 在可见 viewport 内插值为 [0,1]，无需顶点缓冲或屏幕 Quad Mesh。
                float2 uv = float2((vertexID << 1) & 2u, vertexID & 2u);
                output.positionCS = float4((uv * 2.0 - 1.0) * _MajFinalCopyViewport.xy
                    + _MajFinalCopyViewport.zw, 0.0, 1.0);
                output.uv.zw = uv;

                // 基础 UV 的原点对应 clip-space (-1,-1)，没有 Core 全屏 UV helper 的 API 翻转。
                // 当前的标准 RT → Game CameraTarget 路径传入恒等 scale/bias，保持画面方向。
                // 不能单凭 graphicsUVStartsAtTop 在调用端再翻一次。
                // 正常复制：(1,1,0,0)；上下翻转：(1,-1,0,1)。
                // xy 为缩放、zw 为偏移，也可以用于复制源纹理的某个 UV 子区域。
                output.uv.xy = uv * _MajFinalCopyScaleBias.xy + _MajFinalCopyScaleBias.zw;
                return output;
            }

            half4 MajFinalCopyFragment(Varyings input) : SV_Target
            {
                // 全屏三角形延伸到 local UV=2；偏移/局部矩形必须裁掉 [0,1] 外的区域，
                // 防止负偏移把源纹理边缘拉伸到本该清空的屏幕。裁剪使用未经源 UV 翻转的坐标。
                clip(min(min(input.uv.z, input.uv.w), min(1.0 - input.uv.z, 1.0 - input.uv.w)));
                // 按源 RT 的 Point/Bilinear 过滤采样，与旧 URP Auto 整数/非整数放大一致。
                // 保留颜色与 alpha。Linear/Gamma 转换交给纹理格式和目标 sRGB 设置，
                // 此处不再执行 pow、LinearToGammaSpace 或 GammaToLinearSpace。
                return UNITY_SAMPLE_TEX2D(_MajCameraColorTexture, input.uv.xy);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
