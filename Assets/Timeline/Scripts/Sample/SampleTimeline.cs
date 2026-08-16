using System;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;

/// <summary>
/// 動作確認用テストスクリプト。
/// GameObject にアタッチし、スペースキーでサンプルタイムラインを配信する。
/// </summary>
public class SampleTimeline : MonoBehaviour
{
    [SerializeField] private ServerConfig _serverConfig;
    [SerializeField] private string _roomId = "room1";

    private ITimelinePublisher _client;

    async void Start()
    {
        _client = new TimelineHubClient();
        try
        {
            await _client.ConnectAsync(_serverConfig.ServerAddress, _roomId);
            Debug.Log("[SampleTimeline] Connected. Press Space to broadcast.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            Broadcast();
    }

    async void Broadcast()
    {
        try
        {
            await _client.BroadcastAsync(BuildTimeline());
            Debug.Log("[SampleTimeline] Broadcast sent.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ※ 動作確認では待ち時間が長くならないよう time を短く調整してよい。
    private static TimelineAction[] BuildTimeline() => new[]
    {
        new TimelineAction { Time = "10:00:00:00", Action = ActionType.Play, Value = true },
        new TimelineAction { Time = "10:00:03:00", Action = ActionType.VolumeChange, Value = 10 },
        new TimelineAction { Time = "10:00:06:00", Action = ActionType.Play, Value = false },
    };

    async void OnDestroy()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }
}
