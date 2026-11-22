Shader "Hidden/RenderProbe"
{
    Properties
    {
        _MainTex ("RGBA source", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            sampler2D _MainTex;
            struct VertexInput
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct VertexOutput
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            VertexOutput Vert(VertexInput input)
            {
                VertexOutput output;
                output.position = float4(input.position.xy * 2.0, 0.0, 1.0);
                output.uv = input.uv;
                return output;
            }
            float4 Frag(VertexOutput input) : SV_Target
            {
                return tex2D(_MainTex, input.uv);
            }
            ENDCG
        }
    }
    Fallback Off
}
