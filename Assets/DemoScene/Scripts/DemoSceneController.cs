using System;
using System.Collections.Generic;
using Livisor.Device;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// サーバーを使わないDemoScene専用のライブ操作。
/// StageDirectorの通常の再生経路を使い、音楽と演出のタイミングを維持する。
/// QuestとキーボードのX/Y/Aで雷・銀テープ・音量を操作する。
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class DemoSceneController : MonoBehaviour
{
    [SerializeField]
    StageDirector _stageDirector;

    [SerializeField, Tooltip("Play Mode開始時にライブを自動再生する。")]
    bool _playOnStart = true;

    [SerializeField, Tooltip("音楽の再生・停止・音量を送る振動デバイス。未設定なら単独で再生する。")]
    DeviceCommandExample _device;

    [Header("Demo lightning area")]
    [Tooltip("雷を落とすステージ範囲の中心。")]
    public Transform lightningStagePoint;
    [SerializeField, Tooltip("ステージ上のランダム範囲の幅と奥行き（m）。")]
    Vector2 _lightningAreaSize = new(4, 4);

    readonly Queue<string> _recentEffects = new();
    LightningVfxController _lightning;
    bool _xWasPressed, _yWasPressed, _aWasPressed;
    double _positionSeconds;
    bool _finished;
    int _deviceVolume = -1;
    bool _devicePlaying;
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

        _stageDirector.FireSilverStreamersOnEnd = false;
        _lightning = GetComponent<LightningVfxController>();
        if (_lightning == null) _lightning = gameObject.AddComponent<LightningVfxController>();
        _lightning.ConfigureRandomArea(lightningStagePoint,
            new Vector3(_lightningAreaSize.x, 0, _lightningAreaSize.y));

        if (_playOnStart)
            ResumePerformance();

        Debug.Log("[DemoScene] X=lightning, Y=silver streamers, A=volume 30%/100%, S=resume, P=pause", this);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
            ResumePerformance();
        if (Input.GetKeyDown(KeyCode.P))
            PausePerformance();

        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        left.TryGetFeatureValue(CommonUsages.primaryButton, out var x);
        left.TryGetFeatureValue(CommonUsages.secondaryButton, out var y);
        right.TryGetFeatureValue(CommonUsages.primaryButton, out var a);
        HandleButtons(x || Input.GetKey(KeyCode.X), y || Input.GetKey(KeyCode.Y), a || Input.GetKey(KeyCode.A));
        SyncDevice();

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
    }

    void SyncDevice()
    {
        if (_device == null || !_device.IsReachable)
        {
            _deviceVolume = -1;
            return;
        }

        var source = _stageDirector.MusicPlayerController.MainSource;
        var firstSync = _deviceVolume < 0;
        var volume = Mathf.RoundToInt(source.volume * 100);
        if (_deviceVolume != volume)
        {
            _device.SetVolume(volume);
            _deviceVolume = volume;
        }
        // 実際に音楽が鳴り始めた時点で送る。初回のAnimation Event待ちも含む。
        if (firstSync || _devicePlaying != source.isPlaying)
        {
            _device.ApplyPlaying(source.isPlaying);
            _devicePlaying = source.isPlaying;
        }
    }

    void OnDisable()
    {
        if (_device != null && _device.IsReachable)
            _device.ApplyPlaying(false);
        _deviceVolume = -1;
    }

    internal void HandleButtons(bool x, bool y, bool a)
    {
        if (x && !_xWasPressed) FireLightning();
        if (y && !_yWasPressed) FireSilverStreamers();
        if (a && !_aWasPressed) ToggleVolume();
        _xWasPressed = x;
        _yWasPressed = y;
        _aWasPressed = a;
    }

    public void FireLightning()
    {
        _lightning.Strike(useUnscaledTime: true);
        RecordEffect("雷");
    }

    public void FireSilverStreamers()
    {
        _stageDirector.FireSilverStreamers();
        RecordEffect("銀テープ");
    }

    public void ToggleVolume()
    {
        var percent = _stageDirector.MusicPlayerController.MainSource.volume > 0.5f ? 30 : 100;
        _stageDirector.SetMainVolume(percent);
        RecordEffect($"音量 {percent}%");
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
            + $"音量: {(source != null ? source.volume * 100 : 0):0}%\n"
            + $"振動デバイス: {DeviceStatus}\n"
            + "X: 雷  Y: 銀テープ  A: 音量30%/100%\n\n直近の操作（曲内の実行時刻）\n"
            + (_recentEffects.Count == 0 ? "まだ発火していません" : string.Join("\n", _recentEffects));
    }

    string DeviceStatus => _device == null || !_device.isActiveAndEnabled ? "無効"
        : !string.IsNullOrEmpty(_device.LastError) ? "通信エラー"
        : _device.IsReachable ? "接続済み" : "接続確認中";

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

    void RecordEffect(string name)
    {
        if (_recentEffects.Count == 5) _recentEffects.Dequeue();
        _recentEffects.Enqueue($"{FormatTime(_positionSeconds)}  {name}");
        Debug.Log($"[DemoScene] {name}", this);
    }
}
