using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Livisor.MRDive
{
    /// <summary>オーバーレイが頭をどう追うか。</summary>
    public enum OverlayFollow
    {
        /// <summary>頭の位置だけ追う。回転は追わないので模様が視線に貼り付かない。全天球レイヤー向け。</summary>
        PositionOnly,

        /// <summary>頭の位置と回転を追う。常に正面に居てほしい板向け。</summary>
        PositionAndRotation,

        /// <summary>生成時の場所に置き去りにする。世界に固定したいオブジェクト向け。</summary>
        Detached,
    }

    /// <summary>
    /// <see cref="DiveOverlay"/> が作った 1 枚のレイヤー。
    /// マテリアルはレイヤー専用の実体なので、演出中に好きなだけ書き換えてよい。
    /// </summary>
    public sealed class OverlayLayer
    {
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public MeshRenderer Renderer { get; }
        public Material Material { get; }

        internal OverlayLayer(GameObject go, MeshRenderer renderer, Material material)
        {
            GameObject = go;
            Transform = go.transform;
            Renderer = renderer;
            Material = material;
        }

        public bool Visible
        {
            get => Renderer != null && Renderer.enabled;
            set { if (Renderer != null) Renderer.enabled = value; }
        }

        /// <summary>シェーダー共通の _Alpha。持っていないシェーダーでは無視される。</summary>
        public float Alpha
        {
            get => Material != null && Material.HasProperty(AlphaId) ? Material.GetFloat(AlphaId) : 1f;
            set { if (Material != null && Material.HasProperty(AlphaId)) Material.SetFloat(AlphaId, value); }
        }

        public OverlayLayer Set(int id, float value)
        {
            if (Material != null) Material.SetFloat(id, value);
            return this;
        }

        public OverlayLayer Set(int id, Color value)
        {
            if (Material != null) Material.SetColor(id, value);
            return this;
        }

        public OverlayLayer Set(int id, Vector4 value)
        {
            if (Material != null) Material.SetVector(id, value);
            return this;
        }

        public OverlayLayer Set(string name, float value) => Set(Shader.PropertyToID(name), value);
        public OverlayLayer Set(string name, Color value) => Set(Shader.PropertyToID(name), value);
        public OverlayLayer Set(string name, Vector4 value) => Set(Shader.PropertyToID(name), value);

        public OverlayLayer SetTexture(string name, Texture value)
        {
            if (Material != null) Material.SetTexture(name, value);
            return this;
        }

        public OverlayLayer SetKeyword(string keyword, bool on)
        {
            if (Material == null) return this;
            if (on) Material.EnableKeyword(keyword);
            else Material.DisableKeyword(keyword);
            return this;
        }
    }

    /// <summary>
    /// 視界を覆うレイヤーの置き場。
    ///
    /// VR でフルスクリーン効果をやる方法として、ポストエフェクト (OnRenderImage) ではなく
    /// 「頭を包むジオメトリ」を採用している。Single Pass Instanced でも確実に両目に出て、
    /// レンズ歪みとも喧嘩せず、Built-in RP のまま動くため。
    ///
    /// 全天球レイヤーの既定半径を 5m と大きく取っているのは、目の間隔 (~6cm) に対して
    /// 十分遠く、左右の視差がほぼ消えるから。近い板は VR では目が疲れる。
    /// 深度は ZTest Always 前提なので、遠くに置いても必ず最前面に描かれる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class DiveOverlay : MonoBehaviour
    {
        /// <summary>不透明レイヤーの標準キュー。手前に重ねたいものはこれに加算していく。</summary>
        public const int BaseQueue = 4000;

        readonly List<OverlayLayer> _layers = new List<OverlayLayer>();
        readonly List<Transform> _anchors = new List<Transform>();

        Transform _head;
        Transform _positionRoot;   // 位置だけ追う
        Transform _lockedRoot;     // 位置と回転を追う（head の子）
        Transform _detachedRoot;   // 置き去り

        public Transform Head => _head;
        public Camera EyeCamera { get; private set; }

        /// <summary>頭の Transform にオーバーレイ空間を用意する。</summary>
        public static DiveOverlay Attach(Transform head, Camera eyeCamera)
        {
            var go = new GameObject("MRDive Overlay");
            var overlay = go.AddComponent<DiveOverlay>();
            overlay._head = head;
            overlay.EyeCamera = eyeCamera;

            overlay._positionRoot = new GameObject("Position Locked").transform;
            overlay._positionRoot.SetParent(go.transform, false);

            overlay._detachedRoot = new GameObject("Detached").transform;
            overlay._detachedRoot.SetParent(go.transform, false);

            // 回転まで追うものだけは親子付けで済ませる。毎フレームの代入を減らせる。
            overlay._lockedRoot = new GameObject("Head Locked").transform;
            overlay._lockedRoot.SetParent(head, false);
            overlay._lockedRoot.localPosition = Vector3.zero;
            overlay._lockedRoot.localRotation = Quaternion.identity;

            overlay.SyncToHead();
            return overlay;
        }

        void LateUpdate()
        {
            SyncToHead();
        }

        void SyncToHead()
        {
            if (_head == null || _positionRoot == null) return;
            _positionRoot.position = _head.position;
        }

        /// <summary>頭を包む全天球レイヤーを 1 枚追加する。</summary>
        public OverlayLayer AddSphere(
            Shader shader,
            int renderQueue = BaseQueue,
            float radius = 5f,
            OverlayFollow follow = OverlayFollow.PositionOnly)
        {
            var layer = CreateLayer(PrimitiveType.Sphere, "Sphere Layer", shader, renderQueue, follow);
            if (layer == null) return null;

            layer.Transform.localPosition = Vector3.zero;
            layer.Transform.localRotation = Quaternion.identity;
            layer.Transform.localScale = Vector3.one * (radius * 2f);

            // 球は内側から見るので前面を捨てる。
            SetCull(layer.Material, CullMode.Front);
            return layer;
        }

        /// <summary>正面に板を 1 枚追加する。coverage は視野に対する余裕（1 で視野ぴったり）。</summary>
        public OverlayLayer AddQuad(
            Shader shader,
            int renderQueue = BaseQueue,
            float distance = 1.5f,
            float coverage = 1.3f,
            OverlayFollow follow = OverlayFollow.PositionAndRotation)
        {
            var layer = CreateLayer(PrimitiveType.Quad, "Quad Layer", shader, renderQueue, follow);
            if (layer == null) return null;

            layer.Transform.localPosition = new Vector3(0f, 0f, distance);
            layer.Transform.localRotation = Quaternion.identity;

            float size = ViewSizeAt(distance) * Mathf.Max(0.01f, coverage);
            layer.Transform.localScale = new Vector3(size, size, 1f);
            SetCull(layer.Material, CullMode.Off);
            return layer;
        }

        /// <summary>パーティクルなど任意のオブジェクトをぶら下げるための空の親を作る。</summary>
        public Transform CreateAnchor(string anchorName, OverlayFollow follow, float forwardDistance = 0f)
        {
            var go = new GameObject(string.IsNullOrEmpty(anchorName) ? "Anchor" : anchorName);
            go.transform.SetParent(RootFor(follow), false);
            go.transform.localPosition = new Vector3(0f, 0f, forwardDistance);
            go.transform.localRotation = Quaternion.identity;

            if (follow == OverlayFollow.Detached && _head != null)
            {
                go.transform.position = _head.position + _head.forward * forwardDistance;
                go.transform.rotation = Quaternion.LookRotation(_head.forward, Vector3.up);
            }

            _anchors.Add(go.transform);
            return go.transform;
        }

        /// <summary>距離 distance の位置で視野を覆うのに必要な一辺の長さ。</summary>
        public float ViewSizeAt(float distance)
        {
            // XR ではステレオ投影行列が実行時に差し替わるため、Camera.fieldOfView は
            // プレハブのシリアライズ値（多くの場合 60 度）のままで実効 FOV と一致しない。
            // それを信じると板が視野を覆いきらず、四隅に背景が残る。投影行列から直接取る。
            float tanHalfV = 0f;
            float tanHalfH = 0f;

            if (EyeCamera != null)
            {
                Matrix4x4 proj = EyeCamera.stereoEnabled
                    ? EyeCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
                    : EyeCamera.projectionMatrix;

                // 透視投影では m00 = 1/tan(fovX/2)、m11 = 1/tan(fovY/2)。
                float m00 = Mathf.Abs(proj.m00);
                float m11 = Mathf.Abs(proj.m11);
                if (m00 > 1e-4f && m11 > 1e-4f)
                {
                    tanHalfH = 1f / m00;
                    tanHalfV = 1f / m11;
                }
            }

            if (tanHalfV <= 0f || tanHalfH <= 0f)
            {
                // 取れなければ広め（片側 50 度＝水平 100 度相当）を仮定して、覆い損ねを避ける。
                tanHalfH = Mathf.Tan(50f * Mathf.Deg2Rad);
                tanHalfV = tanHalfH;
            }

            return 2f * distance * Mathf.Max(tanHalfV, tanHalfH);
        }

        public void Remove(OverlayLayer layer)
        {
            if (layer == null) return;
            _layers.Remove(layer);
            DestroyLayer(layer);
        }

        /// <summary>作ったレイヤーとアンカーを全部片付ける。</summary>
        public void Clear()
        {
            for (int i = 0; i < _layers.Count; i++) DestroyLayer(_layers[i]);
            _layers.Clear();

            for (int i = 0; i < _anchors.Count; i++)
            {
                if (_anchors[i] != null) Destroy(_anchors[i].gameObject);
            }
            _anchors.Clear();
        }

        void OnDestroy()
        {
            Clear();

            // _lockedRoot だけは head（カメラ）の子に付けているので、この GameObject を
            // 消しても道連れにならない。明示的に片付ける。
            if (_lockedRoot != null) Destroy(_lockedRoot.gameObject);
        }

        // ------------------------------------------------------------------

        OverlayLayer CreateLayer(PrimitiveType primitive, string label, Shader shader, int renderQueue, OverlayFollow follow)
        {
            if (shader == null)
            {
                Debug.LogError($"[MRDive] {label} のシェーダーが null。MRDiveShaders のパスを確認してください。", this);
                return null;
            }

            var go = GameObject.CreatePrimitive(primitive);
            go.name = $"{label} ({shader.name})";

            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            go.transform.SetParent(RootFor(follow), false);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;

            var material = new Material(shader) { renderQueue = renderQueue };
            renderer.sharedMaterial = material;

            var layer = new OverlayLayer(go, renderer, material);
            _layers.Add(layer);
            return layer;
        }

        Transform RootFor(OverlayFollow follow)
        {
            switch (follow)
            {
                case OverlayFollow.PositionAndRotation: return _lockedRoot;
                case OverlayFollow.Detached: return _detachedRoot;
                default: return _positionRoot;
            }
        }

        static void SetCull(Material material, CullMode mode)
        {
            if (material == null) return;
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)mode);
        }

        static void DestroyLayer(OverlayLayer layer)
        {
            if (layer == null) return;
            if (layer.Material != null) Destroy(layer.Material);
            if (layer.GameObject != null) Destroy(layer.GameObject);
        }
    }
}
