using TelemetryCollectorService.Agent.Services;

namespace TelemetryCollectorService.Agent;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MachineMetricsService service = new MachineMetricsService();
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                Console.WriteLine(service.Collect().ToString());
            }
            await Task.Delay(1000, stoppingToken);  
        }
    }
}
