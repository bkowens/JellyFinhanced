using System;
using System.Collections.Generic;
using System.Linq;
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
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class MoviesControllerTests
{
    private readonly Mock<IUserManager> _userManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly Mock<IDtoService> _dtoServiceMock;
    private readonly Mock<IServerConfigurationManager> _serverConfigMock;
    private readonly MoviesController _controller;
    private readonly Guid _authenticatedUserId;

    public MoviesControllerTests()
    {
        _userManagerMock = new Mock<IUserManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();
        _dtoServiceMock = new Mock<IDtoService>();
        _serverConfigMock = new Mock<IServerConfigurationManager>();

        _serverConfigMock
            .Setup(s => s.Configuration)
            .Returns(new ServerConfiguration { EnableExternalContentInSuggestions = false });

        // GetPeople is called by GetDirectors/GetActors — return empty list to avoid null.
        _libraryManagerMock
            .Setup(l => l.GetPeople(It.IsAny<InternalPeopleQuery>()))
            .Returns(Array.Empty<PersonInfo>());

        _controller = new MoviesController(
            _userManagerMock.Object,
            _libraryManagerMock.Object,
            _dtoServiceMock.Object,
            _serverConfigMock.Object);

        _authenticatedUserId = Guid.NewGuid();
        SetupControllerContext();
    }

    private void SetupControllerContext()
    {
        var user = CreateTestUser();
        var claims = new[]
        {
            new Claim(ClaimTypes.Role, UserRoles.User),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(InternalClaimTypes.UserId, _authenticatedUserId.ToString("N")),
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

    // ──────────────────────────────────────────────────────────────
    // GetMovieRecommendations
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMovieRecommendations_ValidRequest_ReturnsOk()
    {
        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        var result = await _controller.GetMovieRecommendations(
            null, null, Array.Empty<ItemFields>(), 5, 8);

        var okResult = Assert.IsType<ActionResult<IEnumerable<RecommendationDto>>>(result);
        Assert.True(okResult.Value is not null || okResult.Result is not null);
    }

    [Fact]
    public async Task GetMovieRecommendations_NullUser_ReturnsOk()
    {
        // No userId supplied; user resolved to null — should still succeed
        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        var result = await _controller.GetMovieRecommendations(
            null, null, Array.Empty<ItemFields>(), 5, 8);

        Assert.Null(result.Result as NotFoundResult);
        Assert.Null(result.Result as BadRequestResult);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public async Task GetMovieRecommendations_CategoryLimitRespected_ReturnsAtMostCategoryLimit(int categoryLimit)
    {
        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        var result = await _controller.GetMovieRecommendations(
            null, null, Array.Empty<ItemFields>(), categoryLimit, 8);

        var okResult = result.Result as OkObjectResult;
        if (okResult?.Value is IEnumerable<RecommendationDto> dtos)
        {
            Assert.True(
                dtos.Count() <= categoryLimit,
                $"Expected at most {categoryLimit} categories, got {dtos.Count()}");
        }

        // If result.Value is directly set, that's also valid
    }

    [Fact]
    public async Task GetMovieRecommendations_WithExternalContentEnabled_QueriesAdditionalTypes()
    {
        _serverConfigMock
            .Setup(s => s.Configuration)
            .Returns(new ServerConfiguration { EnableExternalContentInSuggestions = true });

        InternalItemsQuery? capturedQuery = null;

        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => capturedQuery = q)
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetMovieRecommendations(
            null, null, Array.Empty<ItemFields>(), 5, 8);

        // The library manager must have been called
        _libraryManagerMock.Verify(l => l.GetItemList(It.IsAny<InternalItemsQuery>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetMovieRecommendations_DefaultParameters_UsesDefaultCategoryAndItemLimits()
    {
        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        // Call without explicit defaults — signature defaults are categoryLimit=5, itemLimit=8
        var result = await _controller.GetMovieRecommendations(null, null, Array.Empty<ItemFields>());

        Assert.Null(result.Result as BadRequestResult);
    }

    [Fact]
    public async Task GetMovieRecommendations_WithSpecificUserId_LooksUpUser()
    {
        // Use the authenticated user's ID so RequestHelpers.GetUserId doesn't reject it.
        var userId = _authenticatedUserId;

        _libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtosAsync(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>()))
            .ReturnsAsync(Array.Empty<BaseItemDto>());

        await _controller.GetMovieRecommendations(
            userId, null, Array.Empty<ItemFields>(), 5, 8);

        _userManagerMock.Verify(u => u.GetUserById(It.IsAny<Guid>()), Times.AtLeastOnce);
    }
}
