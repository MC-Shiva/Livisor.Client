using System;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    public enum PenlightLayoutMode
    {
        Grid,
        Arc
    }

    public enum PenlightSwingDirection
    {
        ForwardBackward,
        LeftRight,
        DiagonalLeft,
        DiagonalRight,
        Custom,
        RandomPerStick
    }

    public enum PenlightRhythm
    {
        Smooth,
        Sharp,
        Triangle,
        Alternating
    }

    public enum PenlightColorMode
    {
        Fixed,
        RandomPalette,
        AudioReactive
    }

    public enum PenlightAudioInput
    {
        OverallVolume,
        Bass,
        LowMid,
        HighMid,
        Treble
    }

    [Serializable]
    public struct PenlightAudioReactiveSettings
    {
        public PenlightAudioInput input;

        [ColorUsage(true, true)]
        public Color lowLevelColor;

        [ColorUsage(true, true)]
        public Color highLevelColor;

        [Min(0.0f)]
        public float gain;

        [Min(0.0f)]
        public float attackSpeed;

        [Min(0.0f)]
        public float releaseSpeed;

        [Range(0.0f, 1.0f)]
        public float perStickColorSpread;

        [Min(0.0f)]
        public float minimumEmissionMultiplier;

        [Min(0.0f)]
        public float maximumEmissionMultiplier;

        [Range(1.0f, 60.0f)]
        public float colorUpdateRateHz;

        public static PenlightAudioReactiveSettings Default()
        {
            return new PenlightAudioReactiveSettings
            {
                input = PenlightAudioInput.OverallVolume,
                lowLevelColor = new Color(0.02f, 0.08f, 0.35f, 1.0f),
                highLevelColor = new Color(1.0f, 0.12f, 0.75f, 1.0f),
                gain = 1.25f,
                attackSpeed = 8.0f,
                releaseSpeed = 2.5f,
                perStickColorSpread = 0.15f,
                minimumEmissionMultiplier = 0.6f,
                maximumEmissionMultiplier = 1.8f,
                colorUpdateRateHz = 30.0f
            };
        }

        public void Clamp()
        {
            gain = Mathf.Max(0.0f, gain);
            attackSpeed = Mathf.Max(0.0f, attackSpeed);
            releaseSpeed = Mathf.Max(0.0f, releaseSpeed);
            perStickColorSpread = Mathf.Clamp01(perStickColorSpread);
            minimumEmissionMultiplier = Mathf.Max(0.0f, minimumEmissionMultiplier);
            maximumEmissionMultiplier = Mathf.Max(
                minimumEmissionMultiplier,
                maximumEmissionMultiplier);
            colorUpdateRateHz = Mathf.Clamp(colorUpdateRateHz, 1.0f, 60.0f);
        }
    }

    [Serializable]
    public struct PenlightAppearanceSettings
    {
        public PenlightColorMode colorMode;

        [ColorUsage(true, true)]
        public Color baseColor;

        public Color[] randomColorPalette;

        public PenlightAudioReactiveSettings audioReactive;

        [Min(0.0f)]
        public float emissionIntensity;

        public uint randomSeed;

        public static PenlightAppearanceSettings Default()
        {
            return new PenlightAppearanceSettings
            {
                colorMode = PenlightColorMode.RandomPalette,
                baseColor = new Color(0.1f, 1.0f, 0.35f, 1.0f),
                randomColorPalette = new[]
                {
                    new Color(1.0f, 0.12f, 0.18f, 1.0f),
                    new Color(0.1f, 0.45f, 1.0f, 1.0f),
                    new Color(0.15f, 1.0f, 0.35f, 1.0f),
                    new Color(1.0f, 0.75f, 0.08f, 1.0f),
                    new Color(0.95f, 0.15f, 1.0f, 1.0f)
                },
                audioReactive = PenlightAudioReactiveSettings.Default(),
                emissionIntensity = 2.0f,
                randomSeed = 12345u
            };
        }

        public void Clamp()
        {
            emissionIntensity = Mathf.Max(0.0f, emissionIntensity);
            audioReactive.Clamp();
            if (randomSeed == 0u) randomSeed = 1u;
        }
    }

    [Serializable]
    public struct PenlightMotionSettings
    {
        public PenlightSwingDirection direction;
        public Vector3 customAxis;

        [Range(0.0f, 90.0f)]
        public float swingAngleDegrees;

        [Range(0.0f, 1.0f)]
        public float directionRandomness;

        [Min(0.0f)]
        public float armLength;

        [Min(1.0f)]
        public float bpm;

        [Min(0.25f)]
        public float beatsPerSwing;

        public PenlightRhythm rhythm;

        [Min(0.0f)]
        public float timingOffsetSeconds;

        [Range(0.0f, 1.0f)]
        public float rhythmNoiseAmount;

        public static PenlightMotionSettings Default()
        {
            return new PenlightMotionSettings
            {
                direction = PenlightSwingDirection.LeftRight,
                customAxis = Vector3.forward,
                swingAngleDegrees = 55.0f,
                directionRandomness = 0.15f,
                armLength = 0.35f,
                bpm = 120.0f,
                beatsPerSwing = 2.0f,
                rhythm = PenlightRhythm.Smooth,
                timingOffsetSeconds = 0.2f,
                rhythmNoiseAmount = 0.1f
            };
        }

        public void Clamp()
        {
            swingAngleDegrees = Mathf.Clamp(swingAngleDegrees, 0.0f, 90.0f);
            directionRandomness = Mathf.Clamp01(directionRandomness);
            armLength = Mathf.Max(0.0f, armLength);
            bpm = Mathf.Max(1.0f, bpm);
            beatsPerSwing = Mathf.Max(0.25f, beatsPerSwing);
            timingOffsetSeconds = Mathf.Max(0.0f, timingOffsetSeconds);
            rhythmNoiseAmount = Mathf.Clamp01(rhythmNoiseAmount);
        }
    }

    [Serializable]
    public struct PenlightLayoutSettings
    {
        public PenlightLayoutMode layoutMode;
        public Vector2Int blockCount;
        public Vector2Int seatPerBlock;
        public Vector2 seatPitch;
        public Vector2 aisleWidth;

        [Min(0.0f)]
        public float rowHeight;

        [Min(0.0f)]
        public float shoulderHeight;

        [Min(0.1f)]
        public float arcRadius;

        [Range(1.0f, 360.0f)]
        public float arcAngle;

        [Min(0.0f)]
        public float viewerExclusionRadius;

        public static PenlightLayoutSettings Default()
        {
            return new PenlightLayoutSettings
            {
                layoutMode = PenlightLayoutMode.Arc,
                blockCount = new Vector2Int(7, 3),
                seatPerBlock = new Vector2Int(8, 12),
                seatPitch = new Vector2(0.35f, 0.45f),
                aisleWidth = new Vector2(0.65f, 0.8f),
                rowHeight = 0.025f,
                shoulderHeight = 0.95f,
                arcRadius = 4.5f,
                arcAngle = 120.0f,
                viewerExclusionRadius = 1.0f
            };
        }

        public void Clamp()
        {
            blockCount.x = Mathf.Max(1, blockCount.x);
            blockCount.y = Mathf.Max(1, blockCount.y);
            seatPerBlock.x = Mathf.Max(1, seatPerBlock.x);
            seatPerBlock.y = Mathf.Max(1, seatPerBlock.y);
            seatPitch.x = Mathf.Max(0.01f, seatPitch.x);
            seatPitch.y = Mathf.Max(0.01f, seatPitch.y);
            aisleWidth.x = Mathf.Max(0.0f, aisleWidth.x);
            aisleWidth.y = Mathf.Max(0.0f, aisleWidth.y);
            rowHeight = Mathf.Max(0.0f, rowHeight);
            shoulderHeight = Mathf.Max(0.0f, shoulderHeight);
            arcRadius = Mathf.Max(0.1f, arcRadius);
            arcAngle = Mathf.Clamp(arcAngle, 1.0f, 360.0f);
            viewerExclusionRadius = Mathf.Max(0.0f, viewerExclusionRadius);
        }
    }
}
