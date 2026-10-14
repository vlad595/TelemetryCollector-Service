using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;
using Npgsql;

namespace TelemetryCollectorService.Core.Collectors
{
    public class PostgresCollector : IMetricCollctor<DbMetric>
    {
        public string Name { get; set; } = "PostgreSQL Collector";

        private Guid instanceId;

        private readonly string _connectionString;
        private static readonly int[] PostgresPorts = { 5432, 5433 };
        private static readonly string[] PostgresProcessNames = { "postgres", "postmaster", "pg_ctl" };

        public PostgresCollector(string connectionString = null)
        {
            this.instanceId = Guid.NewGuid();
            _connectionString = connectionString
                ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
                ?? "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres;Timeout=2;Command Timeout=2";
        }

        public bool IsAvailable()
        {
            if (GetActivePostgresPort() > 0)
                return true;

            try
            {
                return Process.GetProcesses()
                    .Any(p => PostgresProcessNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public DbMetric Collect()
        {
            if (TryCollectFromPgStat(out int connections, out int slowQueries, out int deadlocks, out double sqlLatencyMs))
            {
                return new DbMetric
                {
                    instanceId = Guid.NewGuid(),
                    Timestamp = DateTime.Now,
                    Environment = "Prod",
                    ConnectionsCount = connections,
                    SlowQueriesCount = slowQueries,
                    QueryLatency = Math.Round(sqlLatencyMs, 2),
                    DeadlocksCount = deadlocks
                };
            }

            int activePort = GetActivePostgresPort();
            int tcpConnections = CountTcpConnectionsOnPort(activePort);
            double tcpLatencyMs = activePort > 0 ? MeasureTcpLatency(activePort) : 0.0;

            return new DbMetric
            {
                instanceId = Guid.NewGuid(),
                Timestamp = DateTime.Now,
                Environment = "Prod",
                ConnectionsCount = tcpConnections,
                SlowQueriesCount = 0,
                QueryLatency = Math.Round(tcpLatencyMs, 2),
                DeadlocksCount = 0
            };
        }

        private bool TryCollectFromPgStat(out int connections, out int slowQueries, out int deadlocks, out double latencyMs)
        {
            connections = 0;
            slowQueries = 0;
            deadlocks = 0;
            latencyMs = 0.0;

            const string query = @"
                SELECT
                    (SELECT count(*)::int FROM pg_stat_activity WHERE pid <> pg_backend_pid()),
                    (SELECT count(*)::int FROM pg_stat_activity WHERE state = 'active' AND pid <> pg_backend_pid() AND now() - query_start > interval '1 second'),
                    (SELECT COALESCE(sum(deadlocks), 0)::int FROM pg_stat_database);";

            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                conn.Open();

                var sw = Stopwatch.StartNew();
                using var cmd = new NpgsqlCommand(query, conn);
                using var reader = cmd.ExecuteReader();
                sw.Stop();

                if (reader.Read())
                {
                    connections = reader.GetInt32(0);
                    slowQueries = reader.GetInt32(1);
                    deadlocks = reader.GetInt32(2);
                    latencyMs = sw.Elapsed.TotalMilliseconds;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static int GetActivePostgresPort()
        {
            try
            {
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                return listeners
                    .Select(ep => ep.Port)
                    .FirstOrDefault(port => PostgresPorts.Contains(port));
            }
            catch
            {
                return 0;
            }
        }

        private static int CountTcpConnectionsOnPort(int port)
        {
            if (port <= 0)
                return 0;

            try
            {
                var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
                int rawCount = connections.Count(c =>
                    c.State == TcpState.Established &&
                    c.LocalEndPoint.Port == port);

                return rawCount;
            }
            catch
            {
                return 0;
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
    }
}