namespace Livisor.Shared.Common
{
    /// <summary>
    /// 状態同期で使う既知のキー。Client / Server 共通の語彙。
    /// 同期する項目は公演ごとに増えるため、ここに無いキー（照明色など）も送受信できる。
    /// 再生中かどうかは <c>ITimelineService</c> が確定させるトランスポートが正であり、
    /// この状態には含めない（二重管理を避けるため）。
    /// ただし未知のキーは素通しするので <c>playing</c> を送っても拒否されない。送らない約束で運用する。
    /// Issue #18 は同期対象に「再生中」を挙げているが、2026-08-29 の整理で採用していない。
    /// </summary>
    public static class RoomStateKeys
    {
        /// <summary>
        /// 音量の大きさ。数値。現在値はこの状態が正。
        /// <c>ITimelineService.ScheduleActionAsync</c> の volumeChange は「再生開始から相対 t 後に
        /// 音量を変える」予約であり、発火したクライアントが結果をこの状態へ publish する。
        /// </summary>
        public const string Volume = "volume";

        /// <summary>心拍数。数値。</summary>
        public const string HeartRate = "heartRate";
    }
}
