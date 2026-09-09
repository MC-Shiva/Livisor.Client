using Livisor.Live.Penlights;
using UnityEngine;
using UnityEngine.Rendering;

namespace Livisor.Live.Effects
{
    /// <summary>低音に合わせた筐体の振動と、メッシュを共有する2枚の残像。</summary>
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SpeakerVibrationController : MonoBehaviour
    {
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        [Header("Vibration")]
        [SerializeField, Tooltip("スピーカーのローカル座標での振動方向。X=左右、Y=上下、Z=前後。ベクトルの長さは振幅に影響せず、すべて0にすると動かない。")]
        Vector3 _direction = Vector3.right;
        [SerializeField, Min(0), Tooltip("1秒あたりの振動の往復回数（Hz）。大きいほど速く揺れる。揺れ幅はMax Amplitudeで調整する。")]
        float _frequency = 12.0f;
        [SerializeField, Min(0), Tooltip("初期位置から片側への最大振幅（ワールド座標のm）。0.01で最大1cm。実際の振幅は低音の強さに比例する。")]
        float _maxAmplitude = 0.1f;
        [SerializeField, Min(0), Tooltip("低音が強くなったときの反応を滑らかにする時定数（秒）。小さいほど素早く反応し、0で即座に追従する。")]
        float _audioAttack = 0.04f;
        [SerializeField, Min(0), Tooltip("低音が弱くなったときの反応を滑らかにする時定数（秒）。大きいほど振動の余韻が長くなり、0で即座に追従する。")]
        float _audioRelease = 0.12f;

        [Header("Afterimages")]
        [SerializeField, Tooltip("残像用マテリアルの雛形。Livisor/Speaker Afterimageシェーダーを使用する。生成時に本体の基本色・テクスチャをコピーする。未設定では残像を生成しない。")]
        Material _afterimageMaterial;
        [SerializeField, Range(0, 1), Tooltip("残像の出現に必要な、平滑化後の低音の強さ（0〜1）。この値以上の状態がActivation Duration秒続くと出現する。小さいほど出現しやすい。")]
        float _activationLevel = 0.65f;
        [SerializeField, Range(0, 1), Tooltip("平滑化後の低音がこの値（0〜1）を下回ると残像が消え始める。Activation Levelより低くすると境界付近の点滅を抑えられる。")]
        float _deactivationLevel = 0.45f;
        [SerializeField, Min(0), Tooltip("低音がActivation Level以上で連続して鳴る必要時間（秒）。大きいほど短いピークでは残像が出にくくなる。0では待ち時間なし。")]
        float _activationDuration = 0.15f;
        [SerializeField, Min(0), Tooltip("残像の表示係数を0から1へ上げる時間（秒）。大きいほどゆっくり現れる。0で即座に表示する。濃さには低音の強さも影響する。")]
        float _fadeInDuration = 0.1f;
        [SerializeField, Min(0), Tooltip("残像の表示係数を1から0へ下げる時間（秒）。大きいほどゆっくり消える。0で即座に消す。濃さには低音の強さも影響する。")]
        float _fadeOutDuration = 0.2f;
        [SerializeField, Min(0), Tooltip("本体から振動方向の両側に置く各残像までの最大距離（ワールド座標のm）。大きいほど残像が広がる。実際の距離は低音の強さとフェードに応じて変わる。")]
        float _maxSpread = 0.03f;
        [SerializeField, Range(0, 1), Tooltip("各残像の最大不透明度（0〜1）。大きいほど濃く、0で非表示。実際の濃さは低音の強さ・フェード・元の色やテクスチャの透明度にも依存する。")]
        float _maxOpacity = 0.15f;

        readonly MeshRenderer[] _ghosts = new MeshRenderer[2];
        Material[] _ghostMaterials;
        MaterialPropertyBlock _properties;
        IPenlightAudioSource _audio;
        StageDirector _director;
        MusicPlayerController _music;
        Vector3 _restPosition;
        float _phase;
        float _level;
        float _aboveThresholdTime;
        float _visibility;
        bool _afterimagesActive;

        public void Initialize(IPenlightAudioSource audio, StageDirector director)
        {
            _audio = audio;
            _director = director;
            _music = director != null ? director.MusicPlayerController : null;
        }

        void Awake()
        {
            _restPosition = transform.localPosition;
            _properties = new MaterialPropertyBlock();
            CreateAfterimages();
        }

        void OnValidate()
        {
            _deactivationLevel = Mathf.Min(_deactivationLevel, _activationLevel);
        }

        void Update()
        {
            // Reaktion can retain a nonzero value while paused or after the clip ends.
            if (_director != null && _director.IsPerformancePaused)
                return;

            bool playing = _music != null && _music.HasStarted &&
                _music.MainSource != null && _music.MainSource.isPlaying;
            float input = playing && _audio != null && _audio.IsAvailable
                ? Mathf.Clamp01(_audio.GetLevel(PenlightAudioInput.Bass)) : 0.0f;
            Advance(input, playing, Time.deltaTime);
        }

        void Advance(float input, bool playing, float deltaTime)
        {
            float smoothing = input > _level ? _audioAttack : _audioRelease;
            _level = Mathf.Lerp(_level, input,
                smoothing > 0 ? 1.0f - Mathf.Exp(-deltaTime / smoothing) : 1.0f);
            if (input == 0 && _level < 0.0001f)
                _level = 0;

            _phase = Mathf.Repeat(_phase + deltaTime * _frequency, 1.0f);
            Vector3 worldAxis = transform.TransformDirection(_direction.normalized);
            // Convert world metres to the parent's coordinates, including scaled stages.
            Vector3 parentAxis = transform.parent != null
                ? transform.parent.InverseTransformVector(worldAxis) : worldAxis;
            transform.localPosition = _restPosition +
                parentAxis * (Mathf.Sin(_phase * Mathf.PI * 2.0f) * _maxAmplitude * _level);

            if (!playing || _level < _deactivationLevel)
                _afterimagesActive = false;

            if (playing && _level >= _activationLevel)
            {
                _aboveThresholdTime += deltaTime;
                if (_aboveThresholdTime >= _activationDuration)
                    _afterimagesActive = true;
            }
            else
                _aboveThresholdTime = 0;

            float fadeDuration = _afterimagesActive ? _fadeInDuration : _fadeOutDuration;
            _visibility = Mathf.MoveTowards(_visibility, _afterimagesActive ? 1 : 0,
                fadeDuration > 0 ? deltaTime / fadeDuration : 1.0f);
            _properties.SetFloat(OpacityId, _maxOpacity * _visibility * _level);

            // Children inherit the body's vibration; only their extra spread is applied here.
            Vector3 localSpread = transform.InverseTransformVector(worldAxis) *
                (_maxSpread * _level * _visibility);
            for (int i = 0; i < _ghosts.Length; i++)
            {
                var ghost = _ghosts[i];
                if (ghost == null)
                    continue;
                ghost.enabled = _visibility * _level * _maxOpacity > 0.0001f;
                ghost.transform.localPosition = localSpread * (i == 0 ? -1 : 1);
                if (ghost.enabled)
                    ghost.SetPropertyBlock(_properties);
            }
        }

        void CreateAfterimages()
        {
            var mesh = GetComponent<MeshFilter>().sharedMesh;
            var sourceRenderer = GetComponent<MeshRenderer>();
            if (_afterimageMaterial == null || mesh == null)
            {
                Debug.LogWarning("Speaker afterimages require a mesh and afterimage material.", this);
                return;
            }

            var sourceMaterials = sourceRenderer.sharedMaterials;
            _ghostMaterials = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                var material = new Material(_afterimageMaterial)
                {
                    name = $"Speaker Afterimage ({i})"
                };
                var source = sourceMaterials[i];
                if (source != null)
                {
                    string colorName = source.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                    if (source.HasProperty(colorName))
                        material.SetColor("_BaseColor", source.GetColor(colorName));
                    string mapName = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    if (source.HasProperty(mapName))
                    {
                        material.SetTexture("_BaseMap", source.GetTexture(mapName));
                        material.SetTextureScale("_BaseMap", source.GetTextureScale(mapName));
                        material.SetTextureOffset("_BaseMap", source.GetTextureOffset(mapName));
                    }
                }
                _ghostMaterials[i] = material;
            }

            for (int i = 0; i < _ghosts.Length; i++)
            {
                var child = new GameObject($"Speaker Afterimage {i + 1}");
                child.layer = gameObject.layer;
                child.transform.SetParent(transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.enabled = false;
                renderer.sharedMaterials = _ghostMaterials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
                _ghosts[i] = renderer;
            }
        }

        void OnDisable()
        {
            transform.localPosition = _restPosition;
            _phase = _level = _aboveThresholdTime = _visibility = 0;
            _afterimagesActive = false;
            foreach (var ghost in _ghosts)
                if (ghost != null)
                    ghost.enabled = false;
        }

        void OnDestroy()
        {
            foreach (var ghost in _ghosts)
                if (ghost != null)
                    Destroy(ghost.gameObject);
            if (_ghostMaterials != null)
                foreach (var material in _ghostMaterials)
                    Destroy(material);
        }
    }
}
