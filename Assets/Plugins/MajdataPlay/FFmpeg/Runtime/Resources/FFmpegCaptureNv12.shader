Shader "Hidden/FFmpeg/CaptureNv12"
{
    Properties { _MainTex ("Scaled capture", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            // Integer arithmetic and texel loads keep the samples identical to VideoPixelKernels.ConvertRgbaToYuv420.
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Nv12
            #include "UnityCG.cginc"

            // The scaled RGBA capture. Load and SV_Position both address memory rows, which AsyncGPUReadback returns in
            // order on every graphics API, so no API-specific flip is involved.
            Texture2D _MainTex;
            // x: width, y: height, z: 1 when the capture rows are bottom-first.
            float4 _FFCLayout;
            // Fixed-point weights (red, green, blue) and rounding offset of luma, blue difference and red difference.
            float4 _FFCLuma;
            float4 _FFCBlue;
            float4 _FFCRed;
            // x: fraction bits of luma, y: fraction bits of chroma (which also divide the four-pixel sum).
            float4 _FFCShifts;

            // Reads the 8-bit sRGB codes of one pixel, addressed by its image row counted from the top.
            int3 LoadCodes(int x, int imageRow)
            {
                int height = (int)_FFCLayout.y;
                int row = _FFCLayout.z > 0.5 ? height - 1 - imageRow : imageRow;
                float3 color = saturate(_MainTex.Load(int3(x, row, 0)).rgb);
                #ifndef UNITY_COLORSPACE_GAMMA
                    // Linear projects sample sRGB targets decoded; the CPU path converts the encoded bytes.
                    color = float3(LinearToGammaSpaceExact(color.r), LinearToGammaSpaceExact(color.g), LinearToGammaSpaceExact(color.b));
                #endif
                return (int3)round(color * 255.0);
            }

            // Applies one fixed-point weight set to channel codes or 2x2 sums.
            int Weigh(float4 weights, int3 codes, float shift)
            {
                int3 factors = (int3)weights.xyz;
                return (factors.x * codes.x + factors.y * codes.y + factors.z * codes.z + (int)weights.w) >> (int)shift;
            }

            // Output rows 0 to height - 1 hold the Y plane; the next height / 2 rows hold Cb, Cr pairs of 2x2 blocks.
            float4 Nv12(v2f_img input) : SV_Target
            {
                int height = (int)_FFCLayout.y;
                // The full-precision pixel center, unlike the half-precision UV of v2f_img, is exact at any size.
                int x = (int)input.pos.x;
                int row = (int)input.pos.y;
                int code;
                if (row < height)
                {
                    code = Weigh(_FFCLuma, LoadCodes(x, row), _FFCShifts.x);
                }
                else
                {
                    int column = x & ~1;
                    int top = 2 * (row - height);
                    int3 sums = LoadCodes(column, top) + LoadCodes(column + 1, top)
                        + LoadCodes(column, top + 1) + LoadCodes(column + 1, top + 1);
                    code = Weigh((x & 1) == 0 ? _FFCBlue : _FFCRed, sums, _FFCShifts.y);
                }

                return float4(code / 255.0, 0.0, 0.0, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
