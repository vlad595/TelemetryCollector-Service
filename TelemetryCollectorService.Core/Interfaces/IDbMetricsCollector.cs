using System;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Interfaces
{
    interface IDbMetricsCollector
    {
        public DbMetric Collect();
    }
}