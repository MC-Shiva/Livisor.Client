using System.Threading.Tasks;
using Livisor.Shared.DTO;
using MagicOnion;

namespace Livisor.Shared.Hubs
{
    /// <summary>
    /// 状態同期用の StreamingHub 契約。
    /// 心拍数や音量のように、リアルタイムに更新され続ける値を room 単位で共有する。
    /// 一度きりの操作（再生・停止・予約）は <c>Livisor.Shared.UnaryServices.ITimelineService</c> が担う。
    /// </summary>
    public interface IRoomStateHub : IStreamingHub<IRoomStateHub, IRoomStateHubReceiver>
    {
        /// <summary>
        /// 指定した room に参加し、参加時点の状態同期の全項目を受け取る（差分ではない）。
        /// 戻り値に再生トランスポートは含まない。再生中かどうかが必要なクライアントは、
        /// 参加後に <c>Livisor.Shared.UnaryServices.ITimelineService.GetTransportAsync</c> を 1 回呼ぶ。
        /// 以後の変化は <c>OnStateChanged</c> / <c>OnTransportChanged</c> で届く。
        /// </summary>
        ValueTask<RoomStatePatch> JoinAsync(string roomId);

        /// <summary>変化した項目だけを同じ room の全員へ反映する。</summary>
        ValueTask PublishAsync(RoomStateEntry[] entries);
    }
}
