#ifndef LIVISOR_UNITYCHAN_LIGHTING_INCLUDED
#define LIVISOR_UNITYCHAN_LIGHTING_INCLUDED

// GetMainLight and TransformWorldToShadowCoord are required by the custom
// lighting function below. The include guard in URP prevents duplicate loads
// when the generated Shader Graph pass already includes Lighting.hlsl.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

void UnityChanSkinTunable_float(
    float4 Color,
    UnityTexture2D MainTex,
    UnityTexture2D FalloffSampler,
    UnityTexture2D RimLightSampler,
    float4 ShadowColor,
    float RimLightIntensity,
    float SkinBrightness,
    float4 UV,
    float3 WorldPosition,
    float3 WorldNormal,
    float3 WorldViewDirection,
    out float3 BaseColor,
    out float Alpha)
{
    const float2 controlTextureV = float2(0.0, 0.25);

    float2 mainUV = UV.xy * MainTex.scaleTranslate.xy + MainTex.scaleTranslate.zw;
    float4 diffuse = SAMPLE_TEXTURE2D(MainTex.tex, MainTex.samplerstate, mainUV);

    float3 normalWS = normalize(WorldNormal);
    float3 viewDirectionWS = normalize(WorldViewDirection);

    // Convert the view angle to the same one-dimensional lookup used by
    // the original CharaSkin.cg falloff texture.
    float normalDotView = dot(normalWS, viewDirectionWS);
    float falloffU = clamp(1.0 - abs(normalDotView), 0.02, 0.98);
    float4 falloff = SAMPLE_TEXTURE2D(
        FalloffSampler.tex,
        FalloffSampler.samplerstate,
        float2(falloffU, controlTextureV.y));

    float3 combinedColor = lerp(
        diffuse.rgb,
        falloff.rgb * diffuse.rgb,
        falloff.a);

    float3 mainLightDirection;
    float3 mainLightColor;
    float mainLightShadowAttenuation;

#if defined(SHADERGRAPH_PREVIEW)
    mainLightDirection = normalize(float3(0.5, 0.5, 1.0));
    mainLightColor = 1.0;
    mainLightShadowAttenuation = 1.0;
#else
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPosition);
    Light mainLight = GetMainLight(shadowCoord);
    mainLightDirection = mainLight.direction;
    mainLightColor = mainLight.color;
    mainLightShadowAttenuation = mainLight.shadowAttenuation;
#endif

    // The original skin shader uses the main-light angle only to shape the
    // rim lookup; it does not use Lambert or PBR lighting.
    float rimLightDot = saturate(0.5 * (dot(normalWS, mainLightDirection) + 1.0));
    float rimU = saturate(rimLightDot * falloffU);
    float rim = SAMPLE_TEXTURE2D(
        RimLightSampler.tex,
        RimLightSampler.samplerstate,
        float2(rimU, controlTextureV.y)).r;
    combinedColor += rim * diffuse.rgb * RimLightIntensity;

    // Match CharaSkin.cg: remap the shadow attenuation so that its lower half
    // becomes the material's stylized shadow color.
    float shadowAttenuation = saturate(2.0 * mainLightShadowAttenuation - 1.0);
    float3 shadedColor = lerp(
        ShadowColor.rgb * combinedColor,
        combinedColor,
        shadowAttenuation);

    BaseColor = shadedColor * Color.rgb * mainLightColor * SkinBrightness;
    Alpha = diffuse.a * Color.a;
}

void UnityChanSkinTunable_half(
    half4 Color,
    UnityTexture2D MainTex,
    UnityTexture2D FalloffSampler,
    UnityTexture2D RimLightSampler,
    half4 ShadowColor,
    half RimLightIntensity,
    half SkinBrightness,
    half4 UV,
    half3 WorldPosition,
    half3 WorldNormal,
    half3 WorldViewDirection,
    out half3 BaseColor,
    out half Alpha)
{
    float3 baseColorFloat;
    float alphaFloat;

    UnityChanSkinTunable_float(
        Color,
        MainTex,
        FalloffSampler,
        RimLightSampler,
        ShadowColor,
        RimLightIntensity,
        SkinBrightness,
        UV,
        WorldPosition,
        WorldNormal,
        WorldViewDirection,
        baseColorFloat,
        alphaFloat);

    BaseColor = (half3)baseColorFloat;
    Alpha = (half)alphaFloat;
}

// Compatibility entry points used by the eye and blush graphs. Those graphs
// intentionally retain the original skin-lighting strength; only the opaque
// skin graph exposes the new HMD-oriented tuning controls.
void UnityChanSkin_float(
    float4 Color,
    UnityTexture2D MainTex,
    UnityTexture2D FalloffSampler,
    UnityTexture2D RimLightSampler,
    float4 ShadowColor,
    float4 UV,
    float3 WorldPosition,
    float3 WorldNormal,
    float3 WorldViewDirection,
    out float3 BaseColor,
    out float Alpha)
{
    UnityChanSkinTunable_float(
        Color,
        MainTex,
        FalloffSampler,
        RimLightSampler,
        ShadowColor,
        0.5,
        1.0,
        UV,
        WorldPosition,
        WorldNormal,
        WorldViewDirection,
        BaseColor,
        Alpha);
}

void UnityChanSkin_half(
    half4 Color,
    UnityTexture2D MainTex,
    UnityTexture2D FalloffSampler,
    UnityTexture2D RimLightSampler,
    half4 ShadowColor,
    half4 UV,
    half3 WorldPosition,
    half3 WorldNormal,
    half3 WorldViewDirection,
    out half3 BaseColor,
    out half Alpha)
{
    UnityChanSkinTunable_half(
        Color,
        MainTex,
        FalloffSampler,
        RimLightSampler,
        ShadowColor,
        0.5,
        1.0,
        UV,
        WorldPosition,
        WorldNormal,
        WorldViewDirection,
        BaseColor,
        Alpha);
}

float3 UnityChanOverlay_float(float3 upperColor, float3 lowerColor)
{
    float3 oneMinusLower = 1.0 - lowerColor;
    float3 greaterResult = upperColor * (2.0 * oneMinusLower) + (2.0 * lowerColor - 1.0);
    float3 lowerResult = 2.0 * lowerColor * upperColor;
    return lerp(lowerResult, greaterResult, round(lowerColor));
}

void UnityChanMain_float(
    float4 Color,
    UnityTexture2D MainTex,
    UnityTexture2D FalloffSampler,
    UnityTexture2D RimLightSampler,
    float4 ShadowColor,
    float SpecularPower,
    UnityTexture2D SpecularReflectionSampler,
    UnityTexture2D EnvMapSampler,
    UnityTexture2D NormalMapSampler,
    float4 UV,
    float3 WorldPosition,
    float3 WorldNormal,
    float3 WorldTangent,
    float3 WorldBitangent,
    float3 WorldViewDirection,
    out float3 BaseColor,
    out float Alpha)
{
    const float controlTextureV = 0.25;

    float2 mainUV = UV.xy * MainTex.scaleTranslate.xy + MainTex.scaleTranslate.zw;
    float4 diffuse = SAMPLE_TEXTURE2D(MainTex.tex, MainTex.samplerstate, mainUV);

    // These textures are intentionally sampled as ordinary color textures.
    // The source UnityChan shader manually decodes RGB to [-1, 1], and the
    // existing project textures have not been reimported as Normal Maps.
    float3 normalTS = normalize(
        SAMPLE_TEXTURE2D(NormalMapSampler.tex, NormalMapSampler.samplerstate, mainUV).xyz
        * 2.0 - 1.0);
    float3 normalWS = normalize(
        normalTS.x * normalize(WorldTangent)
        + normalTS.y * normalize(WorldBitangent)
        + normalTS.z * normalize(WorldNormal));
    float3 viewDirectionWS = normalize(WorldViewDirection);

    float normalDotView = dot(normalWS, viewDirectionWS);
    float falloffU = clamp(1.0 - abs(normalDotView), 0.02, 0.98);
    float4 falloff = 0.3 * SAMPLE_TEXTURE2D(
        FalloffSampler.tex,
        FalloffSampler.samplerstate,
        float2(falloffU, controlTextureV));

    float3 shadowTone = diffuse.rgb * diffuse.rgb;
    float3 combinedColor = lerp(diffuse.rgb, shadowTone, falloff.r);
    combinedColor *= 1.0 + falloff.rgb * falloff.a;

    float4 reflectionMask = SAMPLE_TEXTURE2D(
        SpecularReflectionSampler.tex,
        SpecularReflectionSampler.samplerstate,
        mainUV);

    // CharaMain.cg deliberately uses the view vector as its specular-light
    // vector. This is not a PBR highlight and is reproduced as-is.
    float specular = normalDotView < 0.0
        ? 0.0
        : pow(saturate(normalDotView), SpecularPower);
    combinedColor += saturate(specular) * reflectionMask.rgb * diffuse.rgb;

    float3 reflectVector = reflect(-viewDirectionWS, normalWS);
    float2 sphereMapUV = 0.5 * (1.0 + float2(reflectVector.x, reflectVector.z));
    float3 environmentColor = SAMPLE_TEXTURE2D(
        EnvMapSampler.tex,
        EnvMapSampler.samplerstate,
        sphereMapUV).rgb;
    float3 overlayReflection = UnityChanOverlay_float(environmentColor, combinedColor);
    combinedColor = lerp(combinedColor, overlayReflection, reflectionMask.a);

    float3 mainLightDirection;
    float3 mainLightColor;
    float mainLightShadowAttenuation;

#if defined(SHADERGRAPH_PREVIEW)
    mainLightDirection = normalize(float3(0.5, 0.5, 1.0));
    mainLightColor = 1.0;
    mainLightShadowAttenuation = 1.0;
#else
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPosition);
    Light mainLight = GetMainLight(shadowCoord);
    mainLightDirection = mainLight.direction;
    mainLightColor = mainLight.color;
    mainLightShadowAttenuation = mainLight.shadowAttenuation;
#endif

    combinedColor *= Color.rgb * mainLightColor;

    float shadowAttenuation = saturate(2.0 * mainLightShadowAttenuation - 1.0);
    combinedColor = lerp(
        ShadowColor.rgb * combinedColor,
        combinedColor,
        shadowAttenuation);

    // As in the source shader, rim light is added after main-light tinting
    // and shadow coloring.
    float rimLightDot = saturate(0.5 * (dot(normalWS, mainLightDirection) + 1.0));
    float rimU = saturate(rimLightDot * falloffU);
    float rim = SAMPLE_TEXTURE2D(
        RimLightSampler.tex,
        RimLightSampler.samplerstate,
        float2(rimU, controlTextureV)).r;
    combinedColor += rim * diffuse.rgb;

    BaseColor = combinedColor;
    Alpha = diffuse.a * Color.a;
}

void UnityChanMain_half(
    half4 Color,
    UnityTexture2D MainTex,
    UnityTexture2D FalloffSampler,
    UnityTexture2D RimLightSampler,
    half4 ShadowColor,
    half SpecularPower,
    UnityTexture2D SpecularReflectionSampler,
    UnityTexture2D EnvMapSampler,
    UnityTexture2D NormalMapSampler,
    half4 UV,
    half3 WorldPosition,
    half3 WorldNormal,
    half3 WorldTangent,
    half3 WorldBitangent,
    half3 WorldViewDirection,
    out half3 BaseColor,
    out half Alpha)
{
    float3 baseColorFloat;
    float alphaFloat;

    UnityChanMain_float(
        Color,
        MainTex,
        FalloffSampler,
        RimLightSampler,
        ShadowColor,
        SpecularPower,
        SpecularReflectionSampler,
        EnvMapSampler,
        NormalMapSampler,
        UV,
        WorldPosition,
        WorldNormal,
        WorldTangent,
        WorldBitangent,
        WorldViewDirection,
        baseColorFloat,
        alphaFloat);

    BaseColor = (half3)baseColorFloat;
    Alpha = (half)alphaFloat;
}

#endif
