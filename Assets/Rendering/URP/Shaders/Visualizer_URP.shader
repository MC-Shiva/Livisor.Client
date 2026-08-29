Shader "Livisor/URP/Visualizer"
{
    Properties
    {
        [HideInInspector] _Spectra ("Spectra", Vector) = (0, 0, 0, 0)
        [HideInInspector] _Center ("Center", Vector) = (0, 0, 0, 0)

        [HDR] _Color ("Tint", Color) = (1, 1, 1, 1)
        _AudioResponse ("Audio Response", Range(0, 2)) = 1
        _EmissionClamp ("Emission Clamp", Range(0, 10)) = 3

        [Header(Rings)]
        _RingSrtide ("Ring Stride", Range(0.05, 1)) = 0.2
        _RingThicknessMin ("Ring Thickness Min", Range(0, 1)) = 0.1
        _RingThicknessMax ("Ring Thickness Max", Range(0, 1)) = 0.5
        _RingBaseEmission ("Ring Base Emission", Range(0, 2)) = 0.5
        _RingEmission ("Ring Emission", Range(0, 10)) = 2.5
        _RingSpeedMin ("Ring Speed Min", Range(0, 2)) = 0.2
        _RingSpeedMax ("Ring Speed Max", Range(0, 2)) = 0.5

        [Header(Grid)]
        [HDR] _GridColor ("Grid Color", Color) = (0.2, 0.3, 0.5, 1)
        _GridEmission ("Grid Emission", Range(0, 10)) = 1.2
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
            Name "VisualizerUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Spectra;
                float4 _Center;
                half4 _Color;
                float _AudioResponse;
                float _EmissionClamp;
                float _RingSrtide;
                float _RingThicknessMin;
                float _RingThicknessMax;
                float _RingBaseEmission;
                float _RingEmission;
                float _RingSpeedMin;
                float _RingSpeedMax;
                half4 _GridColor;
                float _GridEmission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float value)
            {
                return frac(sin(value) * 43758.5453);
            }

            float PositiveMod(float value, float divisor)
            {
                return value - divisor * floor(value / divisor);
            }

            float2 PositiveMod(float2 value, float2 divisor)
            {
                return value - divisor * floor(value / divisor);
            }

            float Rings(float3 position, float4 spectra)
            {
                const float pi = 3.14159;
                float2 planePosition = position.xz;
                float stride = max(_RingSrtide, 1e-4);
                float halfStride = stride * 0.5;
                float spectrumAmount = length(spectra);
                float thickness = max(
                    1e-4,
                    1.0 - (_RingThicknessMin + spectrumAmount * (_RingThicknessMax - _RingThicknessMin))
                );

                float distanceFromCenter = abs(length(planePosition) - _Time.y * 0.1);
                float fraction = PositiveMod(distanceFromCenter, stride);
                float cycle = floor(distanceFromCenter / stride);
                float ring = halfStride - abs(fraction - halfStride) - halfStride * thickness;
                ring = saturate(ring / max(halfStride * thickness, 1e-4));

                float randomSpeed = Hash(cycle * cycle);
                float rotation = Hash(cycle) + _Time.y * lerp(_RingSpeedMin, _RingSpeedMax, randomSpeed);
                float angle = atan2(planePosition.y, planePosition.x) / pi * 0.5 + 0.5;
                float arc = 1.0 - PositiveMod(angle + rotation, 1.0);

                return max(arc - 0.7, 0.0) * ring;
            }

            float HexDistance(float2 position, float radius)
            {
                float2 absolutePosition = abs(position);
                return max(
                    absolutePosition.x - radius,
                    max(
                        absolutePosition.x + absolutePosition.y * 0.57735,
                        absolutePosition.y * 1.1547
                    ) - radius
                );
            }

            float HexGrid(float3 position)
            {
                const float scale = 1.2;
                const float2 grid = float2(0.692, 0.4) * scale;
                const float radius = 0.22 * scale;

                float2 position1 = PositiveMod(position.xz, grid) - grid * 0.5;
                float2 position2 = PositiveMod(position.xz + grid * 0.5, grid) - grid * 0.5;
                return min(HexDistance(position1, radius), HexDistance(position2, radius));
            }

            float MovingCircle(float3 position)
            {
                const float outerRadius = 5.0;
                const float innerRadius = 4.0;
                float distanceFromCenter = length(position.xz);
                float cycle = PositiveMod(distanceFromCenter - _Time.y * 1.5, outerRadius);
                return saturate(cycle - innerRadius);
            }

            float3 ClampEmission(float3 emission)
            {
                if (_EmissionClamp <= 0.0)
                    return emission;

                float peak = max(max(emission.r, emission.g), emission.b);
                return peak > _EmissionClamp ? emission * (_EmissionClamp / peak) : emission;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 spectra = saturate(_Spectra * _AudioResponse);
                float3 centeredPosition = input.positionWS - _Center.xyz;

                float ring = Rings(centeredPosition, spectra);
                float gridDistance = HexGrid(centeredPosition);
                float gridAntialias = max(fwidth(gridDistance), 1e-4);
                float grid = smoothstep(-gridAntialias, gridAntialias, gridDistance);
                float circle = MovingCircle(centeredPosition);

                float3 emission = ring * (_RingBaseEmission.xxx + spectra.xyz * _RingEmission);
                emission += _GridColor.rgb * (grid * circle) * _GridEmission;
                emission = ClampEmission(max(emission, 0.0)) * _Color.rgb;

                return half4(emission, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
