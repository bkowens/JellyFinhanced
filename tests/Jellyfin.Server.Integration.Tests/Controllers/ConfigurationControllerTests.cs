using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the Configuration controller.
/// Validates that server configuration can be read and updated through the full request pipeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConfigurationControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public ConfigurationControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetConfiguration_Authenticated_ReturnsServerConfiguration()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("System/Configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var config = await response.Content.ReadFromJsonAsync<ServerConfiguration>(_jsonOptions);
        Assert.NotNull(config);
    }

    [Fact]
    public async Task GetConfiguration_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Configuration");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateConfiguration_ValidPayload_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        // Retrieve current config first to avoid overwriting required fields
        using var getResponse = await client.GetAsync("System/Configuration");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var current = await getResponse.Content.ReadFromJsonAsync<ServerConfiguration>(_jsonOptions);
        Assert.NotNull(current);

        // Mutate a single benign field
        current.ServerName = "IntegrationTestServer";

        using var postResponse = await client.PostAsJsonAsync("System/Configuration", current, _jsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, postResponse.StatusCode);

        // Verify the change was persisted
        using var verifyResponse = await client.GetAsync("System/Configuration");
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var updated = await verifyResponse.Content.ReadFromJsonAsync<ServerConfiguration>(_jsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("IntegrationTestServer", updated.ServerName);
    }

    [Fact]
    public async Task UpdateConfiguration_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var config = new ServerConfiguration { ServerName = "AnonymousAttempt" };
        using var response = await client.PostAsJsonAsync("System/Configuration", config, _jsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDefaultMetadataOptions_Authenticated_ReturnsMetadataOptions()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("System/Configuration/MetadataOptions/Default");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var opts = await response.Content.ReadFromJsonAsync<MetadataOptions>(_jsonOptions);
        Assert.NotNull(opts);
    }

    [Fact]
    public async Task GetDefaultMetadataOptions_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Configuration/MetadataOptions/Default");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
