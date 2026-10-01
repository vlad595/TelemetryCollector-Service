using System;
using TelemetryCollectorService.Core.Interfaces;

namespace TelemetryCollectorService.Core.Models
{
    public class APIMetrics: IMetricData
    {
        public Guid instanceId {get;set;}
        public DateTime Timestamp {get;set;}
        public string Environment {get;set;}
        public int RPS {get;set;}
        public double Latency {get;set;}
        public int ClientErrorCount {get;set;}
        public int ServerErrorCount {get;set;}
        public int ConnectionCount {get;set;}
    }
}