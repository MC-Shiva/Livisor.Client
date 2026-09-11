using System.Collections;
using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// LIQUID DIVE — 現実の空間に波紋が広がり、部屋が水で満たされ、頭まで沈んで深く潜っていく。
    /// 「ダイブ」という語をそのまま画にした、静かで叙情的なパターン。3 つの中で唯一ゆっくり進む。
    ///
    ///   Phase 1 (0.00–0.18) 波紋   … 正面 2m の 1 点から同心円が生まれ、視界全体へ広がる
    ///   Phase 2 (0.18–0.46) 水位上昇 … 下から水面ラインがせり上がる。水面下は青く沈みコースティクスが走る
    ///   Phase 3 (0.46–0.86) 沈降   … 水面が頭を越え、気泡が昇り、深くなるほど暗く青が濃くなる
    ///   Phase 4 (0.86–1.00) 着底   … 下方に光が差し、吸い込まれるように加速して深い藍へ収束
    ///
    /// ■ 水位はワールド座標で持つ
    ///   全天球レイヤーは <see cref="OverlayFollow.PositionOnly"/> で頭に位置追従するため、
    ///   「頭からの相対の高さ」で水面を決めると頭を上下させたときに水面が一緒に動いて嘘になる。
    ///   そこで演出開始時の足元を <see cref="_floorY"/>（ワールド絶対値）として押さえ、
    ///   そこから上昇したワールド Y を _WaterLevelY、今の頭のワールド Y を _HeadY として毎フレーム
    ///   シェーダーへ渡し、シェーダー側でピクセルのワールド Y を復元して比較している。
    ///   結果、しゃがめば水面は視界の上へ、背伸びすれば下へ動く。
    ///
    /// ■ 気泡はシェーダー内の数式で描いている（ParticleSystem を使わない）
    ///   1) 全天球レイヤーを既に 1 パス描いているので、追加のドローコールも CPU シミュレーションも要らない
    ///   2) Single Pass Instanced でも自動的に両目へ正しく出る（近距離ビルボードの左右ズレが起きない）
    ///   3) 「深さに応じて量と速度を変える」といった調整が、シェーダープロパティ 1 本で済む
    ///   粒の密度が足りなければ MRDive_LiquidDive.shader の BubbleLayer の COLS / rowScale を上げる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Livisor/MR Dive/Liquid Dive Transition")]
    public sealed class LiquidDiveTransition : DiveTransitionBase
    {
        // --- フェーズ境界（仕様書の数字をそのまま定数に） ---
        const float RippleEnd = 0.18f;
        const float RiseEnd = 0.46f;
        const float SinkEnd = 0.86f;

        // --- シェーダープロパティ ID ---
        // 毎フレーム触るので PropertyToID をキャッシュする。文字列版の Set を回すと
        // フレームごとに内部のハッシュ引きが走ってモバイルでは無視できない。
        static readonly int WaterLevelYId = Shader.PropertyToID("_WaterLevelY");
        static readonly int HeadYId = Shader.PropertyToID("_HeadY");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int DepthRangeId = Shader.PropertyToID("_DepthRange");
        static readonly int WaterAlphaId = Shader.PropertyToID("_WaterAlpha");
        static readonly int SubmergeId = Shader.PropertyToID("_Submerge");
        static readonly int SurfaceThicknessId = Shader.PropertyToID("_SurfaceThickness");
        static readonly int SurfaceWaveId = Shader.PropertyToID("_SurfaceWave");
        static readonly int SurfaceGlowId = Shader.PropertyToID("_SurfaceGlow");
        static readonly int CausticsId = Shader.PropertyToID("_Caustics");
        static readonly int BubblesId = Shader.PropertyToID("_Bubbles");
        static readonly int StreakId = Shader.PropertyToID("_Streak");
        static readonly int SwayId = Shader.PropertyToID("_Sway");
        static readonly int WobbleId = Shader.PropertyToID("_Wobble");
        static readonly int BottomLightId = Shader.PropertyToID("_BottomLight");
        static readonly int ConvergeId = Shader.PropertyToID("_Converge");
        static readonly int FlowTimeId = Shader.PropertyToID("_FlowTime");
        static readonly int BubbleTimeId = Shader.PropertyToID("_BubbleTime");

        static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        static readonly int SurfaceColorId = Shader.PropertyToID("_SurfaceColor");
        static readonly int BottomLightColorId = Shader.PropertyToID("_BottomLightColor");

        // 波紋シェーダー側。_FlowTime は両方のシェーダーで同じ名前なので FlowTimeId を使い回す。
        static readonly int OriginId = Shader.PropertyToID("_Origin");
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int RingColorId = Shader.PropertyToID("_RingColor");
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int RingFreqId = Shader.PropertyToID("_RingFreq");
        static readonly int RefractId = Shader.PropertyToID("_Refract");

        [Header("尺")]
        [Tooltip("演出全体の秒数。3 つのパターンの中で唯一ゆっくり進む。")]
        [SerializeField] float duration = 3.8f;

        [Header("配色")]
        [Tooltip("浅い水の色。#2FA8D8")]
        [SerializeField] Color shallowColor = new Color(0.1843f, 0.6588f, 0.8471f, 1f);

        [Tooltip("深部の色。#06283C。着地フェードもこの色から明ける。")]
        [SerializeField] Color deepColor = new Color(0.0235f, 0.1569f, 0.2353f, 1f);

        [Tooltip("水面ラインと気泡の色。白〜薄いシアン。")]
        [SerializeField] Color surfaceColor = new Color(0.874f, 0.969f, 1f, 1f);

        [Tooltip("着底時に下方から差す光の色。")]
        [SerializeField] Color bottomLightColor = new Color(0.62f, 0.914f, 1f, 1f);

        [Header("波紋 (Phase 1)")]
        [Tooltip("波紋が生まれるワールド上の点までの距離[m]。頭の正面。")]
        [SerializeField] float rippleDistance = 2f;

        [Tooltip("リングの本数感。大きいほど細かい同心円になる。")]
        [SerializeField] float ringFrequency = 16f;

        [Tooltip("屈折風の強さ。0 で純粋な光のリングだけになる。")]
        [Range(0f, 1f)][SerializeField] float refraction = 0.6f;

        [Header("水位 (Phase 2-4)")]
        [Tooltip("演出開始時の水位。頭から何 m 下か（＝だいたい足元）。")]
        [SerializeField] float startBelowHead = 1.6f;

        [Tooltip("Phase 3 の終わりまでに、開始時の頭の高さから何 m 上まで水位を上げるか。")]
        [SerializeField] float riseAboveHead = 5.5f;

        [Tooltip("Phase 4 でさらに潜る深さ[m]。大きいほど最後の加速が強い。")]
        [SerializeField] float plungeDepth = 9f;

        [Tooltip("0 で完全な等速。上げるほど後半が速くなる。VR 酔い対策で上げすぎないこと。")]
        [Range(0f, 1f)][SerializeField] float riseCurveBias = 0.65f;

        [Tooltip("この深さ[m]まで沈むと『完全に水中』扱いになる。")]
        [SerializeField] float submergeDepth = 4f;

        [Tooltip("水の色が深部色へ振り切るまでの深さ[m]。")]
        [SerializeField] float depthRange = 9f;

        [Tooltip("Phase 2 の水面下の不透明度。1 未満にすると現実が青く透けて『沈んで』見える。")]
        [Range(0.3f, 1f)][SerializeField] float shallowWaterOpacity = 0.72f;

        [Header("水中の見え方")]
        [Tooltip("全天球レイヤーの半径[m]。目の間隔に対して十分遠く、左右の視差が消える距離。")]
        [SerializeField] float skyRadius = 5f;

        [Tooltip("水面ラインの太さ[m]。")]
        [SerializeField] float surfaceThickness = 0.07f;

        [Tooltip("水面の細波の振幅[m]。")]
        [SerializeField] float surfaceWave = 0.05f;

        [Tooltip("水面全体のうねりの振幅[m]。")]
        [SerializeField] float surfaceSway = 0.06f;

        [Tooltip("水中のゆらぎ[rad]。視野の 1〜2% 程度に抑えること。大きくすると強烈に酔う。")]
        [Range(0f, 0.06f)][SerializeField] float underwaterWobble = 0.018f;

        [Tooltip("気泡が昇る速さ。")]
        [SerializeField] float bubbleSpeed = 1.5f;

        [Header("パススルー")]
        [Tooltip("水位上昇中、現実の輪郭をシアンで光らせる。")]
        [SerializeField] bool useEdgeRendering = true;

        OverlayLayer _liquid;
        OverlayLayer _ripple;
        Transform _head;

        Vector3 _rippleOrigin;      // 波紋が生まれるワールド上の点
        float _floorY;              // 演出開始時の足元のワールド Y。水位の基準
        float _bubbleTime;          // 気泡だけは加速させたいので別の時計を回す

        // パススルーの色調整はリフレクション越しで毎フレーム叩くと細かい GC が走る。
        // 直近の値を覚えておき、目に見えない差は捨てる。
        float _lastBrightness = float.NaN;
        float _lastSaturation = float.NaN;
        bool _edgeOn;

        public override string DisplayName => "LIQUID DIVE";

        public override string Tagline => "部屋が水で満たされ、頭まで沈んでいく";

        public override float Duration => duration;

        public override Color AccentColor => shallowColor;

        /// <summary>深い藍。遷移先はこの色から明けてくる。</summary>
        public override Color ArrivalFadeColor => deepColor;

        public override void OnPrepare(DiveContext context)
        {
            var overlay = context?.Overlay;
            if (overlay == null) return;

            _head = context.Head;
            _bubbleTime = 0f;
            _lastBrightness = float.NaN;
            _lastSaturation = float.NaN;
            _edgeOn = false;

            // 水位の基準は「演出開始時の足元」。ここだけワールド絶対値で押さえておけば、
            // 以降どれだけ頭が動いても水面は現実に対して静止する。
            _floorY = (_head != null ? _head.position.y : 0f) - startBelowHead;
            _rippleOrigin = context.ForwardPoint(rippleDistance);

            // 全天球 2 枚。水のレイヤーが先、波紋がその上。どちらも ZTest Always なので
            // 描画順はキューだけで決まる。
            _liquid = overlay.AddSphere(
                MRDiveShaders.Load(MRDiveShaders.LiquidDive),
                DiveOverlay.BaseQueue + 10,
                skyRadius,
                OverlayFollow.PositionOnly);

            _ripple = overlay.AddSphere(
                MRDiveShaders.Load(MRDiveShaders.Ripple),
                DiveOverlay.BaseQueue + 20,
                skyRadius * 0.95f,
                OverlayFollow.PositionOnly);

            if (_liquid != null)
            {
                // 色と定数は 1 回だけ。Linear プロジェクトだが Core (DiveArrivalFade) が
                // Color をそのまま SetColor しているので、着地フェードと色が揃うよう同じ扱いにする。
                _liquid.Set(ShallowColorId, shallowColor);
                _liquid.Set(DeepColorId, deepColor);
                _liquid.Set(SurfaceColorId, surfaceColor);
                _liquid.Set(BottomLightColorId, bottomLightColor);

                _liquid.Set(RadiusId, skyRadius);
                _liquid.Set(DepthRangeId, Mathf.Max(0.01f, depthRange));
                _liquid.Set(SurfaceThicknessId, Mathf.Max(0.001f, surfaceThickness));
                _liquid.Set(SurfaceWaveId, surfaceWave);
                _liquid.Set(SwayId, surfaceSway);
                _liquid.Set(WobbleId, underwaterWobble);

                _liquid.Alpha = 1f;
                _liquid.Set(WaterAlphaId, 0f);
                _liquid.Visible = false;   // Phase 1 の間は塗るものが無いので消しておく（フィルレート節約）
            }

            if (_ripple != null)
            {
                _ripple.Set(RingColorId, Color.white);
                _ripple.Set(CoreColorId, Color.Lerp(surfaceColor, shallowColor, 0.45f));
                _ripple.Set(RingFreqId, ringFrequency);
                _ripple.Set(RefractId, refraction);
                _ripple.Alpha = 0f;
                _ripple.Visible = false;
            }
        }

        public override IEnumerator Run(DiveContext context)
        {
            if (context == null) yield break;

            // Director 以外から直接呼ばれても動くように、未準備ならここで作る。
            if (_liquid == null && _ripple == null) OnPrepare(context);

            var passthrough = context.Passthrough;

            yield return Sweep(t =>
            {
                float time = t * Mathf.Max(0.01f, duration);
                float headY = _head != null ? _head.position.y : _floorY + startBelowHead;

                // ---- Phase 4 の進行度は複数箇所で使うので先に出す ----
                float plunge = Span(t, SinkEnd, 1f);

                // ============================================================
                // Phase 1 (0.00–0.18) 波紋
                // 正面 2m のワールド点から同心円が生まれ、視界全体へ広がる。
                // ============================================================
                if (_ripple != null)
                {
                    // 進行そのものは 0.20 で振り切り、余韻だけ 0.26 まで引く。
                    // イージングは控えめ。OutCubic のように強くかけると 0.2 秒足らずで視界の外へ
                    // 抜けてしまい、せっかくの波紋がほとんど見えないまま終わる。
                    float spread = Span(t, 0f, 0.20f);
                    float progress = Mathf.Lerp(spread, DiveEase.OutQuad(spread), 0.45f);
                    float fade = Span(t, 0f, 0.03f) * (1f - Span(t, 0.15f, 0.26f));

                    _ripple.Visible = fade > 0.002f;
                    if (_ripple.Visible)
                    {
                        // 中心は「方向」ではなく「ワールド上の点」。頭が歩いても中心が置き去りになる。
                        Vector3 dir = _head != null
                            ? (_rippleOrigin - _head.position).normalized
                            : Vector3.forward;

                        _ripple.Set(OriginId, new Vector4(dir.x, dir.y, dir.z, 0f));
                        _ripple.Set(ProgressId, progress);
                        _ripple.Set(FlowTimeId, time);
                        _ripple.Alpha = fade;
                    }
                }

                // ============================================================
                // Phase 2 (0.18–0.46) 水位上昇 / Phase 3 (0.46–0.86) 沈降
                // 水位はワールド絶対値。ほぼ等速で上げ、急加速させない。
                // ============================================================
                float rise = Span(t, RippleEnd, SinkEnd);
                float eased = rise * ((1f - riseCurveBias) + riseCurveBias * rise);

                float level = _floorY + eased * (startBelowHead + riseAboveHead);

                // Phase 4 はさらに潜る。水位を上げるのではなく「自分が沈む」絵だが、
                // 相対関係は同じなので水位側を伸ばして表現する。
                level += DiveEase.InQuad(plunge) * plungeDepth;

                // 頭がどれだけ水面下にいるか。0 = まだ水上、1 = 完全に水中。
                float submerge = Mathf.Clamp01((level - headY) / Mathf.Max(0.01f, submergeDepth));

                if (_liquid != null)
                {
                    // 水の被膜は波紋が通り過ぎるのに合わせて現れる。
                    // Phase 2 の水面下は完全には塗り潰さない。現実が青く「沈んで」見えてほしいので、
                    // 頭が沈みきるまでは向こう側のパススルーを透かす。
                    float waterAlpha = Span(t, 0.13f, 0.30f) * Mathf.Lerp(shallowWaterOpacity, 1f, submerge);
                    _liquid.Visible = waterAlpha > 0.002f;

                    if (_liquid.Visible)
                    {
                        _liquid.Set(WaterLevelYId, level);
                        _liquid.Set(HeadYId, headY);
                        _liquid.Set(WaterAlphaId, waterAlpha);
                        _liquid.Set(SubmergeId, submerge);

                        // 水面は白く光る。深く潜るほど遠ざかるので弱める。
                        _liquid.Set(SurfaceGlowId, Mathf.Lerp(1.7f, 0.15f, submerge));

                        // コースティクスは Phase 2 で立ち上がり、深くなるほど届かなくなる。
                        _liquid.Set(CausticsId, Span(t, 0.20f, 0.34f) * Mathf.Lerp(0.95f, 0.25f, submerge));

                        // 気泡は水面が頭に迫るあたりから。Phase 4 で一気に増える。
                        _liquid.Set(BubblesId, Mathf.Clamp01(Span(t, 0.44f, 0.60f) * 0.8f + plunge * 0.4f));
                        _liquid.Set(StreakId, DiveEase.InQuad(plunge));

                        // 下方の光と、深い藍への収束。
                        _liquid.Set(BottomLightId, DiveEase.InQuad(Span(t, SinkEnd, 0.97f)) * 1.1f);
                        _liquid.Set(ConvergeId, DiveEase.InQuad(Span(t, 0.90f, 1f)));

                        _liquid.Set(FlowTimeId, time);

                        // 気泡だけは別時計。Phase 4 で流れが加速しても不連続にならないよう積分する。
                        _bubbleTime += Time.deltaTime * bubbleSpeed * (1f + DiveEase.InQuad(plunge) * 5f);
                        _liquid.Set(BubbleTimeId, _bubbleTime);
                    }
                }

                // ============================================================
                // パススルー（現実の見え方）
                // ============================================================
                if (passthrough != null)
                {
                    // Phase 2: 現実を青く沈ませる。色相は動かせないので、暗く・彩度を落として
                    // 上に重ねた青の被膜に馴染ませる。Phase 1 では一切触らない。
                    float tint = Span(t, RippleEnd, 0.52f);
                    if (tint > 0f)
                        PushColorAdjustment(passthrough, -0.30f * tint, 0.10f * tint, -0.40f * tint);

                    // Phase 3: 現実が水に飲まれて消えていく。水面が頭を越えた直後から落とす。
                    passthrough.Opacity = 1f - DiveEase.InOutSine(Span(t, RiseEnd + 0.02f, 0.84f));

                    // 水位上昇の間だけ、現実の輪郭をシアンで光らせて「濡れた」感じを出す。
                    if (useEdgeRendering)
                    {
                        bool wantEdge = t >= RippleEnd && t < 0.56f;
                        if (wantEdge != _edgeOn)
                        {
                            _edgeOn = wantEdge;
                            passthrough.SetEdgeRendering(wantEdge, surfaceColor);
                        }
                    }
                }
            });
        }

        public override void OnCleanup(DiveContext context)
        {
            if (context != null && context.Passthrough != null && _edgeOn)
            {
                context.Passthrough.SetEdgeRendering(false, surfaceColor);
                _edgeOn = false;
            }

            // レイヤーの破棄は Overlay.Clear() が面倒を見るので、参照を落とすだけでよい。
            _liquid = null;
            _ripple = null;
            _head = null;
        }

        /// <summary>
        /// パススルーの色調整を送る。SDK 側はリフレクション経由で object[] を作るため、
        /// 毎フレーム叩くと細かい GC が走る。目に見えない差は捨てる。
        /// </summary>
        void PushColorAdjustment(PassthroughBridge passthrough, float brightness, float contrast, float saturation)
        {
            const float step = 1f / 48f;

            if (!float.IsNaN(_lastBrightness)
                && Mathf.Abs(brightness - _lastBrightness) < step
                && Mathf.Abs(saturation - _lastSaturation) < step)
            {
                return;
            }

            _lastBrightness = brightness;
            _lastSaturation = saturation;
            passthrough.SetColorAdjustment(brightness, contrast, saturation);
        }
    }
}
