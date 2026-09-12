using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Livisor.Live.Effects;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;

/// <summary>実音源・Animator・演出・一時停止を検証する。Liveは受信済みスナップショットを注入する。</summary>
public sealed class TestCutPlaybackDriver : MonoBehaviour
{
    public double startSeconds;
    public bool cutMode, waitForFinale, live;
    readonly List<string> checks = new();
    readonly List<string> errors = new();
    readonly List<string> effects = new();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    StageDirector director;
    TimelineReceiver receiver;
    float previousVolume;
    double firstPosition;

    void OnEnable()
    {
        previousVolume = AudioListener.volume;
        AudioListener.volume = 0;
        Application.logMessageReceived += OnLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
        AudioListener.volume = previousVolume;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) errors.Add(message);
        if (message.StartsWith("[Effect] ")) effects.Add(message.Substring(9));
    }

    void Update()
    {
        if (receiver != null) Invoke(receiver, "Update");
    }

    static object Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Private).Invoke(target, args);

    static void Set(object target, string name, object value)
        => target.GetType().GetField(name, Private).SetValue(target, value);

    void Check(bool condition, string message)
    {
        checks.Add($"{(condition ? "PASS" : "FAIL")}: {message}");
        if (!condition) errors.Add(message);
    }

    TransportState Snapshot(bool playing) => new()
    {
        Playing = playing,
        Actions = DefaultTimeline.Create(),
        ServerTimeMs = 1000,
        StartedAtServerMs = 1000,
    };

    IEnumerator Start()
    {
        director = FindFirstObjectByType<StageDirector>();
        if (live)
        {
            receiver = FindFirstObjectByType<TimelineReceiver>();
            Set(receiver, "_player", new StageMediaPlayer(director));
            Set(receiver, "_stageDirector", director);
            Set(receiver, "_effects", new EffectDispatcher(director));
            Invoke(receiver, "ApplyTransport", Snapshot(true));
        }

        var deadline = Time.realtimeSinceStartupAsDouble + 12;
        while (director.MusicTimeSeconds < 0 && Time.realtimeSinceStartupAsDouble < deadline)
            yield return null;
        firstPosition = director.MusicTimeSeconds;
        Check(firstPosition >= startSeconds && firstPosition < startSeconds + 0.5, "音源は指定位置から開始");
        if (firstPosition < 0) { Finish(); yield break; }

        yield return new WaitForSecondsRealtime(0.8f);
        var source = director.MusicPlayerController.MainSource;
        Check(director.MusicPlayerController.AnalysisSources.Length == 4, "解析用音源は4つ");
        Check(director.MusicPlayerController.AnalysisSources.All(s => s.isPlaying
            && Math.Abs(s.timeSamples / (double)s.clip.frequency - director.MusicTimeSeconds) < 0.06),
            "Mainと全解析用音源の位置が一致");
        var animator = director.GetComponent<Animator>();
        var stageState = animator.GetCurrentAnimatorStateInfo(0);
        var stageSeconds = stageState.normalizedTime * stageState.length;
        Check(Math.Abs(stageSeconds - director.MusicTimeSeconds - 2.01666665) < 0.25, "ステージ時刻は曲内時刻+導入時間");
        foreach (var name in new[] { "unitychan_hw(Clone)", "LipSyncController(Clone)" })
        {
            var performer = GameObject.Find(name).GetComponent<Animator>();
            var state = performer.GetCurrentAnimatorStateInfo(0);
            var time = state.normalizedTime * state.length;
            Check(Math.Abs(time - (state.loop ? stageSeconds : Math.Min(stageSeconds, state.length))) < 0.25,
                $"{name}のアニメーション位置がステージと一致");
        }
        if (startSeconds > 0)
        {
            Check(!effects.Contains(EffectNames.Lightning) && !effects.Contains(EffectNames.ConfettiOn),
                "過去の雷と紙吹雪ONを新規発火しない");
            var confetti = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(t => t.name == "Confetti(Clone)").GetComponentsInChildren<ParticleSystem>(true);
            Check(confetti.All(p => p.isEmitting == (startSeconds < 210)), "開始位置の紙吹雪ON/OFFを復元");
        }
        else
            Check(effects.Contains(EffectNames.ConfettiOn), "通常/0秒開始では0.5秒の紙吹雪が発火");

        // アセットを編集しても、再開時には確定済みの開始位置を保持する。
        var config = typeof(StageDirector).GetField("playbackConfig", Private).GetValue(director);
        Set(config, "_startSeconds", 10.0);
        if (live) Invoke(receiver, "ApplyTransport", Snapshot(false)); else director.PausePerformance();
        var pausedSample = source.timeSamples;
        // timeScale変更フレームには既に確定したdeltaTimeによるAnimator評価が残る。
        yield return null;
        var pausedAnimation = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        var effectCount = effects.Count;
        yield return new WaitForSecondsRealtime(0.4f);
        Check(source.timeSamples == pausedSample, "一時停止中は音楽が進まない");
        Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == pausedAnimation, "一時停止中はアニメーションが進まない");
        Check(effects.Count == effectCount, "一時停止中は予約が進まない");
        if (live) Invoke(receiver, "ApplyTransport", Snapshot(true)); else director.ResumePerformance();
        director.StartMusic(); // 遅延/重複したAnimation Eventでも戻らない。
        yield return new WaitForSecondsRealtime(0.4f);
        Check(director.PlaybackStartSeconds == startSeconds && source.timeSamples > pausedSample,
            "設定変更・再受信・重複開始でも一時停止位置から再開");
        Check(director.MusicTimeSeconds < pausedSample / (double)source.clip.frequency + 0.8,
            "再開で別の位置へジャンプしない");

        if (waitForFinale)
        {
            var until = Time.realtimeSinceStartupAsDouble + 30;
            while (!director.IsPerformancePaused && Time.realtimeSinceStartupAsDouble < until)
                yield return null;
            var silver = FindFirstObjectByType<SilverStreamerController>();
            var beforeFinale = silver.ParticleCount;
            var emissionDeadline = Time.realtimeSinceStartupAsDouble + 3;
            // 射出は音の0.6秒後。停止を観測したフレームでは、まだ予約中のことがある。
            while (silver.ParticleCount <= beforeFinale && Time.realtimeSinceStartupAsDouble < emissionDeadline)
                yield return null;
            Check(director.IsPerformancePaused, "曲末の終了イベントで停止");
            Check(effects.SequenceEqual(new[] { EffectNames.SilverStreamer }), "カット開始後の220秒の銀テープだけが発火");
            Check(silver.ParticleCount > beforeFinale, $"終了時の銀テープも射出 ({beforeFinale} -> {silver.ParticleCount})");
            Check(director.overlayIntensity > 0.99, "曲末は暗転");
            Check(FindFirstObjectByType<DemoSceneController>().GetStatusText().Contains("終了"), "Demoは終了と表示");
        }
        else
        {
            var demo = FindFirstObjectByType<DemoSceneController>();
            if (demo != null)
                Check(demo.GetStatusText().Contains(cutMode ? "カット: ON" : "カット: OFF"), "Demoにカット設定を表示");
        }
        Finish();
    }

    void Finish()
    {
        var report = new Report { ok = errors.Count == 0, firstPosition = firstPosition,
            checks = checks.ToArray(), errors = errors.ToArray(), effects = effects.ToArray() };
        var path = Path.Combine(Application.dataPath, "..", "Logs", $"cut-{(live ? "live" : "demo")}-{(cutMode ? startSeconds.ToString("0") : "normal")}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Debug.Log($"[TestCutPlayback] {(report.ok ? "PASS" : "FAIL")} -> {path}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    [Serializable]
    sealed class Report
    {
        public bool ok;
        public double firstPosition;
        public string[] checks, errors, effects;
    }
}
