using System.Collections.Generic;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;

/// <summary>
/// タイムライン再生のスケジュール計算（UnityEngine 非依存・テスト可能）。
/// 受信した配列を「先頭アクション基準の相対オフセット」に変換する。
/// 絶対時刻方式だと、配信時に既に過ぎた time のアクションが一斉発火し、
/// 曲のブツ切れや音量の急変が起きる。相対オフセットにして間隔を保つことでこれを防ぐ。
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
    /// パースルールは <see cref="PlaybackTime"/>（Server と共通）に委譲する。不正な値は 0 を返す。
    /// </summary>
    public static double ParseTimeToSeconds(string time)
        => PlaybackTime.TryParse(time, out var t) ? t.TotalSeconds : 0;
}
