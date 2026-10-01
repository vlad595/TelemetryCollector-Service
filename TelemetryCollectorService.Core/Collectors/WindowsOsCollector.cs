using System;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;
using System.Management;

namespace TelemetryCollectorService.Core.Collectors
{
    public class WindowsOsCollector: IMetricCollctor<MachineMetrics>
    {
        public string Name {get; set;} = "OS Windows";
        public bool IsAvailable()
        {
            return true;
        }
        public MachineMetrics Collect()
        {
            Dictionary<string, ulong> ramMetrics = CollectRamMetrics();
            float ramLoad = (float)ramMetrics["usedMemory"] / ramMetrics["totalMemory"];
            return new MachineMetrics
            {
                instanceId = Guid.NewGuid(),
                Timestamp = DateTime.Now,
                Environment = "Prod",
                RamLoad = ramLoad,
            };
        }
        public Dictionary<string, ulong> CollectRamMetrics()
        {
            ulong totalMemory = 0;
            ulong freeMemory = 0;
            ulong usedMemory = 0;

            string ramQuery = "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem";
            var ramResearcher = new ManagementObjectSearcher(ramQuery);
            foreach (ManagementObject mo in ramResearcher.Get())
            {
                totalMemory = Convert.ToUInt64(mo["TotalVisibleMemorySize"]) / 1024;
                freeMemory = Convert.ToUInt64(mo["FreePhysicalMemory"]) / 1024;
                usedMemory = totalMemory - freeMemory;
            }
            Dictionary<string, ulong> result = new Dictionary<string, ulong> {};
            result.Add("totalMemory", totalMemory);
            result.Add("freeMemory", freeMemory);
            result.Add("usedMemory", usedMemory);
            return result;
        }
    }
}