using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class QuestXRInput : MonoBehaviour
{
    public KeyCode editorRecenterKey = KeyCode.R;

    private readonly List<InputDevice> _controllers = new List<InputDevice>();
    private Transform _rigTransform;

    void Awake()
    {
        _rigTransform = transform;
        RefreshControllers();
    }

    void Update()
    {
        if (_controllers.Count == 0 || !_controllers[0].isValid)
            RefreshControllers();

        if (WasPrimaryButtonPressed() || Input.GetKeyDown(editorRecenterKey))
            RecenterYaw();
    }

    private void RefreshControllers()
    {
        _controllers.Clear();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _controllers);
    }

    private bool WasPrimaryButtonPressed()
    {
        foreach (var controller in _controllers)
        {
            if (controller.TryGetFeatureValue(CommonUsages.primaryButton, out var pressed) && pressed)
                return true;
        }
        return false;
    }

    private void RecenterYaw()
    {
        var camera = Camera.main;
        if (!camera)
            return;

        var yaw = camera.transform.eulerAngles.y;
        _rigTransform.rotation = Quaternion.Euler(0.0f, _rigTransform.eulerAngles.y - yaw + 180.0f, 0.0f);
    }
}
