using System;
using System.Threading.Tasks;
using Livisor.Shared.DTO;
using Livisor.Shared.Hubs;
using MagicOnion;
using MagicOnion.Client;

/// <summary>
/// 通信アダプタ: MagicOnion StreamingHub への接続を閉じ込める。
/// gRPC/MagicOnion への依存をここに集約し、上位の MonoBehaviour はこのクラスだけを触る。
/// サーバからのブロードキャスト受信は <see cref="TimelineReceived"/> イベントで上位に通知する。
/// <see cref="ITimelinePublisher"/>（送信）と <see cref="ITimelineSubscriber"/>（受信）を両方実装する。
/// </summary>
public class TimelineHubClient : ITimelinePublisher, ITimelineSubscriber, ITimelineHubReceiver, IAsyncDisposable
{
    private GrpcChannelx _channel;
    private ITimelineHub _hub;

    /// <summary>
    /// サーバからタイムラインがブロードキャストされたときに発火する。
    /// 呼び出しスレッドは非メインスレッドの可能性があるため、購読側でメインスレッドへ整流すること。
    /// </summary>
    public event Action<TimelineAction[], long> TimelineReceived;

    /// <summary>接続して room に参加する。</summary>
    public async Task ConnectAsync(string serverAddress, string roomId)
    {
        _channel = GrpcChannelx.ForAddress(serverAddress);
        _hub = await StreamingHubClient.ConnectAsync<ITimelineHub, ITimelineHubReceiver>(_channel, this);
        await _hub.JoinAsync(roomId);
    }

    /// <summary>タイムライン配列を同じ room の配信対象へ一括配信する。</summary>
    public async Task BroadcastAsync(TimelineAction[] actions)
    {
        await _hub.BroadcastTimelineAsync(actions);
    }

    // === ITimelineHubReceiver（サーバからのプッシュ受信）===
    public void OnBroadcastTimeline(TimelineAction[] actions, long broadcastAtMs)
    {
        TimelineReceived?.Invoke(actions, broadcastAtMs);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }

        _channel?.Dispose();
        _channel = null;
    }
}
