using System;
using System.Collections;
using System.Collections.Concurrent;
using Livisor.Shared.DTO;
using UnityEngine;

/// <summary>
/// 受信側クライアント（薄い glue）。
/// 通信は <see cref="TimelineHubClient"/>、再生スケジュールは <see cref="TimelinePlayback"/>、
/// 実際の操作は <see cref="IMediaPlayer"/> に委譲する。
/// サーバアドレスは <see cref="ServerConfig"/> で一元管理する。
/// このクラスの責務は Unity ライフサイクル・メインスレッド整流・配線のみ。
/// </summary>
public class TimelineReceiver : MonoBehaviour
{
    [SerializeField] private ServerConfig _serverConfig;
    [SerializeField] private string _roomId = "room1";

    private ITimelineSubscriber _client;
    private IMediaPlayer _player;

    // 受信したタイムラインをメインスレッドで順に再生するためのキュー。broadcastAtMs は絶対時間再生の基準。
    private readonly ConcurrentQueue<(TimelineAction[] actions, long broadcastAtMs)> _pendingQueue = new();

    async void Start()
    {
        // 一旦仮でLoggingのみ
        _player = new LoggingMediaPlayer();
        _client = new TimelineHubClient();
        _client.TimelineReceived += OnTimelineReceived;

        try
        {
            await _client.ConnectAsync(_serverConfig.ServerAddress, _roomId);
            Debug.Log($"[Receiver] joined room '{_roomId}', waiting for timeline...");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // 非メインスレッドの可能性があるため、ここではキューへの追加のみ。再生は Update（メインスレッド）で。
    private void OnTimelineReceived(TimelineAction[] actions, long broadcastAtMs)
    {
        Debug.Log($"[Receiver] received {actions.Length} actions");
        _pendingQueue.Enqueue((actions, broadcastAtMs));
    }

    void Update()
    {
        while (_pendingQueue.TryDequeue(out var item))
            StartCoroutine(PlayTimeline(item.actions, item.broadcastAtMs));
    }

    // スケジュール計算は純粋ロジックへ委譲し、ここは待機とディスパッチだけを担う。
    // broadcastAtMs を基準とした絶対 UTC 時刻で発火させ、複数受信者が同時にアクションを実行。
    private IEnumerator PlayTimeline(TimelineAction[] actions, long broadcastAtMs)
    {
        var schedule = TimelinePlayback.BuildSchedule(actions);

        foreach (var scheduled in schedule)
        {
            long targetMs = broadcastAtMs + (long)(scheduled.DelaySeconds * 1000);
            while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < targetMs)
                yield return null;

            Dispatch(scheduled.Action);
        }
    }

    private void Dispatch(TimelineAction action)
    {
        switch (action.Action)
        {
            case ActionType.Start:
                _player.Start(action.Value);
                break;
            case ActionType.Stop:
                _player.Stop(action.Value);
                break;
            case ActionType.VolumeChange:
                _player.ChangeVolume(action.Value);
                break;
        }
    }

    async void OnDestroy()
    {
        if (_client != null)
        {
            _client.TimelineReceived -= OnTimelineReceived;
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
