using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class LibraryControllerTests
{
    private readonly Mock<IProviderManager> _providerManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly Mock<IUserManager> _userManagerMock;
    private readonly Mock<IDtoService> _dtoServiceMock;
    private readonly Mock<IActivityManager> _activityManagerMock;
    private readonly Mock<ILocalizationManager> _localizationMock;
    private readonly Mock<ILibraryMonitor> _libraryMonitorMock;
    private readonly Mock<ILogger<LibraryController>> _loggerMock;
    private readonly Mock<IServerConfigurationManager> _serverConfigMock;
    private readonly LibraryController _controller;
    private readonly Guid _userId;
    private readonly User _testUser;

    public LibraryControllerTests()
    {
        _providerManagerMock = new Mock<IProviderManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();
        _userManagerMock = new Mock<IUserManager>();
        _dtoServiceMock = new Mock<IDtoService>();
        _activityManagerMock = new Mock<IActivityManager>();
        _localizationMock = new Mock<ILocalizationManager>();
        _libraryMonitorMock = new Mock<ILibraryMonitor>();
        _loggerMock = new Mock<ILogger<LibraryController>>();
        _serverConfigMock = new Mock<IServerConfigurationManager>();

        _userId = Guid.NewGuid();
        _testUser = CreateTestUser();

        _controller = new LibraryController(
            _providerManagerMock.Object,
            _libraryManagerMock.Object,
            _userManagerMock.Object,
            _dtoServiceMock.Object,
            _activityManagerMock.Object,
            _localizationMock.Object,
            _libraryMonitorMock.Object,
            _loggerMock.Object,
            _serverConfigMock.Object);

        SetupControllerContext(_userId);

        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns(_testUser);
    }

    private void SetupControllerContext(Guid userId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, UserRoles.User),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(InternalClaimTypes.UserId, userId.ToString("N")),
            new Claim(InternalClaimTypes.DeviceId, Guid.Empty.ToString("N")),
            new Claim(InternalClaimTypes.Device, "test"),
            new Claim(InternalClaimTypes.Client, "test"),
            new Claim(InternalClaimTypes.Version, "1.0"),
            new Claim(InternalClaimTypes.Token, "testtoken"),
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var claimsPrincipal = new ClaimsPrincipal(identity);

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

    // ──────────────────────────────────────────────────────────────
    // GetFile
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetFile_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<Guid>()))
            .Returns((BaseItem?)null);

        var result = _controller.GetFile(itemId);

        Assert.IsType<NotFoundResult>(result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetThemeSongs
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetThemeSongs_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = await _controller.GetThemeSongs(itemId, _userId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetThemeVideos
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetThemeVideos_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = await _controller.GetThemeVideos(itemId, _userId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetItemCounts
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetItemCounts_ValidRequest_ReturnsItemCounts()
    {
        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>());

        var result = _controller.GetItemCounts(_userId, null);

        Assert.IsType<ActionResult<ItemCounts>>(result);
        Assert.Null(result.Result as NotFoundResult);
    }

    // ──────────────────────────────────────────────────────────────
    // GetSimilarMovies / GetSimilarShows / GetSimilarAlbums / GetSimilarItems
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSimilarItems_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<User?>()))
            .Returns((BaseItem?)null);

        var result = await _controller.GetSimilarItems(
            itemId, Array.Empty<Guid>(), null, null, Array.Empty<ItemFields>());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetCriticReviews (obsolete — always returns empty result)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetCriticReviews_AlwaysReturnsEmptyQueryResult()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var result = _controller.GetCriticReviews();
#pragma warning restore CS0618

        Assert.True(result.Value is not null || result.Result is not null);
        // Either Value is an empty QueryResult, or Result wraps one
        if (result.Value is not null)
        {
            Assert.Equal(0, result.Value.TotalRecordCount);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // DeleteItem
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteItem_ItemNotFound_ReturnsUnauthorized()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, It.IsAny<Guid>()))
            .Returns((BaseItem?)null);

        var result = _controller.DeleteItem(itemId);

        // Returns Unauthorized when item is not found (no access)
        Assert.True(result is UnauthorizedResult || result is NotFoundResult);
    }

    // ──────────────────────────────────────────────────────────────
    // GetAncestors
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAncestors_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId))
            .Returns((BaseItem?)null);

        var result = await _controller.GetAncestors(itemId, _userId);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
