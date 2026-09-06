using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Livisor.Device
{
    /// <summary>
    /// Livisor.Device への再生制御の使い方サンプル。
    ///
    /// 使い方:
    ///   1. 空の GameObject にこのコンポーネントを付ける
    ///   2. useMdns にチェック(既定) → 有効化時に mDNS で raspberrypi.local を解決
    ///      した後、副作用のない ping を送り TCP 疎通まで確認する
    ///      うまく引けない場合は useMdns を外して manualIp に IP を直接入れる
    ///   3. UI ボタンの OnClick から PlayNow / StopNow / SetVolume / ScheduleStart を呼ぶ
    /// </summary>
    public class DeviceCommandExample : MonoBehaviour
    {
        [Header("接続先")]
        [Tooltip("mDNS で解決するホスト名(.local)")]
        public string mdnsHostName = "raspberrypi.local";

        [Tooltip("mDNS を使わず IP を直接指定する場合はここに入れる")]
        public string manualIp = "";

        public int port = 9901;

        [Tooltip("有効化時に mDNS でホスト名を解決する")]
        public bool useMdns = true;

        [Tooltip("疎通確認成功後に音量50%を送る(任意・通常はオフ)")]
        public bool pingVolumeOnStart = false;

        LivisorDeviceClient _client;
        Task _initializationTask;
        CancellationTokenSource _connectionCts;
        int _connectionGeneration;

        public bool IsReachable { get; private set; }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            CancelConnection();
            IsReachable = false;
            int generation = ++_connectionGeneration;
            _connectionCts = new CancellationTokenSource();
            _initializationTask = InitializeAsync(
                generation, _connectionCts.Token);
            ObserveInitialization(_initializationTask, generation);
        }

        void OnDisable()
        {
            _connectionGeneration++;
            CancelConnection();
            _initializationTask = null;
            IsReachable = false;
        }

        void CancelConnection()
        {
            var cts = _connectionCts;
            _connectionCts = null;
            if (cts == null)
                return;

            cts.Cancel();
            cts.Dispose();
        }

        async Task InitializeAsync(
            int generation, CancellationToken cancellationToken)
        {
            var client = new LivisorDeviceClient(
                string.IsNullOrEmpty(manualIp) ? mdnsHostName : manualIp, port);
            _client = client;

            if (useMdns && string.IsNullOrEmpty(manualIp))
            {
                bool ok = await client.ConnectViaMdnsAsync(
                    mdnsHostName, 3000, cancellationToken);
                if (!ok)
                    throw new InvalidOperationException(
                        "mDNS 解決に失敗。manualIp を設定してください");
            }

            if (!IsCurrentConnection(generation))
                return;

            await client.SendPingAsync(cancellationToken);

            if (!IsCurrentConnection(generation))
                return;

            IsReachable = true;
            Debug.Log($"[Livisor] Device 疎通確認成功: {client.Host}:{client.Port}");

            if (pingVolumeOnStart)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await client.SendVolumeAsync(50);
            }
        }

        async void ObserveInitialization(Task initialization, int generation)
        {
            try
            {
                await initialization;
            }
            catch (OperationCanceledException)
            {
                // GameObjectが無効化された場合は正常な中断として扱う。
            }
            catch (Exception e)
            {
                if (!IsCurrentConnection(generation))
                    return;

                IsReachable = false;
                Debug.LogError($"[Livisor] Device 疎通確認失敗: {e.Message}");
                Debug.LogException(e);
            }
        }

        bool IsCurrentConnection(int generation)
            => generation == _connectionGeneration && isActiveAndEnabled;

        // ── UI から呼ぶ用(即時) ─────────────────────────────

        public async void PlayNow()
            => await SafeSend(client => client.SendStartAsync());

        public async void StopNow()
            => await SafeSend(client => client.SendStopAsync());

        public async void SetVolume(int percent)
            => await SafeSend(client => client.SendVolumeAsync(percent));

        /// <summary>UI の Slider(0-1) から直接つなぐ用。</summary>
        public async void SetVolumeNormalized(float value)
            => await SafeSend(client =>
                client.SendVolumeAsync(Mathf.RoundToInt(value * 100f)));

        // ── 時刻指定(例: N 秒後に再生開始) ──────────────────

        public async void ScheduleStartInSeconds(float seconds)
        {
            string time = LivisorDeviceClient.FormatTime(DateTime.Now.AddSeconds(seconds));
            await SafeSend(client => client.SendStartAsync(time));
        }

        /// <summary>"HH:mm:ss.fff" を直接指定して再生をスケジュールする。</summary>
        public async void ScheduleStartAt(string time)
            => await SafeSend(client => client.SendStartAsync(time));

        async Task SafeSend(Func<LivisorDeviceClient, Task<string>> send)
        {
            try
            {
                int generation = _connectionGeneration;
                var client = _client;
                var initialization = _initializationTask;
                if (initialization == null)
                    throw new InvalidOperationException(
                        "Device接続GameObjectが有効ではありません");

                await initialization;
                if (!IsCurrentConnection(generation) || client != _client)
                    throw new OperationCanceledException();

                if (!IsReachable || client == null)
                    throw new InvalidOperationException("Deviceとの接続確認が完了していません");

                await send(client);
            }
            catch (OperationCanceledException)
            {
                // 無効化された世代からの操作は送らない。
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
