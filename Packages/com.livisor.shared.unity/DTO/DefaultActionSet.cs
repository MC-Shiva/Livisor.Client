using Livisor.Shared.Common;

namespace Livisor.Shared.DTO
{
    /// <summary>
    /// Server と DemoScene が共通で使う事前定義。Server は起動時、Demo は直接読み込む。
    /// Create の各行は At(曲の先頭からの時刻, 操作, 値)。時刻は "HH:mm:ss:ff"（末尾はセンチ秒）。
    /// 音量なら At("00:00:30:00", ActionType.VolumeChange, 50)、停止なら ActionType.Play, false と書く。
    /// 変更後はリポジトリ直下で make shared/sync を実行してから、Client を再ビルドする。
    /// 時刻は仮値。クライアントは時刻順に実行し、一時停止中は進めない。
    /// Demo の時刻は音源の終了前に置く。
    /// 銀テープは寿命 12 秒と射出待ち 0.6 秒があるため、終了時の射出要求まで 13 秒以上空ける。
    /// </summary>
    public static class DefaultActionSet
    {
        /// <summary>
        /// デフォルト演出を返す。DTO は可変なので、呼び出しごとに新しいインスタンスを作る。
        /// </summary>
        public static TimelineAction[] Create() => new[]
        {
            At("00:00:00:50", ActionType.Effect, EffectNames.ConfettiOn),
            At("00:01:00:00", ActionType.Effect, EffectNames.Lightning),
            At("00:02:10:00", ActionType.Effect, EffectNames.Lightning),
            At("00:03:10:00", ActionType.Effect, EffectNames.Lightning),
            At("00:03:30:00", ActionType.Effect, EffectNames.ConfettiOff),
            At("00:03:40:00", ActionType.Effect, EffectNames.SilverStreamer),
        };

        private static TimelineAction At(string time, ActionType action, ActionValue value) => new TimelineAction
        {
            Time = time,
            Action = action,
            Value = value,
        };
    }
}
