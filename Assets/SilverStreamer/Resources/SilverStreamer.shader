Shader "Livisor/Silver Streamer"
{
    Properties
    {
        _BaseColor("Silver Color", Color) = (.82, .85, .89, 1)
        _Metallic("Metallic", Range(0, 1)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = .94
        _SilverFill("Silver Fill", Range(0, 2)) = .45
        _GlintIntensity("Glint Intensity", Range(0, 6)) = 2.5
        _Bend("Bend", Float) = .045
        _Twist("Twist", Float) = 1.2
        _Width("Width", Float) = .035
        _FlutterSpeed("Flutter speed", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Bend, _Twist, _Width, _FlutterSpeed;
                float _EffectTime;
                half4 _BaseColor;
                half _Metallic, _Smoothness;
                half _SilverFill, _GlintIntensity;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 data : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float age = input.data.z;
                float phase = input.data.w * 6.2831853;
                float clock = _EffectTime * _FlutterSpeed * lerp(.8, 1.2, input.data.w);
                float wave = input.data.y * 6.2831853 + phase + clock;
                float flutter = lerp(.12, 1, smoothstep(0, .2, age));
                float endScale = 1 - smoothstep(.85, 1, age);
                float angle = sin(wave * .7) * _Twist * flutter;
                float across = (input.data.x - .5) * _Width * endScale;
                float3 n = normalize(input.normalOS);
                float3 t = normalize(input.tangentOS.xyz);
                float3 displacement = n * (sin(wave) * _Bend * flutter * endScale + across * sin(angle))
                    + t * (across * (cos(angle) - 1));
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz + displacement);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Derivatives follow the deformed surface, including both sides of the foil.
                float3 normal = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                normal *= dot(normal, view) < 0 ? -1 : 1;
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = normal;
                lighting.viewDirectionWS = view;
                lighting.bakedGI = SampleSH(normal);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                // An artistic studio reflection keeps foil readable on the dark stage,
                // even when no reflection probe or bright sky is available. The bands
                // move with the deformed normal and the eye, rather than flashing on a timer.
                float3 reflected = reflect(-view, normal);
                float broadBand = pow(saturate(1 - abs(dot(reflected,
                    normalize(float3(.35, 1, .2))))), 8);
                float sharpBand = pow(saturate(1 - abs(dot(reflected,
                    normalize(float3(-.65, .3, .7))))), 96);
                float rim = pow(1 - saturate(dot(normal, view)), 4);
                surface.emission = _BaseColor.rgb * (_SilverFill + broadBand * .65)
                    + half3(1, .98, .95) * sharpBand * _GlintIntensity
                    + half3(.75, .85, 1) * rim * .35;
                return UniversalFragmentPBR(lighting, surface);
            }
            ENDHLSL
        }
    }
}
