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
/// 音は AudioListener をミュートして進める（再生位置は進む）。再射出の検証を含めて約 4 分半かかる。
/// 演出の順序・時刻、実際の粒子と雷の再生、一時停止、銀テープの再射出を検証する。
/// 結果は Logs/demo-effects-check.json。バッチ実行では終了コードにもなる。
/// </summary>
public class TestDemoEffectsDriver : MonoBehaviour
{
    private const string EffectPrefix = "[Effect] ";
    private readonly List<string> _effects = new();
    private readonly List<double> _times = new();
    private readonly List<string> _errors = new();
    private StageDirector _director;
    private DemoSceneController _demo;
    private bool _lightningStarted;
    private bool _lightningPositionsMatch = true;
    private readonly List<Vector3> _lightningPoints = new();
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
                var lightning = FindObjectsByType<LightningBolt>(FindObjectsSortMode.None).FirstOrDefault(b => b.IsPlaying);
                _lightningStarted |= lightning != null && lightning.IsPlaying;
                var targets = DemoLightningSchedule.Create().OrderBy(c => c.Seconds).ToArray();
                var cue = targets[_lightningPoints.Count];
                var expectedPoint = cue.Target == "audience" ? _demo.lightningAudiencePoint.position
                    : cue.Target == "stage" ? _demo.lightningStagePoint.position
                    : cue.Target == "unity-chan" ? new Plane(_demo.lightningOrigin.up, _demo.lightningOrigin.position)
                        .ClosestPointOnPlane(_director.PerformerHips.position)
                    : _demo.lightningOrigin.position + _demo.lightningOrigin.rotation * cue.LocalPosition;
                var point = lightning != null ? TestLightningPositions.Ground(lightning) : Vector3.positiveInfinity;
                _lightningPositionsMatch &= Vector3.Distance(point, expectedPoint) < 0.001f;
                _lightningPoints.Add(point);
                if (_lightningPoints.Count <= 3) StartCoroutine(CaptureLightning(_lightningPoints.Count));
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
        var demo = _demo = FindFirstObjectByType<DemoSceneController>();
        var defined = DefaultTimeline.Create().Where(a => a.Value.Text != EffectNames.Lightning)
            .Select(a => (seconds: PlaybackTime.Parse(a.Time).TotalSeconds, effect: a.Value.Text))
            .Concat(DemoLightningSchedule.Create().Select(c => (seconds: c.Seconds, effect: EffectNames.Lightning)))
            .OrderBy(a => a.seconds).ToArray();
        var expected = defined.Select(a => a.effect).ToArray();
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
            report.confettiEmitted |= confetti != null && confetti.GetComponentsInChildren<ParticleSystem>()
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
                var pausedStatus = demo.GetStatusText();
                yield return new WaitForSecondsRealtime(0.5f);
                report.pauseChecked = true;
                report.pauseWorks = source.timeSamples == sample && _effects.Count == count;
                report.statusPauseWorks = pausedStatus.Contains("一時停止") && pausedStatus == demo.GetStatusText();
                _director.ResumePerformance();

                var paper = confetti != null ? confetti.GetComponentsInChildren<ParticleSystem>() : Array.Empty<ParticleSystem>();
                _director.SetConfetti(false);
                report.confettiRestarted = paper.Length > 0 && paper.All(p => !p.isEmitting);
                _director.SetConfetti(true);
                yield return new WaitForSecondsRealtime(0.5f);
                report.confettiRestarted &= paper.All(p => p.isEmitting && p.particleCount > 0);
                var times = paper.Select(p => p.time).ToArray();
                _director.SetConfetti(true);
                report.confettiOnIsIdempotent = times.Length > 0 && times.All(time => time > 0)
                    && paper.Select((p, i) => p.time == times[i]).All(unchanged => unchanged);
            }
            yield return null;
        }
        report.silverBeforeFinale = silver != null ? silver.ParticleCount : 0;
        // Editor が重いとアニメーションが音源より遅れるため、終了イベントまで余裕を持って待つ。
        var finaleDeadline = Time.realtimeSinceStartup + 30f;
        while (_director != null && !_director.IsPerformancePaused && Time.realtimeSinceStartup < finaleDeadline)
            yield return null;
        yield return new WaitForSecondsRealtime(1f); // Prefab の soundTimingOffset は -0.6 秒。音の後の射出を待つ。
        report.silverFinaleParticles = silver != null ? silver.ParticleCount : 0;
        report.silverFinaleEmitted = _director != null && _director.IsPerformancePaused
            && report.silverFinaleParticles > report.silverBeforeFinale;

        report.effects = _effects.ToArray();
        report.times = _times.ToArray();
        report.lastMusicTimeSeconds = lastMusicTime;
        report.lightningStarted = _lightningStarted;
        report.lightningPositionsMatch = _lightningPositionsMatch && _lightningPoints.Count == DemoLightningSchedule.Create().Length;
        report.lightningPoints = _lightningPoints.ToArray();
        report.timingWorks = _times.Count == defined.Length && defined.Select((a, i) =>
            Math.Abs(_times[i] - a.seconds) < 3).All(ok => ok);

        report.confettiStopped = _confettiStopped;
        report.confettiDraining = _confettiDraining;
        var status = demo.GetStatusText();
        report.statusShowsEnd = status.Contains("終了");
        report.statusShowsEffects = status.Contains("紙吹雪: OFF")
            && status.Contains("雷") && status.Contains("紙吹雪 OFF") && status.Contains("銀テープ");

        // 既存の銀テープが自然に消えた後、曲終了後にも再射出できることを確認する。
        var dispatcher = new EffectDispatcher(_director);
        report.silverRepeated = silver != null;
        if (silver != null)
        {
            for (var i = 0; i < 2; i++)
            {
                yield return new WaitForSecondsRealtime(13f); // Prefab の最大寿命は 12 秒。
                dispatcher.Fire(EffectNames.SilverStreamer);
                yield return new WaitForSecondsRealtime(1f);
                report.silverRepeated &= silver.ParticleCount > 0;
            }
        }

        report.errors = _errors.ToArray();
        report.ok = started && report.offline && report.pauseWorks && report.confettiEmitted
            && report.confettiStopped && report.confettiDraining && report.confettiCleared
            && report.confettiRestarted && report.confettiOnIsIdempotent
            && report.lightningStarted && report.lightningPositionsMatch && report.silverEmitted && report.silverFinaleEmitted
            && report.silverRepeated && report.timingWorks
            && report.statusPauseWorks && report.statusShowsEnd && report.statusShowsEffects
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

    private IEnumerator CaptureLightning(int index)
    {
        yield return new WaitForSecondsRealtime(0.15f);
        yield return new WaitForEndOfFrame();
        var directory = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, $"lightning-position-{index}.png"));
    }

    [Serializable]
    private class Report
    {
        public bool ok, offline, pauseChecked, pauseWorks, confettiEmitted, lightningStarted;
        public bool lightningPositionsMatch;
        public Vector3[] lightningPoints;
        public bool confettiStopped, confettiDraining, confettiCleared, silverEmitted, silverRepeated, timingWorks;
        public bool confettiRestarted, confettiOnIsIdempotent, silverFinaleEmitted;
        public bool statusPauseWorks, statusShowsEnd, statusShowsEffects;
        public int silverBeforeFinale, silverFinaleParticles;
        public double lastMusicTimeSeconds;
        public string[] effects, errors;
        public double[] times;
    }
}
