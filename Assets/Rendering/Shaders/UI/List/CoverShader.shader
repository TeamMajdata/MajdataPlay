Shader "UI/List/CoverShader"
{
    Properties
    {
        _ListDepthOffset ("List Depth Offset", Float) = 0
        [PerRendererData] [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0.0, 1.0)) = 1.0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5

        _Center ("Center", Vector) = (0.5,0.5,0,0)
        _Radius ("Radius", Range(0,1)) = 0.45
        _Feather ("Feather", Range(0,0.2)) = 0.01

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off
        ColorMask [_ColorMask]

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _ListDepthOffset;
                float4 _MainTex_ST;
                half4 _Color;
                float4 _Center;
                float _Radius;
                float _Feather;
                float _Cutoff;
                #if !defined(UNITY_INSTANCING_ENABLED)
                    float _Brightness;
                #endif
            CBUFFER_END

            #if defined(UNITY_INSTANCING_ENABLED)
                UNITY_INSTANCING_BUFFER_START(Props)
                    UNITY_DEFINE_INSTANCED_PROP(float, _Brightness)
                UNITY_INSTANCING_BUFFER_END(Props)
            #endif

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
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
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                float dist = distance(input.uv, _Center.xy);
                float aa = max(fwidth(dist), 0.00001);
                float circleAlpha = 1.0 - smoothstep(
                    _Radius - _Feather - aa,
                    _Radius + aa,
                    dist);

                // Keep fully transparent pixels discarded even when Cutoff is zero.
                float alpha = color.a * circleAlpha;
                clip(alpha - max(_Cutoff, 0.00001));

                float brightness = UNITY_ACCESS_INSTANCED_PROP(Props, _Brightness);
                return half4(color.rgb * brightness, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
