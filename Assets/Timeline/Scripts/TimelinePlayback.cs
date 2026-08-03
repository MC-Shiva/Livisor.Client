using System.Collections.Generic;
using Livisor.Shared.DTO;

/// <summary>
/// タイムライン再生の純粋ロジック（UnityEngine 非依存・テスト可能）。
/// 受信した配列を「先頭アクション基準の相対オフセット」に変換する。
/// </summary>
public static class TimelinePlayback
{
    /// <summary>スケジュールの 1 要素: 先頭からの相対遅延(秒)と、その時刻に実行するアクション。</summary>
    public readonly struct ScheduledAction
    {
        public double DelaySeconds { get; }
        public TimelineAction Action { get; }

        public ScheduledAction(double delaySeconds, TimelineAction action)
        {
            DelaySeconds = delaySeconds;
            Action = action;
        }
    }

    /// <summary>
    /// 先頭アクションの time を基準(0 秒)とした相対オフセットのスケジュールを作る。
    /// </summary>
    public static IReadOnlyList<ScheduledAction> BuildSchedule(TimelineAction[] actions)
    {
        var schedule = new List<ScheduledAction>();
        if (actions == null || actions.Length == 0) return schedule;

        double baseSeconds = ParseTimeToSeconds(actions[0].Time);
        foreach (var action in actions)
        {
            double delay = ParseTimeToSeconds(action.Time) - baseSeconds;
            if (delay < 0) delay = 0;
            schedule.Add(new ScheduledAction(delay, action));
        }

        return schedule;
    }

    /// <summary>
    /// "HH:mm:ss:ff" を秒に変換する。第 4 フィールドはセンチ秒(1/100 秒)として扱う。
    /// （フレーム単位に変えたい場合はここを調整する）
    /// </summary>
    public static double ParseTimeToSeconds(string time)
    {
        if (string.IsNullOrEmpty(time)) return 0;

        var parts = time.Split(':');
        if (parts.Length < 4) return 0;
        if (!int.TryParse(parts[0], out var h)) return 0;
        if (!int.TryParse(parts[1], out var m)) return 0;
        if (!int.TryParse(parts[2], out var s)) return 0;
        if (!int.TryParse(parts[3], out var ff)) return 0;

        return h * 3600 + m * 60 + s + ff / 100.0;
    }
}
