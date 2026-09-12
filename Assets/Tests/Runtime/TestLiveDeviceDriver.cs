#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Livisor.Device;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Drives the real UI and LiveScene. This test actuates the physical device.
public sealed class TestLiveDeviceDriver : MonoBehaviour
{
    [Serializable]
    public sealed class Report
    {
        public bool success;
        public string startedAtUtc;
        public double elapsedSeconds;
        public string error;
        public string deviceAddress;
        public int pingChecks;
        public int successfulDeviceCommands;
        public List<string> steps = new();
        public List<string> deviceReplies = new();
        public List<string> runtimeErrors = new();
    }

    readonly Report report = new();
    readonly ConcurrentQueue<string> replies = new();
    readonly ConcurrentQueue<string> errors = new();
    VisualElement root;
    DeviceCommandExample device;
    StageDirector director;
    LivisorDeviceClient probe;
    DateTime started;

    async void Start()
    {
        started = DateTime.UtcNow;
        report.startedAtUtc = started.ToString("O");
        Application.logMessageReceivedThreaded += Observe;
        Application.runInBackground = true;
        Application.targetFrameRate = 30;
        try
        {
            root = FindFirstObjectByType<UIDocument>().rootVisualElement;
            await Click("connect-button");
            await Until(() => root.Q<Label>("connection-status").text.Contains("接続済み"), "Admin CONNECT", 15);
            await Click("cancel-schedule-button");
            await Until(() => root.Q<Label>("transport-status").text.Contains("予約なし"), "Reset schedule");
            await Click("stop-button");
            await Until(() => root.Q<Label>("transport-status").text.StartsWith("停止中"), "Reset transport");

            var load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/LiveScene.unity",
                new LoadSceneParameters(LoadSceneMode.Additive));
            await Until(() => load.isDone, "Load real LiveScene", 45);
            device = FindFirstObjectByType<DeviceCommandExample>();
            director = FindFirstObjectByType<StageDirector>();
            if (device == null || director == null) throw new Exception("LiveScene receiver/device/director missing");
            await Until(() => device.IsReachable, "Device mDNS + ping", 15);
            report.deviceAddress = device.ResolvedHost;
            probe = new LivisorDeviceClient(device.ResolvedHost, device.port);
            await device.WaitForCommandsAsync();
            await SetVolume(30);
            await SetPlaying(true);
            Step("VIBRATION_START: volume 30; first 60-second playback");

            // An unchanged playing snapshot must not send another start (Pi would rewind).
            int commands = CountReplies("\"start\":1");
            await Click("play-button");
            await Task.Delay(1500);
            await device.WaitForCommandsAsync();
            if (CountReplies("\"start\":1") != commands) throw new Exception("Repeated PLAY restarted the device");
            Step("Repeated PLAY did not restart Pi");
            await Hold(60, true);

            await SetPlaying(false);
            Step("VIBRATION_STOP: 30-second pause");
            await Hold(30, false);
            await SetVolume(45);
            await SetPlaying(true);
            Step("VIBRATION_START: volume 45; second 60-second playback");

            var list = root.Q<ListView>("timeline-list");
            var row = list.GetRootElementForIndex(0);
            if (row == null) throw new Exception("Timeline row missing");
            row.Q<IntegerField>("seconds-field").value = 5;
            row.Q<DropdownField>("action-dropdown").value = "VolumeChange";
            row.Q<IntegerField>("number-field").value = 20;
            await Click("schedule-button");
            await Until(() => device.LastResponse != null && device.LastResponse.Contains("\"volume\": 20"), "Scheduled Pi volume 20", 12);
            if (Math.Abs(director.MusicPlayerController.MainVolume - .2f) > .01f)
                throw new Exception("Scheduled live volume differs from Pi volume");
            await Click("cancel-schedule-button");
            Step("Scheduled volume 20 reached live audio and device");
            await Hold(60, true);

            await SetPlaying(false);
            Step("VIBRATION_STOP: 30-second pause");
            await Hold(30, false);
            await SetVolume(35);
            await SetPlaying(true);
            Step("VIBRATION_START: volume 35; third 60-second playback");
            await Hold(60, true);
            await SetPlaying(false);
            await Hold(Math.Max(0, 300 - (DateTime.UtcNow - started).TotalSeconds), false);
            if (report.runtimeErrors.Count != 0) throw new Exception("Runtime errors occurred; see report");
            report.success = true;
        }
        catch (Exception e) { report.error = e.ToString(); }
        finally
        {
            try
            {
                if (root != null && root.Q<Button>("stop-button").enabledSelf)
                {
                    await Click("cancel-schedule-button");
                    await Until(() => root.Q<Button>("stop-button").enabledInHierarchy, "Cleanup wait for CANCEL");
                    await Click("stop-button");
                    await Task.Delay(1000);
                }
            }
            catch (Exception e) { report.success = false; report.error += "\nUI cleanup: " + e; }
            try
            {
                if (device != null && device.IsReachable)
                {
                    await device.StopAndWaitAsync();
                    if (device.LastError != null) throw new Exception(device.LastError);
                }
            }
            catch (Exception e) { report.success = false; report.error += "\nCleanup: " + e; }
            report.successfulDeviceCommands = device != null ? device.SuccessfulCommandCount : 0;
            report.elapsedSeconds = (DateTime.UtcNow - started).TotalSeconds;
            Save();
            Application.logMessageReceivedThreaded -= Observe;
            Debug.Log($"[LiveDeviceTest] {(report.success ? "PASS" : "FAIL")} {report.elapsedSeconds:F1}s");
            if (Application.isBatchMode) EditorApplication.Exit(report.success ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }

    async Task SetVolume(int value)
    {
        root.Q<SliderInt>("volume-field").value = value;
        await Click("volume-button");
        await Until(() =>
        {
            DrainLogs();
            var latestVolumeReply = report.deviceReplies.LastOrDefault(l => l.Contains("\"volumeChange\""));
            return latestVolumeReply != null && latestVolumeReply.Contains($"\"volume\": {value}");
        }, $"Pi volume {value}");
        if (Math.Abs(director.MusicPlayerController.MainVolume - value / 100f) > .01f)
            throw new Exception("Live volume differs from Pi volume");
        Step($"Admin VOLUME {value}: live and device ACK");
    }

    async Task SetPlaying(bool playing)
    {
        string marker = playing ? "\"start\":1" : "\"stop\":1";
        int before = CountReplies(marker);
        await Click(playing ? "play-button" : "stop-button");
        await Until(() => CountReplies(marker) > before && director.IsPerformancePaused != playing,
            playing ? "Live/Pi PLAY" : "Live/Pi STOP");
        if (device.LastError != null) throw new Exception(device.LastError);
        Step(playing ? "Admin PLAY: live and device ACK" : "Admin STOP: live and device ACK");
    }

    async Task Hold(double seconds, bool playing)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, Math.Max(.01, (until - DateTime.UtcNow).TotalSeconds))));
            if (director.IsPerformancePaused == playing) throw new Exception("Live playback state changed unexpectedly");
            if (device.LastError != null) throw new Exception(device.LastError);
            await probe.SendPingAsync();
            report.pingChecks++;
            if (playing && !director.MusicPlayerController.MainSource.isPlaying)
                throw new Exception("Live Main AudioSource is not playing");
            Save();
        }
        Step($"Held {(playing ? "playback" : "pause")} for {seconds:F0}s");
    }

    void Observe(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Livisor] {") && message.Contains(" -> ")) replies.Enqueue(message);
        if (type == LogType.Exception || type == LogType.Error) errors.Enqueue(message);
    }

    void Step(string value)
    {
        report.steps.Add($"{(DateTime.UtcNow - started).TotalSeconds:F1}s: {value}");
        Debug.Log("[LiveDeviceTest] " + value);
        Save();
    }

    void DrainLogs()
    {
        while (replies.TryDequeue(out var reply)) report.deviceReplies.Add(reply);
        while (errors.TryDequeue(out var error)) report.runtimeErrors.Add(error);
    }

    int CountReplies(string marker)
    {
        DrainLogs();
        return report.deviceReplies.Count(l => l.Contains(marker));
    }

    void Save()
    {
        DrainLogs();
        Directory.CreateDirectory("Logs");
        report.elapsedSeconds = (DateTime.UtcNow - started).TotalSeconds;
        File.WriteAllText("Logs/live-device-report.json", JsonUtility.ToJson(report, true));
    }

    async Task Click(string name)
    {
        var button = root.Q<Button>(name);
        if (button == null) throw new Exception("Button missing: " + name);
        await Until(() => button.enabledInHierarchy, "Button busy: " + name);
        using var evt = ClickEvent.GetPooled();
        evt.target = button;
        var invoke = typeof(Clickable).GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(EventBase) }, null);
        if (invoke == null) throw new Exception("UI Toolkit Clickable.Invoke unavailable");
        invoke.Invoke(button.clickable, new object[] { evt });
    }

    static async Task Until(Func<bool> condition, string label, double seconds = 10)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException(label);
            await Task.Delay(50);
        }
    }
}
#endif
