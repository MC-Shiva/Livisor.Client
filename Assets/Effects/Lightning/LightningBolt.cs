using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 雷 1 本ぶんの見た目と再生を受け持つ。
/// 子オブジェクト（稲妻の LineRenderer・光の柱・閃光の Light）とマテリアルは
/// すべてコードから生成する。Unity エディタでのマテリアル・Prefab 作成を不要にするため。
/// 発火は <see cref="LightningStrikeController"/> から行う。
/// </summary>
[DisallowMultipleComponent]
public class LightningBolt : MonoBehaviour
{
    /// <summary>雷の見た目。Bolt=枝分かれする稲妻 / Beam=光の柱 / Both=両方。</summary>
    public enum Style
    {
        Bolt,
        Beam,
        Both,
    }

    [Header("見た目")]
    [SerializeField] private Style _style = Style.Both;
    [SerializeField] private Color _color = new Color(0.62f, 0.78f, 1f, 1f);
    [SerializeField] private float _intensity = 8f;

    [Header("稲妻")]
    [SerializeField] private int _segmentCount = 24;
    [SerializeField] private float _jaggedness = 0.6f;
    [SerializeField] private float _width = 0.18f;
    [SerializeField] private int _maxBranchCount = 4;
    [SerializeField, Range(0.1f, 1f)] private float _branchLengthRatio = 0.45f;

    [Header("光の柱")]
    [SerializeField] private float _beamWidth = 1.2f;
    [SerializeField] private float _beamIntensityRatio = 0.35f;

    [Header("再生")]
    [SerializeField] private float _lifeTime = 0.5f;
    [SerializeField] private int _flickerCount = 4;

    [SerializeField]
    private AnimationCurve _intensityCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.2f, 0.85f),
        new Keyframe(1f, 0f));

    [Header("閃光")]
    [SerializeField] private float _flashRange = 20f;
    [SerializeField] private float _flashIntensity = 40f;

    private bool _built;
    private bool _playing;
    private Coroutine _routine;

    private LineRenderer _trunk;
    private LineRenderer[] _branches;
    private Transform _beamRoot;
    private Transform[] _beamQuads;
    private Light _flash;
    private Material _boltMaterial;
    private Material _beamMaterial;
    private Texture2D _boltTexture;
    private Texture2D _beamTexture;

    // 点列生成の作業用バッファ。毎フレーム作り直すため使い回して GC を避ける。
    private readonly List<Vector3> _points = new List<Vector3>();

    /// <summary>再生中なら true。プールの空き判定に使う。</summary>
    public bool IsPlaying => _playing;

    /// <summary>見た目の切り替え。Inspector と同じ値を実行時にも変えられる。</summary>
    public Style CurrentStyle
    {
        get => _style;
        set => _style = value;
    }

    private void Awake() => EnsureBuilt();

    /// <summary>上端 <paramref name="top"/> から着弾点 <paramref name="ground"/> まで雷を 1 回落とす。</summary>
    public void Play(Vector3 top, Vector3 ground)
    {
        // StartCoroutine は非アクティブな GameObject では失敗するため、先に有効化する。
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        EnsureBuilt();

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(PlayRoutine(top, ground));
    }

    private IEnumerator PlayRoutine(Vector3 top, Vector3 ground)
    {
        _playing = true;

        // Play 中に Inspector で数値を変えても次の 1 発から反映されるよう、毎回設定を流し込む。
        ApplySettings();

        bool showBolt = _style != Style.Beam;
        bool showBeam = _style != Style.Bolt;

        _trunk.enabled = showBolt;
        _beamRoot.gameObject.SetActive(showBeam);

        if (showBeam) LayoutBeam(top, ground);

        if (showBolt)
        {
            Reshape(top, ground);
        }
        else
        {
            // 前回の枝が残ったまま見えてしまうため、稲妻を出さないときは明示的に消す。
            foreach (var branch in _branches)
                branch.enabled = false;
        }

        _flash.transform.position = Vector3.Lerp(ground, top, 0.1f);
        _flash.enabled = true;

        float interval = _flickerCount > 0 ? _lifeTime / _flickerCount : _lifeTime;
        float nextReshape = interval;
        float flicker = 1f;
        float elapsed = 0f;

        while (elapsed < _lifeTime)
        {
            if (showBolt && elapsed >= nextReshape)
            {
                Reshape(top, ground);
                flicker = Random.Range(0.55f, 1f);
                nextReshape += interval;
            }

            ApplyIntensity(_intensityCurve.Evaluate(elapsed / _lifeTime) * flicker);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyIntensity(0f);
        _flash.enabled = false;
        _playing = false;
        _routine = null;
        gameObject.SetActive(false);
    }

    /// <summary>Inspector の値を各コンポーネントへ反映する。発火のたびに呼ぶ。</summary>
    private void ApplySettings()
    {
        SetWidth(_trunk, 1f);
        foreach (var branch in _branches)
            SetWidth(branch, 0.5f);

        _flash.color = _color;
        _flash.range = _flashRange;
    }

    private void SetWidth(LineRenderer line, float scale)
    {
        line.widthCurve = new AnimationCurve(
            new Keyframe(0f, _width * scale),
            new Keyframe(1f, _width * scale * 0.35f));
    }

    /// <summary>幹と枝の形を作り直す。明滅のたびに呼び、稲妻の揺らぎを出す。</summary>
    private void Reshape(Vector3 top, Vector3 ground)
    {
        BuildPolyline(top, ground, _segmentCount, _jaggedness, _points);
        _trunk.positionCount = _points.Count;
        _trunk.SetPositions(_points.ToArray());

        float trunkLength = Vector3.Distance(top, ground);

        for (int i = 0; i < _branches.Length; i++)
        {
            var branch = _branches[i];

            // 枝は毎回ランダムに出す。半分程度は出さないことで枝の本数もばらつかせる。
            if (_points.Count < 4 || Random.value < 0.35f)
            {
                branch.enabled = false;
                continue;
            }

            int from = Random.Range(_points.Count / 5, _points.Count - 2);
            Vector3 start = _points[from];
            Vector3 dir = (ground - top).normalized;
            Vector3 spread = Random.onUnitSphere;
            Vector3 branchDir = (dir + spread * 0.8f).normalized;
            float length = trunkLength * _branchLengthRatio * Random.Range(0.4f, 1f);

            BuildPolyline(start, start + branchDir * length, 6, _jaggedness * 0.5f, _points2);
            branch.positionCount = _points2.Count;
            branch.SetPositions(_points2.ToArray());
            branch.enabled = true;
        }
    }

    private readonly List<Vector3> _points2 = new List<Vector3>();

    /// <summary>光の柱を上端から着弾点に合わせて配置する。</summary>
    private void LayoutBeam(Vector3 top, Vector3 ground)
    {
        Vector3 axis = top - ground;
        float length = axis.magnitude;
        if (length < 0.001f) return;

        _beamRoot.SetPositionAndRotation(
            (top + ground) * 0.5f,
            Quaternion.FromToRotation(Vector3.up, axis / length));

        // 親を非一様スケールしたまま子を回すと幅が崩れるため、スケールは各 Quad に入れる。
        foreach (var quad in _beamQuads)
            quad.localScale = new Vector3(_beamWidth, length, 1f);
    }

    private void ApplyIntensity(float t)
    {
        t = Mathf.Max(t, 0f);
        _boltMaterial.SetColor(BaseColorId, _color * (_intensity * t));
        _beamMaterial.SetColor(BaseColorId, _color * (_intensity * _beamIntensityRatio * t));
        _flash.intensity = _flashIntensity * t;
    }

    /// <summary>
    /// 始点から終点まで、横方向にランダムなずれを入れた折れ線を作る。
    /// 両端はずらさないため、上端と着弾点は必ず指定どおりになる。
    /// </summary>
    public static void BuildPolyline(Vector3 from, Vector3 to, int segmentCount, float jaggedness, List<Vector3> result)
    {
        result.Clear();
        segmentCount = Mathf.Max(segmentCount, 1);

        Vector3 axis = to - from;
        Vector3 dir = axis.normalized;
        Vector3 right = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        Vector3 forward = Vector3.Cross(dir, right).normalized;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            Vector3 point = from + axis * t;

            if (i > 0 && i < segmentCount)
            {
                // 端に近いほどずれを小さくして、始点と終点へ滑らかにつなぐ。
                float taper = Mathf.Sin(t * Mathf.PI);
                point += (right * Random.Range(-1f, 1f) + forward * Random.Range(-1f, 1f)) * (jaggedness * taper);
            }

            result.Add(point);
        }
    }

    // === 生成まわり ===

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _boltTexture = CreateSoftTexture(4, 64, false);
        _beamTexture = CreateSoftTexture(64, 64, true);
        _boltMaterial = CreateAdditiveMaterial(_boltTexture);
        _beamMaterial = CreateAdditiveMaterial(_beamTexture);

        _trunk = CreateLine("Trunk", _boltMaterial, 1f);

        _branches = new LineRenderer[Mathf.Max(_maxBranchCount, 0)];
        for (int i = 0; i < _branches.Length; i++)
        {
            _branches[i] = CreateLine($"Branch {i}", _boltMaterial, 0.5f);
            _branches[i].enabled = false;
        }

        _beamRoot = new GameObject("Beam").transform;
        _beamRoot.SetParent(transform, false);
        _beamQuads = new[]
        {
            CreateBeamQuad("Beam Quad X", Quaternion.identity),
            CreateBeamQuad("Beam Quad Z", Quaternion.Euler(0f, 90f, 0f)),
        };

        var flashObject = new GameObject("Flash");
        flashObject.transform.SetParent(transform, false);
        _flash = flashObject.AddComponent<Light>();
        _flash.type = LightType.Point;
        _flash.color = _color;
        _flash.range = _flashRange;
        _flash.intensity = 0f;
        _flash.shadows = LightShadows.None;
        _flash.enabled = false;
    }

    private LineRenderer CreateLine(string name, Material material, float widthScale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 0;
        line.numCornerVertices = 2;
        line.sharedMaterial = material;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        SetWidth(line, widthScale);
        return line;
    }

    private Transform CreateBeamQuad(string name, Quaternion localRotation)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());

        quad.transform.SetParent(_beamRoot, false);
        quad.transform.localRotation = localRotation;

        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _beamMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return quad.transform;
    }

    /// <summary>URP Unlit を加算合成に設定したマテリアルを作る。HDR 色を入れると Bloom が拾う。</summary>
    private static Material CreateAdditiveMaterial(Texture2D texture)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        material.SetFloat("_Surface", 1f); // Transparent
        material.SetFloat("_Blend", 1f);   // Additive
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetTexture(BaseMapId, texture);
        return material;
    }

    /// <summary>
    /// 中心が明るく縁が減衰するテクスチャを作る。
    /// <paramref name="horizontal"/> が true なら横方向（光の柱用）、false なら縦方向（LineRenderer の幅方向）に減衰する。
    /// </summary>
    private static Texture2D CreateSoftTexture(int width, int height, bool horizontal)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int across = horizontal ? x : y;
                int acrossSize = horizontal ? width : height;
                float d = Mathf.Abs((across + 0.5f) / acrossSize * 2f - 1f);

                // 広いにじみ（グロー）と細い芯を重ねて、光の線らしい断面にする。
                float value = Mathf.Clamp01(Mathf.Pow(1f - d, 2f) * 0.55f + Mathf.Pow(1f - d, 12f) * 0.9f);

                // 光の柱は上端をわずかに薄くして、空へ溶けるようにする。
                if (horizontal)
                {
                    float alongTop = (y + 0.5f) / height;
                    value *= Mathf.Lerp(1f, 0.35f, alongTop);
                }

                pixels[y * width + x] = new Color(value, value, value, value);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void OnDestroy()
    {
        if (_boltMaterial != null) Destroy(_boltMaterial);
        if (_beamMaterial != null) Destroy(_beamMaterial);
        if (_boltTexture != null) Destroy(_boltTexture);
        if (_beamTexture != null) Destroy(_beamTexture);
    }
}
