// MRP 的单 Pass 无光照材质。保留原 URP Unlit 的常用属性名，让现有材质只换 Shader 引用即可迁移。
// 不包含 Lit、雾、阴影、GI、DBuffer、深度预通过或 Motion Vectors。
Shader "Maj/Unlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1,1,1,1)
        [Enum(Opaque,0,Transparent,1)] _Surface ("Surface", Float) = 0
        [Enum(Alpha,0,Premultiply,1,Additive,2,Multiply,3)] _Blend ("Blend", Float) = 0
        [ToggleUI] _AlphaClip ("Alpha Clipping", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _AlphaToMask ("Alpha To Coverage (MSAA)", Float) = 0
        [IntRange] _QueueOffset ("Queue Offset", Range(-50,50)) = 0

        // 与旧材质序列化字段保持一致，Inspector 由本地 ShaderGUI 统一维护这些状态。
        [HideInInspector] _BlendOp ("Blend Operation", Float) = 0
        [HideInInspector] _SrcBlend ("Source Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination Blend", Float) = 0
        [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
        [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 0
        [HideInInspector] _ZWrite ("Depth Write", Float) = 1
        [HideInInspector] _MainTex ("Legacy Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Legacy Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            BlendOp [_BlendOp]
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _ALPHAMODULATE_ON

            #include "Assets/Rendering/MajRenderPipeline/Library/MajCore.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // 所有被程序读取的材质常量都放在同一个 UnityPerMaterial 中，保持 SRP Batcher 兼容。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                half _Surface;
                half _AlphaToMask;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // 与 URP Unlit 一样，只使用贴图和 _BaseColor，不额外乘入顶点色。
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

            #if defined(_ALPHATEST_ON)
                if (_AlphaToMask > 0.5h)
                {
                    // alpha-to-coverage 需要输出覆盖率；否则 Opaque 的 alpha=1 会丢掉 MSAA 的软边信息。
                    // 与原 URP AlphaClip 相同：cutoff=0 表示不裁剪，避免零阈值产生透明边缘。
                    half dAlpha = fwidth(color.a);
                    color.a = _Cutoff <= 0.0h ? 1.0h
                        : saturate((color.a - _Cutoff - 0.5h * dAlpha) / max(dAlpha, 0.0001h) + 1.0h);
                    clip(color.a - 0.0001h);
                }
                else
                {
                    clip(color.a - _Cutoff);
                }
            #endif

                // 保持当前 URP 17.3 Unlit 的 Premultiply 语义：由混合状态使用预乘颜色，
                // Shader 不再将 RGB 乘一次 alpha，避免已预乘素材变暗。保留关键词以兼容旧材质 / GUI。
            #if defined(_ALPHAMODULATE_ON)
                // Multiply 的透明像素应该接近白色（乘法单位元），不是接近黑色。
                color.rgb = lerp(half3(1.0h, 1.0h, 1.0h), color.rgb, color.a);
            #endif

                // 不透明材质正常输出 alpha=1；开启 Alpha Clip + A2C 时保留刚计算的覆盖率。
                if (_Surface < 0.5h)
                {
                #if defined(_ALPHATEST_ON)
                    color.a = _AlphaToMask > 0.5h ? color.a : 1.0h;
                #else
                    color.a = 1.0h;
                #endif
                }
                return color;
            }
            ENDHLSL
        }
    }
    CustomEditor "MajdataPlay.Rendering.Editor.MajUnlitShaderGUI"
    FallBack Off
}
