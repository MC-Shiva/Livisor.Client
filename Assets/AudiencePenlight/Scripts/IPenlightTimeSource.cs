using UnityEngine;

namespace Livisor.Live.Penlights
{
    /// <summary>
    /// ペンライトアニメーションが参照する「ライブ開始後の時刻」を供給するインターフェース。
    /// ControllerをTimelineやネットワーク同期の実装へ直接依存させないために分離している。
    /// 将来、同期時刻を返す別実装をSetTimeSourceから注入できる。
    /// </summary>
    public interface IPenlightTimeSource
    {
        /// <summary>振りアニメーションの計算に使用する現在時刻（秒）。</summary>
        double CurrentTime { get; }
    }

    /// <summary>
    /// Unityのローカル再生時間をそのまま使用する標準の時刻源。
    /// 外部同期を使用しない現在の既定実装となる。
    /// </summary>
    public sealed class LocalPenlightTimeSource : IPenlightTimeSource
    {
        public double CurrentTime => Time.timeAsDouble;
    }
}
