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
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
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
        logger.LogInformation("{Action} {Kind} '{Name}'",
            result.Hardware.Action, result.Hardware.Kind, result.Hardware.Name);

        if (result.System != null)
        {
            logger.LogInformation("{Action} {Kind} '{Name}'",
                result.System.Action, result.System.Kind, result.System.Name);
        }
    }
    else
    {
        logger.LogError("Failed to send inventory data");
        Environment.Exit(1);
    }
}
