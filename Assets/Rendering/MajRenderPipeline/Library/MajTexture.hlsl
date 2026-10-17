// Adapted from Unity Graphics 17.3.0. Copyright (c) Unity Technologies ApS.
// Unity Companion License; see LICENSE-Unity.md and SOURCE.md in this folder.
#ifndef MAJ_TEXTURE_INCLUDED
#define MAJ_TEXTURE_INCLUDED

// 只使用 Unity Editor 自带的跨平台声明，不导入任何 SRP 包的 Common / Lighting 库。
#include "HLSLSupport.cginc"
#include "MajMipSampling.hlsl"

// SRP Batcher 在 GLES / Vulkan 同样需要真实常量缓冲，不能沿用旧 Built-in 的 GLES 空 CBUFFER 宏。
#undef CBUFFER_START
#undef CBUFFER_END
#define CBUFFER_START(name) cbuffer name {
#define CBUFFER_END };

#ifndef real

// The including shader should define whether half
// precision is suitable for its needs.  The shader
// API (for now) can indicate whether half is possible.
//
// We do not define this on Switch2 currently, this would cause function DV_SmithJointGGX (that refers to value REAL_MIN)
// to return incorrect values, which then would cause specular highlights to look extremely bright and incorrect.
#if defined(SHADER_API_MOBILE) || defined(SHADER_API_SWITCH) || defined(UNITY_UNIFIED_SHADER_PRECISION_MODEL)
#define HAS_HALF 1
#else
#define HAS_HALF 0
#endif

#ifndef PREFER_HALF
#define PREFER_HALF 1
#endif

#if HAS_HALF && PREFER_HALF
#define REAL_IS_HALF 1
#else
#define REAL_IS_HALF 0
#endif // Do we have half?

#if REAL_IS_HALF
#define HALF_IS_FLOAT 0
#define half min16float
#define half2 min16float2
#define half3 min16float3
#define half4 min16float4
#define half2x2 min16float2x2
#define half2x3 min16float2x3
#define half3x2 min16float3x2
#define half3x3 min16float3x3
#define half3x4 min16float3x4
#define half4x3 min16float4x3
#define half4x4 min16float4x4
#else
#define HALF_IS_FLOAT (!defined(UNITY_DEVICE_SUPPORTS_NATIVE_16BIT))
#endif

#if REAL_IS_HALF
#define real half
#define real2 half2
#define real3 half3
#define real4 half4

#define real2x2 half2x2
#define real2x3 half2x3
#define real2x4 half2x4
#define real3x2 half3x2
#define real3x3 half3x3
#define real3x4 half3x4
#define real4x3 half4x3
#define real4x4 half4x4

#define REAL_MIN HALF_MIN
#define REAL_MAX HALF_MAX
#define REAL_EPS HALF_EPS

#else

#define real float
#define real2 float2
#define real3 float3
#define real4 float4

#define real2x2 float2x2
#define real2x3 float2x3
#define real2x4 float2x4
#define real3x2 float3x2
#define real3x3 float3x3
#define real3x4 float3x4
#define real4x3 float4x3
#define real4x4 float4x4

#define REAL_MIN FLT_MIN
#define REAL_MAX FLT_MAX
#define REAL_EPS FLT_EPS

#endif // REAL_IS_HALF

#endif // #ifndef real


// 当前 MRP 的平台边界是现代图形 API（Windows D3D / Android GLES3、Vulkan 等），不支持 GLES2。
#if defined(SHADER_API_GLES)
    #error MRP shaders require OpenGL ES 3 or a newer graphics API.
#endif

// 现代 API 明确使用分离的纹理 / sampler 声明，避免内置兼容宏在 Vulkan + DXC 下回退到 legacy sampler2D。
#define TEXTURE2D(textureName) Texture2D textureName
#define TEXTURE2D_ARRAY(textureName) Texture2DArray textureName
#define SAMPLER(samplerName) SamplerState samplerName
#define SAMPLE_TEXTURE2D(textureName, samplerName, uv) textureName.SampleBias(samplerName, uv, (_GlobalMipBias.x + _MajTextureMipBias))
#define SAMPLE_TEXTURE2D_LOD(textureName, samplerName, uv, lod) textureName.SampleLevel(samplerName, uv, lod)

// 原 Core 的 SAMPLE_TEXTURE2D 会加 RenderScale 对应 mip bias，迁移后继续保持该采样语义。
// 图像采样额外加纹理质量偏移；legacy helper 仅加质量偏移，不改变旧 RenderScale 语义。
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    #define TEXTURE2D_X(textureName) TEXTURE2D_ARRAY(textureName)
    #define SAMPLE_TEXTURE2D_X(textureName, samplerName, uv) textureName.SampleBias(samplerName, float3(uv, unity_StereoEyeIndex), (_GlobalMipBias.x + _MajTextureMipBias))
    #define SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, uv, lod) textureName.SampleLevel(samplerName, float3(uv, unity_StereoEyeIndex), lod)
#else
    #define TEXTURE2D_X(textureName) TEXTURE2D(textureName)
    #define SAMPLE_TEXTURE2D_X(textureName, samplerName, uv) SAMPLE_TEXTURE2D(textureName, samplerName, uv)
    #define SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, uv, lod) SAMPLE_TEXTURE2D_LOD(textureName, samplerName, uv, lod)
#endif

#ifndef TRANSFORM_TEX
    #define TRANSFORM_TEX(uv, name) ((uv).xy * name##_ST.xy + name##_ST.zw)
#endif

#endif // MAJ_TEXTURE_INCLUDED
