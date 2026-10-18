using System;
using System.Net.Http.Json;
using TelemetryCollectorService.Core.Interfaces;
using TelemetryCollectorService.Core.Models;

namespace TelemetryCollectorService.Agent.Services
{
    public interface IPublisher
    {
        Task Publish(IMetricData metric);
    }
    public class MachineMetricsPublisher: IPublisher
    {
        private readonly HttpClient _client;
        private readonly string _apiUrl;
        public MachineMetricsPublisher(IConfiguration configuration)
        {
            this._client = new HttpClient();
            this._apiUrl = configuration["ApiUrl"] ?? "";
        }
        public async Task Publish(IMetricData metric)
        {
            try
            {
                HttpResponseMessage response = await this._client.PostAsJsonAsync(this._apiUrl, metric);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"The data has been sent to {this._apiUrl}");
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error: {error}");
                }
            }catch(Exception ex)
            {
                Console.WriteLine($"Error while estabilishing connection with server: {ex.Message}");
            }
        }
    }
}