using System;
using System.Threading.Tasks;
using Livisor.Shared.DTO;

/// <summary>
/// 送信側(Publisher)のインターフェース。
/// 接続して room に参加し、タイムラインを全受信者へブロードキャストする。
/// </summary>
public interface ITimelinePublisher : IAsyncDisposable
{
    /// <summary>接続して room に参加する。</summary>
    Task ConnectAsync(string serverAddress, string roomId);

    /// <summary>タイムライン配列を同じ room の全受信者へブロードキャストする。</summary>
    Task BroadcastAsync(TimelineAction[] actions);
}
