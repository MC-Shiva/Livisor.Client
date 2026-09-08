using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Livisor.Device
{
    /// <summary>
    /// Livisor.Device の再生制御デーモン(player-ctl.py / TCP 9901)へ
    /// コマンドを送るクライアント。
    ///
    /// プロトコルは 1行1リクエストの JSON(改行区切り)。1コマンドごとに
    /// 接続する(デーモンはスケジュールを接続とは別のタスクで持つので、
    /// 送信後すぐ切断してもスケジュールは生きる)。
    ///
    ///   var client = new LivisorDeviceClient("raspberrypi.local", 9901);
    ///   await client.SendStartAsync("10:00:00.000");   // 指定時刻に再生
    ///   await client.SendVolumeAsync(40);              // 即時に音量40%
    ///   await client.SendStopAsync();                  // 即時に停止
    ///
    /// host はIP文字列でも .local 名でもよい。.local 名で接続できない
    /// 環境向けに <see cref="ConnectViaMdnsAsync"/> で IP を先に解決できる。
    /// </summary>
    public class LivisorDeviceClient
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public int TimeoutMs { get; set; } = 4000;

        public LivisorDeviceClient(string host = "raspberrypi.local", int port = 9901)
        {
            Host = host;
            Port = port;
        }

        /// <summary>
        /// mDNS で .local ホスト名を IP に解決し、以後その IP に接続するようにする。
        /// 解決できれば true。Android(Quest) で .local が直接引けない場合に使う。
        /// </summary>
        public Task<bool> ConnectViaMdnsAsync(
            string localHostName = null, int timeoutMs = 3000)
            => ConnectViaMdnsAsync(
                localHostName, timeoutMs, CancellationToken.None);

        public async Task<bool> ConnectViaMdnsAsync(
            string localHostName, int timeoutMs, CancellationToken cancellationToken)
        {
            var name = string.IsNullOrEmpty(localHostName) ? Host : localHostName;
            IPAddress ip = await MdnsResolver.ResolveAsync(
                    name, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            if (ip == null)
            {
                Debug.LogWarning($"[Livisor] mDNS で {name} を解決できなかった");
                return false;
            }
            Host = ip.ToString();
            Debug.Log($"[Livisor] {name} -> {Host}");
            return true;
        }

        // ── コマンド API ─────────────────────────────────────

        /// <summary>指定時刻に(time=null なら即時)再生を開始する。</summary>
        public Task<string> SendStartAsync(string time = null)
            => SendAsync(BuildCommand(time, "\"start\":1"));

        /// <summary>指定時刻に(time=null なら即時)再生を停止する。</summary>
        public Task<string> SendStopAsync(string time = null)
            => SendAsync(BuildCommand(time, "\"stop\":1"));

        /// <summary>指定時刻に(time=null なら即時)絶対音量を 0-100% に設定する。</summary>
        public Task<string> SendVolumeAsync(int percent, string time = null)
        {
            percent = Mathf.Clamp(percent, 0, 100);
            return SendAsync(BuildCommand(time, $"\"volumeChange\":{percent}"));
        }

        /// <summary>再生状態や音量を変えずに Device との疎通を確認する。</summary>
        public Task<string> SendPingAsync()
            => SendPingAsync(CancellationToken.None);

        public async Task<string> SendPingAsync(CancellationToken cancellationToken)
        {
            string json = await SendAsync(
                    BuildCommand(null, "\"ping\":1"), cancellationToken)
                .ConfigureAwait(false);
            var response = ParseSuccessfulResponse(json);
            if (response.result == null
                || response.result.action != "ping"
                || !response.result.pong)
            {
                throw new InvalidOperationException(
                    $"Livisor.Device の ping 応答が不正: {json}");
            }
            return json;
        }

        /// <summary>DateTime を time フィールド書式 "HH:mm:ss.fff" に変換する。</summary>
        public static string FormatTime(DateTime t) => t.ToString("HH:mm:ss.fff");

        static string BuildCommand(string time, string actionBody)
        {
            var sb = new StringBuilder(64);
            sb.Append('{');
            if (!string.IsNullOrEmpty(time))
                sb.Append("\"time\":\"").Append(time).Append("\",");
            sb.Append("\"action\":{").Append(actionBody).Append("}}");
            return sb.ToString();
        }

        // ── 送受信 ───────────────────────────────────────────

        /// <summary>
        /// 1行の JSON を送って1行の成功応答を受け取り、生JSONを返す。
        /// 空応答・不正JSON・ok=falseは例外にする。
        /// </summary>
        public Task<string> SendAsync(string json)
            => SendAsync(json, CancellationToken.None);

        public async Task<string> SendAsync(
            string json, CancellationToken cancellationToken)
        {
            using (var timeoutCts = new CancellationTokenSource(TimeoutMs))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(
                       timeoutCts.Token, cancellationToken))
            using (var tcp = new TcpClient())
            {
                try
                {
                    await ConnectWithTimeout(tcp, cts.Token).ConfigureAwait(false);

                    using (var stream = tcp.GetStream())
                    {
                        byte[] payload = Encoding.UTF8.GetBytes(json + "\n");
                        await stream.WriteAsync(payload, 0, payload.Length, cts.Token)
                            .ConfigureAwait(false);

                        string line = await ReadLine(stream, cts.Token).ConfigureAwait(false);
                        Debug.Log($"[Livisor] {json} -> {line}");
                        ParseSuccessfulResponse(line);
                        return line;
                    }
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Livisor.Device ({Host}:{Port}) への接続/送信がタイムアウトした");
                }
            }
        }

        async Task ConnectWithTimeout(TcpClient tcp, CancellationToken ct)
        {
            var connect = tcp.ConnectAsync(Host, Port);
            var done = await Task.WhenAny(connect, Task.Delay(Timeout.Infinite, ct))
                .ConfigureAwait(false);
            if (done != connect)
                ct.ThrowIfCancellationRequested();
            await connect.ConfigureAwait(false); // 接続例外を伝播
        }

        static async Task<string> ReadLine(NetworkStream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var buf = new byte[256];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(buf.Length)];
            var decoder = Encoding.UTF8.GetDecoder();
            while (true)
            {
                int n = await stream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                if (n <= 0) break; // 相手が切断
                int charCount = decoder.GetChars(buf, 0, n, chars, 0, false);
                for (int i = 0; i < charCount; i++)
                {
                    if (chars[i] == '\n')
                    {
                        if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                            sb.Length--;
                        return sb.ToString();
                    }
                    sb.Append(chars[i]);
                }
            }
            return sb.ToString();
        }

        static DeviceResponseEnvelope ParseSuccessfulResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("Livisor.Device から空の応答が返された");

            DeviceResponseEnvelope response;
            try
            {
                response = JsonUtility.FromJson<DeviceResponseEnvelope>(json);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"Livisor.Device の応答JSONが不正: {json}", e);
            }

            if (response == null || !response.ok)
            {
                string detail = response == null || string.IsNullOrEmpty(response.error)
                    ? json
                    : response.error;
                throw new InvalidOperationException(
                    $"Livisor.Device がエラーを返した: {detail}");
            }

            return response;
        }

        [Serializable]
        sealed class DeviceResponseEnvelope
        {
            public bool ok = false;
            public string error = null;
            public DeviceResponseResult result = null;
        }

        [Serializable]
        sealed class DeviceResponseResult
        {
            public string action = null;
            public bool pong = false;
        }
    }
}
