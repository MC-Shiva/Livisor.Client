using System;
using System.Threading.Tasks;
using Livisor.Shared.DTO;

/// <summary>
/// サーバーとの通信を 1 つにまとめた窓口。
/// 一度きりの操作（再生・停止・予約）は Unary の <c>ITimelineService</c>、
/// 変わり続ける値（音量など）の配信は StreamingHub の <c>IRoomStateHub</c> を使う。
/// 呼び出し側（管理者画面・受信側）は MagicOnion を直接触らず、この窓口だけを使う。
/// </summary>
public interface IRoomClient : IAsyncDisposable
{
    /// <summary>
    /// サーバーから再生トランスポート（再生中かどうか・予約）が届いたときに発火する。
    /// 接続直後に現在値が 1 回届き、以後は変化のたびに届く。
    /// 呼び出しスレッドは非メインスレッドの可能性があるため、購読側でメインスレッドへ整流すること。
    /// </summary>
    event Action<TransportState> TransportChanged;

    /// <summary>
    /// 状態同期（音量・心拍数など）の差分が届いたときに発火する。
    /// 接続直後に参加時点の全項目が 1 回届き、以後は変化した項目だけが届く。
    /// 呼び出しスレッドは非メインスレッドの可能性がある。
    /// </summary>
    event Action<RoomStatePatch> StateChanged;

    /// <summary>接続して room に参加する。参加時点の状態とトランスポートは上のイベントで届く。</summary>
    Task ConnectAsync(string serverAddress, string roomId);

    /// <summary>再生を開始する。開始時刻はサーバーが確定する。</summary>
    Task<TransportState> PlayAsync();

    /// <summary>再生を停止する。予約は残る。</summary>
    Task<TransportState> StopAsync();

    /// <summary>一覧をキューに追加する。Time は曲の先頭からの位置。</summary>
    Task<TransportState> ScheduleActionsAsync(params TimelineAction[] actions);

    /// <summary>追加予約をすべて取り消す。デフォルト演出は残す。</summary>
    Task<TransportState> CancelScheduledActionsAsync();

    /// <summary>変化した項目を同じ room の全員へ配る。自分にも <see cref="StateChanged"/> で戻ってくる。</summary>
    Task PublishStateAsync(params RoomStateEntry[] entries);
}
