using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class QuestXRStageRig
{
    public static GameObject Create(
        Vector3 originPosition,
        Quaternion originRotation,
        Vector3 previewHeadPosition,
        GameObject playerPenlightPrefab)
    {
        var rig = new GameObject("Quest XR Stage Rig");
        rig.transform.SetPositionAndRotation(originPosition, originRotation);

        var trackingSpace = new GameObject("Tracking Space");
        trackingSpace.transform.SetParent(rig.transform, false);

        if (playerPenlightPrefab != null)
            Object.Instantiate(playerPenlightPrefab, trackingSpace.transform, false);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(trackingSpace.transform, false);
        cameraObject.transform.localPosition = previewHeadPosition;
        cameraObject.transform.localRotation = Quaternion.identity;

        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 200.0f;
        camera.allowHDR = true;

        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.dithering = true;
        cameraData.volumeLayerMask = 1 << 0; // Default layer
        cameraData.volumeTrigger = camera.transform;
        //Build in専用APIのためコメントアウト
        //camera.stereoTargetEye = StereoTargetEyeMask.Both;

        cameraObject.AddComponent<AudioListener>();

        var poseDriver = cameraObject.AddComponent<QuestXRHeadPoseDriver>();
        poseDriver.previewLocalPosition = previewHeadPosition;

        rig.AddComponent<QuestXRInput>();
        return rig;
    }
}
