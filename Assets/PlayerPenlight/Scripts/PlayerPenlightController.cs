using UnityEngine;

namespace Livisor.Live.Penlights
{
    /// <summary>
    /// 自分用ペンライトの表示状態を、コントローラー追跡可否と現在の視点に合わせる。
    /// Pose更新を止めないよう、ルートではなくVisualだけを表示・非表示にする。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerPenlightController : MonoBehaviour
    {
        [SerializeField]
        QuestXRControllerPoseDriver _poseDriver;

        [SerializeField]
        GameObject _visualRoot;

        UnityChanEyeCamera _viewSource;
        bool _poseAvailable;
        bool _audienceView = true;

        void Awake()
        {
            if (_poseDriver == null)
                _poseDriver = GetComponent<QuestXRControllerPoseDriver>();
            if (_visualRoot == null)
            {
                var visual = transform.Find("Visual");
                if (visual != null)
                    _visualRoot = visual.gameObject;
            }
        }

        void OnEnable()
        {
            if (_poseDriver != null)
            {
                _poseDriver.PoseAvailabilityChanged += HandlePoseAvailabilityChanged;
                _poseAvailable = _poseDriver.IsPoseAvailable;
            }
            else
            {
                _poseAvailable = true;
            }

            if (_viewSource != null)
            {
                _viewSource.ViewChanged += HandleViewChanged;
                _audienceView = !_viewSource.IsUnityChanView;
            }

            ApplyVisibility();
        }

        void OnDisable()
        {
            if (_poseDriver != null)
                _poseDriver.PoseAvailabilityChanged -= HandlePoseAvailabilityChanged;
            if (_viewSource != null)
                _viewSource.ViewChanged -= HandleViewChanged;
        }

        public void BindViewSource(UnityChanEyeCamera viewSource)
        {
            if (_viewSource == viewSource)
            {
                _audienceView = viewSource == null || !viewSource.IsUnityChanView;
                ApplyVisibility();
                return;
            }

            if (isActiveAndEnabled && _viewSource != null)
                _viewSource.ViewChanged -= HandleViewChanged;

            _viewSource = viewSource;

            if (isActiveAndEnabled && _viewSource != null)
                _viewSource.ViewChanged += HandleViewChanged;

            _audienceView = _viewSource == null || !_viewSource.IsUnityChanView;
            ApplyVisibility();
        }

        void HandlePoseAvailabilityChanged(bool available)
        {
            _poseAvailable = available;
            ApplyVisibility();
        }

        void HandleViewChanged(bool isUnityChanView)
        {
            _audienceView = !isUnityChanView;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            if (_visualRoot != null && _visualRoot != gameObject)
                _visualRoot.SetActive(_poseAvailable && _audienceView);
        }
    }
}
