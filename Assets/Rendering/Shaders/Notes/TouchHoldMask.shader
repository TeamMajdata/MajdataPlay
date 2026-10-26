Shader "Maj/Notes/TouchHoldMask"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
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
        }

        Cull Off Lighting Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TouchHoldMask"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing 
            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajCore.hlsl"

            // 保留原径向遮罩的角度精度与边缘计算。
            static const float MAJ_MASK_PI = 3.14159265359f;

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                half4 color     : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 localPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            sampler2D _MainTex;

            // 保留 SpriteRenderer 的原生实例颜色 / Flip 数组名称和布局。
            // _RendererColor.a 驱动遮罩进度；顶点 alpha 不参与进度计算。
            #ifdef UNITY_INSTANCING_ENABLED

            UNITY_INSTANCING_BUFFER_START(PerDrawSprite)

                UNITY_DEFINE_INSTANCED_PROP(
                    half4,
                    unity_SpriteRendererColorArray
                )

                UNITY_DEFINE_INSTANCED_PROP(
                    half2,
                    unity_SpriteFlipArray
                )

            UNITY_INSTANCING_BUFFER_END(PerDrawSprite)

            #define _RendererColor \
                UNITY_ACCESS_INSTANCED_PROP(PerDrawSprite, unity_SpriteRendererColorArray)

            #define _Flip \
                UNITY_ACCESS_INSTANCED_PROP(PerDrawSprite, unity_SpriteFlipArray)

            #endif


            CBUFFER_START(UnityPerDrawSprite)

            #ifndef UNITY_INSTANCING_ENABLED

                half4 _RendererColor;
                half2 _Flip;

            #endif

            CBUFFER_END

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                
                OUT.vertex = TransformObjectToHClip(IN.vertex.xyz);
                OUT.texcoord = IN.texcoord;
                OUT.localPos = IN.vertex.xy;

                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float currentProgress = _RendererColor.a;

                half4 col = MAJ_SAMPLE_TEX2D(_MainTex, IN.texcoord);
                col *= half4(IN.color.rgb, 1.0);
                float2 dir = IN.localPos;


                float angle = atan2(dir.x, dir.y);
                
                if (angle < 0) angle += MAJ_MASK_PI * 2.0;
                
                float normalizedAngle = angle / (MAJ_MASK_PI * 2.0);
                float safeSoftness = 0.005;
                float fillEnd = currentProgress * (1.0 + safeSoftness);
                float mask = 1.0 - smoothstep(fillEnd - safeSoftness, fillEnd, normalizedAngle);

                col.a *= mask;
                return col;
            }
            ENDHLSL
        }
    }
}
