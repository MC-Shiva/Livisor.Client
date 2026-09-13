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

        [Tooltip("無効化・シーン終了時に、送信中のコマンドを待ってから停止を送る")]
        public bool stopOnDisable = false;

        LivisorDeviceClient _client;
        Task _initializationTask;
        CancellationTokenSource _connectionCts;
        int _connectionGeneration;
        Task _sendTail = Task.CompletedTask;
        bool? _lastAppliedPlaying;

        public int SuccessfulCommandCount { get; private set; }
        public string LastResponse { get; private set; }
        public string LastError { get; private set; }

        public bool IsReachable { get; private set; }
        public string ResolvedHost => _client?.Host;

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            CancelConnection();
            IsReachable = false;
            _lastAppliedPlaying = null;
            LastError = null;
            int generation = ++_connectionGeneration;
            _connectionCts = new CancellationTokenSource();
            _initializationTask = InitializeAsync(
                generation, _connectionCts.Token);
            ObserveInitialization(_initializationTask, generation);
        }

        void OnDisable()
        {
            var client = stopOnDisable && IsReachable ? _client : null;
            _connectionGeneration++;
            CancelConnection();
            _initializationTask = null;
            IsReachable = false;
            if (client != null)
                _sendTail = StopAfterAsync(_sendTail, client);
        }

        static async Task StopAfterAsync(Task previous, LivisorDeviceClient client)
        {
            try
            {
                await previous;
                await client.SendStopAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
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
                LastError = e.Message;
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

        // TransportState is a snapshot: repeated playing=true must not restart the Pi's file.
        public async void ApplyPlaying(bool playing)
            => await SafeSend(async client =>
            {
                if (_lastAppliedPlaying == playing) return null;
                string response = playing ? await client.SendStartAsync() : await client.SendStopAsync();
                _lastAppliedPlaying = playing;
                return response;
            });

        public Task StopAndWaitAsync() => SafeSend(async client =>
        {
            string response = await client.SendStopAsync();
            _lastAppliedPlaying = false;
            return response;
        });

        public Task WaitForCommandsAsync() => _sendTail;

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

        Task SafeSend(Func<LivisorDeviceClient, Task<string>> send)
        {
            // Preserve UI/transport order even when initialization or an earlier TCP reply is pending.
            var pending = SendAfterAsync(_sendTail, send);
            _sendTail = pending;
            return pending;
        }

        async Task SendAfterAsync(Task previous, Func<LivisorDeviceClient, Task<string>> send)
        {
            try
            {
                int generation = _connectionGeneration;
                var client = _client;
                var initialization = _initializationTask;
                await previous;
                if (initialization == null)
                    throw new InvalidOperationException(
                        "Device接続GameObjectが有効ではありません");

                await initialization;
                if (!IsCurrentConnection(generation) || client != _client)
                    throw new OperationCanceledException();

                if (!IsReachable || client == null)
                    throw new InvalidOperationException("Deviceとの接続確認が完了していません");

                string response = await send(client);
                if (response != null)
                {
                    LastResponse = response;
                    LastError = null;
                    SuccessfulCommandCount++;
                }
            }
            catch (OperationCanceledException)
            {
                // 無効化された世代からの操作は送らない。
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogException(e);
            }
        }
    }
}
