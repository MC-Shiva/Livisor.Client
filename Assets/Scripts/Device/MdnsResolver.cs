using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Livisor.Device
{
    /// <summary>
    /// 依存ライブラリ無しの最小 mDNS リゾルバ。
    ///
    /// "raspberrypi.local" のような .local ホスト名を、マルチキャスト
    /// (224.0.0.251:5353) に A レコードのクエリを投げて IP に解決する。
    /// クエリには QU ビット(ユニキャスト応答要求)を立てるので、応答は
    /// こちらの送信ポートに直接返る。マルチキャストグループへの JOIN が
    /// 不要になり、受信側の実装が単純になる。
    ///
    /// Android(Meta Quest) では標準の Dns.GetHostAddresses が .local を
    /// 引けないため、この経路で解決する。マルチキャスト送受信には
    /// WifiManager の MulticastLock が必要なので <see cref="AndroidMulticastLock"/>
    /// で確保する。
    /// </summary>
    public static class MdnsResolver
    {
        static readonly IPAddress MdnsGroup = IPAddress.Parse("224.0.0.251");
        const int MdnsPort = 5353;

        /// <summary>
        /// .local ホスト名を IP アドレスに解決する。見つからなければ null。
        /// </summary>
        public static Task<IPAddress> ResolveAsync(string host, int timeoutMs = 3000)
            => ResolveAsync(host, timeoutMs, CancellationToken.None);

        public static async Task<IPAddress> ResolveAsync(
            string host, int timeoutMs, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            cancellationToken.ThrowIfCancellationRequested();

            using (var mcLock = AndroidMulticastLock.Acquire())
            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.Client.SetSocketOption(SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

                byte[] query = BuildAQuery(host);
                try
                {
                    await udp.SendAsync(query, query.Length,
                        new IPEndPoint(MdnsGroup, MdnsPort)).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[mDNS] send failed: {e.Message}");
                    return null;
                }

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    int remain = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remain <= 0) break;

                    var receiveTask = udp.ReceiveAsync();
                    var done = await Task.WhenAny(receiveTask,
                            Task.Delay(remain, cancellationToken))
                        .ConfigureAwait(false);
                    if (done != receiveTask)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        break; // timeout
                    }

                    UdpReceiveResult res;
                    try { res = receiveTask.Result; }
                    catch { break; }

                    IPAddress ip = ParseFirstA(res.Buffer);
                    if (ip != null)
                        return ip;
                    // 別の応答だった場合は残り時間内で受信を続ける
                }
                return null;
            }
        }

        // ── DNS メッセージの組み立て / 解釈 ─────────────────────

        static byte[] BuildAQuery(string host)
        {
            // ヘッダ(12) + QNAME + QTYPE(2) + QCLASS(2)
            var name = host.TrimEnd('.').Split('.');
            int len = 12;
            foreach (var label in name) len += 1 + label.Length;
            len += 1 + 4;

            var buf = new byte[len];
            // ID=0, flags=0(標準クエリ), QDCOUNT=1
            buf[5] = 1;
            int pos = 12;
            foreach (var label in name)
            {
                buf[pos++] = (byte)label.Length;
                foreach (char c in label) buf[pos++] = (byte)c;
            }
            buf[pos++] = 0;            // ルートラベル
            buf[pos++] = 0; buf[pos++] = 1; // QTYPE = A
            // QCLASS = IN(1) に QU ビット(0x8000)を立ててユニキャスト応答を要求
            buf[pos++] = 0x80; buf[pos++] = 0x01;
            return buf;
        }

        /// <summary>応答から最初の A レコードの IP を取り出す。無ければ null。</summary>
        static IPAddress ParseFirstA(byte[] buf)
        {
            try
            {
                if (buf.Length < 12) return null;
                int qd = (buf[4] << 8) | buf[5];
                int an = (buf[6] << 8) | buf[7];
                int pos = 12;

                for (int i = 0; i < qd; i++)
                {
                    SkipName(buf, ref pos);
                    pos += 4; // QTYPE + QCLASS
                }

                for (int i = 0; i < an; i++)
                {
                    SkipName(buf, ref pos);
                    if (pos + 10 > buf.Length) return null;
                    int type = (buf[pos] << 8) | buf[pos + 1];
                    int rdlen = (buf[pos + 8] << 8) | buf[pos + 9];
                    pos += 10;
                    if (type == 1 && rdlen == 4 && pos + 4 <= buf.Length)
                    {
                        return new IPAddress(new[] { buf[pos], buf[pos + 1],
                                                     buf[pos + 2], buf[pos + 3] });
                    }
                    pos += rdlen;
                }
            }
            catch
            {
                // 壊れた/想定外のパケットは無視
            }
            return null;
        }

        /// <summary>DNS 名を読み飛ばす。圧縮ポインタ(0xC0)にも対応。</summary>
        static void SkipName(byte[] buf, ref int pos)
        {
            while (pos < buf.Length)
            {
                int len = buf[pos];
                if (len == 0) { pos += 1; return; }
                if ((len & 0xC0) == 0xC0) { pos += 2; return; } // ポインタで名前終了
                pos += 1 + len;
            }
        }
    }
}
