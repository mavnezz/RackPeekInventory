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
            var response = new InventoryResponse
            {
                Hardware = new ResourceResult { Name = "srv01", Kind = "Server", Action = "created" }
            };
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
            var response = new InventoryResponse
            {
                Hardware = new ResourceResult { Name = "srv01", Kind = "Server", Action = "created" }
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
            var response = new InventoryResponse
            {
                Hardware = new ResourceResult { Name = "srv01", Kind = "Server", Action = "created" },
                System = new ResourceResult { Name = "srv01-system", Kind = "System", Action = "created" }
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
        Assert.Equal("srv01", result.Hardware.Name);
        Assert.Equal("Server", result.Hardware.Kind);
        Assert.Equal("created", result.Hardware.Action);
        Assert.NotNull(result.System);
        Assert.Equal("srv01-system", result.System.Name);
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
