using System;
using TelemetryCollectorService.Core.Interfaces;

namespace TelemetryCollectorService.Core.Models
{
    public class MachineMetrics: IMetricData
    {
        public Guid instanceId {get;set;}
        public DateTime Timestamp {get;set;}
        public string Environment {get;set;}
        public float CpuLoad {get;set;}
        public List<CoreLoad> CpuCoreLoads {get;set;} = new List<CoreLoad>();

        public float RamLoad {get;set;}

        public double WiFiTx {get;set;}
        public double WiFiRx {get;set;}
        public int OpenSockets {get;set;}

        public float FreeDiskSpace {get;set;}
        public double WritingSpeed {get;set;}
        public double ReadingSpeed {get;set;}

        public string ToString()
        {
            return $"{this.instanceId} | {this.Timestamp} | {this.RamLoad}";
        }
    }
    public class CoreLoad
    {
        public int CoreId {get;set;}
        public float Load {get;set;}
    }
}