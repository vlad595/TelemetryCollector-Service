using System.Reflection.PortableExecutable;
using TelemetryCollectorService.Agent.Services;

namespace TelemetryCollectorService.Agent;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var service = new CollectorService();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var metrics = service.CollectAll();
            if (logger.IsEnabled(LogLevel.Information))
            {
                Console.Clear();
                foreach(var metric in metrics)
                {
                    Console.WriteLine(metric.ToString());
                }
            }
        }
    }
}