using System;
using Livisor.Shared.Common;
using Livisor.Shared.DTO;
using UnityEngine;

/// <summary>
/// 送信側クライアント（薄い glue）。通信は <see cref="TimelineHubClient"/> に委譲し、
/// タイムライン配列を同じ room の全受信者へブロードキャストする。
/// サーバアドレスは <see cref="ServerConfig"/> で一元管理する。
/// </summary>
public class TimelineBroadcaster : MonoBehaviour
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
            Debug.Log($"[Broadcaster] joined room '{_roomId}'");

            await _client.BroadcastAsync(BuildTimeline());
            Debug.Log("[Broadcaster] timeline distributed");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

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
