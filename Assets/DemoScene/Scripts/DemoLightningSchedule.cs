using System;
using UnityEngine;

/// <summary>Demo専用の雷。曲の秒数と、名前またはステージ基準の座標（m）を各行に指定する。</summary>
public static class DemoLightningSchedule
{
    public static Cue[] Create() => new Cue[]
    {
        new(15, "unity-chan"),
        new(30, "audience"),
        new(45, "stage"),
        new(60, "unity-chan"),
        new(75, "audience"),
        new(90, "stage"),
        new(105, "unity-chan"),
        new(120, "audience"),
        new(135, "stage"),
        new(150, "unity-chan"),
        new(165, "audience"),
        new(180, "stage"),
        new(195, "unity-chan"),
        new(210, "audience"),
    };

    public sealed class Cue
    {
        public double Seconds { get; }
        public string Target { get; }
        public Vector3 LocalPosition { get; }

        public Cue(double seconds, string target)
        {
            if (target != "unity-chan" && target != "audience" && target != "stage")
                throw new ArgumentException($"不明な雷の対象: {target}", nameof(target));
            Seconds = ValidateSeconds(seconds);
            Target = target;
        }

        // 例: new(30, 3, 0, 2) は30秒にステージ基準の (3, 0, 2) mへ落とす。
        public Cue(double seconds, float x, float y, float z)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new ArgumentException("雷の座標には有限の数値を指定してください。");
            Seconds = ValidateSeconds(seconds);
            LocalPosition = new Vector3(x, y, z);
        }

        static double ValidateSeconds(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds), "雷の時刻には0以上の有限の秒数を指定してください。");
            return seconds;
        }
    }
}
