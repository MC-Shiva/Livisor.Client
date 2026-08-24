using System.Collections.Generic;
using Livisor.Shared.DTO;

/// <summary>
/// 配信中のタイムラインの進行状況を計算する。UnityEngine に依存しない純粋なロジック。
/// スケジュール計算自体は <see cref="TimelinePlayback"/> に委譲し、ここでは
/// 経過時間から「今どのアクションまで発火済みか」を引き当てるだけを担う。
/// </summary>
public class BroadcastSession
{
    private readonly IReadOnlyList<TimelinePlayback.ScheduledAction> _schedule;
    private readonly long _broadcastAtMs;

    public double TotalSeconds { get; }

    public BroadcastSession(TimelineAction[] actions, long broadcastAtMs)
    {
        _schedule = TimelinePlayback.BuildSchedule(actions);
        _broadcastAtMs = broadcastAtMs;
        TotalSeconds = _schedule.Count > 0 ? _schedule[_schedule.Count - 1].DelaySeconds : 0;
    }

    /// <summary>
    /// nowMs 時点で最後に発火済みのアクションの index。まだ 1 件も発火していなければ -1。
    /// </summary>
    public int ActiveIndex(long nowMs)
    {
        var elapsedSeconds = (nowMs - _broadcastAtMs) / 1000.0;

        var index = -1;
        for (var i = 0; i < _schedule.Count; i++)
        {
            if (_schedule[i].DelaySeconds > elapsedSeconds)
                break;

            index = i;
        }

        return index;
    }

    /// <summary>最終アクションの発火時刻を過ぎていれば true。</summary>
    public bool IsFinished(long nowMs) => (nowMs - _broadcastAtMs) / 1000.0 >= TotalSeconds;

    /// <summary>nowMs 時点の経過秒数。表示用に [0, TotalSeconds] へクランプする。</summary>
    public double ElapsedSeconds(long nowMs)
    {
        var elapsed = (nowMs - _broadcastAtMs) / 1000.0;
        if (elapsed < 0) elapsed = 0;
        if (elapsed > TotalSeconds) elapsed = TotalSeconds;
        return elapsed;
    }
}
