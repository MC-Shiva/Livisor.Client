// PORTAL RIFT の全天球レイヤー。頭を包む球の内側に描く。
// 担当するのは「ポータルの外側で起きること」= 中心から外へ流れる放射状の速度線 /
// 視界を閉じるビネット / 虚空のゆらぎ / 全画面の閃光。
//
// MRDive_Fade.shader をひな形にしているので、Tags / ZTest / Cull [_Cull] /
// instancing マクロの並びはそちらと完全に同じ。Single Pass Instanced 前提。
// Blend だけはパススルーの Underlay 合成に合わせてアルファ分離にしてある（下のコメント参照）。
//
// ステレオの約束:
//   - 高周波の放射模様（速度線）は画面 UV ではなく world 方向から組む。
//     スクリーン UV の中心は非対称フラスタムのぶん目ごとに光軸とずれるため、
//     そこを放射中心にすると左右でレーンの向きが食い違って融像できない。
//   - 低周波の同心円状減衰（ビネット）は目ごとのずれが見えないので screenPos でよい。
//
// モバイル GPU 向けの約束:
//   - テクスチャを一切使わない（数式のみ）
//   - fbm は 3 オクターブ固定
//   - 三角関数は atan2 が 1 回だけ。ハッシュは sin を使わない版を使う
//   - 各ブロックは uniform 値で分岐させる。フェーズ外では丸ごと飛ぶ
Shader "Livisor/MRDive/Portal Rift"
{
    Properties
    {
        _CoreColor ("Accent Color", Color) = (0.373, 0.906, 1, 1)
        _DeepColor ("Deep Color", Color) = (0.02, 0.03, 0.12, 1)
        _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)

        // 速度線の放射中心を作る頭の姿勢。C# がこのオブジェクトのローカル空間で毎フレーム入れる。
        _Forward ("View Forward (Object Space)", Vector) = (0, 0, 1, 0)
        _Right ("View Right (Object Space)", Vector) = (1, 0, 0, 0)
        _Up ("View Up (Object Space)", Vector) = (0, 1, 0, 0)

        _Streak ("Speed Lines", Range(0, 1)) = 0
        _StreakPhase ("Speed Line Phase", Float) = 0
        _Warp ("Void Turbulence", Range(0, 1)) = 0
        _Vignette ("Vignette", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0

        _Alpha ("Alpha", Range(0, 1)) = 1
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

        // RGB は通常アルファ、A だけ (One, OneMinusSrcAlpha) の分離ブレンド。
        // パススルーは Underlay 合成でアイバッファの A をそのままマスクに使うため、
        // A にも SrcAlpha を掛けると A_dst = A_src^2 + A_dst*(1-A_src) とアルファが二乗され、
        // 半透明区間の効果が実機でだけ薄くなる（エディタでは気づけない）。
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
                float4 screenPos : TEXCOORD0;
                float3 dir : TEXCOORD1;   // オブジェクト空間の方向（球の中心から頂点へ）
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _CoreColor;
            fixed4 _DeepColor;
            fixed4 _FlashColor;
            float4 _Forward;
            float4 _Right;
            float4 _Up;
            float _Streak;
            float _StreakPhase;
            float _Warp;
            float _Vignette;
            float _Flash;
            fixed _Alpha;

            // ------------------------------------------------------------------
            // 乱数まわり。三角関数を使わない版（Dave Hoskins 系）。
            // ------------------------------------------------------------------
            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 2D バリューノイズ。格子 4 点の補間。
            float vnoise(float2 p)
            {
                float2 ip = floor(p);
                float2 fp = frac(p);
                fp = fp * fp * (3.0 - 2.0 * fp);

                float a = hash21(ip);
                float b = hash21(ip + float2(1.0, 0.0));
                float c = hash21(ip + float2(0.0, 1.0));
                float d = hash21(ip + float2(1.0, 1.0));
                return lerp(lerp(a, b, fp.x), lerp(c, d, fp.x), fp.y);
            }

            // 3 オクターブ固定の fbm。これ以上は Quest で割に合わない。
            float fbm3(float2 p)
            {
                float v = vnoise(p) * 0.50;
                v += vnoise(p * 2.03) * 0.30;
                v += vnoise(p * 4.11) * 0.20;
                return v;
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.pos);
                // 球プリミティブなので頂点座標がそのまま中心からの方向になる。
                // 球の UV は極で破綻するため、模様はこの方向ベクトルから組む。
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // 画面中心からの距離。screenR は画面の上下端でおよそ 1.0。
                // ビネットは低周波の同心円状減衰なので、目ごとの光軸ずれが出ても
                // 融像に影響しない。ここだけは screenPos のままでよい。
                float2 sp = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                float2 c = sp - 0.5;
                c.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float screenR = length(c) * 2.0;

                // 速度線とゆらぎはオブジェクト空間の方向から組む。
                float3 dir = normalize(i.dir);

                float3 col = _DeepColor.rgb;
                float alpha = 0.0;

                // --- 虚空のビネット: 周辺から視界を閉じる ---
                if (_Vignette > 0.001)
                {
                    float v = smoothstep(0.15, 0.95, screenR) * _Vignette;
                    alpha = max(alpha, v);
                }

                // --- 虚空のゆらぎ ---
                // 方向ベクトルを線形に 2D へ落としてから fbm を取る。緯度経度に直さないので
                // 極の破綻も継ぎ目もない。球は頭の回転を追わないため模様は world 固定に見える。
                if (_Warp > 0.001)
                {
                    float2 np = float2(dir.x + dir.z, dir.y + dir.z * 0.5) * 2.6;
                    np += float2(_StreakPhase * 0.12, -_StreakPhase * 0.07);

                    float cloud = smoothstep(0.40, 0.92, fbm3(np)) * _Warp;
                    col = lerp(col, lerp(_DeepColor.rgb, _CoreColor.rgb, 0.30), cloud);
                    alpha = max(alpha, cloud * 0.55);
                }

                // --- 放射状の速度線: 中心から外へ流れる ---
                if (_Streak > 0.001)
                {
                    // 放射中心は「画面中心」ではなく「頭の forward」。
                    // Quest の片目投影は非対称フラスタムなので、スクリーン UV の (0.5, 0.5) は
                    // 目の光軸と一致せず左右で数度ずれる。44 本もの高周波レーンをそこから
                    // 放射させると左右の目で別方向にレーンが走り、脳が融像できない。
                    // world 方向を視線基底へ透視投影すれば、両目が必ず同じ方向の模様を見る。
                    float z = dot(dir, _Forward.xyz);
                    float front = smoothstep(0.05, 0.45, z);          // 後ろ半球では投影が破綻する
                    float2 vp = float2(dot(dir, _Right.xyz), dot(dir, _Up.xyz)) / max(z, 0.2);
                    float rad = length(vp);                            // 視線からの tan 半径
                    float ang = atan2(vp.y, vp.x);                     // この shader で唯一の三角関数

                    float lanes = 44.0;
                    // atan2 の戻り -PI..PI を 0..lanes へ。端は整数幅で折り返すので継ぎ目が出ない。
                    float a = (ang * 0.15915494 + 0.5) * lanes;
                    float id = fmod(floor(a), lanes);
                    float rnd = hash11(id);

                    // レーンごとに太さ・速度・位相をばらす
                    // （変数名に line は使えない。HLSL の予約語なので beam にしている）
                    float lane = frac(a) - 0.5;
                    float width = 0.12 + rnd * 0.26;
                    float beam = saturate(1.0 - abs(lane) / width);
                    beam *= beam;   // 芯を細く

                    float travel = frac(rad * (1.35 + rnd * 1.2) - _StreakPhase * (0.85 + rnd * 0.9) + rnd);
                    float head = smoothstep(0.45, 1.0, travel);   // 進行方向に尾を引く

                    float s = beam * head * front * smoothstep(0.05, 0.45, rad) * _Streak;
                    col = lerp(col, lerp(_CoreColor.rgb, float3(1.0, 1.0, 1.0), head * 0.55) * 1.35, s);
                    alpha = max(alpha, s);
                }

                // --- 閃光: Phase 1 の微かな明滅と Phase 4 のホワイトアウト兼用 ---
                if (_Flash > 0.001)
                {
                    col = lerp(col, _FlashColor.rgb, _Flash);
                    alpha = max(alpha, _Flash);
                }

                return fixed4(col, saturate(alpha * _Alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
