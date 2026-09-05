using Livisor.Shared.Common;
using UnityEngine;

/// <summary>
/// <see cref="IMediaPlayer"/> の本実装。ライブシーンの <see cref="StageDirector"/> を操作する。
/// 再生・停止は音源と演出をまとめて止め、音量は観客に聞こえる Main 音源だけに適用する。
/// </summary>
public class StageMediaPlayer : IMediaPlayer
{
    private readonly StageDirector _director;

    public StageMediaPlayer(StageDirector director) => _director = director;

    public void Play(bool isPlaying)
    {
        if (isPlaying)
            _director.ResumePerformance();
        else
            _director.PausePerformance();
    }

    public void ChangeVolume(ActionValue value)
    {
        if (value.Kind != ActionValueKind.Number)
        {
            Debug.LogWarning($"[StageMediaPlayer] 音量は数値で受け取る想定 (Kind={value.Kind})");
            return;
        }

        _director.SetMainVolume(value.Number);
    }
}
