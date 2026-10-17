Shader "Maj/UI/CircleMaskWithOutlineShadow"
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
        // 片元返回预乘 Alpha；RGB 与 Alpha 均按 source-over 混合，避免软边再次乘 Alpha。
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CircleUI"
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

            // Keep UV/distance/soft-edge math in float; local sampled and blended colors already use half.
            // 计算圆形距离（基于 UV 中心）
            float circleDistance(float2 uv)
            {
                float2 center = float2(0.5, 0.5);
                return length(uv - center);
            }

            half4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // 主体保留原有纹理与 UI Tint；描边、阴影继续使用各自颜色参数。
                half4 col = MAJ_SAMPLE_TEX2D(_MainTex, uv) * i.color;

                float dist = circleDistance(uv);
                // 圆周位于片元内部，不能只依赖 MSAA 的几何覆盖；过渡宽度随实际像素调整。
                float aa = max(fwidth(dist), 0.00001) * 0.5;

                // ---------- 阴影 ----------
                // 阴影使用偏移后的 UV
                float2 shadowUV = uv + _ShadowOffset.xy;
                float shadowDist = circleDistance(shadowUV);

                // 保留设计的阴影模糊宽度；零 Softness 也至少有一个像素的稳定过渡。
                float shadowWidth = max(_ShadowSoftness, max(fwidth(shadowDist), 0.00001));
                float shadowMask = 1.0 - smoothstep(_Radius, _Radius + shadowWidth, shadowDist);
                half shadowAlpha = saturate(_ShadowColor.a) * shadowMask;

                // ---------- 圆形裁剪 ----------
                float outlineWidth = max(_OutlineWidth, 0.0);
                float edgeInner = _Radius - outlineWidth * 0.5;
                float edgeOuter = _Radius + outlineWidth * 0.5;

                float insideCoverage = 1.0 - smoothstep(edgeInner - aa, edgeInner + aa, dist);
                float outerCoverage = 1.0 - smoothstep(edgeOuter - aa, edgeOuter + aa, dist);

                // ---------- Outline ----------
                // 两个嵌套圆的覆盖率差就是描边，与主体互补，内接缝不会漏出背景。
                // 不能将两个 smoothstep 相乘：宽度为 0 时仍会留下 s*(1-s) 的假描边。
                float outlineCoverage = outlineWidth > 0.0
                    ? max(outerCoverage - insideCoverage, 0.0)
                    : 0.0;

                // ---------- 组合 ----------
                // 主体与描边占据互补区域，先合成一层预乘前景，再覆盖到阴影上。
                // 对它们各做一次 Over 会让内边缘的两份 0.5 覆盖只得到 0.75，产生透明接缝。
                half bodyAlpha = saturate(col.a) * insideCoverage;
                half outlineAlpha = saturate(_OutlineColor.a) * outlineCoverage;
                half foregroundAlpha = saturate(bodyAlpha + outlineAlpha);
                half3 foregroundRGB = col.rgb * bodyAlpha + _OutlineColor.rgb * outlineAlpha;

                half shadowBehind = shadowAlpha * (1.0h - foregroundAlpha);
                half4 finalCol = half4(
                    foregroundRGB + _ShadowColor.rgb * shadowBehind,
                    foregroundAlpha + shadowBehind);

                // 仅丢弃完全透明的尾部；保留圆周 AA 和有效软阴影，不增加纹理采样或 Pass。
                if (finalCol.a <= 0.0h)
                    discard;

                return finalCol;
            }
            ENDHLSL
        }
    }
}
