using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RackPeekInventory;
using RackPeekInventory.Client;
using RackPeekInventory.Models;

namespace Tests.Client;

public class RackPeekClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task SendAsync_sends_api_key_header()
    {
        string? capturedApiKey = null;
        var handler = new MockHttpHandler((req, _) =>
        {
            capturedApiKey = req.Headers.GetValues("X-Api-Key").FirstOrDefault();
            var response = new ImportResponse { Added = ["srv01"] };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response, options: JsonOptions)
            });
        });

        var client = CreateClient(handler, new InventorySettings
        {
            ServerUrl = "http://localhost:5000",
            ApiKey = "my-secret-key"
        });

        await client.SendAsync(new InventoryRequest { Hostname = "srv01" });

        Assert.Equal("my-secret-key", capturedApiKey);
    }

    [Fact]
    public async Task SendAsync_posts_to_api_inventory()
    {
        Uri? capturedUri = null;
        var handler = new MockHttpHandler((req, _) =>
        {
            capturedUri = req.RequestUri;
            var response = new ImportResponse { Added = ["srv01"] };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response, options: JsonOptions)
            });
        });

        var client = CreateClient(handler, new InventorySettings
        {
            ServerUrl = "http://localhost:5000",
            ApiKey = "test-key"
        });

        await client.SendAsync(new InventoryRequest { Hostname = "srv01" });

        Assert.NotNull(capturedUri);
        Assert.Equal("/api/inventory", capturedUri.AbsolutePath);
    }

    [Fact]
    public async Task SendAsync_returns_null_when_api_key_empty()
    {
        var handler = new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var client = CreateClient(handler, new InventorySettings
        {
            ServerUrl = "http://localhost:5000",
            ApiKey = ""
        });

        var result = await client.SendAsync(new InventoryRequest { Hostname = "srv01" });

        Assert.Null(result);
    }

    [Fact]
    public async Task SendAsync_returns_null_on_server_error()
    {
        var handler = new MockHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server error")
            }));

        var client = CreateClient(handler, new InventorySettings
        {
            ServerUrl = "http://localhost:5000",
            ApiKey = "test-key"
        });

        var result = await client.SendAsync(new InventoryRequest { Hostname = "srv01" });

        Assert.Null(result);
    }

    [Fact]
    public async Task SendAsync_deserializes_response()
    {
        var handler = new MockHttpHandler((_, _) =>
        {
            var response = new ImportResponse
            {
                Added = ["srv01", "srv01-system"],
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response, options: JsonOptions)
            });
        });

        var client = CreateClient(handler, new InventorySettings
        {
            ServerUrl = "http://localhost:5000",
            ApiKey = "test-key"
        });

        var result = await client.SendAsync(new InventoryRequest { Hostname = "srv01" });

        Assert.NotNull(result);
        Assert.Equal(["srv01", "srv01-system"], result.Added);
        Assert.Empty(result.Updated);
        Assert.Empty(result.Replaced);
    }

    [Fact]
    public void BuildApiPayload_creates_hardware_resource()
    {
        var data = new InventoryRequest
        {
            Hostname = "srv01",
            HardwareType = "server",
            RamGb = 64,
            RamMts = 3200,
            Ipmi = true,
            Model = "ProLiant DL380",
            Cpus = [new InventoryCpu { Model = "Xeon Gold", Cores = 16, Threads = 32 }],
            Drives = [new InventoryDrive { Type = "nvme", Size = 512 }],
            Gpus = [new InventoryGpu { Model = "RTX 4000", Vram = 16 }],
            Nics = [new InventoryNic { Type = "rj45", Speed = 1.0, Ports = 2 }],
            Tags = ["prod"],
            Labels = new Dictionary<string, string> { ["env"] = "production" },
        };

        var payload = RackPeekClient.BuildApiPayload(data);

        Assert.NotNull(payload.Json);
        Assert.Equal(2, payload.Json.Version);
        Assert.Single(payload.Json.Resources); // no system fields → only hardware

        var hw = Assert.IsType<HardwarePayload>(payload.Json.Resources[0]);
        Assert.Equal("Server", hw.Kind);
        Assert.Equal("srv01", hw.Name);
        Assert.NotNull(hw.Ram);
        Assert.Equal(64, hw.Ram.Size);
        Assert.Equal(3200, hw.Ram.Mts);
        Assert.True(hw.Ipmi);
        Assert.Equal("ProLiant DL380", hw.Model);
        Assert.Single(hw.Cpus!);
        Assert.Equal("Xeon Gold", hw.Cpus![0].Model);
        Assert.Single(hw.Drives!);
        Assert.Single(hw.Gpus!);
        Assert.Single(hw.Nics!);
        Assert.Equal(["prod"], hw.Tags);
        Assert.Equal("production", hw.Labels!["env"]);
    }

    [Fact]
    public void BuildApiPayload_creates_system_resource_when_system_fields_set()
    {
        var data = new InventoryRequest
        {
            Hostname = "srv01",
            HardwareType = "server",
            SystemType = "baremetal",
            Os = "Ubuntu 22.04",
            Cores = 32,
            SystemRam = 64,
            SystemDrives = [new InventoryDrive { Type = "nvme", Size = 512 }],
        };

        var payload = RackPeekClient.BuildApiPayload(data);

        Assert.Equal(2, payload.Json!.Resources.Count);

        var sys = Assert.IsType<SystemPayload>(payload.Json.Resources[1]);
        Assert.Equal("System", sys.Kind);
        Assert.Equal("srv01-system", sys.Name);
        Assert.Equal(["srv01"], sys.RunsOn);
        Assert.Equal("baremetal", sys.Type);
        Assert.Equal("Ubuntu 22.04", sys.Os);
        Assert.Equal(32, sys.Cores);
        Assert.Equal(64, sys.Ram);
        Assert.Single(sys.Drives!);
    }

    [Fact]
    public void BuildApiPayload_maps_hardware_kind_correctly()
    {
        var serverPayload = RackPeekClient.BuildApiPayload(new InventoryRequest { Hostname = "h", HardwareType = "server" });
        var desktopPayload = RackPeekClient.BuildApiPayload(new InventoryRequest { Hostname = "h", HardwareType = "desktop" });
        var laptopPayload = RackPeekClient.BuildApiPayload(new InventoryRequest { Hostname = "h", HardwareType = "laptop" });

        Assert.Equal("Server", ((HardwarePayload)serverPayload.Json!.Resources[0]).Kind);
        Assert.Equal("Desktop", ((HardwarePayload)desktopPayload.Json!.Resources[0]).Kind);
        Assert.Equal("Laptop", ((HardwarePayload)laptopPayload.Json!.Resources[0]).Kind);
    }

    [Fact]
    public void BuildApiPayload_serializes_to_expected_json()
    {
        var data = new InventoryRequest
        {
            Hostname = "srv01",
            HardwareType = "server",
            RamGb = 32,
            SystemType = "baremetal",
            Os = "Ubuntu 22.04",
        };

        var payload = RackPeekClient.BuildApiPayload(data);
        var json = JsonSerializer.Serialize(payload, RackPeekClient.ApiJsonOptions);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("json", out var jsonProp));
        Assert.Equal(2, jsonProp.GetProperty("version").GetInt32());

        var resources = jsonProp.GetProperty("resources");
        Assert.Equal(2, resources.GetArrayLength());

        var hw = resources[0];
        Assert.Equal("Server", hw.GetProperty("kind").GetString());
        Assert.Equal("srv01", hw.GetProperty("name").GetString());
        Assert.Equal(32, hw.GetProperty("ram").GetProperty("size").GetDouble());

        var sys = resources[1];
        Assert.Equal("System", sys.GetProperty("kind").GetString());
        Assert.Equal("srv01-system", sys.GetProperty("name").GetString());
        Assert.Equal("baremetal", sys.GetProperty("type").GetString());
    }

    private static RackPeekClient CreateClient(MockHttpHandler handler, InventorySettings settings)
    {
        var http = new HttpClient(handler);
        var opts = Options.Create(settings);
        return new RackPeekClient(http, opts, NullLogger<RackPeekClient>.Instance);
    }
}

internal class MockHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => handler(request, ct);
}
