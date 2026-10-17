// Local Cubism bridge to MRP's unlit shader API. Shader names, material properties,
// blend math, mask channel packing and Cubism's own license remain unchanged.
#ifndef CUBISM_MAJ_CORE_INCLUDED
#define CUBISM_MAJ_CORE_INCLUDED

// This entry point also works with URP: it consumes Unity's standard matrices and
// per-draw inputs, without importing URP lighting, RenderGraph or package headers.
#include "Assets/Rendering/MajRenderPipeline/Library/MajCore.hlsl"

// Adapted from Unity Graphics 17.3 ShaderVariablesFunctions.deprecated.hlsl.
// Copyright (c) Unity Technologies ApS. Unity Companion License; see
// Assets/Rendering/MajRenderPipeline/Library/LICENSE-Unity.md and SOURCE.md.
// Keep the original projected-screen UV convention used by Cubism BlendImage.
// _ProjectionParams.x already describes the active projection flip; adding a
// graphicsUVStartsAtTop flip here would change background sampling on render RTs.
float4 CubismComputeScreenPos(float4 positionCS)
{
    float4 screenPos = positionCS * 0.5f;
    screenPos.xy = float2(screenPos.x, screenPos.y * _ProjectionParams.x) + screenPos.w;
    screenPos.zw = positionCS.zw;
    return screenPos;
}

#endif // CUBISM_MAJ_CORE_INCLUDED
