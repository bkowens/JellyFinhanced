using System;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Tasks;
using Xunit;

namespace Jellyfin.Server.Integration.Tests.Controllers;

/// <summary>
/// Integration tests for the ScheduledTasks controller.
/// Validates task listing, retrieval, and lifecycle management through the full request pipeline.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ScheduledTaskControllerTests : IClassFixture<JellyfinApplicationFactory>
{
    private readonly JellyfinApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;
    private static string? _accessToken;

    public ScheduledTaskControllerTests(JellyfinApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetTasks_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.GetAsync("ScheduledTasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTasks_Authenticated_ReturnsTaskList()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("ScheduledTasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        var tasks = await response.Content.ReadFromJsonAsync<TaskInfo[]>(_jsonOptions);
        Assert.NotNull(tasks);
        // The server registers built-in tasks, so there must be at least one
        Assert.NotEmpty(tasks);
    }

    [Fact]
    public async Task GetTasks_FilterHiddenFalse_ReturnsVisibleTasks()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("ScheduledTasks?isHidden=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tasks = await response.Content.ReadFromJsonAsync<TaskInfo[]>(_jsonOptions);
        Assert.NotNull(tasks);
    }

    [Fact]
    public async Task GetTask_NonExistentId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.GetAsync("ScheduledTasks/nonexistent-task-id-xyz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTask_ValidId_ReturnsTaskInfo()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        // First get the list to retrieve a real task ID
        using var listResponse = await client.GetAsync("ScheduledTasks");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var tasks = await listResponse.Content.ReadFromJsonAsync<TaskInfo[]>(_jsonOptions);
        Assert.NotNull(tasks);
        Assert.NotEmpty(tasks);

        var firstTaskId = tasks[0].Id;
        Assert.NotNull(firstTaskId);

        using var getResponse = await client.GetAsync($"ScheduledTasks/{firstTaskId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var task = await getResponse.Content.ReadFromJsonAsync<TaskInfo>(_jsonOptions);
        Assert.NotNull(task);
        Assert.Equal(firstTaskId, task.Id);
        Assert.NotEmpty(task.Name);
    }

    [Fact]
    public async Task StartTask_NonExistentId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.PostAsync("ScheduledTasks/Running/nonexistent-task-id-xyz", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartTask_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        using var response = await client.PostAsync("ScheduledTasks/Running/any-task-id", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StopTask_NonExistentId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        using var response = await client.DeleteAsync("ScheduledTasks/Running/nonexistent-task-id-xyz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTaskTriggers_NonExistentId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        var triggers = new TaskTriggerInfo[]
        {
            new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(12).Ticks }
        };

        using var response = await client.PostAsJsonAsync("ScheduledTasks/nonexistent-task-id-xyz/Triggers", triggers, _jsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTaskTriggers_ValidId_ReturnsNoContent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.AddAuthHeader(_accessToken ??= await AuthHelper.CompleteStartupAsync(client));

        // Get a real task ID first
        using var listResponse = await client.GetAsync("ScheduledTasks");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var tasks = await listResponse.Content.ReadFromJsonAsync<TaskInfo[]>(_jsonOptions);
        Assert.NotNull(tasks);
        Assert.NotEmpty(tasks);

        var firstTask = tasks[0];
        Assert.NotNull(firstTask.Id);

        // Set a simple interval trigger — safe for any task
        var triggers = new TaskTriggerInfo[]
        {
            new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(24).Ticks }
        };

        using var updateResponse = await client.PostAsJsonAsync($"ScheduledTasks/{firstTask.Id}/Triggers", triggers, _jsonOptions);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
    }
}
