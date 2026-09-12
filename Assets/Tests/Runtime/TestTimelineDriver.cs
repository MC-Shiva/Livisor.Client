using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;

/// <summary>
/// 疎通確認の管理者役。Sandbox シーンの受信側（TimelineReceiver）と同じサーバーに繋ぎ、
/// 再生 → 予約 → 音量 → 停止 の順に送る。受信側のログと自分に届いた通知を数えて合否を出す。
/// 合格: 受信側が transport を 3 回以上受け取り、即時の音量 42 と予約の音量 30 が反映され、管理者役にも 42 が届くこと。
/// </summary>
public class TestTimelineDriver : MonoBehaviour
{
    private const string RoomId = "room1";
    private readonly List<string> _receiverLogs = new();
    private int _adminTransport;
    private readonly List<int> _adminVolumes = new();

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private void OnLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Receiver]") || message.StartsWith("[Play]"))
            _receiverLogs.Add(message);
    }

    private IEnumerator Start()
    {
        var config = Resources.FindObjectsOfTypeAll<ServerConfig>();
        var address = config.Length > 0 ? config[0].ServerAddress : "http://localhost:5210";
        Debug.Log($"[Smoke] server={address}");

        yield return new WaitForSecondsRealtime(4f); // 受信側の接続を待つ

        var task = RunAdminAsync(address);
        var deadline = Time.realtimeSinceStartup + 30f;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
            yield return null;

        yield return new WaitForSecondsRealtime(1f); // 受信側の最後の通知を待つ

        var error = task.IsFaulted ? task.Exception?.GetBaseException().Message : (task.IsCompleted ? null : "timeout");
        var receiverTransport = _receiverLogs.FindAll(l => l.StartsWith("[Receiver] transport")).Count;
        var receiverVolume42 = _receiverLogs.Exists(l => l.Contains("VOLUME -> 42"));
        // 予約の発火結果（音量 30）は受信側が状態同期へ書き戻すので、管理者役には 42 のあとに 30 も届く。
        var receiverVolume30 = _receiverLogs.Exists(l => l.Contains("VOLUME -> 30"));
        var ok = error == null && receiverTransport >= 3 && receiverVolume42 && receiverVolume30 && _adminTransport >= 3 && _adminVolumes.Contains(42);

        var result = "{\n" +
            $"  \"ok\": {(ok ? "true" : "false")},\n" +
            $"  \"error\": {(error == null ? "null" : "\"" + error.Replace("\"", "'") + "\"")},\n" +
            $"  \"receiverTransportEvents\": {receiverTransport},\n" +
            $"  \"receiverGotVolume42\": {(receiverVolume42 ? "true" : "false")},\n" +
            $"  \"adminTransportEvents\": {_adminTransport},\n" +
            $"  \"adminVolumes\": [{string.Join(", ", _adminVolumes)}],\n" +
            $"  \"receiverGotScheduledVolume30\": {(receiverVolume30 ? "true" : "false")},\n" +
            $"  \"receiverLogs\": [{string.Join(", ", _receiverLogs.ConvertAll(l => "\"" + l.Replace("\"", "'") + "\""))}]\n" +
            "}\n";
        var path = Path.Combine(Application.dataPath, "..", "SmokeTestResult.json");
        File.WriteAllText(path, result);
        Debug.Log($"[Smoke] {(ok ? "PASS" : "FAIL")} -> {path}\n{result}");

#if UNITY_EDITOR
        if (Application.isBatchMode)
            UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
        else
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private async Task RunAdminAsync(string address)
    {
        var admin = new RoomClient();
        admin.TransportChanged += _ => _adminTransport++;
        admin.StateChanged += p =>
        {
            foreach (var e in p.Entries)
                if (e.Key == RoomStateKeys.Volume) _adminVolumes.Add(e.Value.Number);
        };
        await admin.ConnectAsync(address, RoomId);
        Debug.Log("[Smoke] admin connected");

        await admin.PlayAsync();
        await admin.ScheduleActionsAsync(new TimelineAction { Time = "00:00:01:00", Action = ActionType.VolumeChange, Value = 30 });
        await admin.PublishStateAsync(new RoomStateEntry { Key = RoomStateKeys.Volume, Value = ActionValue.From(42) });
        await Task.Delay(2500); // 予約（1 秒後の音量 30）が受信側で発火するのを待つ
        await admin.StopAsync();
        await Task.Delay(500);
        await admin.DisposeAsync();
    }
}
