using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using TelemetryCollectorService.Agent.Services;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Agent;
public class Worker : BackgroundService
{
    private readonly HubConnection _hubConnection;
    private readonly string _agentId;
    public Worker(HubConnection hubConnection, string agentId)
    {
        this._hubConnection = hubConnection;
        this._agentId = agentId;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MachineMetricsService service = new MachineMetricsService();
        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Clear();

            MachineMetrics machineMetrics = service.Collect();

            Console.WriteLine(machineMetrics.ToString());

            await _hubConnection.InvokeAsync("SendMetrics", _agentId, machineMetrics);

            await Task.Delay(1000, stoppingToken);  
        }
    }
}