using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

/// <summary>雷本体はVFX Graph、着弾は軽量メッシュ。発火とパラメーターを管理する。</summary>
[DisallowMultipleComponent]
public class LightningBolt : MonoBehaviour
{
    public enum Style { Bolt, Beam, Both }
    public enum Quality { Quest3, DesktopEnhanced }

    [Header("描画負荷")]
    [Tooltip("雷本体は両設定ともVFX Graph。Quest3は追加ライトとBloomなし。DesktopEnhancedは着弾のGPU火花と環境光を追加。")]
    [SerializeField] private Quality _quality = Quality.Quest3;
    [Header("雷撃")]
    [SerializeField] private Style _style = Style.Bolt;
    [ColorUsage(true, true)] [SerializeField] private Color _coreColor = new Color(0.85f, 0.96f, 1f);
    [ColorUsage(true, true)] [SerializeField] private Color _color = new Color(0.05f, 0.65f, 1f);
    [SerializeField, Min(0f)] private float _intensity = 5f;
    [SerializeField, Min(0.05f)] private float _width = 0.95f;
    [Tooltip("複数の柱が分かれる半径。柱そのものの太さとは独立。")]
    [SerializeField, Min(0f)] private float _bundleRadius = 0.7f;
    [SerializeField, Min(0.005f)] private float _branchWidth = 0.018f;
    [SerializeField, Min(0.005f)] private float _impactArcWidth = 0.025f;
    [SerializeField, Range(0f, 1f)] private float _angularity = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _entanglement = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _electricContrast = 0.85f;
    [SerializeField, Range(1f, 60f)] private float _arcSpeed = 24f;
    [SerializeField, Range(0f, 1f)] private float _bloomIntensity = 0.18f;
    [Header("落下と残光")]
    [SerializeField, Min(0.01f)] private float _descentDuration = 0.1f;
    [SerializeField, Min(0f)] private float _trunkHold = 0.16f;
    [SerializeField, Min(0.01f)] private float _trunkErase = 0.18f;
    [SerializeField, Min(0.01f)] private float _groundDuration = 0.65f;
    [SerializeField, Min(0f)] private float _impactRadius = 4.8f;


    private VisualEffect _stripVfx, _impactVfx;
    private LightningImpactMesh _impact;
    private Light _impactLight;
    private Volume _volume;
    private VolumeProfile _profile;
    private Bloom _bloom;
    private Vector3 _ground;
    private float _elapsed;
    private bool _playing, _impacted, _vfxAllowed;
    private static readonly int StrikeTimeId = Shader.PropertyToID("StrikeTime");

    public bool IsPlaying => _playing;
    public Style CurrentStyle { get => _style; set => _style = value; }
    public void ConfigurePerformance(Quality quality) => _quality = quality;
    public void ConfigureBundle(float radius) => _bundleRadius = Mathf.Max(0f, radius);
    public void ConfigureStrike(Color core, Color edge, float width, float descent, float radius)
    {
        _coreColor = core / Mathf.Max(1f, core.maxColorComponent);
        _color = edge / Mathf.Max(1f, edge.maxColorComponent);
        _width = Mathf.Max(0.05f, width);
        _descentDuration = Mathf.Max(0.01f, descent);
        _impactRadius = Mathf.Max(0f, radius);
    }
    public void ConfigureElectricity(float contrast, float speed, float bloom,
        float branchWidth = 0.018f, float impactArcWidth = 0.025f)
    {
        _electricContrast = Mathf.Clamp01(contrast);
        _arcSpeed = Mathf.Clamp(speed, 1f, 60f);
        _bloomIntensity = Mathf.Clamp01(bloom);
        _branchWidth = Mathf.Max(0.005f, branchWidth);
        _impactArcWidth = Mathf.Max(0.005f, impactArcWidth);
    }
    public void ConfigureShape(float angularity, float entanglement)
    {
        _angularity = Mathf.Clamp01(angularity);
        _entanglement = Mathf.Clamp01(entanglement);
    }
    public void ConfigureTiming(float hold, float erase, float groundDuration)
    {
        _trunkHold = Mathf.Max(0f, hold);
        _trunkErase = Mathf.Max(0.01f, erase);
        _groundDuration = Mathf.Max(0.01f, groundDuration);
    }


    private void Awake() => Warmup();
    public void Warmup()
    {
        if (_stripVfx == null)
        {
            var asset = Resources.Load<VisualEffectAsset>("Lightning/VFX_LightningBolt");
            if (asset == null) return;
            var go = new GameObject("VFX Lightning Strips");
            go.layer = gameObject.layer;
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _stripVfx = go.AddComponent<VisualEffect>();
            _stripVfx.initialEventName = "None";
            _stripVfx.visualEffectAsset = asset;
        }
        if (_impact == null)
        {
            var go = new GameObject("Lightning Impact");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _impact = go.AddComponent<LightningImpactMesh>();
            _impact.Warmup();
        }
        PrepareExtras();
    }

    public void Play(Vector3 top, Vector3 ground)
    {
        Warmup();
        if (_stripVfx == null || !SystemInfo.supportsComputeShaders
            || SystemInfo.maxComputeBufferInputsVertex == 0
            || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3)
        {
            Debug.LogError("[Lightning] 雷本体のVFX Graphが使用できません。アセットと対応描画APIを確認してください。AndroidではVulkanを使用します。", this);
            return;
        }
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        ResetEffects();
        _ground = ground;
        _elapsed = 0f;
        _impacted = false;
        _playing = true;
        uint seed = (uint)Random.Range(1, 1000000);
        Color core = _coreColor * _intensity;
        Color edge = _color * Mathf.Min(_intensity * 0.48f, 2.4f);
        _stripVfx.SetVector3("Top", top);
        _stripVfx.SetVector3("Ground", ground);
        _stripVfx.SetVector3("BoundsCenter", (top + ground) * 0.5f);
        Vector3 delta = ground - top;
        float padding = Mathf.Max(2f, _bundleRadius * 2f + _width) * 2f;
        _stripVfx.SetVector3("BoundsSize", new Vector3(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z))
            + Vector3.one * padding);
        _stripVfx.SetFloat(StrikeTimeId, 0f);
        _stripVfx.SetFloat("Descent", _descentDuration);
        _stripVfx.SetFloat("Hold", _trunkHold);
        _stripVfx.SetFloat("Erase", _trunkErase);
        _stripVfx.SetFloat("Width", _width);
        _stripVfx.SetFloat("BranchWidth", _branchWidth);
        _stripVfx.SetFloat("BundleRadius", _bundleRadius);
        _stripVfx.SetFloat("Angularity", _angularity);
        _stripVfx.SetFloat("Entanglement", _entanglement);
        _stripVfx.SetFloat("ArcSpeed", _arcSpeed);
        _stripVfx.SetFloat("Seed", seed);
        _stripVfx.SetFloat("BeamMode", (float)_style);
        _stripVfx.SetVector4("CoreColor", core);
        _stripVfx.SetVector4("EdgeColor", edge);
        _stripVfx.SetFloat("Contrast", _electricContrast);
        _impact.Begin(ground, _impactRadius, _impactArcWidth, _groundDuration,
            _quality == Quality.Quest3 || Application.platform == RuntimePlatform.Android,
            seed, core, edge, _electricContrast, _arcSpeed);
        _stripVfx.gameObject.SetActive(true);
        _stripVfx.Reinit();
        _stripVfx.SendEvent("OnPlay");
    }

    private void LateUpdate()
    {
        if (!_playing) return;
        _elapsed += Time.deltaTime;
        float age = _elapsed - _descentDuration;
        if (age >= Mathf.Max(_trunkHold + _trunkErase, _groundDuration))
        {
            gameObject.SetActive(false);
            return;
        }
        if (age >= _trunkHold + _trunkErase) _stripVfx.gameObject.SetActive(false);
        else _stripVfx.SetFloat(StrikeTimeId, _elapsed);
        if (age >= 0f && !_impacted)
        {
            _impacted = true;
            if (_impactVfx != null && _vfxAllowed && _impactRadius > 0f)
            {
                _impactVfx.transform.position = _ground + Vector3.up * 0.06f;
                _impactVfx.transform.localScale = Vector3.one * (_impactRadius / 4f);
                _impactVfx.gameObject.SetActive(true);
                _impactVfx.Reinit();
                _impactVfx.SendEvent("OnPlay");
            }
        }
        _impact.Render(age);
        UpdateExtras(age);
    }

    private void PrepareExtras()
    {
        bool enhanced = _quality == Quality.DesktopEnhanced && Application.platform != RuntimePlatform.Android;
        if (enhanced && _impactLight == null)
        {
            var go = new GameObject("Lightning Impact Light");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            _impactLight = go.AddComponent<Light>();
            _impactLight.type = LightType.Point;
            _impactLight.shadows = LightShadows.None;
            _impactLight.enabled = false;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.HideAndDontSave;
            _bloom = _profile.Add<Bloom>();
            _bloom.threshold.Override(1.2f);
            _bloom.scatter.Override(0.45f);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 20;
            _volume.weight = 0;
            _volume.sharedProfile = _profile;
        }
        bool supportsVfx = enhanced && SystemInfo.supportsComputeShaders && SystemInfo.maxComputeBufferInputsVertex > 0
            && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3;
        if (supportsVfx && _impactVfx == null)
        {
            var asset = Resources.Load<VisualEffectAsset>("Lightning/VFX_ElectricImpact");
            if (asset != null)
            {
                var go = new GameObject("VFX Impact Sparks");
                go.layer = gameObject.layer;
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                _impactVfx = go.AddComponent<VisualEffect>();
                _impactVfx.initialEventName = "None";
                _impactVfx.visualEffectAsset = asset;
            }
        }
        _vfxAllowed = supportsVfx;
        if (_impactVfx != null && !supportsVfx) _impactVfx.gameObject.SetActive(false);
        if (_volume != null) _volume.enabled = enhanced && _bloomIntensity > 0f;
    }
    private void UpdateExtras(float age)
    {
        if (_impactLight == null) return;
        bool enhanced = _quality == Quality.DesktopEnhanced && Application.platform != RuntimePlatform.Android;
        float flash = enhanced && age >= 0f && _impactRadius > 0f ? Mathf.Exp(-age * 28f) : 0f;
        _impactLight.enabled = flash > 0.01f;
        _impactLight.transform.position = _ground + Vector3.up * 0.5f;
        _impactLight.color = _color;
        _impactLight.range = _impactRadius * 1.5f;
        _impactLight.intensity = flash * 8f;
        _bloom.intensity.Override(_bloomIntensity);
        _volume.weight = flash;
    }

    private void ResetEffects()
    {
        if (_stripVfx != null) { _stripVfx.Stop(); _stripVfx.gameObject.SetActive(false); }
        if (_impactVfx != null) { _impactVfx.Stop(); _impactVfx.gameObject.SetActive(false); }
        if (_impactLight != null) _impactLight.enabled = false;
        if (_volume != null) _volume.weight = 0f;
    }
    private void OnDisable()
    {
        _playing = false;
        ResetEffects();
    }
    private void OnDestroy()
    {
        if (_bloom != null) Destroy(_bloom);
        if (_profile != null) Destroy(_profile);
    }
}
