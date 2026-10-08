using System;
using System.Collections.Generic;
using TelemetryCollectorService.Core.Interfaces;

namespace TelemetryCollectorService.Core.Models
{
    public class MachineMetrics : IMetricData
    {
        public Guid instanceId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Environment { get; set; }
        public string CpuName { get; set; }
        public float CpuLoad { get; set; }
        public List<CoreLoad> CpuCoreLoads { get; set; } = new List<CoreLoad>();

        public string GpuName { get; set; } = string.Empty;
        public float GpuLoad { get; set; }
        public float GpuRamUsedMb { get; set; }

        public float RamLoad { get; set; }
        public int TotalRam { get; set; }

        public float WiFiTx { get; set; }
        public float WiFiRx { get; set; }
        public int OpenSockets { get; set; }

        public float DiskCapacity { get; set; }
        public float FreeDiskSpace { get; set; }
        public double WritingSpeed { get; set; }
        public double ReadingSpeed { get; set; }

        public string ToString()
        {
            string cpuLoad = string.Empty;
            foreach (CoreLoad core in CpuCoreLoads)
            {
                cpuLoad += $" / {core.CoreId} - {core.Load}";
            }
            
            return $"{this.instanceId} | {this.Timestamp} | RAM: {this.RamLoad * 100}% | CPU: {this.CpuName} {cpuLoad} | GPU: {this.GpuName} ({this.GpuLoad}%, {this.GpuRamUsedMb}MB) | Net: {this.WiFiRx}Rx/{this.WiFiTx}Tx | Disk: {FreeDiskSpace}Gb вільних | Read: {this.ReadingSpeed} | Write: {this.WritingSpeed}";
        }
    }

    public class CoreLoad
    {
        public string CoreId { get; set; }
        public float Load { get; set; }
    }
}