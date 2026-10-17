// Adapted from Unity Graphics 17.3.0. Copyright (c) Unity Technologies ApS.
// Unity Companion License; see LICENSE-Unity.md and SOURCE.md in this folder.
#ifndef MAJ_CORE_INCLUDED
#define MAJ_CORE_INCLUDED

// MRP 的无光照 Shader 入口。这里只提供现有 Sprite / UI 需要的类型、纹理、矩阵和实例化。
// 不提供 Lit、灯光、阴影、雾、DBuffer、Rendering Layers MRT 或 URP 的 renderer 功能。
#include "MajTexture.hlsl"
#include "MajUnityInput.hlsl"
// 顺序很重要：先声明 UnityPerDraw，再由实例化库重定向 UNITY_MATRIX_M / I_M。
#include "MajInstancing.hlsl"

float4x4 GetObjectToWorldMatrix()
{
    return UNITY_MATRIX_M;
}

float4x4 GetWorldToObjectMatrix()
{
    return UNITY_MATRIX_I_M;
}

float3 TransformObjectToWorld(float3 positionOS)
{
    return mul(GetObjectToWorldMatrix(), float4(positionOS, 1.0)).xyz;
}

float4 TransformWorldToHClip(float3 positionWS)
{
    return mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
}

float4 TransformObjectToHClip(float3 positionOS)
{
    // 分成两次乘法以保持原 SpaceTransforms 的计算顺序，也确保实例化后使用对应对象的矩阵。
    return mul(UNITY_MATRIX_VP, mul(GetObjectToWorldMatrix(), float4(positionOS, 1.0)));
}

#endif // MAJ_CORE_INCLUDED
