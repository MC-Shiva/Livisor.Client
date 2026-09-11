// 単色のベール。暗転・白飛び・着地フェードに使う最小構成。
// MRDive のオーバーレイ用シェーダーはすべてこの形をひな形にしている。
Shader "Livisor/MRDive/Fade"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
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

        // RGB と A でブレンド式を分ける。パススルーは Underlay 合成なので、アイバッファの
        // A チャンネルがそのまま「現実をどれだけ隠すか」のマスクになる。A にも SrcAlpha を
        // 掛けるとアルファが二乗され、実機でだけ効果が薄くなる（Editor では気づけない）。
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed _Alpha;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return fixed4(_Color.rgb, saturate(_Color.a * _Alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}
