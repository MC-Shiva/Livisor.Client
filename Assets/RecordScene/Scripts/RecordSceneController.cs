using UnityEngine;
using UnityEngine.XR;

/// <summary>録画用のローカル操作。通信や時刻指定の自動演出は使わない。</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class RecordSceneController : MonoBehaviour
{
    [SerializeField] StageDirector _stageDirector;
    [SerializeField] LightningVfxController _lightning;
    [SerializeField, Tooltip("シーン開始時にライブを自動再生する。")]
    bool _playOnStart = true;

    bool _primaryWasPressed;
    bool _secondaryWasPressed;

    void Start()
    {
        if (_stageDirector == null || _lightning == null)
        {
            Debug.LogError("RecordSceneController requires StageDirector and LightningVfxController.", this);
            enabled = false;
            return;
        }

        // モデル付属のモーション確認用ボタンを録画へ映り込ませない。
        // ダンス用Animatorはそのまま動かし、手動モーション切替だけを無効にする。
        if (_stageDirector.PerformerHips != null)
            foreach (var control in _stageDirector.PerformerHips.GetComponentsInParent<UnityChan.IdleChanger>())
                control.enabled = false;

        if (_playOnStart)
            _stageDirector.ResumePerformance();
    }

    void Update()
    {
        // 毎フレーム取得し直すため、起動後の接続や再接続にも対応する。
        var rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        var primary = rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out var a) && a;
        var secondary = rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out var b) && b;
        ProcessButtons(primary, secondary);

#if UNITY_EDITOR
        // 雷のLキーはLightningVfxControllerが処理する。
        if (Input.GetKeyDown(KeyCode.T)) FireSilverStreamers();
        if (Input.GetKeyDown(KeyCode.S)) _stageDirector.ResumePerformance();
        if (Input.GetKeyDown(KeyCode.P)) _stageDirector.PausePerformance();
#endif
    }

    internal void ProcessButtons(bool primary, bool secondary)
    {
        // 押し始めだけを扱い、長押しでの連射を防ぐ。A/Bの同時押しも個別に扱う。
        if (primary && !_primaryWasPressed) FireLightning();
        if (secondary && !_secondaryWasPressed) FireSilverStreamers();
        _primaryWasPressed = primary;
        _secondaryWasPressed = secondary;
    }

    public void FireLightning() => _lightning.Strike();
    public void FireSilverStreamers() => _stageDirector.FireSilverStreamers();

    void OnDisable()
    {
        _primaryWasPressed = false;
        _secondaryWasPressed = false;
    }
}
