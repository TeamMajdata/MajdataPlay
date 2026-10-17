Shader "Maj/Common/Opaque"
{
    Properties
    {
        _ListDepthOffset ("List Depth Offset", Float) = 0
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff (MSAA Off)", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off
        AlphaToMask On

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Assets/Rendering/MajRenderPipeline/Library/MajCore.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // MRP 根据已创建场景附件的实际样本数设置；所有共用材质统一使用，不增加变体。
            float _MajAlphaToMaskAvailable;

            CBUFFER_START(UnityPerMaterial)
                float _ListDepthOffset;
                float4 _MainTex_ST;
                half4 _Color;
                float _Cutoff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Keep list geometry coplanar for Canvas batching; restore depth on the GPU.
                input.positionOS.z += _ListDepthOffset;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;

                if (_MajAlphaToMaskAvailable > 0.5)
                {
                    // 标准最小 A2C：直接以原 Alpha 控制样本覆盖，不估算、不重映射 cutoff。
                    // Alpha=0 的样本不写颜色/深度；Blend Off 与不透明队列保持不变。
                    return color;
                }

                // 无 MSAA 时保留原硬裁切并输出 Alpha=1，不让单采样目标处理部分覆盖率。
                // cutoff=0 时，完全透明像素仍需丢弃，不能挡住后层。
                clip(color.a - max(_Cutoff, 0.0001));
                return half4(color.rgb, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
