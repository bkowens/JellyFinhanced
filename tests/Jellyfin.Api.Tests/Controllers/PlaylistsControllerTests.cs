using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Api.Models.PlaylistDtos;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Users;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

public class PlaylistsControllerTests
{
    private readonly Mock<IDtoService> _dtoServiceMock;
    private readonly Mock<IPlaylistManager> _playlistManagerMock;
    private readonly Mock<IUserManager> _userManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly PlaylistsController _controller;
    private readonly Guid _callingUserId;

    public PlaylistsControllerTests()
    {
        _dtoServiceMock = new Mock<IDtoService>();
        _playlistManagerMock = new Mock<IPlaylistManager>();
        _userManagerMock = new Mock<IUserManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();

        _callingUserId = Guid.NewGuid();

        _controller = new PlaylistsController(
            _dtoServiceMock.Object,
            _playlistManagerMock.Object,
            _userManagerMock.Object,
            _libraryManagerMock.Object);

        SetupControllerContext(_callingUserId);
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

    private Playlist CreatePlaylist(Guid playlistId, Guid ownerId, List<PlaylistUserPermissions>? shares = null)
    {
        var playlist = new Playlist
        {
            Id = playlistId,
            OwnerUserId = ownerId
        };

        if (shares is not null)
        {
            playlist.Shares = shares;
        }

        return playlist;
    }

    // ──────────────────────────────────────────────────────────────
    // CreatePlaylist
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlaylist_ValidRequest_ReturnsOk()
    {
        _playlistManagerMock
            .Setup(p => p.CreatePlaylist(It.IsAny<PlaylistCreationRequest>()))
            .ReturnsAsync(new PlaylistCreationResult("test-id"));

        var result = await _controller.CreatePlaylist(
            null,
            Array.Empty<Guid>(),
            null,
            null,
            new CreatePlaylistDto { Name = "Test Playlist" });

        Assert.IsType<ActionResult<PlaylistCreationResult>>(result);
        var okResult = result.Result as OkObjectResult;
        // Result may be set directly on ActionResult<T> without an OkObjectResult wrapper
        Assert.True(result.Value is not null || okResult is not null);
    }

    [Fact]
    public async Task CreatePlaylist_CallsPlaylistManagerCreate()
    {
        _playlistManagerMock
            .Setup(p => p.CreatePlaylist(It.IsAny<PlaylistCreationRequest>()))
            .ReturnsAsync(new PlaylistCreationResult("created-id"));

        await _controller.CreatePlaylist(
            "My Playlist",
            Array.Empty<Guid>(),
            _callingUserId,
            null,
            null);

        _playlistManagerMock.Verify(
            p => p.CreatePlaylist(It.Is<PlaylistCreationRequest>(r => r.Name == "My Playlist")),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────
    // GetPlaylist
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetPlaylist_PlaylistExists_ReturnsPlaylistDto()
    {
        var playlistId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, _callingUserId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = _controller.GetPlaylist(playlistId);

        Assert.Null(result.Result as NotFoundResult);
        var dto = result.Value ?? (result.Result as OkObjectResult)?.Value as PlaylistDto;
        Assert.NotNull(dto);
    }

    [Fact]
    public void GetPlaylist_PlaylistNotFound_ReturnsNotFound()
    {
        var playlistId = Guid.NewGuid();

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, It.IsAny<Guid>()))
            .Returns((Playlist)null!);

        var result = _controller.GetPlaylist(playlistId);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // UpdatePlaylist
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePlaylist_OwnerCanUpdate_ReturnsNoContent()
    {
        var playlistId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, _callingUserId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        _playlistManagerMock
            .Setup(p => p.UpdatePlaylist(It.IsAny<PlaylistUpdateRequest>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.UpdatePlaylist(playlistId, new UpdatePlaylistDto { Name = "Updated" });

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdatePlaylist_PlaylistNotFound_ReturnsNotFound()
    {
        var playlistId = Guid.NewGuid();

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, It.IsAny<Guid>()))
            .Returns((Playlist)null!);

        var result = await _controller.UpdatePlaylist(playlistId, new UpdatePlaylistDto { Name = "Updated" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdatePlaylist_NonOwnerWithoutEditShare_ReturnsForbid()
    {
        var playlistId = Guid.NewGuid();
        var ownerId = Guid.NewGuid(); // Different from _callingUserId
        var playlist = CreatePlaylist(playlistId, ownerId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = await _controller.UpdatePlaylist(playlistId, new UpdatePlaylistDto { Name = "Updated" });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task UpdatePlaylist_SharedUserWithEditPermission_ReturnsNoContent()
    {
        var playlistId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var shares = new List<PlaylistUserPermissions>
        {
            new PlaylistUserPermissions(_callingUserId, canEdit: true)
        };
        var playlist = CreatePlaylist(playlistId, ownerId, shares);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        _playlistManagerMock
            .Setup(p => p.UpdatePlaylist(It.IsAny<PlaylistUpdateRequest>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.UpdatePlaylist(playlistId, new UpdatePlaylistDto { Name = "Shared Update" });

        Assert.IsType<NoContentResult>(result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetPlaylistUsers
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetPlaylistUsers_OwnerRequests_ReturnsShares()
    {
        var playlistId = Guid.NewGuid();
        var shareUserId = Guid.NewGuid();
        var shares = new List<PlaylistUserPermissions>
        {
            new PlaylistUserPermissions(shareUserId, canEdit: false)
        };
        var playlist = CreatePlaylist(playlistId, _callingUserId, shares);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = _controller.GetPlaylistUsers(playlistId);

        var listResult = result.Value;
        Assert.NotNull(listResult);
        Assert.Single(listResult);
    }

    [Fact]
    public void GetPlaylistUsers_NonOwnerRequests_ReturnsForbid()
    {
        var playlistId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, ownerId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = _controller.GetPlaylistUsers(playlistId);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public void GetPlaylistUsers_PlaylistNotFound_ReturnsNotFound()
    {
        var playlistId = Guid.NewGuid();

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, It.IsAny<Guid>()))
            .Returns((Playlist)null!);

        var result = _controller.GetPlaylistUsers(playlistId);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // ──────────────────────────────────────────────────────────────
    // AddItemToPlaylist
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddItemToPlaylist_OwnerAddsItems_ReturnsNoContent()
    {
        var playlistId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, _callingUserId);
        var itemIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        _playlistManagerMock
            .Setup(p => p.AddItemToPlaylistAsync(playlistId, itemIds, _callingUserId))
            .Returns(Task.CompletedTask);

        var result = await _controller.AddItemToPlaylist(playlistId, itemIds, _callingUserId);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task AddItemToPlaylist_PlaylistNotFound_ReturnsNotFound()
    {
        var playlistId = Guid.NewGuid();

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, It.IsAny<Guid>()))
            .Returns((Playlist)null!);

        var result = await _controller.AddItemToPlaylist(playlistId, Array.Empty<Guid>(), null);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AddItemToPlaylist_NonOwnerNonEditor_ReturnsForbid()
    {
        var playlistId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, ownerId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = await _controller.AddItemToPlaylist(playlistId, Array.Empty<Guid>(), _callingUserId);

        Assert.IsType<ForbidResult>(result);
    }

    // ──────────────────────────────────────────────────────────────
    // RemoveUserFromPlaylist
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveUserFromPlaylist_OwnerRemovesUser_ReturnsNoContent()
    {
        var playlistId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var shares = new List<PlaylistUserPermissions>
        {
            new PlaylistUserPermissions(targetUserId, canEdit: false)
        };
        var playlist = CreatePlaylist(playlistId, _callingUserId, shares);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        _playlistManagerMock
            .Setup(p => p.RemoveUserFromShares(playlistId, _callingUserId, It.IsAny<PlaylistUserPermissions>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.RemoveUserFromPlaylist(playlistId, targetUserId);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task RemoveUserFromPlaylist_TargetUserNotShared_ReturnsNotFound()
    {
        var playlistId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, _callingUserId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = await _controller.RemoveUserFromPlaylist(playlistId, targetUserId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RemoveUserFromPlaylist_PlaylistNotFound_ReturnsNotFound()
    {
        var playlistId = Guid.NewGuid();

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, It.IsAny<Guid>()))
            .Returns((Playlist)null!);

        var result = await _controller.RemoveUserFromPlaylist(playlistId, Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RemoveUserFromPlaylist_NonOwner_ReturnsForbid()
    {
        var playlistId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, ownerId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = await _controller.RemoveUserFromPlaylist(playlistId, Guid.NewGuid());

        Assert.IsType<ForbidResult>(result);
    }

    // ──────────────────────────────────────────────────────────────
    // GetPlaylistUser
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetPlaylistUser_OwnerRequestsOwnPermission_ReturnsOwnerPermissions()
    {
        var playlistId = Guid.NewGuid();
        var playlist = CreatePlaylist(playlistId, _callingUserId);

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, _callingUserId))
            .Returns(playlist);

        var result = _controller.GetPlaylistUser(playlistId, _callingUserId);

        Assert.Null(result.Result as ForbidResult);
        Assert.Null(result.Result as NotFoundObjectResult);
    }

    [Fact]
    public void GetPlaylistUser_PlaylistNotFound_ReturnsNotFound()
    {
        var playlistId = Guid.NewGuid();

        _playlistManagerMock
            .Setup(p => p.GetPlaylistForUser(playlistId, It.IsAny<Guid>()))
            .Returns((Playlist)null!);

        var result = _controller.GetPlaylistUser(playlistId, Guid.NewGuid());

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }
}
