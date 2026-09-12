using System.Collections.Generic;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メモリ上の一覧を再生する TimelineActionPlayback と Shared の定義（DefaultActionSet）のテスト。
/// 音源もシーンも使わないので、バッチ実行できる。
/// バッチ実行: unity run . --timeout 300 -- -nographics -executeMethod TestTimelineActionPlayback.Run -logFile Logs/default-actions-check.log
/// エディタ上: メニュー Livisor / Tests / Timeline Action Playback
/// 失敗した項目は Console にエラーで出る。バッチでは終了コード 1 になる。
/// </summary>
public static class TestTimelineActionPlayback
{
    private static readonly List<string> Failures = new();

    [MenuItem("Livisor/Tests/Timeline Action Playback")]
    public static void Run()
    {
        Failures.Clear();

        var fired = new List<string>();
        void Fire(TimelineAction a) => fired.Add(a.Value.Text);
        TimelineAction Effect(string time, string name) => new() { Time = time, Action = ActionType.Effect, Value = name };

        var playback = new TimelineActionPlayback();
        playback.Load(new[] { Effect("00:00:10:00", "b"), Effect("00:00:05:00", "a"), Effect("bad", "x") });
        Check(playback.Count == 2, "不正な時刻の行は捨てる");

        playback.Advance(-1, Fire);
        Check(fired.Count == 0, "音楽が鳴る前（負の位置）は発火しない");
        playback.Advance(4.9, Fire);
        Check(fired.Count == 0, "相対時間の前は発火しない");
        playback.Advance(7.0, Fire);
        Check(string.Join(",", fired) == "a", "時刻順に、到達した分だけ発火する");
        playback.Advance(7.0, Fire);
        Check(fired.Count == 1, "同じ位置で二重に発火しない");

        playback.Load(new[] { Effect("00:00:05:00", "a"), Effect("00:00:10:00", "b") });
        playback.Advance(8.0, Fire);
        Check(fired.Count == 1, "同じ定義を読み直しても発火済みをやり直さない");
        playback.Advance(30.0, Fire);
        Check(string.Join(",", fired) == "a,b", "まとめて到達した分は順に発火する");
        playback.Advance(30.0, Fire);
        Check(fired.Count == 2, "全件発火後は何もしない");
        playback.Advance(0.2, Fire);
        playback.Advance(20.0, Fire);
        Check(string.Join(",", fired) == "a,b", "再生位置が戻っても発火し直さない（戻す操作は現状無い）");

        playback.Load(new[] { Effect("00:00:05:00", "a"), Effect("00:00:10:00", "b"),
            Effect("00:00:03:00", "past"), Effect("00:00:40:00", "later") });
        playback.Advance(30, Fire);
        Check(string.Join(",", fired) == "a,b,past", "追加された過去時刻の予約だけをその場で実行する");
        playback.Load(new[] { Effect("00:00:05:00", "a"), Effect("00:00:10:00", "b") });
        playback.Advance(50, Fire);
        Check(fired.Count == 3, "取り消された未来の予約は実行しない");
        playback.Load(new[] { Effect("00:00:05:00", "a"), Effect("00:00:10:00", "b"), Effect("00:00:03:00", "past") });
        playback.Advance(50, Fire);
        Check(string.Join(",", fired) == "a,b,past,past", "取消後の再登録を実行する");
        playback.Load(new[] { Effect("00:01:00:00", "default"), Effect("00:01:00:00", "added") });
        playback.Advance(-1, Fire);
        Check(fired.Count == 4, "停止中はキューを進めない");
        playback.Advance(60, Fire);
        Check(string.Join(",", fired) == "a,b,past,past,default,added", "同じ時刻は入力順に実行する");

        playback.Load(null);
        Check(playback.Count == 0, "null の定義は空として扱う");

        var duplicate = new TimelineActionPlayback();
        var duplicateCount = 0;
        var same = Effect("00:00:01:00", "same");
        duplicate.Load(new[] { same, same });
        duplicate.Advance(1, _ => duplicateCount++);
        Check(duplicateCount == 1, "一覧内の同一内容は1回だけ実行する");
        duplicate.Load(new[] { same });
        duplicate.Advance(2, _ => duplicateCount++);
        Check(duplicateCount == 1, "追加分を消してもデフォルト側に同じ内容が残れば再実行しない");

        var defaults = new TimelineActionPlayback();
        var defined = DefaultActionSet.Create();
        defaults.Load(defined);
        Check(defaults.Count == defined.Length && defined.Length > 0, "Shared のデフォルト演出はすべて HH:mm:ss:ff で読める");
        foreach (var a in defined)
            Check(a.Action != ActionType.Effect || a.Value.Text == EffectNames.ConfettiOn || a.Value.Text == EffectNames.ConfettiOff || a.Value.Text == EffectNames.Lightning || a.Value.Text == EffectNames.SilverStreamer,
                $"既知の演出名だけを使う: {a.Value.Text}");
        foreach (var a in defined)
            Check(ActionValueKindMap.KindOf(a.Action) == a.Value.Kind, $"管理画面の値種別と一致する: {a.Action}");

        if (Failures.Count == 0)
            Debug.Log("[TestTimelineActionPlayback] PASS");
        else
            Debug.LogError($"[TestTimelineActionPlayback] FAIL ({Failures.Count})\n{string.Join("\n", Failures)}");

        if (Application.isBatchMode)
            EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) Failures.Add(what);
    }
}
