namespace Livisor.Shared.Common
{
    /// <summary>
    /// タイムライン上で実行する操作の種類。
    /// </summary>
    public enum ActionType
    {
        Play,           // "play"（true=再生 / false=停止）
        VolumeChange,   // "volumeChange"
        Effect,         // "effect"（値は演出名の文字列。既知の名前は EffectNames）
    }
}
