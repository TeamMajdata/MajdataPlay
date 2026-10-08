/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


Shader "Unlit/BlendMode/OffscreenCompatibleBlend"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [PerRendererData] _OffscreenOpacity("Offscreen Opacity", Float) = 1
        [PerRendererData] cubism_ModelOpacity("Model Opacity", Float) = 1
        [PerRendererData] _ReversedZ("_Reversed_Z", Int) = 0

        // Extension Color settings.
        [PerRendererData] cubism_MultiplyColor("Multiply Color", Color) = (1.0, 1.0, 1.0, 1.0)
        [PerRendererData] cubism_ScreenColor("Screen Color", Color) = (0.0, 0.0, 0.0, 1.0)

        // Blend settings.
        _SrcColor("Source Color", Int) = 1
        _DstColor("Destination Color", Int) = 10
        _SrcAlpha("Source Alpha", Int) = 1
        _DstAlpha("Destination Alpha", Int) = 10

        // Culling setting.
        _Cull("Culling", Int) = 0

        [Toggle(CUBISM_MASK_ON)] cubism_MaskOn("Mask?", Int) = 0
        [Toggle(CUBISM_INVERT_ON)] cubism_InvertOn("Inverted?", Int) = 0

        [PerRendererData] cubism_MaskTexture("cubism_Internal", 2D) = "white" {}
    }
    SubShader
    {
        Blend    [_SrcColor][_DstColor], [_SrcAlpha][_DstAlpha]

        Tags {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull     [_Cull]
        Lighting Off
        ZWrite   Off
        ZTest [_ReversedZ]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ CUBISM_INVERT_ON
            #pragma multi_compile _ CUBISM_MASK_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../CubismCG.cginc"

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

                // Add Cubism specific vertex output data.
                CUBISM_VERTEX_OUTPUT
            };

            CBUFFER_START(UnityPerMaterial)
            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 cubism_MultiplyColor;
            half4 cubism_ScreenColor;
            float _OffscreenOpacity;

            #if defined(CUBISM_MASK_ON) || defined(CUBISM_INVERT_ON)
            int cubism_InvertOn;
            #endif

            // Include Cubism specific shader variables.
            CUBISM_SHADER_VARIABLES

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

                // Initialize Cubism specific vertex output data.
                CUBISM_INITIALIZE_VERTEX_OUTPUT(IN, OUT);

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

                if (textureColor.a >= 0.00001)
                {
                    // Multiply
                    textureColor.rgb *= cubism_MultiplyColor.rgb;
                    // Screen
                    textureColor.rgb = (textureColor.rgb + cubism_ScreenColor.rgb) - (textureColor.rgb * cubism_ScreenColor.rgb);
                }

                half4 OUT = textureColor * IN.color;

                // Mask
#if defined(CUBISM_MASK_ON) || defined(CUBISM_INVERT_ON)
                float clippingMask = 1.0;
                clippingMask = tex2D(cubism_MaskTexture , IN.texcoord).r;
                clippingMask = abs(cubism_InvertOn - clippingMask);
                OUT *= clippingMask;
#endif

                OUT *= _OffscreenOpacity;
                OUT *= cubism_ModelOpacity;

                return OUT;
            }
            ENDHLSL
        }
    }
}
