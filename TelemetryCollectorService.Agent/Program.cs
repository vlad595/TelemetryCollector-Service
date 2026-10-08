using TelemetryCollectorService.Agent;
using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json.Nodes;
using System.Diagnostics;

var builder = Host.CreateApplicationBuilder(args);

string jsonString = await File.ReadAllTextAsync("config.json");
JsonNode doc = JsonNode.Parse(jsonString);
string agentId = doc["agentId"]?.ToString() ?? Guid.NewGuid().ToString();

builder.Services.AddSingleton<HubConnection>(sp =>
{
    var connection = new HubConnectionBuilder()
        .WithUrl("https://vqgbf86r-5034.euw.devtunnels.ms/Hub")
        .WithAutomaticReconnect()
        .Build();

    connection.On<string>("TurnOfPc", (time) =>
    {
        Console.WriteLine($"Received shutdown command. Time: {Convert.ToInt32(time) * 60}");
        Process.Start("shutdown.exe", $"/s /t {Convert.ToInt32(time) * 60}");
    });

    connection.On<string>("ExecuteFile", (path) =>
    {
        Process.Start(path);
    });

    return connection;
});

builder.Services.AddSingleton(agentId);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

var hubConnection = host.Services.GetRequiredService<HubConnection>();
await hubConnection.StartAsync();
await hubConnection.InvokeAsync("RegisterAgent", agentId);

host.Run();