using Livisor.Shared.Common;
using Livisor.Shared.DTO;

/// <summary>
/// 予約アクションの発火計算（UnityEngine 非依存・テスト可能）。
/// サーバーから届いた <see cref="TransportState"/> から「あと何秒待って発火するか」を出す。
///
/// 式は 2026-08-29 の決定どおり（TransportState のコメントを参照）:
///   待ち時間 = 相対時間 - (ServerTimeMs - StartedAtServerMs)
/// 括弧内は「サーバーが送信した時点で既に経過していた再生位置」。
/// 受け取った瞬間を基準にするので、サーバーとクライアントの時計は比べない。
/// 片道の通信遅延ぶんだけ遅れて発火するが、8/31 の合意で今回はこの遅れを扱わない。
/// </summary>
public static class TimelinePlayback
{
    /// <summary>
    /// 発火すべき予約があれば、その待ち時間（秒）とアクションを返す。
    /// 停止中（基準時刻が無い）か、予約が無いか、時刻の形式が不正なら false。
    /// 既に相対時間を過ぎている場合は待ち時間 0 で返す（受信した瞬間に発火する）。
    /// </summary>
    public static bool TryGetPendingAction(TransportState state, out double delaySeconds, out TimelineAction action)
    {
        delaySeconds = 0;
        action = null;

        if (state == null || !state.Playing || state.ScheduledAction == null)
            return false;

        if (!PlaybackTime.TryParse(state.ScheduledAction.Time, out var offset))
            return false;

        var elapsedSeconds = (state.ServerTimeMs - state.StartedAtServerMs) / 1000.0;
        delaySeconds = offset.TotalSeconds - elapsedSeconds;
        if (delaySeconds < 0)
            delaySeconds = 0;

        action = state.ScheduledAction;
        return true;
    }

    /// <summary>
    /// "HH:mm:ss:ff" を秒に変換する。第 4 フィールドはセンチ秒(1/100 秒)として扱う。
    /// パースルールは <see cref="PlaybackTime"/>（Server と共通）に委譲する。不正な値は 0 を返す。
    /// </summary>
    public static double ParseTimeToSeconds(string time)
        => PlaybackTime.TryParse(time, out var t) ? t.TotalSeconds : 0;
}
