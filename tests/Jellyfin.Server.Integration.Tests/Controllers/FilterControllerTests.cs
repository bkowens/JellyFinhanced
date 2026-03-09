using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Querying;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the Filter controller.
/// Validates that query filter endpoints (both legacy and v2) work correctly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FilterControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public FilterControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // Legacy filter endpoint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetQueryFiltersLegacy_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Items/Filters");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQueryFiltersLegacy_Authenticated_ReturnsFilters()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Items/Filters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var filters = await response.Content.ReadFromJsonAsync<QueryFiltersLegacy>(_jsonOptions);
        Assert.NotNull(filters);
    }

    [Fact]
    public async Task GetQueryFiltersLegacy_WithUserId_ReturnsFilters()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        var userDto = await AuthHelper.GetUserDtoAsync(client);

        using var response = await client.GetAsync($"Items/Filters?userId={userDto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var filters = await response.Content.ReadFromJsonAsync<QueryFiltersLegacy>(_jsonOptions);
        Assert.NotNull(filters);
    }

    // -------------------------------------------------------------------------
    // v2 filter endpoint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetQueryFilters_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Items/Filters2");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetQueryFilters_Authenticated_ReturnsFilters()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Items/Filters2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var filters = await response.Content.ReadFromJsonAsync<QueryFilters>(_jsonOptions);
        Assert.NotNull(filters);
    }

    [Fact]
    public async Task GetQueryFilters_WithParentId_ReturnsFilters()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        var rootFolder = await AuthHelper.GetRootFolderDtoAsync(client);

        using var response = await client.GetAsync($"Items/Filters2?parentId={rootFolder.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var filters = await response.Content.ReadFromJsonAsync<QueryFilters>(_jsonOptions);
        Assert.NotNull(filters);
    }
}
