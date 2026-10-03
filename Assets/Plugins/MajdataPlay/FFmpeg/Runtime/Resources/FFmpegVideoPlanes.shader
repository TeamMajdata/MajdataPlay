Shader "Hidden/FFmpeg/VideoPlanes"
{
    Properties { _MainTex ("Decoded surface", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _FFUChroma;
        float4 _FFUTransform;
        float4 _FFUColor;

        float2 NativeUV(float2 uv)
        {
            // FFmpeg rotation is clockwise; input texture rows are top-down.
            if (_FFUTransform.x > 2.5) return float2(uv.y, uv.x);
            if (_FFUTransform.x > 1.5) return float2(1.0 - uv.x, uv.y);
            if (_FFUTransform.x > 0.5) return float2(1.0 - uv.y, 1.0 - uv.x);
            return float2(uv.x, 1.0 - uv.y);
        }
        fixed4 NV12(v2f_img input) : SV_Target
        {
            float2 uv = NativeUV(input.uv);
            float y = tex2D(_MainTex, uv).r;
            float2 cbcr = tex2D(_FFUChroma, uv).rg - (128.0 / 255.0);
            if (_FFUColor.x < 0.5)
            {
                y = (y - 16.0 / 255.0) * (255.0 / 219.0);
                cbcr *= 255.0 / 224.0;
            }
            float3 rgb;
            if (_FFUColor.y > 0.5)
                rgb = float3(y + 1.5748 * cbcr.y, y - 0.187324 * cbcr.x - 0.468124 * cbcr.y, y + 1.8556 * cbcr.x);
            else
                rgb = float3(y + 1.402 * cbcr.y, y - 0.344136 * cbcr.x - 0.714136 * cbcr.y, y + 1.772 * cbcr.x);
            rgb = saturate(rgb);
            #ifndef UNITY_COLORSPACE_GAMMA
                rgb = GammaToLinearSpace(rgb);
            #endif
            return fixed4(rgb, 1.0);
        }
        fixed4 RGBA(v2f_img input) : SV_Target
        {
            float3 rgb = tex2D(_MainTex, NativeUV(input.uv)).rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                rgb = GammaToLinearSpace(rgb);
            #endif
            return fixed4(rgb, 1.0);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment NV12
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment RGBA
            ENDCG
        }
    }
    Fallback Off
}
