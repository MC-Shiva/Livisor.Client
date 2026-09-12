using System.Collections;
using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// DIGITAL DISSOLVE / 電脳解体。
    ///
    /// 現実が走査され、グリッチで分解され、データの奔流になって VR へ転送される。
    ///
    ///   Phase 1 (0.00-0.20) 走査     : 水平の走査線が上から下へ 1 回走り、通過後に青いワイヤーグリッドが焼き付く
    ///   Phase 2 (0.20-0.48) グリッチ : RGB 分離 / ブロックのずれ / 白ノイズ。現実側も寒色・硬質へ
    ///   Phase 3 (0.48-0.82) 奔流     : トンネルが奥へ伸びて加速し、光の粒が流れ込む。パススルーが消える
    ///   Phase 4 (0.82-1.00) LINK START: 一瞬青白く飽和し、急速に黒へ落ちる
    ///
    /// レイヤー構成は全天球 2 枚だけ:
    ///   BaseQueue+1 … MRDive_DataStream   (Phase 3 のトンネルと粒。Phase 3 に入るまで非表示)
    ///   BaseQueue+2 … MRDive_DigitalDissolve (走査線 / グリッド / グリッチ / 閃光 / 暗転)
    /// 最後の閃光と暗転は必ず手前でなければならないので、解体レイヤーを上に置いている。
    ///
    /// データストリームに ParticleSystem ではなく「シェーダーを貼った全天球」を選んだ理由:
    ///   1. Quest の GPU はタイルベースで、透明パーティクルの重なりに弱い。400 個の板が
    ///      視点近くに来ると一気にフルスクリーン級のオーバードローになり、コマ落ちが読めない。
    ///      全天球 1 枚なら描画コストは「FOV ぶんのピクセル × 1 回」で固定される。
    ///   2. CPU 側のシミュレーションとメッシュ更新が毎フレーム発生しない（GC も出ない）。
    ///   3. 3.6 秒の決め打ち演出なので、フレームレートに左右されない決定的な見た目が望ましい。
    ///   4. Single Pass Instanced では自前シェーダーのほうが両目の確実性を担保しやすい。
    /// 板ではなく球にしたのは、板だと内容が 1.5m 前後の近距離に貼り付いて VR では目が疲れるため。
    /// 半径 5m の球なら視差がほぼ消え、もう 1 枚のレイヤーと奥行きが揃う。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Livisor/MR Dive/Transitions/Digital Dissolve")]
    public sealed class DigitalDissolveTransition : DiveTransitionBase
    {
        // ------------------------------------------------------------------
        // フェーズ境界（正規化時間）
        // ------------------------------------------------------------------
        const float ScanEnd = 0.20f;
        const float GlitchEnd = 0.48f;
        const float StreamEnd = 0.82f;

        // ------------------------------------------------------------------
        // シェーダープロパティ ID
        // 毎フレーム書き込むので、文字列引きを避けてここで必ずキャッシュする。
        // ------------------------------------------------------------------
        static readonly int GridColorId = Shader.PropertyToID("_GridColor");
        static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
        static readonly int ScanColorId = Shader.PropertyToID("_ScanColor");
        static readonly int ChargeColorId = Shader.PropertyToID("_ChargeColor");
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        static readonly int ScanPosId = Shader.PropertyToID("_ScanPos");
        static readonly int ScanWidthId = Shader.PropertyToID("_ScanWidth");
        static readonly int ScanIntensityId = Shader.PropertyToID("_ScanIntensity");
        static readonly int GridDensityId = Shader.PropertyToID("_GridDensity");
        static readonly int GridWidthId = Shader.PropertyToID("_GridWidth");
        static readonly int GridIntensityId = Shader.PropertyToID("_GridIntensity");
        static readonly int GlitchId = Shader.PropertyToID("_Glitch");
        static readonly int BlockScaleId = Shader.PropertyToID("_BlockScale");
        static readonly int BlockSeedId = Shader.PropertyToID("_BlockSeed");
        static readonly int RgbSplitId = Shader.PropertyToID("_RgbSplit");
        static readonly int NoiseFlashId = Shader.PropertyToID("_NoiseFlash");
        static readonly int NoiseSeedId = Shader.PropertyToID("_NoiseSeed");
        static readonly int ChargeId = Shader.PropertyToID("_Charge");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int BlackoutId = Shader.PropertyToID("_Blackout");

        static readonly int StreamColorId = Shader.PropertyToID("_StreamColor");
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int ForwardId = Shader.PropertyToID("_Forward");
        static readonly int RightId = Shader.PropertyToID("_Right");
        static readonly int UpId = Shader.PropertyToID("_Up");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        static readonly int TunnelGridId = Shader.PropertyToID("_TunnelGrid");
        static readonly int RingScaleId = Shader.PropertyToID("_RingScale");
        static readonly int SpokeCountId = Shader.PropertyToID("_SpokeCount");
        static readonly int StreakCountId = Shader.PropertyToID("_StreakCount");
        static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
        static readonly int CoreGlowId = Shader.PropertyToID("_CoreGlow");

        // ------------------------------------------------------------------
        // 調整値
        // ------------------------------------------------------------------
        [Header("尺")]
        [Tooltip("演出全体の秒数。")]
        [SerializeField, Range(1.5f, 8f)] float duration = 3.6f;

        [Header("色")]
        [Tooltip("主色。電子ブルー #3CC8FF。")]
        [SerializeField] Color gridColor = new Color(0.235f, 0.784f, 1f, 1f);

        [Tooltip("グリッチのアクセント。マゼンタ #FF3CA8。")]
        [SerializeField] Color accentColor = new Color(1f, 0.235f, 0.659f, 1f);

        [Tooltip("走査線の帯の色。薄いシアン。")]
        [SerializeField] Color scanColor = new Color(0.55f, 0.95f, 1f, 1f);

        [Tooltip("Phase 3 で視界を満たす帯電の色。")]
        [SerializeField] Color chargeColor = new Color(0.62f, 0.88f, 1f, 1f);

        [Tooltip("LINK START の閃光の色。")]
        [SerializeField] Color flashColor = new Color(0.82f, 0.95f, 1f, 1f);

        [Header("グリッド")]
        [SerializeField] float gridDensityStart = 16f;
        [SerializeField] float gridDensityEnd = 46f;
        [SerializeField, Range(0.002f, 0.06f)] float gridLineWidth = 0.016f;

        [Header("グリッチ")]
        [Tooltip("0 にするとグリッチ表現を完全に切れる。光過敏性への配慮が必要な場面用。")]
        [SerializeField, Range(0f, 1f)] float glitchStrength = 1f;

        [Tooltip("グリッチが断続する周波数[Hz]。光過敏性の基準（3Hz）を超えられないよう上限を 3 で止めてある。")]
        [SerializeField, Range(0.5f, 3f)] float glitchBurstRate = 2.6f;

        [Tooltip("ずれるブロックの並びを組み替える頻度[Hz]。こちらは 3Hz を超えるが、" +
                 "明滅する面積が視野の 20%（光過敏性の目安である 25% 未満）に抑えてあるため基準内。")]
        [SerializeField, Range(2f, 12f)] float blockRefreshRate = 8f;

        [Header("データストリーム")]
        [SerializeField] float streamSpeedStart = 0.5f;
        [SerializeField] float streamSpeedEnd = 5.5f;

        [Tooltip("流れ込むレーンの本数。シェーダー側が半分の本数の層を重ねるので、偶数に丸めて渡す。")]
        [SerializeField] float streakCount = 88f;

        [SerializeField] float spokeCount = 22f;

        [Header("パススルー")]
        [Tooltip("グリッチ中に現実の輪郭を電子ブルーで光らせる。SDK が未対応なら黙って無視される。")]
        [SerializeField] bool useEdgeRendering = true;

        // ------------------------------------------------------------------
        // 実行時の状態
        // ------------------------------------------------------------------
        OverlayLayer _dissolve;
        OverlayLayer _stream;
        bool _streamVisible;
        bool _edgeOn;
        float _colorAdjustStep = float.NaN;

        public override string DisplayName => "DIGITAL DISSOLVE";
        public override string Tagline => "現実を走査し、解体し、データにして転送する";
        public override float Duration => duration;
        public override Color AccentColor => gridColor;
        public override Color ArrivalFadeColor => Color.black;

        // ------------------------------------------------------------------

        public override void OnPrepare(DiveContext context)
        {
            if (context == null || context.Overlay == null) return;

            var overlay = context.Overlay;

            // 下: データストリーム / 上: 解体レイヤー。閃光と暗転は必ず手前でなければならない。
            _stream = overlay.AddSphere(
                MRDiveShaders.Load(MRDiveShaders.DataStream),
                DiveOverlay.BaseQueue + 1,
                follow: OverlayFollow.PositionOnly);

            _dissolve = overlay.AddSphere(
                MRDiveShaders.Load(MRDiveShaders.DigitalDissolve),
                DiveOverlay.BaseQueue + 2,
                follow: OverlayFollow.PositionOnly);

            // 演出中に変わらないものはここで 1 回だけ入れておく。
            if (_dissolve != null)
            {
                _dissolve
                    .Set(GridColorId, gridColor)
                    .Set(AccentColorId, accentColor)
                    .Set(ScanColorId, scanColor)
                    .Set(ChargeColorId, chargeColor)
                    .Set(FlashColorId, flashColor)
                    .Set(GridWidthId, gridLineWidth)
                    .Set(ScanWidthId, 0.045f)
                    .Set(ScanPosId, -0.2f)
                    .Set(ScanIntensityId, 0f)
                    .Set(GridDensityId, gridDensityStart)
                    .Set(GridIntensityId, 0f)
                    .Set(GlitchId, 0f)
                    .Set(BlockScaleId, 12f)
                    .Set(BlockSeedId, 0f)
                    .Set(RgbSplitId, 0f)
                    .Set(NoiseFlashId, 0f)
                    .Set(NoiseSeedId, 0f)
                    .Set(ChargeId, 0f)
                    .Set(FlashId, 0f)
                    .Set(BlackoutId, 0f);
            }

            if (_stream != null)
            {
                // レーン数は必ず偶数の整数にする。シェーダーが 2 層目で半分の本数を使うため、
                // 半端な値だと経度の折り返しでレーンが 1 本切れて継ぎ目が見える。
                float lanes = Mathf.Max(8f, Mathf.Round(streakCount * 0.5f) * 2f);

                _stream
                    .Set(StreamColorId, gridColor)
                    .Set(CoreColorId, chargeColor)
                    .Set(IntensityId, 0f)
                    .Set(ScrollId, 0f)
                    .Set(TunnelGridId, 0f)
                    .Set(RingScaleId, 0.55f)
                    .Set(SpokeCountId, Mathf.Max(3f, Mathf.Round(spokeCount)))
                    .Set(StreakCountId, lanes)
                    .Set(LineWidthId, 0.03f)
                    .Set(CoreGlowId, 0f);

                // トンネルの軸は「演出が始まった瞬間の視線」で固定する。毎フレーム頭に追従させると
                // 消失点が視線に貼り付き、全視野の放射フローが常に中心から湧いて強い vection
                // （自己運動錯覚 = VR 酔い）を起こす。固定すれば頭を振って流れから目を外せるし、
                // 「消失点が付いてくる」不自然さも消える。
                Vector3 fwd = Vector3.forward;
                Vector3 right = Vector3.right;
                Vector3 up = Vector3.up;
                if (context.Head != null)
                {
                    Transform st = _stream.Transform;
                    fwd = st.InverseTransformDirection(context.Head.forward);
                    right = st.InverseTransformDirection(context.Head.right);
                    up = st.InverseTransformDirection(context.Head.up);
                }

                _stream
                    .Set(ForwardId, (Vector4)fwd)
                    .Set(RightId, (Vector4)right)
                    .Set(UpId, (Vector4)up);

                // Phase 3 まで出番がない。フルスクリーンのオーバードローを 1 枚ぶん節約する。
                _stream.Visible = false;
                _streamVisible = false;
            }

            _edgeOn = false;
            _colorAdjustStep = float.NaN;
        }

        public override IEnumerator Run(DiveContext context)
        {
            if (context == null) yield break;

            // Director 以外から直接呼ばれても動くように、未準備ならここで作る（他 2 本と同じ）。
            if (_dissolve == null && _stream == null) OnPrepare(context);

            var passthrough = context.Passthrough;

            yield return Sweep(t =>
            {
                float seconds = t * duration;

                float p1 = Span(t, 0f, ScanEnd);         // 走査
                float p2 = Span(t, ScanEnd, GlitchEnd);  // グリッチ
                float p3 = Span(t, GlitchEnd, StreamEnd);// データストリーム
                float p4 = Span(t, StreamEnd, 1f);       // LINK START

                // ==========================================================
                // Phase 1: 走査線が上から下へ 1 回だけ走る
                // ==========================================================
                // 画面外から入って画面外へ抜ける。抜けきった後は _ScanPos が 1 を超えたまま残り、
                // シェーダー側の gridMask が「全天に焼き付いたグリッド」になる。
                float scanPos = Mathf.Lerp(-0.08f, 1.08f, DiveEase.InOutSine(p1));
                float scanIntensity = 1f - Span(t, ScanEnd, ScanEnd + 0.04f);

                // ==========================================================
                // グリッドの濃さと密度
                // ==========================================================
                // 走査線の通過で薄く焼き付き → グリッチで密度と輝度が上がり → 奔流に呑まれて消える。
                float gridIntensity = 0.30f * DiveEase.OutQuad(p1);
                gridIntensity = Mathf.Lerp(gridIntensity, 0.85f, DiveEase.OutQuad(p2));
                gridIntensity = Mathf.Lerp(gridIntensity, 0.30f, DiveEase.InOutSine(p3));
                gridIntensity = Mathf.Lerp(gridIntensity, 0f, DiveEase.InQuad(p4));

                float gridDensity = Mathf.Lerp(gridDensityStart, gridDensityEnd,
                    Mathf.Max(DiveEase.OutQuad(p2) * 0.7f, DiveEase.InOutCubic(p3)));

                // ==========================================================
                // Phase 2: グリッチ
                // ==========================================================
                // 断続感は「時間を量子化してからハッシュ」で作る。DiveEase.Flicker を連続した t に
                // そのまま与えるとフレームごとに値が飛んで毎フレーム点滅してしまうため。
                float burstStep = Mathf.Floor(seconds * glitchBurstRate);
                float burst = DiveEase.Flicker(burstStep, 1f, 4.7f);
                float gate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.80f, burst));

                float glitchEnv = DiveEase.OutQuad(Span(t, ScanEnd, ScanEnd + 0.05f))
                                  * (1f - DiveEase.InQuad(Span(t, GlitchEnd - 0.06f, GlitchEnd + 0.03f)));
                float glitch = glitchEnv * gate * glitchStrength;

                // 分離量はグリッド 1 周期に対する割合。シェーダー側で _GridDensity で割って
                // 経度に換算する。経度で固定にすると、密度が上がる Phase 3 で分離量が
                // 1 周期に迫り、色収差ではなく「隣の線と重なった別の線」に見えてしまう。
                float rgbSplit = glitch * 0.30f * (0.4f + 0.6f * DiveEase.Flicker(burstStep, 1f, 11.3f));
                float blockSeed = Mathf.Floor(seconds * blockRefreshRate);
                float blockScale = Mathf.Lerp(9f, 17f, p2);

                // 白ノイズのフラッシュ 3 回。
                // 光過敏性への配慮として、(a) 間隔は必ず 0.36 秒（< 3Hz）空ける、
                // (b) シェーダー側で画素単位のまだらにして被覆率を 45% で頭打ちにする、
                // の 2 段構えで「全画面同時のストロボ」にならないようにしている。
                float noiseFlash = 0f;
                float glitchStartSec = ScanEnd * duration;
                float glitchEndSec = GlitchEnd * duration;
                for (int k = 0; k < 3; k++)
                {
                    float centerSec = glitchStartSec + 0.16f + 0.36f * k;
                    if (centerSec > glitchEndSec) break;
                    noiseFlash = Mathf.Max(noiseFlash, DiveEase.Pulse(seconds, centerSec, 0.07f));
                }
                noiseFlash *= 0.85f * glitchStrength;

                // ==========================================================
                // Phase 3: データストリーム
                // ==========================================================
                float streamIn = DiveEase.OutQuad(Span(t, GlitchEnd - 0.03f, GlitchEnd + 0.12f));
                float streamOut = 1f - DiveEase.InQuad(Span(t, StreamEnd + 0.02f, StreamEnd + 0.12f));
                float streamIntensity = streamIn * streamOut;

                // スクロール量は「速度を積分した式」で出す。毎フレーム加算しないので、
                // フレームレートが揺れても同じ t なら必ず同じ絵になる。
                //   v(p) = lerp(v0, v1, p^2) を p について 0..P で積分 = v0*P + (v1-v0)*P^3/3
                float streamSeconds = (StreamEnd - GlitchEnd) * duration;
                float scroll = streamSeconds *
                               (streamSpeedStart * p3 + (streamSpeedEnd - streamSpeedStart) * p3 * p3 * p3 / 3f);
                scroll += p4 * (1f - StreamEnd) * duration * streamSpeedEnd;

                // トンネルが奥へ伸びていく。
                float ringScale = Mathf.Lerp(0.55f, 1.15f, DiveEase.OutQuad(p3));
                float coreGlow = DiveEase.InQuad(p3) * 0.55f;

                // 視界全体が青白く帯電していく。
                float charge = DiveEase.InQuad(p3) * 0.5f;

                // ==========================================================
                // Phase 4: LINK START
                // ==========================================================
                // この演出で唯一の全画面ストロボ。立ち上げてすぐ黒へ落とす。
                float flash = DiveEase.OutQuint(Span(t, StreamEnd, StreamEnd + 0.035f))
                              * (1f - DiveEase.InQuad(Span(t, StreamEnd + 0.045f, StreamEnd + 0.16f)));
                float blackout = DiveEase.InQuad(Span(t, StreamEnd + 0.06f, 0.985f));

                // ==========================================================
                // レイヤーへ反映
                // ==========================================================
                if (_dissolve != null)
                {
                    _dissolve
                        .Set(ScanPosId, scanPos)
                        .Set(ScanIntensityId, scanIntensity)
                        .Set(GridIntensityId, gridIntensity)
                        .Set(GridDensityId, gridDensity)
                        .Set(GlitchId, glitch)
                        .Set(RgbSplitId, rgbSplit)
                        .Set(BlockScaleId, blockScale)
                        .Set(BlockSeedId, blockSeed)
                        .Set(NoiseFlashId, noiseFlash)
                        .Set(NoiseSeedId, Mathf.Floor(seconds * 24f))
                        .Set(ChargeId, charge)
                        .Set(FlashId, flash)
                        .Set(BlackoutId, blackout);
                }

                if (_stream != null)
                {
                    // 黒に沈みきったら描くだけ無駄なので止める。
                    bool wantVisible = streamIntensity > 0.001f && blackout < 0.95f;
                    if (wantVisible != _streamVisible)
                    {
                        _stream.Visible = wantVisible;
                        _streamVisible = wantVisible;
                    }

                    if (wantVisible)
                    {
                        // _Forward / _Right / _Up は OnPrepare で入れた「開始時の視線」のまま動かさない。
                        // VR 酔い（vection）対策なので、頭に追従させないこと。
                        _stream
                            .Set(IntensityId, streamIntensity)
                            .Set(ScrollId, scroll)
                            .Set(TunnelGridId, streamIn)
                            .Set(RingScaleId, ringScale)
                            .Set(CoreGlowId, coreGlow * streamOut);
                    }
                }

                // ==========================================================
                // 現実側
                // ==========================================================
                if (passthrough != null)
                {
                    // Phase 2 で寒色・硬質に。Phase 3 で現実そのものを閉じる。
                    ApplyColorAdjustment(passthrough, DiveEase.OutQuad(p2));
                    ApplyEdgeRendering(passthrough, useEdgeRendering && p2 > 0.05f && p3 < 0.35f);
                    passthrough.Opacity = 1f - DiveEase.InOutSine(p3);
                }
            });
        }

        public override void OnCleanup(DiveContext context)
        {
            if (context != null && context.Passthrough != null)
            {
                ApplyEdgeRendering(context.Passthrough, false);
            }

            // レイヤー自体は Overlay.Clear() が破棄するので、参照を手放すだけでよい。
            _dissolve = null;
            _stream = null;
            _streamVisible = false;
            _colorAdjustStep = float.NaN;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// 現実を寒色・硬質に寄せる。amount 0 で素のまま、1 で振り切り。
        /// PassthroughBridge の中身はリフレクション呼び出しなので、毎フレーム叩くと
        /// boxing のゴミが積もる。段階が変わったときだけ送る。
        /// </summary>
        void ApplyColorAdjustment(PassthroughBridge passthrough, float amount)
        {
            // 量子化した整数値どうしの比較なので、NaN 初期値も含めてこの比較で正しく動く。
            float step = Mathf.Round(Mathf.Clamp01(amount) * 24f);
            if (step == _colorAdjustStep) return;
            _colorAdjustStep = step;

            float a = step / 24f;
            passthrough.SetColorAdjustment(-0.10f * a, 0.30f * a, -0.60f * a);
        }

        /// <summary>現実の輪郭線。こちらも状態が変わったときだけ送る。</summary>
        void ApplyEdgeRendering(PassthroughBridge passthrough, bool on)
        {
            if (on == _edgeOn) return;
            _edgeOn = on;
            passthrough.SetEdgeRendering(on, gridColor);
        }
    }
}
