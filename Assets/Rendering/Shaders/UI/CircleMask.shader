Shader "Maj/UI/Circle Mask"
{
    Properties
    {
        [PerRendererData]_MainTex("Sprite",2D)="white"{}
        _Color("Tint", Color) = (1,1,1,1)

        _Center("Center", Vector) = (0.5,0.5,0,0)
        _Radius("Radius", Range(0,1)) = 0.45
        _Feather("Feather", Range(0,0.2)) = 0.01

        // UI Mask Support
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
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

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CircleMask"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            // UGUI Mask can enable this keyword on runtime stencil-material copies.
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajCore.hlsl"

            sampler2D _MainTex;
            half4 _Color;

            float4 _MainTex_ST;

            float2 _Center;
            float _Radius;
            float _Feather;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv,_MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 col = MAJ_SAMPLE_TEX2D(_MainTex, i.uv) * i.color;

                // Keep UV/distance/derivative AA in float so subpixel circle edges do not move.
                float dist = distance(i.uv, _Center);

                float aa = fwidth(dist);

                float alpha = 1.0 - smoothstep(
                    _Radius - _Feather - aa,
                    _Radius + aa,
                    dist);

                col.a *= alpha;

                #ifdef UNITY_UI_ALPHACLIP
                    clip(col.a - 0.001);
                #endif

                return col;
            }

            ENDHLSL
        }
    }
}
