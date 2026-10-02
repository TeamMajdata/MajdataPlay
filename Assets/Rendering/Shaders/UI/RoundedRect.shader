Shader "UI/Rounded Rect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _RadiusTL ("Top Left Radius", Float) = 0
        _RadiusTR ("Top Right Radius", Float) = 0
        _RadiusBL ("Bottom Left Radius", Float) = 0
        _RadiusBR ("Bottom Right Radius", Float) = 0
        
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            Tags { "LightMode"="SRPDefaultUnlit" }
        HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma shader_feature __ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                half4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 localPos : TEXCOORD1;
            };

            half4 _Color;
            half4 _TextureSampleAdd;
            
            float _RadiusTL, _RadiusTR, _RadiusBL, _RadiusBR;
            sampler2D _MainTex;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = TransformObjectToHClip(v.vertex.xyz);
                OUT.texcoord = v.texcoord;
                OUT.localPos = v.vertex.xy;
                OUT.color = v.color * _Color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                float w = length(float2(ddx(IN.localPos.x), ddy(IN.localPos.x))) / (length(float2(ddx(IN.texcoord.x), ddy(IN.texcoord.x))) + 1e-5);
                float h = length(float2(ddx(IN.localPos.y), ddy(IN.localPos.y))) / (length(float2(ddx(IN.texcoord.y), ddy(IN.texcoord.y))) + 1e-5);
                float2 size = float2(w, h);
                float2 halfSize = size * 0.5;

                float2 p = (IN.texcoord - 0.5) * size;

                float topRadius = p.x >= 0.0 ? _RadiusTR : _RadiusTL;
                float bottomRadius = p.x >= 0.0 ? _RadiusBR : _RadiusBL;
                float radius = max(p.y >= 0.0 ? topRadius : bottomRadius, 0.0);
                radius = min(radius, min(halfSize.x, halfSize.y));

                float2 q = abs(p) - (halfSize - radius);
                float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

                float aa = max(length(float2(ddx(dist), ddy(dist))), 1e-3);
                float alpha = smoothstep(aa, -aa, dist);

                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                color.a *= alpha;

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDHLSL
        }
    }
}
