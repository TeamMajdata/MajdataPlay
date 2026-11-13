// Derived from Maj/UI/Default and the official Unity 6000.3.17f1 built-in UI shader.
// Preserves UGUI mask, tint, and List depth while replacing large panel fills with a shared background snapshot.
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

Shader "Maj/UI/PV Glass"
{
    Properties
    {
        _ListDepthOffset ("List Depth Offset", Float) = 0
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _PanelBaseTex ("Authored Fill Mask", 2D) = "white" {}
        [HideInInspector] _FillAlpha ("Authored Fill Alpha", Float) = 0.5019608
        [HideInInspector] _ReferenceOpacity ("Reference UI Opacity", Float) = 1
        [HideInInspector] _FillSampleUV ("Interior Artwork Sample UV", Vector) = (0.5,0.5,0,0)
        _TintStrength ("Gaussian Texture Color Filter Strength", Range(0,1)) = 0.4
        _LiquidTintStrength ("Glass And Button Texture Color Filter Strength", Range(0,1)) = 0.9
        _LiquidArtworkBlend ("Glass And Button Original Artwork Share", Range(0,1)) = 0.25
        [HideInInspector] _GaussianButtonTintStrength ("Gaussian Peripheral Texture Filter Strength", Range(0,1)) = 0.65
        [HideInInspector] _LiquidArtworkProtection ("Original Artwork Source Cutoff, Feather, Enabled", Vector) = (0,0,0,0)
        [HideInInspector] _LiquidTintSourceTex ("Underlying Panel Artwork", 2D) = "white" {}
        [HideInInspector] _LiquidTintSourceUV ("Underlying Artwork Fill UV", Vector) = (0.5,0.5,0,0)
        [HideInInspector] _LiquidTintSourceColor ("Underlying Working RGB And Weight", Vector) = (1,1,1,0)
        _FrostStrength ("Gaussian Neutral White Veil Strength", Range(0,1)) = 0.08
        [HideInInspector] _BackdropIsButton ("Use Later Button Snapshot", Float) = 0
        [HideInInspector] _BackdropIsCategory ("Use First Control Snapshot", Float) = 0
        [HideInInspector] _BackdropIsNativeControl ("Preserve Native Control Artwork Edges", Float) = 0
        [HideInInspector] _BackdropUsesPanelBlur ("Use Scene-Prefix Board Backdrop", Float) = 0
        [HideInInspector] _BackdropIsRoundedRect ("Use Authored Procedural Corners", Float) = 0
        [HideInInspector] _RadiusTL ("Top Left Radius", Float) = 0
        [HideInInspector] _RadiusTR ("Top Right Radius", Float) = 0
        [HideInInspector] _RadiusBL ("Bottom Left Radius", Float) = 0
        [HideInInspector] _RadiusBR ("Bottom Right Radius", Float) = 0
        _GlassRefraction ("Edge Refraction / Short Half Size", Range(0,0.05)) = 0.008
        _GlassHighlight ("Edge Highlight", Range(0,0.3)) = 0.045
        [HideInInspector] _GlassUvToLocal ("Atlas UV To Logical Panel", Vector) = (1,1,0,0)
        [HideInInspector] _GlassShape ("Half Size, Radius, Enabled", Vector) = (1,1,0,0)
        [HideInInspector] _LiquidGlassRect ("Logical Center And Half Size", Vector) = (0,0,1,1)
        [HideInInspector] _LiquidGlassShape ("Radius, Ellipse, Clipped Circle, Sprite Coordinates", Vector) = (0,0,0,0)
        [HideInInspector] _LiquidGlassCircle ("Clipped Ellipse Center And Horizontal/Vertical Radii", Vector) = (0,0,0,0)
        [HideInInspector] _LiquidGlassClipSmoothness ("Clipped Ellipse Crop Intersection Width", Float) = 0
        [HideInInspector] _LiquidGlassCorners ("Top Left, Top Right, Bottom Right, Bottom Left", Vector) = (0,0,0,0)
        [HideInInspector] _LiquidGlassOptics ("Bevel Width, Thickness, Highlight Width, Softness", Vector) = (8,9.6,3.2,2)
        [HideInInspector] _LiquidRefractionTail ("Peripheral Body Refraction Tail", Range(0,1)) = 0
        [HideInInspector] _LiquidGlassUvToLocal ("Atlas UV To Original Sprite", Vector) = (0,0,0,0)
        [HideInInspector] _LiquidGlassSpriteRect ("Original Sprite Body Center And Half Size", Vector) = (0,0,1,1)
        [HideInInspector] _LiquidGlassSpriteCorners ("Original Sprite Corner Radii", Vector) = (0,0,0,0)
        [HideInInspector] _LiquidContourTex ("Baked Original Sprite Distances", 2D) = "black" {}
        [HideInInspector] _LiquidContourUV ("Existing Atlas UV To Baked Tile", Vector) = (1,1,0,0)
        [HideInInspector] _LiquidContourClamp ("Baked Tile Pixel-Center Bounds", Vector) = (0,0,1,1)
        [HideInInspector] _LiquidContourDecode ("Encoded Distance Scale And Source Offset", Vector) = (1,0,0,0)
        [HideInInspector] _LiquidContourReady ("Use Baked Sprite Contour", Float) = 0
        [HideInInspector] _LiquidNativeContourReady ("Reuse Native Rounded Rect Coverage Distance", Float) = 0
        [HideInInspector] _LiquidRefractionReady ("Supported Contour And Fixed Profile Available", Float) = 0
        [HideInInspector] _LiquidProfileTex ("Baked Refraction And Edge Response", 2D) = "black" {}
        [HideInInspector] _LiquidProfileUV ("Profile Scale And Half Texel", Vector) = (1,0,0,0)
        [HideInInspector] _LiquidProfileReady ("Use Baked Optical Response", Float) = 0

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
            Name "PV Glass"
            Tags { "LightMode"="SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ MAJ_UI_LIQUID_GLASS

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
                float4 pvUV : TEXCOORD2;
                float2 glassLocal : TEXCOORD3;
                float2 localPosition : TEXCOORD4;
                half3 fillColor : TEXCOORD5;
                half3 inverseFillColor : TEXCOORD6;
                half3 inverseRemainingFillColor : TEXCOORD7;
                #ifdef MAJ_UI_LIQUID_GLASS
                half3 sourceTint : TEXCOORD8;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _PanelBaseTex;
            half _FillAlpha;
            half _ReferenceOpacity;
            float4 _FillSampleUV;
            half _TintStrength;
            half _LiquidTintStrength;
            half _LiquidArtworkBlend;
            half _GaussianButtonTintStrength;
            float4 _LiquidArtworkProtection;
            sampler2D _LiquidTintSourceTex;
            float4 _LiquidTintSourceUV;
            half4 _LiquidTintSourceColor;
            half _FrostStrength;
            half _BackdropIsButton;
            half _BackdropIsCategory;
            half _BackdropIsNativeControl;
            half _BackdropUsesPanelBlur;
            float _BackdropIsRoundedRect;
            float _RadiusTL, _RadiusTR, _RadiusBL, _RadiusBR;
            float _GlassRefraction;
            half _GlassHighlight;
            float4 _GlassUvToLocal;
            float4 _GlassShape;
            float4x4 _GlassWorldToLocal;
            float4 _LiquidGlassRect;
            float4 _LiquidGlassShape;
            float4 _LiquidGlassCircle;
            float _LiquidGlassClipSmoothness;
            float4 _LiquidGlassCorners;
            float4 _LiquidGlassOptics;
            float _LiquidRefractionTail;
            float4 _LiquidGlassUvToLocal;
            float4 _LiquidGlassSpriteRect;
            float4 _LiquidGlassSpriteCorners;
            // Distance derivatives amplify storage and sampling roundoff inside the bevel.
            // Keep the texture sample full precision on mobile as well as desktop.
            #if defined(SHADER_API_GLES) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)
            sampler2D_float _LiquidContourTex;
            #else
            Texture2D<float4> _LiquidContourTex;
            SamplerState sampler_LinearClamp;
            #endif
            float4 _LiquidContourUV;
            float4 _LiquidContourClamp;
            float2 _LiquidContourDecode;
            float _LiquidContourReady;
            float _LiquidNativeContourReady;
            float _LiquidRefractionReady;
            sampler2D _LiquidProfileTex;
            float4 _LiquidProfileUV;
            float _LiquidProfileReady;

            // Globals deliberately stay outside Properties so per-panel materials never hide new PV frames.
            sampler2D _MajPvBlurTexture;
            float4 _MajPvBlurTexture_TexelSize;
            float _MajPvBlurReady;
            sampler2D _MajUiPanelBlurTexture;
            float4 _MajUiPanelBlurTexture_TexelSize;
            float _MajUiPanelBlurReady;
            sampler2D _MajUiButtonBlurTexture;
            float4 _MajUiButtonBlurTexture_TexelSize;
            float _MajUiButtonBlurReady;
            // RT2 and RT3 retain independent circular coverage and sampling bounds.
            // Later button capture must not replace the category source's mapping.
            float4 _MajUiCategoryClipUvX;
            float4 _MajUiCategoryClipUvY;
            float4 _MajUiCategoryClipSampleBounds;
            float4 _MajUiButtonClipUvX;
            float4 _MajUiButtonClipUvY;
            // Projected circle center and an inset radius keep the single background read valid.
            float4 _MajUiButtonClipSampleBounds;
            // Select one complete optical program so Gaussian draws do not reserve liquid state.
            #ifdef MAJ_UI_LIQUID_GLASS
            #define MAJ_UI_LIQUID_STYLE 1.0
            #else
            #define MAJ_UI_LIQUID_STYLE 0.0
            #endif
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

                // The snapshot is the resolved camera prefix, before these panels, in this same frame.
                // Use original clip position before the sorting-only depth offset; no decoder UV mapping.
                output.pvUV = ComputeScreenPos(UnityObjectToClipPos(input.vertex));
                // Original, pre-ST atlas UVs survive Canvas batching and expanded shadow margins.
                output.glassLocal = input.texcoord * _GlassUvToLocal.xy + _GlassUvToLocal.zw;
                if (MAJ_UI_LIQUID_STYLE > 0.5)
                {
                    // Canvas batching changes input vertices into its own local space. Recover
                    // this particular control's coordinates before adding the sorting-only Z offset.
                    float4 worldPosition = mul(unity_ObjectToWorld, input.vertex);
                    output.glassLocal = mul(_GlassWorldToLocal, worldPosition).xy - _LiquidGlassRect.xy;
                }
                output.localPosition = input.vertex.xy;

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
                // Every supported glass slice has a uniform interior. Read its actual artwork color
                // at LOD0 per vertex, not per pixel or by CPU readback. This also follows texture
                // replacement and hardware sRGB conversion; no yellow/blue/gray palette is baked in.
                float2 fillUV = _FillSampleUV.xy * _MainTex_ST.xy + _MainTex_ST.zw;
                float3 fill = tex2Dlod(_MainTex, float4(fillUV, 0, 0)).rgb + _TextureSampleAdd.rgb;
                output.fillColor = fill;
                output.inverseFillColor = rcp(max(fill, 0.0001));
                output.inverseRemainingFillColor = rcp(max(1 - fill, 0.0001));
                #ifdef MAJ_UI_LIQUID_GLASS
                output.sourceTint = 1;
                if (MAJ_UI_LIQUID_STYLE > 0.5 && _LiquidTintSourceColor.a > 0)
                {
                    // Sample source artwork per vertex, never the already tinted scene or a CPU readback.
                    // Use the same working-space multiplier as ordinary UI; no yellow palette is baked in.
                    half3 sourceFill = tex2Dlod(_LiquidTintSourceTex, float4(_LiquidTintSourceUV.xy, 0, 0)).rgb;
                    output.sourceTint = lerp(half3(1, 1, 1), sourceFill * _LiquidTintSourceColor.rgb,
                        _LiquidTintSourceColor.a);
                }
                #endif
                return output;
            }

            half4 SampleBackdropTap(float2 uv)
            {
                half4 result;
                UNITY_BRANCH
                if (_BackdropUsesPanelBlur > 0.5h)
                {
                    result = tex2D(_MajUiPanelBlurTexture, saturate(uv));
                }
                else
                {
                    UNITY_BRANCH
                    if (_BackdropIsButton > 0.5h)
                    {
                        result = tex2D(_MajUiButtonBlurTexture, saturate(uv));
                    }
                    else
                    {
                        result = tex2D(_MajPvBlurTexture, saturate(uv));
                    }
                }
                return result;
            }
            float SampleLiquidContour(float2 uv)
            {
                float4 encoded;
                #if defined(SHADER_API_GLES) || defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)
                encoded = tex2Dlod(_LiquidContourTex, float4(uv, 0, 0));
                #else
                encoded = _LiquidContourTex.SampleLevel(sampler_LinearClamp, uv, 0);
                #endif
                // Linear decoding commutes with native bilinear filtering. Never round
                // or apply frac after this sample: interpolated channel values are fractional.
                float normalizedDistance = dot(encoded.rgb, float3(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0)) + encoded.a / 765.0;
                return normalizedDistance * _LiquidContourDecode.x + _LiquidContourDecode.y;
            }
            #ifdef MAJ_UI_LIQUID_GLASS
            // The baked field stores the smooth ellipse. Apply its authored crop only after
            // sampling so the clipped edge does not blur into the curved field's derivative.
            float GlassCropDistance(float ellipseDistance, float slabDistance)
            {
                float result = max(ellipseDistance, slabDistance);
                if (_LiquidGlassClipSmoothness > 0)
                {
                    float join = max(_LiquidGlassClipSmoothness - abs(ellipseDistance - slabDistance), 0);
                    result += join * join / (4 * _LiquidGlassClipSmoothness);
                }
                return result;
            }
            #endif
            // Coverage only: preserve foreground artwork outside its body when optics are
            // unavailable. This mask never supplies a refraction distance, normal or UV offset.
            float GlassArtworkCoverage(float2 p, float2 halfSize, float4 corners)
            {
                halfSize = max(halfSize, 1e-4);
                if (_LiquidGlassShape.y > 0.5 || _LiquidGlassShape.z > 0.5)
                {
                    float2 radii = halfSize;
                    if (_LiquidGlassShape.z > 0.5)
                    {
                        float radius = _LiquidGlassCircle.z > 0 ? _LiquidGlassCircle.z : halfSize.x;
                        float radiusY = _LiquidGlassCircle.w > 0 ? _LiquidGlassCircle.w : radius;
                        radii = max(float2(radius, radiusY), 1e-4);
                        p -= _LiquidGlassCircle.xy;
                    }
                    float2 ellipse = p / radii;
                    float edge = dot(ellipse, ellipse) - 1;
                    float aa = max(fwidth(edge) * 0.75, 1e-5);
                    float coverage = 1 - smoothstep(-aa, aa, edge);
                    if (_LiquidGlassShape.z > 0.5)
                    {
                        float slab = abs(p.y + _LiquidGlassCircle.y) - halfSize.y;
                        float slabAA = max(fwidth(slab) * 0.75, 1e-5);
                        coverage *= 1 - smoothstep(-slabAA, slabAA, slab);
                    }
                    return coverage;
                }
                float top = p.x < 0 ? corners.x : corners.y;
                float bottom = p.x < 0 ? corners.w : corners.z;
                float radius = clamp(p.y >= 0 ? top : bottom, 0, min(halfSize.x, halfSize.y));
                float2 corner = abs(p) - halfSize + radius;
                float edge = length(max(corner, 0)) + min(max(corner.x, corner.y), 0) - radius;
                float aa = max(fwidth(edge) * 0.75, 1e-5);
                return 1 - smoothstep(-aa, aa, edge);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const half alphaPrecision = half(0xff);
                input.color.a = round(input.color.a * alphaPrecision) / alphaPrecision;

                half4 artwork = MAJ_SAMPLE_TEX2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                half ready = lerp(_MajPvBlurReady, _MajUiButtonBlurReady, _BackdropIsButton);
                if (_BackdropIsCategory > 0.5h)
                {
                    ready = _MajPvBlurReady;
                }
                if (_BackdropUsesPanelBlur > 0.5h)
                {
                    ready = _MajUiPanelBlurReady;
                }
                bool liquidEnabled = false;
                #ifdef MAJ_UI_LIQUID_GLASS
                bool nativeLiquidContour = _LiquidNativeContourReady > 0.5 && _BackdropIsRoundedRect > 0.5;
                liquidEnabled = MAJ_UI_LIQUID_STYLE > 0.5 && ready >= 0.5h &&
                    _LiquidRefractionReady > 0.5 && _LiquidProfileReady > 0.5 &&
                    (nativeLiquidContour || (_LiquidContourReady > 0.5 && _LiquidGlassShape.w > 0.5));
                #endif
                // Keep all geometry and derivatives in float. Half quantizes large atlas coordinates.
                // Refraction never supplies sprite coverage or mask clipping.
                float distance = 0;
                float shortHalfSize = max(min(_GlassShape.x, _GlassShape.y), 1);
                float shapeEnabled = _GlassShape.w;
                float2 roundedGradient = 0;
                float roundedUnitsPerPixel = 1;
                half roundedCoverage = 1;
                if (_BackdropIsRoundedRect > 0.5)
                {
                    // Match Maj/UI/Rounded Rect exactly, including independent animated corner radii.
                    float width = length(float2(ddx(input.localPosition.x), ddy(input.localPosition.x)))
                        / (length(float2(ddx(input.texcoord.x), ddy(input.texcoord.x))) + 1e-5);
                    float height = length(float2(ddx(input.localPosition.y), ddy(input.localPosition.y)))
                        / (length(float2(ddx(input.texcoord.y), ddy(input.texcoord.y))) + 1e-5);
                    float2 halfSize = float2(width, height) * 0.5;
                    float2 p = (input.texcoord - 0.5) * float2(width, height);
                    float topRadius = p.x >= 0 ? _RadiusTR : _RadiusTL;
                    float bottomRadius = p.x >= 0 ? _RadiusBR : _RadiusBL;
                    float radius = min(max(p.y >= 0 ? topRadius : bottomRadius, 0), min(halfSize.x, halfSize.y));
                    float2 q = abs(p) - (halfSize - radius);
                    distance = length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
                    roundedGradient = float2(ddx(distance), ddy(distance));
                    roundedUnitsPerPixel = length(roundedGradient);
                    float aa = max(roundedUnitsPerPixel, 1e-3);
                    roundedCoverage = smoothstep(aa, -aa, distance);
                    shortHalfSize = max(min(halfSize.x, halfSize.y), 1);
                    shapeEnabled = 1;
                }
                else if (MAJ_UI_LIQUID_STYLE <= 0.5 && ready >= 0.5h && _BackdropIsNativeControl < 0.5h && shapeEnabled > 0)
                {
                    // Liquid replaces this contour completely; native Gaussian controls do not use edge lighting.
                    float2 q = abs(input.glassLocal) - (_GlassShape.xy - _GlassShape.z);
                    distance = length(max(q, 0)) + min(max(q.x, q.y), 0) - _GlassShape.z;
                }
                float2 liquidNormal = 0;
                float liquidUnitsPerPixel = 1;
                float liquidTailRemaining = 0;
                half liquidBodyCoverage = 1;
                half liquidEdgeBand = 0;
                #ifdef MAJ_UI_LIQUID_GLASS
                UNITY_BRANCH
                if (liquidEnabled && nativeLiquidContour)
                {
                    // Native UVs are not Sprite-local coordinates. The coverage SDF is already
                    // in UI units: reuse its screen gradient without a second contour evaluation
                    // or texture read. The existing roundedCoverage clips the edge exactly once.
                    liquidUnitsPerPixel = max(roundedUnitsPerPixel, 1e-5);
                    liquidNormal = roundedGradient / liquidUnitsPerPixel;
                }
                else if (liquidEnabled)
                {
                    float2 localDx = ddx(input.glassLocal);
                    float2 localDy = ddy(input.glassLocal);
                    float2 bodyPosition = input.texcoord * _LiquidGlassUvToLocal.xy + _LiquidGlassUvToLocal.zw
                        - _LiquidGlassSpriteRect.xy;
                    float2 dataUV = input.texcoord * _LiquidContourUV.xy + _LiquidContourUV.zw;
                    // Guard storage is sampled for helper derivatives. Pixels beyond it receive
                    // no substitute analytic refraction or extrapolated normal.
                    float sourceDistance = SampleLiquidContour(clamp(dataUV, _LiquidContourClamp.xy, _LiquidContourClamp.zw));
                    if (_LiquidGlassShape.z > 0.5)
                    {
                        sourceDistance = GlassCropDistance(sourceDistance, abs(bodyPosition.y) - _LiquidGlassSpriteRect.w);
                    }
                    UNITY_BRANCH
                    if (_LiquidRefractionTail > 0)
                    {
                        // Normalize against the authored body, excluding Sprite padding and
                        // shadows. The UI rectangle can be larger than the visible lens.
                        float bodyRadius = max(min(_LiquidGlassSpriteRect.z, _LiquidGlassSpriteRect.w), 1e-4);
                        liquidTailRemaining = 1 - saturate(max(-sourceDistance, 0) / bodyRadius);
                    }
                    float2 sourceGradient = float2(ddx(sourceDistance), ddy(sourceDistance));
                    float sourceRate = max(length(sourceGradient), 1e-5);
                    liquidNormal = sourceGradient / sourceRate;
                    liquidUnitsPerPixel = max(length(localDx * liquidNormal.x + localDy * liquidNormal.y), 1e-5);
                    distance = sourceDistance * liquidUnitsPerPixel / sourceRate;
                    liquidBodyCoverage = 1 - smoothstep(-liquidUnitsPerPixel * 0.75,
                        liquidUnitsPerPixel * 0.75, distance);
                    shortHalfSize = max(min(_LiquidGlassRect.z, _LiquidGlassRect.w), 1e-4);
                    shapeEnabled = 1;
                    liquidEnabled = all(dataUV >= _LiquidContourClamp.xy) && all(dataUV <= _LiquidContourClamp.zw);
                }
                #endif
                if (MAJ_UI_LIQUID_STYLE > 0.5 && ready >= 0.5h && !liquidEnabled)
                {
                    float2 bodyPosition = input.glassLocal;
                    float2 halfSize = _LiquidGlassRect.zw;
                    float4 corners = _LiquidGlassCorners;
                    if (_LiquidGlassShape.w > 0.5)
                    {
                        bodyPosition = input.texcoord * _LiquidGlassUvToLocal.xy + _LiquidGlassUvToLocal.zw
                            - _LiquidGlassSpriteRect.xy;
                        halfSize = _LiquidGlassSpriteRect.zw;
                        corners = _LiquidGlassSpriteCorners;
                    }
                    liquidBodyCoverage = GlassArtworkCoverage(bodyPosition, halfSize, corners);
                }
                float2 normal = 0;
                half bevel = 0;
                half rim = 0;
                UNITY_BRANCH
                if (MAJ_UI_LIQUID_STYLE <= 0.5 && ready >= 0.5h && _BackdropIsNativeControl < 0.5h && shapeEnabled > 0)
                {
                    float2 gradient = float2(ddx(distance), ddy(distance));
                    float inverseGradient = rsqrt(max(dot(gradient, gradient), 1e-8));
                    normal = gradient * inverseGradient;
                    float bevelWidth = shortHalfSize * 0.08;
                    bevel = (1 - smoothstep(0, bevelWidth, max(-distance, 0))) * shapeEnabled;
                    rim = (1 - smoothstep(0, bevelWidth * 0.18, abs(distance))) * shapeEnabled;
                }
                float2 pvUV = input.pvUV.xy / input.pvUV.w;
                bool usesCategoryCapture = _BackdropUsesPanelBlur < 0.5h && _BackdropIsButton < 0.5h;
                float4 captureClipUvX = usesCategoryCapture ? _MajUiCategoryClipUvX : _MajUiButtonClipUvX;
                float4 captureClipUvY = usesCategoryCapture ? _MajUiCategoryClipUvY : _MajUiButtonClipUvY;
                float4 captureSampleBounds = usesCategoryCapture
                    ? _MajUiCategoryClipSampleBounds : _MajUiButtonClipSampleBounds;
                bool clipsBackdropCapture = (usesCategoryCapture ||
                    (_BackdropIsButton > 0.5h && _BackdropIsNativeControl < 0.5h)) && captureClipUvX.w > 0.5;
                float2 captureDx = 0;
                float2 captureDy = 0;
                #ifdef MAJ_UI_LIQUID_GLASS
                UNITY_BRANCH
                if (liquidEnabled)
                {
                    captureDx = ddx(pvUV);
                    captureDy = ddy(pvUV);
                    float bandWidth = max(_LiquidGlassOptics.x, 1e-4);
                    float t = saturate(max(-distance, 0) / bandWidth);
                    float strength = saturate(_GlassRefraction / 0.008);
                    float normalizedDisplacement = 0;
                    // Only the matching baked response is used. Missing profiles never run Snell math.
                    UNITY_BRANCH
                    if (t < 1)
                    {
                        float2 response = tex2Dlod(_LiquidProfileTex,
                            float4(t * _LiquidProfileUV.x + _LiquidProfileUV.y, 0.5, 0, 0)).rg;
                        normalizedDisplacement = response.x;
                        liquidEdgeBand = response.y * strength;
                    }
                    UNITY_BRANCH
                    if (_LiquidRefractionTail > 0)
                    {
                        // Continue the peripheral lens through the body, rather than ending at
                        // the narrow reflection band. Both the center offset and its slope vanish.
                        float onset = t * t * t * (10 + t * (-15 + 6 * t));
                        // The fixed cubic-cap/Snell LUT peaks at this normalized displacement.
                        // Use only its remaining headroom: retain the existing strongest bend.
                        float headroom = 1 - saturate(normalizedDisplacement / 0.397008);
                        normalizedDisplacement += _LiquidRefractionTail * onset * liquidTailRemaining * liquidTailRemaining * headroom;
                    }
                    float displacement = normalizedDisplacement * bandWidth * strength;
                    displacement /= liquidUnitsPerPixel;
                    pvUV -= (captureDx * liquidNormal.x + captureDy * liquidNormal.y) * displacement;
                }
                #endif
                half4 color;
                if (ready < 0.5)
                {
                    // Unavailable camera capture retains original alpha artwork, including its half-opacity fill.
                    color = artwork * input.color;
                    color.a *= roundedCoverage;
                    color.rgb *= color.a;
                }
                else
                {
                    half4 baseArtwork = MAJ_SAMPLE_TEX2D(_PanelBaseTex, input.texcoord);
                    half liquidReflectionDirection = 0;
                    half liquidInnerShade = 0;
                    bool liquidFineRimActive = false;
                    float liquidFineVariance = 1;
                    // Authored fill alpha describes the shape, not glass translucency: a 0.5 fill
                    // becomes full RT coverage, while its 0.5-to-zero feather becomes one-to-zero.
                    half coverage = saturate(baseArtwork.a / _FillAlpha) * roundedCoverage;
                    UNITY_BRANCH
                    if (clipsBackdropCapture)
                    {
                        // Missing exterior data cannot be reconstructed from alpha. Keep the
                        // refracted lookup inside the same circle instead of drawing a second
                        // artwork-colored band wherever the displaced lookup lost coverage.
                        float3 samplePosition = float3(pvUV, 1);
                        float2 sampleCircle = float2(dot(samplePosition, captureClipUvX.xyz),
                            dot(samplePosition, captureClipUvY.xyz));
                        float radiusSquared = dot(sampleCircle, sampleCircle);
                        float safeRadius = captureSampleBounds.z;
                        UNITY_BRANCH
                        if (radiusSquared > safeRadius * safeRadius)
                        {
                            float radialScale = safeRadius * rsqrt(radiusSquared);
                            pvUV = captureSampleBounds.xy
                                + (pvUV - captureSampleBounds.xy) * radialScale;
                        }
                    }
                    // Both styles consume one prefiltered source; categories no longer repeat a nine-tap blur per pixel.
                    half4 captured = SampleBackdropTap(pvUV);
                    half3 backdrop = captured.rgb;
                    UNITY_BRANCH
                    if (clipsBackdropCapture)
                    {
                        // Unpremultiply the filtered valid scene, so Gaussian filtering does not
                        // spread the opaque black mask inward. Genuine black scene pixels stay black.
                        backdrop = saturate(backdrop / max(captured.a, 1.0h / 255.0h));
                    }
                    half3 reflectedBackground = backdrop;
                    // Tint strength changes the RGB multiplier, not the coverage or original artwork palette.
                    // Gaussian buttons share liquid glass's texture-derived transmission and artwork share.
                    bool useGlassColor = MAJ_UI_LIQUID_STYLE > 0.5 || _BackdropIsButton > 0.5h || _BackdropIsCategory > 0.5h
                        || _BackdropIsNativeControl > 0.5h;
                    half3 authoredFilter = input.fillColor * input.color.rgb;
                    if (useGlassColor)
                    {
                        half transmissionStrength = _LiquidTintStrength;
                        if (MAJ_UI_LIQUID_STYLE < 0.5 && _BackdropIsButton > 0.5h)
                        {
                            // Strong Gaussian filtering gathers dark exterior pixels around the ring.
                            // Reduce absorption only here; liquid and non-peripheral recipes stay intact.
                            transmissionStrength = min(transmissionStrength, _GaussianButtonTintStrength);
                        }
                        #ifdef MAJ_UI_LIQUID_GLASS
                        authoredFilter = lerp(half3(1, 1, 1), authoredFilter, transmissionStrength) * input.sourceTint;
                        #else
                        authoredFilter = lerp(half3(1, 1, 1), authoredFilter, transmissionStrength);
                        #endif
                        // A less absorptive curved surface reveals the refracted background color.
                        // The flat center keeps the configured texture-derived multiplier.
                        authoredFilter = lerp(authoredFilter, half3(1, 1, 1),
                            0.36h * liquidEdgeBand);
                        backdrop *= authoredFilter;
                    }
                    else
                    {
                        half3 colorFilter = lerp(half3(1, 1, 1), authoredFilter, _TintStrength);
                        backdrop = (backdrop * (1 - _FrostStrength) + _FrostStrength) * colorFilter;
                    }
                    // A restrained directional rim and opposing dark bevel suggest thickness without
                    // normal textures, additional PV reads, chromatic noise, or another render pass.
                    if (liquidEnabled)
                    {
                        // Reflection width remains continuous. Filter the subtle inner lip here;
                        // reflection coverage is integrated after the nonlinear Max comparison below.
                        if (_LiquidGlassOptics.z > 0)
                        {
                            float width = _LiquidGlassOptics.z;
                            float sigma = max(_LiquidGlassOptics.w, width * 0.35);
                            float variance = max(sigma * sigma
                                + liquidUnitsPerPixel * liquidUnitsPerPixel / 12, 1e-8);
                            float inside = max(-distance, 0);
                            float sigmaAA = sqrt(variance);
                            float inner = inside - (width + 2 * sigmaAA);
                            bool innerActive = abs(inner) <= 6 * sigmaAA;
                            float fineSigma = 0.5 * sigma;
                            liquidFineVariance = max(fineSigma * fineSigma, 1e-8);
                            // Each footprint offset is at most sqrt(2)/4 pixels. This slightly wider
                            // bound retains every sample within six sigma without evaluating its exp2.
                            liquidFineRimActive = abs(distance) <= 6 * max(fineSigma, 0.0001)
                                + 0.36 * liquidUnitsPerPixel;
                            UNITY_BRANCH
                            if (liquidEdgeBand > 0 || liquidFineRimActive || innerActive)
                            {
                                float2 backdropTexelSize = _BackdropUsesPanelBlur > 0.5h
                                    ? _MajUiPanelBlurTexture_TexelSize.xy : _BackdropIsButton > 0.5h
                                    ? _MajUiButtonBlurTexture_TexelSize.xy : _MajPvBlurTexture_TexelSize.xy;
                                float2 imageNormal = (captureDx * liquidNormal.x + captureDy * liquidNormal.y)
                                    / max(abs(backdropTexelSize), 1e-6);
                                float2 screenNormal = imageNormal * rsqrt(max(dot(imageNormal, imageNormal), 1e-8));
                                half light = saturate(dot(screenNormal, float2(-0.6, 0.8)) * 0.5 + 0.5);
                                liquidReflectionDirection = 0.3h + 0.7h * light * light;
                                UNITY_BRANCH
                                if (innerActive)
                                {
                                    // The omitted six-sigma tail is below 1e-9 RGB; the authored lip stays unchanged.
                                    half secondary = (1 - light) * (1 - light);
                                    float integratedCoverage = sigma * rsqrt(variance);
                                    liquidInnerShade = exp2(-0.72134752 * inner * inner / variance)
                                        * integratedCoverage * (0.02h + 0.04h * secondary);
                                }
                            }
                        }
                        // Shade the transmitted background before reconstructing foreground ink.
                        // Reusing this read adds no texture taps and leaves dark glyphs/dashes intact.
                        half angularReflection = 0.36h * liquidEdgeBand * liquidEdgeBand;
                        half3 reflectedColor = saturate(reflectedBackground * 1.12h);
                        backdrop = lerp(backdrop, max(backdrop, reflectedColor),
                            angularReflection * liquidReflectionDirection);
                    }
                    else if (MAJ_UI_LIQUID_STYLE <= 0.5 && _BackdropIsNativeControl < 0.5h)
                    {
                        half lighting = dot(normal, float2(-0.6, -0.8));
                        half highlight = _GlassHighlight * (rim * (0.45h + 0.55h * max(lighting, 0))
                            + bevel * bevel * 0.12h);
                        backdrop *= 1 - _GlassHighlight * bevel * max(-lighting, 0) * 0.35h;
                        backdrop = lerp(backdrop, half3(1, 1, 1), highlight);
                    }
                    if (useGlassColor)
                    {
                        // The original artwork supplies the additive share; its palette also filters
                        // transmission. Keep the same proportions in both styles without changing coverage.
                        backdrop = lerp(backdrop, input.fillColor * input.color.rgb, _LiquidArtworkBlend);
                    }

                    // Unmatte against the same sampled fill, rather than guessing a white-pixel mask.
                    // This retains the orange dashed decoration even when the moving PV is nearly black.
                    half3 dark = max((input.fillColor - artwork.rgb) * input.inverseFillColor, 0);
                    half3 light = max((artwork.rgb - input.fillColor) * input.inverseRemainingFillColor, 0);
                    half3 difference = max(dark, light);
                    half inkAlpha = saturate(max(difference.r, max(difference.g, difference.b)));
                    half3 inkPremultiplied = max(artwork.rgb - input.fillColor * (1 - inkAlpha), 0);
                    half3 surface = backdrop * (1 - inkAlpha) + inkPremultiplied * input.color.rgb;
                    // Resolve the edge target over the finished, texture-tinted artwork. Keep
                    // neutral light independent of the authored RGB and preserve the UI alpha blend.
                    if (liquidEnabled)
                    {
                        half3 shadedSurface = surface * (1 - saturate(liquidInnerShade));
                        // Finish transmission/body masking before lighting the edge. Applying this
                        // coverage again after the rim would dim and move the apparent reflection inward.
                        shadedSurface = lerp(artwork.rgb * input.color.rgb, shadedSurface, liquidBodyCoverage);
                        surface = saturate(shadedSurface);
                        UNITY_BRANCH
                        if (liquidFineRimActive)
                        {
                            half headroom = saturate(1 - max(shadedSurface.r, max(shadedSurface.g, shadedSurface.b)));
                            // A fixed thin profile retains the List reference width. Brightness controls
                            // the available neutral light, never a threshold that erases bright buttons.
                            // Four footprint samples retain subpixel AA without a one-pixel minimum.
                            float delta = -distance;
                            float2 footprint = 0.25 * liquidUnitsPerPixel
                                * float2(liquidNormal.x + liquidNormal.y, liquidNormal.x - liquidNormal.y);
                            float4 sampleDelta = delta + float4(footprint.x, -footprint.x, footprint.y, -footprint.y);
                            float4 profile = exp2(-0.72134752 * sampleDelta * sampleDelta / liquidFineVariance);
                            half gain = 1.6h * max(_GlassHighlight / 0.045h, 0);
                            float4 reflected = min(headroom, headroom * profile * gain);
                            half fineDirection = min(1.0h, liquidReflectionDirection * 1.2h);
                            half reflection = dot(reflected, float4(0.25, 0.25, 0.25, 0.25)) * fineDirection;
                            half3 edgeTarget = shadedSurface + reflection;
                            // Neutral Max light is applied last; original PNG coverage below clips it once.
                            surface = saturate(max(shadedSurface, edgeTarget));
                        }
                    }

                    else if (MAJ_UI_LIQUID_STYLE > 0.5)
                    {
                        // Missing optics keep the body-exterior artwork instead of tinting
                        // original decorations with the unshifted background capture.
                        surface = lerp(artwork.rgb * input.color.rgb, surface, liquidBodyCoverage);
                    }

                    // Only the outlined atlas adds the shadow outside the original, normalized panel contour.
                    half remaining = max(1 - baseArtwork.a, 1.0h / 255.0h);
                    half shadowAlpha = saturate((artwork.a - baseArtwork.a) / remaining);
                    half3 shadow = clamp((artwork.rgb * artwork.a - baseArtwork.rgb * baseArtwork.a)
                        / remaining, 0, shadowAlpha);
                    // Normalize the Image's authored resting alpha; only relative UI animation /
                    // CanvasGroup fades reveal the sharp scene. Tint strength belongs to RGB above.
                    half opacity = saturate(input.color.a / _ReferenceOpacity);
                    color = half4(surface * coverage + shadow * (1 - coverage),
                        coverage + shadowAlpha * (1 - coverage));
                    color.rgb *= opacity;
                    color.a *= opacity;
                    if (MAJ_UI_LIQUID_STYLE > 0.5 && _LiquidArtworkProtection.z > 0.5)
                    {
                        // UserInfo's binding rings overlap the glass body's left edge. Restore
                        // their original premultiplied RGBA after all tint, ink and rim operations.
                        // Existing Sprite UVs retain this region under native nine-slice layout.
                        float sourceX = input.texcoord.x * _LiquidGlassUvToLocal.x + _LiquidGlassUvToLocal.z;
                        float feather = max(_LiquidArtworkProtection.y,
                            max(abs(ddx(sourceX)) + abs(ddy(sourceX)), 1e-5));
                        half preserve = 1 - smoothstep(_LiquidArtworkProtection.x - feather,
                            _LiquidArtworkProtection.x + feather, sourceX);
                        half4 original = artwork * input.color;
                        original.rgb *= original.a;
                        color = lerp(color, original, preserve);
                    }
                }
                #ifdef UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                color *= mask.x * mask.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                // Retain UGUI's optional stencil-mask contract, not the old visual 0.5 cutoff.
                clip(color.a - 0.001h);
                #endif

                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
