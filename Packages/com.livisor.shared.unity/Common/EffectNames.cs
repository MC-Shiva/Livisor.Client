namespace Livisor.Shared.Common
{
    /// <summary>
    /// <see cref="ActionType.Effect"/> に渡す演出名。実際の演出との対応づけは Client が持つ。
    /// </summary>
    public static class EffectNames
    {
        /// <summary>雷を 1 回落とす。</summary>
        public const string Lightning = "lightning";

        /// <summary>銀テープを射出する。</summary>
        public const string SilverStreamer = "silverStreamer";
    }
}
