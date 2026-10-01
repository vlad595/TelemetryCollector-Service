using System;

namespace TelemetryCollectorService.Core.Interfaces
{
    public interface IMetricData
    {
        public Guid instanceId {get;set;}
        public DateTime Timestamp {get;set;}
        public string Environment {get;set;}
        public string ToString();
    }
}