#ifndef MAJ_GLASS_SURFACE_LIGHTING_INCLUDED
#define MAJ_GLASS_SURFACE_LIGHTING_INCLUDED

// Derive the tinted material's volume from authored fill, never the captured background.
// Uniform UI fills evaluate this per vertex using their existing artwork sample.
half MajGlassMaterialDepth(half3 baseColor)
{
    #ifdef UNITY_COLORSPACE_GAMMA
    baseColor = GammaToLinearSpace(baseColor);
    #endif
    half luminance = dot(baseColor, half3(0.2126h, 0.7152h, 0.0722h));
    return 1 - smoothstep(0.55h, 0.90h, luminance);
}

// Combine the low-frequency face gradient and white reflections before foreground ink.
// The broad face depth is continuous source geometry, independent of distance derivatives.
// Only the narrow contour reflection uses optical distance and its pixel footprint.
half3 MajApplyGlassSurfaceLighting(half3 surface, float insideDistance, float2 distanceGradient,
    float normalizedDepth, float normalizedHeight, float reflectionWidth,
    half materialDepth, half strength, bool isPanel, bool isPeripheralLens)
{
    half height = clamp(normalizedHeight, -1, 1);
    half shade;
    half reflection;
    half contourReflection = 0.28h + 0.28h * height * height;

    UNITY_BRANCH
    if (isPeripheralLens)
    {
        // Use the button's own circle, not the screen crop, to shape the lens.
        // Interior absorption separates the soft white shoulder even on a flat light capture.
        // Compress only the color bands. The refraction tail still uses the original depth.
        half colorDepth = normalizedDepth * 2;
        half bodyDepth = smoothstep(0.035h, 0.80h, colorDepth);
        half rimShoulder = 1 - smoothstep(0.035h, 0.40h, colorDepth);
        shade = lerp(0.02h, 0.16h + 0.045h * materialDepth, bodyDepth);
        reflection = (0.26h + 0.14h * materialDepth) * rimShoulder;
        contourReflection = 0.56h;
    }
    else if (isPanel)
    {
        // Each face transition occupies ten percent of the full body height. Keep the
        // central eighty percent level, rather than making a large plate look like a lens.
        half upperEdge = smoothstep(0.8h, 1.0h, height);
        half lowerEdge = smoothstep(0.8h, 1.0h, -height);
        half edge = 1 - smoothstep(0, 0.2, normalizedDepth);
        shade = 0.015h + 0.015h * materialDepth + 0.035h * materialDepth * lowerEdge;
        reflection = (0.070h + 0.100h * materialDepth) * upperEdge + 0.045h * lowerEdge
            + 0.040h * edge;
    }
    else
    {
        // Small controls and cover rims retain the continuous vertical face treatment.
        half upper = max(height, 0);
        half lower = max(-height, 0);
        half center = 1 - height * height;
        shade = 0.012h + (0.030h + 0.025h * materialDepth) * center
            + 0.105h * materialDepth * lower;
        half faceReflection = (0.050h + 0.140h * materialDepth) * upper * upper
            + 0.025h * lower * lower;
        half broad = 1 - smoothstep(0, 0.45, normalizedDepth);
        reflection = faceReflection + broad * (0.120h + 0.080h * materialDepth)
            * (0.25h + 0.75h * height * height);
    }

    // Four subpixel footprints filter a soft cubic glint without exp2 or a one-pixel width floor.
    float fineWidth = reflectionWidth * 1.15;
    float footprintRadius = 0.25 * (abs(distanceGradient.x) + abs(distanceGradient.y));
    UNITY_BRANCH
    if (fineWidth > 0 && abs(insideDistance) < fineWidth + footprintRadius)
    {
        float2 footprint = 0.25 * float2(distanceGradient.x + distanceGradient.y,
            distanceGradient.x - distanceGradient.y);
        float4 sampleDistance = insideDistance + float4(footprint.x, -footprint.x, footprint.y, -footprint.y);
        float4 profile = 1 - smoothstep(0, 1, abs(sampleDistance) / fineWidth);
        reflection += dot(profile, float4(0.25, 0.25, 0.25, 0.25)) * contourReflection;
    }

    surface *= 1 - saturate(shade * strength);
    return surface + saturate(1 - surface) * saturate(reflection * strength);
}

#endif
