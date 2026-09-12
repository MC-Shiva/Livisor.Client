using UnityEngine;

/// <summary>
/// Unity Editor の Play Mode でライブ制御を直接確認するためのキーボード操作。
/// バックエンドや Timeline 通信は使用しない。
/// </summary>
public sealed class LivePerformanceKeyboardController : MonoBehaviour
{
    // 通信を介さず、Main シーンの StageDirector を直接操作する。
    [SerializeField] private StageDirector _stageDirector;

#if UNITY_EDITOR
    void Start()
    {
        if (_stageDirector == null)
        {
            Debug.LogError("LivePerformanceKeyboardController requires StageDirector.", this);
            enabled = false;
            return;
        }

        Debug.Log("[LiveControlTest] S=resume, P=pause, 0=volume 0%, 5=volume 50%, 9=volume 100%", this);
    }

    void Update()
    {
        // 再生と一時停止は、音楽と演出を同じタイミングで切り替える。
        if (Input.GetKeyDown(KeyCode.S))
        {
            _stageDirector.ResumePerformance();
            Debug.Log("[LiveControlTest] Resume", this);
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            _stageDirector.PausePerformance();
            Debug.Log("[LiveControlTest] Pause", this);
        }

        // 音量の代表値を割り当て、Main 音源だけが変化することを確認する。
        if (Input.GetKeyDown(KeyCode.Alpha0))
            SetVolume(0);
        if (Input.GetKeyDown(KeyCode.Alpha5))
            SetVolume(50);
        if (Input.GetKeyDown(KeyCode.Alpha9))
            SetVolume(100);
    }

    private void SetVolume(int percent)
    {
        _stageDirector.SetMainVolume(percent);
        Debug.Log($"[LiveControlTest] Main volume: {percent}%", this);
    }
#else
    void Awake()
    {
        // EditorOnly タグが外れた場合でも、本番 Player ではキー操作を受け付けない。
        enabled = false;
    }
#endif
}
