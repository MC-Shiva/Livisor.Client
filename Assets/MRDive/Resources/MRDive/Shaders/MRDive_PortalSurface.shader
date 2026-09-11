// PORTAL RIFT のポータル面。正面の板 1 枚に「亀裂 → 円形ポータル」を描く。
//
// 形は _Extent (半幅, 半高) ひとつで決まる。板の UV を -1..1 に直した p に対して
//   q = p / _Extent,  d = |q|  … d < 1 が内側
// とするので、_Extent を (極細, 縦長) から (半径, 半径) へ動かすだけで
// 縦の亀裂が横に開いて円になる。C# 側はこのベクトルを毎フレーム流し込むだけでよい。
//
// 縁の太さやグローは「板の p 空間」で一定にしたいので、楕円の勾配から
// 符号付き距離 sd を近似して使う。これをやらないと亀裂のとき横方向のグローが
// 1px に潰れる（q 空間は極端に異方的なため）。
//
// MRDive_Fade.shader をひな形にしているので Tags / Blend / ZTest / Cull [_Cull] /
// instancing マクロの並びはそちらと完全に同じ。Single Pass Instanced 前提。
Shader "Livisor/MRDive/Portal Surface"
{
    Properties
    {
        _CoreColor ("Accent Color", Color) = (0.373, 0.906, 1, 1)
        _DeepColor ("Deep Color", Color) = (0.03, 0.05, 0.20, 1)
        _HotColor ("Hot Core Color", Color) = (1, 1, 1, 1)

        _Extent ("Extent (half width, half height)", Vector) = (0.004, 0.03, 0, 0)
        _Open ("Open", Range(0, 1)) = 0
        _Spin ("Spin Phase", Float) = 0
        _Swirl ("Swirl", Range(0, 2)) = 0
        _Dust ("Star Dust", Range(0, 1)) = 0

        _RimWidth ("Rim Width", Range(0.001, 0.4)) = 0.012
        _GlowRange ("Glow Range", Range(0.005, 1)) = 0.09
        _Glow ("Glow Intensity", Range(0, 4)) = 1.6
        _EdgeNoise ("Edge Noise", Range(0, 0.4)) = 0

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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _CoreColor;
            fixed4 _DeepColor;
            fixed4 _HotColor;
            float4 _Extent;
            float _Open;
            float _Spin;
            float _Swirl;
            float _Dust;
            float _RimWidth;
            float _GlowRange;
            float _Glow;
            float _EdgeNoise;
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

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 p = i.uv * 2.0 - 1.0;            // 板の中心を原点にした -1..1
                float2 ext = max(_Extent.xy, 1e-4);
                float2 q = p / ext;
                float d = length(q);                     // 1 が輪郭
                float dd = max(d, 1e-3);

                // 楕円の勾配から p 空間での符号付き距離を近似する。
                // 円 (ext.x == ext.y) のときは厳密に |p| - R に一致する。
                float2 gv = q / ext;
                float gl = max(length(gv), 1e-5);
                float sd = (d - 1.0) * dd / gl;          // 正 = 外側

                // 輪郭方向。中心でのゼロ除算を避ける。
                // dd のおかげで dir は有限だが、q が真に 0 の画素だけは atan2(0, 0) になる。
                // x にごく小さな下駄を履かせて未定義を踏まないようにする。
                float2 dir = q / dd;
                float ang = atan2(dir.y, dir.x + 1e-8); // この shader で唯一の三角関数

                // --- 縁の不規則な揺れ ---
                // 単位円上を noise で舐めるので角度について自然に周期的。継ぎ目が出ない。
                float wob = 0.0;
                if (_EdgeNoise > 0.0001)
                {
                    wob = (vnoise(dir * 3.2 + _Spin * 0.35) - 0.5) * _EdgeNoise;
                }
                float e = sd - wob;

                float soft = max(_RimWidth * 0.6, 0.0015);
                float inside = 1.0 - smoothstep(-soft, soft, e);

                // --- 内側: 深い藍の渦 ---
                // 腕の本数を整数にしてあるので frac の折り返しが ang の継ぎ目と一致する。
                float arms = 3.0;
                float spiral = (ang * 0.15915494 + 0.5) * arms + dd * 1.35
                             + _Swirl / max(dd, 0.22) + _Spin;
                float arm = 1.0 - abs(frac(spiral) * 2.0 - 1.0);
                arm *= arm;

                float3 vortex = lerp(_DeepColor.rgb, _CoreColor.rgb,
                                     arm * (0.25 + 0.75 * saturate(1.0 - dd)));

                // 中心へ吸い込まれる星屑。travel の位相を進めると内側へ流れる。
                if (_Dust > 0.001)
                {
                    float lanes = 30.0;
                    float a = (ang * 0.15915494 + 0.5) * lanes;
                    float id = fmod(floor(a), lanes);
                    float rnd = hash11(id);

                    float lane = frac(a) - 0.5;
                    float lw = 0.10 + rnd * 0.18;
                    float lmask = saturate(1.0 - abs(lane) / lw);

                    float travel = frac(dd * (1.5 + rnd) + _Spin * (0.55 + rnd * 0.6) + rnd);
                    float spark = smoothstep(0.80, 1.0, travel) * lmask;
                    // smoothstep の端は必ず昇順にする（降順は環境によって未定義）。
                    spark *= smoothstep(0.06, 0.45, dd) * (1.0 - smoothstep(0.72, 1.02, dd));

                    vortex += _HotColor.rgb * spark * _Dust * 1.3;
                }

                // 中心の強い光点
                float core = 1.0 - smoothstep(0.0, 0.30, dd);
                vortex += _HotColor.rgb * core * core * (0.6 + _Open * 1.4);

                // 亀裂のうちは白熱した芯。開くにつれて渦へ変わる。
                float3 interior = lerp(_HotColor.rgb, vortex, smoothstep(0.06, 0.55, _Open));

                // --- 縁のリムと外側グロー ---
                float rim = saturate(1.0 - abs(e) / max(_RimWidth, 1e-4));
                rim *= rim;

                // pow を使わずに裾の長い減衰を作る。内側には乗せない
                // （乗せると渦の上にシアンの一様な膜が張ってしまう）。
                float od = max(e, 0.0) / max(_GlowRange, 1e-4);
                float glow = 1.0 / (1.0 + od * od * 6.0);
                glow *= glow;
                glow *= 1.0 - inside;

                float3 rimCol = lerp(_CoreColor.rgb, _HotColor.rgb, rim * 0.7) * _Glow;

                // 外側は現実をうっすら沈めるだけ。実際の色はリムとグローが乗せる。
                float3 col = lerp(_DeepColor.rgb * 0.15, interior, inside);
                col += rimCol * (rim + glow * 0.35);

                float alpha = saturate(max(inside, rim * 0.95 + glow * 0.5));

                // --- Phase 4: 白い閃光 ---
                if (_Flash > 0.001)
                {
                    col = lerp(col, _HotColor.rgb, _Flash);
                    alpha = max(alpha, _Flash);
                }

                return fixed4(col, saturate(alpha * _Alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
