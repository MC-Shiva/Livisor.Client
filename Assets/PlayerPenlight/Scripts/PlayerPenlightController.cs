using UnityEngine;

namespace Livisor.Live.Penlights
{
    // ReaktionのUpdate（既定順序0）より後に実行し、観客席と同じ最新音声値を使う。
    /// <summary>
    /// 自分用ペンライトの表示状態と、音楽に連動する色・発光強度を管理する。
    /// Pose更新を止めないよう、ルートではなくVisualだけを表示・非表示にする。
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PlayerPenlightController : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");

        [SerializeField]
        QuestXRControllerPoseDriver _poseDriver;

        [SerializeField]
        GameObject _visualRoot;

        [Header("Audio Reactive Appearance")]
        [SerializeField]
        Renderer _visualRenderer;

        [SerializeField]
        PenlightAudioReactiveSettings _audioReactive = default;

        [SerializeField, Min(0.0f)]
        float _emissionIntensity = 2.0f;

        [SerializeField]
        uint _audienceRandomSeed = 12345u;

        UnityChanEyeCamera _viewSource;
        IPenlightAudioSource _audioSource;
        MaterialPropertyBlock _materialProperties;
        float _smoothedAudioLevel;
        float _colorSpreadOffset;
        bool _poseAvailable;
        bool _audienceView = true;
        bool _missingAudioSourceWarningIssued;

        void Reset()
        {
            _audioReactive = PenlightAudioReactiveSettings.Default();
            _audioReactive.input = PenlightAudioInput.Bass;
            _emissionIntensity = 2.0f;
            _audienceRandomSeed = 12345u;
        }

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

            if (_visualRenderer == null && _visualRoot != null)
                _visualRenderer = _visualRoot.GetComponentInChildren<Renderer>(true);

            _audioReactive.Clamp();
            _emissionIntensity = Mathf.Max(0.0f, _emissionIntensity);
            _colorSpreadOffset = CalculateAudienceColorSpreadOffset();
            _materialProperties = new MaterialPropertyBlock();
            ApplyAudioReactiveAppearance();
        }

        void OnValidate()
        {
            _audioReactive.Clamp();
            _emissionIntensity = Mathf.Max(0.0f, _emissionIntensity);
            _colorSpreadOffset = CalculateAudienceColorSpreadOffset();
        }

        void Update()
        {
            var hasAudio = _audioSource != null && _audioSource.IsAvailable;
            if (!hasAudio && !_missingAudioSourceWarningIssued)
            {
                Debug.LogWarning(
                    "Player penlight requires a penlight audio source for audio-reactive color.",
                    this);
                _missingAudioSourceWarningIssued = true;
            }

            var targetLevel = hasAudio
                ? Mathf.Clamp01(_audioSource.GetLevel(_audioReactive.input) * _audioReactive.gain)
                : 0.0f;
            var responseSpeed = targetLevel > _smoothedAudioLevel
                ? _audioReactive.attackSpeed
                : _audioReactive.releaseSpeed;
            _smoothedAudioLevel = Mathf.MoveTowards(
                _smoothedAudioLevel,
                targetLevel,
                responseSpeed * Time.deltaTime);

            ApplyAudioReactiveAppearance();
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

        /// <summary>観客席と共有する音声解析結果をStageDirectorから設定する。</summary>
        public void SetAudioSource(IPenlightAudioSource audioSource)
        {
            _audioSource = audioSource;
            _missingAudioSourceWarningIssued = false;
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

        void ApplyAudioReactiveAppearance()
        {
            if (_visualRenderer == null || _materialProperties == null)
                return;

            // 観客の先頭個体と同じSeed計算を使い、同じ色変化へ揃える。
            var level = Mathf.Clamp01(_smoothedAudioLevel + _colorSpreadOffset);
            var color = Color.Lerp(
                _audioReactive.lowLevelColor,
                _audioReactive.highLevelColor,
                level);
            var intensity = _emissionIntensity * Mathf.Lerp(
                _audioReactive.minimumEmissionMultiplier,
                _audioReactive.maximumEmissionMultiplier,
                level);

            _visualRenderer.GetPropertyBlock(_materialProperties);
            _materialProperties.SetColor(BaseColorId, color);
            _materialProperties.SetFloat(EmissionIntensityId, intensity);
            _visualRenderer.SetPropertyBlock(_materialProperties);
        }

        float CalculateAudienceColorSpreadOffset()
        {
            // AudiencePenlightZoneの先頭個体とAudiencePenlightControllerの色Hashに合わせる。
            var seed = _audienceRandomSeed;
            var value = seed ^ (0x9e3779b9u + (seed << 6) + (seed >> 2));
            value = Hash(value);
            if (value == 0u)
                value = 1u;

            var random01 = (Hash(value) & 0x00ffffffu) / 16777215.0f;
            return (random01 - 0.5f) * _audioReactive.perStickColorSpread;
        }

        static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
