using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class UserLibraryControllerTests
{
    private readonly Mock<IUserManager> _userManagerMock;
    private readonly Mock<IUserDataManager> _userDataManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly Mock<IDtoService> _dtoServiceMock;
    private readonly Mock<IUserViewManager> _userViewManagerMock;
    private readonly Mock<IFileSystem> _fileSystemMock;
    private readonly UserLibraryController _controller;
    private readonly Guid _userId;
    private readonly User _testUser;

    public UserLibraryControllerTests()
    {
        _userManagerMock = new Mock<IUserManager>();
        _userDataManagerMock = new Mock<IUserDataManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();
        _dtoServiceMock = new Mock<IDtoService>();
        _userViewManagerMock = new Mock<IUserViewManager>();
        _fileSystemMock = new Mock<IFileSystem>();

        _userId = Guid.NewGuid();
        _testUser = CreateTestUser();

        _controller = new UserLibraryController(
            _userManagerMock.Object,
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            _dtoServiceMock.Object,
            _userViewManagerMock.Object,
            _fileSystemMock.Object);

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
    // GetItem
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetItem_ValidUserAndItem_ReturnsDto()
    {
        var itemId = Guid.NewGuid();
        var fakeItem = new Mock<BaseItem>();
        var expectedDto = new BaseItemDto { Id = itemId };

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns(fakeItem.Object);

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtoAsync(fakeItem.Object, It.IsAny<DtoOptions>(), _testUser, null))
            .ReturnsAsync(expectedDto);

        var result = await _controller.GetItem(_userId, itemId);

        Assert.Null(result.Result as NotFoundResult);
    }

    [Fact]
    public async Task GetItem_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        var result = await _controller.GetItem(_userId, Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetItem_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = await _controller.GetItem(_userId, itemId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetRootFolder
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRootFolder_ValidUser_ReturnsDto()
    {
        var rootFolder = new Mock<Folder>();
        var expectedDto = new BaseItemDto { Id = Guid.NewGuid() };

        _libraryManagerMock
            .Setup(l => l.GetUserRootFolder())
            .Returns(rootFolder.Object);

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtoAsync(rootFolder.Object, It.IsAny<DtoOptions>(), _testUser, null))
            .ReturnsAsync(expectedDto);

        var result = await _controller.GetRootFolder(_userId);

        Assert.Null(result.Result as NotFoundResult);
    }

    [Fact]
    public async Task GetRootFolder_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        var result = await _controller.GetRootFolder(_userId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetIntros
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIntros_ValidUserAndItem_ReturnsQueryResult()
    {
        var itemId = Guid.NewGuid();
        var fakeItem = new Mock<BaseItem>();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns(fakeItem.Object);

        _libraryManagerMock
            .Setup(l => l.GetIntros(fakeItem.Object, _testUser))
            .ReturnsAsync(new List<Video>());

        _dtoServiceMock
            .Setup(d => d.GetBaseItemDtoAsync(It.IsAny<BaseItem>(), It.IsAny<DtoOptions>(), _testUser, null))
            .ReturnsAsync(new BaseItemDto());

        var result = await _controller.GetIntros(_userId, itemId);

        Assert.Null(result.Result as NotFoundResult);
    }

    [Fact]
    public async Task GetIntros_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        var result = await _controller.GetIntros(_userId, Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetIntros_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = await _controller.GetIntros(_userId, itemId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // MarkFavoriteItem
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void MarkFavoriteItem_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        var result = _controller.MarkFavoriteItem(_userId, Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void MarkFavoriteItem_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = _controller.MarkFavoriteItem(_userId, itemId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // DeleteUserItemRating
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteUserItemRating_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        var result = _controller.DeleteUserItemRating(_userId, Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void DeleteUserItemRating_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = _controller.DeleteUserItemRating(_userId, itemId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetLocalTrailers
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLocalTrailers_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        var result = await _controller.GetLocalTrailers(_userId, Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetLocalTrailers_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = await _controller.GetLocalTrailers(_userId, itemId);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetSpecialFeatures
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSpecialFeatures_UserNotFound_ReturnsNotFound()
    {
        _userManagerMock
            .Setup(u => u.GetUserById(It.IsAny<Guid>()))
            .Returns((User?)null);

        var result = await _controller.GetSpecialFeatures(_userId, Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetSpecialFeatures_ItemNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();

        _libraryManagerMock
            .Setup(l => l.GetItemById<BaseItem>(itemId, _testUser))
            .Returns((BaseItem?)null);

        var result = await _controller.GetSpecialFeatures(_userId, itemId);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
