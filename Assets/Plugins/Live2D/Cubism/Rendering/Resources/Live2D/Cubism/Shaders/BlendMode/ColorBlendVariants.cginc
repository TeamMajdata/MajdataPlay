/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

#ifndef CUBISM_COLOR_BLEND_INCLUDED
#define CUBISM_COLOR_BLEND_INCLUDED

#if defined(COLOR_BLEND_NORMAL) // NORMAL
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return Cs;
}
#elif defined(COLOR_BLEND_ADD) // ADD
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return float3(0.0, 0.0, 0.0);
}
#elif defined(COLOR_BLEND_MULTIPLY) // MULTIPLY
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return float3(0.0, 0.0, 0.0);
}
#elif defined(COLOR_BLEND_ADD_R2) // ADD_R2
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return min(Cs + Cd, 1.0);
}
#elif defined(COLOR_BLEND_ADD_GLOW) // ADD_GLOW
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return Cs + Cd;
}
#elif defined(COLOR_BLEND_DARKEN) // DARKEN
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return min(Cs, Cd);
}
#elif defined(COLOR_BLEND_MULTIPLY_R2) // MULTIPLY_R2
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return  Cs * Cd;
}
#elif defined(COLOR_BLEND_COLORBURN) // COLORBURN
float ColorBurn(float Cs, float Cd)
{
    if (abs(Cd - 1.0) < 0.000001)
    {
        return 1.0;
    }
    else if (abs(Cs) < 0.000001)
    {
        return 0.0;
    }

    return 1.0 - min(1.0, (1.0 - Cd) / Cs);
}

inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return float3(
        ColorBurn(Cs.r, Cd.r),
        ColorBurn(Cs.g, Cd.g),
        ColorBurn(Cs.b, Cd.b)
    );
}
#elif defined(COLOR_BLEND_LINEARBURN) // LINEARBURN
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return max(float3(0.0, 0.0, 0.0), Cs + Cd - float3(1.0, 1.0, 1.0));
}
#elif defined(COLOR_BLEND_LIGHTEN) // LIGHTEN
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return max(Cs, Cd);
}
#elif defined(COLOR_BLEND_SCREEN) // SCREEN
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return Cs + Cd - Cs * Cd;
}
#elif defined(COLOR_BLEND_COLORDODGE) // COLORDODGE
float ColorDodge(float Cs, float Cd)
{
    if (Cd <= 0.0)
    {
        return 0.0;
    }
    else if (Cs == 1.0)
    {
        return 1.0;
    }

    return min(1.0, Cd / (1.0 - Cs));
}

float3 ColorBlend(float3 Cs, float3 Cd)
{
    return float3(
        ColorDodge(Cs.r, Cd.r),
        ColorDodge(Cs.g, Cd.g),
        ColorDodge(Cs.b, Cd.b)
    );
}
#elif defined(COLOR_BLEND_OVERLAY) // OVERLAY
float Overlay(float Cs, float Cd)
{
    float mul = 2.0 * Cs * Cd;
    float scr = 1.0 - 2.0 * (1.0 - Cs) * (1.0 - Cd) ;
    return Cd < 0.5 ? mul : scr ;
}

float3 ColorBlend(float3 Cs, float3 Cd)
{
    return float3(
        Overlay(Cs.r, Cd.r),
        Overlay(Cs.g, Cd.g),
        Overlay(Cs.b, Cd.b)
    );
}
#elif defined(COLOR_BLEND_SOFTLIGHT) // SOFTLIGHT
float SoftLight(float Cs, float Cd)
{
    float val1 = Cd - (1.0 - 2.0 * Cs) * Cd * (1.0 - Cd);
    float val2 = Cd + (2.0 * Cs - 1.0) * Cd * ((16.0 * Cd - 12.0) * Cd + 3.0);
    float val3 = Cd + (2.0 * Cs - 1.0) * (sqrt(Cd) - Cd);

    if (Cs <= 0.5)
    {
        return val1;
    }
    else if (Cd <= 0.25)
    {
        return val2;
    }

    return val3;
}

float3 ColorBlend(float3 Cs, float3 Cd)
{
    return float3(
        SoftLight(Cs.r, Cd.r),
        SoftLight(Cs.g, Cd.g),
        SoftLight(Cs.b, Cd.b)
    );
}
#elif defined(COLOR_BLEND_HARDLIGHT) // HARDLIGHT
float HardLight(float Cs, float Cd)
{
    float mul = 2.0 * Cs * Cd;
    float scr = 1.0 - 2.0 * (1.0 - Cs) * (1.0 - Cd);

    if (Cs < 0.5)
    {
        return mul;
    }

    return scr;
}

float3 ColorBlend(float3 Cs, float3 Cd)
{
    return float3(
        HardLight(Cs.r, Cd.r),
        HardLight(Cs.g, Cd.g),
        HardLight(Cs.b, Cd.b)
    );
}
#elif defined(COLOR_BLEND_LINEARLIGHT) // LINEARLIGHT
float LinearLight(float Cs, float Cd)
{
    float burn = max(0.0, 2.0 * Cs + Cd - 1.0);
    float dodge = min(1.0, 2.0 * (Cs - 0.5) + Cd);

    if (Cs < 0.5)
    {
        return burn;
    }

    return dodge;
}

float3 ColorBlend(float3 Cs, float3 Cd)
{
    return float3(
        LinearLight(Cs.r, Cd.r),
        LinearLight(Cs.g, Cd.g),
        LinearLight(Cs.b, Cd.b)
    );
}
#elif defined(COLOR_BLEND_HUE) || defined(COLOR_BLEND_COLOR) // HUE or COLOR
static const float rCoeff = 0.30;
static const float gCoeff = 0.59;
static const float bCoeff = 0.11;

float GetMax(float3 rgbC)
{
    return max(rgbC.r, max(rgbC.g, rgbC.b));
}

float GetMin(float3 rgbC)
{
    return min(rgbC.r, min(rgbC.g, rgbC.b));
}

float GetRange(float3 rgbC)
{
    return max(rgbC.r, max(rgbC.g, rgbC.b)) - min(rgbC.r, min(rgbC.g, rgbC.b));
}

float Saturation(float3 rgbC)
{
    return GetRange(rgbC);
}

float Luma(float3 rgbC)
{
    return rCoeff * rgbC.r + gCoeff * rgbC.g + bCoeff * rgbC.b;
}

float3 ClipColor(float3 rgbC)
{
    float   luma = Luma(rgbC);
    float   maxv = GetMax(rgbC);
    float   minv = GetMin(rgbC);
    float3    outputColor = rgbC;

    outputColor = minv < 0.0 ? luma + (outputColor - luma) * luma / (luma - minv) : outputColor;
    outputColor = maxv > 1.0 ? luma + (outputColor - luma) * (1.0 - luma) / (maxv - luma) : outputColor;

    return outputColor;
}

float3 SetLuma(float3 rgbC, float luma)
{
    return ClipColor(rgbC + (luma - Luma(rgbC)));
}

float3 SetSaturation(float3 rgbC, float saturation)
{
    float maxv = GetMax(rgbC);
    float minv = GetMin(rgbC);
    float medv = rgbC.r + rgbC.g + rgbC.b - maxv - minv;
    float outputMax, outputMed, outputMin;

    outputMax = minv < maxv ? saturation : 0.0;
    outputMed = minv < maxv ? (medv - minv) * saturation / (maxv - minv) : 0.0;
    outputMin = 0.0;

    if(rgbC.r == maxv)
    {
        return rgbC.b < rgbC.g ? float3(outputMax, outputMed, outputMin) : float3(outputMax, outputMin, outputMed);
    }
    else if(rgbC.g == maxv)
    {
        return rgbC.r < rgbC.b ? float3(outputMin, outputMax, outputMed) : float3(outputMed, outputMax, outputMin);
    }
    else
    {
        return rgbC.g < rgbC.r ? float3(outputMed, outputMin, outputMax) : float3(outputMin, outputMed, outputMax);
    }
}
#if defined(COLOR_BLEND_HUE) // HUE
float3 ColorBlend(float3 Cs, float3 Cd)
{
    return SetLuma(SetSaturation(Cs, Saturation(Cd)), Luma(Cd));
}
#else // COLOR
float3 ColorBlend(float3 Cs, float3 Cd)
{
    return SetLuma(Cs, Luma(Cd));
}
#endif

#else // INVALID BLEND MODE
inline float3 ColorBlend(float3 Cs, float3 Cd) {
    return Cs;
}

#endif
#define COLOR_BLEND(Cs, Cd) ColorBlend(Cs, Cd)

#endif
