using System;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the Image controller.
/// Validates image retrieval, info endpoints, and authentication enforcement
/// through the full request pipeline without requiring real media files.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ImageControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public ImageControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // Item image info
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetItemImageInfos_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync($"Items/{Guid.NewGuid()}/Images");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetItemImageInfos_NonExistentItem_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync($"Items/{Guid.NewGuid()}/Images");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Item image serving
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetItemImage_NonExistentItem_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync($"Items/{Guid.NewGuid()}/Images/Primary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // User image
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetUserImage_Anonymous_NonExistentUser_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        // GET UserImage is a public endpoint (no [Authorize] attribute).
        // A non-existent userId returns 404.
        using var response = await client.GetAsync($"UserImage?userId={Guid.NewGuid()}&imageType=Primary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUserImage_CurrentUser_ReturnsNotFoundOrNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        var userDto = await AuthHelper.GetUserDtoAsync(client);

        using var response = await client.GetAsync($"UserImage?userId={userDto.Id}&imageType=Primary");

        // The test user has no image, so either 404 or the redirect/no-image response
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound
            || response.StatusCode == HttpStatusCode.OK,
            $"Unexpected status {response.StatusCode}");
    }

    [Fact]
    public async Task DeleteUserImage_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.DeleteAsync("UserImage");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Named entity images (Persons, Genres, Studios, Artists, MusicGenres)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Persons/Nobody/Images/Primary")]
    [InlineData("Genres/NoGenre/Images/Primary")]
    [InlineData("MusicGenres/NoMusicGenre/Images/Primary")]
    [InlineData("Studios/NoStudio/Images/Primary")]
    [InlineData("Artists/NoArtist/Images/Primary/0")]
    public async Task GetNamedEntityImage_NonExistentEntity_ReturnsNotFound(string path)
    {
        var client = _factory.CreateClient();

        // These endpoints accept unauthenticated requests but return 404 for unknown entities
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
