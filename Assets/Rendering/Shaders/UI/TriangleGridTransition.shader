Shader "UI/Majdata Triangle Grid Transition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // Shader-based circular clip. Replaces the stencil UI Mask that used to
        // clip MainImage (and its stencil-copied siblings) into the 1080 circle.
        _Center ("Mask Center", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Mask Radius", Range(0, 1)) = 0.5
        _Feather ("Mask Feather", Range(0, 0.2)) = 0.0035
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "RenderPipeline"="UniversalPipeline"
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "TriangleGridTransition"
            Tags { "LightMode"="SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            half4 _Color;
            half4 _TextureSampleAdd;

            // Unity UI creates stencil-material copies for masked children. Keeping
            // layout values global makes every copy update together in edit preview.
            float _MajSceneTriangleColumns;
            float _MajSceneTriangleFadeSpan;
            float _MajSceneTriangleGridRotation;
            float _MajSceneTriangleSpinDegrees;
            float _MajSceneTriangleClosing;
            float _MajSceneTransitionProgress;

            // Circular clip, ported from UI/Circle Mask. Kept as regular (non-global)
            // material properties since the circle is intrinsic to this quad's UV
            // space, not something the scene transition needs to drive per-frame.
            float2 _Center;
            float _Radius;
            float _Feather;

            static const float SQRT_THREE = 1.73205080757;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = TransformObjectToHClip(input.vertex.xyz);
                output.color = input.color * _Color;
                output.texcoord = input.texcoord;
                return output;
            }

            float2 Rotate(float2 value, float angle)
            {
                float sine;
                float cosine;
                sincos(angle, sine, cosine);
                return float2(
                    cosine * value.x - sine * value.y,
                    sine * value.x + cosine * value.y);
            }

            // Same falloff as UI/Circle Mask: a distance-to-center smoothstep,
            // widened by fwidth so the edge stays a stable ~1px wide at any scale.
            float CircleMaskAlpha(float2 uv)
            {
                float dist = distance(uv, _Center);
                float aa = fwidth(dist);
                return 1.0 - smoothstep(
                    _Radius - _Feather - aa,
                    _Radius + aa,
                    dist);
            }

            half4 frag(v2f input) : SV_Target
            {
                float coverage = saturate(_MajSceneTransitionProgress);
                float columns = max(3.0, round(_MajSceneTriangleColumns));
                float fadeSpan = clamp(_MajSceneTriangleFadeSpan, 0.2, 0.4);

                // Open is normally disabled by CanvasRenderer, but this also keeps
                // material previews and interrupted terminal frames inexpensive.
                if (coverage <= 0.00001)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }
                if (coverage >= 0.99999)
                {
                    half4 fullColor = tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                    fullColor.rgb *= input.color.rgb;
                    fullColor.a *= input.color.a * CircleMaskAlpha(input.texcoord);

                    #ifdef UNITY_UI_ALPHACLIP
                        clip(fullColor.a - 0.001);
                    #endif

                    return fullColor;
                }

                // MainImage is the square 1080 x 1080 Main Display quad. Building
                // the grid in its local UV space keeps the origin at its true center
                // on every aspect ratio, independent of a phone camera viewport.
                float2 position = input.texcoord * 2.0 - 1.0;
                float gridRotation = radians(_MajSceneTriangleGridRotation);
                float2 gridPosition = Rotate(position, gridRotation);

                // Build an equilateral-triangle lattice. Every rhombus is split into
                // one upright and one inverted triangle.
                float side = 2.0 / columns;
                float height = side * SQRT_THREE * 0.5;
                // Put the exact screen center on the shared horizontal edge of one
                // upright and one inverted triangle. The first fading pair is then
                // vertically symmetric; centering a single triangle's centroid here
                // makes the wave look shifted because a triangle is not Y-symmetric.
                float2 centerOffset = float2(side * 0.5, 0.0);
                float2 latticePosition = gridPosition + centerOffset;
                float latticeY = latticePosition.y / height;
                float latticeX = latticePosition.x / side - latticeY * 0.5;
                float2 latticeCell = floor(float2(latticeX, latticeY));
                float2 withinCell = frac(float2(latticeX, latticeY));

                float upperHalf = step(1.0, withinCell.x + withinCell.y);
                float triangleCenterOffset = lerp(1.0 / 3.0, 2.0 / 3.0, upperHalf);
                float2 triangleCenterLattice = latticeCell + triangleCenterOffset;
                float2 triangleCenter = float2(
                    side * (triangleCenterLattice.x + triangleCenterLattice.y * 0.5),
                    height * triangleCenterLattice.y) - centerOffset;

                float circularDistance = saturate(length(triangleCenter));
                // Preserve the original radial fold: Close assembles edge-to-center
                // and Open is its exact opposite, center-to-edge.
                float closeOrder = 1.0 - circularDistance;
                float openOrder = circularDistance;
                float isClosing = step(0.5, _MajSceneTriangleClosing);
                float transitionProgress = lerp(1.0 - coverage, coverage, isClosing);
                float tileOrder = lerp(openOrder, closeOrder, isClosing);
                float fadeStart = tileOrder * (1.0 - fadeSpan);
                float localProgress = saturate(
                    (transitionProgress - fadeStart) / fadeSpan);
                float tileAlpha = lerp(1.0 - localProgress, localProgress, isClosing);

                // Rotate and scale every fragment around its triangle centroid.
                // Upright and inverted faces spin in opposite directions, while
                // the source texture follows the exact same transform.
                float foldScale = lerp(0.03, 1.0, tileAlpha);
                float spinAmount = 1.0 - tileAlpha;
                float spinDirection = lerp(-1.0, 1.0, upperHalf);
                float triangleRotation = radians(_MajSceneTriangleSpinDegrees)
                    * spinDirection * spinAmount * spinAmount;
                float2 unfoldedPosition = triangleCenter + Rotate(
                    gridPosition - triangleCenter,
                    -triangleRotation) / max(foldScale, 0.001);

                float2 triangleCenterDisplayPosition = Rotate(
                    triangleCenter,
                    -gridRotation);
                float2 triangleCenterUv = triangleCenterDisplayPosition
                    * 0.5 + 0.5;
                float2 foldedTextureUv = triangleCenterUv + Rotate(
                    input.texcoord - triangleCenterUv,
                    -triangleRotation) / max(foldScale, 0.001);
                foldedTextureUv = saturate(foldedTextureUv);
                foldedTextureUv = lerp(
                    foldedTextureUv,
                    input.texcoord,
                    smoothstep(0.995, 1.0, tileAlpha));

                float2 unfoldedLatticePosition = unfoldedPosition + centerOffset;
                float unfoldedLatticeY = unfoldedLatticePosition.y / height;
                float unfoldedLatticeX = unfoldedLatticePosition.x / side
                    - unfoldedLatticeY * 0.5;
                float2 unfoldedWithinCell = float2(
                    unfoldedLatticeX,
                    unfoldedLatticeY) - latticeCell;

                // Barycentric coordinates provide the rotated triangle mask.
                float3 lowerBarycentric = float3(
                    1.0 - unfoldedWithinCell.x - unfoldedWithinCell.y,
                    unfoldedWithinCell.x,
                    unfoldedWithinCell.y);
                float3 upperBarycentric = float3(
                    unfoldedWithinCell.x + unfoldedWithinCell.y - 1.0,
                    1.0 - unfoldedWithinCell.y,
                    1.0 - unfoldedWithinCell.x);
                float3 barycentric = lerp(
                    lowerBarycentric,
                    upperBarycentric,
                    upperHalf);
                float minimumBarycentric = min(
                    barycentric.x,
                    min(barycentric.y, barycentric.z));
                float antialiasWidth = max(fwidth(minimumBarycentric), 0.0005);
                float foldShape = smoothstep(
                    -antialiasWidth,
                    0.0,
                    minimumBarycentric);
                // Fully unfolded tiles bypass the AA ramp so no hairline seams
                // remain after Close has completed.
                foldShape = lerp(
                    foldShape,
                    1.0,
                    smoothstep(0.995, 1.0, tileAlpha));
                float foldPulse = 4.0 * tileAlpha * (1.0 - tileAlpha);
                float foldHighlight = foldPulse * (1.0 - saturate(
                    abs(minimumBarycentric)
                                        / (antialiasWidth * 4.5))) * 0.24;
                tileAlpha *= foldShape;

                half4 color = tex2D(_MainTex, foldedTextureUv) + _TextureSampleAdd;
                color.rgb *= input.color.rgb;
                color.rgb = lerp(color.rgb, half3(1.0, 1.0, 1.0), foldHighlight);
                // Circular clipping now lives entirely in this shader (see
                // CircleMaskAlpha) instead of relying on a stencil UI Mask, so the
                // fold effect and the 1080 circle boundary share one code path.
                color.a *= input.color.a * tileAlpha * CircleMaskAlpha(input.texcoord);

                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDHLSL
        }
    }
}
