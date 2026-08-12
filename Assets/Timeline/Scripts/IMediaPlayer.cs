using Livisor.Shared.Common;

/// <summary>
/// タイムラインのアクションを実際の操作に落とすためのインターフェース。
/// 現状は <see cref="LoggingMediaPlayer"/>（ログ出力）。将来 SR Display 制御実装へ差し替える。
/// </summary>
public interface IMediaPlayer
{
    void Start(ActionValue value);
    void Stop(ActionValue value);
    void ChangeVolume(ActionValue value);
}
