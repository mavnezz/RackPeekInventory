using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RackPeekInventory;
using RackPeekInventory.Client;
using RackPeekInventory.Collectors;
using RackPeekInventory.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<InventorySettings>(builder.Configuration.GetSection("Inventory"));

// Platform-specific collector
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    builder.Services.AddSingleton<ISystemCollector, WindowsCollector>();
else
    builder.Services.AddSingleton<ISystemCollector, LinuxCollector>();

builder.Services.AddHttpClient<RackPeekClient>();

var isDaemon = args.Contains("--daemon", StringComparer.OrdinalIgnoreCase);
var isDryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
var isVerbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

if (isVerbose)
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}

if (isDaemon)
{
    builder.Services.AddHostedService<InventoryWorker>();
    var host = builder.Build();
    await host.RunAsync();
}
else
{
    var host = builder.Build();
    var collector = host.Services.GetRequiredService<ISystemCollector>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var settings = host.Services.GetRequiredService<IOptions<InventorySettings>>().Value;

    logger.LogInformation("Collecting system inventory...");
    var data = await collector.CollectAsync();

    if (isDryRun)
    {
        var payload = RackPeekClient.BuildApiPayload(data);
        var dryRunOptions = new JsonSerializerOptions(RackPeekClient.ApiJsonOptions) { WriteIndented = true };
        var json = JsonSerializer.Serialize(payload, dryRunOptions);
        Console.WriteLine(json);
        return;
    }

    if (string.IsNullOrWhiteSpace(settings.ApiKey))
    {
        logger.LogError("API key is not configured. Use --Inventory:ApiKey=<key> or set Inventory__ApiKey environment variable");
        Environment.Exit(1);
        return;
    }

    var client = host.Services.GetRequiredService<RackPeekClient>();
    var result = await client.SendAsync(data);

    if (result != null)
    {
        foreach (var name in result.Added)
            logger.LogInformation("Added '{Name}'", name);
        foreach (var name in result.Updated)
            logger.LogInformation("Updated '{Name}'", name);
        foreach (var name in result.Replaced)
            logger.LogInformation("Replaced '{Name}'", name);

        if (result.Added.Count == 0 && result.Updated.Count == 0 && result.Replaced.Count == 0)
            logger.LogInformation("No changes");
    }
    else
    {
        logger.LogError("Failed to send inventory data");
        Environment.Exit(1);
    }
}
