using System;

namespace TelemetryCollectorService.Core.Models
{
    class DbMetric: BaseMetric
    {
        public int ConnectionsCount {get;set;}
        public int SlowQueriesCount {get;set;}
        public double QueryLatency {get;set;}
        public int DeadlocksCount {get;set;}
    }
}