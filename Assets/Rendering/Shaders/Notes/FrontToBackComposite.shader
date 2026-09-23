// 把 front-to-back 累积出来的 note RT 以 premultiplied over 合成回 camera color
Shader "MajdataPlay/Notes/FrontToBackComposite"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "FrontToBackComposite"

            Cull Off
            ZWrite Off
            ZTest Always
            // RT 里已经是 premultiplied alpha, 所以是 One / OneMinusSrcAlpha
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, IN.texcoord);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
