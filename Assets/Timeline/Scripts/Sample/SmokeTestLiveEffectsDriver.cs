using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Livisor.Live.Effects;
using Livisor.Shared.Common;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Admin → ローカルサーバー → LiveScene の演出予約を確認する。
/// 紙吹雪の停止・開始、雷、銀テープ、重複通知、取消、一時停止を検査する。
/// Demo の事前定義が Live で発火しないことも 60 秒地点まで確認する。
/// 起動は SmokeTest.RunLiveEffects。結果は Logs/live-effects-check.json。
/// </summary>
public class SmokeTestLiveEffectsDriver : MonoBehaviour
{
    readonly List<string> _effects = new();
    readonly List<double> _times = new();
    readonly List<string> _checks = new();
    readonly List<string> _errors = new();
    StageDirector _director;
    VisualElement _root;
    float _previousVolume;
    bool _lightningStarted;

    void OnEnable()
    {
        _previousVolume = AudioListener.volume;
        AudioListener.volume = 0;
        Application.logMessageReceived += OnLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= OnLog;
        AudioListener.volume = _previousVolume;
    }

    void OnLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("[Effect] "))
        {
            _effects.Add(message.Substring("[Effect] ".Length));
            _times.Add(_director != null ? _director.MusicTimeSeconds : -1);
            if (message == "[Effect] " + EffectNames.Lightning)
                _lightningStarted |= FindFirstObjectByType<LightningBolt>()?.IsPlaying == true;
        }
        if (type == LogType.Error || type == LogType.Exception)
            _errors.Add(message);
    }

    IEnumerator Start()
    {
        _director = FindFirstObjectByType<StageDirector>();
        _root = FindFirstObjectByType<AdminConsoleView>().GetComponent<UIDocument>().rootVisualElement;
        yield return null;
        Click("connect-button");
        yield return WaitFor(() => _root.Q<Button>("connect-button").text == "DISCONNECT", 10);
        if (!_root.Q<Button>("play-button").enabledSelf)
        {
            Check(false, "Admin connects to local server");
            Finish();
            yield break;
        }
        Click("play-button");
        yield return WaitFor(() => _director.MusicTimeSeconds >= 3, 15);
        Check(_director.MusicTimeSeconds >= 3, "Admin PLAY starts Live music");

        var confetti = GameObject.Find("Confetti(Clone)");
        var paper = confetti != null ? confetti.GetComponentsInChildren<ParticleSystem>() : Array.Empty<ParticleSystem>();
        Check(paper.Length > 0 && paper.Any(p => p.particleCount > 0), "existing scene confetti still starts");
        Check(_effects.Count == 0, "Demo confettiOn is not dispatched in Live");

        Schedule(EffectNames.ConfettiOff, 0);
        yield return WaitFor(() => _effects.Count >= 1, 5);
        Check(paper.Length > 0 && paper.All(p => !p.isEmitting) && paper.Any(p => p.particleCount > 0), "past confettiOff stops emission without clearing paper");
        yield return WaitFor(() => paper.All(p => p.particleCount == 0), 25);
        Check(paper.Length > 0 && paper.All(p => p.particleCount == 0), "remaining paper disappears naturally");

        Schedule(EffectNames.ConfettiOn, 0);
        yield return WaitFor(() => _effects.Count >= 2, 5);
        yield return new WaitForSecondsRealtime(0.5f);
        Check(paper.All(p => p.isEmitting && p.particleCount > 0), "confettiOn restarts existing paper");
        Check(FindObjectsByType<PropActivator>(FindObjectsSortMode.None).Count(p => p.gameObject.name == "Confetti(Clone)") == 1, "only one confetti instance exists");
        var count = _effects.Count;
        Schedule(EffectNames.ConfettiOn, 0);
        yield return new WaitForSecondsRealtime(1f);
        Check(_effects.Count == count, "same reservation is not executed twice");
        Click("cancel-schedule-button");
        yield return WaitFor(() => _root.Q<Label>("transport-status").text.Contains("予約なし"), 5);
        Schedule(EffectNames.ConfettiOn, 0);
        yield return WaitFor(() => _effects.Count > count, 5);
        Check(_effects.Count == count + 1, "cancel then schedule starts a new reservation");

        var lightningTime = Math.Ceiling(_director.MusicTimeSeconds + 3);
        Schedule(EffectNames.Lightning, (int)lightningTime);
        yield return new WaitForSecondsRealtime(0.3f);
        Click("stop-button");
        yield return WaitFor(() => _director.IsPerformancePaused, 5);
        var sample = _director.MusicPlayerController.MainSource.timeSamples;
        count = _effects.Count;
        yield return new WaitForSecondsRealtime(4f);
        Check(_director.IsPerformancePaused && sample == _director.MusicPlayerController.MainSource.timeSamples && _effects.Count == count, "STOP freezes music and pending effect");
        Click("play-button");
        yield return WaitFor(() => _effects.Count > count, 8);
        Check(_lightningStarted && _effects.Count == count + 1 && Math.Abs(_times.Last() - lightningTime) < 0.3, "lightning follows music position after resume");

        count = _effects.Count;
        Schedule(EffectNames.SilverStreamer, 0);
        yield return WaitFor(() => _effects.Count > count, 5);
        yield return new WaitForSecondsRealtime(1f);
        var silver = FindFirstObjectByType<SilverStreamerController>();
        Check(silver != null && silver.ParticleCount > 0, "Admin silverStreamer emits particles");

        count = _effects.Count;
        Schedule(EffectNames.Lightning, (int)Math.Ceiling(_director.MusicTimeSeconds + 3));
        yield return new WaitForSecondsRealtime(0.3f);
        Click("cancel-schedule-button");
        yield return new WaitForSecondsRealtime(4f);
        Check(_effects.Count == count, "CANCEL prevents pending effect");
        _root.Q<IntegerField>("volume-field").value = 42;
        Click("volume-button");
        yield return WaitFor(() => Mathf.Abs(_director.MusicPlayerController.MainSource.volume - 0.42f) < 0.001f, 5);
        Check(Mathf.Abs(_director.MusicPlayerController.MainSource.volume - 0.42f) < 0.001f, "Admin volume reaches Live");

        yield return WaitFor(() => _director.MusicTimeSeconds >= 61, 65);
        Check(_director.MusicTimeSeconds >= 61 && _effects.Count == count, "Demo lightning at 60 seconds is not dispatched in Live");
        Click("stop-button");
        yield return WaitFor(() => _director.IsPerformancePaused, 5);
        Check(_director.IsPerformancePaused, "Admin STOP pauses Live");
        Click("connect-button");
        yield return WaitFor(() => _root.Q<Button>("connect-button").text == "CONNECT", 5);
        Finish();
    }

    void Schedule(string effectName, int seconds)
    {
        var row = _root.Q<ListView>("timeline-list").GetRootElementForIndex(0);
        row.Q<IntegerField>("seconds-field").value = seconds % 60;
        row.Q<IntegerField>("minutes-field").value = seconds / 60;
        row.Q<DropdownField>("action-dropdown").value = "Effect";
        row.Q<TextField>("text-field").value = effectName;
        Click("schedule-button");
    }

    void Click(string name)
    {
        var button = _root.Q<Button>(name);
        using var evt = ClickEvent.GetPooled();
        evt.target = button;
        var invoke = typeof(Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, new[] { typeof(EventBase) }, null);
        if (invoke != null) invoke.Invoke(button.clickable, new object[] { evt });
        else button.SendEvent(evt);
    }

    static IEnumerator WaitFor(Func<bool> condition, float seconds)
    {
        var deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
    }

    void Check(bool ok, string name) => _checks.Add((ok ? "OK: " : "NG: ") + name);

    void Finish()
    {
        var report = new Report { ok = _checks.All(c => c.StartsWith("OK: ")) && _errors.Count == 0,
            checks = _checks.ToArray(), errors = _errors.ToArray(), effects = _effects.ToArray(), times = _times.ToArray() };
        var path = Path.Combine(Application.dataPath, "..", "Logs", "live-effects-check.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Debug.Log($"[SmokeLive] {(report.ok ? "PASS" : "FAIL")} -> {path}");
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(report.ok ? 0 : 1);
        else UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    [Serializable]
    class Report
    {
        public bool ok;
        public string[] checks, errors, effects;
        public double[] times;
    }
}
