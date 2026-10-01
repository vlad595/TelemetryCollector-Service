using System;
using System.Runtime.InteropServices;
using TelemetryCollectorService.Core.Collectors;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Agent.Services
{
    public class MachineMetricsService
    {
        private WindowsOsCollector collector = new WindowsOsCollector();
        public MachineMetricsService()
        {
            collector.Name = "";
        }
        public MachineMetrics Collect()
        {
            return collector.Collect();
        }
    }
}