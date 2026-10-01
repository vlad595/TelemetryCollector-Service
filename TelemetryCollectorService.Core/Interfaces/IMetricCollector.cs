using System;

namespace TelemetryCollectorService.Core.Interfaces
{
    public interface IMetricCollctor<out T> where T: IMetricData
    {
        string Name {get;set;}
        bool IsAvailable();
        T Collect();
    }
}