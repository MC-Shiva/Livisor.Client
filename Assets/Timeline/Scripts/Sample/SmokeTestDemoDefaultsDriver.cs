using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Livisor.Live.Effects;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;

/// <summary>
/// DemoScene でデフォルト演出（Issue #22）が音楽の再生位置どおりに発火することを確かめる。通信は使わない。
/// 音は AudioListener をミュートして進める（再生位置は進む）。曲が終わるまで約 4 分かかる。
/// 演出の順序・時刻、実際の粒子と雷の再生、一時停止、銀テープの再射出を検証する。
/// 結果は Logs/demo-effects-check.json。バッチ実行では終了コードにもなる。
/// </summary>
public class SmokeTestDemoDefaultsDriver : MonoBehaviour
{
    private const string EffectPrefix = "[Effect] ";
    private readonly List<string> _effects = new();
    private readonly List<double> _times = new();
    private readonly List<string> _errors = new();
    private StageDirector _director;
    private bool _lightningStarted;
    private bool _confettiStopped, _confettiDraining;
    private float _previousVolume;

    private void OnEnable()
    {
        _previousVolume = AudioListener.volume;
        Application.logMessageReceived += OnLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
        AudioListener.volume = _previousVolume;
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (message.StartsWith(EffectPrefix))
        {
            _effects.Add(message.Substring(EffectPrefix.Length));
            _times.Add(_director != null ? _director.MusicTimeSeconds : -1);
            if (message == EffectPrefix + EffectNames.Lightning)
            {
                var lightning = FindFirstObjectByType<LightningBolt>();
                _lightningStarted |= lightning != null && lightning.IsPlaying;
            }
            if (message == EffectPrefix + EffectNames.ConfettiOff)
            {
                var paper = GameObject.Find("Confetti(Clone)");
                var particles = paper != null ? paper.GetComponentsInChildren<ParticleSystem>() : Array.Empty<ParticleSystem>();
                _confettiStopped = particles.Length > 0 && particles.All(p => !p.isEmitting);
                _confettiDraining = particles.Any(p => p.particleCount > 0);
            }
        }
        if (type == LogType.Exception || type == LogType.Error)
            _errors.Add(message);
    }

    private IEnumerator Start()
    {
        AudioListener.volume = 0f;

        _director = FindFirstObjectByType<StageDirector>();
        var defined = DefaultActionSet.Create().OrderBy(a => a.Time).ToArray();
        var expected = defined.Select(a => a.Value.Text).ToArray();
        var deadline = Time.realtimeSinceStartup + 320f;
        var started = false;
        var lastMusicTime = -1.0;
        var report = new Report
        {
            offline = FindFirstObjectByType<TimelineReceiver>() == null
                && FindFirstObjectByType<AdminConsoleView>() == null
                && FindFirstObjectByType<Livisor.Device.DeviceCommandExample>() == null,
        };
        var silver = FindFirstObjectByType<SilverStreamerController>();

        // 音楽が鳴り始めてから止まる（曲が終わって EndPerformance で一時停止する）まで待つ。
        while (Time.realtimeSinceStartup < deadline)
        {
            var t = _director != null ? _director.MusicTimeSeconds : -1;
            if (t >= 0) { started = true; lastMusicTime = t; }
            else if (started) break;

            var confetti = GameObject.Find("Confetti(Clone)");
            report.confettiRendered |= confetti != null && confetti.GetComponentsInChildren<ParticleSystem>()
                .Any(p => p.particleCount > 0);
            if (_confettiStopped && confetti != null)
                report.confettiCleared |= confetti.GetComponentsInChildren<ParticleSystem>().All(p => p.particleCount == 0);
            report.silverEmitted |= silver != null && silver.ParticleCount > 0;

            if (!report.pauseChecked && t >= 2)
            {
                _director.PausePerformance();
                var source = _director.MusicPlayerController.MainSource;
                var sample = source.timeSamples;
                var count = _effects.Count;
                yield return new WaitForSecondsRealtime(0.5f);
                report.pauseChecked = true;
                report.pauseWorks = source.timeSamples == sample && _effects.Count == count;
                _director.ResumePerformance();
            }
            yield return null;
        }
        yield return new WaitForSecondsRealtime(6f); // EndPerformance（240 秒）を過ぎるまで待つ

        report.effects = _effects.ToArray();
        report.times = _times.ToArray();
        report.lastMusicTimeSeconds = lastMusicTime;
        report.lightningStarted = _lightningStarted;
        report.timingWorks = _times.Count == defined.Length && defined.Select((a, i) =>
            Math.Abs(_times[i] - PlaybackTime.Parse(a.Time).TotalSeconds) < 3).All(ok => ok);

        report.confettiStopped = _confettiStopped;
        report.confettiDraining = _confettiDraining;

        // 曲終了後にも、同じ銀テープを 2 回続けて出せることを粒子数で確認する。
        var dispatcher = new EffectDispatcher(_director);
        report.silverRepeated = silver != null;
        if (silver != null)
        {
            for (var i = 0; i < 2; i++)
            {
                silver.GetComponentInChildren<ParticleSystem>().Clear();
                dispatcher.Fire(EffectNames.SilverStreamer);
                yield return new WaitForSecondsRealtime(1f);
                report.silverRepeated &= silver.ParticleCount > 0;
            }
        }

        report.errors = _errors.ToArray();
        report.ok = started && report.offline && report.pauseWorks && report.confettiRendered
            && report.confettiStopped && report.confettiDraining && report.confettiCleared
            && report.lightningStarted && report.silverEmitted && report.silverRepeated && report.timingWorks
            && report.effects.SequenceEqual(expected) && report.errors.Length == 0;
        var result = JsonUtility.ToJson(report, true);
        var path = Path.Combine(Application.dataPath, "..", "Logs", "demo-effects-check.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, result);
        Debug.Log($"[SmokeDemo] {(report.ok ? "PASS" : "FAIL")} -> {path}\n{result}");

#if UNITY_EDITOR
        if (Application.isBatchMode)
            UnityEditor.EditorApplication.Exit(report.ok ? 0 : 1);
        else
            UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    [Serializable]
    private class Report
    {
        public bool ok, offline, pauseChecked, pauseWorks, confettiRendered, lightningStarted;
        public bool confettiStopped, confettiDraining, confettiCleared, silverEmitted, silverRepeated, timingWorks;
        public double lastMusicTimeSeconds;
        public string[] effects, errors;
        public double[] times;
    }
}
