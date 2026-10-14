using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Collectors
{
    [SupportedOSPlatform("linux")]
    public class LinuxOsCollector : IMetricCollctor<MachineMetrics>
    {
        public string Name { get; set; } = "OS Linux";
        private readonly string _cpuName;

        private readonly Dictionary<string, (ulong Idle, ulong Total)> _prevCpuStats = new();
        private (ulong RxBytes, ulong TxBytes, DateTime Timestamp) _prevNetStats;
        private (ulong ReadBytes, ulong WriteBytes, DateTime Timestamp) _prevDiskStats;

        private float _cachedFreeDiskSpaceMb;
        private DateTime _lastDiskSpaceCheck = DateTime.MinValue;
        private static readonly TimeSpan DiskCacheTtl = TimeSpan.FromSeconds(30);

        private static readonly HashSet<string> IgnoredDriveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            "tmpfs", "devtmpfs", "squashfs", "overlay", "proc", "sysfs", "cgroup", "cgroup2fs", "nsfs", "mqueue"
        };

        public LinuxOsCollector()
        {
            if (!IsAvailable())
                return;

            _cpuName = GetCpuNameOnce();
            UpdateCpuStats(out _, null);
            _prevNetStats = ReadNetRawStats();
            _prevDiskStats = ReadDiskRawStats();
        }

        public bool IsAvailable() => OperatingSystem.IsLinux();

        public MachineMetrics Collect()
        {
            if (!IsAvailable())
                return null;

            (int totalRamMb, float ramLoad) = CollectRamFast();
            var coreLoads = new List<CoreLoad>();
            UpdateCpuStats(out float totalCpuLoad, coreLoads);
            (float rxBps, float txBps) = CollectTxRxFast();
            (float freeDiskMb, double readBps, double writeBps) = CollectDiskMetricsFast();

            return new MachineMetrics
            {
                instanceId = Guid.NewGuid(),
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

        private static string GetCpuNameOnce()
        {
            const string cpuInfoPath = "/proc/cpuinfo";
            if (!File.Exists(cpuInfoPath))
                return "Unknown CPU";

            foreach (string line in File.ReadLines(cpuInfoPath))
            {
                if (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Processor", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase))
                {
                    int idx = line.IndexOf(':');
                    if (idx >= 0 && idx + 1 < line.Length)
                        return line[(idx + 1)..].Trim();
                }
            }
            return "Unknown CPU";
        }

        private void UpdateCpuStats(out float totalLoad, List<CoreLoad> coreLoads)
        {
            totalLoad = 0f;
            const string statPath = "/proc/stat";
            if (!File.Exists(statPath))
                return;

            foreach (string line in File.ReadLines(statPath))
            {
                if (!line.StartsWith("cpu"))
                    break;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                    continue;

                string cpuId = parts[0];
                ulong user = ulong.Parse(parts[1]);
                ulong nice = ulong.Parse(parts[2]);
                ulong system = ulong.Parse(parts[3]);
                ulong idle = ulong.Parse(parts[4]);
                ulong iowait = parts.Length > 5 ? ulong.Parse(parts[5]) : 0;
                ulong irq = parts.Length > 6 ? ulong.Parse(parts[6]) : 0;
                ulong softirq = parts.Length > 7 ? ulong.Parse(parts[7]) : 0;
                ulong steal = parts.Length > 8 ? ulong.Parse(parts[8]) : 0;

                ulong idleAll = idle + iowait;
                ulong total = idleAll + user + nice + system + irq + softirq + steal;

                float load = 0f;
                if (_prevCpuStats.TryGetValue(cpuId, out var prev))
                {
                    ulong totalDiff = total - prev.Total;
                    ulong idleDiff = idleAll - prev.Idle;
                    if (totalDiff > 0)
                        load = (float)Math.Round((double)(totalDiff - idleDiff) / totalDiff, 2);
                }

                _prevCpuStats[cpuId] = (idleAll, total);

                if (cpuId.Length == 3)
                {
                    totalLoad = load;
                }
                else if (coreLoads != null)
                {
                    coreLoads.Add(new CoreLoad
                    {
                        CoreId = cpuId[3..],
                        Load = load
                    });
                }
            }
        }

        private static (int TotalRamMb, float RamLoad) CollectRamFast()
        {
            ulong totalKb = 0, availKb = 0, freeKb = 0, buffersKb = 0, cachedKb = 0;
            const string memInfoPath = "/proc/meminfo";

            if (File.Exists(memInfoPath))
            {
                foreach (string line in File.ReadLines(memInfoPath))
                {
                    if (line.StartsWith("MemTotal:"))
                        totalKb = ParseKb(line);
                    else if (line.StartsWith("MemAvailable:"))
                        availKb = ParseKb(line);
                    else if (line.StartsWith("MemFree:"))
                        freeKb = ParseKb(line);
                    else if (line.StartsWith("Buffers:"))
                        buffersKb = ParseKb(line);
                    else if (line.StartsWith("Cached:"))
                        cachedKb = ParseKb(line);

                    if (totalKb > 0 && availKb > 0)
                        break;
                }
            }

            if (availKb == 0)
                availKb = freeKb + buffersKb + cachedKb;

            if (totalKb == 0)
                return (0, 0f);

            ulong usedKb = totalKb >= availKb ? totalKb - availKb : 0;
            int totalMb = (int)(totalKb / 1024UL);
            float ramLoad = (float)Math.Round((double)usedKb / totalKb, 2);

            return (totalMb, ramLoad);
        }

        private static ulong ParseKb(string line)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && ulong.TryParse(parts[1], out ulong val) ? val : 0;
        }

        private static (ulong RxBytes, ulong TxBytes, DateTime Timestamp) ReadNetRawStats()
        {
            ulong rxTotal = 0, txTotal = 0;
            const string netDevPath = "/proc/net/dev";

            if (File.Exists(netDevPath))
            {
                int lineIndex = 0;
                foreach (string line in File.ReadLines(netDevPath))
                {
                    if (lineIndex++ < 2)
                        continue;

                    int colon = line.IndexOf(':');
                    if (colon < 0)
                        continue;

                    ReadOnlySpan<char> iface = line.AsSpan(0, colon).Trim();
                    if (iface.Equals("lo", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length >= 9 &&
                        ulong.TryParse(fields[0], out ulong rx) &&
                        ulong.TryParse(fields[8], out ulong tx))
                    {
                        rxTotal += rx;
                        txTotal += tx;
                    }
                }
            }

            return (rxTotal, txTotal, DateTime.UtcNow);
        }

        private (float RxBps, float TxBps) CollectTxRxFast()
        {
            var current = ReadNetRawStats();
            double elapsed = (current.Timestamp - _prevNetStats.Timestamp).TotalSeconds;
            float rxBps = 0f, txBps = 0f;

            if (elapsed > 0)
            {
                if (current.RxBytes >= _prevNetStats.RxBytes)
                    rxBps = (float)Math.Round((current.RxBytes - _prevNetStats.RxBytes) / elapsed, 2);

                if (current.TxBytes >= _prevNetStats.TxBytes)
                    txBps = (float)Math.Round((current.TxBytes - _prevNetStats.TxBytes) / elapsed, 2);
            }

            _prevNetStats = current;
            return (rxBps, txBps);
        }

        private static bool IsWholePhysicalDisk(string dev)
        {
            if (dev.StartsWith("sd") || dev.StartsWith("vd") || dev.StartsWith("xvd"))
                return !char.IsDigit(dev[^1]);

            if (dev.StartsWith("nvme") || dev.StartsWith("mmcblk"))
                return !dev.Contains('p');

            return false;
        }

        private static (ulong ReadBytes, ulong WriteBytes, DateTime Timestamp) ReadDiskRawStats()
        {
            ulong readSectors = 0, writeSectors = 0;
            const string diskStatsPath = "/proc/diskstats";

            if (File.Exists(diskStatsPath))
            {
                foreach (string line in File.ReadLines(diskStatsPath))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 10 || !IsWholePhysicalDisk(parts[2]))
                        continue;

                    if (ulong.TryParse(parts[5], out ulong r) && ulong.TryParse(parts[9], out ulong w))
                    {
                        readSectors += r;
                        writeSectors += w;
                    }
                }
            }

            return (readSectors * 512UL, writeSectors * 512UL, DateTime.UtcNow);
        }

        private (float FreeSpaceMb, double ReadBps, double WriteBps) CollectDiskMetricsFast()
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastDiskSpaceCheck >= DiskCacheTtl)
            {
                long freeBytes = 0;
                var seenTotals = new HashSet<long>();

                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || drive.DriveType != DriveType.Fixed || IgnoredDriveFormats.Contains(drive.DriveFormat))
                            continue;

                        if (drive.TotalSize > 0 && seenTotals.Add(drive.TotalSize))
                            freeBytes += drive.AvailableFreeSpace;
                    }
                    catch { }
                }

                _cachedFreeDiskSpaceMb = (float)Math.Round(freeBytes / (1024.0 * 1024.0), 2);
                _lastDiskSpaceCheck = now;
            }

            var currentDisk = ReadDiskRawStats();
            double elapsed = (currentDisk.Timestamp - _prevDiskStats.Timestamp).TotalSeconds;
            double readSpeed = 0.0, writeSpeed = 0.0;

            if (elapsed > 0)
            {
                if (currentDisk.ReadBytes >= _prevDiskStats.ReadBytes)
                    readSpeed = Math.Round((currentDisk.ReadBytes - _prevDiskStats.ReadBytes) / elapsed, 2);

                if (currentDisk.WriteBytes >= _prevDiskStats.WriteBytes)
                    writeSpeed = Math.Round((currentDisk.WriteBytes - _prevDiskStats.WriteBytes) / elapsed, 2);
            }

            _prevDiskStats = currentDisk;
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
    }
}