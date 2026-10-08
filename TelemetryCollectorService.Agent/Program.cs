using TelemetryCollectorService.Agent;
using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json.Nodes;

var builder = Host.CreateApplicationBuilder(args);

string jsonString = await File.ReadAllTextAsync("config.json");
JsonNode doc = JsonNode.Parse(jsonString);
string agentId = doc["agentId"]?.ToString() ?? Guid.NewGuid().ToString();

builder.Services.AddSingleton<HubConnection>(sp =>
{
    var connection = new HubConnectionBuilder()
        .WithUrl("http://localhost:5034/Hub")
        .WithAutomaticReconnect()
        .Build();

    connection.On<string>("TurnOffPc", (time) =>
    {
        Console.WriteLine($"Received turning off command. Time: {time}");
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
