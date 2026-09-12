using UnityEngine;

/// <summary>Live / Demo 共通の事前設定。実行時には StageDirector が初回再生操作で値を確定する。</summary>
[CreateAssetMenu(fileName = "PerformancePlaybackConfig", menuName = "Livisor/Performance Playback Config")]
public sealed class PerformancePlaybackConfig : ScriptableObject
{
    [SerializeField, Tooltip("音楽・ダンス・演出を指定した曲内時刻から開始する。")]
    bool _cutMode;

    [SerializeField, Min(0), Tooltip("曲の先頭からの秒数。例: 90 = 1分30秒。曲の長さ未満を指定する。")]
    double _startSeconds = 90;

    public bool CutMode => _cutMode;
    public double StartSeconds => _startSeconds;

    public bool TryGetStartSeconds(double duration, out double seconds)
    {
        seconds = _cutMode ? _startSeconds : 0;
        return !double.IsNaN(seconds) && !double.IsInfinity(seconds)
            && seconds >= 0 && seconds < duration;
    }
}
