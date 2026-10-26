// 调试传感器直接绘制所选几何，无纹理采样；视觉位置变换不移动输入Collider。
Shader "Maj/Debug/SensorBorder"
{
    Properties
    {
        [MainColor] _BaseColor ("Border Color", Color) = (1,0,0,0.2)
        [HideInInspector] _SensorDisplayTransform ("Display Scale and Offset", Vector) = (1,1,0,0)
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
            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajCore.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _SensorDisplayTransform;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                positionWS.xy = positionWS.xy * _SensorDisplayTransform.xy + _SensorDisplayTransform.zw;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _BaseColor; }
            ENDHLSL
        }
    }
    Fallback Off
}
