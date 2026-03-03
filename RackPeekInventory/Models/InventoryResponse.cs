namespace RackPeekInventory.Models;

/// <summary>
/// API response matching the server's ImportYamlResponse format.
/// </summary>
public class ImportResponse
{
    public List<string> Added { get; set; } = new();
    public List<string> Updated { get; set; } = new();
    public List<string> Replaced { get; set; } = new();
    public Dictionary<string, string> OldYaml { get; set; } = new();
    public Dictionary<string, string> NewYaml { get; set; } = new();
}
