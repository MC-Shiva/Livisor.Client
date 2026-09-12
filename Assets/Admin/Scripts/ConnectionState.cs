/// <summary>
/// Admin画面から Server への接続状態。
/// <see cref="AdminConsoleView"/> がこの状態に応じて CONNECT/DISCONNECT ボタン・
/// status-dot・connection-status ラベルの表示を切り替える。
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}
