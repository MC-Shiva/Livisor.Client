using System;
using UnityEngine;

namespace Livisor.Live.Penlights
{
    /// <summary>観客席を直線状または円弧状のどちらで並べるか。</summary>
    public enum PenlightLayoutMode
    {
        Grid,
        Arc
    }

    /// <summary>ペンライトを振る基準方向。RandomPerStickは一本ごとに方向を変える。</summary>
    public enum PenlightSwingDirection
    {
        ForwardBackward,
        LeftRight,
        DiagonalLeft,
        DiagonalRight,
        Custom,
        RandomPerStick
    }

    /// <summary>位相から振り角度を作るときに使用するリズム波形。</summary>
    public enum PenlightRhythm
    {
        Smooth,
        Sharp,
        Triangle,
        Alternating
    }

    /// <summary>ペンライトの色を決定・更新する方式。</summary>
    public enum PenlightColorMode
    {
        Fixed,
        RandomPalette,
        AudioReactive
    }

    /// <summary>音楽連動色で参照する音量または周波数帯。</summary>
    public enum PenlightAudioInput
    {
        OverallVolume,
        Bass,
        LowMid,
        HighMid,
        Treble
    }

    /// <summary>Audio Reactiveモード専用の色・追従速度・輝度設定。</summary>
    [Serializable]
    public struct PenlightAudioReactiveSettings
    {
        // 色変化の入力として利用する周波数帯。
        public PenlightAudioInput input;

        // 入力レベル0と1に対応するHDR色。この2色間を補間する。
        [ColorUsage(true, true)]
        public Color lowLevelColor;

        [ColorUsage(true, true)]
        public Color highLevelColor;

        // 音声解析値へ掛ける感度。最終的な入力値は0～1へ制限される。
        [Min(0.0f)]
        public float gain;

        // 音量上昇時（Attack）と下降時（Release）の追従速度。
        [Min(0.0f)]
        public float attackSpeed;

        [Min(0.0f)]
        public float releaseSpeed;

        // 同じ音量でも全員が完全に同色にならないよう、Seed由来の差を加える量。
        [Range(0.0f, 1.0f)]
        public float perStickColorSpread;

        // 共通Emission Intensityへ掛ける、無音時と最大入力時の倍率。
        [Min(0.0f)]
        public float minimumEmissionMultiplier;

        [Min(0.0f)]
        public float maximumEmissionMultiplier;

        // CPU側の色配列をGPUのGraphicsBufferへ転送する頻度。
        [Range(1.0f, 60.0f)]
        public float colorUpdateRateHz;

        /// <summary>新規コンポーネント追加時に使用する推奨初期値を返す。</summary>
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

        /// <summary>Inspectorや外部コードから設定された値を安全な範囲へ補正する。</summary>
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

    /// <summary>固定色、ランダム色、音楽連動色を含む外観設定。</summary>
    [Serializable]
    public struct PenlightAppearanceSettings
    {
        // Fixed / RandomPalette / AudioReactiveの切り替え。
        public PenlightColorMode colorMode;

        // Fixedモードで全インスタンスに使用するHDR色。
        [ColorUsage(true, true)]
        public Color baseColor;

        // RandomPaletteモードでSeedを使って選択する候補色。
        public Color[] randomColorPalette;

        // AudioReactiveモードでのみ使用する詳細設定。
        public PenlightAudioReactiveSettings audioReactive;

        // シェーダーへ渡す全モード共通の基準発光強度。
        [Min(0.0f)]
        public float emissionIntensity;

        // 色、振り方向、タイミング差を再現可能にする共通Seed。
        public uint randomSeed;

        /// <summary>従来のランダムパレット表示を維持した推奨初期値を返す。</summary>
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

        /// <summary>外観設定を安全な範囲へ補正する。</summary>
        public void Clamp()
        {
            emissionIntensity = Mathf.Max(0.0f, emissionIntensity);
            audioReactive.Clamp();
            if (randomSeed == 0u) randomSeed = 1u;
        }
    }

    /// <summary>BPM、振り角度、方向、個体差などのアニメーション設定。</summary>
    [Serializable]
    public struct PenlightMotionSettings
    {
        // 基本の振り方向。Custom選択時だけcustomAxisを使用する。
        public PenlightSwingDirection direction;
        public Vector3 customAxis;

        // 中心から左右それぞれへ振る最大角度。
        [Range(0.0f, 90.0f)]
        public float swingAngleDegrees;

        // 基本方向へ一本ごとのランダムな軸を混ぜる割合。
        [Range(0.0f, 1.0f)]
        public float directionRandomness;

        // 肩位置からペンライトまでの仮想的な腕の長さ。
        [Min(0.0f)]
        public float armLength;

        // 曲のテンポと、一往復の振りに使用する拍数。
        [Min(1.0f)]
        public float bpm;

        [Min(0.25f)]
        public float beatsPerSwing;

        public PenlightRhythm rhythm;

        // 個体Seedから作る振りタイミング差の最大秒数。
        [Min(0.0f)]
        public float timingOffsetSeconds;

        // 位相へ加える滑らかなノイズ量。
        [Range(0.0f, 1.0f)]
        public float rhythmNoiseAmount;

        /// <summary>自然なばらつきを含む推奨初期値を返す。</summary>
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

        /// <summary>振り設定を安全な範囲へ補正する。</summary>
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

    /// <summary>観客席のブロック、座席、間隔、円弧形状を表す配置設定。</summary>
    [Serializable]
    public struct PenlightLayoutSettings
    {
        // ブロック数と、1ブロック内の座席数。xが横、yが奥行き。
        public PenlightLayoutMode layoutMode;
        public Vector2Int blockCount;
        public Vector2Int seatPerBlock;

        // 隣り合う座席の間隔と、ブロック間の通路幅。
        public Vector2 seatPitch;
        public Vector2 aisleWidth;

        // 後方へ一列進むごとの高さ増分。
        [Min(0.0f)]
        public float rowHeight;

        // Zone原点から見た、最前列の肩の高さ。
        [Min(0.0f)]
        public float shoulderHeight;

        // Arcモードの最前列半径と、観客席全体が占める角度。
        [Min(0.1f)]
        public float arcRadius;

        [Range(1.0f, 360.0f)]
        public float arcAngle;

        // HMD周囲にペンライトを置かないためのXZ平面上の半径。
        [Min(0.0f)]
        public float viewerExclusionRadius;

        /// <summary>円弧状の観客席を生成する推奨初期値を返す。</summary>
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

        /// <summary>0個生成やゼロ間隔を避けるため、配置設定を安全な範囲へ補正する。</summary>
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
