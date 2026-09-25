Shader "UI/MajRadar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}

        _FillColor ("Fill Color", Color) = (0,1,1,0.5)
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width", Range(0.001,0.05)) = 0.01

        _V0 ("Value 0", Range(0,1)) = 1
        _V1 ("Value 1", Range(0,1)) = 1
        _V2 ("Value 2", Range(0,1)) = 1
        _V3 ("Value 3", Range(0,1)) = 1
        _V4 ("Value 4", Range(0,1)) = 1
        _V5 ("Value 5", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Radar"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _FillColor;
            float4 _OutlineColor;
            float _OutlineWidth;

            float _V0;
            float _V1;
            float _V2;
            float _V3;
            float _V4;
            float _V5;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;

                return OUT;
            }

            float2 GetVertex(int idx)
            {
                float value;

                switch(idx)
                {
                    case 0: value = _V0; break;
                    case 1: value = _V1; break;
                    case 2: value = _V2; break;
                    case 3: value = _V3; break;
                    case 4: value = _V4; break;
                    default: value = _V5; break;
                }
                float radius = max(value, 0.01);
                float angle = radians(90.0 - idx * 60.0);

                return float2(
                    cos(angle),
                    sin(angle)
                ) * radius;
            }

            float Cross2D(float2 a, float2 b)
            {
                return a.x * b.y - a.y * b.x;
            }

            bool PointInTriangle(
                float2 p,
                float2 a,
                float2 b,
                float2 c)
            {
                float d1 = Cross2D(b - a, p - a);
                float d2 = Cross2D(c - b, p - b);
                float d3 = Cross2D(a - c, p - c);

                return
                (
                    d1 >= 0 &&
                    d2 >= 0 &&
                    d3 >= 0
                )
                ||
                (
                    d1 <= 0 &&
                    d2 <= 0 &&
                    d3 <= 0
                );
            }

            float DistanceToSegment(
                float2 p,
                float2 a,
                float2 b)
            {
                float2 ab = b - a;

                float denom = max(dot(ab, ab), 0.0001);

                float t =
                    saturate(
                        dot(p - a, ab) / denom
                    );

                float2 closest = a + ab * t;

                return distance(p, closest);
            }

            half4 frag(Varyings IN) : SV_Target
            {

                float2 p = IN.uv * 2.0 - 1.0;

                float2 verts[6];

                [unroll]
                for (int v = 0; v < 6; v++)
                {
                    verts[v] = GetVertex(v);
                }

                bool inside = false;

                [unroll]
                for (int tri = 0; tri < 6; tri++)
                {
                    int next = (tri + 1) % 6;

                    inside =
                        inside ||
                        PointInTriangle(
                            p,
                            float2(0,0),
                            verts[tri],
                            verts[next]
                        );
                }

                float edgeDist = 999.0;

                [unroll]
                for (int edge = 0; edge < 6; edge++)
                {
                    int next = (edge + 1) % 6;

                    edgeDist = min(
                        edgeDist,
                        DistanceToSegment(
                            p,
                            verts[edge],
                            verts[next]
                        )
                    );
                }

                float aa = fwidth(edgeDist);

                float outline =
                    1.0 -
                    smoothstep(
                        _OutlineWidth - aa,
                        _OutlineWidth + aa,
                        edgeDist
                    );

                float4 col = float4(0,0,0,0);

                if (inside)
                {
                    col = _FillColor;
                }

                col = lerp(col, _OutlineColor, outline);

                return col * IN.color;
            }

            ENDHLSL
        }
    }
}