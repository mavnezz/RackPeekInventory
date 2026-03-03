using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RackPeekInventory.Models;

namespace RackPeekInventory.Client;

public class RackPeekClient(HttpClient http, IOptions<InventorySettings> settings, ILogger<RackPeekClient> logger)
{
    internal static readonly JsonSerializerOptions ApiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ImportResponse?> SendAsync(InventoryRequest request, CancellationToken ct = default)
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

        var payload = BuildApiPayload(request);

        logger.LogDebug("Sending inventory for {Hostname} to {ServerUrl}", request.Hostname, cfg.ServerUrl);

        var response = await http.PostAsJsonAsync("/api/inventory", payload, ApiJsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Server returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<ImportResponse>(ApiJsonOptions, ct);
        return result;
    }

    public static ImportRequest BuildApiPayload(InventoryRequest data)
    {
        var resources = new List<object>();

        // Hardware resource
        var hardware = new HardwarePayload
        {
            Kind = MapHardwareKind(data.HardwareType),
            Name = data.Hostname,
            Ipmi = data.Ipmi,
            Model = data.Model,
            Cpus = data.Cpus,
            Drives = data.Drives,
            Gpus = data.Gpus,
            Nics = data.Nics,
            Tags = data.Tags,
            Labels = data.Labels,
            Notes = data.Notes,
        };

        if (data.RamGb != null || data.RamMts != null)
            hardware.Ram = new RamPayload { Size = data.RamGb, Mts = data.RamMts };

        resources.Add(hardware);

        // System resource (optional — created when system fields are set)
        if (data.SystemType != null || data.Os != null)
        {
            resources.Add(new SystemPayload
            {
                Name = data.SystemName ?? $"{data.Hostname}-system",
                RunsOn = [data.Hostname],
                Type = data.SystemType,
                Os = data.Os,
                Cores = data.Cores,
                Ram = data.SystemRam,
                Drives = data.SystemDrives,
                Tags = data.Tags,
                Labels = data.Labels,
            });
        }

        return new ImportRequest
        {
            Json = new ResourceRoot { Resources = resources }
        };
    }

    private static string MapHardwareKind(string type) => type.ToLowerInvariant() switch
    {
        "server" => "Server",
        "desktop" => "Desktop",
        "laptop" => "Laptop",
        _ => char.ToUpperInvariant(type[0]) + type[1..]
    };
}
