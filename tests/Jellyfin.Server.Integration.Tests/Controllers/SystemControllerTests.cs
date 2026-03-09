using System;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.System;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the System controller, covering health, info, ping, and log endpoints.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SystemControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public SystemControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // Public endpoints (no auth required)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPublicSystemInfo_Anonymous_ReturnsOk()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Info/Public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var info = await response.Content.ReadFromJsonAsync<PublicSystemInfo>(_jsonOptions);
        Assert.NotNull(info);
        Assert.NotEmpty(info.ServerName);
        Assert.NotEmpty(info.Version);
        Assert.NotEmpty(info.Id);
    }

    [Fact]
    public async Task PingSystem_Get_Anonymous_ReturnsServerName()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Response is a JSON-encoded string
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task PingSystem_Post_Anonymous_ReturnsServerName()
    {
        var client = _factory.CreateClient();

        using var response = await client.PostAsync("System/Ping", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    // -------------------------------------------------------------------------
    // Authenticated endpoints
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetSystemInfo_Authenticated_ReturnsFullInfo()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("System/Info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var info = await response.Content.ReadFromJsonAsync<SystemInfo>(_jsonOptions);
        Assert.NotNull(info);
        Assert.NotEmpty(info.ServerName);
        Assert.NotEmpty(info.Version);
        Assert.NotEmpty(info.Id);
    }

    [Fact]
    public async Task GetSystemInfo_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Info");

        // Requires FirstTimeSetupOrIgnoreParentalControl policy — anonymous is rejected
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetServerLogs_Authenticated_ReturnsLogList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("System/Logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetServerLogs_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEndpointInfo_Authenticated_ReturnsEndpointInfo()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("System/Endpoint");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetEndpointInfo_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Endpoint");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLogFile_NonExistent_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("System/Logs/Log?name=doesnotexist_xyz.log");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Destructive / admin endpoints: only verify auth rejection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RestartApplication_Anonymous_LocalAccess_ReturnsNoContent()
    {
        var client = _factory.CreateClient();

        using var response = await client.PostAsync("System/Restart", null);

        // LocalAccessOrRequiresElevation policy allows local connections without auth
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ShutdownApplication_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.PostAsync("System/Shutdown", null);

        // RequiresElevation policy requires admin auth even for local connections
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
