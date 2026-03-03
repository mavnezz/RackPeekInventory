namespace RackPeekInventory.Models;

/// <summary>
/// API request payload matching the server's ImportYamlRequest format.
/// </summary>
public class ImportRequest
{
    public ResourceRoot? Json { get; set; }
}

public class ResourceRoot
{
    public int Version { get; set; } = 2;
    public List<object> Resources { get; set; } = new();
}

public class HardwarePayload
{
    public string Kind { get; set; } = "Server";
    public required string Name { get; set; }
    public string[]? Tags { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public string? Notes { get; set; }
    public RamPayload? Ram { get; set; }
    public bool? Ipmi { get; set; }
    public string? Model { get; set; }
    public List<InventoryCpu>? Cpus { get; set; }
    public List<InventoryDrive>? Drives { get; set; }
    public List<InventoryGpu>? Gpus { get; set; }
    public List<InventoryNic>? Nics { get; set; }
}

public class SystemPayload
{
    public string Kind { get; set; } = "System";
    public required string Name { get; set; }
    public List<string>? RunsOn { get; set; }
    public string[]? Tags { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public string? Type { get; set; }
    public string? Os { get; set; }
    public int? Cores { get; set; }
    public double? Ram { get; set; }
    public List<InventoryDrive>? Drives { get; set; }
}

public class RamPayload
{
    public double? Size { get; set; }
    public int? Mts { get; set; }
}
