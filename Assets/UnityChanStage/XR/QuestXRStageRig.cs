using UnityEngine;

public static class QuestXRStageRig
{
    public static GameObject Create(Vector3 originPosition, Quaternion originRotation, Vector3 previewHeadPosition)
    {
        var rig = new GameObject("Quest XR Stage Rig");
        rig.transform.SetPositionAndRotation(originPosition, originRotation);

        var trackingSpace = new GameObject("Tracking Space");
        trackingSpace.transform.SetParent(rig.transform, false);

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
        camera.stereoTargetEye = StereoTargetEyeMask.Both;

        cameraObject.AddComponent<AudioListener>();

        var poseDriver = cameraObject.AddComponent<QuestXRHeadPoseDriver>();
        poseDriver.previewLocalPosition = previewHeadPosition;

        rig.AddComponent<QuestXRInput>();
        return rig;
    }
}
