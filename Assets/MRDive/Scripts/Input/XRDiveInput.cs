using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Livisor.MRDive
{
    /// <summary>
    /// コントローラ入力の薄いラッパー。
    ///
    /// 方針:
    ///   - <see cref="UnityEngine.XR.InputDevices"/> だけを使う。Meta XR SDK にも
    ///     com.unity.inputsystem にも依存しないので、SDK のバージョンが変わっても壊れない。
    ///   - デバイス一覧は接続イベントで更新してキャッシュする。
    ///     毎フレームの GetDevicesWithCharacteristics は List を舐めるうえに alloc が出る。
    ///   - ボタンは「今フレーム押された」のエッジで返す。押しっぱなしで連射しない。
    ///   - XR デバイスが 1 台も繋がっていなくても例外を投げず、ただ false を返す。
    ///
    /// 状態の更新は最初のアクセサ呼び出しで 1 フレームに 1 回だけ走る（<see cref="Poll"/>）。
    /// MonoBehaviour を持たないので Update を持てないが、Time.frameCount で二重取得を防いでいる。
    /// </summary>
    public static class XRDiveInput
    {
        /// <summary>手に持つコントローラだけを拾う。HMD やトラッカーは除外。</summary>
        const InputDeviceCharacteristics ControllerMask =
            InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller;

        /// <summary>アナログトリガーのしきい値。行きと帰りをずらしてチャタリングを防ぐ。</summary>
        const float TriggerPress = 0.7f;
        const float TriggerRelease = 0.4f;

        static readonly List<InputDevice> Controllers = new List<InputDevice>();
        static readonly List<InputDevice> Scratch = new List<InputDevice>();

        static bool _hooked;
        static bool _dirty = true;
        static int _polledFrame = -1;

        // 今フレーム / 前フレームの押下状態
        static bool _select, _selectPrev;
        static bool _primary, _primaryPrev;
        static bool _secondary, _secondaryPrev;
        static bool _analogTriggerHeld;

        /// <summary>現在つながっているコントローラの数。0 でも正常動作する。</summary>
        public static int ControllerCount
        {
            get
            {
                EnsureHooked();
                return Controllers.Count;
            }
        }

        public static bool HasController => ControllerCount > 0;

        /// <summary>
        /// 決定操作。左右どちらかのトリガー、または A/X ボタンが今フレーム押されたか。
        /// </summary>
        public static bool SelectPressedThisFrame()
        {
            Poll();
            return _select && !_selectPrev;
        }

        /// <summary>A（右）/ X（左）が今フレーム押されたか。パターンの直接起動用。</summary>
        public static bool PrimaryPressedThisFrame()
        {
            Poll();
            return _primary && !_primaryPrev;
        }

        /// <summary>B（右）/ Y（左）が今フレーム押されたか。</summary>
        public static bool SecondaryPressedThisFrame()
        {
            Poll();
            return _secondary && !_secondaryPrev;
        }

        /// <summary>押しっぱなし判定が要るとき用。</summary>
        public static bool SelectHeld()
        {
            Poll();
            return _select;
        }

        /// <summary>HMD を被ったまま状況を確認するためのデバッグ文字列。</summary>
        public static string DebugSummary()
        {
            EnsureHooked();
            int n = Controllers.Count;
            if (n == 0) return "コントローラ 未接続（注視で操作）";
            return n == 1 ? "コントローラ 1 台" : $"コントローラ {n} 台";
        }

        /// <summary>デバイス一覧を取り直す。普段はイベント任せで、手動で呼ぶ必要はない。</summary>
        public static void Refresh()
        {
            EnsureHooked();
            RefreshNow();
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// 再生開始のたびに static の状態を初期化する。
        /// Enter Play Mode Options でドメインリロードを切っていると static は残るため、
        /// ここで明示的に畳んでおかないと前回の押下状態を引きずる。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetOnPlay()
        {
            _polledFrame = -1;
            _select = _selectPrev = false;
            _primary = _primaryPrev = false;
            _secondary = _secondaryPrev = false;
            _analogTriggerHeld = false;
            _dirty = true;

            EnsureHooked();
        }

        static void EnsureHooked()
        {
            if (_hooked) return;
            _hooked = true;

            // 二重購読を避けるため、足す前に必ず外す（ドメインリロード無効時の保険）。
            InputDevices.deviceConnected -= OnDeviceChanged;
            InputDevices.deviceConnected += OnDeviceChanged;
            InputDevices.deviceDisconnected -= OnDeviceChanged;
            InputDevices.deviceDisconnected += OnDeviceChanged;
            InputDevices.deviceConfigChanged -= OnDeviceChanged;
            InputDevices.deviceConfigChanged += OnDeviceChanged;

            RefreshNow();
        }

        static void OnDeviceChanged(InputDevice device)
        {
            // イベントの中で取り直すと順序依存でこぼすことがあるので、次の Poll に回す。
            _dirty = true;
        }

        static void RefreshNow()
        {
            _dirty = false;
            Controllers.Clear();

            // XR が動いていない（エディタで HMD 無し等）なら空リストが返るだけで例外にはならない。
            Scratch.Clear();
            InputDevices.GetDevicesWithCharacteristics(ControllerMask, Scratch);

            for (int i = 0; i < Scratch.Count; i++)
            {
                if (Scratch[i].isValid) Controllers.Add(Scratch[i]);
            }
        }

        static void Poll()
        {
            EnsureHooked();

            int frame = Time.frameCount;
            if (_polledFrame == frame) return;
            _polledFrame = frame;

            if (_dirty) RefreshNow();

            _selectPrev = _select;
            _primaryPrev = _primary;
            _secondaryPrev = _secondary;

            bool trigger = false;
            bool primary = false;
            bool secondary = false;
            float analog = 0f;
            bool sawInvalid = false;

            for (int i = 0; i < Controllers.Count; i++)
            {
                InputDevice device = Controllers[i];
                if (!device.isValid)
                {
                    sawInvalid = true;
                    continue;
                }

                // TryGetFeatureValue はその機能を持たない機器では false を返すだけ。投げない。
                if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool t) && t) trigger = true;
                if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool p) && p) primary = true;
                if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool s) && s) secondary = true;

                // triggerButton を持たない機器のためにアナログ値も見ておく。
                if (device.TryGetFeatureValue(CommonUsages.trigger, out float a) && a > analog) analog = a;
            }

            if (sawInvalid) _dirty = true;

            // 行き 0.7 / 帰り 0.4 のヒステリシスで、しきい値付近のばたつきを潰す。
            _analogTriggerHeld = analog > (_analogTriggerHeld ? TriggerRelease : TriggerPress);
            if (_analogTriggerHeld) trigger = true;

            _select = trigger || primary;
            _primary = primary;
            _secondary = secondary;

#if UNITY_EDITOR
            // エディタでの動作確認用フォールバック。実機ビルドには入らない。
            // GetKey（押しっぱなし）で拾い、エッジ検出は上と同じ仕組みに任せる。
            if (UnityEngine.Input.GetKey(KeyCode.Space) || UnityEngine.Input.GetKey(KeyCode.Return)) _select = true;
            if (UnityEngine.Input.GetKey(KeyCode.Alpha1)) { _primary = true; _select = true; }
            if (UnityEngine.Input.GetKey(KeyCode.Alpha2)) { _secondary = true; }
#endif
        }
    }
}
