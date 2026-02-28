using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RackPeekInventory.Models;

namespace RackPeekInventory.Collectors;

public class WindowsCollector(IOptions<InventorySettings> settings, ILogger<WindowsCollector> logger) : ISystemCollector
{
    public Task<InventoryRequest> CollectAsync(CancellationToken ct = default)
    {
        var cfg = settings.Value;

        var request = new InventoryRequest
        {
            Hostname = Environment.MachineName,
            HardwareType = cfg.HardwareType,
            SystemType = cfg.SystemType,
            Tags = cfg.Tags,
            Labels = cfg.Labels,
        };

        request.Os = CollectOs();
        request.Model = CollectModel();
        request.Ipmi = CollectIpmi();

        var (ramGb, ramMts) = CollectRam();
        request.RamGb = ramGb;
        request.RamMts = ramMts;

        request.Cpus = CollectCpus();
        request.Cores = request.Cpus?.Sum(c => c.Threads ?? c.Cores ?? 0);
        request.SystemRam = ramGb;
        request.Drives = CollectDrives();
        request.SystemDrives = request.Drives;
        request.Gpus = CollectGpus();
        request.Nics = CollectNics();

        return Task.FromResult(request);
    }

#if WINDOWS
    private string? CollectOs()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
                return obj["Caption"]?.ToString();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_OperatingSystem");
        }

        return RuntimeInformation.OSDescription;
    }

    private string? CollectModel()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString();
                if (!string.IsNullOrEmpty(model) && model != "System Product Name" && model != "To Be Filled By O.E.M.")
                    return model;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_ComputerSystem for Model");
        }

        return null;
    }

    private bool? CollectIpmi()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_BMC");
            return searcher.Get().Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private (double? GigaBytes, int? Mts) CollectRam()
    {
        double? gb = null;
        int? mts = null;

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                if (obj["TotalPhysicalMemory"] is ulong bytes)
                    gb = Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_ComputerSystem for RAM");
        }

        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Speed FROM Win32_PhysicalMemory");
            foreach (var obj in searcher.Get())
            {
                if (obj["Speed"] is uint speed && speed > 0)
                {
                    mts = (int)speed;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_PhysicalMemory for speed");
        }

        return (gb, mts);
    }

    private List<InventoryCpu>? CollectCpus()
    {
        try
        {
            var cpus = new List<InventoryCpu>();
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

            foreach (var obj in searcher.Get())
            {
                cpus.Add(new InventoryCpu
                {
                    Model = obj["Name"]?.ToString()?.Trim(),
                    Cores = obj["NumberOfCores"] is uint cores ? (int)cores : null,
                    Threads = obj["NumberOfLogicalProcessors"] is uint threads ? (int)threads : null
                });
            }

            return cpus.Count > 0 ? cpus : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_Processor");
            return null;
        }
    }

    private List<InventoryDrive>? CollectDrives()
    {
        try
        {
            var drives = new List<InventoryDrive>();
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Model, Size, MediaType, InterfaceType FROM Win32_DiskDrive");

            foreach (var obj in searcher.Get())
            {
                var sizeBytes = obj["Size"] is ulong s ? s : 0UL;
                var sizeGb = (int)(sizeBytes / 1_000_000_000);
                var mediaType = obj["MediaType"]?.ToString() ?? "";
                var interfaceType = obj["InterfaceType"]?.ToString() ?? "";

                var driveType = interfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "nvme"
                    : interfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase) ? "usb"
                    : mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "ssd"
                    : mediaType.Contains("Fixed", StringComparison.OrdinalIgnoreCase) ? "hdd"
                    : "ssd";

                drives.Add(new InventoryDrive
                {
                    Type = driveType,
                    Size = sizeGb > 0 ? sizeGb : null
                });
            }

            return drives.Count > 0 ? drives : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_DiskDrive");
            return null;
        }
    }

    private List<InventoryGpu>? CollectGpus()
    {
        try
        {
            var gpus = new List<InventoryGpu>();
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, AdapterRAM FROM Win32_VideoController");

            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                // Skip Microsoft Basic Display Adapter
                if (name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                    continue;

                int? vram = null;
                if (obj["AdapterRAM"] is uint adapterRam && adapterRam > 0)
                    vram = (int)(adapterRam / 1_073_741_824);

                gpus.Add(new InventoryGpu
                {
                    Model = name,
                    Vram = vram
                });
            }

            return gpus.Count > 0 ? gpus : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_VideoController");
            return null;
        }
    }

    private List<InventoryNic>? CollectNics()
    {
        try
        {
            var nics = new List<InventoryNic>();
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT Name, Speed, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");

            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                // Skip virtual/tunnel adapters
                if (name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tunnel", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                    continue;

                double? speed = null;
                if (obj["Speed"] is ulong speedBps && speedBps > 0)
                    speed = speedBps / 1_000_000_000.0; // bps to Gbps

                var nicType = speed switch
                {
                    >= 100 => "qsfp28",
                    >= 25 => "sfp28",
                    >= 10 => "sfp+",
                    >= 1 => "rj45",
                    _ => "rj45"
                };

                nics.Add(new InventoryNic
                {
                    Type = nicType,
                    Speed = speed,
                    Ports = 1
                });
            }

            return nics.Count > 0 ? nics : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not query Win32_NetworkAdapter");
            return null;
        }
    }

#else
    // Fallback stubs for non-Windows builds
    private string? CollectOs() => RuntimeInformation.OSDescription;
    private string? CollectModel() => null;
    private bool? CollectIpmi() => false;
    private (double?, int?) CollectRam() => (null, null);
    private List<InventoryCpu>? CollectCpus() => null;
    private List<InventoryDrive>? CollectDrives() => null;
    private List<InventoryGpu>? CollectGpus() => null;
    private List<InventoryNic>? CollectNics() => null;
#endif
}
