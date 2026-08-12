using System;
using System.Threading.Tasks;
using Livisor.Shared.DTO;

/// <summary>
/// 受信側(Subscriber)のインターフェース。
/// 接続して room に参加し、ブロードキャストされたタイムラインを <see cref="TimelineReceived"/> で受け取る。
/// </summary>
public interface ITimelineSubscriber : IAsyncDisposable
{
    /// <summary>サーバからタイムラインがブロードキャストされたときに発火する。呼び出しは非メインスレッドの可能性がある。</summary>
    event Action<TimelineAction[], long> TimelineReceived;

    /// <summary>接続して room に参加する。</summary>
    Task ConnectAsync(string serverAddress, string roomId);
}
