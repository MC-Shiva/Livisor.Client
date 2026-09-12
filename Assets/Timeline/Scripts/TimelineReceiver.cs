using System;
using System.Collections.Concurrent;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using Livisor.Device;
using UnityEngine;

/// <summary>
/// Server の演出キューを TimelineActionPlayback に渡し、曲の再生位置で実行する。
/// 受信した状態は Update でメインスレッドに反映する。
/// </summary>
public class TimelineReceiver : MonoBehaviour
{
    [SerializeField] private ServerConfig _serverConfig;
    [SerializeField] private string _roomId = "room1";

    // 未設定ならシーン内の StageDirector を探し、それも無ければログ出力だけの仮実装にする。
    [SerializeField] private StageDirector _stageDirector;
    [SerializeField] private DeviceCommandExample _device;

    private IRoomClient _client;
    private IMediaPlayer _player;

    private EffectDispatcher _effects;

    // 受信は非メインスレッドで起きるため、メインスレッド（Update）で処理するためのキュー。
    private readonly ConcurrentQueue<TransportState> _pendingTransports = new();
    private readonly ConcurrentQueue<RoomStatePatch> _pendingStates = new();

    private readonly TimelineActionPlayback _playback = new();
    private bool _playing;
    private double _receivedPosition;
    private double _receivedAt;

    async void Start()
    {
        _player = CreateMediaPlayer();
        if (_stageDirector != null)
            _effects = new EffectDispatcher(_stageDirector);

        _client = new RoomClient();
        _client.TransportChanged += OnTransportChanged;
        _client.StateChanged += OnStateChanged;

        try
        {
            await _client.ConnectAsync(_serverConfig.ServerAddress, _roomId);
            Debug.Log($"[Receiver] joined room '{_roomId}'");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private IMediaPlayer CreateMediaPlayer()
    {
        if (_stageDirector == null)
            _stageDirector = FindFirstObjectByType<StageDirector>();

        if (_stageDirector != null)
            return new StageMediaPlayer(_stageDirector);

        Debug.LogWarning("[Receiver] StageDirector が無いため、ログ出力だけの LoggingMediaPlayer を使う");
        return new LoggingMediaPlayer();
    }

    // 非メインスレッドの可能性があるため、ここではキューへの追加のみ。
    private void OnTransportChanged(TransportState state) => _pendingTransports.Enqueue(state);

    private void OnStateChanged(RoomStatePatch patch) => _pendingStates.Enqueue(patch);

    void Update()
    {
        while (_pendingStates.TryDequeue(out var patch))
            ApplyState(patch);

        while (_pendingTransports.TryDequeue(out var state))
            ApplyTransport(state);

        if (_playing)
            _playback.Advance(_stageDirector != null ? _stageDirector.MusicTimeSeconds
                : _receivedPosition + Time.realtimeSinceStartupAsDouble - _receivedAt, Dispatch);
    }

    // 状態の差分を反映する。いま扱うのは音量だけ。他のキー（心拍数・照明色など）は無視する。
    private void ApplyState(RoomStatePatch patch)
    {
        foreach (var entry in patch.Entries)
        {
            if (entry.Key == RoomStateKeys.Volume)
            {
                _player.ChangeVolume(entry.Value);
                if (entry.Value.Kind == ActionValueKind.Number && _device != null)
                    _device.SetVolume(entry.Value.Number);
            }
        }
    }

    private void ApplyTransport(TransportState state)
    {
        Debug.Log($"[Receiver] transport: playing={state.Playing} actions={state.Actions.Length}");
        _playback.Load(state.Actions);
        _playing = state.Playing;
        _receivedPosition = Math.Max(0, (state.ServerTimeMs - state.StartedAtServerMs) / 1000.0);
        _receivedAt = Time.realtimeSinceStartupAsDouble;
        _player.Play(state.Playing);
        if (_device != null) _device.ApplyPlaying(state.Playing);
    }

    // 予約アクションを実行する。
    private async void Dispatch(TimelineAction action)
    {
        switch (action.Action)
        {
            case ActionType.Play:
                // play は true=再生 / false=停止。bool 以外が来ると Value.Bool が既定値 false になり
                // 「再生のつもりが停止」になるため、種別を確かめてから渡す。
                if (action.Value.Kind != ActionValueKind.Bool)
                {
                    Debug.LogWarning($"[Receiver] play の値が bool ではないため無視する (Kind={action.Value.Kind})");
                    break;
                }
                _player.Play(action.Value.Bool);
                if (_device != null) _device.ApplyPlaying(action.Value.Bool);
                break;

            case ActionType.VolumeChange:
                _player.ChangeVolume(action.Value);
                if (action.Value.Kind == ActionValueKind.Number && _device != null)
                    _device.SetVolume(action.Value.Number);
                // 予約で変えた音量は状態同期にも書き戻す（RoomStateKeys.Volume のコメント参照）。
                // 全員が同じ予約を持っているので、それぞれが同じ値を publish する。
                try
                {
                    await _client.PublishStateAsync(new RoomStateEntry { Key = RoomStateKeys.Volume, Value = action.Value });
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                break;

            case ActionType.Effect:
                // 演出名は文字列。StageDirector が無いシーンでは実行先が無いのでログだけ出す。
                if (action.Value.Kind != ActionValueKind.Text)
                {
                    Debug.LogWarning($"[Receiver] effect の値が文字列ではないため無視する (Kind={action.Value.Kind})");
                    break;
                }
                if (_effects == null)
                {
                    Debug.Log($"[Effect] {action.Value.Text} (StageDirector が無いため実行しない)");
                    break;
                }
                _effects.Fire(action.Value.Text);
                break;
        }
    }

    async void OnDestroy()
    {
        if (_client != null)
        {
            _client.TransportChanged -= OnTransportChanged;
            _client.StateChanged -= OnStateChanged;
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
