using Livisor.Shared.Common;
using UnityEngine;

/// <summary>
/// <see cref="IMediaPlayer"/> の暫定実装。実操作の代わりにログ出力する。
/// 疎通確認用。将来 SR Display 制御実装（例: SrDisplayMediaPlayer）に差し替える。
/// </summary>
public class LoggingMediaPlayer : IMediaPlayer
{
    public void Play(bool isPlaying) => Debug.Log($"[Play] {(isPlaying ? "PLAY" : "STOP")}");

    public void ChangeVolume(ActionValue value) => Debug.Log($"[Play] VOLUME -> {value}");
}
