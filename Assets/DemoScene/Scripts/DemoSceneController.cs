using UnityEngine;

/// <summary>
/// 通信を使わないDemoScene専用のライブ操作。
/// StageDirectorの通常の再生経路を使い、音楽と演出のタイミングを維持する。
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class DemoSceneController : MonoBehaviour
{
    [SerializeField]
    StageDirector _stageDirector;

    [SerializeField, Tooltip("Play Mode開始時にライブを自動再生する。")]
    bool _playOnStart = true;

    void Awake()
    {
        if (_stageDirector == null)
            _stageDirector = GetComponent<StageDirector>();
    }

    void Start()
    {
        if (_stageDirector == null)
        {
            Debug.LogError("DemoSceneController requires StageDirector.", this);
            enabled = false;
            return;
        }

        if (_playOnStart)
            ResumePerformance();

        Debug.Log("[DemoScene] S=resume, P=pause", this);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
            ResumePerformance();
        if (Input.GetKeyDown(KeyCode.P))
            PausePerformance();
    }

    public void ResumePerformance()
    {
        _stageDirector.ResumePerformance();
        Debug.Log("[DemoScene] Resume", this);
    }

    public void PausePerformance()
    {
        _stageDirector.PausePerformance();
        Debug.Log("[DemoScene] Pause", this);
    }
}
