using System;
using System.Data;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;
using Npgsql;

namespace TelemetryCollectorService.Core.Collectors
{
    public class PostgresCollector : IMetricCollctor<DbMetric>, IDisposable
    {
        public string Name { get; set; } = "PostgreSQL Collector";

        private readonly string _connectionString;
        private NpgsqlConnection _persistentConnection;

        private bool _isPortAvailable;
        private int _activePort;
        private bool _sqlAuthFailed;
        private DateTime _lastAvailabilityCheck = DateTime.MinValue;
        private static readonly TimeSpan BackoffTime = TimeSpan.FromSeconds(30);

        private const string MetricSql = @"
            SELECT
                (SELECT count(*)::int FROM pg_stat_activity WHERE pid <> pg_backend_pid()),
                (SELECT count(*)::int FROM pg_stat_activity WHERE state = 'active' AND pid <> pg_backend_pid() AND now() - query_start > interval '1 second'),
                (SELECT COALESCE(sum(deadlocks), 0)::int FROM pg_stat_database);";

        public PostgresCollector(string connectionString = null)
        {
            _connectionString = connectionString
                ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
                ?? "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1;Pooling=true";
        }

        public bool IsAvailable()
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastAvailabilityCheck >= BackoffTime)
            {
                _activePort = DetectPostgresPort();
                _isPortAvailable = _activePort > 0;
                _lastAvailabilityCheck = now;

                // Раз на 30 секунд дозволяємо повторну спробу SQL-підключення, якщо раніше воно впало
                if (_isPortAvailable && _sqlAuthFailed)
                    _sqlAuthFailed = false;
            }

            return _isPortAvailable;
        }

        public DbMetric Collect()
        {
            if (!IsAvailable())
                return null;

            if (!_sqlAuthFailed && TryCollectViaSql(out DbMetric sqlMetric))
            {
                return sqlMetric;
            }

            int tcpConnections = CountConnectionsOnPort(_activePort);
            double tcpLatency = MeasureLocalTcpLatency(_activePort);

            return new DbMetric
            {
                instanceId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Environment = "Prod",
                ConnectionsCount = tcpConnections,
                SlowQueriesCount = 0,
                QueryLatency = Math.Round(tcpLatency, 2),
                DeadlocksCount = 0
            };
        }

        private bool TryCollectViaSql(out DbMetric metric)
        {
            metric = null;
            try
            {
                if (_persistentConnection == null)
                    _persistentConnection = new NpgsqlConnection(_connectionString);

                if (_persistentConnection.State != ConnectionState.Open)
                    _persistentConnection.Open();

                var sw = Stopwatch.StartNew();
                using var cmd = new NpgsqlCommand(MetricSql, _persistentConnection);
                using var reader = cmd.ExecuteReader();
                sw.Stop();

                if (reader.Read())
                {
                    metric = new DbMetric
                    {
                        instanceId = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Environment = "Prod",
                        ConnectionsCount = reader.GetInt32(0),
                        SlowQueriesCount = reader.GetInt32(1),
                        DeadlocksCount = reader.GetInt32(2),
                        QueryLatency = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
                    };
                    return true;
                }
            }
            catch
            {
                _sqlAuthFailed = true;
                ClosePersistentConnection();
            }

            return false;
        }

        private static int DetectPostgresPort()
        {
            try
            {
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                for (int i = 0; i < listeners.Length; i++)
                {
                    int port = listeners[i].Port;
                    if (port == 5432 || port == 5433)
                        return port;
                }
            }
            catch { }

            return 0;
        }

        private static int CountConnectionsOnPort(int port)
        {
            if (port <= 0)
                return 0;

            try
            {
                var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
                int count = 0;
                for (int i = 0; i < connections.Length; i++)
                {
                    if (connections[i].State == TcpState.Established &&
                        connections[i].LocalEndPoint.Port == port)
                    {
                        count++;
                    }
                }
                return count;
            }
            catch
            {
                return 0;
            }
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

        private void ClosePersistentConnection()
        {
            try { _persistentConnection?.Close(); } catch { }
            try { _persistentConnection?.Dispose(); } catch { }
            _persistentConnection = null;
        }

        public void Dispose()
        {
            ClosePersistentConnection();
        }
    }
}