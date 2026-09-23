#ifndef WATER_VERTICAL_DEPTH_INCLUDED
#define WATER_VERTICAL_DEPTH_INCLUDED

// Scene01: Shader Graph Scene Depth Linear01 (0 near, ~1 sky / far clip).
// RayThickness: eye-space metres from the water surface to whatever is behind it.
// ViewY: world view-direction Y, used only to recover vertical metres (not the look blob).
void WaterVerticalDepth_float(float RayThickness, float Scene01, float ViewY, out float Out)
{
    float ray = max(0.0, RayThickness);
    float vertical = ray * abs(ViewY);

    // Far clip / sky used to read as kilometres of water and followed the camera.
    // Treat it as a normal river column so distant water stays tinted, not a moving hole.
    float noBed = saturate((Scene01 - 0.96) * 40.0);
    vertical = lerp(vertical, 4.5, noBed);

    // Scale is view-independent: denser colour/alpha than the last pass, still no gaze blob.
    Out = min(vertical * 4.0, 22.0);
}

void WaterVerticalDepth_half(half RayThickness, half Scene01, half ViewY, out half Out)
{
    float o;
    WaterVerticalDepth_float(RayThickness, Scene01, ViewY, o);
    Out = (half)o;
}

#endif
