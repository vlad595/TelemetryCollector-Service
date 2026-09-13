using System;

namespace TelemetryCollectorService.Core.Interfaces
{
    interface IMetricsPublisher
    {
        public void Publish();
    }
}