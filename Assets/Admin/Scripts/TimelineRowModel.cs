using Livisor.Shared.Common;

/// <summary>
/// 管理画面の行 1 つ分の編集中データ。TimelineAction に変換する前の入力状態を保持する。
/// 時刻は 4 つの数値欄（時/分/秒/センチ秒）で入力させるため、あらかじめ範囲内の整数として持つ。
/// </summary>
public class TimelineRowModel
{
    public int Hours;
    public int Minutes;
    public int Seconds;
    public int Centiseconds;

    // 既定は音量 100 にする。既定を play=false にすると、未編集のまま SCHEDULE を押したときに
    // 「再生開始 0 秒で停止」という予約が送られてしまうため。
    public ActionType Action = ActionType.VolumeChange;
    public string NumberText = "100";
    public bool BoolValue;
    public string TextValue = string.Empty;
}
