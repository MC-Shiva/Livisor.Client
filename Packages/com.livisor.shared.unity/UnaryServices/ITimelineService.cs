using Livisor.Shared.DTO;
using MagicOnion;

namespace Livisor.Shared.UnaryServices
{
    /// <summary>
    /// タイムライン（再生トランスポートと予約アクション）の Unary 契約。
    /// 遅延なく確定させたい一度きりの操作（再生・停止・予約）をここで扱う。
    /// 変化し続ける値の同期は <c>Livisor.Shared.Hubs.IRoomStateHub</c> が担う。
    /// 確定した結果は応答で返すと同時に、同じ room の Hub 参加者へも通知される。
    /// </summary>
    public interface ITimelineService : IService<ITimelineService>
    {
        /// <summary>現在のトランスポートを取得する。</summary>
        UnaryResult<TransportState> GetTransportAsync(string roomId);

        /// <summary>再生を開始する。開始時刻はサーバーが確定する。再生中の呼び出しは開始時刻を動かさない。</summary>
        UnaryResult<TransportState> PlayAsync(string roomId);

        /// <summary>再生を停止する。</summary>
        UnaryResult<TransportState> StopAsync(string roomId);

        /// <summary>
        /// 予約アクションを 1 件登録する。既存の予約は置き換える。
        /// <paramref name="action"/> の Time は再生開始からの相対時間として扱う。
        /// </summary>
        UnaryResult<TransportState> ScheduleActionAsync(string roomId, TimelineAction action);

        /// <summary>予約アクションを取り消す。</summary>
        UnaryResult<TransportState> CancelScheduledActionAsync(string roomId);
    }
}
