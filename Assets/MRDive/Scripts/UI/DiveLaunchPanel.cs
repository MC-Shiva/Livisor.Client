using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Livisor.MRDive
{
    /// <summary>
    /// ダイブ演出を選ぶワールド空間パネル。実行時にコードだけで組み立てる。
    ///
    /// なぜ prefab を置かないか:
    ///   シーンに置いた UI は壊れやすく、壊れたことに HMD を被るまで気づけない。
    ///   全部コードで生成しておけば、コンパイルさえ通れば必ず出る。
    ///
    /// なぜ TextMeshPro を使わないか:
    ///   TMP Essential Resources が未インポートのプロジェクトでは実行時に文字が消える。
    ///   レガシーの <see cref="Text"/> なら組み込みフォントだけで完結する。
    ///
    /// なぜ EventSystem / GraphicRaycaster を使わないか:
    ///   VR ではポインタ周りのセットアップが重く壊れやすい。代わりに各ボタンへ
    ///   <see cref="BoxCollider"/> を付けておき、<see cref="DiveGazePointer"/> が
    ///   <see cref="Physics.Raycast"/> で当てて <see cref="Button.onClick"/> を直接 Invoke する。
    ///
    /// 単位の決め方:
    ///   Canvas の RectTransform を 900px 幅にして localScale を 0.001 にしてある。
    ///   つまり「UI の 1px = 現実の 1mm」。フォントサイズ 32 と書けば 32mm の文字になる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Livisor/MR Dive/Dive Launch Panel")]
    public sealed class DiveLaunchPanel : MonoBehaviour
    {
        // --- レイアウト定数（すべて px = mm）---
        const float PanelWidth = 900f;
        const float PadX = 34f;
        const float PadTop = 30f;
        const float PadBottom = 30f;
        const float TitleHeight = 48f;
        const float StatusHeight = 26f;
        const float HintHeight = 26f;
        const float HeaderGap = 22f;
        const float RowHeight = 104f;
        const float RowGap = 12f;
        const float FrameThickness = 3f;
        const float AccentBarWidth = 8f;
        const float DwellBarHeight = 6f;
        const float ColliderDepth = 12f;

        [Header("参照")]
        [Tooltip("未設定ならシーンから自動で探す。")]
        [SerializeField] DiveDirector director;

        [Tooltip("未設定なら Camera.main の Transform を使う。")]
        [SerializeField] Transform head;

        [Header("配置")]
        [Tooltip("頭からパネルまでの距離（m）。")]
        [SerializeField] float distance = 1.6f;

        [Tooltip("目線からどれだけ下に出すか（度）。少し下にあるほうが首が楽。")]
        [SerializeField] float pitchDegrees = 10f;

        [Tooltip("起動時に一度だけ正面へ置く。以降は頭に追従しない（VR では追従がうっとうしいため）。")]
        [SerializeField] bool recenterOnStart = true;

        [Tooltip("トラッキングが安定してからもう一度置き直すまでの秒数。0 で無効。")]
        [SerializeField] float settleDelay = 0.75f;

        [Header("見た目")]
        [Tooltip("パネルの物理的な横幅（m）。")]
        [SerializeField] float panelWidthMeters = 0.9f;

        [Tooltip("ダイナミックフォントの解像度倍率。レイアウトには影響しない。ぼやけるなら 2 に。")]
        [SerializeField] float dynamicPixelsPerUnit = 1f;

        [Tooltip("ボタンのコライダーを置くレイヤー。DiveGazePointer のマスクと合わせること。既定は組み込みの UI(5)。")]
        [SerializeField] int uiLayer = 5;

        [Tooltip("上部に出すタイトル。")]
        [SerializeField] string titleText = "LIVISOR // DIVE";

        [Header("色")]
        [SerializeField] Color panelBackground = new Color(0.035f, 0.045f, 0.065f, 0.88f);
        [SerializeField] Color panelBorder = new Color(0.45f, 0.78f, 1f, 0.55f);
        [SerializeField] Color randomAccent = new Color(0.95f, 0.82f, 0.45f, 1f);

        /// <summary>ボタン 1 枚ぶんの実体。見た目の更新に必要な参照をまとめて持つ。</summary>
        sealed class Entry
        {
            public GameObject Go;
            public RectTransform Rect;
            public Button Button;
            public Image Frame;
            public Image Fill;
            public Image AccentBar;
            public RectTransform DwellBar;
            public Text Label;
            public Color Accent;
        }

        readonly List<Entry> _entries = new List<Entry>();

        GameObject _canvasGo;
        RectTransform _canvasRect;
        Canvas _canvas;
        CanvasGroup _group;
        Text _statusLabel;
        Font _font;
        Sprite _roundedSprite;
        bool _interactable = true;
        float _statusTimer;
        bool _built;

        /// <summary>パネルが今表示されているか。</summary>
        public bool IsVisible => _canvasGo != null && _canvasGo.activeSelf;

        /// <summary>組み立て済みのボタン枚数（ランダムボタンを含む）。</summary>
        public int ButtonCount => _entries.Count;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<DiveDirector>();
            if (director == null)
            {
                Debug.LogError("[MRDive] DiveDirector が見つかりません。DiveLaunchPanel を無効化します。", this);
                enabled = false;
                return;
            }

            ResolveHead();
        }

        void OnEnable()
        {
            if (director == null) return;
            director.DiveStarted += HandleDiveStarted;
            director.DiveCancelledOrFinished += HandleDiveFinished;
        }

        void OnDisable()
        {
            if (director == null) return;
            director.DiveStarted -= HandleDiveStarted;
            director.DiveCancelledOrFinished -= HandleDiveFinished;
        }

        void Start()
        {
            Build();

            if (recenterOnStart)
            {
                Recenter();

                // 起動直後は HMD の姿勢がまだ原点付近のことがある。少し待ってもう一度置き直す。
                if (settleDelay > 0f) StartCoroutine(RecenterAfterSettle());
            }

            RefreshStatus();
        }

        void OnDestroy()
        {
            // Canvas はシーン直下に切り離して作っているので、自分で片付ける。
            if (_canvasGo != null) Destroy(_canvasGo);
        }

        void Update()
        {
            if (director == null || !_built) return;

            bool live = !director.IsDiving;
            if (live != _interactable)
            {
                _interactable = live;
                ApplyInteractable();
            }

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = 0.5f;
                RefreshStatus();
            }
        }

        // ------------------------------------------------------------------
        // 公開 API
        // ------------------------------------------------------------------

        /// <summary>今の頭の向きを基準にパネルを置き直す。位置がずれたときに呼ぶ。</summary>
        public void Recenter()
        {
            if (_canvasRect == null) return;
            if (head == null) ResolveHead();
            if (head == null) return;

            // 水平に潰した前方向を基準にすることで、見上げた／見下ろした状態で
            // 起動しても panel が変な高さに出ない。
            Vector3 flat = head.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
            Vector3 dir = Quaternion.AngleAxis(Mathf.Max(0f, pitchDegrees), right) * flat;

            _canvasRect.position = head.position + dir * Mathf.Max(0.3f, distance);

            // Canvas の +Z が「見られる向き」なので、視線と同じ向きを向かせると正面を向く。
            _canvasRect.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>パネルの表示／非表示。</summary>
        public void SetVisible(bool on)
        {
            if (_canvasGo != null) _canvasGo.SetActive(on);
        }

        /// <summary>
        /// 注視中のボタンを知らせる口。<see cref="DiveGazePointer"/> から毎フレーム呼ばれる。
        /// target が null なら全部ハイライト解除。
        /// </summary>
        /// <param name="target">注視されているボタンの GameObject（子でも可）。</param>
        /// <param name="dwell01">注視の進捗 0..1。ボタン下部のバーになる。</param>
        public void SetHighlight(GameObject target, float dwell01)
        {
            dwell01 = Mathf.Clamp01(dwell01);

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Go == null) continue;

                // コライダーが子に付いていても拾えるように子孫も見る。
                bool hovered = target != null &&
                               (ReferenceEquals(e.Go, target) || target.transform.IsChildOf(e.Rect));

                ApplyEntryVisual(e, hovered, hovered ? dwell01 : 0f);
            }
        }

        // ------------------------------------------------------------------
        // 組み立て
        // ------------------------------------------------------------------

        void Build()
        {
            if (_built) return;
            _built = true;

            _font = ResolveFont();
            _roundedSprite = ResolveRoundedSprite();

            // Canvas はシーン直下（親なし）に作る。
            //   - ダイブ中に SetActive(false) しても、このコンポーネント自身の Update は生きる
            //   - 親のスケールや回転に引きずられないので、置いた場所に確実に留まる
            _canvasGo = new GameObject("MRDive Launch Panel", typeof(RectTransform));
            _canvasGo.layer = uiLayer;

            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            // ワールド空間では Canvas.scaleFactor は無視される（Screen Space 専用）。
            // ダイナミックフォントのラスタライズ解像度を上げるのは CanvasScaler の仕事で、
            // こちらはレイアウト（パネル幅・ボタン位置）には一切影響しない。
            // referencePixelsPerUnit は 100 のままにして、組み込みスプライトの
            // 9-slice 枠が素の太さで出るようにする。
            _canvas.referencePixelsPerUnit = 100f;

            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.referencePixelsPerUnit = 100f;
            scaler.dynamicPixelsPerUnit = Mathf.Max(1f, dynamicPixelsPerUnit);

            _group = _canvasGo.AddComponent<CanvasGroup>();

            // ここを false にすると Selectable.IsInteractable() が親の CanvasGroup を辿って
            // 全ボタンを押せない扱いにしてしまう。注視判定がまるごと死ぬので必ず true。
            _group.interactable = true;

            // EventSystem を使わないので、レイキャストは切ってよい。
            _group.blocksRaycasts = false;

            _canvasRect = _canvasGo.GetComponent<RectTransform>();
            _canvasRect.sizeDelta = new Vector2(PanelWidth, 400f);   // 高さは後で確定させる
            // 900px 幅 × 0.001 = 0.9m。この比率のおかげで 1px = 1mm になる。
            float scale = Mathf.Max(0.05f, panelWidthMeters) / PanelWidth;
            _canvasRect.localScale = new Vector3(scale, scale, scale);

            // 背景板。パススルーの上に重なるので、暗くして文字を白く抜く。
            Image background = _canvasGo.AddComponent<Image>();
            ApplySprite(background, panelBackground);

            // 上辺のアクセントライン。パネル全体を囲う枠は描画コストのわりに効かないので、
            // 「ここが上だ」と分かる 1 本だけにしている。
            RectTransform topLine = MakeTopAnchored("Top Line", _canvasRect, PanelWidth, 4f, 0f);
            ApplySprite(AddImage(topLine), panelBorder);

            float y = PadTop;

            RectTransform titleRect = MakeTopAnchored("Title", _canvasRect, PanelWidth - PadX * 2f, TitleHeight, y);
            MakeText(titleRect, titleText, 40, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            y += TitleHeight + 2f;

            RectTransform statusRect = MakeTopAnchored("Status", _canvasRect, PanelWidth - PadX * 2f, StatusHeight, y);
            _statusLabel = MakeText(statusRect, "…", 20, FontStyle.Normal,
                new Color(0.62f, 0.72f, 0.82f, 1f), TextAnchor.MiddleLeft);
            y += StatusHeight + 2f;

            RectTransform hintRect = MakeTopAnchored("Hint", _canvasRect, PanelWidth - PadX * 2f, HintHeight, y);
            MakeText(hintRect, "見つめて決定 / トリガー・A・X で即決定", 20, FontStyle.Normal,
                new Color(0.55f, 0.62f, 0.72f, 1f), TextAnchor.MiddleLeft);
            y += HintHeight + HeaderGap;

            IReadOnlyList<DiveTransitionBase> transitions = director.Transitions;
            int count = transitions != null ? transitions.Count : 0;

            for (int i = 0; i < count; i++)
            {
                DiveTransitionBase t = transitions[i];
                if (t == null) continue;

                // 番号ではなく実体を捕まえる。並び順が変わってもボタンと演出がずれない。
                DiveTransitionBase captured = t;
                CreateButton(
                    $"Dive {i}",
                    string.IsNullOrEmpty(t.DisplayName) ? $"演出 {i + 1}" : t.DisplayName,
                    string.IsNullOrEmpty(t.Tagline) ? $"{t.Duration:0.0} 秒" : t.Tagline,
                    t.AccentColor,
                    y,
                    () => director.Dive(captured));

                y += RowHeight + RowGap;
            }

            if (count == 0)
            {
                RectTransform empty = MakeTopAnchored("Empty", _canvasRect, PanelWidth - PadX * 2f, RowHeight, y);
                MakeText(empty, "演出が 1 つも登録されていません。\nDiveDirector の子に DiveTransition を置いてください。",
                    22, FontStyle.Normal, new Color(1f, 0.6f, 0.5f, 1f), TextAnchor.MiddleLeft);
                y += RowHeight + RowGap;
            }
            else
            {
                CreateButton("Dive Random", "ランダム", "おまかせで 1 つ選ぶ", randomAccent, y,
                    () => director.DiveRandom());
                y += RowHeight + RowGap;
            }

            y = y - RowGap + PadBottom;
            _canvasRect.sizeDelta = new Vector2(PanelWidth, y);

            // 子は全部「上端アンカー」で置いてあるので、ここで高さを変えても位置はずれない。
            ApplyInteractable();
        }

        void CreateButton(string name, string label, string tagline, Color accent, float topOffset, UnityEngine.Events.UnityAction onClick)
        {
            float width = PanelWidth - PadX * 2f;

            RectTransform root = MakeTopAnchored(name, _canvasRect, width, RowHeight, topOffset);

            var entry = new Entry
            {
                Go = root.gameObject,
                Rect = root,
                Accent = accent,
            };

            // 枠 → 塗り の 2 枚重ね。塗りを 3px 内側に入れることで細い縁取りになる。
            entry.Frame = AddImage(root);
            ApplySprite(entry.Frame, FrameColor(accent, false));

            RectTransform fillRect = MakeStretched("Fill", root, FrameThickness);
            entry.Fill = AddImage(fillRect);
            ApplySprite(entry.Fill, FillColor(accent, false));

            // 左端のアクセントバー。色で演出を見分けられるようにする。
            RectTransform barRect = NewRect("Accent", fillRect);
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(AccentBarWidth, 0f);
            barRect.anchoredPosition = Vector2.zero;
            entry.AccentBar = AddImage(barRect);
            entry.AccentBar.color = accent;

            float textLeft = AccentBarWidth + 22f;

            RectTransform labelRect = NewRect("Label", fillRect);
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(textLeft, 0f);
            labelRect.offsetMax = new Vector2(-20f, -8f);
            entry.Label = MakeText(labelRect, label, 32, FontStyle.Bold, Color.white, TextAnchor.LowerLeft);

            RectTransform taglineRect = NewRect("Tagline", fillRect);
            taglineRect.anchorMin = new Vector2(0f, 0f);
            taglineRect.anchorMax = new Vector2(1f, 0.5f);
            taglineRect.pivot = new Vector2(0.5f, 0.5f);
            taglineRect.offsetMin = new Vector2(textLeft, 12f);
            taglineRect.offsetMax = new Vector2(-20f, 0f);
            MakeText(taglineRect, tagline, 20, FontStyle.Normal,
                Color.Lerp(accent, Color.white, 0.55f), TextAnchor.UpperLeft);

            // 注視進捗バー。幅は anchorMax.x を動かして表す（fillAmount と違いスプライト不要）。
            RectTransform dwellRect = NewRect("Dwell", fillRect);
            dwellRect.anchorMin = new Vector2(0f, 0f);
            dwellRect.anchorMax = new Vector2(0f, 0f);
            dwellRect.pivot = new Vector2(0f, 0f);
            dwellRect.sizeDelta = new Vector2(0f, DwellBarHeight);
            dwellRect.anchoredPosition = Vector2.zero;
            Image dwellImage = AddImage(dwellRect);
            dwellImage.color = accent;
            entry.DwellBar = dwellRect;

            // 当たり判定。EventSystem を使わないので自前のコライダーで拾う。
            // Canvas 全体が 0.001 倍なので、ここの数値はそのまま mm と思ってよい。
            root.gameObject.layer = uiLayer;
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(width, RowHeight, ColliderDepth);
            box.center = Vector3.zero;   // pivot が中央なのでオフセット不要

            entry.Button = root.gameObject.AddComponent<Button>();
            entry.Button.transition = Selectable.Transition.None;   // 色は自前で塗る
            entry.Button.targetGraphic = entry.Fill;
            entry.Button.onClick.AddListener(onClick);

            _entries.Add(entry);
        }

        // ------------------------------------------------------------------
        // 見た目の更新
        // ------------------------------------------------------------------

        void ApplyInteractable()
        {
            if (_group != null)
            {
                _group.alpha = _interactable ? 1f : 0.35f;
                _group.interactable = _interactable;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                if (e.Button != null) e.Button.interactable = _interactable;

                // ダイブ中はレイも通さない。誤爆防止。
                var box = e.Go != null ? e.Go.GetComponent<BoxCollider>() : null;
                if (box != null) box.enabled = _interactable;

                ApplyEntryVisual(e, false, 0f);
            }
        }

        void ApplyEntryVisual(Entry e, bool hovered, float dwell01)
        {
            bool live = hovered && _interactable;

            if (e.Frame != null) e.Frame.color = FrameColor(e.Accent, live);
            if (e.Fill != null) e.Fill.color = FillColor(e.Accent, live);
            if (e.Label != null) e.Label.color = live ? Color.white : new Color(0.93f, 0.95f, 0.97f, 1f);
            if (e.AccentBar != null)
                e.AccentBar.color = live ? Color.Lerp(e.Accent, Color.white, 0.4f) : e.Accent;

            if (e.DwellBar != null)
            {
                float w = _interactable ? dwell01 : 0f;
                e.DwellBar.anchorMax = new Vector2(w, 0f);
                e.DwellBar.offsetMin = new Vector2(0f, 0f);
                e.DwellBar.offsetMax = new Vector2(0f, DwellBarHeight);
            }
        }

        void RefreshStatus()
        {
            if (_statusLabel == null) return;

            string passthrough = director != null && director.Passthrough != null
                ? director.Passthrough.StatusMessage
                : "PassthroughBridge 未初期化";

            _statusLabel.text = $"{passthrough}   /   {XRDiveInput.DebugSummary()}";
        }

        void HandleDiveStarted(DiveTransitionBase transition)
        {
            SetVisible(false);
        }

        void HandleDiveFinished(DiveTransitionBase transition)
        {
            SetVisible(true);
            _interactable = true;
            ApplyInteractable();
        }

        IEnumerator RecenterAfterSettle()
        {
            yield return new WaitForSeconds(settleDelay);
            if (director == null || !director.IsDiving) Recenter();
        }

        void ResolveHead()
        {
            if (head != null) return;

            Camera cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
            if (cam != null)
            {
                head = cam.transform;
                return;
            }

            Debug.LogWarning("[MRDive] カメラが見つからないため、パネルを原点に置きます。", this);
        }

        // ------------------------------------------------------------------
        // 小道具
        // ------------------------------------------------------------------

        Color FillColor(Color accent, bool hovered)
        {
            // 明るい部屋のパススルーに負けないよう、地は必ず暗く落とす。
            Color c = Color.Lerp(accent, Color.black, hovered ? 0.6f : 0.82f);
            c.a = hovered ? 0.96f : 0.9f;
            return c;
        }

        Color FrameColor(Color accent, bool hovered)
        {
            Color c = accent;
            c.a = hovered ? 1f : 0.7f;
            return c;
        }

        RectTransform MakeTopAnchored(string name, Transform parent, float width, float height, float topOffset)
        {
            RectTransform rt = NewRect(name, parent);

            // 上端アンカー＋中央ピボット。こうしておくと後から親の高さを変えてもずれず、
            // BoxCollider の center も 0 のままで済む。
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, -(topOffset + height * 0.5f));
            return rt;
        }

        RectTransform MakeStretched(string name, Transform parent, float inset)
        {
            RectTransform rt = NewRect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = uiLayer;
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            return rt;
        }

        static Image AddImage(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;   // EventSystem を使わないので全部切る
            return image;
        }

        void ApplySprite(Image image, Color color)
        {
            if (image == null) return;

            if (_roundedSprite != null)
            {
                image.sprite = _roundedSprite;
                image.type = Image.Type.Sliced;   // 角丸を保ったまま引き伸ばす
            }
            else
            {
                image.sprite = null;
                image.type = Image.Type.Simple;   // スプライトが無ければただの矩形
            }

            image.color = color;
        }

        Text MakeText(RectTransform rect, string content, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.text = content;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = false;
            return text;
        }

        /// <summary>
        /// 組み込みフォントを取る。Unity 6 では "LegacyRuntime.ttf"。
        /// 昔の名前や OS フォントにも落とせるようにして、名前違いで文字が消えるのを防ぐ。
        /// </summary>
        static Font ResolveFont()
        {
            Font font = TryBuiltinFont("LegacyRuntime.ttf");
            if (font == null) font = TryBuiltinFont("Arial.ttf");
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont("Arial", 32);
                Debug.LogWarning("[MRDive] 組み込みフォントが取れないため OS フォントで代用します。", null);
            }
            return font;
        }

        static Font TryBuiltinFont(string path)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>角丸 9-slice の組み込みスプライト。取れなければ null（矩形にフォールバック）。</summary>
        static Sprite ResolveRoundedSprite()
        {
            try
            {
                return Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            }
            catch
            {
                return null;
            }
        }
    }
}
