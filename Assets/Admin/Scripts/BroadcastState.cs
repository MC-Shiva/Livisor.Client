/// <summary>
/// Admin画面から見た配信の進行状態。
/// <see cref="AdminConsoleView"/> がこの状態に応じて Broadcast/STOP ボタン・
/// status-label・進行中の行ハイライトの表示を切り替える。
/// </summary>
public enum BroadcastState
{
    Idle,
    Broadcasting,
}
