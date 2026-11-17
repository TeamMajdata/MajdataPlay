// UGUI clipping follows the Unity 6000.3.17f1 built-in UI shader.
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
Shader "Maj/UI/List/Cover Decoration Glass"
{
    Properties
    {
        [PerRendererData] [MainTexture] _MainTex ("Sprite Texture", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _ListDepthOffset ("List Depth Offset", Float) = 0
        // QualityUiImage uses this marker to register the frame as an RT3 consumer.
        [HideInInspector] _Frame ("Late Glass Frame", Float) = 1
        _ShapeUvTransform ("Sprite UV To Circle", Vector) = (1,1,0,0)
        _Radius ("Circle Radius", Float) = 0.5
        _BevelWidth ("Soft Reflection Width", Float) = 0.045
        _HighlightStrength ("Reflection Strength", Range(0,1)) = 0.95
        _Refraction ("Frame Refraction", Float) = 0.022
        _Transmission ("Frame Background Share", Range(0,1)) = 0.72
        _ShadowColor ("Shadow Color", Color) = (0,0,0,0.5)
        _ShadowOffset ("Shadow Offset", Vector) = (0.02,-0.02,0,0)
        _ShadowSoftness ("Shadow Softness", Float) = 0.02
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Stencil Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        AlphaToMask Off
        ColorMask [_ColorMask]

        Pass
        {
            Name "Cover Decoration Glass"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajMipSampling.hlsl"
            #include "Assets/Rendering/Shaders/UI/GlassSurfaceLighting.hlsl"

            sampler2D _MainTex;
            sampler2D _MajUiButtonBlurTexture;
            half4 _TextureSampleAdd;
            float _MajUiButtonBlurReady;
            float4 _MajUiButtonClipUvX;
            float4 _MajUiButtonClipUvY;
            float4 _MajUiButtonClipSampleBounds;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _ListDepthOffset;
                float4 _ShapeUvTransform;
                float _Radius;
                float _BevelWidth;
                half _HighlightStrength;
                float _Refraction;
                half _Transmission;
                half4 _ShadowColor;
                float4 _ShadowOffset;
                float _ShadowSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                float4 screenPosition : TEXCOORD1;
                #ifdef UNITY_UI_CLIP_RECT
                float4 mask : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                input.positionOS.z += _ListDepthOffset;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                output.uv = float4(TRANSFORM_TEX(input.uv.xy, _MainTex), input.uv.zw);
                #ifdef UNITY_UI_CLIP_RECT
                float2 pixelSize = output.positionCS.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = float4(input.positionOS.xy * 2 - rect.xy - rect.zw,
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
                // RT3 contains the difficulty ring beneath this clear coat. The
                // original cover, badges and text draw afterward at full resolution.
                half4 artwork = MAJ_SAMPLE_TEX2D(_MainTex, input.uv.xy) + _TextureSampleAdd;
                half3 fill = artwork.rgb * input.color.rgb;
                float2 circlePosition = input.uv.xy * _ShapeUvTransform.xy + _ShapeUvTransform.zw - 0.5;
                float distanceToCenter = length(circlePosition);
                float2 radialGradient = float2(ddx(distanceToCenter), ddy(distanceToCenter));
                float aa = max(abs(radialGradient.x) + abs(radialGradient.y), 0.00001) * 0.5;
                float2 normal = circlePosition / max(distanceToCenter, 0.00001);
                float2 screenUV = input.screenPosition.xy / input.screenPosition.w;
                // Derivatives precede the narrow-band branch, including during animated scaling.
                float2 shapeDx = ddx(circlePosition);
                float2 shapeDy = ddy(circlePosition);
                float2 screenDx = ddx(screenUV);
                float2 screenDy = ddy(screenUV);
                half bodyAlpha = artwork.a;
                // UV0.zw carries the authored ring's current radius and relative
                // opacity. It shares the existing vertex stream, not another RT.
                half inlayCoverage = 0;
                if (input.uv.z > 0)
                {
                    inlayCoverage = (1 - smoothstep(input.uv.z - aa, input.uv.z + aa, distanceToCenter)) * saturate(input.uv.w);
                }
                // The clearcoat has one silhouette and no separate outline layer.
                float bodyCoverage = 1 - smoothstep(_Radius - aa, _Radius + aa, distanceToCenter);
                // The same annulus covers the captured ring and the outer white
                // frame. The later cover retains its original clear center.
                float innerRadius = max(_Radius - _BevelWidth, 0);
                half innerCoverage = smoothstep(innerRadius - aa, innerRadius + aa, distanceToCenter);
                bodyAlpha *= bodyCoverage * innerCoverage;
                // Without a current snapshot, keep the already drawn ring
                // visible instead of covering it with the white fallback.
                if (_MajUiButtonBlurReady < 0.5)
                {
                    bodyAlpha *= 1 - inlayCoverage;
                }
                float shadowDistance = length(circlePosition + _ShadowOffset.xy);
                float shadowWidth = max(_ShadowSoftness, max(fwidth(shadowDistance), 0.00001));
                // Rise softly outside the actual body instead of exposing a dark disc at its AA edge.
                // Keep the offset falloff and clear center while separating the shadow from the white shoulder.
                half shadowOnset = smoothstep(0, shadowWidth * 0.5, distanceToCenter - _Radius);
                half shadowAlpha = _ShadowColor.a * shadowOnset
                    * (1 - smoothstep(_Radius, _Radius + shadowWidth, shadowDistance)) * (1 - bodyCoverage);
                UNITY_BRANCH
                if (bodyAlpha + shadowAlpha <= 0)
                {
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(-1);
                    #endif
                    return 0;
                }
                UNITY_BRANCH
                if (_MajUiButtonBlurReady > 0.5 && bodyAlpha > 0)
                {
                    float2 sampleUV = screenUV;
                    float determinant = shapeDx.x * shapeDy.y - shapeDx.y * shapeDy.x;
                    UNITY_BRANCH
                    if (bodyCoverage >= 1 && abs(determinant) > 1e-12)
                    {
                        // The subpixel silhouette has negligible displacement. Keep its sample exact
                        // and invert the affine mapping only for the fully covered lens interior.
                        float t = saturate((_Radius - distanceToCenter) / _BevelWidth);
                        float lens = 4 * t * (1 - t);
                        float2 offset = -normal * (_Refraction * lens * lens);
                        float inverseDeterminant = rcp(determinant);
                        float2 pixelOffset = float2(shapeDy.y * offset.x - shapeDy.x * offset.y,
                            shapeDx.x * offset.y - shapeDx.y * offset.x) * inverseDeterminant;
                        sampleUV += screenDx * pixelOffset.x + screenDy * pixelOffset.y;
                    }
                    UNITY_BRANCH
                    if (_MajUiButtonClipUvX.w > 0.5)
                    {
                        float3 position = float3(sampleUV, 1);
                        float2 circle = float2(dot(position, _MajUiButtonClipUvX.xyz), dot(position, _MajUiButtonClipUvY.xyz));
                        float squaredRadius = dot(circle, circle);
                        float safeRadius = _MajUiButtonClipSampleBounds.z;
                        if (squaredRadius > safeRadius * safeRadius)
                        {
                            sampleUV = _MajUiButtonClipSampleBounds.xy + (sampleUV - _MajUiButtonClipSampleBounds.xy)
                                * (safeRadius * rsqrt(squaredRadius));
                        }
                    }
                    // RT3 has no mip chain. Explicit LOD keeps this nonuniform body branch derivative-independent.
                    half4 captured = tex2Dlod(_MajUiButtonBlurTexture, float4(sampleUV, 0, 0));
                    half3 backdrop = captured.rgb;
                    if (_MajUiButtonClipUvX.w > 0.5)
                    {
                        backdrop = saturate(backdrop / max(captured.a, 1.0h / 255.0h));
                    }
                    // The outer white frame keeps its 25/75 mix; the colored
                    // inlay transmits RT3 without a white wash or another tint.
                    half transmission = lerp(_Transmission, 1, inlayCoverage);
                    fill = lerp(fill, backdrop * fill, transmission);
                }
                // Reflection is applied after transmission, independently of ring color.
                // Use only the soft reflection across the visible annulus. The white frame
                // already defines the silhouette and needs no second contour glint.
                half materialDepth = MajGlassMaterialDepth(artwork.rgb * input.color.rgb);
                fill = MajApplyGlassSurfaceLighting(fill, _Radius - distanceToCenter, radialGradient,
                    max(_Radius - distanceToCenter, 0) / max(_BevelWidth, 0.00001),
                    circlePosition.y / max(_Radius, 0.00001),
                    0, materialDepth, max(_HighlightStrength, 0), false, false);
                half foregroundAlpha = saturate(bodyAlpha);
                // Frame shadowAlpha already includes the exterior contour's coverage.
                // Multiplying that coverage again would attenuate the shadow twice.
                half4 color = half4(fill * bodyAlpha + _ShadowColor.rgb * shadowAlpha,
                    foregroundAlpha + shadowAlpha);
                color *= saturate(input.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001h);
                #endif
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
