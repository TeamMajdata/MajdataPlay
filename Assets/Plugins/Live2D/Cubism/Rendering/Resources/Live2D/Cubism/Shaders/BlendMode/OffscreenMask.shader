/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


Shader "Unlit/BlendMode/OffscreenMask"
{
    Properties
    {
        [PerRendererData] _MainTex("Mask Texture", 2D) = "white" {}

        // Transform settings.
        [PerRendererData] _OffsetScale ("OffsetScale", Vector) = (0, 0, 1, 1)
        [PerRendererData] _ZOffset ("Z Offset", Float) = 0
        [PerRendererData] _RotationQuaternion ("Rotation Quaternion", Vector) = (0, 0, 0, 1)

        // Culling setting.
        _Cull("Culling", Int) = 0
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD      100
        ZWrite   Off
        Lighting Off
        Cull     [_Cull]
        Blend    One One

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
            float4 _OffsetScale;
            float4 _RotationQuaternion;
            float _ZOffset;

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

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Draw to the red channel because this is a high-precision mask method
                half4 col = half4(1.0, 0.0, 0.0, 1.0) * tex2D(_MainTex, IN.texcoord).a;

                return col;
            }
            ENDHLSL
        }
    }
}
