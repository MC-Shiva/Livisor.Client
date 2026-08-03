/// <summary>
/// タイムラインのアクションを実際の操作に落とすための抽象（Port）。
/// 現状は <see cref="LoggingMediaPlayer"/>（ログ出力）。将来 SR Display 制御実装へ差し替える。
/// </summary>
public interface IMediaPlayer
{
    void Start(int value);
    void Stop(int value);
    void ChangeVolume(int value);
}
