using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Search;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the Search controller.
/// Validates search hint retrieval through the full request pipeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SearchControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public SearchControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSearchHints_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Search/Hints?searchTerm=test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSearchHints_EmptyLibrary_ReturnsEmptyResult()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Search/Hints?searchTerm=anyterm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var result = await response.Content.ReadFromJsonAsync<SearchHintResult>(_jsonOptions);
        Assert.NotNull(result);
        // Empty library, so no results
        Assert.Equal(0, result.TotalRecordCount);
    }

    [Fact]
    public async Task GetSearchHints_WithPagination_ReturnsOk()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Search/Hints?searchTerm=test&startIndex=0&limit=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SearchHintResult>(_jsonOptions);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetSearchHints_MissingSearchTerm_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        // searchTerm is [Required] on the endpoint
        using var response = await client.GetAsync("Search/Hints");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
