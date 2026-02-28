namespace RackPeekInventory;

public class InventorySettings
{
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string ApiKey { get; set; } = "";
    public string HardwareType { get; set; } = "server";
    public int IntervalSeconds { get; set; } = 300;
    public string? SystemType { get; set; } = "baremetal";
    public string[]? Tags { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
}
