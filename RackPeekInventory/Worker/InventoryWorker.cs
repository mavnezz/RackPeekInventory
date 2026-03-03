using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RackPeekInventory.Client;
using RackPeekInventory.Collectors;

namespace RackPeekInventory.Worker;

public class InventoryWorker(
    ISystemCollector collector,
    RackPeekClient client,
    IOptions<InventorySettings> settings,
    ILogger<InventoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(settings.Value.IntervalSeconds);
        logger.LogInformation("Daemon mode started. Interval: {Interval}s", interval.TotalSeconds);

        // Run immediately on start, then on interval
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var data = await collector.CollectAsync(ct);
                var result = await client.SendAsync(data, ct);

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
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inventory cycle failed, retrying in {Interval}s", interval.TotalSeconds);
            }

            await Task.Delay(interval, ct);
        }

        logger.LogInformation("Daemon stopped");
    }
}
