using System;
using System.Collections.Generic;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;

/// <summary>
/// TimelineAction の一覧をメモリに保持し、曲の再生位置に達したものを一度ずつ実行する。
/// Load で一覧を読み込み、Advance に再生位置（秒）と実行処理を渡す。
/// データの取得元は呼び出し側が決める。UnityEngine や Shared の事前定義には依存しない。
/// DemoSceneController は Shared の定義を渡し、Admin の予約は TimelineReceiver が別に扱う。
///
/// 再生位置は進む一方である前提（シークも曲の再スタートも現状の操作には無い）。
/// 位置が戻っても発火し直さない。曲を再スタートするときは、このクラスのインスタンスを作り直す。
/// </summary>
public sealed class TimelineActionPlayback
{
    private readonly List<(double seconds, TimelineAction action)> _actions = new();
    private int _cursor;
    private double _lastSeconds = double.NegativeInfinity;

    public int Count => _actions.Count;

    /// <summary>
    /// 一覧をメモリに読み込み、時刻順に並べる。不正な時刻の行は捨てる。null は空として扱う。
    /// 一覧を読み直しても発火済みをやり直さないよう、
    /// 現在の再生位置以前のアクションは飛ばす。
    /// </summary>
    public void Load(IEnumerable<TimelineAction> actions)
    {
        _actions.Clear();
        if (actions != null)
        {
            foreach (var action in actions)
            {
                if (action != null && PlaybackTime.TryParse(action.Time, out var time))
                    _actions.Add((time.TotalSeconds, action));
            }
        }

        _actions.Sort((a, b) => a.seconds.CompareTo(b.seconds));

        _cursor = 0;
        while (_cursor < _actions.Count && _actions[_cursor].seconds <= _lastSeconds)
            _cursor++;
    }

    /// <summary>
    /// 再生位置を進め、到達したアクションを順に fire へ渡す。
    /// 負の値は「まだ鳴っていない／一時停止中」として何もしない。
    /// </summary>
    public void Advance(double musicSeconds, Action<TimelineAction> fire)
    {
        if (musicSeconds < 0)
            return;

        if (musicSeconds > _lastSeconds)
            _lastSeconds = musicSeconds;

        while (_cursor < _actions.Count && _actions[_cursor].seconds <= musicSeconds)
        {
            var action = _actions[_cursor].action;
            _cursor++;
            fire(action);
        }
    }
}
