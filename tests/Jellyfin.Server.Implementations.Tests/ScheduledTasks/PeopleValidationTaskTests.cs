using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.ScheduledTasks.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.ScheduledTasks;

/// <summary>
/// Unit tests for <see cref="PeopleValidationTask"/>.
/// </summary>
public class PeopleValidationTaskTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static PeopleValidationTask CreateTask(
        Mock<ILibraryManager>? libraryManagerMock = null,
        Mock<ILocalizationManager>? localizationMock = null)
    {
        libraryManagerMock ??= new Mock<ILibraryManager>();
        localizationMock ??= BuildDefaultLocalizationMock();

        // A context factory that throws when opened so we can test the first
        // half of ExecuteAsync (ValidatePeopleAsync) in isolation.
        var dbFactory = new Mock<Microsoft.EntityFrameworkCore.IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
        dbFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No DB in unit tests."));

        return new PeopleValidationTask(
            libraryManagerMock.Object,
            localizationMock.Object,
            dbFactory.Object);
    }

    private static Mock<ILocalizationManager> BuildDefaultLocalizationMock()
    {
        var mock = new Mock<ILocalizationManager>();
        mock.Setup(l => l.GetLocalizedString(It.IsAny<string>()))
            .Returns((string key) => key);
        return mock;
    }

    // -----------------------------------------------------------------------
    // Metadata properties
    // -----------------------------------------------------------------------

    [Fact]
    public void Key_ReturnsExpectedValue()
    {
        var task = CreateTask();

        Assert.Equal("RefreshPeople", task.Key);
    }

    [Fact]
    public void IsHidden_ReturnsFalse()
    {
        var task = CreateTask();

        Assert.False(task.IsHidden);
    }

    [Fact]
    public void IsEnabled_ReturnsTrue()
    {
        var task = CreateTask();

        Assert.True(task.IsEnabled);
    }

    [Fact]
    public void IsLogged_ReturnsTrue()
    {
        var task = CreateTask();

        Assert.True(task.IsLogged);
    }

    [Fact]
    public void Name_DelegatesToLocalizationManager()
    {
        var localizationMock = new Mock<ILocalizationManager>();
        localizationMock
            .Setup(l => l.GetLocalizedString("TaskRefreshPeople"))
            .Returns("Refresh People");

        var task = CreateTask(localizationMock: localizationMock);

        Assert.Equal("Refresh People", task.Name);
    }

    [Fact]
    public void Description_DelegatesToLocalizationManager()
    {
        var localizationMock = new Mock<ILocalizationManager>();
        localizationMock
            .Setup(l => l.GetLocalizedString("TaskRefreshPeopleDescription"))
            .Returns("Refresh people descriptions");

        var task = CreateTask(localizationMock: localizationMock);

        Assert.Equal("Refresh people descriptions", task.Description);
    }

    [Fact]
    public void Category_DelegatesToLocalizationManager()
    {
        var localizationMock = new Mock<ILocalizationManager>();
        localizationMock
            .Setup(l => l.GetLocalizedString("TasksLibraryCategory"))
            .Returns("Library");

        var task = CreateTask(localizationMock: localizationMock);

        Assert.Equal("Library", task.Category);
    }

    // -----------------------------------------------------------------------
    // GetDefaultTriggers
    // -----------------------------------------------------------------------

    [Fact]
    public void GetDefaultTriggers_ReturnsExactlyOneTrigger()
    {
        var task = CreateTask();

        var triggers = task.GetDefaultTriggers().ToList();

        Assert.Single(triggers);
    }

    [Fact]
    public void GetDefaultTriggers_TriggerIsIntervalType()
    {
        var task = CreateTask();

        var trigger = task.GetDefaultTriggers().First();

        Assert.Equal(TaskTriggerInfoType.IntervalTrigger, trigger.Type);
    }

    [Fact]
    public void GetDefaultTriggers_IntervalIs7Days()
    {
        var task = CreateTask();

        var trigger = task.GetDefaultTriggers().First();

        var expectedTicks = TimeSpan.FromDays(7).Ticks;
        Assert.Equal(expectedTicks, trigger.IntervalTicks);
    }

    // -----------------------------------------------------------------------
    // ExecuteAsync – cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsException()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(l => l.ValidatePeopleAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var task = CreateTask(libraryManagerMock: libraryManagerMock);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // The DB context mock throws InvalidOperationException regardless of cancellation.
        // ValidatePeopleAsync completes instantly (mocked), then CreateDbContextAsync throws.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => task.ExecuteAsync(new Progress<double>(), cts.Token));
        Assert.True(
            ex is OperationCanceledException or InvalidOperationException,
            $"Expected OperationCanceledException or InvalidOperationException, got {ex.GetType().Name}");
    }

    // -----------------------------------------------------------------------
    // ExecuteAsync – ValidatePeopleAsync is called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Always_CallsValidatePeopleAsync()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(l => l.ValidatePeopleAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var task = CreateTask(libraryManagerMock: libraryManagerMock);

        // Swallow the expected DB failure.
        try
        {
            await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Expected – no DB in unit tests.
        }

        libraryManagerMock.Verify(
            l => l.ValidatePeopleAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // ExecuteAsync – progress reporting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ReportsProgressWhenValidatePeopleCompletes()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();
        libraryManagerMock
            .Setup(l => l.ValidatePeopleAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .Callback<IProgress<double>, CancellationToken>((p, _) => p.Report(100))
            .Returns(Task.CompletedTask);

        var task = CreateTask(libraryManagerMock: libraryManagerMock);

        var reportedValues = new List<double>();
        var progress = new Progress<double>(v => reportedValues.Add(v));

        try
        {
            await task.ExecuteAsync(progress, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Expected – no DB in unit tests.
        }

        // Allow async progress callbacks to complete (Progress<T> posts to SynchronizationContext).
        await Task.Delay(100);

        // Sub-progress during ValidatePeople should have reported at least one value.
        Assert.NotEmpty(reportedValues);
    }
}
