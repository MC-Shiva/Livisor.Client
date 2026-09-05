using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// 管理者画面の全機能を順に操作し、(a) 画面の表示、(b) 受信側（Sandbox）の反応、(c) 観測役の接続に届いた実際の値、
/// の 3 つを突き合わせる。観測役は管理者画面とは別の RoomClient で、サーバーが配信した TransportState と
/// 状態の差分をそのまま記録する。これで「送った情報が正しいか」を画面の外から確かめる。
/// </summary>
public class SmokeTestAdminFullDriver : MonoBehaviour
{
    private readonly List<string> _receiverLogs = new();
    private readonly List<string> _checks = new();   // "OK: ..." / "NG: ..."
    private readonly List<TransportState> _transports = new();
    private readonly List<int> _volumes = new();
    private IRoomClient _observer;

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private void OnLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Receiver]") || message.StartsWith("[Play]"))
            _receiverLogs.Add(message);
    }

    private void Check(bool cond, string what) => _checks.Add((cond ? "OK: " : "NG: ") + what);

    private int ReceiverCount(string contains) => _receiverLogs.Count(l => l.Contains(contains));

    private IEnumerator Start()
    {
#if UNITY_EDITOR
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
            "Assets/Scenes/Sandbox.unity", new LoadSceneParameters(LoadSceneMode.Additive));
#else
        yield return SceneManager.LoadSceneAsync("Sandbox", LoadSceneMode.Additive);
#endif
        var doc = FindFirstObjectByType<UIDocument>();
        if (doc == null) { Finish("UIDocument が無い（Admin シーンで実行すること）"); yield break; }
        var root = doc.rootVisualElement;
        var address = root.Q<TextField>("server-address").value;

        // 観測役を先に繋ぐ
        _observer = new RoomClient();
        _observer.TransportChanged += s => _transports.Add(s);
        _observer.StateChanged += p => { foreach (var e in p.Entries) if (e.Key == RoomStateKeys.Volume) _volumes.Add(e.Value.Number); };
        var connectTask = _observer.ConnectAsync(address, root.Q<TextField>("room-id").value);
        yield return new WaitUntil(() => connectTask.IsCompleted);
        if (connectTask.IsFaulted) { Finish("観測役が接続できない: " + connectTask.Exception?.GetBaseException().Message); yield break; }
        yield return new WaitForSecondsRealtime(2f); // 受信側の接続を待つ

        // 1. 入力チェック: アドレス空で CONNECT
        var addrField = root.Q<TextField>("server-address");
        addrField.value = "";
        Click(root, "connect-button");
        yield return null;
        Check(root.Q<Label>("status-label").text.Contains("ServerAddress"), "アドレス空の CONNECT は拒否される");
        Check(!root.Q<Button>("play-button").enabledSelf, "未接続時は PLAY が押せない");
        addrField.value = address;

        // 2. CONNECT
        Click(root, "connect-button");
        yield return WaitUntilLabel(root, "connection-status", "接続済み", 10f);
        Check(root.Q<Label>("connection-status").text.Contains("接続済み"), "CONNECT で接続済みになる");
        Check(root.Q<Button>("connect-button").text == "DISCONNECT", "接続後はボタンが DISCONNECT になる");
        Check(root.Q<Button>("play-button").enabledSelf && root.Q<Button>("schedule-button").enabledSelf, "接続後は操作ボタンが押せる");
        yield return new WaitForSecondsRealtime(0.5f);
        var n0 = _transports.Count;

        // 3. PLAY
        Click(root, "play-button");
        yield return WaitUntil(() => _transports.Count > n0, 5f);
        var t1 = _transports.LastOrDefault();
        Check(t1 != null && t1.Playing && t1.ScheduledAction == null && t1.StartedAtServerMs > 0, "PLAY: 観測役に playing=true・予約なし・開始時刻ありが届く");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(root.Q<Label>("transport-status").text.StartsWith("再生中"), "PLAY: 画面が『再生中』になる");
        Check(ReceiverCount("[Play] PLAY") >= 1, "PLAY: 受信側が再生する");

        // 4. PLAY を再度 → 開始時刻が動かない
        var n1 = _transports.Count;
        Click(root, "play-button");
        yield return WaitUntil(() => _transports.Count > n1, 5f);
        var t2 = _transports.LastOrDefault();
        Check(t2 != null && t1 != null && t2.Playing && t2.StartedAtServerMs == t1.StartedAtServerMs, "再生中の PLAY は開始時刻を変えない");

        // 5. 音量 150 → 拒否
        var v0 = _volumes.Count;
        root.Q<IntegerField>("volume-field").value = 150;
        Click(root, "volume-button");
        yield return new WaitForSecondsRealtime(0.5f);
        Check(root.Q<Label>("status-label").text.Contains("0〜100") && _volumes.Count == v0, "音量 150 は送らずに拒否される");

        // 6. 音量 42
        root.Q<IntegerField>("volume-field").value = 42;
        Click(root, "volume-button");
        yield return WaitUntil(() => _volumes.Count > v0, 5f);
        yield return new WaitForSecondsRealtime(0.3f);
        Check(_volumes.LastOrDefault() == 42, "音量 42: 観測役に volume=42 が届く");
        Check(root.Q<Label>("volume-status").text.Contains("42"), "音量 42: 画面に『音量 42』が出る");
        Check(ReceiverCount("VOLUME -> 42") >= 1, "音量 42: 受信側の音量が変わる");

        // 7. 予約: 先頭行 00:00:01:00 volumeChange 30、2 行目 00:00:05:00 play=false → 先頭だけ送られる
        var list = root.Q<ListView>("timeline-list");
        var row0 = list.GetRootElementForIndex(0);
        Check(row0 != null, "タイムラインの先頭行がある");
        if (row0 != null)
        {
            row0.Q<IntegerField>("seconds-field").value = 1;
            row0.Q<DropdownField>("action-dropdown").value = "VolumeChange";
            row0.Q<IntegerField>("number-field").value = 30;
        }
        var addButton = list.Q<Button>("timeline-add-button");
        var rowsBefore = list.itemsSource?.Count ?? -1;
        if (addButton != null) Click(list, "timeline-add-button");
        yield return null;
        var rowsAfter = list.itemsSource?.Count ?? -1;
        Check(rowsAfter == rowsBefore + 1, "＋ ADD CUE で行が増える");
        var row1 = list.GetRootElementForIndex(1);
        if (row1 != null)
        {
            row1.Q<IntegerField>("seconds-field").value = 5;
            row1.Q<DropdownField>("action-dropdown").value = "Play";
            row1.Q<Toggle>("bool-field").value = false;
        }
        var n2 = _transports.Count; var v1 = _volumes.Count; var fired30 = ReceiverCount("VOLUME -> 30");
        Click(root, "schedule-button");
        yield return WaitUntil(() => _transports.Count > n2, 5f);
        var t3 = _transports.LastOrDefault();
        Check(t3 != null && t3.ScheduledAction != null && t3.ScheduledAction.Time == "00:00:01:00"
              && t3.ScheduledAction.Action == ActionType.VolumeChange && t3.ScheduledAction.Value.Number == 30,
              "SCHEDULE: 観測役に予約 00:00:01:00 volumeChange=30 が届く（先頭行の内容）");
        yield return new WaitForSecondsRealtime(0.3f);
        Check(row1 == null || root.Q<Label>("status-label").text.Contains("他 1 件"), "SCHEDULE: 2 行あるときは『他 1 件は送りません』と出る");
        Check(root.Q<Label>("transport-status").text.Contains("予約 00:00:01:00"), "SCHEDULE: 画面に予約が出る");
        yield return WaitUntil(() => ReceiverCount("VOLUME -> 30") > fired30, 4f);
        Check(ReceiverCount("VOLUME -> 30") > fired30, "SCHEDULE: 受信側で約 1 秒後に音量 30 が発火する");
        yield return WaitUntil(() => _volumes.Count > v1, 3f);
        Check(_volumes.LastOrDefault() == 30, "発火した音量 30 が状態同期に書き戻され、観測役に届く");

        // 8. CANCEL
        var n3 = _transports.Count;
        Click(root, "cancel-schedule-button");
        yield return WaitUntil(() => _transports.Count > n3, 5f);
        var t4 = _transports.LastOrDefault();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(t4 != null && t4.ScheduledAction == null && t4.Playing, "CANCEL: 予約だけ消え、再生は続く");
        Check(root.Q<Label>("transport-status").text.Contains("予約なし"), "CANCEL: 画面が『予約なし』になる");

        // 9. SCHEDULE → STOP: 停止しても予約は残る
        var n4 = _transports.Count;
        Click(root, "schedule-button");
        yield return WaitUntil(() => _transports.Count > n4, 5f);
        var n5 = _transports.Count;
        Click(root, "stop-button");
        yield return WaitUntil(() => _transports.Count > n5, 5f);
        var t5 = _transports.LastOrDefault();
        yield return new WaitForSecondsRealtime(0.3f);
        Check(t5 != null && !t5.Playing && t5.ScheduledAction != null && t5.StartedAtServerMs == 0, "STOP: playing=false・開始時刻 0・予約は残る");
        Check(root.Q<Label>("transport-status").text.StartsWith("停止中") && root.Q<Label>("transport-status").text.Contains("予約 00:00:01:00"), "STOP: 画面が『停止中 / 予約あり』になる");
        Check(ReceiverCount("[Play] STOP") >= 2, "STOP: 受信側が停止する");

        // 10. 再度 PLAY → 残っていた予約が新しい開始時刻から再び発火する
        var fired30b = ReceiverCount("VOLUME -> 30"); var n6 = _transports.Count;
        Click(root, "play-button");
        yield return WaitUntil(() => _transports.Count > n6, 5f);
        var t6 = _transports.LastOrDefault();
        Check(t6 != null && t6.Playing && t1 != null && t6.StartedAtServerMs > t1.StartedAtServerMs && t6.ScheduledAction != null, "再 PLAY: 新しい開始時刻になり予約は残る");
        yield return WaitUntil(() => ReceiverCount("VOLUME -> 30") > fired30b, 4f);
        Check(ReceiverCount("VOLUME -> 30") > fired30b, "再 PLAY: 予約が再び発火する");

        // 11. STOP → DISCONNECT
        Click(root, "stop-button");
        yield return new WaitForSecondsRealtime(0.8f);
        Click(root, "connect-button"); // 接続中は DISCONNECT
        yield return WaitUntilLabel(root, "connection-status", "Not Connected", 5f);
        Check(root.Q<Label>("connection-status").text.Contains("Not Connected"), "DISCONNECT で未接続になる");
        Check(!root.Q<Button>("play-button").enabledSelf && root.Q<Label>("transport-status").text == "-", "DISCONNECT で操作ボタンが無効になり表示が消える");

        // 12. 再接続
        Click(root, "connect-button");
        yield return WaitUntilLabel(root, "connection-status", "接続済み", 10f);
        Check(root.Q<Label>("connection-status").text.Contains("接続済み"), "再接続できる");
        yield return new WaitForSecondsRealtime(0.5f);
        Check(root.Q<Label>("transport-status").StartsWith("停止中"), "再接続直後に現在値（停止中）が表示される");

        Finish(null);
    }

    private void Finish(string fatal)
    {
        var ng = _checks.Count(c => c.StartsWith("NG"));
        var ok = fatal == null && ng == 0;
        var result = "{\n" +
            $"  \"ok\": {(ok ? "true" : "false")},\n" +
            $"  \"fatal\": {(fatal == null ? "null" : "\"" + fatal.Replace("\"", "'") + "\"")},\n" +
            $"  \"checks\": [\n    {string.Join(",\n    ", _checks.Select(c => "\"" + c.Replace("\"", "'") + "\""))}\n  ],\n" +
            $"  \"observerTransports\": [{string.Join(", ", _transports.Select(t => $"\"playing={t.Playing} startedAt={t.StartedAtServerMs} scheduled={(t.ScheduledAction == null ? "none" : t.ScheduledAction.Time + " " + t.ScheduledAction.Action + "=" + t.ScheduledAction.Value)}\""))}],\n" +
            $"  \"observerVolumes\": [{string.Join(", ", _volumes)}],\n" +
            $"  \"receiverLogs\": [{string.Join(", ", _receiverLogs.Select(l => "\"" + l.Replace("\"", "'") + "\""))}]\n" +
            "}\n";
        var path = Path.Combine(Application.dataPath, "..", "SmokeTestAdminFullResult.json");
        File.WriteAllText(path, result);
        Debug.Log($"[SmokeAdminFull] {(ok ? "PASS" : "FAIL")} ({_checks.Count - ng}/{_checks.Count} OK) -> {path}\n{result}");
        _ = _observer?.DisposeAsync();
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
        else UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private static void Click(VisualElement root, string name)
    {
        var button = root.Q<Button>(name);
        if (button == null) { Debug.LogWarning($"[SmokeAdminFull] button '{name}' not found"); return; }
        using var evt = ClickEvent.GetPooled();
        evt.target = button;
        var invoke = typeof(Clickable).GetMethod("Invoke",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, new[] { typeof(EventBase) }, null);
        if (invoke != null) invoke.Invoke(button.clickable, new object[] { evt });
        else button.SendEvent(evt);
    }

    private static IEnumerator WaitUntil(System.Func<bool> cond, float timeoutSeconds)
    {
        var deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline && !cond()) yield return null;
    }

    private static IEnumerator WaitUntilLabel(VisualElement root, string name, string contains, float timeoutSeconds)
        => WaitUntil(() => root.Q<Label>(name).text.Contains(contains), timeoutSeconds);
}

internal static class LabelExtensions
{
    public static bool StartsWith(this Label label, string prefix) => label != null && label.text.StartsWith(prefix);
}
