// LIQUID DIVE Phase 1 の波紋リング。全天球レイヤーに同心円を描く。
//
// ■ 波紋の中心をワールド方向で持つ理由
//   レイヤーは OverlayFollow.PositionOnly なので回転は追わない。_Origin にワールド方向を渡し、
//   ピクセル方向との角距離でリングを組むことで、頭を振っても波紋の中心が現実に貼り付いたまま
//   になる（スクリーン空間で組むと視線に貼り付いて嘘になる）。
//
// ■ 角距離に弦長を使う理由
//   acos は 1 ピクセルあたり地味に高い。弦長 |a - b| = 2 sin(θ/2) は sqrt 1 回で求まり、
//   0..PI の範囲で単調、かつ 90 度くらいまでは θ とほぼ線形なので、リングの等間隔感を保てる。
//
// ■ 「屈折風」の正体
//   Quest のパススルー映像はコンポジタ側で合成されるため、アプリのシェーダーからは
//   サンプルできない（GrabPass も効かない）。そのため本物の UV 歪みは原理的に不可能。
//   代わりに「明るい峰 + 暗い谷」を隣り合わせに並べ、峰の内側できらめきのパターンを
//   ねじることで、レンズが通り過ぎたように見せている。
Shader "Livisor/MRDive/Ripple"
{
    Properties
    {
        _RingColor ("Ring Color", Color) = (1, 1, 1, 1)
        _CoreColor ("Core Color", Color) = (0.498, 0.902, 1.0, 1)   // #7FE6FF

        _Origin ("Origin Direction (world)", Vector) = (0, 0, 1, 0)
        _Progress ("Progress", Range(0, 1)) = 0
        _Alpha ("Alpha", Range(0, 1)) = 1

        // 以下 4 つは C# から設定しない。弦長の尺度「0 = 中心 / 1 = 60度 / 1.41 = 真横 / 2 = 真後ろ」
        // を前提にした既定値なので、変えるときはこの尺度で考えること。
        _Reach ("Reach (chord 0-2)", Float) = 2.0       // 進行 1 で真後ろまで抜ける
        _RingFreq ("Ring Frequency", Float) = 16        // 全天で約 5 本。尾の中には常時 2 本ほど見える
        _RingSpeed ("Ring Speed", Float) = 5            // 進行中にリングが約 0.8 周期ぶん後ろへ流れる
        _Trail ("Trail (chord 0-2)", Float) = 0.9       // 先端から後ろ 0.9（約 55 度ぶん）まで残る
        _Refract ("Refraction", Range(0, 1)) = 0.6
        _FlowTime ("Flow Time", Float) = 0

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

        // アルファは分離指定にする。パススルーは Underlay 合成でアイバッファの A が
        // そのままパススルーのマスクになるため、A にも SrcAlpha を掛けると A が二乗されて
        // 実機でだけ効果が薄くなる（エディタでは気付けない）。
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

            fixed4 _RingColor;
            fixed4 _CoreColor;

            float4 _Origin;
            float _Progress;
            fixed _Alpha;

            float _Reach;
            float _RingFreq;
            float _RingSpeed;
            float _Trail;
            fixed _Refract;
            float _FlowTime;

            // リングの稜線を尖らせる。pow は指数が変数だと高いので、整数固定の掛け算にしてある。
            float Pow5(float x) { float x2 = x * x; return x2 * x2 * x; }
            float Pow3(float x) { return x * x * x; }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                // 平行移動を含めず回転・スケールだけ通すと、そのままワールド方向になる。
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 d = normalize(i.dir);
                float3 o = normalize(_Origin.xyz);

                // 弦長を角距離の代わりに使う。0（中心）〜2（真後ろ）。
                // 2 - 2cosθ は 0..4 を取るので saturate では 60 度で頭打ちになる。
                // sqrt の負数ガードだけが目的なので max で 0 側だけ止めること。
                float chord = sqrt(max(0.0, 2.0 - 2.0 * dot(d, o)));

                // 波面の先端。_Progress = 1 で _Reach まで到達する。
                float front = _Progress * _Reach;
                float rel = front - chord;                          // 正なら通過済み

                // 先端は鋭く立ち上がり、後方へゆっくり消える。
                float lead = smoothstep(0.0, 0.05, rel);
                float tail = 1.0 - smoothstep(_Trail * 0.30, _Trail, rel);
                float win = lead * tail;

                // 同心円。峰（明るい）と谷（暗い）を別々に取り出す。
                // 指数は整数に固定して掛け算に落としてある（モバイルでは pow より安い）。
                float s = sin(chord * _RingFreq - _Progress * _RingSpeed);
                float crest  = Pow5(saturate( s)) * win;
                float valley = Pow3(saturate(-s)) * win * _Refract * 0.5;

                // 峰の内側できらめきをねじる ＝ 屈折して見えるところ。
                float shimmer = (sin(chord * 29.0 + _FlowTime * 2.6 + crest * _Refract * 6.0) * 0.5 + 0.5)
                              * (sin(chord * 11.0 - _FlowTime * 1.7) * 0.5 + 0.5);
                shimmer *= win * _Refract * 0.18;

                // 中心から生まれる最初の一閃。
                float birth = (1.0 - smoothstep(0.0, 0.22, _Progress)) * exp2(-chord * chord * 26.0);

                // 峰は白、芯に近いほどシアン。谷は深い青を薄く乗せて「暗く落ちた」ように見せる。
                float3 bright = lerp(_CoreColor.rgb, _RingColor.rgb, saturate(crest * 1.5));
                float3 dark = _CoreColor.rgb * 0.10;

                // crest と valley は排他（sin の正負）なので、被覆で重み付き平均すれば選択になる。
                // 重みの合計は saturate 前の値で割ること。割ってから飽和させないと峰が白飛びする。
                float raw = crest + valley + shimmer + birth;
                float3 col = (bright * (crest + shimmer + birth) + dark * valley) / max(raw, 1e-4);

                return fixed4(col, saturate(raw) * _Alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
