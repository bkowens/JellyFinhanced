using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Globalization;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the Localization controller.
/// Validates that localization data (cultures, countries, ratings, options) is served correctly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LocalizationControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public LocalizationControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCultures_Authenticated_ReturnsNonEmptyList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Localization/Cultures");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var cultures = await response.Content.ReadFromJsonAsync<CultureDto[]>(_jsonOptions);
        Assert.NotNull(cultures);
        Assert.NotEmpty(cultures);

        // Spot-check that English exists
        Assert.Contains(cultures, c => c.TwoLetterISOLanguageName == "en");
    }

    [Fact]
    public async Task GetCultures_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Localization/Cultures");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCountries_Authenticated_ReturnsNonEmptyList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Localization/Countries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var countries = await response.Content.ReadFromJsonAsync<CountryInfo[]>(_jsonOptions);
        Assert.NotNull(countries);
        Assert.NotEmpty(countries);
    }

    [Fact]
    public async Task GetCountries_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Localization/Countries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetParentalRatings_Authenticated_ReturnsNonEmptyList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Localization/ParentalRatings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var ratings = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(ratings.ValueKind == JsonValueKind.Array, "Expected a JSON array");
        Assert.True(ratings.GetArrayLength() > 0, "Expected a non-empty array");
    }

    [Fact]
    public async Task GetLocalizationOptions_Authenticated_ReturnsOptions()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Localization/Options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var options = await response.Content.ReadFromJsonAsync<LocalizationOption[]>(_jsonOptions);
        Assert.NotNull(options);
        Assert.NotEmpty(options);
    }
}
