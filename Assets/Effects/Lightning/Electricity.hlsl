// Shader Graph custom function. UV.x follows the bolt; UV.y spans its width.
#ifndef LIVISOR_ELECTRICITY_INCLUDED
#define LIVISOR_ELECTRICITY_INCLUDED
void Electricity_float(float4 Color, float4 UV, float Time, float Contrast,
    float Speed, float Layer, float4 EdgeColor, out float3 BaseColor, out float Alpha)
{
    // Combined mesh profile: a white-hot core and a cyan halo in ONE transparent pass.
    // UV.z is CPU-authored energy; UV.w selects trunk, filament, or impact blade.
    if (Layer > 2.5)
    {
        float tick = floor(Time * max(Speed, 1.0));
        float cell = floor(UV.x * 37.0);
        // Small hash, no texture sampling, scene depth/color, exp or trig in this path.
        float h = frac(cell * 0.1031 + tick * 0.11369);
        h *= h + 19.19;
        float noise = frac(h * (h + 7.7));
        float d = abs(UV.y * 2.0 - 1.0);
        float aa = max(fwidth(d), 0.012);
        float coreWidth = UV.w < 0.5 ? 0.16 : (UV.w < 1.5 ? 0.28 : 0.12);
        float core = 1.0 - smoothstep(coreWidth - aa, coreWidth + aa * 2.0, d);
        float halo = saturate(1.0 - d);
        halo = halo * halo * (0.65 + halo * 0.35);
        float gate = lerp(1.0, 0.24 + noise * 0.76, saturate(Contrast));
        float energy = max(UV.z, 0.0);
        float blade = step(1.5, UV.w);
        // The core remains continuous. Only fine surrounding currents have deep dark gaps.
        float modulation = UV.w < 0.5 ? lerp(0.8, 1.0, noise) : gate;
        BaseColor = (max(Color.rgb, 0.0) * core * modulation * lerp(1.0, 0.55, blade)
            + max(EdgeColor.rgb, 0.0) * halo * (0.75 + 0.25 * noise) * lerp(1.0, 1.8, blade)) * energy;
        Alpha = 1.0;
        return;
    }
    float tick = floor(Time * max(Speed, 1.0));
    float along = UV.x;
    float cell = floor(along * 47.0);
    float noise = frac(sin(cell * 127.1 + tick * 311.7) * 43758.5453);
    float ripple = sin(along * 163.0 - tick * 2.4) * 0.045;
    float center = 0.5 + (noise - 0.5) * 0.025 + ripple * 0.35;
    float d = abs(UV.y - center) * 2.0;
    float core = 1.0 - smoothstep(0.38, 0.67, d);
    float halo = exp(-d * d * 4.5) * (1.0 - smoothstep(0.65, 1.0, d));
    float lanes = abs(abs(UV.y - 0.5) - (0.27 + ripple + (noise - 0.5) * 0.1));
    float filament = 1.0 - smoothstep(0.008, 0.047, lanes);
    float gate = lerp(1.0, lerp(0.06, 1.0, step(0.42, noise)), saturate(Contrast));
    float wave = pow(saturate(0.5 + 0.5 * sin(along * 44.0 - Time * Speed * 1.9)), 12.0);
    float glowMask = halo * (0.75 + 0.25 * wave) + filament * gate * 0.08;
    // Core remains continuous; the surrounding arcs have deep gaps in brightness.
    float coreMask = core * (0.8 + 0.2 * noise);
    // Thin geometry needs a broad internal profile so distant branches remain readable.
    // Keep a continuous fine thread underneath the electrical flicker.
    float branchMask = (1.0 - smoothstep(0.35, 0.9, d)) * lerp(0.5, 1.0, gate);
    float mask = Layer < 0.5 ? glowMask : (Layer < 1.5 ? coreMask : branchMask);
    mask *= smoothstep(0.0, 0.025, UV.y) * smoothstep(0.0, 0.025, 1.0 - UV.y);
    BaseColor = max(Color.rgb, 0.0) * mask;
    Alpha = 1.0;
}
#endif
