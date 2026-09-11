using System;

namespace TelemetryCollectorService.Core.Models
{
    
    public class BaseMetric
    {
        public Guid instanceId {get;set;}
        public DateTime Timestamp {get;set;}
        public string Environment {get;set;}
    }
    class MachineMetrics : BaseMetric
    {
        public float CpuLoad {get;set;}
        public List<CoreLoad> CpuCoreLoads {get;set;} = new List<CoreLoad>();

        public float RamLoad {get;set;}

        public double WiFiTx {get;set;}
        public double WiFiRx {get;set;}
        public int OpenSockets {get;set;}
        
        public float FreeDiskSpace {get;set;}
        public double WritingSpeed {get;set;}
        public double ReadingSpeed {get;set;}
    }
    class CoreLoad
    {
        public int CoreId {get;set;}
        public float Load {get;set;}
    }
}