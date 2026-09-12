using System;
using System.Collections.Generic;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;

/// <summary>
/// 通信を使わないDemoScene専用のライブ操作。
/// StageDirectorの通常の再生経路を使い、音楽と演出のタイミングを維持する。
/// デフォルト演出（Issue #22）はサーバーを介さず、Shared の <see cref="DefaultTimeline"/> を直接読んで
/// 音楽の再生位置で発火する。
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class DemoSceneController : MonoBehaviour
{
    [SerializeField]
    StageDirector _stageDirector;

    [SerializeField, Tooltip("Play Mode開始時にライブを自動再生する。")]
    bool _playOnStart = true;

    readonly TimelineActionPlayback _playback = new();
    readonly Queue<string> _recentEffects = new();
    EffectDispatcher _effects;
    double _positionSeconds;
    bool _confettiOn;
    bool _finished;
    GUIStyle _statusStyle;

    void Awake()
    {
        if (_stageDirector == null)
            _stageDirector = GetComponent<StageDirector>();
    }

    void Start()
    {
        if (_stageDirector == null)
        {
            Debug.LogError("DemoSceneController requires StageDirector.", this);
            enabled = false;
            return;
        }

        _effects = new EffectDispatcher(_stageDirector);
        _playback.Load(DefaultTimeline.Create());
        Debug.Log($"[DemoScene] default actions loaded: {_playback.Count}", this);

        if (_playOnStart)
            ResumePerformance();

        Debug.Log("[DemoScene] S=resume, P=pause, T=silver streamers, E=finale", this);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
            ResumePerformance();
        if (Input.GetKeyDown(KeyCode.T))
            _stageDirector.FireSilverStreamers();
        if (Input.GetKeyDown(KeyCode.E))
            _stageDirector.EndPerformance();
        if (Input.GetKeyDown(KeyCode.P))
            PausePerformance();

        var position = _stageDirector.MusicTimeSeconds;
        if (position >= 0)
        {
            _positionSeconds = position;
            _finished = false;
        }
        else if (_positionSeconds > 0 && !_stageDirector.IsPerformancePaused)
        {
            _positionSeconds = _stageDirector.MusicPlayerController.MainSource.clip.length;
            _finished = true;
        }
        _playback.Advance(position, Fire);
    }

    void OnGUI()
    {
        if (_stageDirector == null)
            return;

        _statusStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 18,
            padding = new RectOffset(12, 12, 10, 10),
        };
        var text = GetStatusText();
        var width = Mathf.Min(440, Screen.width - 24);
        GUI.Box(new Rect(12, 12, width, _statusStyle.CalcHeight(new GUIContent(text), width)), text, _statusStyle);
    }

    internal string GetStatusText()
    {
        var source = _stageDirector.MusicPlayerController?.MainSource;
        var duration = source != null && source.clip != null ? source.clip.length : 0;
        var state = source != null && source.isPlaying ? "再生中"
            : _finished ? "終了"
            : _stageDirector.IsPerformancePaused ? "一時停止" : "待機中";
        return $"DEMO  {FormatTime(_positionSeconds)} / {FormatTime(duration)}  {state}\n"
            + $"紙吹雪: {(_confettiOn ? "ON" : "OFF")}\n\n直近の演出（曲内の実行時刻）\n"
            + (_recentEffects.Count == 0 ? "まだ発火していません" : string.Join("\n", _recentEffects));
    }

    static string FormatTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss\.ff");

    public void ResumePerformance()
    {
        _stageDirector.ResumePerformance();
        Debug.Log("[DemoScene] Resume", this);
    }

    public void PausePerformance()
    {
        _stageDirector.PausePerformance();
        Debug.Log("[DemoScene] Pause", this);
    }

    // Shared の定義を実行する。
    void Fire(TimelineAction action)
    {
        switch (action.Action)
        {
            case ActionType.Effect:
                if (action.Value.Kind == ActionValueKind.Text)
                {
                    _effects.Fire(action.Value.Text);
                    var name = action.Value.Text switch
                    {
                        EffectNames.ConfettiOn => "紙吹雪 ON",
                        EffectNames.ConfettiOff => "紙吹雪 OFF",
                        EffectNames.Lightning => "雷",
                        EffectNames.SilverStreamer => "銀テープ",
                        _ => action.Value.Text,
                    };
                    if (action.Value.Text == EffectNames.ConfettiOn) _confettiOn = true;
                    if (action.Value.Text == EffectNames.ConfettiOff) _confettiOn = false;
                    if (_recentEffects.Count == 5) _recentEffects.Dequeue();
                    _recentEffects.Enqueue($"{FormatTime(_positionSeconds)}  {name}");
                }
                break;
            case ActionType.Play:
                if (action.Value.Kind == ActionValueKind.Bool)
                {
                    if (action.Value.Bool) ResumePerformance(); else PausePerformance();
                }
                break;
            case ActionType.VolumeChange:
                if (action.Value.Kind == ActionValueKind.Number)
                    _stageDirector.SetMainVolume(action.Value.Number);
                break;
        }
    }
}
