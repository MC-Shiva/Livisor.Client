using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>着弾の放電・飛沫・火花を1枚の再利用メッシュで描く。</summary>
[DisallowMultipleComponent]
internal sealed class LightningImpactMesh : MonoBehaviour
{
    private const int MaxPoints = 17, MaxPaths = 14, MaxSparks = 32, VertexBudget = 1024;
    private sealed class ArcPath
    {
        public readonly Vector3[] points = new Vector3[MaxPoints];
        public int count, kind;
        public float start, end, width, energy;
    }
    private struct Spark { public Vector3 velocity; public float life, width; }
    private readonly ArcPath[] _paths = new ArcPath[MaxPaths];
    private readonly Spark[] _sparks = new Spark[MaxSparks];
    private readonly List<Vector3> _vertices = new List<Vector3>(VertexBudget);
    private readonly List<Vector4> _uv = new List<Vector4>(VertexBudget);
    private readonly List<int> _indices = new List<int>(VertexBudget * 3);
    private Mesh _mesh;
    private MeshRenderer _renderer;
    private Material _material;
    private Camera _camera;
    private Matrix4x4 _worldToLocal;
    private Vector3 _ground, _viewPosition;
    private float _impactRadius, _impactArcWidth, _groundDuration;
    private int _pathCount, _sparkCount;
    private uint _seed;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int ContrastId = Shader.PropertyToID("_ElectricContrast");
    private static readonly int SpeedId = Shader.PropertyToID("_ArcSpeed");

    public void Warmup() => EnsureBuilt();
    public void Begin(Vector3 ground, float radius, float arcWidth, float duration, bool quest,
        uint seed, Color core, Color edge, float contrast, float speed)
    {
        EnsureBuilt();
        _ground = ground; _impactRadius = radius; _impactArcWidth = arcWidth;
        _groundDuration = duration; _seed = seed; _sparkCount = quest ? 20 : 32;
        _camera = Camera.main;
        _material.SetColor(ColorId, core); _material.SetColor(EdgeColorId, edge);
        _material.SetFloat(ContrastId, contrast); _material.SetFloat(SpeedId, speed);
        BuildImpact();
        _renderer.enabled = false;
    }
    private void BuildImpact()
    {
        _pathCount = 0;
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

    public void Render(float age)
    {
        _vertices.Clear(); _uv.Clear(); _indices.Clear();
        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        // Center-eye facing: both XR eyes see the same world-space geometry.
        _viewPosition = _camera != null ? _camera.transform.position : _ground + Vector3.back * 15f + Vector3.up * 5f;
        _worldToLocal = transform.worldToLocalMatrix;
        if (age >= 0f && _impactRadius > 0f)
        {
            for (int i = 0; i < _pathCount; i++)
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
        _material = new Material(shader) { name = "Lightning Impact (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        _material.SetFloat("_Layer", 3f);
        _mesh = new Mesh { name = "Lightning Impact (Pooled)", hideFlags = HideFlags.HideAndDontSave };
        _mesh.MarkDynamic();
        var go = new GameObject("Lightning Impact Mesh");
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
    private void OnDisable() { if (_renderer != null) _renderer.enabled = false; }
    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_material != null) Destroy(_material);
    }
}
