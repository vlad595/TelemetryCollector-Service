using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Collectors
{
    public class RawAPICollector : IMetricCollctor<APIMetrics>, IDisposable
    {
        public string Name { get; set; } = "Raw API Collector";

        private readonly HttpClient _httpClient;
        private HashSet<string> _prevConnectionEndpoints = new();
        private DateTime _prevSampleTime = DateTime.UtcNow;
        private int _clientErrorCount;
        private int _serverErrorCount;

        private static readonly int[] DefaultApiPorts =
        {
            80, 443, 3000, 5000, 5001, 5050, 7000, 7001, 8000, 8080, 8443
        };

        private static readonly string[] ApiProcessNames =
        {
            "dotnet", "w3wp", "iisexpress", "nginx", "apache2", "httpd", "node", "uvicorn", "gunicorn", "java"
        };

        public RawAPICollector()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(2)
            };
        }

        public bool IsAvailable()
        {
            if (GetListeningApiPorts().Count > 0)
                return true;

            try
            {
                return Process.GetProcesses()
                    .Any(p => ApiProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public APIMetrics Collect()
        {
            HashSet<int> activePorts = GetListeningApiPorts();
            (int connectionCount, int estimatedRps) = MeasureConnectionsAndRps(activePorts);

            int targetPort = activePorts.FirstOrDefault(p => p != 443 && p != 8443);
            if (targetPort == 0 && activePorts.Count > 0)
                targetPort = activePorts.First();

            double latencyMs = ProbeApiPort(targetPort);

            return new APIMetrics
            {
                instanceId = Guid.NewGuid(),
                Timestamp = DateTime.Now,
                Environment = "Prod",
                RPS = estimatedRps,
                Latency = Math.Round(latencyMs, 2),
                ClientErrorCount = _clientErrorCount,
                ServerErrorCount = _serverErrorCount,
                ConnectionCount = connectionCount
            };
        }

        private static HashSet<int> GetListeningApiPorts()
        {
            try
            {
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                return listeners
                    .Select(ep => ep.Port)
                    .Where(port => DefaultApiPorts.Contains(port))
                    .ToHashSet();
            }
            catch
            {
                return new HashSet<int>();
            }
        }

        private (int ConnectionCount, int Rps) MeasureConnectionsAndRps(HashSet<int> activePorts)
        {
            if (activePorts.Count == 0)
                return (0, 0);

            try
            {
                var allConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
                var apiConnections = allConnections
                    .Where(c => activePorts.Contains(c.LocalEndPoint.Port))
                    .ToList();

                int establishedCount = apiConnections.Count(c => c.State == TcpState.Established);

                var currentEndpoints = apiConnections
                    .Select(c => $"{c.RemoteEndPoint}->{c.LocalEndPoint.Port}")
                    .ToHashSet();

                int newConnections = currentEndpoints.Count(ep => !_prevConnectionEndpoints.Contains(ep));

                DateTime now = DateTime.UtcNow;
                double elapsedSec = (now - _prevSampleTime).TotalSeconds;
                int rps = elapsedSec > 0
                    ? (int)Math.Round((newConnections + establishedCount) / Math.Max(elapsedSec, 1.0))
                    : establishedCount;

                _prevConnectionEndpoints = currentEndpoints;
                _prevSampleTime = now;

                return (establishedCount, rps);
            }
            catch
            {
                return (0, 0);
            }
        }

        private double ProbeApiPort(int port)
        {
            if (port == 0)
                return 0.0;

            string scheme = (port == 443 || port == 5001 || port == 7001 || port == 8443) ? "https" : "http";
            string url = $"{scheme}://127.0.0.1:{port}/";

            var sw = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
                sw.Stop();

                int statusCode = (int)response.StatusCode;
                if (statusCode is >= 400 and < 500)
                    _clientErrorCount++;
                else if (statusCode >= 500)
                    _serverErrorCount++;

                return sw.Elapsed.TotalMilliseconds;
            }
            catch
            {
                sw.Stop();
                return MeasureTcpLatency(port);
            }
        }

        private static double MeasureTcpLatency(int port)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                bool connected = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                sw.Stop();
                return connected ? sw.Elapsed.TotalMilliseconds : 0.0;
            }
            catch
            {
                sw.Stop();
                return 0.0;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}