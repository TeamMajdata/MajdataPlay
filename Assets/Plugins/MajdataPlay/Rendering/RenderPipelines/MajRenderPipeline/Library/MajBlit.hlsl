// Adapted from Unity Graphics 17.3.0. Copyright (c) Unity Technologies ApS.
// Unity Companion License; see LICENSE-Unity.md and SOURCE.md in this folder.
#ifndef MAJ_BLIT_INCLUDED
#define MAJ_BLIT_INCLUDED
#include "MajCore.hlsl"

// 只迁移 FrontToBackComposite 用到的 Blitter 三角形接口；不携带 Cube / HDR / 八面体 / 灯光工具。
// _BlitTexture 与 _BlitScaleBias 名字保留，已有 Blitter 调用不需要改参数。
TEXTURE2D_X(_BlitTexture);
SAMPLER(sampler_LinearClamp);
float4 _BlitScaleBias;

struct Attributes
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings Vert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float2 triangleUV = float2((input.vertexID << 1) & 2, input.vertexID & 2);
    output.positionCS = float4(triangleUV * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
    // Vulkan 的 swapchain pretransform 也属于原全屏顶点接口的一部分。
    // 常量由 Unity 内置 HLSLSupport 声明；旋转方向必须与 Core/API/Vulkan 一致。
#if defined(UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION)
    switch (UNITY_DISPLAY_ORIENTATION_PRETRANSFORM)
    {
        default: break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_90:
            output.positionCS.xy = float2(output.positionCS.y, -output.positionCS.x); break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_180:
            output.positionCS.xy = -output.positionCS.xy; break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_270:
            output.positionCS.xy = float2(-output.positionCS.y, output.positionCS.x); break;
    }
#endif
    // 与原 Core/Blit 保持一致：此通用 Blitter 的 UV 会按 API 方向调整。
    // 这不是 MajFinalCopy；后者的渲染目标方向已由相机处理，不能套用这里的翻转。
#if UNITY_UV_STARTS_AT_TOP
    triangleUV.y = 1.0 - triangleUV.y;
#endif
    output.texcoord = triangleUV * _BlitScaleBias.xy + _BlitScaleBias.zw;
    return output;
}
#endif // MAJ_BLIT_INCLUDED
