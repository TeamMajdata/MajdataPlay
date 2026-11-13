// Derived from Maj/UI/Default and the official Unity 6000.3.17f1 built-in UI shader.
// Preserves UGUI mask, tint, and premultiplied-alpha contracts while retaining List depth offsets.
// Source: https://download.unity3d.com/download_unity/cf0352b38e81/builtin_shaders-6000.3.17f1.zip
/*
Copyright (c) 2016 Unity Technologies

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

Shader "Maj/UI/Atlas Alpha"
{
    Properties
    {
        _ListDepthOffset ("List Depth Offset", Float) = 0
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        // UGUI Mask may enable this only while writing a structural stencil mask.
        // Ordinary outlined UI does not discard or remap its visual alpha.
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Stencil Alpha Clip", Float) = 0
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
        Blend One OneMinusSrcAlpha
        AlphaToMask Off
        ColorMask [_ColorMask]

        Pass
        {
            Name "Atlas Alpha"
            Tags { "LightMode"="SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajMipSampling.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                #ifdef UNITY_UI_CLIP_RECT
                float4 mask : TEXCOORD1;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            half4 _Color;
            half4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _ListDepthOffset;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Same local-space offset as Maj/Common/Opaque, after Canvas batching.
                input.vertex.z += _ListDepthOffset;
                float4 positionCS = UnityObjectToClipPos(input.vertex);
                output.vertex = positionCS;
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);

                #ifdef UNITY_UI_CLIP_RECT
                float2 pixelSize = positionCS.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = float4(input.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize)));
                #endif

                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                {
                    input.color.rgb = UIGammaToLinear(input.color.rgb);
                }
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const half alphaPrecision = half(0xff);
                input.color.a = round(input.color.a * alphaPrecision) / alphaPrecision;

                half4 color = input.color * (MAJ_SAMPLE_TEX2D(_MainTex, input.texcoord) + _TextureSampleAdd);

                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color.a *= mask.x * mask.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                // Retain UGUI's optional stencil-mask contract, not the old visual 0.5 cutoff.
                clip(color.a - 0.001h);
                #endif

                color.rgb *= color.a;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
