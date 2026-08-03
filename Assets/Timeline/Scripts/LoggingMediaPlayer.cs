using UnityEngine;

/// <summary>
/// <see cref="IMediaPlayer"/> の暫定実装。実操作の代わりにログ出力する。
/// 疎通確認用。将来 SR Display 制御実装（例: SrDisplayMediaPlayer）に差し替える。
/// </summary>
public class LoggingMediaPlayer : IMediaPlayer
{
    public void Start(int value) => Debug.Log($"[Play] START (value={value})");

    public void Stop(int value) => Debug.Log($"[Play] STOP (value={value})");

    public void ChangeVolume(int value) => Debug.Log($"[Play] VOLUME -> {value}");
}
