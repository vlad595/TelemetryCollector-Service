using System;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelemetryCollectorService.Core.Interfaces;

namespace TelemetryCollectorService.Agent.Services
{
    public interface IPublisher
    {
        Task PublishAsync(IMetricData metric);
    }

    public class MachineMetricsPublisher : IPublisher
    {
        private readonly HttpClient _client;
        private readonly string _apiUrl;
        private readonly ILogger<MachineMetricsPublisher> _logger;

        public MachineMetricsPublisher(HttpClient client, IConfiguration configuration, ILogger<MachineMetricsPublisher> logger)
        {
            _client = client;

            _apiUrl = configuration["ApiUrl"] ?? throw new ArgumentNullException("ApiUrl config is missing");
            _logger = logger;
        }

        public async Task PublishAsync(IMetricData metric)
        {
            try
            {
                HttpResponseMessage response = await _client.PostAsJsonAsync(_apiUrl, (object)metric);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("The data has been sent to {ApiUrl}", _apiUrl);
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Server returned error: {ErrorStatusCode} - {Error}", response.StatusCode, error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while establishing connection with server");
            }
        }
    }
}