using System.Reflection.PortableExecutable;
using TelemetryCollectorService.Agent.Services;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Agent;

public class Worker(ILogger<Worker> logger, IPublisher machineMetricsPublisher, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var service = new CollectorService(configuration);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            MachineMetrics metrics = service.CollectMachineMetrics();
            if (logger.IsEnabled(LogLevel.Information))
            {
                await machineMetricsPublisher.Publish(metrics);
            }
        }
    }
}