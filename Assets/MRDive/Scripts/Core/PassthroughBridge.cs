using System;
using System.Reflection;
using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// Meta XR SDK (OVRPassthroughLayer / OVRManager) への依存を、この 1 クラスに閉じ込める。
    ///
    /// SDK の型はすべてリフレクション越しに触る。理由は 2 つ:
    ///   1. SDK が未インポートの状態やバージョン差異でもプロジェクト全体のコンパイルが壊れない
    ///   2. Editor 再生（パススルーが存在しない）でも同じコードパスで演出を確認できる
    ///
    /// SDK が見つからない場合は <see cref="IsAvailable"/> が false になり、
    /// 「現実の見え方」はカメラの背景色を使った簡易フォールバックで代替する。
    ///
    /// メンバ名と種別は Meta XR SDK v203.0.0 の実ソースに合わせてある。特に
    /// <c>hidden</c> と <c>isInsightPassthroughEnabled</c> はプロパティではなく
    /// public フィールドなので、両方を探せる <see cref="Member"/> 経由で触っている。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Livisor/MR Dive/Passthrough Bridge")]
    public sealed class PassthroughBridge : MonoBehaviour
    {
        const string LayerTypeName = "OVRPassthroughLayer";
        const string ManagerTypeName = "OVRManager";

        [Tooltip("フォールバック時に『現実が見えている』代わりに使う背景色。Editor 確認用。アルファは常に 0 に強制される。")]
        [SerializeField] Color fallbackRealityColor = new Color(0.16f, 0.17f, 0.19f, 0f);

        [Tooltip("パススルーが完全に消えたときの背景色。アルファは常に 0 に強制される。")]
        [SerializeField] Color fallbackVoidColor = new Color(0f, 0f, 0f, 0f);

        // --- OVRPassthroughLayer ---
        Component _layer;
        Action<float> _setOpacityFast;
        Member _opacity;
        Member _hidden;
        Member _edgeEnabled;
        Member _edgeColor;
        Member _colorMapType;
        MethodInfo _setBrightnessContrastSaturation;
        MethodInfo _disableColorMap;
        object _colorMapNone;
        object _colorMapAdjustment;

        // --- OVRManager ---
        object _managerInstance;
        Member _insightEnabled;
        MethodInfo _isInitialized;

        Camera _fallbackCamera;

        float _opacityValue = 1f;
        bool _probed;

        float _runningCheckedAt = float.NegativeInfinity;
        bool _runningCache;

        /// <summary>Meta の SDK が実際に見つかり、パススルーを操作できる状態か。</summary>
        public bool IsAvailable { get; private set; }

        /// <summary>
        /// パススルーの描画が実際に走っているか。SDK 側の初期化は非同期なので、
        /// <see cref="IsAvailable"/> が true でもここが false の間は画面に何も出ない。
        /// </summary>
        public bool IsRunning
        {
            get
            {
                if (_isInitialized == null) return IsAvailable;
                try { return (bool)_isInitialized.Invoke(null, null); }
                catch (Exception) { return false; }
            }
        }

        /// <summary>
        /// <see cref="IsRunning"/> の結果を 0.25 秒だけ使い回す。
        /// 判定はリフレクション呼び出しなので、毎フレーム叩く経路からはこちらを使う。
        /// </summary>
        bool IsRunningCached
        {
            get
            {
                float now = Time.unscaledTime;
                if (now - _runningCheckedAt < 0.25f) return _runningCache;
                _runningCheckedAt = now;
                _runningCache = IsRunning;
                return _runningCache;
            }
        }

        /// <summary>探索の結果を人間向けに 1 行で。HUD やログに出す用。</summary>
        public string StatusMessage { get; private set; } = "未初期化";

        /// <summary>1 = 現実が完全に見えている、0 = 現実が完全に消えて VR だけ。</summary>
        public float Opacity
        {
            get => _opacityValue;
            set
            {
                float v = Mathf.Clamp01(value);
                if (_probed && Mathf.Approximately(v, _opacityValue)) return;
                _opacityValue = v;
                ApplyOpacity(v);
            }
        }

        void Awake()
        {
            Probe();
        }

        /// <summary>指定の GameObject に Bridge がなければ足して返す。</summary>
        public static PassthroughBridge Ensure(GameObject host)
        {
            if (host == null) return null;

            // UnityEngine.Object に ?? を使うと「破棄済みだが参照は残っている」偽 null を
            // 拾えないので、明示的に == null で見る。
            var existing = host.GetComponent<PassthroughBridge>();
            return existing != null ? existing : host.AddComponent<PassthroughBridge>();
        }

        /// <summary>フォールバック描画に使うカメラ。Director が解決した視点カメラを渡す。</summary>
        public void BindFallbackCamera(Camera camera)
        {
            // 他の公開メソッドと同じく、先に探索を済ませておく。手動で AddComponent された
            // 場合は Awake 順が保証されず、未探索のまま呼ばれることがある。
            Probe();
            _fallbackCamera = camera;
            ApplyOpacity(_opacityValue);
        }

        /// <summary>パススルーの表示そのものを入切する。ダイブ完了後に false にする。</summary>
        public void SetActive(bool on)
        {
            Probe();

            if (_layer != null)
            {
                if (_layer is Behaviour behaviour) behaviour.enabled = on;
                _hidden?.TrySet(_layer, !on, this);
            }

            _insightEnabled?.TrySet(_managerInstance, on, this);

            if (!IsAvailable) ApplyFallback(on ? _opacityValue : 0f);
        }

        /// <summary>
        /// 現実から色を抜く。0 = そのまま、1 = 完全なモノクロ。
        /// 「現実が色を失っていく」導入部に効く。SDK 側が対応していなければ何もしない。
        /// </summary>
        public void SetDesaturation(float amount)
        {
            SetColorAdjustment(0f, 0f, -Mathf.Clamp01(amount));
        }

        /// <summary>
        /// パススルー映像の明度 / コントラスト / 彩度を直接指定する。いずれも -1..1。
        /// </summary>
        public void SetColorAdjustment(float brightness, float contrast, float saturation)
        {
            Probe();
            if (_setBrightnessContrastSaturation == null || _layer == null) return;

            // 色調整は colorMapEditorType を ColorAdjustment に切り替えてからでないと効かない。
            if (_colorMapAdjustment != null) _colorMapType?.TrySet(_layer, _colorMapAdjustment, this);

            try
            {
                _setBrightnessContrastSaturation.Invoke(_layer, new object[]
                {
                    Mathf.Clamp(brightness, -1f, 1f),
                    Mathf.Clamp(contrast, -1f, 1f),
                    Mathf.Clamp(saturation, -1f, 1f),
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MRDive] 色調整の適用に失敗: {e.Message}", this);
                _setBrightnessContrastSaturation = null;
            }
        }

        /// <summary>現実側の輪郭線。境界を光らせて「世界が縁取られる」表現に。</summary>
        public void SetEdgeRendering(bool on, Color color)
        {
            Probe();
            if (_layer == null) return;
            _edgeColor?.TrySet(_layer, color, this);
            _edgeEnabled?.TrySet(_layer, on, this);
        }

        /// <summary>色調整や輪郭線を初期状態に戻す。</summary>
        public void RestoreDefaults()
        {
            Probe();
            if (_layer == null) return;

            _edgeEnabled?.TrySet(_layer, false, this);

            if (_disableColorMap != null)
            {
                try { _disableColorMap.Invoke(_layer, null); return; }
                catch (Exception) { /* 下の直接指定にフォールバック */ }
            }

            if (_colorMapNone != null) _colorMapType?.TrySet(_layer, _colorMapNone, this);
        }

        // ------------------------------------------------------------------
        // リフレクション探索
        // ------------------------------------------------------------------

        void Probe()
        {
            if (_probed) return;
            _probed = true;

            Type layerType = FindType(LayerTypeName);
            if (layerType == null)
            {
                StatusMessage = $"{LayerTypeName} 型が見つからない（Meta XR SDK 未導入）。フォールバック描画で動作。";
                return;
            }

            _layer = FindFirstOfType(layerType) as Component;
            if (_layer == null)
            {
                StatusMessage = $"{LayerTypeName} がシーンに無い。OVRCameraRig に足してください。";
                return;
            }

            _opacity = Member.Find(layerType, "textureOpacity");
            if (_opacity == null || !_opacity.CanWrite)
            {
                StatusMessage = $"{LayerTypeName}.textureOpacity が書き込めない。SDK のバージョンを確認してください。";
                _layer = null;
                return;
            }

            // 毎フレーム叩くので、boxing しないデリゲートに落としておく。
            // IL2CPP などで作れなければ通常の Set にフォールバックする。
            try
            {
                var setter = _opacity.Setter;
                if (setter != null)
                    _setOpacityFast = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), _layer, setter);
            }
            catch (Exception)
            {
                _setOpacityFast = null;
            }

            // v203 では hidden は public フィールド。プロパティ実装の版もありうるので両方探す。
            _hidden = Member.Find(layerType, "hidden");
            _edgeEnabled = Member.Find(layerType, "edgeRenderingEnabled");
            _edgeColor = Member.Find(layerType, "edgeColor");
            _colorMapType = Member.Find(layerType, "colorMapEditorType");

            _disableColorMap = layerType.GetMethod("DisableColorMap",
                BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            _setBrightnessContrastSaturation = FindMethod(layerType, "SetBrightnessContrastSaturation", 3);

            if (_colorMapType != null && _colorMapType.MemberType.IsEnum)
            {
                Type enumType = _colorMapType.MemberType;
                _colorMapNone = FindEnumValue(enumType, "None");
                _colorMapAdjustment = FindEnumValue(enumType, "ColorAdjustment", "BrightnessContrastSaturation");
            }

            ProbeManager();

            IsAvailable = true;
            StatusMessage =
                $"{LayerTypeName} に接続 / 色調整={(_setBrightnessContrastSaturation != null ? "可" : "不可")}" +
                $" / 輪郭線={(_edgeEnabled != null ? "可" : "不可")}" +
                $" / Insight={(_insightEnabled != null ? "制御可" : "未検出")}";
        }

        void ProbeManager()
        {
            Type managerType = FindType(ManagerTypeName);
            if (managerType == null) return;

            var instanceProp = managerType.GetProperty("instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            try
            {
                _managerInstance = instanceProp != null ? instanceProp.GetValue(null) : null;
            }
            catch (Exception)
            {
                _managerInstance = null;
            }

            // OVRManager.instance は Awake 前だと null なので、シーン検索でも拾っておく。
            if (_managerInstance == null) _managerInstance = FindFirstOfType(managerType);
            if (_managerInstance == null) return;

            // v203 では public フィールド（プロパティではない）。
            _insightEnabled = Member.Find(managerType, "isInsightPassthroughEnabled");

            _isInitialized = managerType.GetMethod("IsInsightPassthroughInitialized",
                BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        }

        void ApplyOpacity(float v)
        {
            bool delivered = false;

            // ここは毎フレーム呼ばれる。例外を漏らすと演出コルーチンごと止まって
            // 「二度とダイブできない」状態になるので、必ず握って縮退させる。
            if (_setOpacityFast != null)
            {
                try
                {
                    _setOpacityFast(v);
                    delivered = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MRDive] textureOpacity の適用に失敗: {e.Message}", this);
                    _setOpacityFast = null;
                }
            }

            if (!delivered && _opacity != null && _layer != null)
            {
                try
                {
                    _opacity.Set(_layer, v);
                    delivered = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MRDive] textureOpacity の適用に失敗: {e.Message}", this);
                    _opacity = null;
                }
            }

            // SDK に値を渡せていても、パススルーの描画がまだ始まっていなければ画面は真っ黒。
            // Editor だと「型は見つかるが何も映らない」状態になりがちなので、
            // 実際に走っていない間は背景色での代替も併せて効かせる。
            if (!delivered || !IsRunningCached) ApplyFallback(v);
        }

        /// <summary>SDK がないときの代役。背景色を「現実の灰色」から「虚無の黒」へ寄せる。</summary>
        void ApplyFallback(float v)
        {
            if (_fallbackCamera == null) return;

            Color color = Color.Lerp(fallbackVoidColor, fallbackRealityColor, v);

            // アルファは必ず 0 にすること。Underlay のパススルーはアイバッファのアルファを
            // そのままマスクに使うので、ここで 1 を書き込むと実機で現実が完全に隠れる。
            // しかも書き戻す経路が無いまま固定されるうえ、Editor には合成相手がいないので
            // まったく再現しない。RGB だけは残るので、Editor での進行確認には支障がない。
            color.a = 0f;

            _fallbackCamera.clearFlags = CameraClearFlags.SolidColor;
            _fallbackCamera.backgroundColor = color;
        }

        // ------------------------------------------------------------------
        // リフレクション小物
        // ------------------------------------------------------------------

        /// <summary>プロパティとフィールドの差を吸収する薄いアクセサ。</summary>
        sealed class Member
        {
            readonly PropertyInfo _property;
            readonly FieldInfo _field;

            Member(PropertyInfo property, FieldInfo field)
            {
                _property = property;
                _field = field;
            }

            /// <summary>候補名を順に、まずプロパティ、次にフィールドとして探す。</summary>
            public static Member Find(Type type, params string[] names)
            {
                const BindingFlags flags =
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

                for (int i = 0; i < names.Length; i++)
                {
                    PropertyInfo p = null;
                    FieldInfo f = null;
                    try
                    {
                        p = type.GetProperty(names[i], flags);
                        if (p == null) f = type.GetField(names[i], flags);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (p != null) return new Member(p, null);
                    if (f != null) return new Member(null, f);
                }
                return null;
            }

            public Type MemberType => _property != null ? _property.PropertyType : _field.FieldType;

            public bool CanWrite => _property != null ? _property.CanWrite : !_field.IsInitOnly;

            public MethodInfo Setter => _property?.GetSetMethod(true);

            public void Set(object target, object value)
            {
                if (target == null) return;
                if (_property != null) _property.SetValue(target, value);
                else _field.SetValue(target, value);
            }

            /// <summary>失敗しても演出を止めたくないので、例外は警告 1 行に落とす。</summary>
            public void TrySet(object target, object value, UnityEngine.Object context)
            {
                if (target == null) return;
                try { Set(target, value); }
                catch (Exception e)
                {
                    string name = _property != null ? _property.Name : _field.Name;
                    Debug.LogWarning($"[MRDive] {name} の設定に失敗: {e.Message}", context);
                }
            }
        }

        static Type FindType(string name)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t;
                try { t = assemblies[i].GetType(name, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        static UnityEngine.Object FindFirstOfType(Type type)
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType(type, FindObjectsInactive.Include);
#else
            return FindObjectOfType(type, true);
#endif
        }

        static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != name) continue;
                if (methods[i].GetParameters().Length != parameterCount) continue;
                return methods[i];
            }
            return null;
        }

        static object FindEnumValue(Type enumType, params string[] candidateNames)
        {
            string[] names = Enum.GetNames(enumType);
            for (int c = 0; c < candidateNames.Length; c++)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (!string.Equals(names[i], candidateNames[c], StringComparison.OrdinalIgnoreCase)) continue;
                    return Enum.Parse(enumType, names[i]);
                }
            }
            return null;
        }
    }
}
