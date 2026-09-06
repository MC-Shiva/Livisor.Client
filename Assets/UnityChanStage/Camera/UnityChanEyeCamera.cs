using UnityEngine;
using UnityEngine.XR;

public sealed class UnityChanEyeCamera : MonoBehaviour
{
    [Tooltip("Press C in the Game view to switch between the audience and Unitychan's eyes.")]
    public KeyCode toggleKey = KeyCode.C;
    [Min(0.0f), Tooltip("Desktop transition time. A connected XR headset switches immediately.")]
    public float transitionDuration = 0.75f;
    [Range(30.0f, 120.0f)]
    public float eyeFieldOfView = 60.0f;

    public bool IsUnityChanView { get; private set; }

    Camera viewCamera;
    Animator performer;
    Transform leftEye, rightEye, head;
    CameraSwitcher cameraSwitcher;
    Vector3 audiencePosition, transitionPosition, trackingPosition;
    Quaternion audienceRotation, transitionRotation, trackingYaw;
    int audienceCullingMask;
    float audienceFieldOfView, audienceNearClip, transitionStarted;
    AnimatorCullingMode audienceAnimatorCulling;
    bool switcherWasEnabled, hasSavedState, transitioning;

    public void Initialize(Camera camera, Animator character)
    {
        RestoreAudience();
        viewCamera = camera;
        performer = character;
        cameraSwitcher = GetComponentInChildren<CameraSwitcher>(true);
        leftEye = rightEye = head = null;
        if (!performer)
            return;

        foreach (var bone in performer.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == "locator_Eye_L") leftEye = bone;
            else if (bone.name == "locator_Eye_R") rightEye = bone;
            else if (bone.name == "Character1_Head") head = bone;
        }
        if (!head && performer.isHuman)
            head = performer.GetBoneTransform(HumanBodyBones.Head);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            ToggleView();
    }

    public void ToggleView()
    {
        if (!viewCamera || !performer)
            return;

        if (!IsUnityChanView)
        {
            if (!hasSavedState)
            {
                audiencePosition = transform.position;
                audienceRotation = transform.rotation;
                audienceCullingMask = viewCamera.cullingMask;
                audienceFieldOfView = viewCamera.fieldOfView;
                audienceNearClip = viewCamera.nearClipPlane;
                audienceAnimatorCulling = performer.cullingMode;
                switcherWasEnabled = cameraSwitcher && cameraSwitcher.enabled;
                hasSavedState = true;
            }

            CaptureTrackingBaseline();
            var characterLayer = LayerMask.NameToLayer("UnityChan");
            if (characterLayer >= 0)
                viewCamera.cullingMask = audienceCullingMask & ~(1 << characterLayer);
            viewCamera.fieldOfView = eyeFieldOfView;
            viewCamera.nearClipPlane = 0.03f;
            performer.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (cameraSwitcher)
                cameraSwitcher.enabled = false;
        }

        IsUnityChanView = !IsUnityChanView;
        transitionPosition = transform.position;
        transitionRotation = transform.rotation;
        transitionStarted = Time.unscaledTime;
        transitioning = true;
        if (XRSettings.isDeviceActive || transitionDuration <= 0.0f)
            ApplyPose(1.0f);
    }

    public bool Recenter()
    {
        if (!IsUnityChanView || !viewCamera || !performer)
            return false;

        CaptureTrackingBaseline();
        ApplyPose(1.0f);
        return true;
    }

    void CaptureTrackingBaseline()
    {
        trackingPosition = transform.InverseTransformPoint(viewCamera.transform.position);
        var localRotation = Quaternion.Inverse(transform.rotation) * viewCamera.transform.rotation;
        trackingYaw = Quaternion.Euler(0.0f, localRotation.eulerAngles.y, 0.0f);
    }

    void LateUpdate()
    {
        if (!hasSavedState)
            return;
        if (!viewCamera || !performer)
        {
            RestoreAudience();
            return;
        }

        var progress = !transitioning || XRSettings.isDeviceActive || transitionDuration <= 0.0f
            ? 1.0f
            : Mathf.Clamp01((Time.unscaledTime - transitionStarted) / transitionDuration);
        ApplyPose(progress);
    }

    void ApplyPose(float progress)
    {
        var position = audiencePosition;
        var rotation = audienceRotation;
        if (IsUnityChanView)
        {
            var eyePosition = leftEye && rightEye
                ? (leftEye.position + rightEye.position) * 0.5f
                : head ? head.position : performer.transform.position + Vector3.up * 1.35f;
            // Keep the horizon upright while preserving the headset's relative pose.
            rotation = Quaternion.Euler(0.0f, performer.transform.eulerAngles.y, 0.0f)
                * Quaternion.Inverse(trackingYaw);
            position = eyePosition - rotation * trackingPosition;
        }

        var blend = Mathf.SmoothStep(0.0f, 1.0f, progress);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transitionPosition, position, blend),
            Quaternion.Slerp(transitionRotation, rotation, blend));

        if (progress < 1.0f)
            return;
        transitioning = false;
        if (!IsUnityChanView)
            RestoreAudience();
    }

    void OnDisable()
    {
        RestoreAudience();
    }

    void RestoreAudience()
    {
        if (!hasSavedState)
            return;

        transform.SetPositionAndRotation(audiencePosition, audienceRotation);
        if (viewCamera)
        {
            viewCamera.cullingMask = audienceCullingMask;
            viewCamera.fieldOfView = audienceFieldOfView;
            viewCamera.nearClipPlane = audienceNearClip;
        }
        if (performer)
            performer.cullingMode = audienceAnimatorCulling;
        if (cameraSwitcher)
            cameraSwitcher.enabled = switcherWasEnabled;
        IsUnityChanView = false;
        hasSavedState = false;
        transitioning = false;
    }
}
