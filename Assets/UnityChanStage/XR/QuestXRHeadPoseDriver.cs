using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class QuestXRHeadPoseDriver : MonoBehaviour
{
    public Vector3 previewLocalPosition = new Vector3(0.0f, 1.35f, 0.0f);

    private InputDevice _headDevice;

    void OnEnable()
    {
        TryResolveHeadDevice();
        ApplyPreviewPose();
    }

    void Update()
    {
        if (!_headDevice.isValid)
            TryResolveHeadDevice();

        if (!_headDevice.isValid)
        {
            ApplyPreviewPose();
            return;
        }

        if (_headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out var position))
            transform.localPosition = position;
        else
            transform.localPosition = previewLocalPosition;

        if (_headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
            transform.localRotation = rotation;
    }

    private void TryResolveHeadDevice()
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.CenterEye, devices);
        _headDevice = devices.Count > 0 ? devices[0] : default;
    }

    private void ApplyPreviewPose()
    {
        transform.localPosition = previewLocalPosition;
        transform.localRotation = Quaternion.identity;
    }
}
