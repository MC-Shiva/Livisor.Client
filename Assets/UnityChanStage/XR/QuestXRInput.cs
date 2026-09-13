using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class QuestXRInput : MonoBehaviour
{
    public KeyCode editorRecenterKey = KeyCode.R;

    private readonly List<InputDevice> _controllers = new List<InputDevice>();
    private Transform _rigTransform;
    private bool _primaryButtonWasPressed;
    private bool _secondaryButtonWasPressed;
    private bool _demoControls;

    void Awake()
    {
        _rigTransform = transform;
        _demoControls = FindFirstObjectByType<DemoSceneController>() != null;
        RefreshControllers();
    }

    void Update()
    {
        if (_controllers.Count == 0 || !_controllers[0].isValid)
            RefreshControllers();

        var primaryPressed = !_demoControls && IsButtonPressed(CommonUsages.primaryButton);
        if ((primaryPressed && !_primaryButtonWasPressed) || Input.GetKeyDown(editorRecenterKey))
            RecenterYaw();
        _primaryButtonWasPressed = primaryPressed;

        bool secondaryPressed;
        // DemoのYは銀テープに使い、視点切替は右手のBだけにする。
        if (_demoControls)
            InputDevices.GetDeviceAtXRNode(XRNode.RightHand).TryGetFeatureValue(CommonUsages.secondaryButton, out secondaryPressed);
        else
            secondaryPressed = IsButtonPressed(CommonUsages.secondaryButton);
        if (secondaryPressed && !_secondaryButtonWasPressed)
            GetComponent<UnityChanEyeCamera>()?.ToggleView();
        _secondaryButtonWasPressed = secondaryPressed;
    }

    private void RefreshControllers()
    {
        _controllers.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _controllers);
    }

    private bool IsButtonPressed(InputFeatureUsage<bool> button)
    {
        foreach (var controller in _controllers)
        {
            if (controller.TryGetFeatureValue(button, out var pressed) && pressed)
                return true;
        }
        return false;
    }

    private void RecenterYaw()
    {
        var eyeCamera = GetComponent<UnityChanEyeCamera>();
        if (eyeCamera && eyeCamera.Recenter())
            return;

        var camera = Camera.main;
        if (!camera)
            return;

        var yaw = camera.transform.eulerAngles.y;
        _rigTransform.rotation = Quaternion.Euler(0.0f, _rigTransform.eulerAngles.y - yaw + 180.0f, 0.0f);
    }
}
