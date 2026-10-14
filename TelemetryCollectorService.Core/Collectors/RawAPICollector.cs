using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Collectors
{
    public class RawAPICollector : IMetricCollctor<APIMetrics>
    {
        public string Name { get; set; } = "Raw API Collector";

        private int _cachedPort;
        private DateTime _lastPortScan = DateTime.MinValue;
        private static readonly TimeSpan PortScanInterval = TimeSpan.FromSeconds(30);

        private int _prevConnections;
        private DateTime _prevSampleTime = DateTime.UtcNow;

        private static readonly int[] CandidatePorts =
        {
            80, 443, 3000, 5000, 5001, 5050, 7000, 7001, 8000, 8080, 8443
        };

        public bool IsAvailable()
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastPortScan >= PortScanInterval)
            {
                _cachedPort = DetectActivePort();
                _lastPortScan = now;
            }

            return _cachedPort > 0;
        }

        public APIMetrics Collect()
        {
            if (!IsAvailable())
                return null;

            int activeConnections = 0;
            try
            {
                var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
                for (int i = 0; i < connections.Length; i++)
                {
                    if (connections[i].State == TcpState.Established &&
                        connections[i].LocalEndPoint.Port == _cachedPort)
                    {
                        activeConnections++;
                    }
                }
            }
            catch
            {
                _cachedPort = 0;
                return null;
            }

            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _prevSampleTime).TotalSeconds;
            int newConns = Math.Max(0, activeConnections - _prevConnections);
            int estimatedRps = elapsed > 0
                ? (int)Math.Round((newConns + activeConnections) / Math.Max(elapsed, 1.0))
                : activeConnections;

            _prevConnections = activeConnections;
            _prevSampleTime = now;

            double latencyMs = MeasureLocalTcpLatency(_cachedPort);

            return new APIMetrics
            {
                instanceId = Guid.NewGuid(),
                Timestamp = now,
                Environment = "Prod",
                RPS = estimatedRps,
                Latency = Math.Round(latencyMs, 2),
                ClientErrorCount = 0,
                ServerErrorCount = 0,
                ConnectionCount = activeConnections
            };
        }

        private static int DetectActivePort()
        {
            try
            {
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                for (int i = 0; i < listeners.Length; i++)
                {
                    int port = listeners[i].Port;
                    for (int j = 0; j < CandidatePorts.Length; j++)
                    {
                        if (port == CandidatePorts[j])
                            return port;
                    }
                }
            }
            catch { }

            return 0;
        }

        private static double MeasureLocalTcpLatency(int port)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Blocking = true;
                socket.Connect("127.0.0.1", port);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds;
            }
            catch
            {
                sw.Stop();
                return 0.0;
            }
        }
    }
}