using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using Grpc.Net.Client;
using Livisor.Shared.UnaryServices;
using MagicOnion.Client;

namespace Livisor
{
    public static class ConnectivityCheck
    {
        public const string DefaultServerAddress = "http://57.183.27.205:5210";

        public static async Task<int> CheckServerAsync(
            string address = DefaultServerAddress,
            CancellationToken cancellationToken = default)
        {
            using (var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = new YetAnotherHttpHandler { Http2Only = true },
                DisposeHttpClient = true,
            }))
            {
                var client = MagicOnionClient.Create<IMyFirstService>(channel)
                    .WithDeadline(DateTime.UtcNow.AddSeconds(10))
                    .WithCancellationToken(cancellationToken);
                int result = await client.SumAsync(100, 200);
                if (result != 300)
                    throw new InvalidOperationException($"SumAsync(100, 200): expected 300, got {result}");
                return result;
            }
        }
    }
}
