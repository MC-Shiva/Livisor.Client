using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class StageDirector : MonoBehaviour
{
    // Control options.
    public bool ignoreFastForward = true;
    public bool useDirectorCameraInEditor = true;
    public Vector3 questXRStageOrigin = new Vector3(0.0f, 0.0f, 4.27f);
    public Vector3 questXRStageRotation = new Vector3(0.0f, 180.0f, 0.0f);
    public Vector3 questXRPreviewHeadPosition = new Vector3(0.0f, 1.35f, 0.0f);

    // Prefabs.
    public GameObject musicPlayerPrefab;
    public GameObject mainCameraRigPrefab;
    public GameObject[] prefabsNeedsActivation;
    public GameObject[] prefabsOnTimeline;
    public GameObject[] miscPrefabs;

    // Camera points.
    public Transform[] cameraPoints;

    // Exposed to animator.
    public float overlayIntensity = 1.0f;

    // Objects to be controlled.
    GameObject musicPlayer;
    CameraSwitcher mainCameraSwitcher;
    ScreenOverlay[] screenOverlays;
    GameObject[] objectsNeedsActivation;
    GameObject[] objectsOnTimeline;

    void Awake()
    {
        // Instantiate the prefabs.
        musicPlayer = (GameObject)Instantiate(musicPlayerPrefab);

        SetupCameraRig();

        objectsNeedsActivation = new GameObject[prefabsNeedsActivation.Length];
        for (var i = 0; i < prefabsNeedsActivation.Length; i++)
            objectsNeedsActivation[i] = (GameObject)Instantiate(prefabsNeedsActivation[i]);

        objectsOnTimeline = new GameObject[prefabsOnTimeline.Length];
        for (var i = 0; i < prefabsOnTimeline.Length; i++)
            objectsOnTimeline[i] = (GameObject)Instantiate(prefabsOnTimeline[i]);

        foreach (var p in miscPrefabs) Instantiate(p);
    }

    void Update()
    {
        foreach (var so in screenOverlays)
        {
            so.intensity = overlayIntensity;
            so.enabled = overlayIntensity > 0.01f;
        }
    }

    void SetupCameraRig()
    {
#if UNITY_EDITOR
        if (useDirectorCameraInEditor)
        {
            var cameraRig = (GameObject)Instantiate(mainCameraRigPrefab);
            mainCameraSwitcher = cameraRig.GetComponentInChildren<CameraSwitcher>();
            screenOverlays = cameraRig.GetComponentsInChildren<ScreenOverlay>();
            return;
        }
#endif

        QuestXRStageRig.Create(
            questXRStageOrigin,
            Quaternion.Euler(questXRStageRotation),
            questXRPreviewHeadPosition);
        mainCameraSwitcher = null;
        screenOverlays = new ScreenOverlay[0];
    }

    public void StartMusic()
    {
        foreach (var source in musicPlayer.GetComponentsInChildren<AudioSource>())
            source.Play();
    }

    // === サーバーからの指示で操作するための入口（TimelineReceiver → StageMediaPlayer が呼ぶ）===
    // メソッド名は PR #9（ライブの再生停止・音量調整）と同じにしてあり、PR #9 を取り込むときはそちらの実装で置き換える。

    // 一時停止前の再生速度。再開時に元へ戻す。
    float resumeTimeScale = 1.0f;
    bool isPerformancePaused;

    /// <summary>現在位置で音楽とライブ演出を一時停止する。音源は位置を保持して止め、演出は timeScale でまとめて止める。</summary>
    public void PausePerformance()
    {
        if (isPerformancePaused)
            return;

        if (Time.timeScale > 0.0f)
            resumeTimeScale = Time.timeScale;

        foreach (var source in musicPlayer.GetComponentsInChildren<AudioSource>())
            source.Pause();

        Time.timeScale = 0.0f;
        isPerformancePaused = true;
    }

    /// <summary>一時停止位置から音楽とライブ演出を再開する。まだ一度も鳴らしていなければ先頭から鳴らす。</summary>
    public void ResumePerformance()
    {
        if (!isPerformancePaused)
            return;

        Time.timeScale = resumeTimeScale > 0.0f ? resumeTimeScale : 1.0f;

        foreach (var source in musicPlayer.GetComponentsInChildren<AudioSource>())
        {
            if (source.time > 0.0f)
                source.UnPause();
            else
                source.Play();
        }

        isPerformancePaused = false;
    }

    /// <summary>
    /// 観客に聞こえる Main 音源の音量だけを 0〜100 の整数で変える。
    /// Spectrum（演出の解析用音源）には触れない。範囲外は無視する。
    /// </summary>
    public void SetMainVolume(int percent)
    {
        if (percent < 0 || percent > 100)
        {
            Debug.LogWarning($"[StageDirector] Volume must be an integer from 0 to 100: {percent}", this);
            return;
        }

        var main = musicPlayer.transform.Find("Main");
        var source = main != null ? main.GetComponent<AudioSource>() : null;
        if (source == null)
        {
            Debug.LogError("MusicPlayer requires an AudioSource on child GameObject 'Main'.", musicPlayer);
            return;
        }

        source.volume = percent / 100.0f;
    }

    public void ActivateProps()
    {
        foreach (var o in objectsNeedsActivation) o.BroadcastMessage("ActivateProps");
    }

    public void SwitchCamera(int index)
    {
        if (mainCameraSwitcher)
            mainCameraSwitcher.ChangePosition(cameraPoints[index], true);
    }

    public void StartAutoCameraChange()
    {
        if (mainCameraSwitcher)
            mainCameraSwitcher.StartAutoChange();
    }

    public void StopAutoCameraChange()
    {
        if (mainCameraSwitcher)
            mainCameraSwitcher.StopAutoChange();
    }

    public void FastForward(float second)
    {
        if (!ignoreFastForward)
        {
            FastForwardAnimator(GetComponent<Animator>(), second, 0);
            foreach (var go in objectsOnTimeline)
                foreach (var animator in go.GetComponentsInChildren<Animator>())
                    FastForwardAnimator(animator, second, 0.5f);
        }
    }

    void FastForwardAnimator(Animator animator, float second, float crossfade)
    {
        for (var layer = 0; layer < animator.layerCount; layer++)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            if (crossfade > 0.0f)
                animator.CrossFade(info.fullPathHash, crossfade / info.length, layer, info.normalizedTime + second / info.length);
            else
                animator.Play(info.fullPathHash, layer, info.normalizedTime + second / info.length);
        }
    }

    public void EndPerformance()
    {
        //Application.LoadLevel(0);
        SceneManager.LoadScene(0);
    }
}
