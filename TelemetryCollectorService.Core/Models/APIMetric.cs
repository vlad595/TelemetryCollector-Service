using System;
using TelemetryCollectorService.Core.Interfaces;

namespace TelemetryCollectorService.Core.Models
{
    public class APIMetrics : IMetricData
    {
        public Guid instanceId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Environment { get; set; }
        public int RPS { get; set; }
        public double Latency { get; set; }
        public int ClientErrorCount { get; set; }
        public int ServerErrorCount { get; set; }
        public int ConnectionCount { get; set; }

        public override string ToString()
        {
            return $"{this.instanceId} | {this.Timestamp} | {this.Environment} | RPS: {this.RPS} | Latency: {this.Latency}ms | 4xx: {this.ClientErrorCount} | 5xx: {this.ServerErrorCount} | Connections: {this.ConnectionCount}";
        }
    }
}