using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.SessionManager;

/// <summary>
/// Extended unit tests for <see cref="Emby.Server.Implementations.Session.SessionManager"/>.
/// These tests cover input validation, session creation, and disposal scenarios.
/// </summary>
public class SessionManagerExtendedTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Emby.Server.Implementations.Session.SessionManager CreateSessionManager(
        Mock<IDeviceManager>? deviceManagerMock = null,
        Mock<IServerConfigurationManager>? configManagerMock = null,
        Mock<ILibraryManager>? libraryManagerMock = null)
    {
        deviceManagerMock ??= new Mock<IDeviceManager>();
        configManagerMock ??= new Mock<IServerConfigurationManager>();
        libraryManagerMock ??= new Mock<ILibraryManager>();

        // Default configuration to avoid NRE in InactiveSessionThreshold usage.
        configManagerMock
            .Setup(c => c.Configuration)
            .Returns(new MediaBrowser.Model.Configuration.ServerConfiguration());

        return new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            configManagerMock.Object,
            libraryManagerMock.Object,
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            deviceManagerMock.Object,
            Mock.Of<IMediaSourceManager>(),
            Mock.Of<IHostApplicationLifetime>());
    }

    // -----------------------------------------------------------------------
    // GetAuthorizationToken – argument validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("", typeof(ArgumentException))]
    [InlineData(null, typeof(ArgumentNullException))]
    public async Task GetAuthorizationToken_EmptyOrNullDeviceId_ThrowsExpectedException(
        string? deviceId, Type expectedExceptionType)
    {
        await using var sut = CreateSessionManager();

        await Assert.ThrowsAsync(
            expectedExceptionType,
            () => sut.GetAuthorizationToken(
                new User("test", "default", "default"),
                deviceId,
                "Jellyfin Web",
                "10.9.0",
                "Test Device"));
    }

    [Fact]
    public async Task GetAuthorizationToken_ValidArguments_DoesNotThrow()
    {
        await using var sut = CreateSessionManager();

        // The method may still fail at a later stage due to missing DB, but it should
        // not throw an argument validation exception for well-formed inputs.
        var exception = await Record.ExceptionAsync(
            () => sut.GetAuthorizationToken(
                new User("test", "default", "default"),
                "valid-device-id",
                "Jellyfin Web",
                "10.9.0",
                "Test Device"));

        Assert.IsNotType<ArgumentNullException>(exception);
        Assert.IsNotType<ArgumentException>(exception);
    }

    // -----------------------------------------------------------------------
    // AuthenticateNewSessionInternal – argument validation
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AuthenticateNewSessionInternal_InvalidRequest_TestData))]
    public async Task AuthenticateNewSessionInternal_InvalidRequest_ThrowsExpectedException(
        AuthenticationRequest request, Type expectedExceptionType)
    {
        await using var sut = CreateSessionManager();

        await Assert.ThrowsAsync(
            expectedExceptionType,
            () => sut.AuthenticateNewSessionInternal(request, false));
    }

    public static TheoryData<AuthenticationRequest, Type> AuthenticateNewSessionInternal_InvalidRequest_TestData()
    {
        return new TheoryData<AuthenticationRequest, Type>
        {
            // Empty app name
            {
                new AuthenticationRequest
                {
                    App = string.Empty,
                    DeviceId = "device_id",
                    DeviceName = "device_name",
                    AppVersion = "1.0"
                },
                typeof(ArgumentException)
            },
            // Null app name
            {
                new AuthenticationRequest
                {
                    App = null,
                    DeviceId = "device_id",
                    DeviceName = "device_name",
                    AppVersion = "1.0"
                },
                typeof(ArgumentNullException)
            },
            // Empty device id
            {
                new AuthenticationRequest
                {
                    App = "Jellyfin Web",
                    DeviceId = string.Empty,
                    DeviceName = "device_name",
                    AppVersion = "1.0"
                },
                typeof(ArgumentException)
            },
            // Null device id
            {
                new AuthenticationRequest
                {
                    App = "Jellyfin Web",
                    DeviceId = null,
                    DeviceName = "device_name",
                    AppVersion = "1.0"
                },
                typeof(ArgumentNullException)
            },
            // Empty device name
            {
                new AuthenticationRequest
                {
                    App = "Jellyfin Web",
                    DeviceId = "device_id",
                    DeviceName = string.Empty,
                    AppVersion = "1.0"
                },
                typeof(ArgumentException)
            },
            // Null device name
            {
                new AuthenticationRequest
                {
                    App = "Jellyfin Web",
                    DeviceId = "device_id",
                    DeviceName = null,
                    AppVersion = "1.0"
                },
                typeof(ArgumentNullException)
            },
            // Empty app version
            {
                new AuthenticationRequest
                {
                    App = "Jellyfin Web",
                    DeviceId = "device_id",
                    DeviceName = "device_name",
                    AppVersion = string.Empty
                },
                typeof(ArgumentException)
            },
            // Null app version
            {
                new AuthenticationRequest
                {
                    App = "Jellyfin Web",
                    DeviceId = "device_id",
                    DeviceName = "device_name",
                    AppVersion = null
                },
                typeof(ArgumentNullException)
            }
        };
    }

    // -----------------------------------------------------------------------
    // UpdateDeviceName
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateDeviceName_UnknownSessionId_ThrowsResourceNotFoundException()
    {
        await using var sut = CreateSessionManager();

        // GetSession throws ResourceNotFoundException when session is not found (throwOnMissing defaults to true).
        Assert.Throws<MediaBrowser.Common.Extensions.ResourceNotFoundException>(
            () => sut.UpdateDeviceName("nonexistent-session-id", "New Device Name"));
    }

    // -----------------------------------------------------------------------
    // Sessions property
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sessions_WhenNoSessionsCreated_ReturnsEmptyEnumerable()
    {
        await using var sut = CreateSessionManager();

        Assert.Empty(sut.Sessions);
    }

    // -----------------------------------------------------------------------
    // Disposal
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var sut = CreateSessionManager();

        await sut.DisposeAsync();

        // Second disposal should be idempotent.
        var exception = await Record.ExceptionAsync(() => sut.DisposeAsync().AsTask());
        Assert.Null(exception);
    }

    [Fact]
    public async Task AfterDispose_GetAuthorizationToken_ThrowsNullReferenceException()
    {
        var sut = CreateSessionManager();
        await sut.DisposeAsync();

        // After disposal, internal dependencies are nulled out, causing NullReferenceException.
        await Assert.ThrowsAsync<NullReferenceException>(
            () => sut.GetAuthorizationToken(
                new User("test", "default", "default"),
                "device-id",
                "Jellyfin Web",
                "10.9.0",
                "Test Device"));
    }

    // -----------------------------------------------------------------------
    // CloseLiveStreamIfNeededAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CloseLiveStreamIfNeededAsync_UnknownLiveStreamId_DoesNotCloseStream()
    {
        var mediaSourceManagerMock = new Mock<IMediaSourceManager>();

        await using var sut = new Emby.Server.Implementations.Session.SessionManager(
            NullLogger<Emby.Server.Implementations.Session.SessionManager>.Instance,
            Mock.Of<IEventManager>(),
            Mock.Of<IUserDataManager>(),
            Mock.Of<IServerConfigurationManager>(),
            Mock.Of<ILibraryManager>(),
            Mock.Of<IUserManager>(),
            Mock.Of<IMusicManager>(),
            Mock.Of<IDtoService>(),
            Mock.Of<IImageProcessor>(),
            Mock.Of<IServerApplicationHost>(),
            Mock.Of<IDeviceManager>(),
            mediaSourceManagerMock.Object,
            Mock.Of<IHostApplicationLifetime>());

        await sut.CloseLiveStreamIfNeededAsync("unknown-stream-id", "session-id");

        // Stream was not tracked, so CloseLiveStream should never be called.
        mediaSourceManagerMock.Verify(
            m => m.CloseLiveStream(It.IsAny<string>()),
            Times.Never);
    }
}
