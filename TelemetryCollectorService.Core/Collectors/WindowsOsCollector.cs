using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Collectors
{
    [SupportedOSPlatform("windows")]
    public class WindowsOsCollector : IMetricCollctor<MachineMetrics>, IDisposable
    {
        private string instanceId;
        public string Name { get; set; } = "OS Windows";
        private readonly string _cpuName;

        private PerformanceCounter _totalCpuCounter;
        private List<PerformanceCounter> _coreCounters;
        private List<(PerformanceCounter Rx, PerformanceCounter Tx)> _networkCounters;
        private PerformanceCounter _diskReadCounter;
        private PerformanceCounter _diskWriteCounter;

        private readonly MEMORYSTATUSEX _memStatus = new();
        private float _cachedFreeDiskSpaceMb;
        private DateTime _lastDiskSpaceCheck = DateTime.MinValue;
        private static readonly TimeSpan DiskCacheTtl = TimeSpan.FromSeconds(30);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public WindowsOsCollector(string agentId)
        {
            instanceId = agentId;
            if (!IsAvailable())
                return;

            _cpuName = GetCpuNameOnce();
            _coreCounters = InitPerformanceCounters();
            _networkCounters = InitNetAdaptersCounters();
            InitDiskCounters();
        }

        public bool IsAvailable() => OperatingSystem.IsWindows();

        public MachineMetrics Collect()
        {
            if (!IsAvailable())
                return null;

            (int totalRamMb, float ramLoad) = CollectRamFast();
            (float rxBps, float txBps) = CollectTxRxFast();
            (float freeDiskMb, double readBps, double writeBps) = CollectDiskMetricsFast();
            List<CoreLoad> coreLoads = GetCoreLoadsFast(out float avgCoreLoad);

            float totalCpuLoad = _totalCpuCounter != null
                ? (float)Math.Round(_totalCpuCounter.NextValue() / 100f, 2)
                : avgCoreLoad;

            return new MachineMetrics
            {
                instanceId = Guid.Parse(instanceId),
                Timestamp = DateTime.UtcNow,
                Environment = "Prod",
                CpuName = _cpuName,
                CpuLoad = totalCpuLoad,
                CpuCoreLoads = coreLoads,
                RamLoad = ramLoad,
                TotalRam = totalRamMb,
                WiFiRx = rxBps,
                WiFiTx = txBps,
                OpenSockets = CollectOpenSocketsFast(),
                FreeDiskSpace = freeDiskMb,
                ReadingSpeed = readBps,
                WritingSpeed = writeBps
            };
        }

        private (int TotalRamMb, float RamLoad) CollectRamFast()
        {
            if (GlobalMemoryStatusEx(_memStatus) && _memStatus.ullTotalPhys > 0)
            {
                ulong totalMb = _memStatus.ullTotalPhys / (1024UL * 1024UL);
                ulong usedBytes = _memStatus.ullTotalPhys - _memStatus.ullAvailPhys;
                float ramLoad = (float)Math.Round((double)usedBytes / _memStatus.ullTotalPhys, 2);
                return ((int)totalMb, ramLoad);
            }
            return (0, 0f);
        }

        private static string GetCpuNameOnce()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    using (mo)
                    {
                        return mo["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                    }
                }
            }
            catch
            {

            }
            return "Unknown CPU";
        }

        private List<CoreLoad> GetCoreLoadsFast(out float avgLoad)
        {
            int count = _coreCounters.Count;
            var coreLoads = new List<CoreLoad>(count);
            float sum = 0f;

            for (int i = 0; i < count; i++)
            {
                var counter = _coreCounters[i];
                float load = (float)Math.Round(counter.NextValue() / 100f, 2);
                sum += load;
                coreLoads.Add(new CoreLoad
                {
                    CoreId = counter.InstanceName,
                    Load = load
                });
            }

            avgLoad = count > 0 ? (float)Math.Round(sum / count, 2) : 0f;
            return coreLoads;
        }

        private List<PerformanceCounter> InitPerformanceCounters()
        {
            var coreCounters = new List<PerformanceCounter>();
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

        private static List<(PerformanceCounter Rx, PerformanceCounter Tx)> InitNetAdaptersCounters()
        {
            var category = new PerformanceCounterCategory("Network Interface");
            string[] instances = category.GetInstanceNames();
            var counters = new List<(PerformanceCounter Rx, PerformanceCounter Tx)>(instances.Length);

            foreach (var instance in instances)
            {
                if (instance.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rxCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);
                var txCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);

                rxCounter.NextValue();
                txCounter.NextValue();

                counters.Add((rxCounter, txCounter));
            }
            return counters;
        }

        private (float RxBps, float TxBps) CollectTxRxFast()
        {
            float rx = 0f;
            float tx = 0f;

            for (int i = 0; i < _networkCounters.Count; i++)
            {
                rx += _networkCounters[i].Rx.NextValue();
                tx += _networkCounters[i].Tx.NextValue();
            }

            return ((float)Math.Round(rx, 2), (float)Math.Round(tx, 2));
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

        private (float FreeSpaceMb, double ReadBps, double WriteBps) CollectDiskMetricsFast()
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastDiskSpaceCheck >= DiskCacheTtl)
            {
                long freeBytes = 0;
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        freeBytes += drive.AvailableFreeSpace;
                }
                _cachedFreeDiskSpaceMb = (float)Math.Round(freeBytes / (1024.0 * 1024.0), 2);
                _lastDiskSpaceCheck = now;
            }

            double readSpeed = _diskReadCounter != null ? Math.Round(_diskReadCounter.NextValue(), 2) : 0.0;
            double writeSpeed = _diskWriteCounter != null ? Math.Round(_diskWriteCounter.NextValue(), 2) : 0.0;

            return (_cachedFreeDiskSpaceMb, readSpeed, writeSpeed);
        }

        private static int CollectOpenSocketsFast()
        {
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                return props.GetActiveTcpConnections().Length
                     + props.GetActiveTcpListeners().Length
                     + props.GetActiveUdpListeners().Length;
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            _totalCpuCounter?.Dispose();

            if (_coreCounters != null)
            {
                foreach (var c in _coreCounters)
                    c.Dispose();
            }

            if (_networkCounters != null)
            {
                foreach (var (rx, tx) in _networkCounters)
                {
                    rx.Dispose();
                    tx.Dispose();
                }
            }

            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
        }
    }
}