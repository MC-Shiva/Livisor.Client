using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Livisor.Live.Penlights
{
    // ReaktionのUpdate（既定順序0）より後に実行し、当該フレームの最新音声値を使う。
    /// <summary>
    /// 観客席ペンライト全体の初期化、アニメーション計算、GPU Instancing描画を管理する。
    /// 一本ごとのGameObjectは生成せず、配置データと描画行列を配列として保持する。
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class AudiencePenlightController : MonoBehaviour
    {
        // 文字列検索を毎フレーム行わないよう、シェーダープロパティIDを事前生成する。
        static readonly int InstanceColorBufferId = Shader.PropertyToID("_InstanceColorBuffer");
        static readonly int InstanceIdOffsetId = Shader.PropertyToID("_InstanceIDOffset");
        static readonly int EmissionIntensityId = Shader.PropertyToID("_EmissionIntensity");

        // Inspectorから設定する描画・外観・振りアニメーションの設定。
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

        // RebuildAudience時に生成する、全インスタンス分のCPU側データ。
        // 同じインデックスが同じ一本のペンライトを表す。
        NativeArray<float3> _shoulderPositions;
        NativeArray<quaternion> _baseRotations;
        NativeArray<float> _groupTimingOffsets;
        NativeArray<uint> _seeds;
        NativeArray<Matrix4x4> _matrices;
        NativeArray<float4> _colors;

        // GPUへ渡す色バッファ、Material設定、共通Mesh、カリング用Bounds。
        GraphicsBuffer _colorBuffer;
        MaterialPropertyBlock _materialProperties;
        Mesh _runtimeMesh;
        Bounds _worldBounds;
        // 初期化状態、再構築要求、音声平滑化などの実行時状態。
        int _instanceCount;
        bool _initialized;
        bool _rebuildRequested;
        bool _missingAudioSourceWarningIssued;
        float _smoothedAudioLevel;
        float _nextAudioColorUpdateTime;
        Matrix4x4 _placementMatrix;
        IPenlightTimeSource _timeSource;
        IPenlightAudioSource _audioSource;

        /// <summary>カメラ周辺の除外後に、実際に生成されたペンライト本数。</summary>
        public int InstanceCount => _instanceCount;

        /// <summary>StageDirectorから明示初期化済みか。</summary>
        public bool IsInitialized => _initialized;

        void Reset()
        {
            // Add ComponentまたはReset実行時に、推奨初期値を設定する。
            _appearance = PenlightAppearanceSettings.Default();
            _motion = PenlightMotionSettings.Default();
        }

        void OnEnable()
        {
            _timeSource ??= new LocalPenlightTimeSource();

            // 初回OnEnableではRebuildしない。StageとCameraの生成後にInitializeAtから実行する。
            // 初期化後にGameObjectを再度有効化した場合だけ、解放済みリソースを再作成する。
            if (_initialized)
                RebuildAudience();
        }

        void OnDisable()
        {
            // NativeArrayやGraphicsBufferは明示解放が必要なため、無効化時に破棄する。
            ReleaseResources();
        }

        void OnValidate()
        {
            // Inspector編集値を補正し、Play中なら次のUpdateで安全に再構築する。
            _appearance.Clamp();
            _motion.Clamp();
            _maxInstancesPerDraw = Mathf.Clamp(_maxInstancesPerDraw, 1, 511);
            _rebuildRequested = true;
        }

        void OnTransformChildrenChanged()
        {
            // Zoneの追加・削除時は、インスタンス数と配置を作り直す必要がある。
            _rebuildRequested = true;
        }

        void Update()
        {
            // StageDirectorの明示初期化が完了するまで、Jobと描画を開始しない。
            if (!_initialized)
                return;

            // 配置先または親Transformが動いた場合、保存済みワールド座標とBoundsを更新する。
            if (transform.localToWorldMatrix != _placementMatrix)
                _rebuildRequested = true;

            if (_rebuildRequested)
                RebuildAudience();

            if (_instanceCount == 0 || _material == null || _runtimeMesh == null)
                return;

            // 色・輝度を先に更新してから、当該フレームの振り行列を計算する。
            UpdateAudioReactiveAppearance();

            // 時刻源を差し替えることで、将来Timelineやネットワーク同期時刻へ拡張できる。
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
            // 64本単位で並列処理し、描画前に全行列の完成を待つ。
            job.Schedule(_instanceCount, 64).Complete();

            RenderInstances();
        }

        public void SetTimeSource(IPenlightTimeSource timeSource)
        {
            // nullが渡された場合も、描画を止めずローカル時刻へ戻す。
            _timeSource = timeSource ?? new LocalPenlightTimeSource();
        }

        /// <summary>音楽連動色が参照する音声解析元を差し替える。</summary>
        public void SetAudioSource(IPenlightAudioSource audioSource)
        {
            _audioSource = audioSource;
            _missingAudioSourceWarningIssued = false;
        }

        /// <summary>
        /// Stage上の配置基準TransformへRigを接続し、確定後のワールド座標で初回構築する。
        /// StageDirectorがStage、Camera、MusicPlayerを生成し終えた後に呼び出す。
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
                // 配置基準の子でローカル原点へ揃え、Transform一つで観客席全体を調整可能にする。
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

            // ここからUpdateによるJob計算・描画を許可する。
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

        /// <summary>
        /// Zone設定から全インスタンスの配置・色・GPUリソース・Boundsを作り直す。
        /// ワールド座標を保存するため、配置Transformが確定した後に呼ぶ必要がある。
        /// </summary>
        [ContextMenu("Rebuild Audience")]
        public void RebuildAudience()
        {
            // 今回使用する配置行列を記録し、前回確保したリソースを先に解放する。
            _rebuildRequested = false;
            _placementMatrix = transform.localToWorldMatrix;
            ReleaseResources();

            if (_material == null)
            {
                Debug.LogWarning("Audience penlight material is not assigned.", this);
                return;
            }

            // 一つのRig配下に複数Zoneを置ける。非アクティブZoneは生成対象から除外する。
            var zones = GetComponentsInChildren<AudiencePenlightZone>(true);
            if (zones.Length == 0)
            {
                Debug.LogWarning("AudiencePenlightController requires at least one AudiencePenlightZone.", this);
                return;
            }

            // 除外前の最大本数をList容量に使い、追加時の再確保を減らす。
            var capacity = 0;
            foreach (var zone in zones)
                if (zone.isActiveAndEnabled) capacity += zone.MaximumInstanceCount;

            var positions = new List<float3>(capacity);
            var rotations = new List<quaternion>(capacity);
            var offsets = new List<float>(capacity);
            var seeds = new List<uint>(capacity);

            // HMD周囲にペンライトを置かないため、初期化済みMain Camera位置をZoneへ渡す。
            var viewerCamera = _useMainCameraForViewerExclusion ? Camera.main : null;
            var hasViewer = viewerCamera != null;
            var viewerPosition = hasViewer ? (float3)viewerCamera.transform.position : float3.zero;
            var globalIndex = 0;

            // 全Zoneのデータを同じ配列へ連結する。
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

            // 毎フレームJobから参照するため、破棄まで保持するPersistent NativeArrayへ変換する。
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

            // 色は固定・ランダムならここで確定し、音楽連動なら実行中に同じ配列を更新する。
            BuildColors();
            _colorBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                _instanceCount,
                sizeof(float) * 4);
            _colorBuffer.SetData(_colors);

            // 共有Material自体を書き換えず、このController専用のBufferと輝度を設定する。
            _materialProperties = new MaterialPropertyBlock();
            _materialProperties.SetBuffer(InstanceColorBufferId, _colorBuffer);
            SetEmissionIntensity(_smoothedAudioLevel);

            _nextAudioColorUpdateTime = 0.0f;

            // 全インスタンスで共有する軽量な八角柱Meshを一つだけ生成する。
            _runtimeMesh = CreateCylinderMesh(8, 0.012f, 0.18f);
            CalculateWorldBounds(positions);
        }

        void BuildColors()
        {
            // 色モードに応じて、全インスタンスの初期色をCPU側配列へ書き込む。
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
                    // 同じSeedなら毎回同じ色を選ぶため、再生ごとに配色が変わらない。
                    var index = (int)(Hash(_seeds[i]) % (uint)palette.Length);
                    color = palette[index];
                }
                else
                {
                    // パレットが空でも色が出るよう、SeedからHSVの色相を生成する。
                    var hue = (Hash(_seeds[i]) & 0x00ffffffu) / 16777215.0f;
                    color = Color.HSVToRGB(hue, 0.85f, 1.0f, true);
                }

                _colors[i] = new float4(color.r, color.g, color.b, color.a);
            }
        }

        void UpdateAudioReactiveAppearance()
        {
            // Fixed/RandomPaletteではGPU色バッファを更新せず、初期転送だけで済ませる。
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

            // 入力へGainを掛け、上昇と下降で別の速度を使って急なちらつきを抑える。
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

            // 輝度は軽量なMaterialPropertyBlock更新なので毎フレーム追従させる。
            SetEmissionIntensity(_smoothedAudioLevel);

            // 約2,000色のBuffer転送は設定Hzへ制限し、Questの転送負荷を抑える。
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

            // Seed由来の固定オフセットを加え、同じ音量でも色に自然な個体差を残す。
            var hash = Hash(_seeds[instanceIndex]);
            var random01 = (hash & 0x00ffffffu) / 16777215.0f;
            var spreadOffset = (random01 - 0.5f) * audio.perStickColorSpread;
            var colorLevel = Mathf.Clamp01(audioLevel + spreadOffset);
            return Color.Lerp(audio.lowLevelColor, audio.highLevelColor, colorLevel);
        }

        void SetEmissionIntensity(float audioLevel)
        {
            // Fixed/RandomPaletteでは基準値をそのまま使い、AudioReactiveだけ音量倍率を掛ける。
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
            // RenderMeshInstancedはRendererを持たないため、全体のカリングBoundsを手動指定する。
            var first = (Vector3)positions[0];
            _worldBounds = new Bounds(first, Vector3.zero);
            for (var i = 1; i < positions.Count; i++)
                _worldBounds.Encapsulate((Vector3)positions[i]);

            // 肩位置だけのBoundsでは振った先端がはみ出すため、腕長とMesh分の余白を加える。
            _worldBounds.Expand((_motion.armLength + 0.5f) * 2.0f);
        }

        void RenderInstances()
        {
            // 一般的なMeshRendererは生成せず、そのフレームの描画命令を直接発行する。
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

            // Quest/XRで安全に扱えるよう、最大511本ずつに分割して描画する。
            for (var start = 0; start < _instanceCount; start += _maxInstancesPerDraw)
            {
                var count = Mathf.Min(_maxInstancesPerDraw, _instanceCount - start);
                // シェーダー側のローカルInstance IDを、色Buffer全体の番号へ補正する。
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
            // Persistent NativeArrayはGC対象外なので、生成済みか確認して必ずDisposeする。
            if (_shoulderPositions.IsCreated) _shoulderPositions.Dispose();
            if (_baseRotations.IsCreated) _baseRotations.Dispose();
            if (_groupTimingOffsets.IsCreated) _groupTimingOffsets.Dispose();
            if (_seeds.IsCreated) _seeds.Dispose();
            if (_matrices.IsCreated) _matrices.Dispose();
            if (_colors.IsCreated) _colors.Dispose();

            // GPU Bufferも明示解放し、再構築やScene終了時のリークを防ぐ。
            _colorBuffer?.Dispose();
            _colorBuffer = null;
            _materialProperties = null;

            // Play中とEdit中ではUnity Objectの適切な破棄APIが異なる。
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
            // 側面ごとに下端・上端の2頂点を作り、最後の2頂点を上下の蓋中心に使う。
            var vertices = new Vector3[sideCount * 2 + 2];
            var triangles = new int[sideCount * 12];
            var halfHeight = height * 0.5f;

            // 円周上へ頂点を等間隔に配置する。
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

            // 各面について側面2三角形と、上下の蓋1三角形ずつを作る。
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
            // 頂点を後から変更しないため、GPU転送後はCPU側Meshデータを破棄する。
            mesh.UploadMeshData(true);
            return mesh;
        }

        static uint Hash(uint value)
        {
            // Seedの偏りを拡散し、色選択などで使える決定的な疑似乱数へ変換する。
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
