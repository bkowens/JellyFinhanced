using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.E2E;

/// <summary>
/// End-to-end tests that verify the server starts correctly, all required services are resolvable,
/// and the fundamental request pipeline is operational.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ServerStartupTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;

    public ServerStartupTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // Server liveness
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Server_PingEndpoint_RespondsWith200()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_PublicInfoEndpoint_RespondsWith200AndValidData()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Info/Public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var info = await response.Content.ReadFromJsonAsync<PublicSystemInfo>(_jsonOptions);
        Assert.NotNull(info);
        // Server name and version must be populated
        Assert.NotEmpty(info.ServerName);
        Assert.NotEmpty(info.Version);
        // Every server instance gets a unique stable ID
        Assert.NotEmpty(info.Id);
        Assert.NotEqual(Guid.Empty.ToString(), info.Id);
    }

    [Fact]
    public async Task Server_OpenApiSpec_IsReachableAndValid()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("api-docs/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
    }

    // -------------------------------------------------------------------------
    // Request pipeline: serialization / content negotiation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Server_JsonResponse_UsesUtf8AndApplicationJson()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("System/Info/Public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    public async Task Server_UnknownRoute_Returns404()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("ThisRouteDefinitelyDoesNotExist/AtAll");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Authentication pipeline: unauthenticated requests are rejected
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("System/Logs")]
    [InlineData("Users")]
    [InlineData("Sessions")]
    [InlineData("ScheduledTasks")]
    [InlineData("Plugins")]
    [InlineData("Devices")]
    [InlineData("Auth/Keys")]
    public async Task Server_ProtectedEndpoints_RejectAnonymousRequests(string path)
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Service container: required services must be resolvable
    // -------------------------------------------------------------------------

    [Fact]
    public void ServiceContainer_MediaBrowserServices_AreRegistered()
    {
        // Access the service provider through the factory's Services property
        // which is available after the host is started
        var services = _factory.Services;

        Assert.NotNull(services.GetService<MediaBrowser.Controller.Library.ILibraryManager>());
        Assert.NotNull(services.GetService<MediaBrowser.Controller.Library.IUserManager>());
        Assert.NotNull(services.GetService<MediaBrowser.Controller.Session.ISessionManager>());
        Assert.NotNull(services.GetService<MediaBrowser.Common.Configuration.IApplicationPaths>());
        Assert.NotNull(services.GetService<MediaBrowser.Controller.Configuration.IServerConfigurationManager>());
    }

    [Fact]
    public void ServiceContainer_TaskManager_IsRegistered()
    {
        var services = _factory.Services;
        Assert.NotNull(services.GetService<MediaBrowser.Model.Tasks.ITaskManager>());
    }

    [Fact]
    public void ServiceContainer_LoggerFactory_IsRegistered()
    {
        var services = _factory.Services;
        Assert.NotNull(services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>());
    }
}
