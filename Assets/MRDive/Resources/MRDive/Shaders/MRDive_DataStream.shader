// DIGITAL DISSOLVE の Phase 3「データストリーム」レイヤー。
// 視線方向の消失点へ向かってトンネル状のリング／スポークが伸び、
// 光の粒と短い線分が奥から手前へ流れ込んでくる。
//
// 設計メモ:
//  * 座標は「視線方向への透視投影」で作る。C# から _Forward / _Right / _Up に
//    頭の姿勢（このオブジェクトのローカル空間）を入れてもらう。
//    画面 UV ではなくオブジェクト空間の方向から組むので、Single Pass Instanced でも
//    左右の目がまったく同じ world 方向の模様を見る＝ステレオが破綻しない。
//    （画面中心は目ごとに光軸とずれるため、UV 基準にすると両眼で消失点が食い違う）
//  * _Forward は演出開始時の姿勢で固定し、毎フレーム更新しない。追従させると
//    消失点が視線に貼り付き、全視野の放射オプティカルフローが常に中心から湧く形になって
//    強い vection（自己運動錯覚）＝ VR 酔いを誘発するため。頭を振れば流れから目を外せる。
//  * pow() は使わず自乗の繰り返しで尾を作る。三角関数は atan2 の 1 回だけ。
//  * Blend は RGB と A で式を分けた通常アルファ。パススルーのアンダーレイ合成は
//    アイバッファのアルファをマスクとして見るため、A まで SrcAlpha を掛けると
//    アルファが二乗されて実機でだけ効果が薄くなる。
Shader "Livisor/MRDive/DataStream"
{
    Properties
    {
        _StreamColor ("Stream Color (電子ブルー)", Color) = (0.235, 0.784, 1, 1)
        _CoreColor   ("Core Color (白青)", Color) = (0.85, 0.96, 1, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1

        _Forward ("View Forward (Object Space)", Vector) = (0, 0, 1, 0)
        _Right   ("View Right (Object Space)", Vector)   = (1, 0, 0, 0)
        _Up      ("View Up (Object Space)", Vector)      = (0, 1, 0, 0)

        _Intensity   ("Intensity", Range(0, 1)) = 0
        _Scroll      ("Scroll", Float) = 0
        _TunnelGrid  ("Tunnel Grid", Range(0, 1)) = 0
        _RingScale   ("Ring Scale", Float) = 0.7
        _SpokeCount  ("Spoke Count", Float) = 22
        _StreakCount ("Streak Count", Float) = 88
        _LineWidth   ("Line Width", Range(0.002, 0.2)) = 0.03
        _CoreGlow    ("Core Glow", Range(0, 1)) = 0

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
        // Underlay 合成のマスクが薄まって実機でだけ効果が弱くなる（Editor では気づけない）。
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
                float4 pos    : SV_POSITION;
                float3 objDir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _StreamColor;
            float4 _CoreColor;
            float _Alpha;

            float4 _Forward;
            float4 _Right;
            float4 _Up;

            float _Intensity;
            float _Scroll;
            float _TunnelGrid;
            float _RingScale;
            float _SpokeCount;
            float _StreakCount;
            float _LineWidth;
            float _CoreGlow;

            static const float kInvTwoPi = 0.15915494;

            float2 Hash22(float2 p)
            {
                float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // aa（1 画素ぶんの幅）は呼び出し側で fwidth から作って渡す。
            // 消失点付近では微分が発散するので、呼び出し側で上限を切っている。
            float GridLine(float x, float w, float aa)
            {
                float f = abs(frac(x) - 0.5) * 2.0;
                return 1.0 - smoothstep(w, w + aa, f);
            }

            // 消失点から放射状に伸びるレーンを 1 本ずつ流す。
            // azi でレーンを選び、depth（画面半径の逆数＝奥行き）方向に位相を進める。
            float Streaks(float azi, float depth, float scroll, float count, float seed)
            {
                float slot = azi * count + seed;
                float id = floor(slot);
                float f = frac(slot) - 0.5;
                float2 h = Hash22(float2(id, seed + 1.0));

                // レーンごとに太さを散らす。
                float thick = 0.10 + 0.30 * h.x;
                float lane = 1.0 - smoothstep(thick * 0.6, thick, abs(f));

                // 手前へ来るほど間隔が開く＝加速して見える。
                float trav = frac(depth * 0.55 + scroll * (0.75 + 0.5 * h.y) + h.x * 7.31);
                float k = saturate(1.0 - trav);
                float k2 = k * k;
                float k4 = k2 * k2;
                float k8 = k4 * k4;

                float head = k8 * k8;          // 先端の粒（k^16）
                float tail = k4 * 0.30;        // 短い尾
                return lane * (head + tail) * (0.5 + 0.5 * h.y);
            }

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
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 dir = normalize(i.objDir);

                float z = dot(dir, _Forward.xyz);
                float front = smoothstep(0.05, 0.45, z);

                // 視線方向へ透視投影する。トンネルの消失点が p = (0,0)。
                float2 p = float2(dot(dir, _Right.xyz), dot(dir, _Up.xyz)) / max(z, 0.2);
                float rad = length(p);
                float azi = atan2(p.y, p.x) * kInvTwoPi + 0.5;

                // 画面半径の逆数が奥行きに比例する（半径 R の筒を軸方向に覗いた形）。
                float depth = 1.0 / (rad + 0.12);

                // 消失点付近は情報密度が上がりすぎて必ずジャギるので落とす。
                float centerFade = smoothstep(0.05, 0.32, rad);
                // 周辺視野は vection（自己運動錯覚）への寄与が大きい。放射状のフローを
                // 視野の中心寄りに閉じ込めて、VR 酔いのトリガーを減らしている。
                // rad 0.55 ≒ 視線から 29 度、rad 1.6 ≒ 58 度。
                float edgeFade = 1.0 - smoothstep(0.55, 1.6, rad);
                float mask = _Intensity * front * centerFade * edgeFade;

                // ---- トンネルのリングとスポーク ----
                // 上限は消失点付近の微分の発散よけ、下限は GridLine 内の smoothstep が
                // 0 除算で NaN を吐かないようにするため。
                float ringAA = clamp(fwidth(depth) * abs(_RingScale) * 2.0, 1e-4, 0.45);
                float spokeAA = clamp(fwidth(azi) * _SpokeCount * 2.0, 1e-4, 0.45);

                float ring = GridLine(depth * _RingScale + _Scroll, _LineWidth, ringAA);
                float spoke = GridLine(azi * _SpokeCount, _LineWidth * 1.6, spokeAA)
                            * smoothstep(0.12, 0.65, rad);
                float grid = saturate(ring * 0.9 + spoke * 0.45) * _TunnelGrid;

                // ---- 流れ込む粒と線分。2 層重ねて規則性を崩す ----
                float s1 = Streaks(azi, depth, _Scroll, _StreakCount, 0.0);
                // 2 層目のレーン数は必ず整数にする。非整数だと azi が 1→0 に折り返す
                // 経線でレーンが 1 本だけ途中で切れて継ぎ目が見える。
                // （_StreakCount は C# 側で偶数に丸めてから渡している）
                float s2 = Streaks(azi, depth, _Scroll * 1.7, _StreakCount * 0.5, 3.7);
                float streak = saturate(s1 + s2 * 0.7);

                float3 pre = float3(0.0, 0.0, 0.0);
                float a = 0.0;

                float gridA = grid * mask * 0.5;
                Over(pre, a, _StreamColor.rgb * gridA, gridA);

                // 明るい先端ほど白青（コア色）に寄せる。
                float strA = streak * mask;
                float3 strCol = lerp(_StreamColor.rgb, _CoreColor.rgb, streak);
                Over(pre, a, strCol * strA, strA);

                // ---- 引き込まれる先の光点 ----
                float core = saturate(1.0 - rad * 2.0);
                core = core * core;
                float coreA = core * _CoreGlow * front * 0.8;
                Over(pre, a, _CoreColor.rgb * coreA, coreA);

                a = saturate(a);
                float3 col = min(pre / max(a, 1e-3), 4.0);
                return fixed4(col, saturate(a * _Alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
