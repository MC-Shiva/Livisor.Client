using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Livisor.Live.Effects;
using UnityEngine;

/// <summary>Demoの手動演出・音量・自動発火の停止をPlay Modeで検証する。通信は使わない。</summary>
public class TestDemoEffectsDriver : MonoBehaviour
{
    readonly List<string> _effects = new();
    readonly List<string> _errors = new();
    readonly List<string> _checks = new();
    float _previousVolume;

    void OnEnable()
    {
        _previousVolume = AudioListener.volume;
        Application.logMessageReceived += OnLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
        AudioListener.volume = _previousVolume;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (message == "[DemoScene] 雷" || message == "[DemoScene] 銀テープ") _effects.Add(message);
        if (type == LogType.Exception || type == LogType.Error) _errors.Add(message);
    }

    void Check(bool success, string name)
    {
        if (success) _checks.Add(name); else _errors.Add(name);
    }

    IEnumerator Start()
    {
        AudioListener.volume = 0;
        var demo = FindFirstObjectByType<DemoSceneController>();
        var director = demo.GetComponent<StageDirector>();
        var silver = FindFirstObjectByType<SilverStreamerController>();
        var deadline = Time.realtimeSinceStartup + 20;
        while (director.MusicTimeSeconds < 7 && Time.realtimeSinceStartup < deadline) yield return null;
        Check(director.MusicTimeSeconds >= 7, "Demoの音楽が単独で再生する");
        Check(!FindObjectsByType<TimelineReceiver>(FindObjectsSortMode.None).Any(r => r.isActiveAndEnabled)
            && FindFirstObjectByType<AdminConsoleView>() == null
            && !FindObjectsByType<Livisor.Device.DeviceCommandExample>(FindObjectsSortMode.None).Any(d => d.isActiveAndEnabled),
            "演出テストはServer・Admin・Deviceへ接続しない");
        var paper = Resources.FindObjectsOfTypeAll<ParticleSystem>().Where(p => p.gameObject.scene.IsValid()
            && p.transform.root.name == "Confetti(Clone)").ToArray();
        Check(_effects.Count == 0 && silver.ParticleCount == 0 && paper.All(p => !p.isEmitting),
            "開始時に雷・銀テープ・紙吹雪を自動発火しない");
        _checks.AddRange(TestLightningPositions.Run(demo));

        var source = director.MusicPlayerController.MainSource;
        source.time = 221;
        yield return null;
        yield return null;
        Check(_effects.Count == 0 && silver.ParticleCount == 0, "旧予約時刻を過ぎても自動発火しない");
        director.EndPerformance();
        yield return new WaitForSecondsRealtime(1);
        Check(director.IsPerformancePaused && silver.ParticleCount == 0, "Demoの曲終了時にも銀テープを自動射出しない");

        demo.HandleButtons(true, false, false);
        demo.HandleButtons(true, false, false);
        Check(_effects.Count == 1, "Xを押し続けても雷は1回だけ");
        var bolt = FindObjectsByType<LightningBolt>(FindObjectsSortMode.None).FirstOrDefault(b => b.IsPlaying);
        Check(bolt != null, "Xから雷の描画を開始する");
        yield return new WaitForSecondsRealtime(0.15f);
        ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "..", "Logs", "demo-buttons-lightning.png"));
        yield return new WaitForSecondsRealtime(1);
        Check(bolt != null && !bolt.IsPlaying, "曲の停止中でも雷は進行して消える");
        demo.HandleButtons(false, false, false);
        demo.HandleButtons(true, false, false);
        Check(_effects.Count == 2, "Xを押し直すと雷を再発火する");
        demo.HandleButtons(false, false, false);

        director.SetMainVolume(100);
        var spectrum = director.MusicPlayerController.GetComponentsInChildren<AudioSource>()
            .Where(s => s != source).Select(s => (source: s, volume: s.volume)).ToArray();
        demo.HandleButtons(false, false, true);
        demo.HandleButtons(false, false, true);
        Check(Mathf.Approximately(source.volume, 0.3f), "Aで100%から30%にし、押し続けても戻らない");
        Check(demo.GetStatusText().Contains("音量: 30%"), "画面に現在の音量を表示する");
        demo.HandleButtons(false, false, false);
        demo.HandleButtons(false, false, true);
        Check(Mathf.Approximately(source.volume, 1), "Aを押し直すと100%に戻る");
        Check(spectrum.All(s => s.source.volume == s.volume), "音量切替は演出用の音源に影響しない");
        demo.HandleButtons(false, false, false);

        demo.HandleButtons(false, true, false);
        demo.HandleButtons(false, true, false);
        yield return new WaitForSecondsRealtime(1);
        Check(silver.ParticleCount > 0 && _effects.Count == 3, "Yで銀テープを1回射出する");
        ScreenCapture.CaptureScreenshot(Path.Combine(Application.dataPath, "..", "Logs", "demo-buttons-silver.png"));
        yield return new WaitForSecondsRealtime(13);
        Check(silver.ParticleCount == 0, "停止中でも銀テープが自然に消える");
        demo.HandleButtons(false, false, false);
        demo.HandleButtons(false, true, false);
        yield return new WaitForSecondsRealtime(1);
        Check(silver.ParticleCount > 0 && _effects.Count == 4, "Yを押し直すと銀テープを再射出する");
        Check(demo.GetStatusText().Contains("雷") && demo.GetStatusText().Contains("銀テープ"), "演出の履歴を表示する");
        demo.HandleButtons(false, false, false);
        source.time = 7;
        demo.ResumePerformance();
        yield return new WaitForSecondsRealtime(0.5f);
        Check(source.isPlaying && director.MusicTimeSeconds > 7, "音楽を再開できる");

        var report = new Report { ok = _errors.Count == 0, checks = _checks.ToArray(), errors = _errors.ToArray() };
        var path = Path.Combine(Application.dataPath, "..", "Logs", "demo-effects-check.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Debug.Log($"[SmokeDemo] {(report.ok ? "PASS" : "FAIL")} -> {path}");
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(report.ok ? 0 : 1);
        else UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    [Serializable]
    class Report
    {
        public bool ok;
        public string[] checks, errors;
    }
}
