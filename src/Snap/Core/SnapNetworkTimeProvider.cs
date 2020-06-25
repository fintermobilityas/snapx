using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Snap.Extensions;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapNetworkTimeProvider
    {
        string Server { get; }
        int Port { get; }
        Task<DateTime?> NowUtcAsync(TimeSpan timeout = default);
        Task<DateTime?> NowUtcAsync(string server, int port = 123, TimeSpan timeout = default);
        DateTime? NowUtc(TimeSpan timeout = default);
        DateTime? NowUtc(string server, int port = 123, TimeSpan timeout = default);
    }

    internal sealed class SnapNetworkTimeProvider : ISnapNetworkTimeProvider
    {
        public string Server { get; }
        public int Port { get; }

        public SnapNetworkTimeProvider(string ntpServer, int port)
        {
            if (string.IsNullOrWhiteSpace(ntpServer)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(ntpServer));
            if (port <= 0) throw new ArgumentOutOfRangeException(nameof(port));
            Server = ntpServer;
            Port = port;
        }

        public Task<DateTime?> NowUtcAsync(TimeSpan timeout = default)
        {
            return NowImpl(Server, Port, timeout);
        }

        public Task<DateTime?> NowUtcAsync(string server, int port = 123, TimeSpan timeout = default)
        {
            return NowImpl(server, port, timeout);
        }

        public DateTime? NowUtc(string server, int port = 123, TimeSpan timeout = default)
        {
            return TplHelper.RunSync(() => NowUtcAsync(server, port, timeout));
        }

        public DateTime? NowUtc(TimeSpan timeout = default)
        {
            return TplHelper.RunSync(() => NowUtcAsync(timeout));
        }

        static Task<DateTime?> NowImpl(string ntpServer, int port, TimeSpan timeout)
        {
            if (port <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "Must be greater than zero.");
            }
            if (timeout == default || timeout <= TimeSpan.MinValue)
            {
                timeout = TimeSpan.FromSeconds(10);
            }
            var tsc = new TaskCompletionSource<DateTime?>();
            ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    var ntpData = new byte[48];
                    ntpData[0] = 0x1B; //LeapIndicator = 0 (no warning), VersionNum = 3 (IPv4 only), Mode = 3 (Client Mode)

                    var addresses = Dns.GetHostAddresses(ntpServer);
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                    {
                        ReceiveTimeout = (int)timeout.TotalSeconds * 1000,
                        SendTimeout = (int)timeout.TotalSeconds * 1000
                    };

                    socket.Connect(addresses, port);
                    socket.Send(ntpData);
                    socket.Receive(ntpData);
                    socket.Close();

                    var intPart = (ulong)ntpData[40] << 24 | (ulong)ntpData[41] << 16 | (ulong)ntpData[42] << 8 | ntpData[43];
                    var fractPart = (ulong)ntpData[44] << 24 | (ulong)ntpData[45] << 16 | (ulong)ntpData[46] << 8 | ntpData[47];

                    var milliseconds = intPart * 1000 + fractPart * 1000 / 0x100000000L;
                    var networkDateTime = new DateTime(1900, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds((long)milliseconds);

                    tsc.TrySetResult(networkDateTime);
                }
                catch
                {
                    tsc.TrySetResult(null);
                }
            });

            return tsc.Task;
        }

        public override string ToString()
        {
            return $"{Server}:{Port}";
        }
    }
}
