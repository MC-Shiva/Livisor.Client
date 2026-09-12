using MessagePack;

namespace Livisor.Shared.DTO
{
    /// <summary>受信時に1回実行する演出。予約キューや参加時の状態には保存しない。</summary>
    [MessagePackObject]
    public class EffectCommand
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        /// <summary>雷の対象名。雷以外は空文字列。</summary>
        [Key(1)]
        public string Target { get; set; } = string.Empty;
    }
}
