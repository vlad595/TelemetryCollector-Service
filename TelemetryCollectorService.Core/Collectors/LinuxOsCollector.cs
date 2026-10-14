using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Core.Collectors
{
    [SupportedOSPlatform("linux")]
    public class LinuxOsCollector : IMetricCollctor<MachineMetrics>
    {
        private Guid instanceId;
        public string Name { get; set; } = "OS Linux";
        private string CpuName { get; set; }

        private Dictionary<string, (ulong Idle, ulong Total)> _prevCpuStats = new();
        private (ulong RxBytes, ulong TxBytes, DateTime Timestamp) _prevNetStats;
        private (ulong ReadBytes, ulong WriteBytes, DateTime Timestamp) _prevDiskStats;

        private static readonly Regex WholeDiskRegex = new(
            @"^(sd[a-z]+|vd[a-z]+|xvd[a-z]+|nvme\d+n\d+|mmcblk\d+)$",
            RegexOptions.Compiled);

        private static readonly HashSet<string> IgnoredDriveFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            "tmpfs", "devtmpfs", "squashfs", "overlay", "proc", "sysfs", "cgroup", "cgroup2fs", "nsfs", "mqueue"
        };

        public LinuxOsCollector()
        {
            instanceId = Guid.NewGuid();
            if (!IsAvailable())
                return;

            CpuName = GetCpuName();

            _prevCpuStats = ReadCpuRawStats();
            _prevNetStats = ReadNetRawStats();
            _prevDiskStats = ReadDiskRawStats();
        }

        public bool IsAvailable()
        {
            return OperatingSystem.IsLinux();
        }

        public MachineMetrics Collect()
        {
            Dictionary<string, ulong> ramMetrics = CollectRamMetrics();
            float ramLoad = ramMetrics["totalMemory"] > 0 ? (float)ramMetrics["usedMemory"] / ramMetrics["totalMemory"] : 0f;

            (float totalCpuLoad, List<CoreLoad> coreLoads) = CollectCpuLoads();
            Dictionary<string, float> networkTraffic = CollectTxRx();
            Dictionary<string, double> diskMetrics = CollectDiskMetrics();

            return new MachineMetrics
            {
                instanceId = instanceId,
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

        private string GetCpuName()
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
                    int colonIndex = line.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex + 1 < line.Length)
                    {
                        return line[(colonIndex + 1)..].Trim();
                    }
                }
            }
            return "Unknown CPU";
        }

        private Dictionary<string, (ulong Idle, ulong Total)> ReadCpuRawStats()
        {
            var stats = new Dictionary<string, (ulong Idle, ulong Total)>();
            const string statPath = "/proc/stat";

            if (!File.Exists(statPath))
                return stats;

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
                ulong nonIdle = user + nice + system + irq + softirq + steal;
                ulong total = idleAll + nonIdle;

                stats[cpuId] = (idleAll, total);
            }

            return stats;
        }

        private (float TotalLoad, List<CoreLoad> CoreLoads) CollectCpuLoads()
        {
            var currentStats = ReadCpuRawStats();
            var coreLoads = new List<CoreLoad>();
            float totalLoad = 0f;

            foreach (var (cpuId, current) in currentStats)
            {
                float load = 0f;
                if (_prevCpuStats.TryGetValue(cpuId, out var prev))
                {
                    ulong totalDiff = current.Total - prev.Total;
                    ulong idleDiff = current.Idle - prev.Idle;

                    if (totalDiff > 0)
                    {
                        load = (float)Math.Round((double)(totalDiff - idleDiff) / totalDiff, 2);
                    }
                }

                if (cpuId.Equals("cpu", StringComparison.OrdinalIgnoreCase))
                {
                    totalLoad = load;
                }
                else
                {
                    coreLoads.Add(new CoreLoad
                    {
                        CoreId = cpuId.Replace("cpu", ""),
                        Load = load
                    });
                }
            }

            _prevCpuStats = currentStats;
            return (totalLoad, coreLoads);
        }

        private Dictionary<string, ulong> CollectRamMetrics()
        {
            ulong totalKb = 0;
            ulong availableKb = 0;
            ulong freeKb = 0;
            ulong buffersKb = 0;
            ulong cachedKb = 0;

            const string memInfoPath = "/proc/meminfo";
            if (File.Exists(memInfoPath))
            {
                foreach (string line in File.ReadLines(memInfoPath))
                {
                    if (line.StartsWith("MemTotal:"))
                        totalKb = ParseMemInfoValue(line);
                    else if (line.StartsWith("MemAvailable:"))
                        availableKb = ParseMemInfoValue(line);
                    else if (line.StartsWith("MemFree:"))
                        freeKb = ParseMemInfoValue(line);
                    else if (line.StartsWith("Buffers:"))
                        buffersKb = ParseMemInfoValue(line);
                    else if (line.StartsWith("Cached:"))
                        cachedKb = ParseMemInfoValue(line);
                }
            }

            if (availableKb == 0)
            {
                availableKb = freeKb + buffersKb + cachedKb;
            }

            ulong totalMemoryMb = totalKb / 1024;
            ulong freeMemoryMb = availableKb / 1024;
            ulong usedMemoryMb = totalMemoryMb >= freeMemoryMb ? totalMemoryMb - freeMemoryMb : 0;

            return new Dictionary<string, ulong>
            {
                { "totalMemory", totalMemoryMb },
                { "freeMemory", freeMemoryMb },
                { "usedMemory", usedMemoryMb }
            };
        }

        private static ulong ParseMemInfoValue(string line)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && ulong.TryParse(parts[1], out ulong val) ? val : 0;
        }

        private (ulong RxBytes, ulong TxBytes, DateTime Timestamp) ReadNetRawStats()
        {
            ulong rxTotal = 0;
            ulong txTotal = 0;
            const string netDevPath = "/proc/net/dev";

            if (File.Exists(netDevPath))
            {
                foreach (string line in File.ReadLines(netDevPath).Skip(2))
                {
                    int colonIndex = line.IndexOf(':');
                    if (colonIndex < 0)
                        continue;

                    string iface = line[..colonIndex].Trim();
                    if (iface.Equals("lo", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] fields = line[(colonIndex + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
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

        private Dictionary<string, float> CollectTxRx()
        {
            var current = ReadNetRawStats();
            double elapsedSeconds = (current.Timestamp - _prevNetStats.Timestamp).TotalSeconds;

            float rxBps = 0f;
            float txBps = 0f;

            if (elapsedSeconds > 0)
            {
                if (current.RxBytes >= _prevNetStats.RxBytes)
                    rxBps = (float)Math.Round((current.RxBytes - _prevNetStats.RxBytes) / elapsedSeconds, 2);

                if (current.TxBytes >= _prevNetStats.TxBytes)
                    txBps = (float)Math.Round((current.TxBytes - _prevNetStats.TxBytes) / elapsedSeconds, 2);
            }

            _prevNetStats = current;

            return new Dictionary<string, float>
            {
                { "RxTraffic", rxBps },
                { "TxTraffic", txBps }
            };
        }

        private (ulong ReadBytes, ulong WriteBytes, DateTime Timestamp) ReadDiskRawStats()
        {
            ulong readSectors = 0;
            ulong writeSectors = 0;
            const string diskStatsPath = "/proc/diskstats";

            if (File.Exists(diskStatsPath))
            {
                foreach (string line in File.ReadLines(diskStatsPath))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 10)
                        continue;

                    string devName = parts[2];
                    if (!WholeDiskRegex.IsMatch(devName))
                        continue;

                    if (ulong.TryParse(parts[5], out ulong rSectors) &&
                        ulong.TryParse(parts[9], out ulong wSectors))
                    {
                        readSectors += rSectors;
                        writeSectors += wSectors;
                    }
                }
            }

            return (readSectors * 512UL, writeSectors * 512UL, DateTime.UtcNow);
        }

        private Dictionary<string, double> CollectDiskMetrics()
        {
            long freeBytes = 0;
            var seenTotals = new HashSet<long>();

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                        continue;

                    if (IgnoredDriveFormats.Contains(drive.DriveFormat))
                        continue;

                    if (drive.TotalSize > 0 && seenTotals.Add(drive.TotalSize))
                    {
                        freeBytes += drive.AvailableFreeSpace;
                    }
                }
                catch
                {
                }
            }

            var currentDisk = ReadDiskRawStats();
            double elapsedSeconds = (currentDisk.Timestamp - _prevDiskStats.Timestamp).TotalSeconds;

            double readSpeed = 0.0;
            double writeSpeed = 0.0;

            if (elapsedSeconds > 0)
            {
                if (currentDisk.ReadBytes >= _prevDiskStats.ReadBytes)
                    readSpeed = Math.Round((currentDisk.ReadBytes - _prevDiskStats.ReadBytes) / elapsedSeconds, 2);

                if (currentDisk.WriteBytes >= _prevDiskStats.WriteBytes)
                    writeSpeed = Math.Round((currentDisk.WriteBytes - _prevDiskStats.WriteBytes) / elapsedSeconds, 2);
            }

            _prevDiskStats = currentDisk;

            return new Dictionary<string, double>
            {
                { "freeSpaceMb", Math.Round(freeBytes / (1024.0 * 1024.0), 2) },
                { "readBytesPerSec", readSpeed },
                { "writeBytesPerSec", writeSpeed }
            };
        }

        private int CollectOpenSockets()
        {
            try
            {
                IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
                return properties.GetActiveTcpConnections().Length
                     + properties.GetActiveTcpListeners().Length
                     + properties.GetActiveUdpListeners().Length;
            }
            catch
            {
                return CountProcNetLines("/proc/net/tcp")
                     + CountProcNetLines("/proc/net/tcp6")
                     + CountProcNetLines("/proc/net/udp")
                     + CountProcNetLines("/proc/net/udp6");
            }
        }

        private static int CountProcNetLines(string path)
        {
            if (!File.Exists(path))
                return 0;

            int count = File.ReadLines(path).Count();
            return count > 1 ? count - 1 : 0;
        }
    }
}