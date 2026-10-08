/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

Shader "Unlit/BlendMode/OffscreenBlend"
{
    Properties
    {
        [PerRendererData] _MainTex ("Main Texture", 2D) = "white" {}
        [PerRendererData] _RenderTexture ("Render Texture", 2D) = "white" {}
        [PerRendererData] cubism_ModelOpacity("Model Opacity", Float) = 1
        [PerRendererData] _OffscreenOpacity("Offscreen Opacity", Float) = 1
        [PerRendererData] _ReversedZ("_Reversed_Z", Int) = 0

        // Extension Color settings.
        [PerRendererData] cubism_MultiplyColor("Multiply Color", Color) = (1.0, 1.0, 1.0, 1.0)
        [PerRendererData] cubism_ScreenColor("Screen Color", Color) = (0.0, 0.0, 0.0, 1.0)

        // Culling setting.
        _Cull("Culling", Int) = 0

        [Toggle(CUBISM_MASK_ON)] cubism_MaskOn("Mask?", Int) = 0
        [Toggle(CUBISM_INVERT_ON)] cubism_InvertOn("Inverted?", Int) = 0

        [PerRendererData] cubism_MaskTexture("cubism_Internal", 2D) = "white" {}
    }
    SubShader
    {
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

        Blend  One Zero, One Zero

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ CUBISM_INVERT_ON
            #pragma multi_compile _ CUBISM_MASK_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../CubismCG.cginc"
            #include_with_pragmas "CubismVariants.cginc"
            #include "ColorBlendVariants.cginc"
            #include "AlphaBlendVariants.cginc"

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
            sampler2D _MainTex; // src (current offscreen)
            sampler2D _RenderTexture; // dest (previous offscreen)
            float4 _MainTex_ST;
            float4 _RenderTexture_ST;
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

                // Setting vertex position.
                OUT.vertex = IN.vertex;
                OUT.color = IN.color;

                OUT.texcoord = IN.texcoord;

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
                // Cd (Straight)
                half4 renderTextureColor = tex2D(_RenderTexture, IN.texcoord);
                float3  Cd = renderTextureColor.rgb;
                float Ad = renderTextureColor.a;

                if (abs(Ad) < 0.00001)
                {
                    Cd = half3(0.0, 0.0, 0.0);
                }
                else
                {
                    // To Straight
                    Cd /= Ad;
                }

                // Cs (Straight)
                half4 mainTextureColor = tex2D(_MainTex, IN.texcoord);
                // Multiply
                mainTextureColor.rgb *= cubism_MultiplyColor.rgb;
                // Screen
                mainTextureColor.rgb = (mainTextureColor.rgb + cubism_ScreenColor.rgb) - (mainTextureColor.rgb * cubism_ScreenColor.rgb);

                float3  Cs = mainTextureColor.rgb;
                float As = mainTextureColor.a;

                if (abs(As) < 0.00001)
                {
                    Cs = half3(0.0, 0.0, 0.0);
                }
                else
                {
                    // To Straight
                    Cs /= As;
                }

                // Mask
#if defined(CUBISM_MASK_ON) || defined(CUBISM_INVERT_ON)
                float clippingMask = 1.0;
                clippingMask = tex2D(cubism_MaskTexture , IN.texcoord);
                clippingMask = abs(cubism_InvertOn - clippingMask);
                As *= clippingMask;
#endif

                As *= IN.color.a;
                As *= _OffscreenOpacity;

                float4 OUT = clamp(ALPHA_BLEND(COLOR_BLEND(Cs, Cd), Cs, As, Cd, Ad), 0.0, 1.0);

                // Apply Cubism alpha to color.
                OUT *= cubism_ModelOpacity;

               return OUT;
            }
            ENDHLSL
        }
    }
}
