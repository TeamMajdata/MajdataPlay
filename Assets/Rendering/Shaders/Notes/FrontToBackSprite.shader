// 前向(front-to-back)半透明合成 + Stencil 层数限制
//
// 用途: 大量沿 z / sortingOrder 层叠的半透明 sprite(slide bar / tap line / hold body)
// 效果: 每个像素只有最靠前的 _MaxLayer 层参与混合, 被挡住的层由 stencil test 在
//       fragment shader 之前剔除, overdraw 被硬性限制为 _MaxLayer 层
//
// 使用前提(缺一不可):
//   1. 必须渲染到一张**清空为 (0,0,0,0) 且带 stencil** 的独立 RT, 不能直接画到已有背景的
//      camera color 上(under 混合依赖 dst alpha 表示已累积的覆盖率)
//   2. 绘制顺序必须是从前往后(把 sortingOrder 整体取反)
//   3. 最后用 Notes/FrontToBackComposite.shader 把 RT 以 premultiplied over 合成回场景
Shader "MajdataPlay/Notes/FrontToBackSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.02
        // 参与混合的最大层数, 你的需求是 3
        [IntRange] _MaxLayer ("Max Blended Layers", Range(1,15)) = 3
        [Toggle(_ALPHA_CLIP)] _AlphaClip ("Enable Alpha Clip", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Pass
        {
            Name "FrontToBackSprite"
            // 由 ScriptableRendererFeature 用这个 LightMode 单独拉一遍 DrawRenderers
            Tags { "LightMode" = "MajNoteFrontToBack" }

            Cull Off
            ZWrite Off
            ZTest Always

            // under 混合(source 必须是 premultiplied alpha):
            //   dst.rgb = src.rgb * (1 - dst.a) + dst.rgb
            //   dst.a   = src.a   * (1 - dst.a) + dst.a
            Blend OneMinusDstAlpha One

            // stencil 作为"这个像素已经画了几层"的计数器
            // Comp Greater: 当 _MaxLayer > stencil 时通过 => 只有前 _MaxLayer 层能通过
            // Pass IncrSat: 通过则 +1(饱和, 不回绕)
            Stencil
            {
                Ref [_MaxLayer]
                ReadMask 15
                WriteMask 15
                Comp Greater
                Pass IncrSat
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHA_CLIP
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Cutoff;
                half   _MaxLayer;
                half   _AlphaClip;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                // SpriteRenderer.color 已经烘进顶点色
                OUT.color      = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

            #ifdef _ALPHA_CLIP
                // 关键: 近乎全透明的 texel 必须 discard,
                // 否则会白白吃掉一层 stencil 配额, 让真正可见的层被剔除
                clip(col.a - _Cutoff);
            #endif

                // 输出 premultiplied alpha 以配合 Blend OneMinusDstAlpha One
                return half4(col.rgb * col.a, col.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
