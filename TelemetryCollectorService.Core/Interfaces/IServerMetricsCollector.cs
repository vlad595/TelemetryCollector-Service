using System;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Interfaces
{
    interface IServerMetricsCollector
    {
        public MachineMetrics Collect();
    }
}