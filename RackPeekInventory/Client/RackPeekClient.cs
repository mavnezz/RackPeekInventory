using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RackPeekInventory.Models;

namespace RackPeekInventory.Client;

public class RackPeekClient(HttpClient http, IOptions<InventorySettings> settings, ILogger<RackPeekClient> logger)
{
    public async Task<InventoryResponse?> SendAsync(InventoryRequest request, CancellationToken ct = default)
    {
        var cfg = settings.Value;

        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            logger.LogError("API key is not configured. Set Inventory:ApiKey or Inventory__ApiKey");
            return null;
        }

        http.BaseAddress ??= new Uri(cfg.ServerUrl.TrimEnd('/'));

        if (!http.DefaultRequestHeaders.Contains("X-Api-Key"))
            http.DefaultRequestHeaders.Add("X-Api-Key", cfg.ApiKey);

        logger.LogDebug("Sending inventory for {Hostname} to {ServerUrl}", request.Hostname, cfg.ServerUrl);

        var response = await http.PostAsJsonAsync("/api/inventory", request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Server returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<InventoryResponse>(ct);
        return result;
    }
}
