Shader "Maj/UI/BlurCircleFade_Gaussian_Fixed"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _BlurSize ("Blur Size", Float) = 1.5
        _BlurStrength ("Blur Strength", Range(0,1)) = 1

        _CircleCenter ("Circle Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _CircleRadius ("Circle Radius", Float) = 0.35
        _CircleSoftness ("Circle Softness", Float) = 0.08

        _FadeStartY ("Fade Start Y", Float) = 0.3
        _FadeEndY ("Fade End Y", Float) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "BlurCircleFadeGaussian"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/Rendering/MajRenderPipeline/Library/MajCore.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float _BlurSize;
            float _BlurStrength;

            float4 _CircleCenter;
            float _CircleRadius;
            float _CircleSoftness;

            float _FadeStartY;
            float _FadeEndY;

            half4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;

                float2 texel = _MainTex_TexelSize.xy * _BlurSize;

                // Keep UV offsets, normalized weights and mask evaluation in float; the color accumulator is already half.
                // normalized gaussian weights
                float w00 = 0.05 / 0.72;
                float w01 = 0.09 / 0.72;
                float w02 = 0.05 / 0.72;

                float w10 = 0.09 / 0.72;
                float w11 = 0.16 / 0.72;
                float w12 = 0.09 / 0.72;

                float w20 = 0.05 / 0.72;
                float w21 = 0.09 / 0.72;
                float w22 = 0.05 / 0.72;

                half4 sum = 0;

                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2(-1, -1)) * w00;
                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2( 0, -1)) * w01;
                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2( 1, -1)) * w02;

                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2(-1,  0)) * w10;
                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv)                         * w11;
                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2( 1,  0)) * w12;

                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2(-1,  1)) * w20;
                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2( 0,  1)) * w21;
                sum += MAJ_SAMPLE_TEX2D(_MainTex, uv + texel * float2( 1,  1)) * w22;

                half4 baseCol = MAJ_SAMPLE_TEX2D(_MainTex, uv);
                half4 blurCol = lerp(baseCol, sum, _BlurStrength);

                // circle mask
                float dist = distance(uv, _CircleCenter.xy);
                float circleMask = saturate(1.0 - smoothstep(_CircleRadius, _CircleRadius + _CircleSoftness, dist));

                // bottom fade
                float fadeMask = saturate((uv.y - _FadeEndY) / max(0.0001, (_FadeStartY - _FadeEndY)));

                float finalMask = circleMask * fadeMask;

                half4 col = blurCol * i.color;
                col.rgb *= finalMask;
                col.a *= finalMask;

                return col;
            }
            ENDHLSL
        }
    }
}
