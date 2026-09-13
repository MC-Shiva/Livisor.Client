using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Livisor.MRDive
{
    /// <summary>
    /// パススルー（現実）から VR のライブ会場へダイブする一連の流れを仕切る。
    ///
    /// 責務はこの 3 つだけに絞ってある:
    ///   - 舞台道具（頭・カメラ・パススルー・オーバーレイ）を揃えて <see cref="DiveContext"/> にする
    ///   - 演出を 1 本だけ走らせる（多重起動を防ぐ）
    ///   - 遷移先シーンの先読みと、演出完了に合わせた切り替え
    ///
    /// 見た目は一切持たない。演出は <see cref="DiveTransitionBase"/> の実装側にある。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Livisor/MR Dive/Dive Director")]
    public sealed class DiveDirector : MonoBehaviour
    {
        [Header("遷移先")]
        [Tooltip("ダイブ後に読み込むシーン名。Build Settings に登録されている必要がある。")]
        [SerializeField] string targetSceneName = "DemoScene";

        [Tooltip("演出中に遷移先を裏で読み込む。重いシーンほど効く。")]
        [SerializeField] bool preloadTargetScene = true;

        [Tooltip("外すと演出だけを繰り返し確認できる。演出の調整中に。")]
        [SerializeField] bool loadTargetScene = true;

        [Tooltip("遷移先で暗転／白飛びから明けるまでの秒数。")]
        [SerializeField] float arrivalFadeDuration = 1.1f;

        [Header("構成")]
        [Tooltip("空なら子オブジェクトから自動で集める。")]
        [SerializeField] List<DiveTransitionBase> transitions = new List<DiveTransitionBase>();

        [Tooltip("未設定なら Camera.main の Transform を使う。")]
        [SerializeField] Transform headOverride;

        [SerializeField] AudioSource audioSource;

        [Header("起動時")]
        [Tooltip("開始時にパススルーを有効化して、現実が見えている状態から始める。")]
        [SerializeField] bool enablePassthroughOnStart = true;

        [Header("デバッグ")]
        [Tooltip("Editor で 1/2/3 キーから演出を直接起動する。HMD を被らずに確認するための経路。")]
        [SerializeField] bool enableKeyboardShortcuts = true;

        DiveOverlay _overlay;
        PassthroughBridge _passthrough;
        Camera _eyeCamera;
        Transform _head;
        Coroutine _running;
        AsyncOperation _preload;

        /// <summary>演出が始まった瞬間。UI を引っ込めるのに使う。</summary>
        public event Action<DiveTransitionBase> DiveStarted;

        /// <summary>演出が終わって元の状態に戻ったとき。シーン遷移した場合は呼ばれない。</summary>
        public event Action<DiveTransitionBase> DiveCancelledOrFinished;

        public bool IsDiving => _running != null;
        public DiveContext Context { get; private set; }
        public IReadOnlyList<DiveTransitionBase> Transitions => transitions;
        public PassthroughBridge Passthrough => _passthrough;
        public string TargetSceneName => targetSceneName;

        void Awake()
        {
            if (transitions == null) transitions = new List<DiveTransitionBase>();

            // 手で並べていなければ子から拾う。並び順がそのまま UI の並び順になる。
            transitions.RemoveAll(t => t == null);
            if (transitions.Count == 0)
                transitions.AddRange(GetComponentsInChildren<DiveTransitionBase>(true));

            ResolveHead();

            _passthrough = PassthroughBridge.Ensure(gameObject);
            _passthrough.BindFallbackCamera(_eyeCamera);

            _overlay = DiveOverlay.Attach(_head, _eyeCamera);

            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            Context = new DiveContext(this, _head, _eyeCamera, _passthrough, _overlay, audioSource);
        }

        void Start()
        {
            if (enablePassthroughOnStart)
            {
                _passthrough.SetActive(true);
                _passthrough.Opacity = 1f;
                _passthrough.RestoreDefaults();
            }

            Debug.Log($"[MRDive] パススルー: {_passthrough.StatusMessage}");

            // 遷移先の確認はパススルーの設定とは無関係なので、必ず通す。
            if (loadTargetScene && !CanLoadTarget())
            {
                Debug.LogWarning(
                    $"[MRDive] 遷移先シーン \"{targetSceneName}\" が Build Settings に見つかりません。" +
                    "Livisor > MR Dive > Build Settings に登録 から追加してください。" +
                    "（このままだと演出だけ流れて元に戻ります）");
            }
        }

#if UNITY_EDITOR
        void Update()
        {
            // HMD なしの Editor 再生では視線もコントローラも動かないので、
            // キーボードから直接叩ける経路を残しておかないと演出を一度も確認できない。
            if (!enableKeyboardShortcuts || IsDiving) return;

            for (int i = 0; i < transitions.Count && i < 9; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                {
                    Dive(i);
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.Alpha0)) DiveRandom();
        }
#endif

        /// <summary>番号でダイブ開始。UI のボタンから。</summary>
        public bool Dive(int index)
        {
            if (index < 0 || index >= transitions.Count)
            {
                Debug.LogWarning($"[MRDive] 演出 #{index} は存在しません（登録数 {transitions.Count}）。", this);
                return false;
            }
            return Dive(transitions[index]);
        }

        /// <summary>どれか 1 つをランダムに。</summary>
        public bool DiveRandom()
        {
            if (transitions.Count == 0) return false;
            return Dive(transitions[UnityEngine.Random.Range(0, transitions.Count)]);
        }

        /// <summary>指定の演出でダイブ開始。すでに走っていれば何もしない。</summary>
        public bool Dive(DiveTransitionBase transition)
        {
            if (transition == null) return false;

            if (IsDiving)
            {
                Debug.Log("[MRDive] すでにダイブ中のため無視しました。", this);
                return false;
            }

            _running = StartCoroutine(DiveRoutine(transition));
            return true;
        }

        IEnumerator DiveRoutine(DiveTransitionBase transition)
        {
            DiveStarted?.Invoke(transition);

            bool willLoad = loadTargetScene && CanLoadTarget();

            // 準備を先に済ませてからプリロードを始める。逆順だと、準備で落ちたときに
            // 「演出なしでいきなり遷移先へ飛ぶ」という一番デバッグしづらい壊れ方をする。
            bool prepared = true;
            try
            {
                transition.OnPrepare(Context);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MRDive] {transition.DisplayName} の準備で例外: {e}", this);
                prepared = false;
            }

            if (!prepared)
            {
                yield return Restore(transition);
                yield break;
            }

            if (willLoad && preloadTargetScene)
            {
                _preload = SceneManager.LoadSceneAsync(targetSceneName);
                if (_preload != null) _preload.allowSceneActivation = false;
            }

            bool runFailed = false;
            yield return RunGuarded(transition.Run(Context), transition.DisplayName, () => runFailed = true);

            if (runFailed)
            {
                // 演出が死んだまま遷移すると「演出ゼロで一瞬で着地」という、
                // 例外に気づけない最悪の壊れ方になる。遷移可否にかかわらず必ず戻す。
                yield return Restore(transition);
                yield break;
            }

            if (!willLoad)
            {
                yield return Restore(transition);
                yield break;
            }

            // ここから先はシーンごと消えるので、後始末は着地側に託す。
            DiveArrivalFade.Spawn(transition.ArrivalFadeColor, arrivalFadeDuration);

            if (_preload != null) _preload.allowSceneActivation = true;
            else SceneManager.LoadScene(targetSceneName);
        }

        /// <summary>
        /// 内側のコルーチンを自前で回して、どの階層で例外が出ても握りつぶす。
        ///
        /// Unity のコルーチンは例外が出た時点で親ごと停止するので、素直に
        /// <c>yield return transition.Run(ctx)</c> と書くと <see cref="_running"/> が
        /// 戻らず、二度とダイブできない状態に陥る。それを防ぐためのラッパー。
        /// </summary>
        IEnumerator RunGuarded(IEnumerator inner, string label, Action onError)
        {
            if (inner == null) yield break;

            var stack = new Stack<IEnumerator>();
            stack.Push(inner);

            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                bool moved;
                object current = null;

                try
                {
                    moved = top.MoveNext();
                    if (moved) current = top.Current;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MRDive] {label} の実行中に例外: {e}", this);
                    onError?.Invoke();
                    DisposeAll(stack);
                    yield break;
                }

                if (!moved)
                {
                    stack.Pop();
                    continue;
                }

                // ネストしたコルーチンも自分で回さないと、その中の例外を捕まえられない。
                // ただし CustomYieldInstruction（WaitUntil 等）は IEnumerator でもあるので、
                // 自前で回すと同一フレーム内で無限に回ってしまう。Unity に委ねる。
                if (current is IEnumerator nested && !(current is CustomYieldInstruction))
                {
                    stack.Push(nested);
                    continue;
                }

                yield return current;
            }
        }

        /// <summary>
        /// 途中で抜けるとき、積んであるコルーチンの後始末を走らせる。
        /// 実装側が Run に try/finally や using を書いていた場合、自前で回している以上
        /// ここで Dispose しないとその finally が永久に実行されない。
        /// </summary>
        static void DisposeAll(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0)
            {
                if (stack.Pop() is IDisposable disposable) disposable.Dispose();
            }
        }

        /// <summary>遷移しない／できないときに、元の「現実が見える」状態へ戻す。</summary>
        IEnumerator Restore(DiveTransitionBase transition)
        {
            try
            {
                transition.OnCleanup(Context);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MRDive] {transition.DisplayName} の後始末で例外: {e}", this);
            }

            _overlay.Clear();
            _passthrough.RestoreDefaults();
            _passthrough.SetActive(true);
            _passthrough.Opacity = 1f;

            // プリロード済みのシーンがあっても、ここでアクティブ化してはいけない。
            // 「戻す」経路で遷移してしまうと、演出なしで唐突に着地することになる。
            _preload = null;

            yield return null;

            _running = null;
            DiveCancelledOrFinished?.Invoke(transition);
        }

        bool CanLoadTarget()
        {
            return !string.IsNullOrEmpty(targetSceneName)
                   && Application.CanStreamedLevelBeLoaded(targetSceneName);
        }

        void ResolveHead()
        {
            if (headOverride != null)
            {
                _head = headOverride;
                _eyeCamera = headOverride.GetComponent<Camera>();
                if (_eyeCamera == null) _eyeCamera = Camera.main;
                return;
            }

            // まず OVRCameraRig の CenterEyeAnchor を直接探す。
            // Camera.main に頼ってはいけない: OVRCameraRig は LeftEyeAnchor にも
            // MainCamera タグを付けており、Camera.main がそちらを返すことがある。
            // そうなると演出の基準点が左目にずれ、パススルーの設定も
            // CenterEyeAnchor ではなく左目のカメラに当たってしまう。
            Transform centerEye = OvrLookup.FindCenterEyeAnchor();
            if (centerEye != null)
            {
                _head = centerEye;
                _eyeCamera = centerEye.GetComponent<Camera>();
                if (_eyeCamera == null) _eyeCamera = Camera.main;
                return;
            }

            _eyeCamera = Camera.main;
            if (_eyeCamera == null)
            {
#if UNITY_2023_1_OR_NEWER
                _eyeCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Exclude);
#else
                _eyeCamera = FindObjectOfType<Camera>();
#endif
            }

            if (_eyeCamera != null)
            {
                _head = _eyeCamera.transform;
                return;
            }

            Debug.LogError("[MRDive] カメラが見つかりません。OVRCameraRig をシーンに置いてください。", this);
            _head = transform;
        }
    }
}
