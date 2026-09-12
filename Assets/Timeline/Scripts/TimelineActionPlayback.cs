using System;
using System.Collections.Generic;
using System.Linq;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;

/// <summary>
/// 一覧をメモリに保持し、曲の再生位置に達したアクションを一度ずつ実行する。
/// Demo は Shared の定義、Live は Server から受信した一覧を Load に渡す。
/// 曲を最初からやり直すときは、このクラスのインスタンスを作り直す。
/// </summary>
public sealed class TimelineActionPlayback
{
    private List<(double seconds, TimelineAction action)> _actions = new();
    // ponytail: 同じ時刻・種類・値は同じ予約と扱う。重複予約を別々に実行する場合は予約IDを追加する。
    private readonly HashSet<(double seconds, ActionType type, ActionValue value)> _fired = new();
    private int _cursor;
    private double _startSeconds;
    private bool _startSet;
    private bool _restorePending;

    public int Count => _actions.Count;

    /// <summary>初回の開始位置を確定する。Load や一時停止・再開では変更しない。</summary>
    public void BeginAt(double seconds)
    {
        if (_startSet)
            return;
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _startSeconds = seconds;
        _restorePending = seconds > 0;
        _startSet = true;
    }

    /// <summary>一覧を置き換える。実行済みは維持し、取り消された予約だけ忘れる。</summary>
    public void Load(IEnumerable<TimelineAction> actions)
    {
        _actions.Clear();
        if (actions != null)
            foreach (var action in actions)
                if (action != null && PlaybackTime.TryParse(action.Time, out var time))
                    _actions.Add((time.TotalSeconds, action));

        // OrderBy は同じ時刻の入力順を保つ。デフォルト演出の後に追加予約を実行する。
        _actions = _actions.OrderBy(action => action.seconds).ToList();
        _fired.IntersectWith(_actions.Select(a => (a.seconds, a.action.Action, a.action.Value)));
        _cursor = 0;
    }

    /// <summary>到達した分を実行する。負の位置は開始前・一時停止中として扱う。</summary>
    public void Advance(double musicSeconds, Action<TimelineAction> fire, Action<TimelineAction> restoreState = null)
    {
        if (double.IsNaN(musicSeconds) || double.IsInfinity(musicSeconds) || musicSeconds < 0)
            return;

        if (_restorePending && _actions.Count > 0)
        {
            _restorePending = false;
            RestoreInitialState(restoreState ?? fire);
        }

        while (_cursor < _actions.Count && _actions[_cursor].seconds <= musicSeconds)
        {
            var action = _actions[_cursor++];
            // 再受信や後から追加された過去予約も、カットした区間からは発火させない。
            if (action.seconds < _startSeconds)
                continue;
            if (_fired.Add((action.seconds, action.action.Action, action.action.Value)))
                fire(action.action);
        }
    }

    private void RestoreInitialState(Action<TimelineAction> restore)
    {
        var confetti = -1;
        var volume = -1;
        for (var i = 0; i < _actions.Count && _actions[i].seconds < _startSeconds; i++)
        {
            var action = _actions[i].action;
            if (action.Action == ActionType.Effect && action.Value.Kind == ActionValueKind.Text
                && (action.Value.Text == EffectNames.ConfettiOn || action.Value.Text == EffectNames.ConfettiOff))
                confetti = i;
            if (action.Action == ActionType.VolumeChange && action.Value.Kind == ActionValueKind.Number
                && action.Value.Number >= 0 && action.Value.Number <= 100)
                volume = i;
        }

        // 最終状態だけを、元の定義順で復元する。雷・銀テープ・再生/停止は呼ばない。
        for (var i = 0; i <= Math.Max(confetti, volume); i++)
            if (i == confetti || i == volume)
                restore(_actions[i].action);
    }
}
