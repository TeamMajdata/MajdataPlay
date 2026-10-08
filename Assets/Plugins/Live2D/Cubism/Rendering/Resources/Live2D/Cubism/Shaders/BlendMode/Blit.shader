/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

Shader "Unlit/BlendMode/Blit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [PerRendererData] _ReversedZ("_Reversed_Z", Int) = 0
    }
    SubShader
    {
        Blend   One OneMinusSrcAlpha, One OneMinusSrcAlpha

        Tags {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        Lighting Off
        ZWrite   Off
        ZTest [_ReversedZ]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 texcoord : TEXCOORD0;
                float4 color    : COLOR;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
            sampler2D _MainTex;
            float4 _MainTex_ST;

            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Setting vertex position
                OUT.vertex = IN.vertex;

                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;

                // If reversed Z is enabled, flip the Y coordinate.
                #if UNITY_REVERSED_Z
                OUT.vertex.y = -OUT.vertex.y;
                #endif

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Sample the texture
                half4 textureColor = tex2D(_MainTex, IN.texcoord);

                half4 OUT = textureColor * IN.color;

                return OUT;
            }
            ENDHLSL
        }
    }
}
