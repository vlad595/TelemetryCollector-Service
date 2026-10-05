using System;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;
using System.Management;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TelemetryCollectorService.Core.Collectors
{
    public class CpuDto
    {
        public string Name {get;set;}
        public int NumberOfCores {get;set;}
        public int NumberOfLogicalProcessors {get;set;}
        public List<CoreLoad> CoreLoads {get;set;}
    }
    public class WindowsOsCollector: IMetricCollctor<MachineMetrics>
    {
        public string Name {get; set;} = "OS Windows";
        private string CpuName {get;set;}
        private List<PerformanceCounter> _coreCounters;
        private Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> _networkCounters;
        public WindowsOsCollector()
        {
            CpuName = GetCpuName();
            _coreCounters = InitPerformanceCounters();
            _networkCounters = InitNetAdaptersCounters();
        }
        public bool IsAvailable()
        {
            return true;
        }
        public MachineMetrics Collect()
        {
            Dictionary<string, ulong> ramMetrics = CollectRamMetrics();
            float ramLoad = (float)ramMetrics["usedMemory"] / ramMetrics["totalMemory"];

            Dictionary<string, float> networkTraffic = CollectTxRx();
            return new MachineMetrics
            {
                instanceId = Guid.NewGuid(),
                Timestamp = DateTime.Now,
                Environment = "Prod",
                RamLoad = (float)Math.Round(ramLoad, 2),
                TotalRam = Convert.ToInt32(ramMetrics["totalMemory"]),
                CpuName = CpuName,
                CpuCoreLoads = GetCoreLoads(),
                WiFiRx = networkTraffic["RxTraffic"],
                WiFiTx = networkTraffic["TxTraffic"]
            };
        }
        private Dictionary<string, ulong> CollectRamMetrics()
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
            Dictionary<string, ulong> result = new Dictionary<string, ulong>();
            result.Add("totalMemory", totalMemory);
            result.Add("freeMemory", freeMemory);
            result.Add("usedMemory", usedMemory);
            return result;
        }
        private string GetCpuName()
        {
            string query = "SELECT Name FROM Win32_Processor";
            using (var searcher = new ManagementObjectSearcher(query))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    return mo["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                }
            }
            return "Unknown CPU";
        }
        private List<CoreLoad> GetCoreLoads()
        {
            List<CoreLoad> coreLoads = new();

            foreach (var counter in _coreCounters)
            {
                float load = counter.NextValue();
                coreLoads.Add(new CoreLoad(){ CoreId = counter.InstanceName, Load = (float)Math.Round(load / 100f, 2) });
            }
            return coreLoads;
        }
        private List<PerformanceCounter> InitPerformanceCounters()
        {
            List<PerformanceCounter> _coreCounters = new();

            var category = new PerformanceCounterCategory("Processor");

            string[] instances = category.GetInstanceNames();

            foreach (var instance in instances)
            {
                if(instance.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var counter = new PerformanceCounter("Processor", "% Processor Time", instance);
                counter.NextValue();
                _coreCounters.Add(counter);
            }

            return _coreCounters;
        }
        private Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> InitNetAdaptersCounters()
        {
            var category = new PerformanceCounterCategory("Network Interface");
            string[] instances = category.GetInstanceNames();

            Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> counters = new();

            foreach (var instance in instances)
            {
                if (instance.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var rxCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);
                var txCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);

                rxCounter.NextValue();
                txCounter.NextValue();

                counters.Add(instance, (rxCounter, txCounter));
            }
            return counters;
        }
        private Dictionary<string, float> CollectTxRx()
        {
            float rx = 0f;
            float tx = 0f;

            Dictionary<string, float> values = new();
            foreach(var counter in _networkCounters)
            {
                rx += counter.Value.Rx.NextValue();
                tx += counter.Value.Tx.NextValue();
            }
            values.Add("TxTraffic", tx);
            values.Add("RxTraffic", rx);
            return values;
        }
        private Dictionary<string, string> CollectDiskMetrics()
        {
            return null;
        }
    }
}