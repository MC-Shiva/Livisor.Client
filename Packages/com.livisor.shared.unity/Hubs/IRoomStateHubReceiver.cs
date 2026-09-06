using Livisor.Shared.DTO;

namespace Livisor.Shared.Hubs
{
    /// <summary>
    /// サーバ → クライアントへのコールバック（受信）契約。
    /// StreamingHub の受信側インターフェース。
    /// </summary>
    public interface IRoomStateHubReceiver
    {
        /// <summary>状態が変化したときに、変化した項目だけを受け取る。受信したら即座に反映する。</summary>
        void OnStateChanged(RoomStatePatch patch);

        /// <summary>
        /// 再生トランスポートが変化したときに受け取る。
        /// Unary の再生・停止・予約はサーバーで確定したあと、ここから同じ room の全員へ届く。
        /// </summary>
        void OnTransportChanged(TransportState state);
    }
}
