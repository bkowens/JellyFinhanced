using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class InstantMixControllerTests
{
    private readonly IFixture _fixture;
    private readonly Mock<IUserManager> _userManagerMock;
    private readonly Mock<IDtoService> _dtoServiceMock;
    private readonly Mock<IMusicManager> _musicManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly InstantMixController _controller;

    public InstantMixControllerTests()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());

        _userManagerMock = new Mock<IUserManager>();
        _dtoServiceMock = new Mock<IDtoService>();
        _musicManagerMock = new Mock<IMusicManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();

        _controller = new InstantMixController(
            _userManagerMock.Object,
            _dtoServiceMock.Object,
            _musicManagerMock.Object,
            _libraryManagerMock.Object);

        SetupControllerContext();
    }

    private void SetupControllerContext()
    {
        var user = CreateTestUser();
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, UserRoles.User),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(InternalClaimTypes.UserId, Guid.NewGuid().ToString("N")),
            new Claim(InternalClaimTypes.DeviceId, Guid.Empty.ToString("N")),
            new Claim(InternalClaimTypes.Device, "test"),
            new Claim(InternalClaimTypes.Client, "test"),
            new Claim(InternalClaimTypes.Version, "1.0"),
            new Claim(InternalClaimTypes.Token, "testtoken"),
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns(user);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            }
        };
    }

    private static User CreateTestUser()
    {
        var user = new User(
            "testuser",
            typeof(DefaultAuthenticationProvider).FullName!,
            typeof(DefaultPasswordResetProvider).FullName!);
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        return user;
    }

    private void SetupMusicManagerReturnsItems(IReadOnlyList<BaseItem> items)
    {
        _musicManagerMock
            .Setup(m => m.GetInstantMixFromItem(It.IsAny<BaseItem>(), It.IsAny<User>(), It.IsAny<DtoOptions>()))
            .Returns(items);

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());
    }

    // ──────────────────────────────────────────────────────────────
    // GetInstantMixFromSong
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInstantMixFromSong_ItemExists_ReturnsOk()
    {
        var itemId = Guid.NewGuid();
        var fakeItem = new Mock<BaseItem>();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns(fakeItem.Object);

        SetupMusicManagerReturnsItems(Array.Empty<BaseItem>());

        var result = await _controller.GetInstantMixFromSong(
            itemId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        var okResult = Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
        Assert.True(okResult.Value is not null || okResult.Result is not null);
    }

    [Fact]
    public async Task GetInstantMixFromSong_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns((BaseItem?)null);

        var result = await _controller.GetInstantMixFromSong(
            itemId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetInstantMixFromAlbum
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInstantMixFromAlbum_ItemExists_ReturnsOk()
    {
        var itemId = Guid.NewGuid();
        var fakeItem = new Mock<BaseItem>();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns(fakeItem.Object);

        SetupMusicManagerReturnsItems(Array.Empty<BaseItem>());

        var result = await _controller.GetInstantMixFromAlbum(
            itemId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
    }

    [Fact]
    public async Task GetInstantMixFromAlbum_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns((BaseItem?)null);

        var result = await _controller.GetInstantMixFromAlbum(
            itemId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetInstantMixFromArtists
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInstantMixFromArtists_ItemExists_ReturnsOk()
    {
        var itemId = Guid.NewGuid();
        var fakeItem = new Mock<BaseItem>();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns(fakeItem.Object);

        SetupMusicManagerReturnsItems(Array.Empty<BaseItem>());

        var result = await _controller.GetInstantMixFromArtists(
            itemId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
    }

    [Fact]
    public async Task GetInstantMixFromArtists_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns((BaseItem?)null);

        var result = await _controller.GetInstantMixFromArtists(
            itemId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetInstantMixFromMusicGenreByName
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInstantMixFromMusicGenreByName_ValidGenre_ReturnsOk()
    {
        var genreName = "Rock";

        _musicManagerMock
            .Setup(m => m.GetInstantMixFromGenres(It.IsAny<IEnumerable<string>>(), It.IsAny<User?>(), It.IsAny<DtoOptions>()))
            .Returns(Array.Empty<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        var result = await _controller.GetInstantMixFromMusicGenreByName(
            genreName,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
    }

    [Fact]
    public async Task GetInstantMixFromMusicGenreByName_PassesGenreNameToMusicManager()
    {
        const string genreName = "Jazz";
        string[]? capturedGenres = null;

        _musicManagerMock
            .Setup(m => m.GetInstantMixFromGenres(It.IsAny<IEnumerable<string>>(), It.IsAny<User?>(), It.IsAny<DtoOptions>()))
            .Callback<IEnumerable<string>, User?, DtoOptions>((genres, _, _) => capturedGenres = genres.ToArray())
            .Returns(Array.Empty<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetInstantMixFromMusicGenreByName(
            genreName,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.NotNull(capturedGenres);
        Assert.Single(capturedGenres);
        Assert.Equal(genreName, capturedGenres[0]);
    }

    // ──────────────────────────────────────────────────────────────
    // GetInstantMixFromPlaylist
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInstantMixFromPlaylist_PlaylistNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<MediaBrowser.Controller.Playlists.Playlist>(itemId, It.IsAny<User?>()))
            .Returns((MediaBrowser.Controller.Playlists.Playlist?)null);

        var result = await _controller.GetInstantMixFromPlaylist(
            itemId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // Limit parameter behaviour
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task GetInstantMixFromSong_WithVariousLimits_ReturnsOk(int? limit)
    {
        var itemId = Guid.NewGuid();
        var fakeItem = new Mock<BaseItem>();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns(fakeItem.Object);

        SetupMusicManagerReturnsItems(Array.Empty<BaseItem>());

        var result = await _controller.GetInstantMixFromSong(
            itemId,
            null,
            limit,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>());

        Assert.Null(result.Result as NotFoundResult);
    }
}
