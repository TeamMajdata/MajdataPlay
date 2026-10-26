Shader "Maj/Notes/Common"
{
    Properties
    {
        _MainTex("Albedo (RGB)", 2D) = "white" {}
        
        _ShineMode ("Shine Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Name "NoteCommon"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Always
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            
            HLSLPROGRAM
            #pragma multi_compile_instancing 
            #pragma vertex vert
            #pragma fragment frag
            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajCore.hlsl"

            sampler2D _MainTex;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _ShineMode)
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
                // 大皮肤图集的 UV 保持 float，避免移动端半精度造成块状采样。
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.color = v.color;
                o.uv = v.texcoord;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                
                half4 renderTex = MAJ_SAMPLE_TEX2D(_MainTex, i.uv) * i.color;

                float shineMode = UNITY_ACCESS_INSTANCED_PROP(Props, _ShineMode);

                if (shineMode < 0.5)
                {
                    return renderTex;
                }

                // 时间/相位和 sin 保留 float，避免长时间运行后周期抖动；之后的着色系数有界。
                float wave = sin(_Time.x * 250.0);

                half brightness;
                half contrast;

                if (shineMode < 1.5)
                {
                    // Break
                    brightness = 0.95 + max(wave * 0.65, 0);
                    contrast = 1.0 + min(wave * -0.55, 0);
                }
                else
                {
                    // Hold
                    brightness = 0.95 + abs(wave) * 0.5;
                    contrast = 1.0;
                }

                half3 finalColor = renderTex.rgb * brightness;

                half gray =
                    0.2125 * renderTex.r +
                    0.7154 * renderTex.g +
                    0.0721 * renderTex.b;

                finalColor = lerp(
                    half3(gray, gray, gray),
                    finalColor,
                    1.0
                );

                finalColor = lerp(
                    half3(0.5, 0.5, 0.5),
                    finalColor,
                    contrast
                );

                return half4(finalColor, renderTex.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
