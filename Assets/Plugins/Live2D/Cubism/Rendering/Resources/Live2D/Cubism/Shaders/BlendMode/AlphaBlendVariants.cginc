/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

#ifndef CUBISM_ALPHA_BLEND_INCLUDED
#define CUBISM_ALPHA_BLEND_INCLUDED


inline float4 AlphaBlendRGBA(float3 C, float3 Cs, float3 Cd, float3 p)
{
    float4 res = float4(1.0, 1.0, 1.0, 1.0);
    res.rgb = C * p.x + Cs * p.y + Cd * p.z;
    res.a = p.x + p.y + p.z;

    return res;
}

#if defined(ALPHA_BLEND_OVER) // OVER
inline float4 AlphaBlend(float3 C, float3 Cs, float As, float3 Cd, float Ad) {
    float3 P = float3(As * Ad, As * (1.0 - Ad), Ad * (1.0 - As));
    return AlphaBlendRGBA(C, Cs, Cd, P);
}

#elif defined(ALPHA_BLEND_ATOP) // ATOP
inline float4 AlphaBlend(float3 C, float3 Cs, float As, float3 Cd, float Ad) {
    float3 P = float3(As * Ad, 0, Ad * (1.0 - As));
    return AlphaBlendRGBA(C, Cs, Cd, P);
}

#elif defined(ALPHA_BLEND_OUT) // OUT
inline float4 AlphaBlend(float3 C, float3 Cs, float As, float3 Cd, float Ad) {
    float3 P = float3(0, 0, Ad * (1.0 - As));
    return AlphaBlendRGBA(C, Cs, Cd, P);
}

#elif defined(ALPHA_BLEND_CONJOINT) // CONJOINT
inline float4 AlphaBlend(float3 C, float3 Cs, float As, float3 Cd, float Ad) {
    float3 P = float3(min(As, Ad), max(As - Ad, 0.0), max(Ad - As, 0.0));
    return AlphaBlendRGBA(C, Cs, Cd, P);
}

#elif defined(ALPHA_BLEND_DISJOINT) // DISJOINT
inline float4 AlphaBlend(float3 C, float3 Cs, float As, float3 Cd, float Ad) {
    float3 P = float3(max(As + Ad - 1.0, 0.0), min(As, 1.0 - Ad), min(Ad, 1.0 - As));
    return AlphaBlendRGBA(C, Cs, Cd, P);
}
#else

inline float4 AlphaBlend(float3 C, float3 Cs, float As, float3 Cd, float Ad)
{
    float4 ret = float4(1.0, 1.0, 1.0, 1.0);
    ret.xyz = float3(1.0, 0.0, 1.0);
    ret.z = As;
    return ret;
}

#endif

#define ALPHA_BLEND(C, Cs, As, Cd, Ad) AlphaBlend(C, Cs, As, Cd, Ad)
#endif
