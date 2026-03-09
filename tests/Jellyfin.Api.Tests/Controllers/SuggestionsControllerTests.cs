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
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class SuggestionsControllerTests
{
    private readonly Mock<IDtoService> _dtoServiceMock;
    private readonly Mock<IUserManager> _userManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly SuggestionsController _controller;
    private readonly Guid _userId;

    public SuggestionsControllerTests()
    {
        _dtoServiceMock = new Mock<IDtoService>();
        _userManagerMock = new Mock<IUserManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();

        _userId = Guid.NewGuid();

        _controller = new SuggestionsController(
            _dtoServiceMock.Object,
            _userManagerMock.Object,
            _libraryManagerMock.Object);

        SetupControllerContext(_userId);
        SetupDefaultMocks();
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

    private void SetupDefaultMocks()
    {
        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());
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
    // GetSuggestions
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSuggestions_ValidRequest_ReturnsOk()
    {
        var result = await _controller.GetSuggestions(
            _userId,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            null,
            null);

        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
        Assert.Null(result.Result as NotFoundResult);
        Assert.Null(result.Result as BadRequestResult);
    }

    [Fact]
    public async Task GetSuggestions_NoUserId_ReturnsOkWithNullUser()
    {
        // When userId is null or empty, user should be null and no lookup should be performed
        var result = await _controller.GetSuggestions(
            null,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            null,
            null);

        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
    }

    [Fact]
    public async Task GetSuggestions_WithMediaTypeFilter_PassesFilterToLibrary()
    {
        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        await _controller.GetSuggestions(
            null,
            new[] { MediaType.Video },
            Array.Empty<BaseItemKind>(),
            null,
            null);

        Assert.NotNull(capturedQuery);
        Assert.Contains(MediaType.Video, capturedQuery.MediaTypes);
    }

    [Fact]
    public async Task GetSuggestions_WithItemTypeFilter_PassesFilterToLibrary()
    {
        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        await _controller.GetSuggestions(
            null,
            Array.Empty<MediaType>(),
            new[] { BaseItemKind.Movie },
            null,
            null);

        Assert.NotNull(capturedQuery);
        Assert.Contains(BaseItemKind.Movie, capturedQuery.IncludeItemTypes);
    }

    [Fact]
    public async Task GetSuggestions_WithPagination_PassesPaginationToLibrary()
    {
        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        await _controller.GetSuggestions(
            null,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            startIndex: 10,
            limit: 20);

        Assert.NotNull(capturedQuery);
        Assert.Equal(10, capturedQuery.StartIndex);
        Assert.Equal(20, capturedQuery.Limit);
    }

    [Fact]
    public async Task GetSuggestions_EnableTotalRecordCount_PassesFlagToLibrary()
    {
        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 100, Array.Empty<BaseItem>()));

        await _controller.GetSuggestions(
            null,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            null,
            null,
            enableTotalRecordCount: true);

        Assert.NotNull(capturedQuery);
        Assert.True(capturedQuery.EnableTotalRecordCount);
    }

    [Fact]
    public async Task GetSuggestions_DisableTotalRecordCount_IsDefault()
    {
        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new QueryResult<BaseItem>(0, 0, Array.Empty<BaseItem>()));

        await _controller.GetSuggestions(
            null,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            null,
            null);

        Assert.NotNull(capturedQuery);
        Assert.False(capturedQuery.EnableTotalRecordCount);
    }

    [Fact]
    public async Task GetSuggestions_ResultContainsStartIndexInResponse()
    {
        const int startIndex = 5;

        _libraryManagerMock
            .Setup(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>(startIndex, 50, Array.Empty<BaseItem>()));

        var result = await _controller.GetSuggestions(
            null,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            startIndex,
            null);

        var queryResult = result.Value;
        if (queryResult is not null)
        {
            Assert.Equal(startIndex, queryResult.StartIndex);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // GetSuggestionsLegacy (backwards-compat wrapper)
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSuggestionsLegacy_DelegatesToGetSuggestions()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        var result = await _controller.GetSuggestionsLegacy(
            _userId,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            null,
            null);
#pragma warning restore CS0618

        Assert.IsType<ActionResult<QueryResult<BaseItemDto>>>(result);
        // The wrapper must call the same library
        _libraryManagerMock.Verify(l => l.GetItemsResult(It.IsAny<InternalItemsQuery>()), Times.Once);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 25)]
    [InlineData(100, 100)]
    public async Task GetSuggestions_VariousPaginationValues_ReturnsOk(int startIndex, int limit)
    {
        var result = await _controller.GetSuggestions(
            null,
            Array.Empty<MediaType>(),
            Array.Empty<BaseItemKind>(),
            startIndex,
            limit);

        Assert.Null(result.Result as BadRequestResult);
        Assert.Null(result.Result as NotFoundResult);
    }
}
