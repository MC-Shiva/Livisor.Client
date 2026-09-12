// LIQUID DIVE の全天球レイヤー。水面ライン / 水中の色 / コースティクス / 気泡 / 着底の光までを
// 1 パスで描く。テクスチャは一切使わず、すべて数式で組んでいる（Quest 3 のモバイル GPU 向け）。
//
// ■ 水面のワールド座標の扱い
//   このレイヤーは OverlayFollow.PositionOnly で頭に「位置だけ」追従する。つまりオブジェクト空間は
//   ワールド軸に平行で、原点が常に頭の位置にある。そこで
//       そのピクセルが指す方向 d（オブジェクト原点＝頭からの向き）
//       ピクセルのワールド Y = _HeadY + d.y * _Radius
//   として復元し、ワールド絶対値の _WaterLevelY と比較する。
//   レイヤーは頭に付いてくるが比較はワールド絶対値なので、頭を上下させても水面は現実に対して
//   静止したまま（しゃがめば水面が視界の上へ、背伸びすれば下へ動く）になる。
//
// ■ 方向をフラグメントで normalize する理由
//   Unity の Sphere プリミティブは面が粗いので、補間されたオブジェクト座標をそのまま使うと
//   水面ラインが多角形にカクつく。フラグメントで normalize して球面へ投げ直すことで滑らかになる。
//
// ■ VR 酔い対策
//   水中のゆらぎ (_Wobble) は視線方向を数 mrad ずらすだけに留めている。全画面が大きくうねると
//   強烈に酔うため、振幅の既定値は視野の 1% 前後。水位の上昇速度も C# 側で等速に近く保つこと。
Shader "Livisor/MRDive/LiquidDive"
{
    Properties
    {
        [Header(Colors)]
        _ShallowColor ("Shallow Color", Color) = (0.184, 0.659, 0.847, 1)   // #2FA8D8
        _DeepColor ("Deep Color", Color) = (0.024, 0.157, 0.235, 1)         // #06283C
        _SurfaceColor ("Surface Color", Color) = (0.874, 0.969, 1.0, 1)
        _BottomLightColor ("Bottom Light Color", Color) = (0.62, 0.914, 1.0, 1)

        [Header(Water Level)]
        _WaterLevelY ("Water Level (world Y)", Float) = -100
        _HeadY ("Head (world Y)", Float) = 0
        _Radius ("Sphere Radius (m)", Float) = 5
        _DepthRange ("Depth Range (m)", Float) = 9

        [Header(Look)]
        _Alpha ("Alpha", Range(0, 1)) = 1
        _WaterAlpha ("Water Alpha", Range(0, 1)) = 0
        _Submerge ("Submerge", Range(0, 1)) = 0
        _SurfaceThickness ("Surface Thickness (m)", Float) = 0.07
        _SurfaceWave ("Surface Wave (m)", Float) = 0.05
        _SurfaceGlow ("Surface Glow", Range(0, 4)) = 1.6
        _Caustics ("Caustics", Range(0, 2)) = 0
        _Bubbles ("Bubbles", Range(0, 1)) = 0
        _Streak ("Bubble Streak", Range(0, 1)) = 0
        _Sway ("Surface Sway (m)", Float) = 0.06
        _Wobble ("Underwater Wobble (rad)", Range(0, 0.06)) = 0.018
        _BottomLight ("Bottom Light", Range(0, 2)) = 0
        _Converge ("Converge", Range(0, 1)) = 0

        [Header(Time)]
        _FlowTime ("Flow Time", Float) = 0
        _BubbleTime ("Bubble Time", Float) = 0

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        // RGB と A でブレンド式を分ける。A にも SrcAlpha を掛けるとアルファが二乗され、
        // _WaterAlpha 0.72 が実機では 0.52 相当まで薄まってしまう（Editor では気づけない）。
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull [_Cull]
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;   // 頭（＝オブジェクト原点）からこのピクセルへのワールド方向
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            fixed4 _SurfaceColor;
            fixed4 _BottomLightColor;

            float _WaterLevelY;
            float _HeadY;
            float _Radius;
            float _DepthRange;

            fixed _Alpha;
            fixed _WaterAlpha;
            fixed _Submerge;
            float _SurfaceThickness;
            float _SurfaceWave;
            half _SurfaceGlow;    // 1 を超えるので fixed(lowp) ではなく half
            half _Caustics;
            fixed _Bubbles;
            fixed _Streak;
            float _Sway;
            float _Wobble;
            half _BottomLight;    // 同上
            fixed _Converge;

            float _FlowTime;
            float _BubbleTime;

            // ------------------------------------------------------------------
            // 小物
            // ------------------------------------------------------------------

            float Hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            // 交差する 3 枚の正弦波の稜線を尖らせて網目を作る。
            // ノイズテクスチャも fbm も使わず sin 3 回 + 掛け算だけなので、モバイルでも安い。
            float CausticLayer(float2 p, float t, float freq)
            {
                float a = sin(dot(p, float2( 1.00,  0.32)) * freq + t * 1.05);
                float b = sin(dot(p, float2(-0.48,  0.88)) * freq - t * 0.83);
                float c = sin(dot(p, float2( 0.30, -0.96)) * freq + t * 0.94);

                float v = 1.0 - abs((a + b + c) * 0.33333);
                v = saturate(v);
                float v2 = v * v;
                float v4 = v2 * v2;
                return v4 * v2;   // 実効 pow(v, 6)。pow より掛け算のほうが安い
            }

            // 気泡 1 レイヤー。セル分割してセルごとに最大 1 粒。
            // az01: 方位角を 0..1 にしたもの（継ぎ目なし） / up: 仰角 sin
            float BubbleLayer(float az01, float up, float t, float streak, float seed, float rowScale)
            {
                const float COLS = 12.0;

                // セル格子を下へ流すと、粒は相対的に上へ昇る。
                float2 g = float2(az01 * COLS + seed * 0.41, up * rowScale - t);
                float2 cell = floor(g);
                float2 f = frac(g) - 0.5;

                // 真後ろ（az01 = 0 と 1）で同じセルになるように列を折り返す。
                float h = Hash21(float2(fmod(cell.x + COLS, COLS), cell.y) + seed * 31.7);

                // セルの 4 割ほどを間引いて、粒の間隔をばらけさせる。
                float live = step(0.55, h);

                float2 c = (float2(frac(h * 7.13), frac(h * 3.71)) - 0.5) * 0.62;
                c.x += sin(t * 1.6 + h * 40.0) * 0.09;   // 左右にゆらぎながら昇る

                float r = 0.045 + 0.055 * frac(h * 11.3);

                // 加速時は縦に引き伸ばして流線にする。
                float2 o = (f - c) * float2(1.0, 1.0 / (1.0 + streak * 6.0));
                return (1.0 - smoothstep(r * 0.25, r, length(o))) * live;
            }

            // ------------------------------------------------------------------

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);

                // 球の中心＝頭なので、オブジェクト座標を回転・スケールだけ通せばワールド方向になる。
                // 平行移動を含めないのがポイント（含めるとワールド原点基準になってしまう）。
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 d = normalize(i.dir);
                float t = _FlowTime;

                float azim = atan2(d.x, d.z);        // -PI..PI
                float az01 = azim * 0.15915494 + 0.5; // 0..1（1/2PI 倍）

                // --- Phase 3: 水中の視界全体が低周波でゆらぐ。振幅は数 mrad に抑える。
                // azim の係数は必ず整数にすること。非整数だと atan2 が ±π で折り返す
                // 真後ろに段差が入り、これで駆動されるコースティクスと気泡に縦の継ぎ目が出る。
                float2 wob = float2(sin(t * 0.70 + d.y * 3.1), sin(t * 0.55 + azim * 2.0)) * _Wobble;
                float3 dw = normalize(d + float3(wob.x, wob.y, 0.0));

                // --- Phase 2: 水面の細波。角度の整数倍なので一周しても継ぎ目が出ない。
                float wave = sin(azim *  9.0 + t * 2.10) * 0.50
                           + sin(azim * 15.0 - t * 1.55) * 0.32
                           + sin(azim * 23.0 + t * 3.05) * 0.18;

                // 水面全体のゆったりしたうねり（メートル）。
                float swayM = (sin(t * 0.62 + azim) + sin(t * 0.41 + d.y * 2.3)) * 0.5 * _Sway;

                float surfaceY = _WaterLevelY + swayM + wave * _SurfaceWave;

                // このピクセルが指すワールド Y を頭の Y から復元する。ここがワールド水位との比較点。
                float worldY = _HeadY + d.y * _Radius;
                float depth = surfaceY - worldY;      // 正なら水中

                // 仰角が上下端に近いほど 1m あたりの見かけの厚みが増すので、
                // 水面ラインの太さをそれに合わせて補正して角度的にほぼ一定に見せる。
                float horiz = sqrt(saturate(1.0 - d.y * d.y));
                float w = max(_SurfaceThickness, 0.001) * max(horiz, 0.25);

                // --- コースティクス: 水面を真上の水平面と見なして方向を投影する。
                float ay = max(abs(dw.y), 0.28);      // 地平付近で UV が伸びきらないよう下限を置く
                float2 proj = dw.xz / ay;
                float caus = CausticLayer(proj, t, 2.6) * 0.65
                           + CausticLayer(proj * 1.9 + 7.3, t * 1.27, 2.6) * 0.35;
                caus *= smoothstep(0.10, 0.42, abs(dw.y));   // 地平線の帯では潰れるので消す

                // --- 気泡: 2 レイヤーぶんのセル格子。
                float bub = BubbleLayer(az01, dw.y, _BubbleTime, _Streak, 0.0, 2.2)
                          + BubbleLayer(az01, dw.y, _BubbleTime * 1.35, _Streak, 1.0, 3.1) * 0.7;
                bub = saturate(bub) * (1.0 - smoothstep(0.72, 0.96, abs(dw.y)));

                // --- 水中の色。局所の深さで階調、頭の深さ (_Submerge) で全体を沈める。
                float grad = saturate(depth / max(_DepthRange, 0.01));
                grad = grad * grad * (3.0 - 2.0 * grad);

                float3 under = lerp(_ShallowColor.rgb, _DeepColor.rgb, grad);
                under = lerp(under, _DeepColor.rgb, _Submerge * _Submerge * 0.85);
                under += _SurfaceColor.rgb * caus * _Caustics * (1.0 - grad * 0.75);
                under += _SurfaceColor.rgb * bub * _Bubbles * 0.9;

                // --- Phase 4: 下方から差す光。真下ほど強く、ゆっくり脈打つ。
                float down = saturate(-dw.y);
                float beam = down * down * down + smoothstep(0.55, 1.0, down) * 0.8;
                beam *= 0.85 + 0.15 * sin(t * 2.4);
                under += _BottomLightColor.rgb * beam * _BottomLight;

                // --- 水面より上。沈む前は透明（現実が見える）、沈んだ後は「見上げた水面の裏側」。
                float3 above = lerp(_ShallowColor.rgb, _SurfaceColor.rgb, 0.35 + 0.55 * caus);

                float submerged = smoothstep(-w, w * 1.5, depth);
                float3 col = lerp(above, under, submerged);

                // --- 水面ラインそのもの。白く光る。（line は HLSL の予約語なので別名にしている）
                float edge = 1.0 - saturate(abs(depth) / w);
                edge = edge * edge * (3.0 - 2.0 * edge);
                col += _SurfaceColor.rgb * edge * _SurfaceGlow;

                // --- 不透明度: 水中は _WaterAlpha、水上は沈んだぶんだけ。水面ラインは上へもはみ出す。
                float a = lerp(_Submerge, 1.0, submerged) * _WaterAlpha;
                a = max(a, edge * saturate(_SurfaceGlow) * _WaterAlpha);

                // --- Phase 4 の収束。最後は深い藍で埋め尽くす。
                col = lerp(col, _DeepColor.rgb, _Converge);
                a = saturate(max(a, _Converge));

                return fixed4(col, saturate(a * _Alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
