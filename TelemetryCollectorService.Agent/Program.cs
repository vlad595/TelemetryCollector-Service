using TelemetryCollectorService.Agent;
using TelemetryCollectorService.Agent.Services;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Telemetry Collector Agent");
builder.Services.AddHostedService<Worker>();

builder.Services.AddHttpClient<IPublisher, MachineMetricsPublisher>();

var host = builder.Build();
host.Run();
