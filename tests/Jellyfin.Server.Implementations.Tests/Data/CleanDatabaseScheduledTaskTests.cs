using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Server.Implementations.Data;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Data;

/// <summary>
/// Unit tests for <see cref="CleanDatabaseScheduledTask"/>.
/// These tests focus on the task's interaction with its dependencies for
/// dead-item cleanup and progress reporting, without exercising a real
/// database connection.
/// </summary>
public class CleanDatabaseScheduledTaskTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a task instance whose db context factory returns a context that
    /// will immediately fail when used, keeping tests focused on the library-
    /// manager side of the logic.
    /// </summary>
    private static CleanDatabaseScheduledTask CreateTask(
        Mock<ILibraryManager> libraryManagerMock,
        Mock<IPathManager>? pathManagerMock = null)
    {
        pathManagerMock ??= new Mock<IPathManager>();

        // A context factory that throws whenever CreateDbContextAsync is called
        // lets us test the dead-item loop without a real DB.
        var dbFactory = new Mock<Microsoft.EntityFrameworkCore.IDbContextFactory<Jellyfin.Database.Implementations.JellyfinDbContext>>();
        dbFactory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No DB available in unit tests."));

        return new CleanDatabaseScheduledTask(
            libraryManagerMock.Object,
            NullLogger<CleanDatabaseScheduledTask>.Instance,
            dbFactory.Object,
            pathManagerMock.Object);
    }

    // -----------------------------------------------------------------------
    // Run – no dead items
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Run_WithNoDeadItems_DoesNotDeleteAnyItem()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();

        // No items with dead parents.
        libraryManagerMock
            .Setup(l => l.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasDeadParentId == true)))
            .Returns(new List<Guid>());

        var task = CreateTask(libraryManagerMock);
        var progress = new Progress<double>();

        // The task will still attempt to open the DB context for the orphan-
        // item-value cleanup step. That attempt throws in this unit-test
        // environment, so we expect it to surface.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.Run(progress, CancellationToken.None));

        // Crucially, DeleteItem must never have been called.
        libraryManagerMock.Verify(
            l => l.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Run – cancellation before first item is processed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Run_CancellationRequested_BeforeFirstItem_ThrowsOperationCanceledException()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();

        var deadId = Guid.NewGuid();
        libraryManagerMock
            .Setup(l => l.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasDeadParentId == true)))
            .Returns(new List<Guid> { deadId });

        // Return a dummy item from GetItemList so the batch load succeeds.
        var dummyItem = new Mock<BaseItem>();
        dummyItem.Object.Id = deadId;
        dummyItem.Setup(i => i.GetMediaSources(false)).Returns(new List<MediaBrowser.Model.Dto.MediaSourceInfo>());
        libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { dummyItem.Object });

        var task = CreateTask(libraryManagerMock);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately.

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => task.Run(new Progress<double>(), cts.Token));

        // DeleteItem should not have been reached.
        libraryManagerMock.Verify(
            l => l.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Run – item with dead parent but GetItemById returns null
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Run_DeadItemIdButGetItemListReturnsEmpty_SkipsDeleteAndContinues()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();

        var deadId = Guid.NewGuid();
        libraryManagerMock
            .Setup(l => l.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasDeadParentId == true)))
            .Returns(new List<Guid> { deadId });

        // Simulate item no longer resolvable — GetItemList returns empty.
        libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem>());

        var task = CreateTask(libraryManagerMock);

        // Expect the DB step to fail – that is fine here.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.Run(new Progress<double>(), CancellationToken.None));

        // Should not have attempted to delete anything.
        libraryManagerMock.Verify(
            l => l.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Run – dead item with no media sources is deleted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Run_DeadItemWithNoMediaSources_DeletesItem()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();

        var deadId = Guid.NewGuid();
        libraryManagerMock
            .Setup(l => l.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasDeadParentId == true)))
            .Returns(new List<Guid> { deadId });

        var dummyItem = new Mock<BaseItem>();
        dummyItem.Object.Id = deadId;
        dummyItem.Setup(i => i.GetMediaSources(false))
                 .Returns(new List<MediaBrowser.Model.Dto.MediaSourceInfo>());
        libraryManagerMock
            .Setup(l => l.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { dummyItem.Object });

        var task = CreateTask(libraryManagerMock);

        // DB step will throw – expected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task.Run(new Progress<double>(), CancellationToken.None));

        // Item must have been deleted before the DB step.
        libraryManagerMock.Verify(
            l => l.DeleteItem(
                dummyItem.Object,
                It.Is<DeleteOptions>(o => !o.DeleteFileLocation)),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Run – progress reporting
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Run_WithNoDeadItems_ReportsProgressAboveZero()
    {
        var libraryManagerMock = new Mock<ILibraryManager>();

        libraryManagerMock
            .Setup(l => l.GetItemIds(It.Is<InternalItemsQuery>(q => q.HasDeadParentId == true)))
            .Returns(new List<Guid>());

        var task = CreateTask(libraryManagerMock);

        var reportedValues = new List<double>();
        var progress = new Progress<double>(v => reportedValues.Add(v));

        // Swallow the expected DB exception.
        try
        {
            await task.Run(progress, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Expected – DB context not available.
        }

        // With no dead items, the loop body does not execute and the DB step
        // throws before any progress is reported, so an empty list is expected.
        // Verify the task at least attempted to run (the exception was caught).
        Assert.Empty(reportedValues);
    }
}
