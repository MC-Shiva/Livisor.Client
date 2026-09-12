using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Livisor.Device;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Livisor.Editor
{
    public sealed class ConnectivityDiagnostics : EditorWindow
    {
        [SerializeField] string serverAddress = ConnectivityCheck.DefaultServerAddress;
        [SerializeField] string deviceHost = "raspberrypi.local";
        [SerializeField] int devicePort = 9901;
        static int running;
        static string lastResult = "未実行";

        [MenuItem("Livisor/Connectivity/Open Diagnostics")]
        public static void Open() => GetWindow<ConnectivityDiagnostics>("Livisor 疎通確認");

        void OnGUI()
        {
            serverAddress = EditorGUILayout.TextField("Server URL", serverAddress);
            deviceHost = EditorGUILayout.TextField("Device mDNS host", deviceHost);
            devicePort = EditorGUILayout.IntField("Device TCP port", devicePort);
            EditorGUILayout.HelpBox("EC2: SumAsync(100, 200) / Device: mDNS → TCP ping\n再生状態・音量は変更しません。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Volatile.Read(ref running) != 0))
                if (GUILayout.Button("疎通確認")) RunFromWindow();
            EditorGUILayout.TextArea(lastResult, GUILayout.MinHeight(160));
        }

        async void RunFromWindow()
        {
            await RunAsync(serverAddress, deviceHost, devicePort);
            Repaint();
        }

        [MenuItem("Livisor/Connectivity/Run Default Checks")]
        public static async void RunDefaults()
            => await RunAsync(ConnectivityCheck.DefaultServerAddress, "raspberrypi.local", 9901);

        // unity run . -- -executeMethod Livisor.Editor.ConnectivityDiagnostics.RunBatch
        // Do not pass -quit: asynchronous checks exit after the report has been written.
        public static async void RunBatch()
        {
            try
            {
                var report = await RunAsync(
                    Argument("-livisorServer", ConnectivityCheck.DefaultServerAddress),
                    Argument("-livisorDeviceHost", "raspberrypi.local"),
                    int.Parse(Argument("-livisorDevicePort", "9901")));
                EditorApplication.Exit(report.success ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(2);
            }
        }

        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index < 0 ? fallback : index + 1 < args.Length
                ? args[index + 1] : throw new ArgumentException($"Missing value: {name}");
        }

        [CliCommand("livisor_check_connectivity", "Check EC2 MagicOnion RPC and Raspberry Pi mDNS/TCP ping",
            MainThreadRequired = false, Tags = new[] { "livisor" })]
        public static async Task<Report> RunAsync(
            [CliArg("server", "MagicOnion server URL")] string server = ConnectivityCheck.DefaultServerAddress,
            [CliArg("host", "Raspberry Pi .local hostname")] string host = "raspberrypi.local",
            [CliArg("port", "Raspberry Pi player TCP port")] int port = 9901)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
                throw new InvalidOperationException("疎通確認は既に実行中です。");
            lastResult = "確認中…";
            var report = new Report
            {
                checkedAtUtc = DateTime.UtcNow.ToString("O"),
                serverAddress = server, deviceHost = host, devicePort = port,
            };
            try
            {
                try
                {
                    report.serverResult = await ConnectivityCheck.CheckServerAsync(server);
                    report.serverSuccess = true;
                }
                catch (Exception e) { report.serverError = e.ToString(); }

                try
                {
                    var client = new LivisorDeviceClient(host, port);
                    if (!await client.ConnectViaMdnsAsync(host, 5000))
                        throw new TimeoutException($"mDNS: {host} を5秒以内に解決できませんでした。同一LAN・ホスト名・Avahiを確認してください。");
                    report.mdnsSuccess = true;
                    report.deviceAddress = client.Host;
                    report.deviceResponse = await client.SendPingAsync();
                    report.deviceSuccess = true;
                }
                catch (Exception e) { report.deviceError = e.ToString(); }

                report.success = report.serverSuccess && report.deviceSuccess;
                lastResult = JsonUtility.ToJson(report, true);
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/connectivity-report.json", lastResult);
                Debug.Log($"[Livisor Connectivity] {lastResult}");
                return report;
            }
            finally { Interlocked.Exchange(ref running, 0); }
        }

        [Serializable]
        public sealed class Report
        {
            public string checkedAtUtc;
            public bool success;
            public string serverAddress;
            public bool serverSuccess;
            public int serverResult;
            public string serverError;
            public string deviceHost;
            public int devicePort;
            public bool mdnsSuccess;
            public string deviceAddress;
            public bool deviceSuccess;
            public string deviceResponse;
            public string deviceError;
        }
    }
}
