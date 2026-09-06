using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// 管理者画面（Admin シーン）のボタンを実際に押して、受信側（Sandbox シーン）に届くことを確かめる。
/// Admin シーンを開いた状態で動かし、Sandbox を追加読み込みして受信側を同居させる。
/// 操作の順: 接続 → PLAY → VOLUME 42 → 先頭行を「00:00:01:00 volumeChange 30」にして SCHEDULE → 停止。
/// 合格: 受信側が transport を 3 回以上受け取り、音量 42 と予約の音量 30 が反映されること。
/// </summary>
public class SmokeTestAdminDriver : MonoBehaviour
{
    private readonly List<string> _receiverLogs = new();
    private readonly List<string> _steps = new();

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private void OnLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Receiver]") || message.StartsWith("[Play]"))
            _receiverLogs.Add(message);
    }

    private IEnumerator Start()
    {
        // 受信側を同じプロセスに置く。Admin シーンの UI と同居しても互いに干渉しない。
#if UNITY_EDITOR
        // Build Settings に入っていないシーンも読めるよう、エディタ専用の読み込みを使う。
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
            "Assets/Scenes/Sandbox.unity", new LoadSceneParameters(LoadSceneMode.Additive));
#else
        yield return SceneManager.LoadSceneAsync("Sandbox", LoadSceneMode.Additive);
#endif
        yield return new WaitForSecondsRealtime(3f); // 受信側の接続を待つ

        var doc = FindFirstObjectByType<UIDocument>();
        string error = null;
        if (doc == null)
        {
            error = "UIDocument が見つからない（Admin シーンで実行すること）";
        }
        else
        {
            var root = doc.rootVisualElement;
            Click(root, "connect-button"); _steps.Add("connect");
            yield return WaitLabel(root, "connection-status", "接続済み", 10f);
            if (!root.Q<Label>("connection-status").text.Contains("接続済み"))
                error = "接続できなかった: " + root.Q<Label>("status-label").text;
            else
            {
                Click(root, "play-button"); _steps.Add("play");
                yield return new WaitForSecondsRealtime(0.5f);

                root.Q<IntegerField>("volume-field").value = 42;
                Click(root, "volume-button"); _steps.Add("volume 42");
                yield return new WaitForSecondsRealtime(0.5f);

                // 先頭行を編集して予約する。行は ListView が実体化しているものを取る。
                var list = root.Q<ListView>("timeline-list");
                var row = list.GetRootElementForIndex(0);
                if (row == null)
                    error = "タイムラインの先頭行が見つからない";
                else
                {
                    row.Q<IntegerField>("seconds-field").value = 1;
                    row.Q<DropdownField>("action-dropdown").value = "VolumeChange";
                    row.Q<IntegerField>("number-field").value = 30;
                    Click(root, "schedule-button"); _steps.Add("schedule 00:00:01:00 volumeChange 30");
                    yield return new WaitForSecondsRealtime(2.5f);

                    Click(root, "stop-button"); _steps.Add("stop");
                    yield return new WaitForSecondsRealtime(1f);
                    _steps.Add("status: " + root.Q<Label>("status-label").text + " / transport: " + root.Q<Label>("transport-status").text + " / volume: " + root.Q<Label>("volume-status").text);
                }
            }
        }

        var receiverTransport = _receiverLogs.FindAll(l => l.StartsWith("[Receiver] transport")).Count;
        var got42 = _receiverLogs.Exists(l => l.Contains("VOLUME -> 42"));
        var got30 = _receiverLogs.Exists(l => l.Contains("VOLUME -> 30"));
        var ok = error == null && receiverTransport >= 3 && got42 && got30;

        var result = "{\n" +
            $"  \"ok\": {(ok ? "true" : "false")},\n" +
            $"  \"error\": {(error == null ? "null" : "\"" + error.Replace("\"", "'") + "\"")},\n" +
            $"  \"steps\": [{string.Join(", ", _steps.ConvertAll(s => "\"" + s.Replace("\"", "'") + "\""))}],\n" +
            $"  \"receiverTransportEvents\": {receiverTransport},\n" +
            $"  \"receiverGotVolume42\": {(got42 ? "true" : "false")},\n" +
            $"  \"receiverGotScheduledVolume30\": {(got30 ? "true" : "false")},\n" +
            $"  \"receiverLogs\": [{string.Join(", ", _receiverLogs.ConvertAll(l => "\"" + l.Replace("\"", "'") + "\""))}]\n" +
            "}\n";
        var path = Path.Combine(Application.dataPath, "..", "SmokeTestAdminResult.json");
        File.WriteAllText(path, result);
        Debug.Log($"[SmokeAdmin] {(ok ? "PASS" : "FAIL")} -> {path}\n{result}");

#if UNITY_EDITOR
        if (Application.isBatchMode)
            UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
        else
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // UI Toolkit のボタンの clicked を起こす。ClickEvent を送るだけでは Clickable が反応しないため、
    // Button が持つ Clickable の Invoke を呼び、画面と同じ経路（clicked の購読）を通す。
    private static void Click(VisualElement root, string name)
    {
        var button = root.Q<Button>(name);
        using var evt = ClickEvent.GetPooled();
        evt.target = button;
        var invoke = typeof(Clickable).GetMethod("Invoke",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, new[] { typeof(EventBase) }, null);
        if (invoke != null)
            invoke.Invoke(button.clickable, new object[] { evt });
        else
            button.SendEvent(evt);
    }

    private static IEnumerator WaitLabel(VisualElement root, string name, string contains, float timeoutSeconds)
    {
        var deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline && !root.Q<Label>(name).text.Contains(contains))
            yield return null;
    }
}
