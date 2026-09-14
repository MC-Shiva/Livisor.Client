using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Livisor.Live.Effects;
using UnityEngine;

/// <summary>RecordSceneのローカル再生と、A/Bの押下・長押し・再押下を実際の演出で検証する。</summary>
public sealed class TestRecordEffectsDriver : MonoBehaviour
{
    readonly List<string> _errors = new();
    readonly List<string> _checks = new();

    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;

    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            _errors.Add(message);
    }

    void Check(bool success, string name)
    {
        if (success) _checks.Add(name);
        else _errors.Add(name);
    }

    int ActiveBolts() => FindObjectsByType<LightningBolt>(FindObjectsSortMode.None).Count(b => b.IsPlaying);

    IEnumerator Start()
    {
        var record = FindFirstObjectByType<RecordSceneController>();
        var director = FindFirstObjectByType<StageDirector>();
        var silver = FindFirstObjectByType<SilverStreamerController>();
        if (record == null || director == null || silver == null)
        {
            _errors.Add("RecordSceneの必須コンポーネントがない");
            Finish();
            yield break;
        }

        yield return new WaitForSecondsRealtime(4);
        Check(director.MusicTimeSeconds >= 0 && !director.IsPerformancePaused, "音楽とライブが自動再生される");
        Check(FindFirstObjectByType<TimelineReceiver>() == null
            && FindFirstObjectByType<AdminConsoleView>() == null
            && FindFirstObjectByType<Livisor.Device.DeviceCommandExample>() == null
            && FindFirstObjectByType<DemoSceneController>() == null, "サーバ・デバイス・Demo自動演出のコンポーネントがない");
        var viewInput = FindFirstObjectByType<QuestXRInput>();
        Check(viewInput != null && !viewInput.useRightHandButtons, "視点操作が左手に限定される");
        Check(ActiveBolts() == 0 && silver.ParticleCount == 0, "ボタンを押す前に雷や銀テープが出ない");
        Check(FindObjectsByType<UnityChan.IdleChanger>(FindObjectsSortMode.None).All(c => !c.enabled),
            "モデル付属のモーション確認用UIを表示しない");

        // 実機入力によるUpdateを止め、同じ入力処理にボタン状態を注入する。
        record.enabled = false;
        record.ProcessButtons(true, false);
        Check(ActiveBolts() == 1, "Aで雷が発火する");
        var target = GameObject.Find("Lightning Target").transform.position;
        Check(FindObjectsByType<LightningBolt>(FindObjectsSortMode.None)
            .Where(b => b.IsPlaying).All(b => Vector3.Distance(TestLightningPositions.Ground(b), target) < .001f),
            "雷が指定のターゲットに落ちる");
        record.ProcessButtons(true, false);
        Check(ActiveBolts() == 1, "Aの長押しで連射しない");
        record.ProcessButtons(false, false);
        record.ProcessButtons(true, false);
        Check(ActiveBolts() == 2, "Aを離して押し直すと再発火する");

        record.ProcessButtons(false, false);
        record.ProcessButtons(false, true);
        yield return new WaitForSecondsRealtime(1);
        var firstCount = silver.ParticleCount;
        Check(firstCount > 0, "Bで銀テープが発火する");
        record.ProcessButtons(false, true);
        yield return new WaitForSecondsRealtime(1);
        Check(silver.ParticleCount <= firstCount, "Bの長押しで追加発射しない");
        silver.Clear();
        record.ProcessButtons(false, false);
        record.ProcessButtons(true, true);
        Check(ActiveBolts() == 1, "A/Bの同時押しで雷が発火する");
        yield return new WaitForSecondsRealtime(1);
        Check(silver.ParticleCount > 0, "Bの再押下とA/Bの同時押しで銀テープが発火する");

        silver.Clear();
        director.EndPerformance();
        yield return new WaitForSecondsRealtime(1);
        Check(director.IsPerformancePaused && silver.ParticleCount == 0, "曲終了で銀テープを自動発射しない");
        record.ProcessButtons(false, false);
        record.ProcessButtons(true, true);
        Check(ActiveBolts() == 1, "曲終了後にも雷を発火できる");
        yield return new WaitForSecondsRealtime(1);
        Check(ActiveBolts() == 0, "一時停止中にも雷が最後まで再生されて消える");
        Check(silver.ParticleCount > 0, "曲終了後にも銀テープを発射できる");
        Finish();
    }

    void Finish()
    {
        var report = new Report { ok = _errors.Count == 0, checks = _checks.ToArray(), errors = _errors.ToArray() };
        var path = Path.Combine(Application.dataPath, "..", "Logs", "record-effects-check.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Debug.Log($"[SmokeRecord] {(report.ok ? "PASS" : "FAIL")} -> {path}");
#if UNITY_EDITOR
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(report.ok ? 0 : 1);
        else UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    [Serializable]
    sealed class Report
    {
        public bool ok;
        public string[] checks, errors;
    }
}
