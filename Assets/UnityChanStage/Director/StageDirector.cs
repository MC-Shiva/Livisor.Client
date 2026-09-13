using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Livisor.Live.Penlights;
using Livisor.Live.Effects;

public class StageDirector : MonoBehaviour
{
    // Control options.
    public bool ignoreFastForward = true;
    public bool useDirectorCameraInEditor = true;
    [Tooltip("Allow C or the Quest secondary button (B/Y) to switch to Unitychan's eyes.")]
    public bool enableUnityChanView;
    public Vector3 questXRStageOrigin = new Vector3(0.0f, 0.0f, 4.27f);
    public Vector3 questXRStageRotation = new Vector3(0.0f, 180.0f, 0.0f);
    public Vector3 questXRPreviewHeadPosition = new Vector3(0.0f, 1.35f, 0.0f);

    // Prefabs.
    public GameObject musicPlayerPrefab;
    public GameObject mainCameraRigPrefab;
    public GameObject playerPenlightPrefab;
    public GameObject confettiPrefab;
    public GameObject[] prefabsNeedsActivation;
    public GameObject[] prefabsOnTimeline;
    public GameObject[] miscPrefabs;

    [SerializeField] SilverStreamerController silverStreamers;
    bool finaleFired;
    public bool FireSilverStreamersOnEnd { get; set; } = true;

    // Audience placement.
    public Transform audiencePenlightPlacement;

    public Transform PerformerHips { get; private set; }

    // Camera points.
    public Transform[] cameraPoints;

    // Exposed to animator.
    public float overlayIntensity = 1.0f;

    // Objects to be controlled.
    GameObject musicPlayer;
    MusicPlayerController musicPlayerController;
    GameObject mainCameraRig;
    CameraSwitcher mainCameraSwitcher;
    ScreenOverlay[] screenOverlays;
    GameObject[] objectsNeedsActivation;
    GameObject[] objectsOnTimeline;
    GameObject confetti;

    // 一時停止前の再生速度。再開時に元へ戻す。
    float resumeTimeScale = 1.0f;
    bool isPerformancePaused;

    public bool IsPerformancePaused => isPerformancePaused;
    public MusicPlayerController MusicPlayerController => musicPlayerController;

    /// <summary>
    /// 音楽の再生位置（秒）。まだ鳴っていない・一時停止中は -1。
    /// 演出の発火基準。AudioSource.time は圧縮音源で粗いため timeSamples から求める。
    /// </summary>
    public double MusicTimeSeconds
    {
        get
        {
            var source = musicPlayerController != null ? musicPlayerController.MainSource : null;
            if (source == null || source.clip == null || !source.isPlaying)
                return -1;
            return source.timeSamples / (double)source.clip.frequency;
        }
    }

    void Awake()
    {
        // Instantiate the prefabs.
        musicPlayer = (GameObject)Instantiate(musicPlayerPrefab);
        musicPlayerController = musicPlayer.GetComponent<MusicPlayerController>();
        if (musicPlayerController == null)
            Debug.LogError("MusicPlayer prefab requires MusicPlayerController.", musicPlayer);

        SetupCameraRig();

        objectsNeedsActivation = new GameObject[prefabsNeedsActivation.Length];
        for (var i = 0; i < prefabsNeedsActivation.Length; i++)
        {
            objectsNeedsActivation[i] = (GameObject)Instantiate(prefabsNeedsActivation[i]);
            if (prefabsNeedsActivation[i] == confettiPrefab)
                confetti = objectsNeedsActivation[i];
        }

        // Live の既存の紙吹雪を使う。Demo のように固定演出に含まれない場合だけ生成する。
        if (confetti == null && confettiPrefab != null)
        {
            confetti = Instantiate(confettiPrefab);
            confetti.SetActive(false);
        }

        objectsOnTimeline = new GameObject[prefabsOnTimeline.Length];
        for (var i = 0; i < prefabsOnTimeline.Length; i++)
        {
            objectsOnTimeline[i] = (GameObject)Instantiate(prefabsOnTimeline[i]);
            var performer = objectsOnTimeline[i].GetComponentInChildren<Animator>();
            if (PerformerHips == null && performer != null && performer.isHuman)
                PerformerHips = performer.GetBoneTransform(HumanBodyBones.Hips);
        }

        var audiencePenlights = new List<AudiencePenlightController>();
        foreach (var p in miscPrefabs)
        {
            var instance = (GameObject)Instantiate(p);
            audiencePenlights.AddRange(
                instance.GetComponentsInChildren<AudiencePenlightController>(true));
        }

        // Stage, camera, and all miscellaneous prefabs now have their final hierarchy.
        // Initialize penlights last so their world-space positions and bounds are baked
        // from the explicit placement transform.
        var penlightAudioSource = new ReaktionPenlightAudioSource(musicPlayer);
        foreach (var stageObject in objectsNeedsActivation)
            foreach (var speaker in stageObject.GetComponentsInChildren<SpeakerVibrationController>(true))
                speaker.Initialize(penlightAudioSource, this);

        foreach (var playerPenlight in
                 mainCameraRig.GetComponentsInChildren<PlayerPenlightController>(true))
            playerPenlight.SetAudioSource(penlightAudioSource);

        foreach (var audiencePenlight in audiencePenlights)
        {
            audiencePenlight.SetAudioSource(penlightAudioSource);
            audiencePenlight.InitializeAt(audiencePenlightPlacement);
        }

        if (enableUnityChanView)
            SetupUnityChanView();

        if (silverStreamers == null)
        {
            var prefab = Resources.Load<GameObject>("SilverStreamer");
            if (prefab != null)
                silverStreamers = Instantiate(prefab, transform.position, transform.rotation, transform)
                    .GetComponent<SilverStreamerController>();
            else
                Debug.LogError("SilverStreamer prefab is missing.", this);
        }

        // 最初の再生操作を受け取るまで、ライブを先頭で待機させる。
        PausePerformance();
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
            mainCameraRig = (GameObject)Instantiate(mainCameraRigPrefab);
            mainCameraSwitcher = mainCameraRig.GetComponentInChildren<CameraSwitcher>();
            screenOverlays = mainCameraRig.GetComponentsInChildren<ScreenOverlay>();
            return;
        }
#endif

        mainCameraRig = QuestXRStageRig.Create(
            questXRStageOrigin,
            Quaternion.Euler(questXRStageRotation),
            questXRPreviewHeadPosition,
            playerPenlightPrefab);
        mainCameraSwitcher = null;
        screenOverlays = new ScreenOverlay[0];
    }

    void SetupUnityChanView()
    {
        foreach (var performer in objectsOnTimeline)
        {
            var animator = performer.GetComponentInChildren<Animator>();
            if (!animator || !animator.isHuman)
                continue;

            var eyeCamera = mainCameraRig.AddComponent<UnityChanEyeCamera>();
            eyeCamera.Initialize(mainCameraRig.GetComponentInChildren<Camera>(), animator);
            foreach (var playerPenlight in
                     mainCameraRig.GetComponentsInChildren<PlayerPenlightController>(true))
                playerPenlight.BindViewSource(eyeCamera);
            return;
        }

        Debug.LogWarning("Unitychan eye camera needs a humanoid performer on the stage timeline.", this);
    }

    public void StartMusic()
    {
        finaleFired = false;
        if (musicPlayerController != null)
        {
            musicPlayerController.PlayAll();
            return;
        }

        foreach (var source in musicPlayer.GetComponentsInChildren<AudioSource>())
            source.Play();
    }

    /// <summary>現在位置で音楽とライブ演出を一時停止する。音源は位置を保持して止め、演出は timeScale でまとめて止める。</summary>
    public void PausePerformance()
    {
        if (isPerformancePaused)
            return;

        if (Time.timeScale > 0.0f)
            resumeTimeScale = Time.timeScale;

        if (musicPlayerController != null)
            musicPlayerController.PauseAll();
        else
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

        if (musicPlayerController != null)
            musicPlayerController.ResumeAll();
        else
        {
            foreach (var source in musicPlayer.GetComponentsInChildren<AudioSource>())
            {
                if (source.time > 0.0f)
                    source.UnPause();
                else
                    source.Play();
            }
        }

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

    /// <summary>紙吹雪の放出を開始・停止する。停止後も表示中の紙片は自然に消える。</summary>
    public void SetConfetti(bool enabled)
    {
        if (confetti == null)
            return;

        if (enabled)
        {
            confetti.SetActive(true);
            confetti.BroadcastMessage("ActivateProps");
        }

        foreach (var particles in confetti.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (enabled)
            {
                if (!particles.isEmitting)
                    particles.Play(false);
            }
            else
                particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
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

    /// <summary>Manually fire at any time, including after the performance has ended.</summary>
    public void FireSilverStreamers()
    {
        if (silverStreamers != null) silverStreamers.Fire();
    }

    public void EndPerformance()
    {
        if (!finaleFired && FireSilverStreamersOnEnd)
        {
            finaleFired = true;
            FireSilverStreamers();
        }
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
