using RackPeekInventory.Models;

namespace RackPeekInventory.Collectors;

public interface ISystemCollector
{
    Task<InventoryRequest> CollectAsync(CancellationToken ct = default);
}
