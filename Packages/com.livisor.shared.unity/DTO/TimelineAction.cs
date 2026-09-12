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
    /// 例: { "time": "00:01:00:00", "action": { "effect": "lightning" } }
    /// </summary>
    [MessagePackObject]
    public class TimelineAction
    {
        /// <summary>
        /// 曲の先頭を "00:00:00:00" とした再生位置。表記は "HH:mm:ss:ff"（時:分:秒:センチ秒）。
        /// "00:01:30:00" は曲の90秒地点を表す。LiveSceneは一時停止・再開後も音源の位置で実行する。
        /// 音源がないClientはサーバーから受けた経過時間を代わりに使う。
        /// </summary>
        [Key(0)]
        public string Time { get; set; } = string.Empty;

        /// <summary>操作の種類（play / volumeChange / effect）。</summary>
        [Key(1)]
        public ActionType Action { get; set; }

        /// <summary>操作に付随する値。play は bool、volumeChange は int、effect は演出名の文字列。</summary>
        [Key(2)]
        [MessagePackFormatter(typeof(ActionValueFormatter))]
        public ActionValue Value { get; set; }
    }
}
