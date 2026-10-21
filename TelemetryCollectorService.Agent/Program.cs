using TelemetryCollectorService.Agent;
using TelemetryCollectorService.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Telemetry Collector Agent");
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IPublisher, MachineMetricsPublisher>();

var host = builder.Build();
host.Run();
