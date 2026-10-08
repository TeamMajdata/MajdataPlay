/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

Shader "Unlit/BlendMode/BlendImage"
{
    Properties
    {
        [PerRendererData] _MainTex ("Main Texture", 2D) = "white" {}
        [PerRendererData] _RenderTexture ("Render Texture", 2D) = "white" {}
        [PerRendererData] cubism_ModelOpacity("Model Opacity", Float) = 1

        // Extension Color settings.
        [PerRendererData] cubism_MultiplyColor("Multiply Color", Color) = (1.0, 1.0, 1.0, 1.0)
        [PerRendererData] cubism_ScreenColor("Screen Color", Color) = (0.0, 0.0, 0.0, 1.0)

        // Transform settings.
        [PerRendererData] _OffsetScale ("OffsetScale", Vector) = (0, 0, 1, 1)
        [PerRendererData] _ZOffset ("Z Offset", Float) = 0
        [PerRendererData] _RotationQuaternion ("Rotation Quaternion", Vector) = (0, 0, 0, 1)

        // Culling setting.
        _Cull("Culling", Int) = 0

        [Toggle(CUBISM_MASK_ON)] cubism_MaskOn("Mask?", Int) = 0
        [Toggle(CUBISM_INVERT_ON)] cubism_InvertOn("Inverted?", Int) = 0

        [PerRendererData] cubism_MaskTexture("cubism_Internal", 2D) = "white" {}
        [PerRendererData] cubism_MaskTile("cubism_Internal", Vector) = (0, 0, 0, 0)
        [PerRendererData] cubism_MaskTransform("cubism_Internal", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Blend    One Zero, One Zero

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
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ CUBISM_INVERT_ON
            #pragma multi_compile _ CUBISM_MASK_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "CubismVariants.cginc"
            #include "AlphaBlendVariants.cginc"
            #include "ColorBlendVariants.cginc"
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
                float4 texcoord2 : TEXCOORD1;
                float4 color    : COLOR;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO

                // Add Cubism specific vertex output data.
                CUBISM_VERTEX_OUTPUT
            };

            CBUFFER_START(UnityPerMaterial)
            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _RenderTexture;
            float4 _RenderTexture_ST;
            half4 cubism_MultiplyColor;
            half4 cubism_ScreenColor;
            float4 _OffsetScale;
            float4 _RotationQuaternion;
            float _ZOffset;

            #if defined(CUBISM_MASK_ON) || defined(CUBISM_INVERT_ON)
            int cubism_InvertOn;
            #endif

            // Include Cubism specific shader variables.
            CUBISM_SHADER_VARIABLES

            CBUFFER_END

            // Quaternion rotation function.
            float3 quaternion(float4 quat, float3 vec)
            {
                // Normalize quaternion to ensure unit length
                float4 normalizedQuat = normalize(quat);
                float3 qv = normalizedQuat.xyz;
                float qw = normalizedQuat.w;

                // Calculate cross product once.
                float3 crossVec = cross(qv, vec);

                return vec + 2.0 * (qw * crossVec + cross(qv, crossVec));
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Convert vertex position
                float4 vertex = IN.vertex;
                vertex.xy *= _OffsetScale.zw;  // Scale

                // Apply quaternion rotation (3-axis rotation)
                float3 rotatedPos = quaternion(_RotationQuaternion, vertex.xyz);
                vertex.xyz = rotatedPos;

                vertex.xy += _OffsetScale.xy;  // Offset
                vertex.z += _ZOffset; // Z Offset

                // Setting vertex position.
                OUT.vertex = TransformObjectToHClip(vertex.xyz);

                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;

                // Initialize Cubism specific vertex output data.
                CUBISM_INITIALIZE_VERTEX_OUTPUT(IN, OUT);

                // Compute screen position for sampling _RenderTexture.
                OUT.texcoord2 = ComputeScreenPos(OUT.vertex);

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // HACK: The calculation breaks unless it is passed to the fragment.
                float2 uv = IN.texcoord2.xy / IN.texcoord2.w;

                // Cd (Straight)
                half4 renderTextureColor = tex2D(_RenderTexture, uv);
                float3  Cd = renderTextureColor.rgb;
                float Ad = renderTextureColor.a;

                // Cs (Straight)
                half4 mainTextureColor = tex2D(_MainTex, IN.texcoord);
                // Multiply
                mainTextureColor.rgb *= cubism_MultiplyColor.rgb;
                // Screen
                mainTextureColor.rgb = (mainTextureColor.rgb + cubism_ScreenColor.rgb) - (mainTextureColor.rgb * cubism_ScreenColor.rgb);

                mainTextureColor = mainTextureColor * IN.color;

                float3 Cs = mainTextureColor.rgb;
                float As = mainTextureColor.a;

                if (abs(Ad) < 0.00001)
                {
                    Cd = half3(0.0, 0.0, 0.0);
                }
                else
                {
                    // To Straight
                    Cd /= Ad;
                }

                // Mask
#if defined(CUBISM_MASK_ON) || defined(CUBISM_INVERT_ON)
                float clippingMask = 1.0;
                half4 cubism_maskChannel = CubismGetClippedMaskChannel(IN.cubism_MaskCoordinates, cubism_MaskTile);
                float  cubism_maskAlpha = CubismSampleMaskTexture(cubism_MaskTexture, cubism_maskChannel, IN.cubism_MaskCoordinates);
                clippingMask = abs(cubism_InvertOn - cubism_maskAlpha);
                As *= clippingMask;
#endif

                half4 OUT = clamp(ALPHA_BLEND(COLOR_BLEND(Cs, Cd), Cs, As, Cd, Ad), 0.0, 1.0);

                // Apply Cubism mask and model alpha.
                OUT *= cubism_ModelOpacity;

                return OUT;
            }
            ENDHLSL
        }
    }
}
