Shader "Hidden/Maj/UI PV Gaussian"
{
    Properties
    {
        _MainTex ("Source Texture", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        HLSLINCLUDE
        #include "UnityCG.cginc"

        #if defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)
        sampler2D _MainTex;
        #else
        Texture2D _MainTex;
        SamplerState sampler_LinearClamp;
        #endif
        float4 _MainTex_TexelSize;
        float _MajUiSourceNeedsManualLinear;
        // Supplied from the actual projected output dimensions, independently of the decoded video size.
        float4 _MajPvDownsampleStep;
        // Fixed sigma-four / radius-eight Gaussian on the quarter-resolution grid.
        // Each XY pair stores (weight, bilinear offset), shared by both filter axes.
        float4 _MajUiPanelBlurStep;
        half _MajUiGaussianCenterWeight;
        float4 _MajUiGaussianPairs[4];
        // The active RT2/RT3 capture publishes its main-display circle. Alpha stores
        // valid coverage independently of scene colors and the camera attachment's alpha.
        float4 _MajUiCaptureClipUvX;
        float4 _MajUiCaptureClipUvY;

        struct Varyings
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(appdata_img input)
        {
            Varyings output;
            UNITY_INITIALIZE_OUTPUT(Varyings, output);
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.pos = UnityObjectToClipPos(input.vertex);
            output.uv = input.texcoord;
            // Blit exposes an inverted render-texture source through a negative texel height.
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0)
            {
                output.uv.y = 1.0 - output.uv.y;
            }
            #endif
            return output;
        }

        // Borrowed scene attachments can be Point filtered. The filtering contract belongs
        // to this draw, without changing the source texture or introducing a copy.
        half4 SampleLinearClamp(float2 uv)
        {
            #if defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)
            // Inline samplers are unsupported on GL. Four texel-center LOD0 reads
            // reconstruct bilinear clamp independently of the borrowed sampler state.
            float2 size = max(abs(_MainTex_TexelSize.zw), 1);
            float2 texel = rcp(size);
            float2 lower = texel * 0.5;
            float2 upper = 1 - lower;
            UNITY_BRANCH
            if (_MajUiSourceNeedsManualLinear < 0.5)
            {
                return tex2Dlod(_MainTex, float4(clamp(uv, lower, upper), 0, 0));
            }
            float2 pixel = saturate(uv) * size - 0.5;
            float2 cell = floor(pixel);
            float2 weight = pixel - cell;
            float2 origin = (cell + 0.5) * texel;
            half4 a = tex2Dlod(_MainTex, float4(clamp(origin, lower, upper), 0, 0));
            half4 b = tex2Dlod(_MainTex, float4(clamp(origin + float2(texel.x, 0), lower, upper), 0, 0));
            half4 c = tex2Dlod(_MainTex, float4(clamp(origin + float2(0, texel.y), lower, upper), 0, 0));
            half4 d = tex2Dlod(_MainTex, float4(clamp(origin + texel, lower, upper), 0, 0));
            return lerp(lerp(a, b, weight.x), lerp(c, d, weight.x), weight.y);
            #else
            return _MainTex.SampleLevel(sampler_LinearClamp, uv, 0);
            #endif
        }

        half4 SampleCapture(float2 uv)
        {
            half4 color = SampleLinearClamp(uv);
            UNITY_BRANCH
            if (_MajUiCaptureClipUvX.w > 0.5)
            {
                // Color reads use the source orientation established by Vert. The circle
                // belongs to the owned destination: undo only that UV flip for coverage.
                // Applying it per tap also maps each downsample offset to the correct side.
                float2 coverageUV = uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                {
                    coverageUV.y = 1.0 - coverageUV.y;
                }
                #endif
                float3 position = float3(coverageUV, 1);
                float2 circle = float2(dot(position, _MajUiCaptureClipUvX.xyz),
                    dot(position, _MajUiCaptureClipUvY.xyz));
                float distance = length(circle) - 1;
                float feather = max(fwidth(distance), 1e-6);
                half coverage = 1 - smoothstep(-0.5 * feather, 0.5 * feather, distance);
                color = half4(color.rgb * coverage, coverage);
            }
            return color;
        }

        half4 LinearCopy(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SampleCapture(input.uv);
        }

        half4 Downsample(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            // Four bilinear samples cover the destination footprint before the Gaussian.
            // At an exact 4:1 reduction they integrate the sixteen source texels of a box.
            // This low-pass approximation is not an exact box integral for arbitrary reduction ratios.
            float2 texel = abs(_MajPvDownsampleStep.xy);
            half4 color = SampleCapture(input.uv + texel * float2(-1, -1));
            color += SampleCapture(input.uv + texel * float2(1, -1));
            color += SampleCapture(input.uv + texel * float2(-1, 1));
            color += SampleCapture(input.uv + texel * float2(1, 1));
            return color * 0.25h;
        }

        half4 PanelGaussian(float2 uv, float2 stepUV)
        {
            half4 color = SampleLinearClamp(uv) * _MajUiGaussianCenterWeight;
            [unroll]
            for (int pair = 0; pair < 4; pair++)
            {
                float2 packed = _MajUiGaussianPairs[pair].xy;
                half weight = packed.x;
                float offset = packed.y;
                color += SampleLinearClamp(uv + stepUV * offset) * weight;
                color += SampleLinearClamp(uv - stepUV * offset) * weight;
            }
            return color;
        }

        half4 PanelHorizontal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return PanelGaussian(input.uv, float2(abs(_MajUiPanelBlurStep.x), 0));
        }

        half4 PanelVertical(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return PanelGaussian(input.uv, float2(0, abs(_MajUiPanelBlurStep.y)));
        }
        ENDHLSL
        Pass
        {
            Name "PV Low-pass Downsample"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Downsample
            ENDHLSL
        }
        Pass
        {
            Name "Strong Panel Gaussian Horizontal"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment PanelHorizontal
            ENDHLSL
        }
        Pass
        {
            Name "Strong Panel Gaussian Vertical"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment PanelVertical
            ENDHLSL
        }
        Pass
        {
            Name "Scene Prefix Linear Copy"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment LinearCopy
            ENDHLSL
        }
    }
    Fallback Off
}
