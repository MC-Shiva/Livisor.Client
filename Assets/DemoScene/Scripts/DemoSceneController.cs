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
    EffectDispatcher _effects;

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

        _playback.Advance(_stageDirector.MusicTimeSeconds, Fire);
    }

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
                    _effects.Fire(action.Value.Text);
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
