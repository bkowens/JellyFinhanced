using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Controller.Security;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using Xunit;
using Xunit.Priority;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the API Key controller.
/// Validates key creation, listing, and revocation through the full request pipeline.
/// </summary>
[Trait("Category", "Integration")]
[TestCaseOrderer(PriorityOrderer.Name, PriorityOrderer.Assembly)]
public sealed class ApiKeyControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;
    private static string? _createdKey;

    public ApiKeyControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetKeys_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Auth/Keys");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Priority(0)]
    public async Task GetKeys_Authenticated_ReturnsKeyList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Auth/Keys");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var result = await response.Content.ReadFromJsonAsync<QueryResult<AuthenticationInfo>>(_jsonOptions);
        Assert.NotNull(result);
    }

    [Fact]
    [Priority(1)]
    public async Task CreateKey_ValidAppName_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.PostAsync("Auth/Keys?app=IntegrationTestApp", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    [Priority(2)]
    public async Task GetKeys_AfterCreation_ReturnsNewKey()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Auth/Keys");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<QueryResult<AuthenticationInfo>>(_jsonOptions);
        Assert.NotNull(result);

        var testKey = result.Items.FirstOrDefault(k => k.AppName == "IntegrationTestApp");
        Assert.NotNull(testKey);

        // Store for revocation test
        _createdKey = testKey.AccessToken;
    }

    [Fact]
    [Priority(3)]
    public async Task RevokeKey_ExistingKey_ReturnsNoContent()
    {
        // Guard: only run if we successfully created and stored a key
        if (string.IsNullOrEmpty(_createdKey))
        {
            return;
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken!);

        using var response = await client.DeleteAsync($"Auth/Keys/{_createdKey}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task CreateKey_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.PostAsync("Auth/Keys?app=AnonymousApp", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokeKey_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.DeleteAsync("Auth/Keys/some-api-key-value");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
