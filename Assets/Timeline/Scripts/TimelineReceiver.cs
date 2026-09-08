using System;
using System.Collections;
using System.Collections.Concurrent;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using Livisor.Device;
using UnityEngine;

/// <summary>
/// 受信側クライアント（薄い glue）。
/// 通信は <see cref="IRoomClient"/>、発火の計算は <see cref="TimelinePlayback"/>、
/// 実際の操作は <see cref="IMediaPlayer"/> に委譲する。
/// サーバアドレスは <see cref="ServerConfig"/> で一元管理する。
/// このクラスの責務は Unity ライフサイクル・メインスレッド整流・配線のみ。
///
/// サーバーから届くものは 2 種類ある。
///   - トランスポート（再生中かどうか・予約 1 件）: <see cref="IRoomClient.TransportChanged"/>
///   - 状態の差分（音量など）: <see cref="IRoomClient.StateChanged"/>
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

    // 受信は非メインスレッドで起きるため、メインスレッド（Update）で処理するためのキュー。
    private readonly ConcurrentQueue<TransportState> _pendingTransports = new();
    private readonly ConcurrentQueue<RoomStatePatch> _pendingStates = new();

    // 予約の発火待ち。トランスポートが変わるたびに張り直す（古い予約が二重に発火しないように）。
    private Coroutine _pendingAction;

    async void Start()
    {
        _player = CreateMediaPlayer();

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

    // トランスポートを反映する。再生・停止を切り替え、予約があれば発火のタイマーを張り直す。
    private void ApplyTransport(TransportState state)
    {
        Debug.Log($"[Receiver] transport: playing={state.Playing} scheduled={(state.ScheduledAction == null ? "none" : state.ScheduledAction.Time)}");

        // 停止中は基準時刻が無いので予約タイマーを取り消す。次に再生になったら張り直す（TransportState のコメント参照）。
        if (_pendingAction != null)
        {
            StopCoroutine(_pendingAction);
            _pendingAction = null;
        }

        _player.Play(state.Playing);
        if (_device != null) _device.ApplyPlaying(state.Playing);

        if (TimelinePlayback.TryGetPendingAction(state, out var delaySeconds, out var action))
            _pendingAction = StartCoroutine(FireAfter(delaySeconds, action));
    }

    private IEnumerator FireAfter(double delaySeconds, TimelineAction action)
    {
        // Time.timeScale に影響されないよう実時間で待つ（停止中に timeScale を 0 にする実装があるため）。
        yield return new WaitForSecondsRealtime((float)delaySeconds);
        _pendingAction = null;
        Dispatch(action);
    }

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
