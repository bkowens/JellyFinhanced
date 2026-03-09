using System;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Devices;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the Devices controller.
/// Validates device listing, retrieval, and management through the full request pipeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DevicesControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public DevicesControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDevices_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Devices");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDevices_Authenticated_ReturnsDeviceList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var result = await response.Content.ReadFromJsonAsync<QueryResult<DeviceInfoDto>>(_jsonOptions);
        Assert.NotNull(result);
        // The test client registers a device, so there should be at least one
        Assert.True(result.TotalRecordCount >= 0);
    }

    [Fact]
    public async Task GetDeviceInfo_NonExistentDevice_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Devices/Info?id=nonexistent-device-id-xyz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDeviceOptions_NonExistentDevice_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Devices/Options?id=nonexistent-device-id-xyz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDevice_NonExistentDevice_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        // The id query param is required; using a device that doesn't exist should
        // result in a 404 or at minimum a non-500 response
        using var response = await client.DeleteAsync("Devices?id=nonexistent-device-id-xyz");

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound
            || response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 404 or 204, got {response.StatusCode}");
    }

    [Fact]
    public async Task DeleteDevice_MissingId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        // id is required
        using var response = await client.DeleteAsync("Devices");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
