using System;
using System.Collections.Generic;
using TelemetryCollectorService.Core.Collectors;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Agent.Services;

public class CollectorService : IDisposable
{
    private readonly IMetricCollctor<MachineMetrics> _osCollector;
    private readonly RawAPICollector _apiCollector;
    private readonly PostgresCollector _postgresCollector;

    public CollectorService()
    {
        if (OperatingSystem.IsWindows())
            _osCollector = new WindowsOsCollector();
        else if (OperatingSystem.IsLinux())
            _osCollector = new LinuxOsCollector();

        _apiCollector = new RawAPICollector();
        _postgresCollector = new PostgresCollector();
    }
    public MachineMetrics CollectMachineMetrics()
    {
        if (_osCollector != null && _osCollector.IsAvailable()){
            return _osCollector.Collect();
        }
        else return null;
    }

    public IReadOnlyList<IMetricData> CollectAll()
    {
        var results = new List<IMetricData>(3);

        if (_osCollector != null && _osCollector.IsAvailable())
        {
            var osMetric = _osCollector.Collect();
            if (osMetric != null)
                results.Add(osMetric);
        }

        if (_apiCollector.IsAvailable())
        {
            var apiMetric = _apiCollector.Collect();
            if (apiMetric != null)
                results.Add(apiMetric);
        }

        if (_postgresCollector.IsAvailable())
        {
            var dbMetric = _postgresCollector.Collect();
            if (dbMetric != null)
                results.Add(dbMetric);
        }

        return results;
    }

    public void Dispose()
    {
        (_osCollector as IDisposable)?.Dispose();
        _postgresCollector?.Dispose();
    }
}