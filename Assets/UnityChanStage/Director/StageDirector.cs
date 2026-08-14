using UnityEngine;
using UnityEngine.SceneManagement;

public class StageDirector : MonoBehaviour
{
    // Control options.
    public bool ignoreFastForward = true;
    public bool useQuestXRWhenAvailable = true;
    public bool forceQuestXRInEditor = false;
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
    // 聞こえる Main 音源と演出解析用音源を、同じ再生位置でまとめて制御する。
    MusicPlayerController musicPlayerController;
    // SR Display が利用できない環境で AudioListener を補う通常カメラ。
    Camera fallbackAudioListenerCamera;
    CameraSwitcher mainCameraSwitcher;
    ScreenOverlay[] screenOverlays;
    GameObject[] objectsNeedsActivation;
    GameObject[] objectsOnTimeline;

    // timeScale と音源の停止状態を一つの状態として管理する。
    bool isPerformancePaused;
    // 一時停止前の再生速度を保存し、再開時に元の速度へ戻す。
    float resumeTimeScale = 1.0f;

    public bool IsPerformancePaused => isPerformancePaused;
    public MusicPlayerController MusicPlayerController => musicPlayerController;

    void Awake()
    {
        // Instantiate the prefabs.
        musicPlayer = (GameObject)Instantiate(musicPlayerPrefab);
        // シーン上の複製ではなく、MusicPlayer Prefab 内の音源を操作対象にする。
        musicPlayerController = musicPlayer.GetComponent<MusicPlayerController>();
        if (musicPlayerController == null)
            Debug.LogError("MusicPlayer prefab requires MusicPlayerController.", musicPlayer);

        if (ShouldUseQuestXR())
        {
            DisableSRDSceneObjects();
            QuestXRStageRig.Create(
                questXRStageOrigin,
                Quaternion.Euler(questXRStageRotation),
                questXRPreviewHeadPosition);
            mainCameraSwitcher = null;
            screenOverlays = new ScreenOverlay[0];
        }
        else
        {
            var cameraRig = (GameObject)Instantiate(mainCameraRigPrefab);
            fallbackAudioListenerCamera = cameraRig.GetComponentInChildren<Camera>(true);
            mainCameraSwitcher = cameraRig.GetComponentInChildren<CameraSwitcher>();
            screenOverlays = cameraRig.GetComponentsInChildren<ScreenOverlay>();
        }

        objectsNeedsActivation = new GameObject[prefabsNeedsActivation.Length];
        for (var i = 0; i < prefabsNeedsActivation.Length; i++)
            objectsNeedsActivation[i] = (GameObject)Instantiate(prefabsNeedsActivation[i]);

        objectsOnTimeline = new GameObject[prefabsOnTimeline.Length];
        for (var i = 0; i < prefabsOnTimeline.Length; i++)
            objectsOnTimeline[i] = (GameObject)Instantiate(prefabsOnTimeline[i]);

        foreach (var p in miscPrefabs) Instantiate(p);

        // 最初の再生操作を受け取るまで、ライブを先頭で待機させる。
        PausePerformance();
    }

    void Start()
    {
        // 全オブジェクトの Awake 後に確認し、SRDManager が自ら無効化された環境でも音を出せるようにする。
        EnsureAudioListener();
    }

    void Update()
    {
        foreach (var so in screenOverlays)
        {
            so.intensity = overlayIntensity;
            so.enabled = overlayIntensity > 0.01f;
        }
    }

    public void StartMusic()
    {
        if (musicPlayerController != null)
        {
            musicPlayerController.PlayAll();
            return;
        }

        // Prefab の設定ミスがあっても既存ライブが無音にならないためのフォールバック。
        foreach (var source in musicPlayer.GetComponentsInChildren<AudioSource>())
            source.Play();
    }

    /// <summary>現在位置で音楽とライブ演出を一時停止する。</summary>
    public void PausePerformance()
    {
        if (isPerformancePaused)
            return;

        if (Time.timeScale > 0.0f)
            resumeTimeScale = Time.timeScale;

        // 音源は現在位置を保持して止め、演出側は timeScale でまとめて停止する。
        musicPlayerController?.PauseAll();
        Time.timeScale = 0.0f;
        isPerformancePaused = true;
    }

    /// <summary>一時停止位置から音楽とライブ演出を再開する。</summary>
    public void ResumePerformance()
    {
        if (!isPerformancePaused)
            return;

        // 演出と音源を同じ操作で再開し、それぞれの停止位置を維持する。
        Time.timeScale = resumeTimeScale > 0.0f ? resumeTimeScale : 1.0f;
        musicPlayerController?.ResumeAll();
        isPerformancePaused = false;
    }

    /// <summary>Spectrum には触れず、聞こえる Main 音源だけを変更する。</summary>
    public void SetMainVolume(int percent)
    {
        if (musicPlayerController == null)
        {
            Debug.LogError("MusicPlayerController is not available.", this);
            return;
        }

        musicPlayerController.SetMainVolume(percent);
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

    bool ShouldUseQuestXR()
    {
#if UNITY_ANDROID
        return useQuestXRWhenAvailable;
#else
        return forceQuestXRInEditor;
#endif
    }

    void DisableSRDSceneObjects()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var root in roots)
            DisableSRDObjectsRecursive(root);
    }

    void DisableSRDObjectsRecursive(GameObject go)
    {
        if (go.name.Contains("SRDisplay"))
        {
            go.SetActive(false);
            return;
        }

        foreach (Transform child in go.transform)
            DisableSRDObjectsRecursive(child.gameObject);
    }

    void EnsureAudioListener()
    {
        // 非アクティブまたは無効な Listener は、実際には音を受け取れないため対象外とする。
        var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var listener in listeners)
        {
            if (listener.enabled && listener.gameObject.activeInHierarchy)
                return;
        }

        if (fallbackAudioListenerCamera == null)
        {
            Debug.LogError("No active AudioListener or fallback camera is available.", this);
            return;
        }

        // SR Display や Quest 側に有効な Listener がない場合だけ、通常カメラで補完する。
        var fallbackListener = fallbackAudioListenerCamera.GetComponent<AudioListener>();
        if (fallbackListener == null)
            fallbackListener = fallbackAudioListenerCamera.gameObject.AddComponent<AudioListener>();

        fallbackListener.enabled = true;
        Debug.Log("AudioListener was added to the fallback Main Camera.", fallbackAudioListenerCamera);
    }

    public void EndPerformance()
    {
        // 今回はシーンを自動リロードせず、最終位置で停止する。
        PausePerformance();
    }

    void OnDestroy()
    {
        // Play Mode 終了や別シーンへの遷移後にグローバルな timeScale を残さない。
        if (isPerformancePaused)
            Time.timeScale = resumeTimeScale > 0.0f ? resumeTimeScale : 1.0f;
    }
}
