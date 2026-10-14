using System;
using System.Collections.Generic;
using System.Linq;
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

        public float RamLoad { get; set; }
        public int TotalRam { get; set; }

        public float WiFiTx { get; set; }
        public float WiFiRx { get; set; }
        public int OpenSockets { get; set; }

        public float FreeDiskSpace { get; set; }
        public double WritingSpeed { get; set; }
        public double ReadingSpeed { get; set; }

        public override string ToString()
        {
            string cores = string.Join(" | ", CpuCoreLoads.Select(c => $"{c.CoreId}: {c.Load}"));
            return $"{instanceId} | {Timestamp:O} | RAM: {TotalRam} MB ({RamLoad}) | {CpuName} (Total: {CpuLoad}) [{cores}] | Rx: {WiFiRx} B/s | Tx: {WiFiTx} B/s | Sockets: {OpenSockets} | Free Disk: {FreeDiskSpace} MB | Read: {ReadingSpeed} B/s | Write: {WritingSpeed} B/s";
        }
    }

    public class CoreLoad
    {
        public string CoreId { get; set; }
        public float Load { get; set; }
    }
}