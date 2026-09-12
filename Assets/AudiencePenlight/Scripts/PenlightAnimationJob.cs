using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    /// <summary>
    /// 全ペンライトの振り位置・回転行列を並列計算するBurst Job。
    /// GameObjectを一本ずつ動かさず、描画用Matrix4x4だけを生成する。
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    struct PenlightAnimationJob : IJobParallelFor
    {
        // RebuildAudience時に確定し、毎フレーム読み取る個体データ。
        [ReadOnly] public NativeArray<float3> shoulderPositions;
        [ReadOnly] public NativeArray<quaternion> baseRotations;
        [ReadOnly] public NativeArray<float> groupTimingOffsets;
        [ReadOnly] public NativeArray<uint> seeds;

        // 現在フレームの共通アニメーション設定。
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

            // 全員が完全に同時に振らないよう、Seedから個別の時間差を作る。
            var individualOffset = math.lerp(
                -timingOffsetSeconds,
                timingOffsetSeconds,
                Random01(seed, 0x71a34b5du));

            // BPMと一往復に使用する拍数から、1秒あたりの振り回数を求める。
            var cyclesPerSecond = bpm / 60.0f / math.max(0.25f, beatsPerSwing);
            var phase = 2.0f * math.PI * cyclesPerSecond *
                        (time + groupTimingOffsets[index] + individualOffset);

            if (rhythmNoiseAmount > 0.0f)
            {
                // 時間差とは別に位相へ滑らかなノイズを加え、機械的な同期感を弱める。
                var noiseCoordinate = Random01(seed, 0x2d9f1b87u) * 2000.0f - 1000.0f;
                phase += noise.snoise(new float2(noiseCoordinate, time * 0.27f))
                         * rhythmNoiseAmount * math.PI;
            }

            var swing = EvaluateRhythm(phase, rhythm);
            var axis = ResolveAxis(seed, direction, customAxis);

            if (directionRandomness > 0.0f)
            {
                // 基本の振り軸へ個体差を混ぜ、同じ方向設定でも少しずつ角度を変える。
                var randomAngle = Random01(seed, 0xa2c79d31u) * 2.0f * math.PI;
                var randomAxis = new float3(math.cos(randomAngle), 0.0f, math.sin(randomAngle));
                axis = math.normalizesafe(
                    axis + randomAxis * directionRandomness,
                    new float3(1.0f, 0.0f, 0.0f));
            }

            // 肩を原点とし、「振り回転 → 腕の長さ分の移動」の順で行列を合成する。
            // 完成した行列はGraphics.RenderMeshInstancedへそのまま渡される。
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
            // 同じ位相から、設定された波形に応じて-1～1の振り量を作る。
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
            // ペンライトのローカルY軸を、どの水平軸の周りに回すか決定する。
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
            // saltを用途ごとに変えることで、同じSeedから独立した決定的乱数を得る。
            var value = math.hash(new uint2(seed, salt));
            return (value & 0x00ffffffu) / 16777215.0f;
        }
    }
}
