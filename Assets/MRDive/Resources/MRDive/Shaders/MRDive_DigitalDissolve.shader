// DIGITAL DISSOLVE の全天球レイヤー。
// 走査線 → 焼き付くワイヤーグリッド → ブロックグリッチ / RGB 分離 → 帯電 → 閃光 → 暗転
// までを 1 パスで受け持つ。MRDive_Fade.shader と同じ枠組み（Tags / Blend / ZTest / Cull / instancing）。
//
// 設計メモ:
//  * テクスチャは一切使わない。模様はすべてオブジェクト空間の頂点方向から数式で組む。
//    球の UV は極で破綻するので、緯度 = dir.y / 経度 = atan2(dir.z, dir.x) を自前で作る。
//  * パススルー映像はコンポジタ側で合成されるため、シェーダーからは読めない。
//    そのため RGB 分離は「自前の模様を 3 回ずらしてサンプルする」ことで再現している。
//  * 合成は内部的に事前乗算アルファで積み上げ、最後に 1 回だけ通常アルファへ戻す。
//    こうすると「発光を重ねる」と「黒で覆う」を同じ式で扱える。
Shader "Livisor/MRDive/DigitalDissolve"
{
    Properties
    {
        _GridColor   ("Grid Color (電子ブルー)", Color) = (0.235, 0.784, 1, 1)
        _AccentColor ("Glitch Accent (マゼンタ)", Color) = (1, 0.235, 0.659, 1)
        _ScanColor   ("Scan Line Color", Color) = (0.55, 0.95, 1, 1)
        _ChargeColor ("Charge Color", Color) = (0.62, 0.88, 1, 1)
        _FlashColor  ("Flash Color", Color) = (0.82, 0.95, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1

        // --- Phase 1: 走査 ---
        _ScanPos       ("Scan Position (0=天頂 1=真下)", Float) = -0.2
        _ScanWidth     ("Scan Width", Range(0.002, 0.4)) = 0.045
        _ScanIntensity ("Scan Intensity", Range(0, 1)) = 0

        // --- 焼き付くグリッド ---
        _GridDensity   ("Grid Density", Float) = 18
        _GridWidth     ("Grid Line Width", Range(0.002, 0.1)) = 0.016
        _GridIntensity ("Grid Intensity", Range(0, 1)) = 0

        // --- Phase 2: グリッチ ---
        _Glitch     ("Glitch Amount", Range(0, 1)) = 0
        _BlockScale ("Glitch Block Scale", Float) = 12
        _BlockSeed  ("Glitch Block Seed", Float) = 0
        _RgbSplit   ("RGB Split (グリッド 1 周期に対する割合)", Range(0, 0.5)) = 0
        _NoiseFlash ("Noise Flash", Range(0, 1)) = 0
        _NoiseSeed  ("Noise Seed", Float) = 0

        // --- Phase 3 / 4 ---
        _Charge   ("Charge", Range(0, 1)) = 0
        _Flash    ("Flash", Range(0, 1)) = 0
        _Blackout ("Blackout", Range(0, 1)) = 0

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
        // グリッドの a=0.3 が実機では 0.09 相当まで薄まってしまう（Editor では気づけない）。
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
                float4 pos       : SV_POSITION;
                float3 objDir    : TEXCOORD0;   // オブジェクト空間の方向。模様はすべてこれが基準。
                float4 screenPos : TEXCOORD1;   // 画面中心からの距離を取る用。
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _GridColor;
            float4 _AccentColor;
            float4 _ScanColor;
            float4 _ChargeColor;
            float4 _FlashColor;
            float _Alpha;

            float _ScanPos;
            float _ScanWidth;
            float _ScanIntensity;

            float _GridDensity;
            float _GridWidth;
            float _GridIntensity;

            float _Glitch;
            float _BlockScale;
            float _BlockSeed;
            float _RgbSplit;
            float _NoiseFlash;
            float _NoiseSeed;

            float _Charge;
            float _Flash;
            float _Blackout;

            static const float kInvTwoPi = 0.15915494;

            // 三角関数を使わない安価なハッシュ（Hoskins 系）。モバイル GPU 向け。
            float Hash12(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // 等間隔の線。x の小数部が 0.5 の位置に線が立つ。
            // aa（1 画素ぶんの幅）は呼び出し側で fwidth から作って渡す。
            // 微分命令を分岐の中で使わないようにするため、あえて引数にしている。
            float GridLine(float x, float w, float aa)
            {
                float f = abs(frac(x) - 0.5) * 2.0;
                return 1.0 - smoothstep(w, w + aa, f);
            }

            // 経線だけを返す。緯線は経度に依存しないので RGB 分離の 3 回評価から外せる。
            float Meridian(float lon, float poleFade, float aa)
            {
                return GridLine(lon * _GridDensity, _GridWidth, aa) * poleFade;
            }

            // 事前乗算アルファでの「上に重ねる」。srcPre は色 * 不透明度。
            void Over(inout float3 pre, inout float a, float3 srcPre, float srcA)
            {
                float inv = 1.0 - srcA;
                pre = srcPre + pre * inv;
                a = srcA + a * inv;
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objDir = v.vertex.xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 dir = normalize(i.objDir);

                // 緯度 0 = 天頂 / 1 = 真下。走査線はこの軸を上から下へ走る。
                float lat = 0.5 - 0.5 * dir.y;
                float lon = atan2(dir.z, dir.x) * kInvTwoPi + 0.5;

                // 極では経線が 1 点に集まってモアレになるので、上下に近いほど落とす。
                float y2 = dir.y * dir.y;
                float y4 = y2 * y2;
                float poleFade = saturate(1.0 - y4 * y2);

                // アンチエイリアス幅は分岐の外で 1 回だけ求める（微分命令を分岐に入れないため）。
                // 経度の継ぎ目では fwidth が発散するので上限を、極では lat の微分が 0 に
                // 漸近して GridLine 内の smoothstep が 0 除算になるので下限を切る。
                float lonAA = clamp(fwidth(lon) * _GridDensity * 2.0, 1e-4, 0.45);
                float latAA = clamp(fwidth(lat) * _GridDensity * 0.7, 1e-4, 0.45);

                // ---- ブロックグリッチ: floor で量子化したセル単位に水平方向へずらす ----
                // 光過敏性への配慮で、明滅するブロックは全体の 20% までに抑える
                // （WCAG / Harding の目安は「視野の 25% 超が 3Hz 超で輝度変化しないこと」）。
                // 面積を削ったぶんは、水平のズレ量とマゼンタの濃さで密度感を補っている。
                float lonG = lon;
                float blockHit = 0.0;
                if (_Glitch > 0.001)
                {
                    // 経度は 2 倍して、セルが画面上でおおよそ正方形になるようにする。
                    float2 cell = floor(float2(lon * 2.0, lat) * _BlockScale);
                    float2 h = Hash22(cell + _BlockSeed);
                    blockHit = step(0.80, h.y) * _Glitch;
                    lonG += (h.x - 0.5) * 0.26 * blockHit;
                }

                // ---- 走査線が通過済みの範囲にだけグリッドが焼き付く ----
                float gridMask = 1.0 - smoothstep(_ScanPos - 0.03, _ScanPos, lat);
                float gi = _GridIntensity * gridMask;

                // 緯線は 1 回だけ評価する。水平線は水平方向にずらしても見た目が変わらないので、
                // RGB 分離の対象にしなくても実写的に正しい。
                float ring = GridLine(lat * _GridDensity * 0.35, _GridWidth, latAA) * poleFade;

                // 分離量はグリッド 1 周期に対する割合で受け取り、ここで経度へ換算する。
                // 経度で固定にすると _GridDensity が上がったとき分離量が 1 周期に迫り、
                // 色収差ではなく「隣の線と重なった別の線」に見えてしまうため。
                float splitLon = _RgbSplit / max(_GridDensity, 1.0);

                float mg = Meridian(lonG, poleFade, lonAA);
                float mr = mg;
                float mb = mg;
                if (_RgbSplit > 0.0005)
                {
                    mr = Meridian(lonG - splitLon, poleFade, lonAA);
                    mb = Meridian(lonG + splitLon, poleFade, lonAA);
                }

                // _RgbSplit の実用上の最大が 0.30（＝1 周期の 30%）なので、
                // 白寄せの係数はそこで 0.45 程度になるように取る。振り切ると青が抜けすぎる。
                float split01 = saturate(_RgbSplit * 1.5);
                float3 chan = saturate(float3(mr, mg, mb) + ring);
                // 分離中は信号が劣化した感じを出すため、基準色を白寄りに寄せてチャンネル差を見せる。
                float3 tint = lerp(_GridColor.rgb, float3(1.0, 1.0, 1.0), split01 * 0.6);

                float3 pre = chan * tint * gi;
                float a = max(max(chan.r, chan.g), chan.b) * gi * 0.85;

                // ---- Phase 1: 走査線そのもの（薄いシアンの帯） ----
                float band = 1.0 - saturate(abs(lat - _ScanPos) / max(_ScanWidth, 1e-4));
                band = band * band * (3.0 - 2.0 * band);
                float scanA = band * _ScanIntensity * 0.55;
                Over(pre, a, _ScanColor.rgb * scanA, scanA);

                // ---- Phase 2: ずれたブロックにマゼンタのアクセント ----
                // 明滅する面積を 20% に絞ったぶん、1 ブロックあたりは濃くする。
                float accA = blockHit * (0.12 + 0.42 * chan.g);
                Over(pre, a, _AccentColor.rgb * accA, accA);

                // ---- Phase 2: 白ノイズのフラッシュ ----
                // 全画面同時のストロボにしないため、画素単位のまだらにして被覆率を 45% で頭打ちにする。
                if (_NoiseFlash > 0.001)
                {
                    float n = Hash12(floor(float2(lon * 640.0, lat * 400.0)) + _NoiseSeed);
                    float hit = step(1.0 - 0.45 * _NoiseFlash, n);
                    float nA = hit * _NoiseFlash * 0.6;
                    Over(pre, a, float3(1.0, 1.0, 1.0) * nA, nA);
                }

                // 画面中心からの距離。ステレオでも各目の投影から正しく取れる。
                float2 suv = i.screenPos.xy / max(i.screenPos.w, 1e-4);
                float center = saturate(1.0 - distance(suv, float2(0.5, 0.5)) * 1.4);

                // ---- Phase 3: 視界全体が青白く帯電していく ----
                float chargeA = _Charge * (0.16 + 0.40 * center);
                Over(pre, a, _ChargeColor.rgb * chargeA, chargeA);

                // ---- Phase 4: LINK START の閃光。この演出で唯一の全画面ストロボ ----
                float flashA = saturate(_Flash) * (0.78 + 0.22 * center);
                Over(pre, a, _FlashColor.rgb * flashA, flashA);

                // ---- Phase 4: 黒へ落ちる ----
                float blackA = saturate(_Blackout);
                Over(pre, a, float3(0.0, 0.0, 0.0), blackA);

                a = saturate(a);
                // 事前乗算 → 通常アルファ。除算は最後の 1 回だけ。
                float3 col = min(pre / max(a, 1e-3), 4.0);
                return fixed4(col, saturate(a * _Alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
