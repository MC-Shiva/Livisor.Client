using System;
using UnityEngine;

/// <summary>Demo専用の雷。曲の秒数と、名前またはステージ基準の座標（m）を各行に指定する。</summary>
public static class DemoLightningSchedule
{
    public static Cue[] Create() => new Cue[]
    {
        new(3, -4, 0, 0),
        new(6, -2, 0, 0),
        new(9, 2, 0, 0),
        new(12, 4, 0, 0),
        new(15, "unity-chan"),
        new(18, -4, 0, 2),
        new(21, -2, 0, 2),
        new(24, 2, 0, 2),
        new(27, 4, 0, 2),
        new(30, "audience"),
        new(33, -5, 0, 6),
        new(36, -2.5f, 0, 6),
        new(39, 2.5f, 0, 6),
        new(42, 5, 0, 6),
        new(45, 3, 0, 2),
        new(48, -6, 0, 8),
        new(51, -3, 0, 8),
        new(54, 3, 0, 8),
        new(57, 6, 0, 8),
        new(60, "unity-chan"),
        new(63, -8, 0, 12),
        new(66, -4, 0, 12),
        new(69, 4, 0, 12),
        new(72, 8, 0, 12),
        new(75, "audience"),
        new(78, -10, 0, 16),
        new(81, -5, 0, 16),
        new(84, 5, 0, 16),
        new(87, 10, 0, 16),
        new(90, "stage"),
        new(93, -8, 0, 20),
        new(96, -3, 0, 20),
        new(99, 3, 0, 20),
        new(102, 8, 0, 20),
        new(105, "unity-chan"),
        new(108, -3, 0, -1),
        new(111, -1, 0, -1),
        new(114, 1, 0, -1),
        new(117, 3, 0, -1),
        new(120, "audience"),
        new(123, -5, 0, 3),
        new(126, -2, 0, 3),
        new(129, 2, 0, 3),
        new(132, 5, 0, 3),
        new(135, "stage"),
        new(138, -7, 0, 10),
        new(141, -3.5f, 0, 10),
        new(144, 3.5f, 0, 10),
        new(147, 7, 0, 10),
        new(150, "unity-chan"),
        new(153, -10, 0, 14),
        new(156, -6, 0, 14),
        new(159, 6, 0, 14),
        new(162, 10, 0, 14),
        new(165, "audience"),
        new(168, -12, 0, 18),
        new(171, -6, 0, 18),
        new(174, 6, 0, 18),
        new(177, 12, 0, 18),
        new(180, "stage"),
        new(183, -4, 0, 4),
        new(186, 4, 0, 4),
        new(189, -8, 0, 9),
        new(192, 8, 0, 9),
        new(195, "unity-chan"),
        new(198, -10, 0, 12),
        new(201, -4, 0, 1),
        new(204, 4, 0, 1),
        new(207, 10, 0, 12),
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
