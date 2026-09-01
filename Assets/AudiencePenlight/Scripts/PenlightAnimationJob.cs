using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    struct PenlightAnimationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> shoulderPositions;
        [ReadOnly] public NativeArray<quaternion> baseRotations;
        [ReadOnly] public NativeArray<float> groupTimingOffsets;
        [ReadOnly] public NativeArray<uint> seeds;

        public float time;
        public float bpm;
        public float beatsPerSwing;
        public float swingAngleRadians;
        public float directionRandomness;
        public float armLength;
        public float timingOffsetSeconds;
        public float rhythmNoiseAmount;
        public float3 customAxis;
        public int direction;
        public int rhythm;

        [WriteOnly]
        public NativeArray<Matrix4x4> matrices;

        public void Execute(int index)
        {
            var seed = seeds[index];
            var individualOffset = math.lerp(
                -timingOffsetSeconds,
                timingOffsetSeconds,
                Random01(seed, 0x71a34b5du));

            var cyclesPerSecond = bpm / 60.0f / math.max(0.25f, beatsPerSwing);
            var phase = 2.0f * math.PI * cyclesPerSecond *
                        (time + groupTimingOffsets[index] + individualOffset);

            if (rhythmNoiseAmount > 0.0f)
            {
                var noiseCoordinate = Random01(seed, 0x2d9f1b87u) * 2000.0f - 1000.0f;
                phase += noise.snoise(new float2(noiseCoordinate, time * 0.27f))
                         * rhythmNoiseAmount * math.PI;
            }

            var swing = EvaluateRhythm(phase, rhythm);
            var axis = ResolveAxis(seed, direction, customAxis);

            if (directionRandomness > 0.0f)
            {
                var randomAngle = Random01(seed, 0xa2c79d31u) * 2.0f * math.PI;
                var randomAxis = new float3(math.cos(randomAngle), 0.0f, math.sin(randomAngle));
                axis = math.normalizesafe(
                    axis + randomAxis * directionRandomness,
                    new float3(1.0f, 0.0f, 0.0f));
            }

            var origin = float4x4.TRS(
                shoulderPositions[index],
                baseRotations[index],
                new float3(1.0f));
            var rotationMatrix = float4x4.AxisAngle(axis, swing * swingAngleRadians);
            var armOffset = float4x4.Translate(new float3(0.0f, armLength, 0.0f));
            matrices[index] = math.mul(origin, math.mul(rotationMatrix, armOffset));
        }

        static float EvaluateRhythm(float phase, int rhythmMode)
        {
            var cosine = math.cos(phase);
            switch ((PenlightRhythm)rhythmMode)
            {
                case PenlightRhythm.Sharp:
                    return math.smoothstep(-0.2f, 0.2f, cosine) * 2.0f - 1.0f;

                case PenlightRhythm.Triangle:
                    return 1.0f - 4.0f * math.abs(math.frac(phase / (2.0f * math.PI)) - 0.5f);

                case PenlightRhythm.Alternating:
                    return math.select(-1.0f, 1.0f, cosine >= 0.0f);

                default:
                    return cosine;
            }
        }

        static float3 ResolveAxis(uint seed, int directionMode, float3 custom)
        {
            switch ((PenlightSwingDirection)directionMode)
            {
                case PenlightSwingDirection.LeftRight:
                    return new float3(0.0f, 0.0f, 1.0f);

                case PenlightSwingDirection.DiagonalLeft:
                    return math.normalize(new float3(1.0f, 0.0f, 1.0f));

                case PenlightSwingDirection.DiagonalRight:
                    return math.normalize(new float3(1.0f, 0.0f, -1.0f));

                case PenlightSwingDirection.Custom:
                    return math.normalizesafe(custom, new float3(1.0f, 0.0f, 0.0f));

                case PenlightSwingDirection.RandomPerStick:
                    var angle = Random01(seed, 0x93e76c41u) * 2.0f * math.PI;
                    return new float3(math.cos(angle), 0.0f, math.sin(angle));

                default:
                    return new float3(1.0f, 0.0f, 0.0f);
            }
        }

        static float Random01(uint seed, uint salt)
        {
            var value = math.hash(new uint2(seed, salt));
            return (value & 0x00ffffffu) / 16777215.0f;
        }
    }
}
