using Livisor.Shared.Common;
using MessagePack;

namespace Livisor.Shared.DTO
{
    /// <summary>
    /// タイムライン上の 1 アクション。Issue #11 で確定した JSON 配列の 1 要素に対応する。
    /// ワイヤ形式は Issue #11 当時と同じだが、Time の意味だけが変わっている（下記）。
    /// 例: { "time": "00:00:30:00", "action": { "play": true } }
    /// 例: { "time": "00:01:00:00", "action": { "play": false } }
    /// 例: { "time": "00:02:15:50", "action": { "volumeChange": 10 } }
    /// </summary>
    [MessagePackObject]
    public class TimelineAction
    {
        /// <summary>
        /// 再生開始を "00:00:00:00" とした相対時間。表記 "HH:mm:ss:ff"（時:分:秒:センチ秒）はそのまま保持する。
        /// 絶対時刻ではない。"00:01:30:00" は「再生開始の 90 秒後」を表す。
        /// 2026-08-29 の決定でこの意味になった。それ以前は絶対時刻だった。
        /// 形式が同じで意味だけ変わったため、絶対時刻を送る古いクライアントはエラーにならず
        /// 別の時刻で発火する。送る側は必ず相対時間へ直す。
        /// </summary>
        [Key(0)]
        public string Time { get; set; } = string.Empty;

        /// <summary>操作の種類（play / volumeChange）。</summary>
        [Key(1)]
        public ActionType Action { get; set; }

        /// <summary>操作に付随する値。play=true（再生）/ play=false（停止）/ volumeChange=10 など。</summary>
        [Key(2)]
        [MessagePackFormatter(typeof(ActionValueFormatter))]
        public ActionValue Value { get; set; }
    }
}
