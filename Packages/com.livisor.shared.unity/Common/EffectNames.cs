namespace Livisor.Shared.Common
{
    /// <summary>
    /// <see cref="ActionType.Effect"/> に渡す演出名。実際の演出との対応づけは Client が持つ。
    /// </summary>
    public static class EffectNames
    {
        /// <summary>紙吹雪を開始する。開始後は舞い続ける。</summary>
        public const string ConfettiOn = "confettiOn";

        /// <summary>新しい紙吹雪の放出を止める。表示中の紙片は自然に消える。</summary>
        public const string ConfettiOff = "confettiOff";

        /// <summary>雷を 1 回落とす。</summary>
        public const string Lightning = "lightning";

        /// <summary>銀テープを射出する。</summary>
        public const string SilverStreamer = "silverStreamer";
    }
}
