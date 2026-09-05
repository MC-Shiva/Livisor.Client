using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

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
    public GameObject[] prefabsNeedsActivation;
    public GameObject[] prefabsOnTimeline;
    public GameObject[] miscPrefabs;

    // Camera points.
    public Transform[] cameraPoints;

    // Exposed to animator.
    public float overlayIntensity = 1.0f;

    // Objects to be controlled.
    GameObject musicPlayer;
    GameObject mainCameraRig;
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

        if (enableUnityChanView)
            SetupUnityChanView();
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
            questXRPreviewHeadPosition);
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
            return;
        }

        Debug.LogWarning("Unitychan eye camera needs a humanoid performer on the stage timeline.", this);
    }

    public void StartMusic()
    {
        foreach (var source in musicPlayer.GetComponentsInChildren<AudioSource>())
            source.Play();
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
