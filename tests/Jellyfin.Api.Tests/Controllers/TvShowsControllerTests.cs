using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class TvShowsControllerTests
{
    private readonly Mock<IUserManager> _userManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly Mock<IDtoService> _dtoServiceMock;
    private readonly Mock<ITVSeriesManager> _tvSeriesManagerMock;
    private readonly TvShowsController _controller;
    private readonly Guid _userId;

    public TvShowsControllerTests()
    {
        _userManagerMock = new Mock<IUserManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();
        _dtoServiceMock = new Mock<IDtoService>();
        _tvSeriesManagerMock = new Mock<ITVSeriesManager>();

        _userId = Guid.NewGuid();

        _controller = new TvShowsController(
            _userManagerMock.Object,
            _libraryManagerMock.Object,
            _dtoServiceMock.Object,
            _tvSeriesManagerMock.Object);

        SetupControllerContext(_userId);
    }

    private void SetupControllerContext(Guid userId)
    {
        var user = CreateTestUser();
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

    private void SetupNextUpReturnsEmpty()
    {
        _tvSeriesManagerMock
            .Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());
    }

    private void SetupUpcomingEpisodesReturnsEmpty()
    {
        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());
    }

    // ──────────────────────────────────────────────────────────────
    // GetNextUp
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNextUp_ValidUser_ReturnsOk()
    {
        SetupNextUpReturnsEmpty();

        var result = await _controller.GetNextUp(
            _userId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null,
            null);

        Assert.Null(result.Result as NotFoundResult);
        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
    }

    [Fact]
    public async Task GetNextUp_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        SetupNextUpReturnsEmpty();

        var result = await _controller.GetNextUp(
            _userId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null,
            null);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetNextUp_WithSeriesIdFilter_PassesFilterToManager()
    {
        var seriesId = Guid.NewGuid();
        NextUpQuery? capturedQuery = null;

        _tvSeriesManagerMock
            .Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((q, _) => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetNextUp(
            _userId,
            null,
            null,
            Array.Empty<ItemFields>(),
            seriesId,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null,
            null);

        Assert.NotNull(capturedQuery);
        Assert.Equal(seriesId, capturedQuery.SeriesId);
    }

    [Fact]
    public async Task GetNextUp_WithPagination_PassesPaginationToManager()
    {
        NextUpQuery? capturedQuery = null;

        _tvSeriesManagerMock
            .Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((q, _) => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetNextUp(
            _userId,
            startIndex: 5,
            limit: 20,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null,
            null);

        Assert.NotNull(capturedQuery);
        Assert.Equal(5, capturedQuery.StartIndex);
        Assert.Equal(20, capturedQuery.Limit);
    }

    [Fact]
    public async Task GetNextUp_EnableRewatching_PassesFlagToManager()
    {
        NextUpQuery? capturedQuery = null;

        _tvSeriesManagerMock
            .Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((q, _) => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetNextUp(
            _userId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null,
            null,
            enableTotalRecordCount: true,
            enableResumable: true,
            enableRewatching: true);

        Assert.NotNull(capturedQuery);
        Assert.True(capturedQuery.EnableRewatching);
    }

    [Fact]
    public async Task GetNextUp_DisableResumable_PassesFlagToManager()
    {
        NextUpQuery? capturedQuery = null;

        _tvSeriesManagerMock
            .Setup(t => t.GetNextUp(It.IsAny<NextUpQuery>(), It.IsAny<DtoOptions>()))
            .Callback<NextUpQuery, DtoOptions>((q, _) => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetNextUp(
            _userId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null,
            null,
            enableResumable: false);

        Assert.NotNull(capturedQuery);
        Assert.False(capturedQuery.EnableResumable);
    }

    // ──────────────────────────────────────────────────────────────
    // GetUpcomingEpisodes
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUpcomingEpisodes_ValidRequest_ReturnsOk()
    {
        SetupUpcomingEpisodesReturnsEmpty();

        var result = await _controller.GetUpcomingEpisodes(
            _userId,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null);

        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
        Assert.Null(result.Result as NotFoundResult);
    }

    [Fact]
    public async Task GetUpcomingEpisodes_WithLimit_PassesLimitToLibrary()
    {
        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetUpcomingEpisodes(
            _userId,
            null,
            limit: 15,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null);

        Assert.NotNull(capturedQuery);
        Assert.Equal(15, capturedQuery.Limit);
    }

    [Fact]
    public async Task GetUpcomingEpisodes_QueriesOnlyEpisodeType()
    {
        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetUpcomingEpisodes(
            null,
            null,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null);

        Assert.NotNull(capturedQuery);
        Assert.Contains(BaseItemKind.Episode, capturedQuery.IncludeItemTypes);
    }

    // ──────────────────────────────────────────────────────────────
    // GetEpisodes (seriesId required)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEpisodes_SeriesNotFound_ReturnsNotFound()
    {
        var seriesId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<MediaBrowser.Controller.Entities.TV.Series>(seriesId))
            .Returns((MediaBrowser.Controller.Entities.TV.Series?)null);

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(seriesId))
            .Returns((BaseItem?)null);

        var result = await _controller.GetEpisodes(
            seriesId,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null,
            null);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetSeasons
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSeasons_SeriesNotFound_ReturnsNotFound()
    {
        var seriesId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<MediaBrowser.Controller.Entities.TV.Series>(seriesId))
            .Returns((MediaBrowser.Controller.Entities.TV.Series?)null);

        var result = await _controller.GetSeasons(
            seriesId,
            null,
            Array.Empty<ItemFields>(),
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ImageType>(),
            null);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
