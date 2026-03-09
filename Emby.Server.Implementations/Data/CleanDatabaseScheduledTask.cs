#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Data;

public class CleanDatabaseScheduledTask : ILibraryPostScanTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<CleanDatabaseScheduledTask> _logger;
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IPathManager _pathManager;

    public CleanDatabaseScheduledTask(
        ILibraryManager libraryManager,
        ILogger<CleanDatabaseScheduledTask> logger,
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IPathManager pathManager)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _dbProvider = dbProvider;
        _pathManager = pathManager;
    }

    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await CleanDeadItems(cancellationToken, progress).ConfigureAwait(false);
    }

    private async Task CleanDeadItems(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var itemIds = _libraryManager.GetItemIds(new InternalItemsQuery
        {
            HasDeadParentId = true
        });

        var numComplete = 0;
        var numItems = itemIds.Count + 1;

        _logger.LogDebug("Cleaning {Number} items with dead parents", numItems);

        IProgress<double> subProgress = new Progress<double>((val) => progress.Report(val / 2));

        // Issue #7 fix: batch-load all dead items in a single query instead of one GetItemById per item.
        var deadItems = itemIds.Count > 0
            ? _libraryManager.GetItemList(new InternalItemsQuery
            {
                ItemIds = itemIds.ToArray(),
                DtoOptions = new DtoOptions(false)
            }).ToDictionary(i => i.Id)
            : new Dictionary<Guid, BaseItem>();

        foreach (var itemId in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!deadItems.TryGetValue(itemId, out var item))
            {
                numComplete++;
                subProgress.Report((double)numComplete / numItems * 100);
                continue;
            }

            _logger.LogInformation("Cleaning item {Item} type: {Type} path: {Path}", item.Name, item.GetType().Name, item.Path ?? string.Empty);

            foreach (var mediaSource in item.GetMediaSources(false))
            {
                // Delete extracted data — GetItemById uses in-memory cache so this is fast.
                // MediaSourceInfo.Id is a string; parse it to Guid before lookup.
                if (!Guid.TryParse(mediaSource.Id, out var mediaSourceId))
                {
                    continue;
                }

                var mediaSourceItem = _libraryManager.GetItemById(mediaSourceId);
                if (mediaSourceItem is null)
                {
                    continue;
                }

                var extractedDataFolders = _pathManager.GetExtractedDataPaths(mediaSourceItem);
                foreach (var folder in extractedDataFolders)
                {
                    if (Directory.Exists(folder))
                    {
                        try
                        {
                            Directory.Delete(folder, true);
                        }
                        catch (Exception e)
                        {
                            _logger.LogWarning("Failed to remove {Folder}: {Exception}", folder, e.Message);
                        }
                    }
                }
            }

            // Delete item
            _libraryManager.DeleteItem(item, new DeleteOptions
            {
                DeleteFileLocation = false
            });

            numComplete++;
            double percent = numComplete;
            percent /= numItems;
            subProgress.Report(percent * 100);
        }

        subProgress = new Progress<double>((val) => progress.Report((val / 2) + 50));
        var context = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                // Use RemoveRange for MySQL provider compatibility (ExecuteDelete not supported)
                var orphanedItemValues = await context.ItemValues
                    .Where(e => e.BaseItemsMap!.Count == 0)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                context.ItemValues.RemoveRange(orphanedItemValues);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                subProgress.Report(50);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                subProgress.Report(100);
            }
        }

        progress.Report(100);
    }
}
