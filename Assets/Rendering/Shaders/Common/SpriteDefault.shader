// Adapted from the official Unity 6000.3.17f1 built-in shader sources.
// Only image sampling uses MRP's additive quality mip bias; Unity interfaces remain compatible.
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
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

Shader "Maj/Sprites/Default"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "SpriteDefault"
            Tags { "LightMode"="SRPDefaultUnlit" }
        HLSLPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment MajSpriteDefaultFragment
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"
            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajMipSampling.hlsl"

            // Keep SpriteVert, UnityPerDrawSprite and Unity's instancing fields from the engine include.
            // The default renderer/vertex tint, flip, PixelSnap and external-alpha contracts stay unchanged.
            fixed4 MajSampleSpriteDefaultTexture(float2 uv)
            {
                fixed4 color = MAJ_SAMPLE_TEX2D(_MainTex, uv);
            #if ETC1_EXTERNAL_ALPHA
                fixed4 alpha = MAJ_SAMPLE_TEX2D(_AlphaTex, uv);
                color.a = lerp(color.a, alpha.r, _EnableExternalAlpha);
            #endif
                return color;
            }

            fixed4 MajSpriteDefaultFragment(v2f IN) : SV_Target
            {
                fixed4 color = MajSampleSpriteDefaultTexture(IN.texcoord) * IN.color;
                color.rgb *= color.a;
                return color;
            }
        ENDHLSL
        }
    }
}
