using Livisor.Shared.Common;

/// <summary>
/// タイムラインのアクションを実際の操作に落とすためのインターフェース。
/// 現状は <see cref="LoggingMediaPlayer"/>（ログ出力）。将来 SR Display 制御実装へ差し替える。
/// </summary>
public interface IMediaPlayer
{
    /// <summary>再生と停止を切り替える。true=再生 / false=停止。</summary>
    void Play(bool isPlaying);

    void ChangeVolume(ActionValue value);
}
