#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Livisor.Device;
using UnityEditor;
using UnityEngine;

/// <summary>実際のTCP/JSONをローカルで受信し、Demoの音楽とデバイス指示の連動を確認する。</summary>
[DefaultExecutionOrder(-200)]
public sealed class TestDemoDeviceDriver : MonoBehaviour
{
    readonly List<string> _commands = new();
    readonly List<string> _checks = new();
    readonly List<string> _errors = new();
    TcpListener _listener;
    Task _receiveTask;
    DeviceCommandExample _device;
    float _volume;

    void Awake()
    {
        _volume = AudioListener.volume;
        AudioListener.volume = 0;
        Application.logMessageReceived += OnLog;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _receiveTask = ReceiveCommands();
        _device = FindFirstObjectByType<DeviceCommandExample>();
        _device.manualIp = "127.0.0.1";
        _device.useMdns = false;
        _device.port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    async Task ReceiveCommands()
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            var command = await reader.ReadLineAsync();
            _commands.Add(command);
            await writer.WriteLineAsync(command.Contains("\"ping\"")
                ? "{\"ok\":true,\"result\":{\"action\":\"ping\",\"pong\":true}}"
                : "{\"ok\":true}");
        }
    }

    async void Start()
    {
        var demo = FindFirstObjectByType<DemoSceneController>();
        var director = demo.GetComponent<StageDirector>();
        var source = director.MusicPlayerController.MainSource;
        try
        {
            Check(!FindObjectsByType<TimelineReceiver>(FindObjectsSortMode.None).Any(r => r.isActiveAndEnabled),
                "サーバー受信を使わない");
            await Until(() => _device.IsReachable, "TCP pingで接続する");
            Check(Count("start") == 0 && !source.isPlaying, "音楽の開始前にデバイスを再生しない");
            await Until(() => source.isPlaying && Count("start") == 1, "音楽開始に合わせてstartを送る");
            Check(_commands.IndexOf("{\"action\":{\"volumeChange\":100}}") < _commands.IndexOf("{\"action\":{\"start\":1}}"),
                "初期音量をstartより先に送る");
            demo.ResumePerformance();
            await Task.Delay(200);
            Check(Count("start") == 1, "再生中はstartを重複送信しない");

            var stops = Count("stop");
            demo.PausePerformance();
            await Until(() => Count("stop") == stops + 1, "一時停止でstopを送る");
            demo.HandleButtons(false, false, true);
            await Until(() => _commands.Contains("{\"action\":{\"volumeChange\":30}}"), "Aで音量30を送る");
            demo.HandleButtons(false, false, true);
            await Task.Delay(100);
            Check(_commands.Count(c => c == "{\"action\":{\"volumeChange\":30}}") == 1,
                "Aの押しっぱなしは1回だけ送る");
            demo.HandleButtons(false, false, false);
            demo.HandleButtons(false, false, true);
            await Until(() => _commands.Count(c => c == "{\"action\":{\"volumeChange\":100}}") == 2,
                "Aを押し直すと音量100を送る");
            demo.HandleButtons(false, false, false);
            demo.ResumePerformance();
            await Until(() => Count("start") == 2, "再開でstartを送る");

            stops = Count("stop");
            source.time = source.clip.length - 0.2f;
            await Until(() => !source.isPlaying && Count("stop") == stops + 1, "曲の自然終了でstopを送る");
            director.StartMusic();
            await Until(() => Count("start") == 3, "曲を再生し直すとstartを送る");
            await _device.WaitForCommandsAsync();
            stops = Count("stop");
            _device.enabled = false;
            await _device.WaitForCommandsAsync();
            Check(Count("stop") == stops + 1, "Deviceの無効化時にもstopを送る");
            Check(source.isPlaying, "DeviceなしでもDemoの再生を続ける");
            _device.enabled = true;
            await Until(() => _device.IsReachable && Count("start") == 4, "再接続時に現在の再生状態を送る");
            await _device.WaitForCommandsAsync();
            stops = Count("stop");
            demo.enabled = false;
            await _device.WaitForCommandsAsync();
            Check(Count("stop") == stops + 1, "Demo操作を無効化したときにstopを送る");
            Check(_device.LastError == null, "Device通信エラーなし");
        }
        catch (Exception e) { _errors.Add(e.ToString()); }
        finally
        {
            _device.enabled = false;
            await _device.WaitForCommandsAsync();
            _listener.Stop();
            try { await _receiveTask; }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            var report = new Report { ok = _errors.Count == 0, checks = _checks.ToArray(),
                commands = _commands.ToArray(), errors = _errors.ToArray() };
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/demo-device-check.json", JsonUtility.ToJson(report, true));
            Debug.Log($"[TestDemoDevice] {(report.ok ? "PASS" : "FAIL")}");
            if (Application.isBatchMode) EditorApplication.Exit(report.ok ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }

    int Count(string action) => _commands.Count(c => c.Contains($"\"{action}\":1"));

    void Check(bool passed, string label)
    {
        if (!passed) throw new Exception(label);
        _checks.Add(label);
    }

    async Task Until(Func<bool> condition, string label)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(label);
            await Task.Delay(20);
        }
        _checks.Add(label);
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) _errors.Add(message);
    }

    void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
        AudioListener.volume = _volume;
        _listener?.Stop();
    }

    [Serializable]
    class Report
    {
        public bool ok;
        public string[] checks, commands, errors;
    }
}
#endif
