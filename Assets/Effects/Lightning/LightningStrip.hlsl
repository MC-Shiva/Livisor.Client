#ifndef LIVISOR_LIGHTNING_STRIP_INCLUDED
#define LIVISOR_LIGHTNING_STRIP_INCLUDED
float LightningHash(float cell, float salt, float seed)
{
    float h = frac(cell * 0.1031 + salt * 0.11369 + seed * 0.0137);
    h *= h + 19.19;
    return frac(h * (h + 7.7));
}
float LightningNoise(float t, float salt, float seed)
{
    float c = floor(t);
    return lerp(LightningHash(c, salt, seed), LightningHash(c + 1, salt, seed), frac(t)) * 2 - 1;
}
float LightningFold(float angle)
{
    float c = angle / 1.04719755;
    return lerp(sin(floor(c) * 1.04719755), sin((floor(c) + 1) * 1.04719755), frac(c));
}
float3 LightningPillar(float t, float pillar, float3 top, float3 ground,
    float radius, float angularity, float entanglement, float tick, float seed)
{
    float3 axis = ground - top;
    float boltLength = length(axis);
    float3 direction = boltLength > 0.0001 ? axis / boltLength : float3(0,-1,0);
    float3 right = normalize(cross(direction, abs(direction.y) > 0.9 ? float3(0,0,1) : float3(0,1,0)));
    float3 forward = cross(direction, right);
    float spread = clamp(boltLength / 12, 0.15, 1.5);
    float envelope = sin(t * 3.14159265);
    float x = LightningNoise(t * 9, 1, seed) * 0.5 + LightningNoise(t * 23, 2, seed) * angularity * 0.22;
    float z = LightningNoise(t * 7, 3, seed) * 0.28 + LightningNoise(t * 19, 4, seed) * angularity * 0.13;
    float3 spine = lerp(top, ground, t) + (right * x + forward * z) * envelope * spread;
    float phase = pillar * 1.57079633 + t * (8 + entanglement * 8) + LightningNoise(t * 7, 110 + pillar, seed) * 0.8;
    float knots = 0.22 + abs(LightningNoise(t * 5, 103, seed)) * 0.78;
    float r = radius * envelope * knots * spread;
    float3 weave = right * LightningFold(phase) + forward * LightningFold(phase + 1.57079633);
    float3 loose = right * LightningNoise(t * 11, 43 + pillar, seed) + forward * LightningNoise(t * 9, 53 + pillar, seed);
    float3 crack = right * LightningNoise(t * 29, 130 + pillar + tick * 5, seed)
        + forward * LightningNoise(t * 31, 170 + pillar + tick * 5, seed);
    return spine + lerp(loose, weave, entanglement) * r + crack * (r * angularity * 0.12);
}

// One fixed burst: 10 strips x 49 points. Four pillars and six connecting discharges.
void LightningStripUpdate(inout VFXAttributes attributes, float3 Top, float3 Ground,
    float StrikeTime, float Descent, float Hold, float Erase, float Width, float BranchWidth,
    float BundleRadius, float Angularity, float Entanglement, float ArcSpeed, float Seed, float BeamMode)
{
    float strip = attributes.stripIndex;
    float u = attributes.particleIndexInStrip / 48.0;
    float age = StrikeTime - Descent;
    float head = saturate(StrikeTime / max(Descent, 0.01));
    float tail = saturate((age - Hold) / max(Erase, 0.01));
    float tick = floor(StrikeTime * min(ArcSpeed, 24.0));
    float start = 0, end = 1;
    if (strip >= 4)
    {
        float b = strip - 4;
        start = 0.12 + fmod(b, 3) * 0.26 + floor(b / 3) * 0.025;
        end = min(1.0, start + 0.1875);
    }
    float lo = max(start, tail), hi = min(end, head);
    float t = clamp(lerp(start, end, u), lo, max(lo, hi));
    float pulse = age < 0 ? 0.65 : 0.48 + 0.65 * exp(-age * 32)
        + 0.55 * exp(-abs(age - 0.09) * 90) + 0.3 * exp(-abs(age - 0.17) * 90);
    float visible = hi > lo ? 1.0 : 0.0;
    float thickness = Width * (0.44 + LightningHash(strip, 101, Seed) * 0.16);
    float energy = 0.64 + LightningHash(strip, 102, Seed) * 0.2;
    float3 position;
    if (strip < 4)
    {
        position = LightningPillar(t, strip, Top, Ground, BundleRadius, Angularity, Entanglement, tick, Seed);
        thickness *= 0.85 + sin(t * 3.14159265) * 0.15;
    }
    else
    {
        float b = strip - 4;
        float v = saturate((t - start) / (end - start));
        float3 a = LightningPillar(t, fmod(b, 4), Top, Ground, BundleRadius, Angularity, Entanglement, tick, Seed);
        float3 c = LightningPillar(t, fmod(b + 1, 4), Top, Ground, BundleRadius, Angularity, Entanglement, tick, Seed);
        float3 axis = Ground - Top;
        float3 dir = dot(axis, axis) > 0.0001 ? normalize(axis) : float3(0,-1,0);
        float3 right = normalize(cross(dir, abs(dir.y) > 0.9 ? float3(0,0,1) : float3(0,1,0)));
        float3 forward = cross(dir, right);
        float phase = b * 2.39996 + v * Entanglement * 5;
        float r = sin(v * 3.14159265) * (0.3 + LightningHash(b, 66, Seed) * 0.5) * clamp(length(axis) / 12, 0.15, 1.5);
        position = lerp(a, c, v) + (right * LightningFold(phase) + forward * LightningFold(phase + 1.57079633)) * r;
        thickness = BranchWidth * 2.7 * lerp(0.85, 0.06, v * v);
        energy = 0.55;
    }
    if (BeamMode > 0.5 && BeamMode < 1.5) visible *= strip == 0 ? 1 : 0;
    if ((BeamMode > 0.5 && BeamMode < 1.5 && strip == 0) || (BeamMode > 1.5 && strip == 9))
    {
        position = lerp(Top, Ground, clamp(u, tail, max(tail, head)));
        thickness = Width * 2.4;
        energy = 0.55;
        visible = head > tail ? 1.0 : 0.0;
    }
    attributes.position = position;
    attributes.size = thickness * visible;
    attributes.alpha = visible * pulse * (1 - tail) * energy;
}
#endif
