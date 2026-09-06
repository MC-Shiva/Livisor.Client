using MessagePack;

namespace Livisor.Shared.DTO
{
    /// <summary>
    /// 状態同期の差分。変化した項目だけを載せる。
    /// 項目は可変長で、{ heartRate, volume, lightColor, ... } を同じ形で運ぶ。
    /// ただし <c>Livisor.Shared.Hubs.IRoomStateHub.JoinAsync</c> の応答だけは、参加時点の全項目が入る。
    /// </summary>
    [MessagePackObject]
    public class RoomStatePatch
    {
        /// <summary>変化した項目。</summary>
        [Key(0)]
        public RoomStateEntry[] Entries { get; set; } = new RoomStateEntry[0];

        /// <summary>サーバーがこの差分を確定した時刻（UTC ミリ秒）。</summary>
        [Key(1)]
        public long ServerTimeMs { get; set; }
    }
}
