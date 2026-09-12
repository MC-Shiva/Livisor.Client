using System;
using System.Threading.Tasks;
using Livisor.Shared.DTO;
using Livisor.Shared.Hubs;
using Livisor.Shared.UnaryServices;
using MagicOnion;
using MagicOnion.Client;

/// <summary>
/// <see cref="IRoomClient"/> の MagicOnion 実装。
/// 1 本の gRPC チャネルを Unary サービスと StreamingHub で共有する。
/// サーバーからの押し出しは StreamingHub だけなので、Unary で確定した結果も
/// <see cref="OnTransportChanged"/> 経由で届く（呼び出し元には戻り値でも返る）。
/// </summary>
public class RoomClient : IRoomClient, IRoomStateHubReceiver
{
    private GrpcChannelx _channel;
    private ITimelineService _timeline;
    private IRoomStateHub _hub;
    private string _roomId;

    public event Action<TransportState> TransportChanged;
    public event Action<RoomStatePatch> StateChanged;

    public async Task ConnectAsync(string serverAddress, string roomId)
    {
        _roomId = roomId;
        _channel = GrpcChannelx.ForAddress(serverAddress);
        _timeline = MagicOnionClient.Create<ITimelineService>(_channel);
        _hub = await StreamingHubClient.ConnectAsync<IRoomStateHub, IRoomStateHubReceiver>(_channel, this);

        // 参加すると、参加時点の全項目が応答で返る（差分ではない）。以後の変化は OnStateChanged で届く。
        var initialState = await _hub.JoinAsync(roomId);
        StateChanged?.Invoke(initialState);

        // 参加の応答にはトランスポートが含まれない（IRoomStateHub の契約）。1 回だけ Unary で取りに行く。
        var transport = await _timeline.GetTransportAsync(roomId);
        TransportChanged?.Invoke(transport);
    }

    public async Task<TransportState> PlayAsync() => await _timeline.PlayAsync(_roomId);

    public async Task<TransportState> StopAsync() => await _timeline.StopAsync(_roomId);

    public async Task<TransportState> ScheduleActionsAsync(params TimelineAction[] actions)
        => await _timeline.ScheduleActionsAsync(_roomId, actions);

    public async Task<TransportState> CancelScheduledActionsAsync()
        => await _timeline.CancelScheduledActionsAsync(_roomId);

    public async Task PublishStateAsync(params RoomStateEntry[] entries)
        => await _hub.PublishAsync(entries);

    // === IRoomStateHubReceiver（サーバーからの押し出し）===

    public void OnStateChanged(RoomStatePatch patch) => StateChanged?.Invoke(patch);

    public void OnTransportChanged(TransportState state) => TransportChanged?.Invoke(state);

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }

        _timeline = null;
        _channel?.Dispose();
        _channel = null;
    }
}
