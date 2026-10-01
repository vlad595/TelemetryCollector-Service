using System;
using TelemetryCollectorService.Core.Interfaces;

namespace TelemetryCollectorService.Core.Models
{
    public class DbMetric: IMetricData
    {
        public Guid instanceId {get;set;}
        public DateTime Timestamp {get;set;}
        public string Environment {get;set;}
        public int ConnectionsCount {get;set;}
        public int SlowQueriesCount {get;set;}
        public double QueryLatency {get;set;}
        public int DeadlocksCount {get;set;}
    }
}