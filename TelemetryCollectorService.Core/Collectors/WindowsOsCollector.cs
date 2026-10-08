using System;
using System.Collections.Generic;
using System.Linq;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;
using System.Management;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace TelemetryCollectorService.Core.Collectors
{
    [SupportedOSPlatform("Windows")]
    public class WindowsOsCollector : IMetricCollctor<MachineMetrics>, IDisposable
    {
        public string Name { get; set; } = "OS Windows";
        
        private string CpuName { get; set; }
        private string GpuName { get; set; }

        private List<PerformanceCounter> _coreCounters;
        private Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> _networkCounters;
        private Dictionary<string, (PerformanceCounter readingSpeed, PerformanceCounter writingSpeed)> _diskCounters;
        
        // Словники для GPU
        private Dictionary<string, PerformanceCounter> _gpuLoadCounters = new();
        private Dictionary<string, PerformanceCounter> _gpuRamCounters = new();

        public WindowsOsCollector()
        {
            CpuName = GetCpuName();
            GpuName = GetGpuName();
            
            _coreCounters = InitPerformanceCounters();
            _networkCounters = InitNetAdaptersCounters();
            _diskCounters = InitDiskCounters();
            InitGpuRamCounters();
        }

        public bool IsAvailable() => true;

        public MachineMetrics Collect()
        {
            Dictionary<string, ulong> ramMetrics = CollectRamMetrics();
            float ramLoad = (float)ramMetrics["usedMemory"] / ramMetrics["totalMemory"];

            Dictionary<string, float> networkTraffic = CollectTxRx();
            Dictionary<string, float> diskTraffic = CollectDiskMetrics();

            var diskInf = GetDiskCapacityAndFreeSpace();
            
            return new MachineMetrics
            {
                instanceId = Guid.NewGuid(),
                Timestamp = DateTime.Now,
                Environment = "Prod",
                RamLoad = (float)Math.Round(ramLoad, 2),
                TotalRam = Convert.ToInt32(ramMetrics["totalMemory"]),
                
                CpuName = CpuName,
                CpuCoreLoads = GetCoreLoads(),
                
                GpuName = GpuName,
                GpuLoad = CollectGpuLoad(),
                GpuRamUsedMb = CollectGpuRamUsed(),
                
                WiFiRx = networkTraffic["RxTraffic"],
                WiFiTx = networkTraffic["TxTraffic"],
                ReadingSpeed = diskTraffic["ReadingSpeed"],
                WritingSpeed = diskTraffic["WritingSpeed"],
                DiskCapacity = diskInf.TotalCapacity,
                FreeDiskSpace = diskInf.FreeSpace
            };
        }
        
        private string GetGpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject mo in searcher.Get())
                {
                    return mo["Name"]?.ToString()?.Trim() ?? "Unknown GPU";
                }
            }
            catch { }
            return "Unknown GPU";
        }

        private void InitGpuRamCounters()
        {
            try
            {
                var category = new PerformanceCounterCategory("GPU Adapter Memory");
                foreach (var instance in category.GetInstanceNames())
                {
                    var counter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", instance);
                    counter.NextValue();
                    _gpuRamCounters.Add(instance, counter);
                }
            }
            catch { }
        }

        private float CollectGpuRamUsed()
        {
            float totalBytes = 0f;
            foreach (var counter in _gpuRamCounters.Values)
            {
                try { totalBytes += counter.NextValue(); } catch { }
            }
            return (float)Math.Round(totalBytes / 1024f / 1024f, 2);
        }

        private float CollectGpuLoad()
        {
            float totalLoad = 0f;
            try
            {
                var category = new PerformanceCounterCategory("GPU Engine");
                var currentInstances = category.GetInstanceNames()
                    .Where(i => i.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var deadInstances = _gpuLoadCounters.Keys.Except(currentInstances).ToList();
                foreach (var dead in deadInstances)
                {
                    _gpuLoadCounters[dead].Dispose();
                    _gpuLoadCounters.Remove(dead);
                }

                foreach (var instance in currentInstances)
                {
                    if (!_gpuLoadCounters.ContainsKey(instance))
                    {
                        var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                        counter.NextValue(); // Ініціалізаційний виклик
                        _gpuLoadCounters.Add(instance, counter);
                    }
                    else
                    {
                        totalLoad += _gpuLoadCounters[instance].NextValue();
                    }
                }
            }
            catch { }

            return totalLoad > 100f ? 100f : (float)Math.Round(totalLoad, 2);
        }

        private Dictionary<string, ulong> CollectRamMetrics()
        {
            ulong totalMemory = 0;
            ulong freeMemory = 0;
            ulong usedMemory = 0;

            string ramQuery = "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem";
            using (var ramResearcher = new ManagementObjectSearcher(ramQuery))
            {
                foreach (ManagementObject mo in ramResearcher.Get())
                {
                    totalMemory = Convert.ToUInt64(mo["TotalVisibleMemorySize"]) / 1024;
                    freeMemory = Convert.ToUInt64(mo["FreePhysicalMemory"]) / 1024;
                    usedMemory = totalMemory - freeMemory;
                }
            }
            return new Dictionary<string, ulong> { { "totalMemory", totalMemory }, { "freeMemory", freeMemory }, { "usedMemory", usedMemory } };
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

        private (float TotalCapacity, float FreeSpace) GetDiskCapacityAndFreeSpace()
        {
            long totalBytes = 0;
            long freeBytes = 0;

            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == System.IO.DriveType.Fixed)
                {
                    totalBytes += drive.TotalSize;
                    freeBytes += drive.AvailableFreeSpace;
                }
            }

            float totalGb = (float)Math.Round(totalBytes / 1024f / 1024f / 1024f, 2);
            float freeGb = (float)Math.Round(freeBytes / 1024f / 1024f / 1024f, 2);
            return (totalGb, freeGb);
        }

        private List<CoreLoad> GetCoreLoads()
        {
            List<CoreLoad> coreLoads = new();
            foreach (var counter in _coreCounters)
            {
                float load = counter.NextValue();
                coreLoads.Add(new CoreLoad() { CoreId = counter.InstanceName, Load = (float)Math.Round(load / 100f, 2) });
            }
            return coreLoads;
        }

        private List<PerformanceCounter> InitPerformanceCounters()
        {
            List<PerformanceCounter> counters = new();
            var category = new PerformanceCounterCategory("Processor");
            foreach (var instance in category.GetInstanceNames())
            {
                if (instance.Equals("_Total", StringComparison.OrdinalIgnoreCase)) continue;
                var counter = new PerformanceCounter("Processor", "% Processor Time", instance);
                counter.NextValue();
                counters.Add(counter);
            }
            return counters;
        }

        private Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> InitNetAdaptersCounters()
        {
            var category = new PerformanceCounterCategory("Network Interface");
            Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> counters = new();
            foreach (var instance in category.GetInstanceNames())
            {
                if (instance.Contains("Loopback", StringComparison.OrdinalIgnoreCase)) continue;
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
            foreach (var counter in _networkCounters)
            {
                rx += counter.Value.Rx.NextValue();
                tx += counter.Value.Tx.NextValue();
            }
            return new Dictionary<string, float> { { "TxTraffic", tx }, { "RxTraffic", rx } };
        }

        private Dictionary<string, (PerformanceCounter readingSpeed, PerformanceCounter writingSpeed)> InitDiskCounters()
        {
            var category = new PerformanceCounterCategory("PhysicalDisk");
            Dictionary<string, (PerformanceCounter readingSpeed, PerformanceCounter writingSpeed)> counters = new();
            foreach (var instance in category.GetInstanceNames())
            {
                if (instance.Equals("_Total", StringComparison.OrdinalIgnoreCase)) continue;
                var diskReadCounters = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instance);
                var diskWriteCounters = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instance);
                diskReadCounters.NextValue();
                diskWriteCounters.NextValue();
                counters.Add(instance, (diskReadCounters, diskWriteCounters));
            }
            return counters;
        }

        private Dictionary<string, float> CollectDiskMetrics()
        {
            float readingSpeed = 0f;
            float writingSpeed = 0f;
            foreach (var counter in _diskCounters)
            {
                readingSpeed += counter.Value.readingSpeed.NextValue();
                writingSpeed += counter.Value.writingSpeed.NextValue();
            }
            return new Dictionary<string, float> { { "ReadingSpeed", readingSpeed }, { "WritingSpeed", writingSpeed } };
        }

        public void Dispose()
        {
            foreach (var counter in _coreCounters) counter.Dispose();
            foreach (var counter in _networkCounters.Values) { counter.Rx.Dispose(); counter.Tx.Dispose(); }
            foreach (var counter in _diskCounters.Values) { counter.readingSpeed.Dispose(); counter.writingSpeed.Dispose(); }
            
            // Звільняємо ресурси GPU
            foreach (var counter in _gpuLoadCounters.Values) counter.Dispose();
            foreach (var counter in _gpuRamCounters.Values) counter.Dispose();
        }
    }
}