// 调试传感器只提交边框几何；无纹理采样、无逐片元边界距离或透明内部。
Shader "Maj/Debug/SensorBorder"
{
    Properties
    {
        [MainColor] _BaseColor ("Border Color", Color) = (1,0,0,0.2)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Sensor Inward Border"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            // 半透明调试边框不写深度；沿用 Debug 排序。
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Assets/Rendering/MajRenderPipeline/Library/MajCore.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // 资源已烘焙最终窄边位置，无运行时偏移或纹理采样。
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
    Fallback Off
}
