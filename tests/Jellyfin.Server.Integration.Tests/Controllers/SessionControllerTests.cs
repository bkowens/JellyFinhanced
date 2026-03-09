using System;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the Session controller.
/// Validates session listing and capability reporting through the full request pipeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SessionControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public SessionControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSessions_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("Sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSessions_Authenticated_ReturnsSessionList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("Sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var sessions = await response.Content.ReadFromJsonAsync<SessionInfoDto[]>(_jsonOptions);
        Assert.NotNull(sessions);
        // The test client itself creates at least one session
        Assert.NotEmpty(sessions);
    }

    [Fact]
    public async Task GetSessions_FilteredByControllableByUserId_ReturnsOk()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        var userDto = await AuthHelper.GetUserDtoAsync(client);
        var url = string.Format(
            CultureInfo.InvariantCulture,
            "Sessions?controllableByUserId={0}",
            userDto.Id);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostCapabilities_Authenticated_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        // Reporting client capabilities is a standard session handshake operation
        using var response = await client.PostAsync(
            "Sessions/Capabilities?supportsMediaControl=false&supportsContentUploading=false&supportsPersistentIdentifier=false",
            null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PostCapabilities_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.PostAsync("Sessions/Capabilities", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SendViewingEvent_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.PostAsync(
            $"Sessions/Viewing?itemId={Guid.NewGuid()}&itemType=Movie",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisplayContent_InvalidSessionId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.PostAsync(
            $"Sessions/nonexistent-session-id/Viewing?itemId={Guid.NewGuid()}&itemType=Movie",
            null);

        // Non-existent session returns 400 (Bad Request)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
