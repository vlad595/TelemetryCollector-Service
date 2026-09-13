using System;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Interfaces
{
    interface IApiMetricsCollector
    {
        public APIMetrics Collect();
    }
}