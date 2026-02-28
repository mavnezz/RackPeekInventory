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
                    logger.LogInformation("{Action} {Kind} '{Name}'",
                        result.Hardware.Action, result.Hardware.Kind, result.Hardware.Name);

                    if (result.System != null)
                    {
                        logger.LogInformation("{Action} {Kind} '{Name}'",
                            result.System.Action, result.System.Kind, result.System.Name);
                    }
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
