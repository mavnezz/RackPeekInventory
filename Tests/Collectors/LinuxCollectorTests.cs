using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RackPeekInventory;
using RackPeekInventory.Collectors;

namespace Tests.Collectors;

public class LinuxCollectorTests
{
    private static LinuxCollector CreateCollector(InventorySettings? settings = null)
    {
        var opts = Options.Create(settings ?? new InventorySettings());
        return new LinuxCollector(opts, NullLogger<LinuxCollector>.Instance);
    }

    [Fact]
    public async Task CollectAsync_returns_hostname()
    {
        var collector = CreateCollector();
        var result = await collector.CollectAsync();

        Assert.Equal(Environment.MachineName, result.Hostname);
    }

    [Fact]
    public async Task CollectAsync_uses_settings_hardware_type()
    {
        var collector = CreateCollector(new InventorySettings { HardwareType = "desktop" });
        var result = await collector.CollectAsync();

        Assert.Equal("desktop", result.HardwareType);
    }

    [Fact]
    public async Task CollectAsync_uses_settings_system_type()
    {
        var collector = CreateCollector(new InventorySettings { SystemType = "vm" });
        var result = await collector.CollectAsync();

        Assert.Equal("vm", result.SystemType);
    }

    [Fact]
    public async Task CollectAsync_uses_tags_and_labels_from_settings()
    {
        var collector = CreateCollector(new InventorySettings
        {
            Tags = ["prod", "linux"],
            Labels = new Dictionary<string, string> { ["env"] = "production" }
        });

        var result = await collector.CollectAsync();

        Assert.Equal(["prod", "linux"], result.Tags);
        Assert.NotNull(result.Labels);
        Assert.Equal("production", result.Labels["env"]);
    }

    [Fact]
    public async Task CollectOs_reads_os_release()
    {
        if (!File.Exists("/etc/os-release")) return;

        var collector = CreateCollector();
        var os = await collector.CollectOs(CancellationToken.None);

        Assert.NotNull(os);
        Assert.NotEmpty(os);
    }

    [Fact]
    public async Task CollectCpus_parses_proc_cpuinfo()
    {
        if (!File.Exists("/proc/cpuinfo")) return;

        var collector = CreateCollector();
        var cpus = await collector.CollectCpus(CancellationToken.None);

        Assert.NotNull(cpus);
        Assert.NotEmpty(cpus);
        Assert.All(cpus, cpu =>
        {
            Assert.NotNull(cpu.Model);
            Assert.True(cpu.Cores > 0);
            Assert.True(cpu.Threads > 0);
        });
    }

    [Fact]
    public async Task CollectRam_reads_proc_meminfo()
    {
        if (!File.Exists("/proc/meminfo")) return;

        var collector = CreateCollector();
        var (gb, _) = await collector.CollectRam(CancellationToken.None);

        Assert.NotNull(gb);
        Assert.True(gb > 0);
    }

    [Fact]
    public async Task CollectDrives_returns_drives_if_lsblk_available()
    {
        var lsblkCheck = await LinuxCollector.RunCommandAsync("which", "lsblk", CancellationToken.None);
        if (lsblkCheck == null) return;

        var collector = CreateCollector();
        var drives = await collector.CollectDrives(CancellationToken.None);

        // Drives might be null in containerized environments, but if present should be valid
        if (drives != null)
        {
            Assert.NotEmpty(drives);
            Assert.All(drives, d => Assert.NotNull(d.Type));
        }
    }

    [Fact]
    public void CollectNics_returns_nics_if_sys_class_net_exists()
    {
        if (!Directory.Exists("/sys/class/net")) return;

        var collector = CreateCollector();
        var nics = collector.CollectNics();

        // NICs might be null if all interfaces are virtual
        if (nics != null)
        {
            Assert.All(nics, nic => Assert.NotNull(nic.Type));
        }
    }

    [Fact]
    public async Task CollectAsync_sets_system_fields_from_hardware()
    {
        var collector = CreateCollector();
        var result = await collector.CollectAsync();

        // SystemRam should mirror RamGb
        Assert.Equal(result.RamGb, result.SystemRam);
        // SystemDrives should mirror Drives
        Assert.Equal(result.Drives, result.SystemDrives);
    }
}
