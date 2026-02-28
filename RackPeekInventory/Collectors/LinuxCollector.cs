using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RackPeekInventory.Models;

namespace RackPeekInventory.Collectors;

public class LinuxCollector(IOptions<InventorySettings> settings, ILogger<LinuxCollector> logger) : ISystemCollector
{
    public async Task<InventoryRequest> CollectAsync(CancellationToken ct = default)
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

        request.Os = await CollectOs(ct);
        request.Model = CollectModel();
        request.Ipmi = CollectIpmi();

        var (ramGb, ramMts) = await CollectRam(ct);
        request.RamGb = ramGb;
        request.RamMts = ramMts;

        request.Cpus = await CollectCpus(ct);
        request.Cores = request.Cpus?.Sum(c => c.Threads ?? c.Cores ?? 0);
        request.SystemRam = ramGb;
        request.Drives = await CollectDrives(ct);
        request.SystemDrives = request.Drives;
        request.Gpus = CollectGpus();
        request.Nics = CollectNics();

        return request;
    }

    internal async Task<string?> CollectOs(CancellationToken ct)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync("/etc/os-release", ct);
            foreach (var line in lines)
            {
                if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                {
                    return line["PRETTY_NAME=".Length..].Trim('"');
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read /etc/os-release");
        }

        return null;
    }

    internal string? CollectModel()
    {
        try
        {
            var path = "/sys/class/dmi/id/product_name";
            if (File.Exists(path))
            {
                var model = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(model) && model != "System Product Name" && model != "To Be Filled By O.E.M.")
                    return model;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read product_name");
        }

        return null;
    }

    internal bool? CollectIpmi()
    {
        return File.Exists("/dev/ipmi0") || File.Exists("/dev/ipmi/0") || File.Exists("/dev/ipmi/mi/0");
    }

    internal async Task<(double? GigaBytes, int? Mts)> CollectRam(CancellationToken ct)
    {
        double? gb = null;
        int? mts = null;

        try
        {
            var lines = await File.ReadAllLinesAsync("/proc/meminfo", ct);
            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kB))
                    {
                        gb = Math.Round(kB / 1024.0 / 1024.0, 1);
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read /proc/meminfo");
        }

        // Try dmidecode for RAM speed (requires root)
        try
        {
            var output = await RunCommandAsync("dmidecode", "-t memory", ct);
            if (output != null)
            {
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Speed:", StringComparison.Ordinal) && !trimmed.Contains("Unknown"))
                    {
                        var speedStr = trimmed["Speed:".Length..].Trim().Split(' ')[0];
                        if (int.TryParse(speedStr, out var speed) && speed > 0)
                        {
                            // dmidecode reports MHz for DDR, MT/s = MHz * 2 for DDR
                            // but newer dmidecode already reports MT/s
                            mts = trimmed.Contains("MT/s") ? speed : speed * 2;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not get RAM speed via dmidecode");
        }

        return (gb, mts);
    }

    internal async Task<List<InventoryCpu>?> CollectCpus(CancellationToken ct)
    {
        try
        {
            var text = await File.ReadAllTextAsync("/proc/cpuinfo", ct);
            var cpuMap = new Dictionary<string, (string? Model, int Cores, int Threads)>();
            string? currentPhysicalId = null;
            string? currentModel = null;
            var coreIds = new HashSet<string>();
            int threadCount = 0;

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    // End of a processor block
                    if (currentPhysicalId != null)
                    {
                        if (!cpuMap.ContainsKey(currentPhysicalId))
                            cpuMap[currentPhysicalId] = (currentModel, 0, 0);

                        var entry = cpuMap[currentPhysicalId];
                        entry.Model ??= currentModel;
                        entry.Threads = threadCount;
                        cpuMap[currentPhysicalId] = entry;
                    }

                    currentPhysicalId = null;
                    currentModel = null;
                    continue;
                }

                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0) continue;
                var key = trimmed[..colonIdx].Trim();
                var value = trimmed[(colonIdx + 1)..].Trim();

                switch (key)
                {
                    case "physical id":
                        currentPhysicalId = value;
                        threadCount = cpuMap.TryGetValue(value, out var existing) ? existing.Threads + 1 : 1;
                        break;
                    case "model name":
                        currentModel = value;
                        break;
                    case "core id" when currentPhysicalId != null:
                        coreIds.Add($"{currentPhysicalId}:{value}");
                        break;
                }
            }

            // Handle last block
            if (currentPhysicalId != null)
            {
                if (!cpuMap.ContainsKey(currentPhysicalId))
                    cpuMap[currentPhysicalId] = (currentModel, 0, 0);

                var entry = cpuMap[currentPhysicalId];
                entry.Model ??= currentModel;
                entry.Threads = threadCount;
                cpuMap[currentPhysicalId] = entry;
            }

            if (cpuMap.Count == 0)
                return null;

            // Count cores per physical CPU
            var coresPerCpu = new Dictionary<string, int>();
            foreach (var coreKey in coreIds)
            {
                var physId = coreKey.Split(':')[0];
                coresPerCpu[physId] = coresPerCpu.GetValueOrDefault(physId) + 1;
            }

            return cpuMap.Select(kv => new InventoryCpu
            {
                Model = kv.Value.Model,
                Cores = coresPerCpu.GetValueOrDefault(kv.Key, kv.Value.Threads),
                Threads = kv.Value.Threads
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read /proc/cpuinfo");
            return null;
        }
    }

    internal async Task<List<InventoryDrive>?> CollectDrives(CancellationToken ct)
    {
        try
        {
            var output = await RunCommandAsync("lsblk", "--json -b -o NAME,SIZE,TYPE,ROTA,TRAN", ct);
            if (output == null) return null;

            using var doc = JsonDocument.Parse(output);
            var devices = doc.RootElement.GetProperty("blockdevices");
            var drives = new List<InventoryDrive>();

            foreach (var dev in devices.EnumerateArray())
            {
                var type = dev.GetProperty("type").GetString();
                if (type != "disk") continue;

                var sizeBytes = dev.GetProperty("size").GetInt64();
                var sizeGb = (int)(sizeBytes / 1_000_000_000);

                var rota = dev.TryGetProperty("rota", out var rotaProp) && rotaProp.ValueKind == JsonValueKind.True;
                var tran = dev.TryGetProperty("tran", out var tranProp) ? tranProp.GetString() : null;

                var driveType = tran?.ToLowerInvariant() switch
                {
                    "nvme" => "nvme",
                    "usb" => "usb",
                    "sata" => rota ? "hdd" : "ssd",
                    "sas" => "sas",
                    _ => rota ? "hdd" : "ssd"
                };

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
            logger.LogDebug(ex, "Could not collect drives via lsblk");
            return null;
        }
    }

    internal List<InventoryGpu>? CollectGpus()
    {
        try
        {
            var gpus = new List<InventoryGpu>();
            var drmPath = "/sys/class/drm";

            if (!Directory.Exists(drmPath)) return null;

            // Find unique PCI devices behind /sys/class/drm/card*
            var seenPciDevices = new HashSet<string>();
            foreach (var cardDir in Directory.GetDirectories(drmPath, "card[0-9]*"))
            {
                var deviceLink = Path.Combine(cardDir, "device");
                if (!Directory.Exists(deviceLink)) continue;

                var realPath = Path.GetFullPath(deviceLink);
                if (!seenPciDevices.Add(realPath)) continue;

                // Read vendor/device or use lspci
                var model = ReadGpuModel(cardDir);
                var vram = ReadGpuVram(cardDir);

                if (model != null)
                {
                    gpus.Add(new InventoryGpu
                    {
                        Model = model,
                        Vram = vram
                    });
                }
            }

            return gpus.Count > 0 ? gpus : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not collect GPUs");
            return null;
        }
    }

    private string? ReadGpuModel(string cardDir)
    {
        try
        {
            // Try reading from uevent
            var ueventPath = Path.Combine(cardDir, "device", "uevent");
            if (File.Exists(ueventPath))
            {
                var lines = File.ReadAllLines(ueventPath);
                var pciSlot = lines.FirstOrDefault(l => l.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal));
                if (pciSlot != null)
                {
                    var slot = pciSlot["PCI_SLOT_NAME=".Length..];
                    // Use lspci to get the name
                    var result = RunCommandAsync("lspci", $"-s {slot}", CancellationToken.None).GetAwaiter().GetResult();
                    if (result != null)
                    {
                        // Format: "01:00.0 VGA compatible controller: NVIDIA Corporation ..."
                        var colonIdx = result.IndexOf(": ", StringComparison.Ordinal);
                        if (colonIdx >= 0)
                            return result[(colonIdx + 2)..].Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read GPU model for {CardDir}", cardDir);
        }

        return null;
    }

    private int? ReadGpuVram(string cardDir)
    {
        try
        {
            var memPath = Path.Combine(cardDir, "device", "mem_info_vram_total");
            if (File.Exists(memPath))
            {
                var text = File.ReadAllText(memPath).Trim();
                if (long.TryParse(text, out var bytes))
                    return (int)(bytes / 1_073_741_824); // bytes to GB
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read GPU VRAM for {CardDir}", cardDir);
        }

        return null;
    }

    internal List<InventoryNic>? CollectNics()
    {
        try
        {
            var netPath = "/sys/class/net";
            if (!Directory.Exists(netPath)) return null;

            var nics = new List<InventoryNic>();
            var skipPrefixes = new[] { "lo", "veth", "docker", "br-", "virbr", "vnet", "tun", "tap", "wg" };

            foreach (var ifaceDir in Directory.GetDirectories(netPath))
            {
                var name = Path.GetFileName(ifaceDir);
                if (skipPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Check if it's a physical device (has a device symlink pointing to PCI)
                var deviceLink = Path.Combine(ifaceDir, "device");
                if (!Directory.Exists(deviceLink))
                    continue;

                double? speed = null;
                var speedPath = Path.Combine(ifaceDir, "speed");
                if (File.Exists(speedPath))
                {
                    try
                    {
                        var speedText = File.ReadAllText(speedPath).Trim();
                        if (int.TryParse(speedText, out var mbps) && mbps > 0)
                            speed = mbps / 1000.0; // Mbps to Gbps
                    }
                    catch
                    {
                        // speed file might return -1 or error if link is down
                    }
                }

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
            logger.LogDebug(ex, "Could not collect NICs");
            return null;
        }
    }

    internal static async Task<string?> RunCommandAsync(string command, string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
