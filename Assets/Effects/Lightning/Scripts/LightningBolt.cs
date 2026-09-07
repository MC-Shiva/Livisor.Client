using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

/// <summary>
/// 雷の束・枝・着弾の放電・火花を再利用する1メッシュで描画。
/// Shader Graphが白い芯と青いハローを同一パスで合成し、QuestではBloom/Light/compute不要。
/// </summary>
[DisallowMultipleComponent]
public class LightningBolt : MonoBehaviour
{
    public enum Style { Bolt, Beam, Both }
    public enum Quality { Quest3, DesktopEnhanced }

    [Header("描画負荷")]
    [Tooltip("Quest3: 1メッシュ、追加ライトとBloomなし。DesktopEnhanced: VFX Graphの火花と環境光を追加。Androidは常にQuest3。")]
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

    // Fixed budgets, including ground discharge and sparks. No per-strike point-array allocation.
    private const int MaxPoints = 97, MaxPaths = 32, MaxSparks = 32, VertexBudget = 8192;
    private sealed class ArcPath
    {
        public readonly Vector3[] points = new Vector3[MaxPoints];
        public int count, kind; // 0 trunk, 1 filament, 2 ground, 3 impact splash
        public float start, end, width, energy;
    }
    private struct Spark { public Vector3 velocity; public float life, width; }
    private readonly ArcPath[] _paths = new ArcPath[MaxPaths];
    private readonly Spark[] _sparks = new Spark[MaxSparks];
    private readonly Vector3[] _spine = new Vector3[MaxPoints];
    private readonly List<Vector3> _vertices = new List<Vector3>(VertexBudget);
    private readonly List<Vector4> _uv = new List<Vector4>(VertexBudget);
    private readonly List<int> _indices = new List<int>(VertexBudget * 3);
    private Mesh _mesh;
    private MeshRenderer _renderer;
    private Material _material;
    private Camera _camera;
    private VisualEffect _impactVfx;
    private Light _impactLight;
    private Volume _volume;
    private VolumeProfile _profile;
    private Bloom _bloom;
    private int _pathCount, _trunkPathCount, _segments, _sparkCount, _shapeFrame;
    private uint _seed;
    private Vector3 _top, _ground, _right, _forward, _viewPosition;
    private Matrix4x4 _worldToLocal;
    private float _elapsed, _nextShape;
    private bool _playing, _impacted, _useQuestBudget, _vfxAllowed;
    private Style _playingStyle;

    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int ContrastId = Shader.PropertyToID("_ElectricContrast");
    private static readonly int SpeedId = Shader.PropertyToID("_ArcSpeed");
    private static readonly ProfilerMarker ShapeMarker = new ProfilerMarker("Lightning.BuildShape");
    private static readonly ProfilerMarker MeshMarker = new ProfilerMarker("Lightning.UpdateMesh");
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

    private void Awake() => EnsureBuilt();
    /// <summary>ロード時に呼ぶと任意のVFX Graphの生成も前倒しできる。</summary>
    public void Warmup() { EnsureBuilt(); PrepareExtras(); }

    public void Play(Vector3 top, Vector3 ground)
    {
        EnsureBuilt();
        if (_material == null) return;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        PrepareExtras();
        ResetExtras();
        _useQuestBudget = _quality == Quality.Quest3 || Application.platform == RuntimePlatform.Android;
        _playingStyle = _style;
        _top = top;
        _ground = ground;
        Vector3 axis = ground - top;
        Vector3 direction = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.down;
        _right = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        _forward = Vector3.Cross(direction, _right).normalized;
        _camera = Camera.main;
        _seed = (uint)Random.Range(1, 1000000);
        _segments = _useQuestBudget ? 48 : 72;
        _sparkCount = _useQuestBudget ? 20 : 32;
        _elapsed = 0f;
        _shapeFrame = 0;
        _nextShape = 0f;
        _impacted = false;
        _playing = true;
        _material.SetColor(ColorId, _coreColor * _intensity);
        _material.SetColor(EdgeColorId, _color * Mathf.Min(_intensity * 0.48f, 2.4f));
        _material.SetFloat(ContrastId, _electricContrast);
        _material.SetFloat(SpeedId, _arcSpeed);
        BuildTrunk();
        BuildImpact();
        _renderer.enabled = false;
    }

    private void LateUpdate()
    {
        if (!_playing) return;
        _elapsed += Time.deltaTime;
        float descent = Mathf.Max(0.01f, _descentDuration);
        float impactAge = _elapsed - descent;
        if (impactAge >= Mathf.Max(_trunkHold + _trunkErase, _groundDuration))
        {
            gameObject.SetActive(false);
            return;
        }
        if (impactAge >= 0f && !_impacted)
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
        if (_elapsed >= _nextShape && impactAge < _trunkHold + _trunkErase)
        {
            // Large bends stay in place; only fine cracks jump, at <=24 Hz on Quest.
            _shapeFrame++;
            using (ShapeMarker.Auto()) BuildTrunk();
            _nextShape = _elapsed + 1f / Mathf.Min(_arcSpeed, _useQuestBudget ? 24f : 40f);
        }
        using (MeshMarker.Auto()) RenderBolt(impactAge, descent);
        UpdateExtras(impactAge);
    }

    private void BuildTrunk()
    {
        Vector3 axis = _ground - _top;
        float spread = Mathf.Clamp(axis.magnitude / 12f, 0.15f, 1.5f);
        for (int i = 0; i <= _segments; i++)
        {
            float t = (float)i / _segments;
            float envelope = Mathf.Sin(t * Mathf.PI);
            float x = Noise(t * 9f, 1) * 0.5f + Noise(t * 23f, 2) * _angularity * 0.22f;
            float z = Noise(t * 7f, 3) * 0.28f + Noise(t * 19f, 4) * _angularity * 0.13f;
            float crack = Noise(t * 45f, 20 + _shapeFrame) * 0.055f;
            _spine[i] = _top + axis * t + (_right * (x + crack) + _forward * z) * (envelope * spread);
        }
        _pathCount = 0;
        if (_playingStyle != Style.Beam)
        {
            // The spine is only a guide: every visible pillar has its own course and width.
            // Reuse the former trunk + filament slots, keeping the same geometry budget.
            int pillars = _useQuestBudget ? 4 : 5;
            for (int s = 0; s < pillars; s++)
            {
                float thickness = _width * (0.44f + Hash(s, 101) * 0.16f);
                var path = AddPath(_segments + 1, 0, 1, thickness, 0.64f + Hash(s, 102) * 0.2f, 0);
                for (int i = 0; i <= _segments; i++)
                {
                    float t = (float)i / _segments;
                    // Shared narrow knots alternate with wider separations. Irregular phase
                    // and local cracks prevent the bundle from looking like a uniform helix.
                    float phase = s * Mathf.PI * 2f / pillars + t * (8f + _entanglement * 8f)
                        + Noise(t * 7f, 110 + s) * 0.8f;
                    float knots = 0.22f + Mathf.Abs(Noise(t * 5f, 103)) * 0.78f;
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    float radius = _bundleRadius * envelope * knots * spread;
                    Vector3 weave = _right * Fold(phase) + _forward * Fold(phase + 1.57f);
                    Vector3 loose = _right * Noise(t * 11f, 43 + s) + _forward * Noise(t * 9f, 53 + s);
                    Vector3 crack = _right * Noise(t * 29f, 130 + s + _shapeFrame * 5)
                        + _forward * Noise(t * 31f, 170 + s + _shapeFrame * 5);
                    path.points[i] = _spine[i] + Vector3.Lerp(loose, weave, _entanglement) * radius
                        + crack * (radius * _angularity * 0.12f);
                    if (i == 0) path.points[i] = _top;
                    if (i == _segments) path.points[i] = _ground;
                }
            }
            int branches = _useQuestBudget ? 6 : 9;
            for (int b = 0; b < branches; b++)
            {
                int first = Mathf.RoundToInt((0.12f + (b % 3) * 0.26f + (b / 3) * 0.025f) * _segments);
                int last = Mathf.Min(_segments, first + _segments / 5);
                var path = AddPath(last - first + 1, (float)first / _segments, (float)last / _segments,
                    _branchWidth * 2.7f, 0.55f, 1);
                float angle = b * 2.39996f;
                for (int j = first; j <= last; j++)
                {
                    float u = (float)(j - first) / (last - first);
                    float phase = angle + u * _entanglement * 5f;
                    float radius = Mathf.Sin(u * Mathf.PI) * (0.3f + Hash(b, 66) * 0.5f) * spread;
                    Vector3 offset = _right * Fold(phase) + _forward * Fold(phase + 1.57f);
                    Vector3 attachment = Vector3.Lerp(_paths[b % pillars].points[j],
                        _paths[(b + 1) % pillars].points[j], u);
                    path.points[j - first] = attachment + offset * radius
                        + _right * (Noise(u * 9f, b + _shapeFrame) * radius * 0.18f * _angularity);
                }
            }
        }
        if (_playingStyle != Style.Bolt)
        {
            var beam = AddPath(2, 0, 1, _width * 2.4f, 0.55f, 0);
            beam.points[0] = _top;
            beam.points[1] = _ground;
        }
        _trunkPathCount = _pathCount;
    }

    private void BuildImpact()
    {
        _pathCount = _trunkPathCount;
        for (int ray = 0; ray < 6; ray++)
        {
            float angle = ray * 2.39996f + Hash(ray, 71);
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            var path = AddPath(17, 0, 1, _impactArcWidth * 3f, 0.85f, 2);
            float length = _impactRadius * (0.65f + Hash(ray, 73) * 0.35f);
            for (int j = 0; j < path.count; j++)
            {
                float u = (float)j / (path.count - 1);
                path.points[j] = _ground + direction * (u * length) + Vector3.up * (0.035f + Mathf.Sin(u * Mathf.PI) * 0.07f)
                    + side * (Noise(u * 12f, ray + 74) * 0.3f * Mathf.Sin(u * Mathf.PI));
            }
        }
        // Uneven rising electric blades make a sharp impact silhouette, without a ground ring.
        for (int ray = 0; ray < 8; ray++)
        {
            float angle = ray * 2.39996f + 0.4f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var path = AddPath(7, 0, 1, 0.85f + Hash(ray, 81) * 0.85f, 1.3f, 3);
            float length = _impactRadius * (0.6f + Hash(ray, 82) * 0.35f);
            for (int j = 0; j < path.count; j++)
            {
                float u = (float)j / (path.count - 1);
                float height = u * u * (0.95f + Hash(ray, 83) * 1.65f)
                    + Mathf.Sin(u * Mathf.PI) * Mathf.Abs(Noise(u * 5f, ray + 86)) * 0.35f;
                path.points[j] = _ground + direction * (u * length) + Vector3.up * (0.07f + height)
                    + Vector3.Cross(Vector3.up, direction) * (Noise(u * 5, ray + 85) * u * 0.3f);
            }
        }
        for (int i = 0; i < _sparkCount; i++)
        {
            float angle = i * 2.39996f;
            float speed = _impactRadius * (0.8f + Hash(i, 91) * 1.8f);
            _sparks[i] = new Spark
            {
                velocity = new Vector3(Mathf.Cos(angle) * speed, 2f + Hash(i, 92) * 5f, Mathf.Sin(angle) * speed),
                life = _groundDuration * (0.45f + Hash(i, 93) * 0.55f),
                width = 0.055f + Hash(i, 94) * 0.055f,
            };
        }
    }

    private ArcPath AddPath(int count, float start, float end, float width, float energy, int kind)
    {
        var path = _paths[_pathCount++];
        path.count = count; path.start = start; path.end = end;
        path.width = width; path.energy = energy; path.kind = kind;
        return path;
    }

    private void RenderBolt(float age, float descent)
    {
        _vertices.Clear(); _uv.Clear(); _indices.Clear();
        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        // Center-eye facing: both XR eyes see the same world-space geometry.
        _viewPosition = _camera != null ? _camera.transform.position : _ground + Vector3.back * 15f + Vector3.up * 5f;
        _worldToLocal = transform.worldToLocalMatrix;
        float head = Mathf.Clamp01(_elapsed / descent);
        float tail = Mathf.Clamp01((age - _trunkHold) / Mathf.Max(0.01f, _trunkErase));
        float pulse = age < 0f ? 0.65f : 0.48f + 0.65f * Mathf.Exp(-age * 32f)
            + 0.55f * Mathf.Exp(-Mathf.Abs(age - 0.09f) * 90f)
            + 0.3f * Mathf.Exp(-Mathf.Abs(age - 0.17f) * 90f);
        for (int i = 0; i < _trunkPathCount; i++)
            AppendPath(_paths[i], tail, head, pulse * (1f - tail), 1f);
        if (age >= 0f && _impactRadius > 0f)
        {
            // Trunk regeneration only touches its own slots, keeping these impact paths stable.
            for (int i = _trunkPathCount; i < _trunkPathCount + 14; i++)
            {
                var path = _paths[i];
                bool splash = path.kind == 3;
                float life = splash ? Mathf.Min(0.36f, _groundDuration) : _groundDuration;
                float t = Mathf.Clamp01(age / Mathf.Max(0.01f, life));
                // Fast outward punch, a short hold, then the tips break away and fade.
                float headTime = splash ? 0.16f : 0.24f;
                float tailStart = splash ? 0.35f : 0.2f;
                AppendPath(path, Mathf.Clamp01((t - tailStart) / (1f - tailStart)), Mathf.Clamp01(t / headTime),
                    splash ? 1f - t * t : (1f - t) * (1f - t), 1f - t * 0.35f);
            }
            AppendSparks(age);
            AppendFlash(age);
        }
        _mesh.Clear(false);
        _mesh.SetVertices(_vertices);
        _mesh.SetUVs(0, _uv);
        _mesh.SetTriangles(_indices, 0, true);
        _renderer.enabled = _vertices.Count > 0;
    }

    private void AppendPath(ArcPath path, float tail, float head, float energy, float widthScale)
    {
        float firstT = Mathf.InverseLerp(path.start, path.end, tail);
        float lastT = Mathf.InverseLerp(path.start, path.end, head);
        if (lastT <= firstT || energy < 0.004f) return;
        int last = path.count - 1;
        float first = firstT * last, end = lastT * last;
        int firstIndex = Mathf.Min(Mathf.FloorToInt(first), last - 1);
        Vector3 point = Vector3.Lerp(path.points[firstIndex], path.points[firstIndex + 1], first - firstIndex);
        int startVertex = _vertices.Count;
        AppendPair(point, path.points[firstIndex + 1] - path.points[firstIndex], path, firstT, energy, widthScale);
        for (int j = firstIndex + 1; j < end; j++)
            AppendPair(path.points[j], path.points[Mathf.Min(j + 1, last)] - path.points[j - 1], path,
                (float)j / last, energy, widthScale);
        int endIndex = Mathf.Min(Mathf.FloorToInt(end), last - 1);
        point = Vector3.Lerp(path.points[endIndex], path.points[endIndex + 1], end - endIndex);
        AppendPair(point, path.points[endIndex + 1] - path.points[endIndex], path, lastT, energy, widthScale);
        for (int v = startVertex; v < _vertices.Count - 2; v += 2) AppendQuadIndices(v);
    }
    private void AppendPair(Vector3 point, Vector3 tangent, ArcPath path, float t, float energy, float widthScale)
    {
        Vector3 side = FacingSide(point, tangent);
        float taper = path.kind == 0 ? 0.85f + Mathf.Sin(t * Mathf.PI) * 0.15f : Mathf.Lerp(0.85f, 0.06f, t * t);
        if (path.kind == 3) taper = (0.3f + Mathf.Sin(t * Mathf.PI) * 0.9f) * (1f - t);
        float width = path.width * widthScale * taper * 0.5f;
        float along = Mathf.Lerp(path.start, path.end, t);
        // UV.z = local energy, UV.w = profile (trunk / filament / impact blade).
        float layer = path.kind == 0 ? 0f : (path.kind == 3 ? 2f : 1f);
        AddVertex(point - side * width, new Vector4(along, 0f, energy * path.energy, layer));
        AddVertex(point + side * width, new Vector4(along, 1f, energy * path.energy, layer));
    }
    private Vector3 FacingSide(Vector3 point, Vector3 tangent)
    {
        Vector3 side = Vector3.Cross(tangent, _viewPosition - point);
        if (side.sqrMagnitude < 0.000001f)
            side = Vector3.Cross(tangent, _camera != null ? _camera.transform.up : Vector3.up);
        return side.sqrMagnitude > 0.000001f ? side.normalized : Vector3.right;
    }
    private void AppendSparks(float age)
    {
        for (int i = 0; i < _sparkCount; i++)
        {
            var spark = _sparks[i];
            if (age >= spark.life) continue;
            Vector3 a = SparkPosition(spark.velocity, Mathf.Max(0f, age - 0.035f));
            Vector3 b = SparkPosition(spark.velocity, age);
            if (b.y <= _ground.y || (b - a).sqrMagnitude < 0.00001f) continue;
            AppendStreak(a, b, spark.width, (1f - age / spark.life) * 0.85f, 1f);
        }
    }
    private Vector3 SparkPosition(Vector3 velocity, float age) => _ground + Vector3.up * 0.08f
        + velocity * age + Vector3.down * (4.9f * age * age);
    private void AppendFlash(float age)
    {
        float fade = Mathf.Clamp01(1f - age / 0.13f);
        if (fade <= 0f) return;
        Vector3 center = _ground + Vector3.up * 0.22f;
        Vector3 right = _camera != null ? _camera.transform.right : Vector3.right;
        Vector3 up = _camera != null ? _camera.transform.up : Vector3.up;
        float size = Mathf.Min(_impactRadius * 0.34f, 1.65f) * (0.7f + fade * 0.3f);
        AppendStreak(center - right * size, center + right * size, size * 0.45f, fade * 1.8f, 2f);
        AppendStreak(center - up * size * 0.12f, center + up * size, size * 0.38f, fade * 1.5f, 2f);
        // Compact diagonal fragments enrich the impact silhouette without a screen-sized glow quad.
        AppendStreak(center, center + (right * 0.85f + up * 0.65f) * size, size * 0.32f, fade * 1.2f, 2f);
        AppendStreak(center, center + (-right * 0.7f + up * 0.9f) * size, size * 0.27f, fade, 2f);
    }
    private void AppendStreak(Vector3 a, Vector3 b, float width, float energy, float layer)
    {
        Vector3 side = FacingSide((a + b) * 0.5f, b - a) * (width * 0.5f);
        int v = _vertices.Count;
        AddVertex(a - side, new Vector4(0, 0, energy * 0.2f, layer));
        AddVertex(a + side, new Vector4(0, 1, energy * 0.2f, layer));
        AddVertex(b - side * 0.25f, new Vector4(1, 0, energy, layer));
        AddVertex(b + side * 0.25f, new Vector4(1, 1, energy, layer));
        AppendQuadIndices(v);
    }
    private void AddVertex(Vector3 point, Vector4 uv)
    {
        _vertices.Add(_worldToLocal.MultiplyPoint3x4(point)); _uv.Add(uv);
    }
    private void AppendQuadIndices(int v)
    {
        _indices.Add(v); _indices.Add(v + 1); _indices.Add(v + 2);
        _indices.Add(v + 2); _indices.Add(v + 1); _indices.Add(v + 3);
    }

    private float Hash(int cell, int salt)
    {
        unchecked
        {
            uint value = (uint)cell * 374761393u + (uint)salt * 668265263u + _seed;
            value = (value ^ (value >> 13)) * 1274126177u;
            return ((value ^ (value >> 16)) & 0x00ffffffu) / 16777215f;
        }
    }
    private float Noise(float t, int salt)
    {
        int cell = Mathf.FloorToInt(t);
        return Mathf.Lerp(Hash(cell, salt), Hash(cell + 1, salt), t - cell) * 2f - 1f;
    }
    private static float Fold(float angle)
    {
        const float step = Mathf.PI / 3f;
        float cell = Mathf.Floor(angle / step);
        return Mathf.Lerp(Mathf.Sin(cell * step), Mathf.Sin((cell + 1f) * step), angle / step - cell);
    }
    // Public utility retained for existing callers.
    public static void BuildPolyline(Vector3 from, Vector3 to, int segmentCount, float jaggedness, List<Vector3> result)
    {
        result.Clear();
        segmentCount = Mathf.Max(1, segmentCount);
        Vector3 axis = to - from;
        Vector3 direction = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.down;
        Vector3 right = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        Vector3 forward = Vector3.Cross(direction, right);
        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            Vector3 offset = i == 0 || i == segmentCount ? Vector3.zero
                : (right * Random.Range(-1f, 1f) + forward * Random.Range(-1f, 1f)) * (jaggedness * Mathf.Sin(t * Mathf.PI));
            result.Add(from + axis * t + offset);
        }
    }

    private void EnsureBuilt()
    {
        if (_mesh != null) return;
        for (int i = 0; i < MaxPaths; i++) _paths[i] = new ArcPath();
        var shader = Resources.Load<Shader>("Lightning/SG_ElectricArc");
        if (shader == null)
        {
            Debug.LogError("[Lightning] Resources/Lightning/SG_ElectricArc が見つかりません。", this);
            return;
        }
        _material = new Material(shader) { name = "Lightning Combined (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        _material.SetFloat("_Layer", 3f);
        _mesh = new Mesh { name = "Lightning Combined (Pooled)", hideFlags = HideFlags.HideAndDontSave };
        _mesh.MarkDynamic();
        var go = new GameObject("Lightning Mesh");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = go.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _renderer.enabled = false;
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
    private void ResetExtras()
    {
        if (_impactVfx != null)
        {
            _impactVfx.Stop();
            _impactVfx.gameObject.SetActive(false);
        }
        if (_impactLight != null) _impactLight.enabled = false;
        if (_volume != null) _volume.weight = 0;
    }
    private void OnDisable()
    {
        _playing = false;
        if (_renderer != null) _renderer.enabled = false;
        ResetExtras();
    }
    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
        if (_bloom != null) Destroy(_bloom);
        if (_profile != null) Destroy(_profile);
    }
}
