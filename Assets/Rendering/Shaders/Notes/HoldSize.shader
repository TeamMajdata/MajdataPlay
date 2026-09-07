Shader "Notes/HoldSize"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _ShineMode("Shine Mode", Float) = 0
        _HoldTargetSize("Target Size", Vector) = (1, 1, 0, 0)
        _HoldSourceSize("Source Size", Vector) = (1, 1, 0, 0)
        _HoldBorder("Border", Vector) = (0, 0, 0, 0)
        _HoldPivot("Pivot", Vector) = (0.5, 0.5, 0, 0)
        _HoldUvRect("UV Rect", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Always
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            sampler2D _MainTex;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _ShineMode)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HoldTargetSize)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HoldSourceSize)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HoldBorder)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HoldPivot)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HoldUvRect)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata_t
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 normalizedPosition : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float RemapSlicedAxis(float coordinate, float targetSize, float sourceSize,
                float startBorder, float endBorder)
            {
                float borderSize = startBorder + endBorder;
                float borderScale = borderSize > 0.0
                    ? min(1.0, targetSize / borderSize)
                    : 1.0;
                float targetStart = startBorder * borderScale;
                float targetEnd = targetSize - endBorder * borderScale;

                if (coordinate < targetStart && borderScale > 0.0)
                    return coordinate / borderScale;

                if (coordinate > targetEnd && borderScale > 0.0)
                    return sourceSize - (targetSize - coordinate) / borderScale;

                float targetCenter = targetEnd - targetStart;
                float sourceCenter = max(0.0, sourceSize - borderSize);
                if (targetCenter <= 0.00001)
                    return startBorder;

                return startBorder + (coordinate - targetStart) * sourceCenter / targetCenter;
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float2 targetSize = UNITY_ACCESS_INSTANCED_PROP(Props, _HoldTargetSize).xy;
                float2 sourceSize = max(
                    UNITY_ACCESS_INSTANCED_PROP(Props, _HoldSourceSize).xy,
                    float2(0.00001, 0.00001));
                float2 pivot = UNITY_ACCESS_INSTANCED_PROP(Props, _HoldPivot).xy;
                float2 normalizedPosition = v.vertex.xy / sourceSize + pivot;
                float2 targetPosition = (normalizedPosition - pivot) * targetSize;

                o.pos = TransformObjectToHClip(float3(targetPosition, v.vertex.z));
                o.normalizedPosition = normalizedPosition;
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float2 targetSize = max(
                    UNITY_ACCESS_INSTANCED_PROP(Props, _HoldTargetSize).xy,
                    float2(0.00001, 0.00001));
                float2 sourceSize = max(
                    UNITY_ACCESS_INSTANCED_PROP(Props, _HoldSourceSize).xy,
                    float2(0.00001, 0.00001));
                float4 border = UNITY_ACCESS_INSTANCED_PROP(Props, _HoldBorder);
                float4 uvRect = UNITY_ACCESS_INSTANCED_PROP(Props, _HoldUvRect);
                float2 targetCoordinate = saturate(i.normalizedPosition) * targetSize;
                float2 sourceCoordinate;
                sourceCoordinate.x = RemapSlicedAxis(
                    targetCoordinate.x, targetSize.x, sourceSize.x, border.x, border.z);
                sourceCoordinate.y = RemapSlicedAxis(
                    targetCoordinate.y, targetSize.y, sourceSize.y, border.y, border.w);
                float2 uv = uvRect.xy + saturate(sourceCoordinate / sourceSize) * uvRect.zw;
                half4 renderTex = tex2D(_MainTex, uv) * i.color;

                float shineMode = UNITY_ACCESS_INSTANCED_PROP(Props, _ShineMode);
                if (shineMode < 0.5)
                    return renderTex;

                float wave = sin(_Time.x * 250.0);
                float brightness;
                float contrast;

                if (shineMode < 1.5)
                {
                    brightness = 0.95 + max(wave * 0.65, 0.0);
                    contrast = 1.0 + min(wave * -0.55, 0.0);
                }
                else
                {
                    brightness = 0.95 + abs(wave) * 0.5;
                    contrast = 1.0;
                }

                half3 finalColor = renderTex.rgb * brightness;
                half gray = dot(renderTex.rgb, half3(0.2125, 0.7154, 0.0721));
                finalColor = lerp(half3(gray, gray, gray), finalColor, 1.0);
                finalColor = lerp(half3(0.5, 0.5, 0.5), finalColor, contrast);
                return half4(finalColor, renderTex.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
