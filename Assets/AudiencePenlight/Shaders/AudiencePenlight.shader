Shader "Livisor/Audience Penlight"
{
    Properties
    {
        _EmissionIntensity("Emission Intensity", Range(0, 8)) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "AudiencePenlightUnlit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _InstanceColorBuffer;

            CBUFFER_START(UnityPerMaterial)
                float _EmissionIntensity;
                uint _InstanceIDOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float4 color : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                #if UNITY_ANY_INSTANCING_ENABLED
                    uint instanceIndex = unity_InstanceID + _InstanceIDOffset;
                    output.color = _InstanceColorBuffer[instanceIndex];
                #else
                    // Material preview and non-instanced fallback do not have an instance ID
                    // or a bound per-instance color buffer.
                    output.color = float4(1.0, 1.0, 1.0, 1.0);
                #endif

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(input.color.rgb * _EmissionIntensity, input.color.a);
            }
            ENDHLSL
        }
    }
}
