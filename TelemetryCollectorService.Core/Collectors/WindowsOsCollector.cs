using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Collectors
{
    public class CpuDto
    {
        public string Name { get; set; }
        public int NumberOfCores { get; set; }
        public int NumberOfLogicalProcessors { get; set; }
        public List<CoreLoad> CoreLoads { get; set; }
    }

    [SupportedOSPlatform("windows")]
    public class WindowsOsCollector : IMetricCollctor<MachineMetrics>, IDisposable
    {
        public string Name { get; set; } = "OS Windows";
        private string CpuName { get; set; }

        private PerformanceCounter _totalCpuCounter;
        private List<PerformanceCounter> _coreCounters;
        private Dictionary<string, (PerformanceCounter Rx, PerformanceCounter Tx)> _networkCounters;
        private PerformanceCounter _diskReadCounter;
        private PerformanceCounter _diskWriteCounter;

        public WindowsOsCollector()
        {
            if (!IsAvailable())
                return;

            CpuName = GetCpuName();
            _coreCounters = InitPerformanceCounters();
            _networkCounters = InitNetAdaptersCounters();
            InitDiskCounters();
        }

        public bool IsAvailable()
        {
            return OperatingSystem.IsWindows();
        }

        public MachineMetrics Collect()
        {
            Dictionary<string, ulong> ramMetrics = CollectRamMetrics();
            float ramLoad = ramMetrics["totalMemory"] > 0
                ? (float)ramMetrics["usedMemory"] / ramMetrics["totalMemory"]
                : 0f;

            Dictionary<string, float> networkTraffic = CollectTxRx();
            Dictionary<string, double> diskMetrics = CollectDiskMetrics();
            List<CoreLoad> coreLoads = GetCoreLoads();

            float totalCpuLoad = _totalCpuCounter != null
                ? (float)Math.Round(_totalCpuCounter.NextValue() / 100f, 2)
                : (coreLoads.Count > 0 ? (float)Math.Round(coreLoads.Average(c => c.Load), 2) : 0f);

            return new MachineMetrics
            {
                instanceId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Environment = "Prod",
                CpuName = CpuName,
                CpuLoad = totalCpuLoad,
                CpuCoreLoads = coreLoads,
                RamLoad = (float)Math.Round(ramLoad, 2),
                TotalRam = Convert.ToInt32(ramMetrics["totalMemory"]),
                WiFiRx = networkTraffic["RxTraffic"],
                WiFiTx = networkTraffic["TxTraffic"],
                OpenSockets = CollectOpenSockets(),
                FreeDiskSpace = (float)diskMetrics["freeSpaceMb"],
                ReadingSpeed = diskMetrics["readBytesPerSec"],
                WritingSpeed = diskMetrics["writeBytesPerSec"]
            };
        }

        private Dictionary<string, ulong> CollectRamMetrics()
        {
            ulong totalMemory = 0;
            ulong freeMemory = 0;
            ulong usedMemory = 0;

            string ramQuery = "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem";
            using (var ramSearcher = new ManagementObjectSearcher(ramQuery))
            using (var results = ramSearcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        // Значення з WMI приходять у кілобайтах, переводимо в мегабайти
                        totalMemory = Convert.ToUInt64(mo["TotalVisibleMemorySize"]) / 1024;
                        freeMemory = Convert.ToUInt64(mo["FreePhysicalMemory"]) / 1024;
                        usedMemory = totalMemory - freeMemory;
                    }
                }
            }

            return new Dictionary<string, ulong>
            {
                { "totalMemory", totalMemory },
                { "freeMemory", freeMemory },
                { "usedMemory", usedMemory }
            };
        }

        private string GetCpuName()
        {
            string query = "SELECT Name FROM Win32_Processor";
            using (var searcher = new ManagementObjectSearcher(query))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        return mo["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                    }
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
                coreLoads.Add(new CoreLoad
                {
                    CoreId = counter.InstanceName,
                    Load = (float)Math.Round(load / 100f, 2)
                });
            }
            return coreLoads;
        }

        private List<PerformanceCounter> InitPerformanceCounters()
        {
            List<PerformanceCounter> coreCounters = new();
            var category = new PerformanceCounterCategory("Processor");
            string[] instances = category.GetInstanceNames();

            foreach (var instance in instances)
            {
                if (instance.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                {
                    _totalCpuCounter = new PerformanceCounter("Processor", "% Processor Time", instance);
                    _totalCpuCounter.NextValue();
                    continue;
                }

                var counter = new PerformanceCounter("Processor", "% Processor Time", instance);
                counter.NextValue();
                coreCounters.Add(counter);
            }

            return coreCounters;
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

            foreach (var counter in _networkCounters.Values)
            {
                rx += counter.Rx.NextValue();
                tx += counter.Tx.NextValue();
            }

            return new Dictionary<string, float>
            {
                { "TxTraffic", (float)Math.Round(tx, 2) },
                { "RxTraffic", (float)Math.Round(rx, 2) }
            };
        }

        private int CollectOpenSockets()
        {
            try
            {
                IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
                int activeTcp = properties.GetActiveTcpConnections().Length;
                int tcpListeners = properties.GetActiveTcpListeners().Length;
                int udpListeners = properties.GetActiveUdpListeners().Length;

                return activeTcp + tcpListeners + udpListeners;
            }
            catch
            {
                return 0;
            }
        }

        private void InitDiskCounters()
        {
            var category = new PerformanceCounterCategory("PhysicalDisk");
            if (category.InstanceExists("_Total"))
            {
                _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

                _diskReadCounter.NextValue();
                _diskWriteCounter.NextValue();
            }
        }

        private Dictionary<string, double> CollectDiskMetrics()
        {
            long freeBytes = 0;

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    freeBytes += drive.AvailableFreeSpace;
                }
            }

            double freeSpaceMb = Math.Round(freeBytes / (1024.0 * 1024.0), 2);
            double readSpeed = _diskReadCounter != null ? Math.Round(_diskReadCounter.NextValue(), 2) : 0.0;
            double writeSpeed = _diskWriteCounter != null ? Math.Round(_diskWriteCounter.NextValue(), 2) : 0.0;

            return new Dictionary<string, double>
            {
                { "freeSpaceMb", freeSpaceMb },
                { "readBytesPerSec", readSpeed },
                { "writeBytesPerSec", writeSpeed }
            };
        }

        public void Dispose()
        {
            _totalCpuCounter?.Dispose();

            if (_coreCounters != null)
            {
                foreach (var counter in _coreCounters)
                    counter.Dispose();
            }

            if (_networkCounters != null)
            {
                foreach (var pair in _networkCounters.Values)
                {
                    pair.Rx.Dispose();
                    pair.Tx.Dispose();
                }
            }

            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
        }
    }
}