using System.Collections;
using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// PORTAL RIFT — 次元の裂け目。
    ///
    /// 現実のパススルー空間に縦の亀裂が走り、それが横に開いて円形ポータルになり、
    /// ポータルが視点へ迫って視界を飲み込み、白い閃光で VR 側へ抜ける。
    ///
    /// レイヤーは 2 枚だけ:
    ///   - 全天球 (MRDive_PortalRift)    … ポータルの「外側」。速度線 / ビネット / 閃光
    ///   - 正面の板 (MRDive_PortalSurface) … ポータルそのもの。亀裂 → 円 → 全面
    ///
    /// 板は毎フレーム距離を詰めるので、両眼の輻輳でも「近づいてくる」ことが分かる。
    /// 視界を回す動きは一切入れていない（VR 酔い対策）。全画面の明滅も Phase 1 の
    /// 微かな 1 回と Phase 4 のホワイトアウトの合計 2 回に抑えてある。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Livisor/MR Dive/Transitions/Portal Rift")]
    public sealed class PortalRiftTransition : DiveTransitionBase
    {
        // ------------------------------------------------------------------
        // フェーズ境界。仕様どおり 0..1 の正規化時間で持つ。
        // ------------------------------------------------------------------
        const float P1End = 0.22f;   // 亀裂が伸びきる
        const float P2End = 0.52f;   // ポータルが開ききる
        const float P3End = 0.88f;   // 視界が飲み込まれる

        [Header("尺と色")]
        [Tooltip("演出全体の秒数。")]
        [SerializeField] float duration = 3.4f;

        [Tooltip("主色。亀裂のグロー・ポータルのリム・パススルーの輪郭線に使う。")]
        [SerializeField] Color accentColor = new Color(0.373f, 0.906f, 1f, 1f);

        [Tooltip("ポータル内部の深い藍。虚空のビネットにも使う。")]
        [SerializeField] Color deepColor = new Color(0.03f, 0.05f, 0.20f, 1f);

        [Tooltip("白熱した芯・星屑・最後の閃光の色。")]
        [SerializeField] Color hotColor = Color.white;

        [Header("Phase 1 — 亀裂")]
        [Tooltip("亀裂が走る正面の距離[m]。")]
        [SerializeField] float riftDistance = 2.2f;

        [Tooltip("亀裂の初期の高さ[m]。")]
        [SerializeField] float riftStartHeight = 0.15f;

        [Tooltip("亀裂が伸びきった高さ[m]。")]
        [SerializeField] float riftEndHeight = 1.4f;

        [Tooltip("亀裂の幅[m]。白熱した芯の太さ。")]
        [SerializeField] float riftWidth = 0.012f;

        [Header("Phase 2 — 開口")]
        [Tooltip("開ききったポータルの半径[m]。")]
        [SerializeField] float portalRadius = 0.7f;

        [Tooltip("開くときの行き過ぎ量。大きいほど勢いよく弾ける。")]
        [SerializeField] float openOvershoot = 2.2f;

        [Tooltip("ポータル内部の渦の回転速度[回転/秒相当]。")]
        [SerializeField] float spinSpeed = 0.55f;

        [Tooltip("縁の揺れ幅。板の半サイズを 1 とした比。")]
        [SerializeField] float edgeNoise = 0.03f;

        [Tooltip("現実から抜く色の量。")]
        [SerializeField, Range(0f, 1f)] float desaturation = 0.85f;

        [Header("Phase 3 — 吸引")]
        [Tooltip("最終的にポータルが到達する距離[m]。近すぎると目が疲れるので 0.5 以上。")]
        [SerializeField] float approachDistance = 0.6f;

        [Tooltip("ポータルが視界を覆うまでの拡大倍率。")]
        [SerializeField] float portalGrowth = 9f;

        [Tooltip("吸引中に渦の回転へ上乗せする位相。")]
        [SerializeField] float pullSpin = 2.4f;

        [Tooltip("放射状の速度線が流れる速さ。")]
        [SerializeField] float streakSpeed = 0.9f;

        [Header("レイヤー")]
        [Tooltip("ポータル板が視野に対してどれだけ余裕を持つか。1 で視野ぴったり。")]
        [SerializeField] float quadCoverage = 1.5f;

        [Tooltip("リムの太さ。板の半サイズを 1 とした比。")]
        [SerializeField] float rimWidth = 0.012f;

        [Tooltip("外側グローの届く範囲。板の半サイズを 1 とした比。")]
        [SerializeField] float glowRange = 0.09f;

        [Tooltip("リムとグローの明るさ。")]
        [SerializeField] float glowIntensity = 1.6f;

        // ------------------------------------------------------------------
        // シェーダープロパティ ID。毎フレーム触るので必ずキャッシュする。
        // ------------------------------------------------------------------
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        static readonly int HotColorId = Shader.PropertyToID("_HotColor");
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        // 全天球
        static readonly int ForwardId = Shader.PropertyToID("_Forward");
        static readonly int RightId = Shader.PropertyToID("_Right");
        static readonly int UpId = Shader.PropertyToID("_Up");
        static readonly int StreakId = Shader.PropertyToID("_Streak");
        static readonly int StreakPhaseId = Shader.PropertyToID("_StreakPhase");
        static readonly int WarpId = Shader.PropertyToID("_Warp");
        static readonly int VignetteId = Shader.PropertyToID("_Vignette");

        // ポータル面
        static readonly int ExtentId = Shader.PropertyToID("_Extent");
        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int SpinId = Shader.PropertyToID("_Spin");
        static readonly int SwirlId = Shader.PropertyToID("_Swirl");
        static readonly int DustId = Shader.PropertyToID("_Dust");
        static readonly int RimWidthId = Shader.PropertyToID("_RimWidth");
        static readonly int GlowRangeId = Shader.PropertyToID("_GlowRange");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int EdgeNoiseId = Shader.PropertyToID("_EdgeNoise");

        OverlayLayer _sky;      // 奥: 全天球
        OverlayLayer _portal;   // 手前: ポータル面

        float _lastDesaturation = -1f;
        bool _edgeOn;

        public override string DisplayName => "PORTAL RIFT";
        public override string Tagline => "次元の裂け目が開き、その向こうへ落ちる";
        public override float Duration => Mathf.Max(0.1f, duration);
        public override Color AccentColor => accentColor;

        /// <summary>白閃光で終わるので、着地側も白から明ける。</summary>
        public override Color ArrivalFadeColor => Color.white;

        // ------------------------------------------------------------------

        public override void OnPrepare(DiveContext context)
        {
            var overlay = context?.Overlay;
            if (overlay == null) return;

            // 重ね順: 全天球が奥 (+10)、ポータル面が手前 (+20)。
            _sky = overlay.AddSphere(
                MRDiveShaders.Load(MRDiveShaders.PortalRift),
                DiveOverlay.BaseQueue + 10,
                5f,
                OverlayFollow.PositionOnly);

            _portal = overlay.AddQuad(
                MRDiveShaders.Load(MRDiveShaders.PortalSurface),
                DiveOverlay.BaseQueue + 20,
                riftDistance,
                quadCoverage,
                OverlayFollow.PositionAndRotation);

            if (_sky != null)
            {
                _sky.Set(CoreColorId, accentColor)
                    .Set(DeepColorId, deepColor)
                    .Set(FlashColorId, hotColor)
                    .Set(StreakId, 0f)
                    .Set(StreakPhaseId, 0f)
                    .Set(WarpId, 0f)
                    .Set(VignetteId, 0f)
                    .Set(FlashId, 0f)
                    .Set(AlphaId, 1f);
            }

            if (_portal != null)
            {
                _portal.Set(CoreColorId, accentColor)
                       .Set(DeepColorId, deepColor)
                       .Set(HotColorId, hotColor)
                       .Set(ExtentId, new Vector4(1e-4f, 1e-4f, 0f, 0f))
                       .Set(OpenId, 0f)
                       .Set(SpinId, 0f)
                       .Set(SwirlId, 0f)
                       .Set(DustId, 0f)
                       .Set(RimWidthId, rimWidth)
                       .Set(GlowRangeId, glowRange)
                       .Set(GlowId, glowIntensity)
                       .Set(EdgeNoiseId, 0f)
                       .Set(FlashId, 0f)
                       .Set(AlphaId, 1f);
            }

            _lastDesaturation = -1f;
            _edgeOn = false;
        }

        public override IEnumerator Run(DiveContext context)
        {
            if (context == null) yield break;

            var overlay = context.Overlay;
            var passthrough = context.Passthrough;

            // Director は OnPrepare を必ず呼ぶが、単体テストから直接 Run されても動くように。
            if (_sky == null && _portal == null) OnPrepare(context);

            if (passthrough != null)
            {
                passthrough.Opacity = 1f;
                passthrough.SetDesaturation(0f);
                _lastDesaturation = 0f;
            }

            yield return Sweep(t =>
            {
                float time = t * Duration;

                // ---- Phase 1 (0.00–0.22) 亀裂 ----
                // 一気に伸びて減速する。0.15m → 1.4m。
                float crack = DiveEase.OutExpo(Span(t, 0f, P1End));
                float crackHeight = Mathf.Lerp(riftStartHeight, riftEndHeight, crack);

                // ---- Phase 2 (0.22–0.52) 開口 ----
                // OutBack なので一度円より横に行き過ぎてから収まる。
                float open01 = Span(t, P1End, P2End);
                float open = DiveEase.OutBack(open01, openOvershoot);

                // ---- Phase 3 (0.52–0.88) 吸引 ----
                float pull01 = Span(t, P2End, P3End);
                float pull = DiveEase.InExpo(pull01);

                // ---- Phase 4 (0.88–1.00) 通過 ----
                float flash = DiveEase.InQuad(Span(t, P3End, 1f));

                UpdatePortal(overlay, time, crackHeight, open, open01, pull, flash);
                UpdateSky(context.Head, t, time, pull, flash);
                UpdatePassthrough(passthrough, t);
            });
        }

        public override void OnCleanup(DiveContext context)
        {
            var overlay = context?.Overlay;
            if (overlay != null)
            {
                overlay.Remove(_sky);
                overlay.Remove(_portal);
            }
            _sky = null;
            _portal = null;

            // 現実の見え方は Director も RestoreDefaults で戻すが、
            // 遷移しない設定で繰り返し確認するとき用にここでも戻しておく。
            var passthrough = context?.Passthrough;
            if (passthrough != null)
            {
                passthrough.SetEdgeRendering(false, accentColor);
                passthrough.SetDesaturation(0f);
            }

            _lastDesaturation = -1f;
            _edgeOn = false;
        }

        // ------------------------------------------------------------------
        // ポータル面
        // ------------------------------------------------------------------

        void UpdatePortal(
            DiveOverlay overlay, float time,
            float crackHeight, float open, float open01,
            float pull, float flash)
        {
            if (_portal == null || overlay == null) return;

            float distance = Mathf.Lerp(riftDistance, approachDistance, pull);

            // 全視野を覆う板の一辺。実際にはこの一部しか使わない（後述の fit）。
            float fullSpan = Mathf.Max(overlay.ViewSizeAt(distance) * quadCoverage, 1e-3f);

            // 形の基準は「亀裂が走る距離での板の半サイズ」= 1.0 とする base 単位。
            // ここを固定しておけば、板が近づいても指定した m 単位の見た目が
            // そのまま角度として保たれる。
            float baseHalf = Mathf.Max(overlay.ViewSizeAt(riftDistance) * quadCoverage * 0.5f, 1e-3f);

            // 亀裂 (極細 × 縦長) → ポータル (半径 × 半径) を補間する。
            // OutBack が 1 を超えるので LerpUnclamped。行き過ぎぶんは横方向にだけ効く。
            var crackExtent = new Vector2(riftWidth * 0.5f / baseHalf, crackHeight * 0.5f / baseHalf);
            var circleExtent = new Vector2(portalRadius / baseHalf, portalRadius / baseHalf);
            Vector2 extent = Vector2.LerpUnclamped(crackExtent, circleExtent, open);

            // Phase 3: 視界を覆うまで拡大する。
            extent *= 1f + pull * portalGrowth;
            extent.x = Mathf.Clamp(extent.x, 1e-4f, 24f);
            extent.y = Mathf.Clamp(extent.y, 1e-4f, 24f);

            // リムとグローも base 単位。ポータルが巨大化しても細くなりすぎないよう連れて広げる。
            float openC = Mathf.Clamp01(open);
            float rim = rimWidth * (1f + openC * 1.2f + pull * 2.5f);
            float glow = glowRange * (1f + openC * 0.8f + pull * 3f);

            // 縁のゆらぎ。DiveEase.Flicker は連続値のハッシュなので、連続する time を
            // そのまま渡すと 11Hz ではなくフレームレートで値が飛ぶ。先に 11Hz へ量子化する。
            float wobble = 0.7f + DiveEase.Flicker(Mathf.Floor(time * 11f), 1f) * 0.5f;
            float edge = edgeNoise * Mathf.Clamp01(open01 * 2.5f) * wobble * (1f + pull * 2.5f);

            // --- 板を「実際に描く範囲」まで絞る ---
            // 極細の亀裂しか出ていない Phase 1〜2 で全視野ぶんのフラグメントを走らせない。
            // グローは od≈2.2 で alpha < 1/255 まで落ちるので、そこまでを余白に取れば
            // 切れ目は見えない。fit=1 なら従来どおり全視野を覆う。
            float margin = rim + glow * 2.2f + edge;
            float fit = Mathf.Clamp(Mathf.Max(extent.x, extent.y) + margin, 0.02f, 1f);
            float scale = fullSpan * fit;

            _portal.Transform.localPosition = new Vector3(0f, 0f, distance);
            _portal.Transform.localScale = new Vector3(scale, scale, 1f);

            // 板を fit 倍に縮めたぶん、板ローカルの -1..1 空間で測る値はすべて 1/fit する。
            // 板は正方形のままなので、シェーダー側の等方的な距離近似はそのまま成立する。
            float inv = 1f / fit;

            _portal.Set(ExtentId, new Vector4(extent.x * inv, extent.y * inv, 0f, 0f))
                   .Set(OpenId, openC)
                   .Set(SpinId, time * spinSpeed + pull * pullSpin)
                   .Set(SwirlId, openC * 0.55f)
                   .Set(DustId, Mathf.Clamp01(open01 * 1.4f))
                   .Set(EdgeNoiseId, edge * inv)
                   .Set(RimWidthId, rim * inv)
                   .Set(GlowRangeId, glow * inv)
                   .Set(GlowId, glowIntensity)
                   .Set(FlashId, flash);
        }

        // ------------------------------------------------------------------
        // 全天球（ポータルの外側）
        // ------------------------------------------------------------------

        void UpdateSky(Transform head, float t, float time, float pull, float flash)
        {
            if (_sky == null) return;

            // Phase 3: 中心から外へ流れる放射状の速度線と、視界を閉じるビネット。
            float streak = DiveEase.OutQuad(Span(t, 0.50f, 0.86f));
            float warp = DiveEase.OutQuad(Span(t, 0.48f, 0.90f));
            float vignette = DiveEase.InOutSine(Span(t, 0.50f, 0.88f)) * 0.95f;

            // 閃光は 2 回だけ。Phase 1 の微かな明滅と、Phase 4 のホワイトアウト。
            float blink = DiveEase.Pulse(t, 0.04f, 0.06f) * 0.20f;
            bool whiteout = flash >= blink;
            float flashAmount = whiteout ? flash : blink;

            // 何も乗っていない Phase 1〜2 の大半は、全画面ぶんの半透明描画ごと省く。
            bool active = streak > 0.001f || warp > 0.001f || vignette > 0.001f || flashAmount > 0.001f;
            _sky.Visible = active;
            if (!active) return;

            // 速度線の放射中心。画面 UV の中心は非対称フラスタムのぶん目ごとに光軸とずれるので、
            // 頭の姿勢を球のローカル空間へ移して渡し、シェーダー側はそこへ透視投影する。
            // こうすると左右の目がまったく同じ world 方向の模様を見るため、必ず融像できる。
            // 球は頭の回転を追わない (PositionOnly) ので、この変換は毎フレーム必要。
            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;
            var skyTransform = _sky.Transform;
            if (head != null && skyTransform != null)
            {
                forward = skyTransform.InverseTransformDirection(head.forward);
                right = skyTransform.InverseTransformDirection(head.right);
                up = skyTransform.InverseTransformDirection(head.up);
            }

            _sky.Set(ForwardId, new Vector4(forward.x, forward.y, forward.z, 0f))
                .Set(RightId, new Vector4(right.x, right.y, right.z, 0f))
                .Set(UpId, new Vector4(up.x, up.y, up.z, 0f))
                .Set(StreakId, streak)
                .Set(StreakPhaseId, time * streakSpeed + pull * 2.5f)
                .Set(WarpId, warp)
                .Set(VignetteId, vignette)
                .Set(FlashColorId, whiteout ? hotColor : Color.Lerp(hotColor, accentColor, 0.5f))
                .Set(FlashId, flashAmount);
        }

        // ------------------------------------------------------------------
        // 現実の見え方
        // ------------------------------------------------------------------

        void UpdatePassthrough(PassthroughBridge passthrough, float t)
        {
            if (passthrough == null) return;

            // Phase 1 は 1.0 のまま。Phase 3 の前半で現実を消しきる。
            passthrough.Opacity = 1f - DiveEase.InOutCubic(Span(t, P2End, 0.80f));

            // Phase 2 で現実から色が抜ける。
            // SetDesaturation はリフレクション越しなので、変化したときだけ叩く。
            float desat = desaturation * DiveEase.OutQuad(Span(t, P1End, P2End));
            if (Mathf.Abs(desat - _lastDesaturation) > 0.01f)
            {
                _lastDesaturation = desat;
                passthrough.SetDesaturation(desat);
            }

            // 亀裂が走りきったところで現実を線画化する。1 回だけ。
            if (!_edgeOn && t >= P1End)
            {
                _edgeOn = true;
                passthrough.SetEdgeRendering(true, accentColor);
            }
        }
    }
}
