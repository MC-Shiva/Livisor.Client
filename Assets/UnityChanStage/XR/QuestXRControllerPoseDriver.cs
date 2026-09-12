using System;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// XRコントローラーのトラッキング空間内Poseを、このGameObjectのローカルPoseへ反映する。
/// HMDが動作していないEditorでは、実機なしで見た目を確認できるプレビューPoseを使用する。
/// </summary>
[DefaultExecutionOrder(-90)]
[DisallowMultipleComponent]
public sealed class QuestXRControllerPoseDriver : MonoBehaviour
{
    [Tooltip("Controller tracked by this anchor. Right Hand is the default for the player penlight.")]
    public XRNode controllerNode = XRNode.RightHand;

    [Tooltip("Local position used when no XR device is active, for Editor preview.")]
    public Vector3 previewLocalPosition = new Vector3(0.25f, 1.1f, 0.35f);

    [Tooltip("Local rotation used when no XR device is active, for Editor preview.")]
    public Vector3 previewLocalEulerAngles = new Vector3(-15.0f, 0.0f, 0.0f);

    public bool IsPoseAvailable { get; private set; }
    public event Action<bool> PoseAvailabilityChanged;

    InputDevice controller;

    void OnEnable()
    {
        controller = default;
        UpdatePose();
    }

    void Update()
    {
        UpdatePose();
    }

    void OnDisable()
    {
        SetPoseAvailable(false);
    }

    void UpdatePose()
    {
        if (!controller.isValid)
            controller = InputDevices.GetDeviceAtXRNode(controllerNode);

        if (controller.isValid &&
            controller.TryGetFeatureValue(CommonUsages.devicePosition, out var position) &&
            controller.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
        {
            transform.localPosition = position;
            transform.localRotation = rotation;
            SetPoseAvailable(true);
            return;
        }

        if (!XRSettings.isDeviceActive)
        {
            transform.localPosition = previewLocalPosition;
            transform.localRotation = Quaternion.Euler(previewLocalEulerAngles);
            SetPoseAvailable(true);
            return;
        }

        // 実機でコントローラーが見つからない場合、最後のPoseへ棒だけを残さない。
        SetPoseAvailable(false);
    }

    void SetPoseAvailable(bool available)
    {
        if (IsPoseAvailable == available)
            return;

        IsPoseAvailable = available;
        PoseAvailabilityChanged?.Invoke(available);
    }
}
