Shader "Maj/Common/BrightnessAdjust"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _Brightness ("Brightness", Range(0.0, 1.0)) = 1.0
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

        Cull Off 
        Lighting Off 
        ZWrite Off 
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "BrightnessAdjust"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            // 显式使用 MRP 的矩阵与实例化声明，避免 CGPROGRAM 自动注入 Built-in 变量。
            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajCore.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            // 保留原来的逐实例亮度通道，兼容现有材质与 MaterialPropertyBlock。
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Brightness)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                
                OUT.vertex = TransformObjectToHClip(IN.vertex.xyz);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color; 
                return OUT;
            }

            half4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // 实例属性仍按 float 上传；亮度是 [0,1] 着色系数，读取后再转 half。
                half brightness = (half)UNITY_ACCESS_INSTANCED_PROP(Props, _Brightness);
                half4 c = MAJ_SAMPLE_TEX2D(_MainTex, IN.texcoord) * IN.color;

                c.rgb *= brightness;
                // c.rgb *= c.a; 

                return c;
            }
            ENDHLSL
        }
    }
}
