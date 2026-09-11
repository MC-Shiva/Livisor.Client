using UnityEngine;
using UnityEngine.UI;

namespace Livisor.MRDive
{
    /// <summary>
    /// 視線（頭の向き）だけでボタンを押せるようにするポインタ。
    ///
    /// コントローラが無くても、電池が切れても、片手が塞がっていても操作できる状態を保つのが目的。
    /// 頭から <see cref="Physics.Raycast"/> を飛ばし、当たった <see cref="Button"/> を一定時間
    /// 見続けたら <see cref="Button.onClick"/> を直接 Invoke する。EventSystem は使わない。
    ///
    /// VR ならではの調整:
    ///   - 視線は常に細かく揺れるので、外れてもすぐには dwell をリセットせず猶予を持たせる
    ///   - レティクルは頭の子にしたワールド空間の板。Screen Space Overlay は VR では描かれない
    ///   - トリガー / A / X を押したら dwell を待たずに即発火（待たされるのは待つ人だけでいい）
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Livisor/MR Dive/Dive Gaze Pointer")]
    public sealed class DiveGazePointer : MonoBehaviour
    {
        /// <summary>レティクル Canvas の論理サイズ。実寸は localScale で決める。</summary>
        const float ReticleUnits = 100f;

        [Header("参照")]
        [Tooltip("未設定なら Camera.main の Transform を使う。")]
        [SerializeField] Transform head;

        [Tooltip("未設定ならシーンから自動で探す。ハイライトの通知先。")]
        [SerializeField] DiveLaunchPanel panel;

        [Tooltip("未設定ならシーンから自動で探す。ダイブ中にレティクルを隠すためだけに使う。")]
        [SerializeField] DiveDirector director;

        [Header("注視")]
        [Tooltip("見つめ続けて発火するまでの秒数。")]
        [SerializeField] float dwellSeconds = 1.6f;

        [Tooltip("視線が外れてから dwell をリセットするまでの猶予。VR では視線が細かく揺れるため。")]
        [SerializeField] float graceSeconds = 0.2f;

        [Tooltip("発火してから次を受け付けるまでの待ち時間。連続誤爆の防止。")]
        [SerializeField] float refireCooldown = 0.6f;

        [Tooltip("トリガー / A / X で dwell を待たずに即発火する。")]
        [SerializeField] bool allowInstantSelect = true;

        [Header("レイ")]
        [Tooltip("レイの最大距離（m）。")]
        [SerializeField] float maxDistance = 6f;

        [Tooltip("UI 以外に当たらないようにする。既定は組み込みの UI レイヤー(5) のみ。")]
        [SerializeField] LayerMask hitMask = 1 << 5;

        [Header("レティクル")]
        [SerializeField] bool showReticle = true;

        [Tooltip("頭からレティクルまでの距離（m）。近すぎると目が疲れる。")]
        [SerializeField] float reticleDistance = 1.5f;

        [Tooltip("レティクルの直径（m）。1.5m 先で 0.05m ≒ 視角 1.9 度。")]
        [SerializeField] float reticleSize = 0.05f;

        [SerializeField] Color reticleIdleColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] Color reticleHotColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] Color reticleProgressColor = new Color(0.45f, 0.85f, 1f, 1f);

        Button _target;
        float _dwell;
        float _lost;
        float _cooldown;
        bool _fired;

        GameObject _reticleGo;
        Image _reticleRing;
        Image _reticleProgress;
        Image _reticleDot;
        RectTransform _reticleDotRect;
        Texture2D _ringTexture;
        Texture2D _dotTexture;
        Sprite _ringSprite;
        Sprite _dotSprite;

        /// <summary>いま注視しているボタン。誰も見ていなければ null。</summary>
        public Button CurrentTarget => _target;

        /// <summary>注視の進捗 0..1。</summary>
        public float Dwell01 =>
            dwellSeconds > 0.0001f ? Mathf.Clamp01(_dwell / dwellSeconds) : (_target != null ? 1f : 0f);

        void Awake()
        {
            if (head == null) ResolveHead();
            if (panel == null) panel = FindFirstObjectByType<DiveLaunchPanel>();
            if (director == null) director = FindFirstObjectByType<DiveDirector>();
        }

        void OnEnable()
        {
            if (director == null) return;
            director.DiveStarted += HandleDiveStarted;
            director.DiveCancelledOrFinished += HandleDiveFinished;
        }

        void OnDisable()
        {
            if (director != null)
            {
                director.DiveStarted -= HandleDiveStarted;
                director.DiveCancelledOrFinished -= HandleDiveFinished;
            }

            ClearTarget();
            if (panel != null) panel.SetHighlight(null, 0f);
        }

        void Start()
        {
            if (showReticle) BuildReticle();
        }

        void OnDestroy()
        {
            if (_reticleGo != null) Destroy(_reticleGo);
            DestroyReticleAssets();
        }

        /// <summary>実行時に作った Sprite と Texture2D を捨てる。これらは GC 対象外なので明示的に。</summary>
        void DestroyReticleAssets()
        {
            if (_ringSprite != null) Destroy(_ringSprite);
            if (_dotSprite != null) Destroy(_dotSprite);
            if (_ringTexture != null) Destroy(_ringTexture);
            if (_dotTexture != null) Destroy(_dotTexture);

            _ringSprite = null;
            _dotSprite = null;
            _ringTexture = null;
            _dotTexture = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_cooldown > 0f) _cooldown -= dt;

            if (head == null)
            {
                ResolveHead();
                if (head == null) return;
            }

            // カメラが後から出来るシーン構成でもレティクルが出るように、ここで作り直す。
            if (showReticle && _reticleGo == null) BuildReticle();

            // 押下のエッジは毎フレーム必ず取る。注視中だけ見に行くと、
            // 的が無いときの押下が次フレームまで持ち越されて誤爆する。
            bool selectEdge = allowInstantSelect && XRDiveInput.SelectPressedThisFrame();

            Button found = Probe();

            if (found != null)
            {
                if (found != _target)
                {
                    _target = found;
                    _dwell = 0f;
                    _fired = false;
                }

                _lost = 0f;
                if (!_fired && _cooldown <= 0f) _dwell += dt;
            }
            else if (_target != null)
            {
                // 完全に外したと決めつけず、少しだけ待つ。dwell は進めず凍らせておく。
                _lost += dt;
                if (_lost >= graceSeconds) ClearTarget();
            }
            else
            {
                ClearTarget();
            }

            if (_target != null && !_fired && _cooldown <= 0f)
            {
                bool dwellDone = dwellSeconds > 0.0001f && _dwell >= dwellSeconds;
                if (selectEdge || dwellDone) Fire();
            }

            float dwell01 = Dwell01;
            GameObject targetGo = _target != null ? _target.gameObject : null;

            if (panel != null) panel.SetHighlight(targetGo, dwell01);
            UpdateReticle(targetGo != null, dwell01);
        }

        // ------------------------------------------------------------------

        /// <summary>頭から前方へレイを飛ばし、押せるボタンだけを返す。</summary>
        Button Probe()
        {
            var ray = new Ray(head.position, head.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Max(0.1f, maxDistance),
                    hitMask.value, QueryTriggerInteraction.Collide))
            {
                return null;
            }

            // コライダーがボタンの子に付いていても拾えるように親もたどる。
            Button button = hit.collider.GetComponentInParent<Button>();
            if (button == null) return null;
            if (!button.IsInteractable()) return null;

            return button;
        }

        void Fire()
        {
            Button button = _target;

            _fired = true;
            _dwell = 0f;
            _cooldown = Mathf.Max(0f, refireCooldown);

            if (button == null || !button.IsInteractable()) return;

            // Director 側にも同じガードがあるが、ここでも見ておくと意図が明確になる。
            if (director != null && director.IsDiving) return;

            // onClick の中でパネルごと消えることがある。以降 _target には触らない。
            button.onClick.Invoke();
        }

        void ClearTarget()
        {
            _target = null;
            _dwell = 0f;
            _lost = 0f;
            _fired = false;
        }

        void HandleDiveStarted(DiveTransitionBase transition)
        {
            ClearTarget();
            if (panel != null) panel.SetHighlight(null, 0f);
            if (_reticleGo != null) _reticleGo.SetActive(false);
        }

        void HandleDiveFinished(DiveTransitionBase transition)
        {
            ClearTarget();
            if (_reticleGo != null) _reticleGo.SetActive(showReticle);
        }

        void ResolveHead()
        {
            Camera cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);

            if (cam != null)
            {
                head = cam.transform;
                return;
            }

            // 毎フレーム警告を出しても邪魔なので、ここでは黙っておく（Probe も走らない）。
        }

        // ------------------------------------------------------------------
        // レティクル
        // ------------------------------------------------------------------

        void BuildReticle()
        {
            if (_reticleGo != null || head == null) return;

            // 外からレティクルを消されたあとの作り直しに備えて、前回ぶんを捨ててから作る。
            DestroyReticleAssets();

            _ringSprite = MakeRadialSprite(64, 0.74f, 0.94f);
            _ringTexture = _ringSprite != null ? _ringSprite.texture : null;

            _dotSprite = MakeRadialSprite(32, 0f, 1f);
            _dotTexture = _dotSprite != null ? _dotSprite.texture : null;

            _reticleGo = new GameObject("MRDive Reticle", typeof(RectTransform));
            _reticleGo.layer = gameObject.layer;

            var canvas = _reticleGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 200;   // パネルより手前に描く

            var rect = _reticleGo.GetComponent<RectTransform>();

            // 頭の子にしておけば、毎フレームの位置合わせが要らず遅延も出ない。
            rect.SetParent(head, false);
            rect.sizeDelta = new Vector2(ReticleUnits, ReticleUnits);
            rect.localPosition = new Vector3(0f, 0f, Mathf.Max(0.2f, reticleDistance));
            rect.localRotation = Quaternion.identity;

            float scale = Mathf.Max(0.005f, reticleSize) / ReticleUnits;
            rect.localScale = new Vector3(scale, scale, scale);

            _reticleRing = AddReticleImage("Ring", rect, _ringSprite, reticleIdleColor);
            StretchFull(_reticleRing.rectTransform);

            _reticleProgress = AddReticleImage("Progress", rect, _ringSprite, reticleProgressColor);
            StretchFull(_reticleProgress.rectTransform);
            _reticleProgress.type = Image.Type.Filled;
            _reticleProgress.fillMethod = Image.FillMethod.Radial360;
            _reticleProgress.fillOrigin = (int)Image.Origin360.Top;
            _reticleProgress.fillClockwise = true;
            _reticleProgress.fillAmount = 0f;

            _reticleDot = AddReticleImage("Dot", rect, _dotSprite, reticleIdleColor);
            _reticleDotRect = _reticleDot.rectTransform;
            _reticleDotRect.anchorMin = new Vector2(0.5f, 0.5f);
            _reticleDotRect.anchorMax = new Vector2(0.5f, 0.5f);
            _reticleDotRect.pivot = new Vector2(0.5f, 0.5f);
            _reticleDotRect.sizeDelta = new Vector2(16f, 16f);
            _reticleDotRect.anchoredPosition = Vector2.zero;
        }

        void UpdateReticle(bool hovering, float dwell01)
        {
            if (_reticleGo == null) return;

            if (_reticleRing != null)
                _reticleRing.color = hovering ? reticleHotColor : reticleIdleColor;

            if (_reticleProgress != null)
                _reticleProgress.fillAmount = hovering ? dwell01 : 0f;

            if (_reticleDotRect != null)
            {
                // 注視中は中心点をふくらませて「拾えている」ことを伝える。
                float s = Mathf.Lerp(16f, 30f, hovering ? dwell01 : 0f);
                _reticleDotRect.sizeDelta = new Vector2(s, s);
            }

            if (_reticleDot != null)
                _reticleDot.color = hovering ? reticleHotColor : reticleIdleColor;
        }

        static Image AddReticleImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;

            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 円／円環のスプライトを実行時に作る。
        /// 組み込みスプライトの名前に依存したくないので自前で描く。
        /// 半径方向に 1px ぶんのなだらかな境界を入れてジャギを消している。
        /// </summary>
        static Sprite MakeRadialSprite(int size, float inner01, float outer01)
        {
            if (size < 4) size = 4;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MRDive Reticle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float inner = inner01 * center;
            float outer = outer01 * center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01(outer - d);
                    if (inner > 0f) alpha *= Mathf.Clamp01(d - inner);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            // Image.Type.Filled は FullRect でないと正しく削れないので、必ず FullRect で作る。
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
        }
    }
}
