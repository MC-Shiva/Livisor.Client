using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Livisor.Live.Penlights
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class AudiencePenlightController : MonoBehaviour
    {
        static readonly int InstanceColorBufferId = Shader.PropertyToID("_InstanceColorBuffer");
        static readonly int InstanceIdOffsetId = Shader.PropertyToID("_InstanceIDOffset");
        static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");

        [Header("Rendering")]
        [SerializeField]
        Material _material;

        [SerializeField, Range(1, 511)]
        int _maxInstancesPerDraw = 511;

        [SerializeField]
        bool _useMainCameraForViewerExclusion = true;

        [Header("Appearance")]
        [SerializeField]
        PenlightAppearanceSettings _appearance = default;

        [Header("Motion")]
        [SerializeField]
        PenlightMotionSettings _motion = default;

        NativeArray<float3> _shoulderPositions;
        NativeArray<quaternion> _baseRotations;
        NativeArray<float> _groupTimingOffsets;
        NativeArray<uint> _seeds;
        NativeArray<Matrix4x4> _matrices;
        NativeArray<float4> _colors;

        GraphicsBuffer _colorBuffer;
        MaterialPropertyBlock _materialProperties;
        Mesh _runtimeMesh;
        Bounds _worldBounds;
        int _instanceCount;
        bool _initialized;
        bool _rebuildRequested;
        bool _missingAudioSourceWarningIssued;
        float _smoothedAudioLevel;
        float _nextAudioColorUpdateTime;
        Matrix4x4 _placementMatrix;
        IPenlightTimeSource _timeSource;
        IPenlightAudioSource _audioSource;

        public int InstanceCount => _instanceCount;
        public bool IsInitialized => _initialized;

        void Reset()
        {
            _appearance = PenlightAppearanceSettings.Default();
            _motion = PenlightMotionSettings.Default();
        }

        void OnEnable()
        {
            _timeSource ??= new LocalPenlightTimeSource();
            if (_initialized)
                RebuildAudience();
        }

        void OnDisable()
        {
            ReleaseResources();
        }

        void OnValidate()
        {
            _appearance.Clamp();
            _motion.Clamp();
            _maxInstancesPerDraw = Mathf.Clamp(_maxInstancesPerDraw, 1, 511);
            _rebuildRequested = true;
        }

        void OnTransformChildrenChanged()
        {
            _rebuildRequested = true;
        }

        void Update()
        {
            if (!_initialized)
                return;

            if (transform.localToWorldMatrix != _placementMatrix)
                _rebuildRequested = true;

            if (_rebuildRequested)
                RebuildAudience();

            if (_instanceCount == 0 || _material == null || _runtimeMesh == null)
                return;

            UpdateAudioReactiveAppearance();

            var showTime = (float)(_timeSource?.CurrentTime ?? Time.timeAsDouble);
            var job = new PenlightAnimationJob
            {
                shoulderPositions = _shoulderPositions,
                baseRotations = _baseRotations,
                groupTimingOffsets = _groupTimingOffsets,
                seeds = _seeds,
                time = showTime,
                bpm = _motion.bpm,
                beatsPerSwing = _motion.beatsPerSwing,
                swingAngleRadians = math.radians(_motion.swingAngleDegrees),
                directionRandomness = _motion.directionRandomness,
                armLength = _motion.armLength,
                timingOffsetSeconds = _motion.timingOffsetSeconds,
                rhythmNoiseAmount = _motion.rhythmNoiseAmount,
                customAxis = _motion.customAxis,
                direction = (int)_motion.direction,
                rhythm = (int)_motion.rhythm,
                matrices = _matrices
            };
            job.Schedule(_instanceCount, 64).Complete();

            RenderInstances();
        }

        public void SetTimeSource(IPenlightTimeSource timeSource)
        {
            _timeSource = timeSource ?? new LocalPenlightTimeSource();
        }

        public void SetAudioSource(IPenlightAudioSource audioSource)
        {
            _audioSource = audioSource;
            _missingAudioSourceWarningIssued = false;
        }

        /// <summary>
        /// Places this rig under the stage-defined placement transform, then builds
        /// its world-space instance data after the stage and camera exist.
        /// </summary>
        public void InitializeAt(Transform placementTransform)
        {
            if (placementTransform == transform ||
                (placementTransform != null && placementTransform.IsChildOf(transform)))
            {
                Debug.LogError("Audience penlight placement cannot be the rig itself or one of its children.", this);
                return;
            }

            if (placementTransform != null)
            {
                transform.SetParent(placementTransform, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
            }
            else
            {
                Debug.LogWarning(
                    "Audience penlight placement is not assigned. The prefab transform will be used.",
                    this);
            }

            _initialized = true;
            if (isActiveAndEnabled)
            {
                RebuildAudience();
                Debug.Log(
                    $"Audience penlights initialized at '{placementTransform?.name ?? "prefab transform"}'. " +
                    $"Instances: {_instanceCount}, Bounds: {_worldBounds}, " +
                    $"Audio: {_audioSource?.Description ?? "not assigned"}",
                    this);
            }
            else
            {
                _rebuildRequested = true;
            }
        }

        [ContextMenu("Rebuild Audience")]
        public void RebuildAudience()
        {
            _rebuildRequested = false;
            _placementMatrix = transform.localToWorldMatrix;
            ReleaseResources();

            if (_material == null)
            {
                Debug.LogWarning("Audience penlight material is not assigned.", this);
                return;
            }

            var zones = GetComponentsInChildren<AudiencePenlightZone>(true);
            if (zones.Length == 0)
            {
                Debug.LogWarning("AudiencePenlightController requires at least one AudiencePenlightZone.", this);
                return;
            }

            var capacity = 0;
            foreach (var zone in zones)
                if (zone.isActiveAndEnabled) capacity += zone.MaximumInstanceCount;

            var positions = new List<float3>(capacity);
            var rotations = new List<quaternion>(capacity);
            var offsets = new List<float>(capacity);
            var seeds = new List<uint>(capacity);

            var viewerCamera = _useMainCameraForViewerExclusion ? Camera.main : null;
            var hasViewer = viewerCamera != null;
            var viewerPosition = hasViewer ? (float3)viewerCamera.transform.position : float3.zero;
            var globalIndex = 0;

            foreach (var zone in zones)
            {
                if (!zone.isActiveAndEnabled) continue;
                zone.CollectInstances(
                    hasViewer,
                    viewerPosition,
                    _appearance.randomSeed,
                    ref globalIndex,
                    positions,
                    rotations,
                    offsets,
                    seeds);
            }

            _instanceCount = positions.Count;
            if (_instanceCount == 0)
                return;

            _shoulderPositions = new NativeArray<float3>(positions.ToArray(), Allocator.Persistent);
            _baseRotations = new NativeArray<quaternion>(rotations.ToArray(), Allocator.Persistent);
            _groupTimingOffsets = new NativeArray<float>(offsets.ToArray(), Allocator.Persistent);
            _seeds = new NativeArray<uint>(seeds.ToArray(), Allocator.Persistent);
            _matrices = new NativeArray<Matrix4x4>(
                _instanceCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            _colors = new NativeArray<float4>(
                _instanceCount,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            BuildColors();
            _colorBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _instanceCount,
                sizeof(float) * 4);
            _colorBuffer.SetData(_colors);

            _materialProperties = new MaterialPropertyBlock();
            _materialProperties.SetBuffer(InstanceColorBufferId, _colorBuffer);
            SetEmissionIntensity(_smoothedAudioLevel);

            _nextAudioColorUpdateTime = 0.0f;

            _runtimeMesh = CreateCylinderMesh(8, 0.012f, 0.18f);
            CalculateWorldBounds(positions);
        }

        void BuildColors()
        {
            var palette = _appearance.randomColorPalette;
            for (var i = 0; i < _instanceCount; i++)
            {
                Color color;
                if (_appearance.colorMode == PenlightColorMode.Fixed)
                {
                    color = _appearance.baseColor;
                }
                else if (_appearance.colorMode == PenlightColorMode.AudioReactive)
                {
                    color = EvaluateAudioReactiveColor(i, _smoothedAudioLevel);
                }
                else if (palette != null && palette.Length > 0)
                {
                    var index = (int)(Hash(_seeds[i]) % (uint)palette.Length);
                    color = palette[index];
                }
                else
                {
                    var hue = (Hash(_seeds[i]) & 0x00ffffffu) / 16777215.0f;
                    color = Color.HSVToRGB(hue, 0.85f, 1.0f, true);
                }

                _colors[i] = new float4(color.r, color.g, color.b, color.a);
            }
        }

        void UpdateAudioReactiveAppearance()
        {
            if (_appearance.colorMode != PenlightColorMode.AudioReactive ||
                _colorBuffer == null ||
                !_colors.IsCreated)
                return;

            var hasAudio = _audioSource != null && _audioSource.IsAvailable;
            if (!hasAudio && !_missingAudioSourceWarningIssued)
            {
                Debug.LogWarning(
                    "Audio Reactive color mode is selected, but no penlight audio source is available.",
                    this);
                _missingAudioSourceWarningIssued = true;
            }

            var audio = _appearance.audioReactive;
            var targetLevel = hasAudio
                ? Mathf.Clamp01(_audioSource.GetLevel(audio.input) * audio.gain)
                : 0.0f;
            var responseSpeed = targetLevel > _smoothedAudioLevel
                ? audio.attackSpeed
                : audio.releaseSpeed;
            _smoothedAudioLevel = Mathf.MoveTowards(
                _smoothedAudioLevel,
                targetLevel,
                responseSpeed * Time.deltaTime);

            SetEmissionIntensity(_smoothedAudioLevel);

            if (Time.unscaledTime < _nextAudioColorUpdateTime)
                return;

            _nextAudioColorUpdateTime = Time.unscaledTime + 1.0f / audio.colorUpdateRateHz;
            for (var i = 0; i < _instanceCount; i++)
            {
                var color = EvaluateAudioReactiveColor(i, _smoothedAudioLevel);
                _colors[i] = new float4(color.r, color.g, color.b, color.a);
            }

            _colorBuffer.SetData(_colors);
        }

        Color EvaluateAudioReactiveColor(int instanceIndex, float audioLevel)
        {
            var audio = _appearance.audioReactive;
            var hash = Hash(_seeds[instanceIndex]);
            var random01 = (hash & 0x00ffffffu) / 16777215.0f;
            var spreadOffset = (random01 - 0.5f) * audio.perStickColorSpread;
            var colorLevel = Mathf.Clamp01(audioLevel + spreadOffset);
            return Color.Lerp(audio.lowLevelColor, audio.highLevelColor, colorLevel);
        }

        void SetEmissionIntensity(float audioLevel)
        {
            var intensity = _appearance.emissionIntensity;
            if (_appearance.colorMode == PenlightColorMode.AudioReactive)
            {
                var audio = _appearance.audioReactive;
                intensity *= Mathf.Lerp(
                    audio.minimumEmissionMultiplier,
                    audio.maximumEmissionMultiplier,
                    Mathf.Clamp01(audioLevel));
            }

            _materialProperties?.SetFloat(EmissionIntensityId, intensity);
        }

        void CalculateWorldBounds(List<float3> positions)
        {
            var first = (Vector3)positions[0];
            _worldBounds = new Bounds(first, Vector3.zero);
            for (var i = 1; i < positions.Count; i++)
                _worldBounds.Encapsulate((Vector3)positions[i]);

            _worldBounds.Expand((_motion.armLength + 0.5f) * 2.0f);
        }

        void RenderInstances()
        {
            var renderParams = new RenderParams(_material)
            {
                worldBounds = _worldBounds,
                matProps = _materialProperties,
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };

            for (var start = 0; start < _instanceCount; start += _maxInstancesPerDraw)
            {
                var count = Mathf.Min(_maxInstancesPerDraw, _instanceCount - start);
                _materialProperties.SetInt(InstanceIdOffsetId, start);
                renderParams.matProps = _materialProperties;
                Graphics.RenderMeshInstanced(
                    renderParams,
                    _runtimeMesh,
                    0,
                    _matrices,
                    count,
                    start);
            }
        }

        void ReleaseResources()
        {
            if (_shoulderPositions.IsCreated) _shoulderPositions.Dispose();
            if (_baseRotations.IsCreated) _baseRotations.Dispose();
            if (_groupTimingOffsets.IsCreated) _groupTimingOffsets.Dispose();
            if (_seeds.IsCreated) _seeds.Dispose();
            if (_matrices.IsCreated) _matrices.Dispose();
            if (_colors.IsCreated) _colors.Dispose();

            _colorBuffer?.Dispose();
            _colorBuffer = null;
            _materialProperties = null;

            if (_runtimeMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeMesh);
                else
                    DestroyImmediate(_runtimeMesh);
                _runtimeMesh = null;
            }

            _instanceCount = 0;
        }

        static Mesh CreateCylinderMesh(int sideCount, float radius, float height)
        {
            var vertices = new Vector3[sideCount * 2 + 2];
            var triangles = new int[sideCount * 12];
            var halfHeight = height * 0.5f;

            for (var i = 0; i < sideCount; i++)
            {
                var angle = i * Mathf.PI * 2.0f / sideCount;
                var x = Mathf.Cos(angle) * radius;
                var z = Mathf.Sin(angle) * radius;
                vertices[i * 2] = new Vector3(x, -halfHeight, z);
                vertices[i * 2 + 1] = new Vector3(x, halfHeight, z);
            }

            var bottomCenter = sideCount * 2;
            var topCenter = bottomCenter + 1;
            vertices[bottomCenter] = new Vector3(0.0f, -halfHeight, 0.0f);
            vertices[topCenter] = new Vector3(0.0f, halfHeight, 0.0f);

            for (var i = 0; i < sideCount; i++)
            {
                var next = (i + 1) % sideCount;
                var triangle = i * 12;
                var bottom = i * 2;
                var top = bottom + 1;
                var nextBottom = next * 2;
                var nextTop = nextBottom + 1;

                triangles[triangle] = bottom;
                triangles[triangle + 1] = top;
                triangles[triangle + 2] = nextTop;
                triangles[triangle + 3] = bottom;
                triangles[triangle + 4] = nextTop;
                triangles[triangle + 5] = nextBottom;

                triangles[triangle + 6] = bottomCenter;
                triangles[triangle + 7] = nextBottom;
                triangles[triangle + 8] = bottom;
                triangles[triangle + 9] = topCenter;
                triangles[triangle + 10] = top;
                triangles[triangle + 11] = nextTop;
            }

            var mesh = new Mesh
            {
                name = "Runtime Audience Penlight",
                hideFlags = HideFlags.DontSave,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
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
