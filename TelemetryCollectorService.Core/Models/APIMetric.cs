using System;

namespace TelemetryCollectorService.Core.Models
{
    class APIMetrics: BaseMetric
    {
        public int RPS {get;set;}
        public double Latency {get;set;}
        public int ClientErrorCount {get;set;}
        public int ServerErrorCount {get;set;}
        public int ConnectionCount {get;set;}
    }
}