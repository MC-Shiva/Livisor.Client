using System;
using System.Collections.Generic;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;

/// <summary>
/// デフォルト演出（Issue #22）の発火計算。UnityEngine 非依存。
/// 音楽の再生位置（秒）を渡すと、相対時間に達したアクションを時刻順に一度ずつ返す。
///
/// 基準はサーバー時刻ではなく音源の再生位置。「曲の開始から何秒後」に忠実で、
/// 一時停止中は再生位置が進まないので発火も止まり、再開すれば続きから発火する。
/// Admin の予約は TimelineReceiver が別に扱い、こちらとは独立。
/// DemoScene は Shared の定義を直接読む。
///
/// 再生位置は進む一方である前提（シークも曲の再スタートも現状の操作には無い）。
/// 位置が戻っても発火し直さない。曲を再スタートするときは、このクラスのインスタンスを作り直す。
/// </summary>
public sealed class DefaultActionPlayback
{
    private readonly List<(double seconds, TimelineAction action)> _actions = new();
    private int _cursor;
    private double _lastSeconds = double.NegativeInfinity;

    public int Count => _actions.Count;

    /// <summary>
    /// 定義を読み込む。時刻の昇順に並べ替え、不正な時刻の行は捨てる。null は空として扱う。
    /// 同じ定義を読み直しても発火済みをやり直さないよう、
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
