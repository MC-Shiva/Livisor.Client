using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// ダイブ演出で多用するイージングと区間リマップ。
    /// すべて 0..1 を受けて 0..1 を返し、範囲外はクランプする。
    /// </summary>
    public static class DiveEase
    {
        public static float Linear(float t) => Mathf.Clamp01(t);

        public static float InQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        public static float OutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u;
        }

        public static float InCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        public static float InQuint(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * t * t;
        }

        public static float OutQuint(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Pow(1f - t, 5f);
        }

        public static float InExpo(float t)
        {
            t = Mathf.Clamp01(t);
            return t <= 0f ? 0f : Mathf.Pow(2f, 10f * (t - 1f));
        }

        public static float OutExpo(float t)
        {
            t = Mathf.Clamp01(t);
            return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        }

        public static float InOutExpo(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t < 0.5f
                ? Mathf.Pow(2f, 20f * t - 10f) * 0.5f
                : (2f - Mathf.Pow(2f, -20f * t + 10f)) * 0.5f;
        }

        public static float InSine(float t) => 1f - Mathf.Cos(Mathf.Clamp01(t) * Mathf.PI * 0.5f);

        public static float OutSine(float t) => Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.5f);

        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * Mathf.Clamp01(t)) - 1f) * 0.5f;

        /// <summary>行き過ぎてから戻る。ポータルが開く瞬間などに。</summary>
        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t);
            float c3 = overshoot + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + overshoot * u * u;
        }

        /// <summary>弾んで収束する。UI のポップインなどに。</summary>
        public static float OutElastic(float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            const float period = (2f * Mathf.PI) / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * period) + 1f;
        }

        /// <summary>
        /// t が区間 [a, b] のどこにいるかを 0..1 で返す。演出のフェーズ分割に使う。
        /// 例) Span(t, 0.25f, 0.55f) は t=0.25 で 0、t=0.55 で 1。
        /// </summary>
        public static float Span(float t, float a, float b)
        {
            if (b - a <= Mathf.Epsilon) return t >= b ? 1f : 0f;
            return Mathf.Clamp01((t - a) / (b - a));
        }

        /// <summary>center を頂点に width で立ち上がって落ちる 0..1 の山。閃光に。</summary>
        public static float Pulse(float t, float center, float width)
        {
            if (width <= Mathf.Epsilon) return 0f;
            float d = Mathf.Abs(t - center) / width;
            return d >= 1f ? 0f : 1f - d * d * (3f - 2f * d);
        }

        /// <summary>0..1 を 0→1→0 に折り返す。</summary>
        public static float PingPong01(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - Mathf.Abs(t * 2f - 1f);
        }

        /// <summary>
        /// 決まった周波数で不規則に暴れる 0..1。グリッチのちらつきに。
        /// 乱数を使わないので、同じ t なら必ず同じ値になる。
        /// </summary>
        public static float Flicker(float t, float frequency, float seed = 0f)
        {
            float x = t * frequency + seed;
            float n = Mathf.Sin(x * 12.9898f) * 43758.5453f;
            return Mathf.Clamp01(n - Mathf.Floor(n));
        }
    }
}
