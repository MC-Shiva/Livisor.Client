using System;
using UnityEngine;

/// <summary>
/// ライブ用 MusicPlayer の聞こえる音源とspectrum をまとめて管理する。
/// 再生位置を揃えるため再生・停止は全音源に適用し、音量は Main だけに適用する。
/// </summary>
public class MusicPlayerController : MonoBehaviour
{
    // 観客に聞かせる正式な音源。音量変更はこの AudioSource だけに適用する。
    [SerializeField] private AudioSource _mainSource;
    // Reaktion のspectrum に使う解析用音源。再生位置は Main と同期させる。
    [SerializeField] private AudioSource[] _analysisSources = Array.Empty<AudioSource>();

    // 初回再生前の一時停止操作を、再開済みとして扱わないための状態。
    private bool _hasStarted;

    public bool HasStarted => _hasStarted;
    public float MainVolume => _mainSource != null ? _mainSource.volume : 0.0f;
    public AudioSource MainSource => _mainSource;
    public AudioSource[] AnalysisSources => _analysisSources;

    void Awake()
    {
        ResolveSourcesIfNeeded();
    }

    /// <summary>Main と解析用音源を先頭から同時に再生する。</summary>
    public void PlayAll()
    {
        if (!ResolveSourcesIfNeeded())
            return;

        foreach (var source in GetAllSources())
            source.Play();

        _hasStarted = true;
    }

    /// <summary>再生中の全音源を、現在位置を保持したまま一時停止する。</summary>
    public void PauseAll()
    {
        if (!_hasStarted || !ResolveSourcesIfNeeded())
            return;

        foreach (var source in GetAllSources())
            source.Pause();
    }

    /// <summary>一時停止している全音源を、保持した位置から再開する。</summary>
    public void ResumeAll()
    {
        if (!_hasStarted || !ResolveSourcesIfNeeded())
            return;

        foreach (var source in GetAllSources())
            source.UnPause();
    }

    /// <summary>
    /// Main 音源の音量だけを0～100の整数で変更する。
    /// 解析用音源の強さには影響させない。
    /// </summary>
    public bool SetMainVolume(int percent)
    {
        if (percent < 0 || percent > 100)
        {
            Debug.LogWarning($"[MusicPlayer] Volume must be an integer from 0 to 100: {percent}", this);
            return false;
        }

        if (!ResolveSourcesIfNeeded())
            return false;

        _mainSource.volume = percent / 100.0f;
        return true;
    }

    private bool ResolveSourcesIfNeeded()
    {
        // Prefab の参照が未設定でも、既存の子オブジェクト構成から復旧できるようにする。
        if (_mainSource == null)
        {
            var main = transform.Find("Main");
            if (main != null)
                _mainSource = main.GetComponent<AudioSource>();
        }

        if (_analysisSources == null || _analysisSources.Length == 0)
        {
            // Main 以外の AudioSource はすべて音声解析用として扱う。
            var allSources = GetComponentsInChildren<AudioSource>(true);
            var count = _mainSource == null ? allSources.Length : allSources.Length - 1;
            _analysisSources = new AudioSource[Mathf.Max(0, count)];

            var index = 0;
            foreach (var source in allSources)
            {
                if (source == _mainSource)
                    continue;

                _analysisSources[index++] = source;
            }
        }

        if (_mainSource != null)
            return true;

        Debug.LogError("MusicPlayer requires an AudioSource on child GameObject 'Main'.", this);
        return false;
    }

    private AudioSource[] GetAllSources()
    {
        // 全音源へ同じ順序で再生操作を適用するため、一時的に一つの配列へまとめる。
        var analysisCount = _analysisSources?.Length ?? 0;
        var sources = new AudioSource[analysisCount + 1];
        sources[0] = _mainSource;

        for (var i = 0; i < analysisCount; i++)
            sources[i + 1] = _analysisSources[i];

        return sources;
    }
}
