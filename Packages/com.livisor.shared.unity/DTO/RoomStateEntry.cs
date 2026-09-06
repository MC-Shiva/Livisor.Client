using Livisor.Shared.Common;
using MessagePack;

namespace Livisor.Shared.DTO
{
    /// <summary>
    /// 状態同期の 1 項目。キーと値の組。
    /// 例: { "heartRate": 82 } / { "volume": 30 } / { "lightColor": "red" }
    /// </summary>
    [MessagePackObject]
    public class RoomStateEntry
    {
        /// <summary>項目名。既知のキーは <see cref="RoomStateKeys"/> を参照。</summary>
        [Key(0)]
        public string Key { get; set; } = string.Empty;

        /// <summary>項目の値。数値 / 真偽値 / 文字列のいずれか。</summary>
        [Key(1)]
        [MessagePackFormatter(typeof(ActionValueFormatter))]
        public ActionValue Value { get; set; }
    }
}
