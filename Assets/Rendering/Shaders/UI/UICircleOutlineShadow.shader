Shader "UI/CircleMaskWithOutlineShadow"
{
    Properties
    {
        _ListDepthOffset ("List Depth Offset", Float) = 0
        [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 圆形参数
        _Radius ("Radius (0~0.5)", Float) = 0.45

        // Outline
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Float) = 0.01

        // Shadow
        _ShadowColor ("Shadow Color", Color) = (0,0,0,0.5)
        _ShadowOffset ("Shadow Offset (x,y)", Vector) = (0.02,-0.02,0,0)
        _ShadowSoftness ("Shadow Softness", Float) = 0.02

        // UI 默认属性
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
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
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CircleUI"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            float4 _MainTex_ST;
            float4 _Color;

            float _ListDepthOffset;
            float _Radius;
            float4 _OutlineColor;
            float _OutlineWidth;

            float4 _ShadowColor;
            float4 _ShadowOffset;   // xy 用作偏移
            float _ShadowSoftness;

            v2f vert (appdata v)
            {
                v2f o;
                v.vertex.z += _ListDepthOffset;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            // 计算圆形距离（基于 UV 中心）
            float circleDistance(float2 uv)
            {
                float2 center = float2(0.5, 0.5);
                return length(uv - center);
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // 主纹理采样
                half4 col = tex2D(_MainTex, uv) * i.color;

                // 圆形距离
                float dist = circleDistance(uv);

                // ---------- 阴影 ----------
                // 阴影使用偏移后的 UV
                float2 shadowUV = uv + _ShadowOffset.xy;
                float shadowDist = circleDistance(shadowUV);

                // 阴影圆形 mask：在半径附近做软边
                float shadowEdge = _Radius;
                float shadowAlpha = 1.0 - smoothstep(shadowEdge, shadowEdge + _ShadowSoftness, shadowDist);

                half4 shadowCol = _ShadowColor;
                shadowCol.a *= shadowAlpha;

                // ---------- 圆形裁剪 ----------
                // 主圆形 mask：硬裁剪 + 轻微软边（可用 _OutlineWidth 或固定值）
                float edgeInner = _Radius - _OutlineWidth * 0.5;
                float edgeOuter = _Radius + _OutlineWidth * 0.5;

                // 圆形内部 alpha
                float insideAlpha = 1.0 - smoothstep(edgeInner, edgeInner + 0.001, dist);

                // ---------- Outline ----------
                // 在 edgeInner ~ edgeOuter 之间为描边区域
                float outlineMask = smoothstep(edgeInner, edgeInner + 0.001, dist) *
                                    (1.0 - smoothstep(edgeOuter, edgeOuter + 0.001, dist));

                half4 outlineCol = _OutlineColor;
                outlineCol.a *= outlineMask;

                // ---------- 组合 ----------
                // 先画阴影，再画主体和描边
                half4 finalCol = 0;

                // 阴影
                finalCol.rgb += shadowCol.rgb * shadowCol.a;
                finalCol.a   = max(finalCol.a, shadowCol.a);

                // 主体（圆形内部）
                col.a *= insideAlpha;
                finalCol.rgb = lerp(finalCol.rgb, col.rgb, col.a);
                finalCol.a   = max(finalCol.a, col.a);

                // 描边
                finalCol.rgb = lerp(finalCol.rgb, outlineCol.rgb, outlineCol.a);
                finalCol.a   = max(finalCol.a, outlineCol.a);

                // 完全在圆外的像素丢弃（避免 UI 交互区域太大时出现方形）
                if (dist > edgeOuter && shadowAlpha <= 0.001)
                    discard;

                return finalCol;
            }
            ENDHLSL
        }
    }
}
