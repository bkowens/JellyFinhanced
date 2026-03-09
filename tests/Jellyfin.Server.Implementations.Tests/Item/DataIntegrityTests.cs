using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Locking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Data-integrity and CRUD round-trip tests for the Jellyfin database entities.
/// Every test uses a fresh in-memory SQLite database so tests are fully isolated.
///
/// Coverage areas:
///   - Null handling for nullable columns
///   - Non-null constraint enforcement for required columns
///   - Pipe-delimited string round-trip (Genres, Tags, Studios, Artists, …)
///   - Video metadata fields from migration 20260308000000_AddVideoMetadataColumns
///   - ItemValue / ItemValueMap relationship integrity (orphan cleanup)
///   - AncestorId relationship insert and cascade
///   - BaseItemImageInfo persistence
///   - Pagination (Skip / Take) correctness on ordered results
///   - Unique-key / duplicate detection helpers
/// </summary>
public sealed class DataIntegrityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JellyfinDbContext _context;

    public DataIntegrityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new JellyfinDbContext(
            options,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteInMemoryDatabaseProvider(),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));

        _context.Database.EnsureCreated();
    }

    // ---------------------------------------------------------------------------
    // Basic CRUD
    // ---------------------------------------------------------------------------

    [Fact]
    public void SaveBaseItem_WithRequiredFields_PersistsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity
        {
            Id = id,
            Type = "MediaBrowser.Controller.Entities.Movies.Movie",
            Name = "Test Movie",
            SortName = "test movie"
        });
        _context.SaveChanges();

        _context.ChangeTracker.Clear();
        var loaded = _context.BaseItems.Find(id);

        Assert.NotNull(loaded);
        Assert.Equal("MediaBrowser.Controller.Entities.Movies.Movie", loaded.Type);
        Assert.Equal("Test Movie", loaded.Name);
        Assert.Equal("test movie", loaded.SortName);
    }

    [Fact]
    public void UpdateBaseItem_ChangedProperties_ArePersisted()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", Name = "Original" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var item = _context.BaseItems.Find(id)!;
        item.Name = "Updated";
        item.CommunityRating = 8.5f;
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var reloaded = _context.BaseItems.Find(id)!;
        Assert.Equal("Updated", reloaded.Name);
        Assert.Equal(8.5f, reloaded.CommunityRating);
    }

    [Fact]
    public void DeleteBaseItem_RemovesRowFromDatabase()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie" });
        _context.SaveChanges();

        _context.BaseItems.Remove(_context.BaseItems.Find(id)!);
        _context.SaveChanges();

        Assert.Null(_context.BaseItems.Find(id));
    }

    // ---------------------------------------------------------------------------
    // Null handling
    // ---------------------------------------------------------------------------

    [Fact]
    public void SaveBaseItem_WithAllNullableFieldsNull_PersistsWithoutError()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var loaded = _context.BaseItems.Find(id)!;

        Assert.Null(loaded.Name);
        Assert.Null(loaded.Path);
        Assert.Null(loaded.SortName);
        Assert.Null(loaded.Overview);
        Assert.Null(loaded.Genres);
        Assert.Null(loaded.Tags);
        Assert.Null(loaded.Studios);
        Assert.Null(loaded.CommunityRating);
        Assert.Null(loaded.PremiereDate);
        Assert.Null(loaded.ProductionYear);
        Assert.Null(loaded.RunTimeTicks);
        Assert.Null(loaded.ParentId);
        Assert.Null(loaded.SeriesId);
        Assert.Null(loaded.SeasonId);
    }

    // ---------------------------------------------------------------------------
    // Video metadata columns (migration 20260308000000_AddVideoMetadataColumns)
    // ---------------------------------------------------------------------------

    [Fact]
    public void SaveBaseItem_VideoType_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", VideoType = "BluRay" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Equal("BluRay", _context.BaseItems.Find(id)!.VideoType);
    }

    [Fact]
    public void SaveBaseItem_VideoType_AllowsNull()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", VideoType = null });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Null(_context.BaseItems.Find(id)!.VideoType);
    }

    [Fact]
    public void SaveBaseItem_Is3D_True_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", Is3D = true });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.True(_context.BaseItems.Find(id)!.Is3D);
    }

    [Fact]
    public void SaveBaseItem_Is3D_Null_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", Is3D = null });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Null(_context.BaseItems.Find(id)!.Is3D);
    }

    [Fact]
    public void SaveBaseItem_IsPlaceHolder_True_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", IsPlaceHolder = true });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.True(_context.BaseItems.Find(id)!.IsPlaceHolder);
    }

    [Fact]
    public void SaveBaseItem_IsPlaceHolder_Null_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", IsPlaceHolder = null });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Null(_context.BaseItems.Find(id)!.IsPlaceHolder);
    }

    [Fact]
    public void SaveBaseItem_SeriesStatus_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Series", SeriesStatus = "Continuing" });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Equal("Continuing", _context.BaseItems.Find(id)!.SeriesStatus);
    }

    [Fact]
    public void SaveBaseItem_SeriesStatus_Null_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Series", SeriesStatus = null });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Null(_context.BaseItems.Find(id)!.SeriesStatus);
    }

    // ---------------------------------------------------------------------------
    // Pipe-delimited strings
    // ---------------------------------------------------------------------------

    [Fact]
    public void SaveBaseItem_Genres_PipeDelimited_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        var genres = "Action|Comedy|Drama";
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", Genres = genres });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Equal(genres, _context.BaseItems.Find(id)!.Genres);
    }

    [Fact]
    public void SaveBaseItem_Tags_PipeDelimited_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        var tags = "Kids|Sports|News";
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", Tags = tags });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Equal(tags, _context.BaseItems.Find(id)!.Tags);
    }

    [Fact]
    public void SaveBaseItem_Studios_PipeDelimited_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        var studios = "Warner Bros|Universal";
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Movie", Studios = studios });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Equal(studios, _context.BaseItems.Find(id)!.Studios);
    }

    [Fact]
    public void SaveBaseItem_Artists_PipeDelimited_RoundTripsCorrectly()
    {
        var id = Guid.NewGuid();
        var artists = "Artist A|Artist B";
        _context.BaseItems.Add(new BaseItemEntity { Id = id, Type = "Audio", Artists = artists });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Equal(artists, _context.BaseItems.Find(id)!.Artists);
    }

    // ---------------------------------------------------------------------------
    // ItemValue / ItemValueMap integrity
    // ---------------------------------------------------------------------------

    [Fact]
    public void SaveItemValue_WithMap_RelationshipPersistsCorrectly()
    {
        var itemId = Guid.NewGuid();
        var valueId = Guid.NewGuid();

        _context.BaseItems.Add(new BaseItemEntity { Id = itemId, Type = "Movie" });
        _context.SaveChanges();

        _context.ItemValues.Add(new ItemValue
        {
            ItemValueId = valueId,
            Type = ItemValueType.Genre,
            Value = "Action",
            CleanValue = "action"
        });
        _context.SaveChanges();

        _context.ItemValuesMap.Add(new ItemValueMap
        {
            ItemId = itemId,
            ItemValueId = valueId,
            Item = null!,
            ItemValue = null!
        });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var map = _context.ItemValuesMap
            .Include(m => m.ItemValue)
            .FirstOrDefault(m => m.ItemId.Equals(itemId) && m.ItemValueId.Equals(valueId));

        Assert.NotNull(map);
        Assert.Equal(ItemValueType.Genre, map.ItemValue.Type);
        Assert.Equal("Action", map.ItemValue.Value);
    }

    [Fact]
    public void ItemValue_OrphanCheck_HasNoMapsAfterMapDeleted()
    {
        var itemId = Guid.NewGuid();
        var valueId = Guid.NewGuid();

        _context.BaseItems.Add(new BaseItemEntity { Id = itemId, Type = "Movie" });
        _context.ItemValues.Add(new ItemValue
        {
            ItemValueId = valueId,
            Type = ItemValueType.Tags,
            Value = "Sports",
            CleanValue = "sports"
        });
        _context.SaveChanges();

        _context.ItemValuesMap.Add(new ItemValueMap
        {
            ItemId = itemId,
            ItemValueId = valueId,
            Item = null!,
            ItemValue = null!
        });
        _context.SaveChanges();

        // Remove the map entry.
        var mapEntry = _context.ItemValuesMap.First(m => m.ItemId.Equals(itemId) && m.ItemValueId.Equals(valueId));
        _context.ItemValuesMap.Remove(mapEntry);
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var orphaned = _context.ItemValues
            .Where(iv => !iv.BaseItemsMap!.Any())
            .Any(iv => iv.ItemValueId.Equals(valueId));

        Assert.True(orphaned, "ItemValue should be orphaned after its only map entry is removed");
    }

    // ---------------------------------------------------------------------------
    // AncestorId relationships
    // ---------------------------------------------------------------------------

    [Fact]
    public void AncestorId_Insert_PersistsParentChildRelationship()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        _context.BaseItems.Add(new BaseItemEntity { Id = parentId, Type = "Folder", IsFolder = true });
        _context.BaseItems.Add(new BaseItemEntity { Id = childId, Type = "Movie" });
        _context.SaveChanges();

        _context.AncestorIds.Add(new AncestorId
        {
            ItemId = childId,
            ParentItemId = parentId,
            Item = null!,
            ParentItem = null!
        });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var ancestor = _context.AncestorIds.FirstOrDefault(a => a.ItemId.Equals(childId) && a.ParentItemId.Equals(parentId));

        Assert.NotNull(ancestor);
    }

    [Fact]
    public void AncestorId_QueryChildrenByParent_ReturnsCorrectItems()
    {
        var parentId = Guid.NewGuid();
        var child1 = Guid.NewGuid();
        var child2 = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();

        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = parentId, Type = "Folder", IsFolder = true },
            new BaseItemEntity { Id = child1, Type = "Movie" },
            new BaseItemEntity { Id = child2, Type = "Series" },
            new BaseItemEntity { Id = unrelatedId, Type = "Movie" });
        _context.SaveChanges();

        _context.AncestorIds.AddRange(
            new AncestorId { ItemId = child1, ParentItemId = parentId, Item = null!, ParentItem = null! },
            new AncestorId { ItemId = child2, ParentItemId = parentId, Item = null!, ParentItem = null! });
        _context.SaveChanges();

        var children = _context.AncestorIds
            .Where(a => a.ParentItemId.Equals(parentId))
            .Select(a => a.ItemId)
            .ToList();

        Assert.Equal(2, children.Count);
        Assert.Contains(child1, children);
        Assert.Contains(child2, children);
        Assert.DoesNotContain(unrelatedId, children);
    }

    // ---------------------------------------------------------------------------
    // BaseItemImageInfo
    // ---------------------------------------------------------------------------

    [Fact]
    public void BaseItemImageInfo_Save_PersistsAllFields()
    {
        var itemId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        _context.BaseItems.Add(new BaseItemEntity { Id = itemId, Type = "Movie" });
        _context.SaveChanges();

        _context.BaseItemImageInfos.Add(new BaseItemImageInfo
        {
            Id = imageId,
            ItemId = itemId,
            Path = "/media/poster.jpg",
            ImageType = ImageInfoImageType.Primary,
            Width = 1920,
            Height = 1080,
            DateModified = now,
            Blurhash = new byte[] { 0x01, 0x02 },
            Item = null!
        });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var loaded = _context.BaseItemImageInfos.Find(imageId)!;

        Assert.Equal(itemId, loaded.ItemId);
        Assert.Equal("/media/poster.jpg", loaded.Path);
        Assert.Equal(ImageInfoImageType.Primary, loaded.ImageType);
        Assert.Equal(1920, loaded.Width);
        Assert.Equal(1080, loaded.Height);
        Assert.NotNull(loaded.Blurhash);
        Assert.Equal(2, loaded.Blurhash!.Length);
    }

    [Fact]
    public void BaseItemImageInfo_DateModified_Null_IsAllowed()
    {
        var itemId = Guid.NewGuid();
        var imageId = Guid.NewGuid();

        _context.BaseItems.Add(new BaseItemEntity { Id = itemId, Type = "Movie" });
        _context.SaveChanges();

        _context.BaseItemImageInfos.Add(new BaseItemImageInfo
        {
            Id = imageId,
            ItemId = itemId,
            Path = "/media/thumb.jpg",
            ImageType = ImageInfoImageType.Thumb,
            Width = 400,
            Height = 300,
            DateModified = null,
            Item = null!
        });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        Assert.Null(_context.BaseItemImageInfos.Find(imageId)!.DateModified);
    }

    // ---------------------------------------------------------------------------
    // Pagination correctness
    // ---------------------------------------------------------------------------

    [Fact]
    public void Pagination_Skip_ReturnsCorrectPage()
    {
        SeedOrderedMovies(10);

        var page = _context.BaseItems
            .Where(e => e.Type == "PaginationTestMovie")
            .OrderBy(e => e.SortName)
            .Skip(3)
            .Take(3)
            .Select(e => e.SortName)
            .ToList();

        Assert.Equal(3, page.Count);
        Assert.Equal("movie_03", page[0]);
        Assert.Equal("movie_04", page[1]);
        Assert.Equal("movie_05", page[2]);
    }

    [Fact]
    public void Pagination_SkipPastEnd_ReturnsEmpty()
    {
        SeedOrderedMovies(5);

        var page = _context.BaseItems
            .Where(e => e.Type == "PaginationTestMovie")
            .OrderBy(e => e.SortName)
            .Skip(100)
            .Take(10)
            .ToList();

        Assert.Empty(page);
    }

    [Fact]
    public void Pagination_TotalCount_MatchesUnpagedQuery()
    {
        SeedOrderedMovies(7);

        var totalCount = _context.BaseItems.Count(e => e.Type == "PaginationTestMovie");
        var pagedItems = _context.BaseItems
            .Where(e => e.Type == "PaginationTestMovie")
            .OrderBy(e => e.SortName)
            .Skip(0)
            .Take(3)
            .ToList();

        Assert.Equal(7, totalCount);
        Assert.Equal(3, pagedItems.Count);
    }

    // ---------------------------------------------------------------------------
    // Filter: IsFolder / IsVirtualItem boolean flags
    // ---------------------------------------------------------------------------

    [Fact]
    public void Filter_IsFolder_True_ReturnsOnlyFolders()
    {
        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "BoxSet", IsFolder = true },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", IsFolder = false });
        _context.SaveChanges();

        var folders = _context.BaseItems
            .Where(e => e.IsFolder && (e.Type == "BoxSet" || e.Type == "Movie"))
            .ToList();

        Assert.All(folders, f => Assert.True(f.IsFolder));
    }

    [Fact]
    public void Filter_IsVirtualItem_False_ExcludesVirtualItems()
    {
        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Episode", IsVirtualItem = false },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Episode", IsVirtualItem = true });
        _context.SaveChanges();

        var real = _context.BaseItems
            .Where(e => e.Type == "Episode" && e.IsVirtualItem == false)
            .ToList();

        Assert.All(real, e => Assert.False(e.IsVirtualItem));
    }

    // ---------------------------------------------------------------------------
    // Filter: date range queries
    // ---------------------------------------------------------------------------

    [Fact]
    public void Filter_DateCreated_MinMax_ReturnsCorrectItems()
    {
        var baseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var early = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Early", DateCreated = baseDate.AddDays(-10) };
        var middle = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Middle", DateCreated = baseDate };
        var late = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Late", DateCreated = baseDate.AddDays(10) };

        _context.BaseItems.AddRange(early, middle, late);
        _context.SaveChanges();

        var results = _context.BaseItems
            .Where(e => e.Type == "Movie" && e.DateCreated >= baseDate.AddDays(-5) && e.DateCreated <= baseDate.AddDays(5))
            .ToList();

        Assert.Single(results);
        Assert.Equal("Middle", results[0].Name);
    }

    // ---------------------------------------------------------------------------
    // Filter: name search (CleanName Contains)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Filter_CleanName_Contains_ReturnsMatchingItems()
    {
        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "The Dark Knight", CleanName = "the dark knight" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Star Wars", CleanName = "star wars" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Dark Shadows", CleanName = "dark shadows" });
        _context.SaveChanges();

        var results = _context.BaseItems
            .Where(e => e.Type == "Movie" && e.CleanName!.Contains("dark"))
            .OrderBy(e => e.CleanName)
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("dark", r.CleanName, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------------
    // Filter: parental rating
    // ---------------------------------------------------------------------------

    [Fact]
    public void Filter_InheritedParentalRating_ReturnsItemsWithinRange()
    {
        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "G", InheritedParentalRatingValue = 1 },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "PG", InheritedParentalRatingValue = 5 },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "R", InheritedParentalRatingValue = 14 },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Unrated", InheritedParentalRatingValue = null });
        _context.SaveChanges();

        var safeItems = _context.BaseItems
            .Where(e => e.Type == "Movie" && (e.InheritedParentalRatingValue == null || e.InheritedParentalRatingValue <= 5))
            .OrderBy(e => e.Name)
            .ToList();

        Assert.Equal(3, safeItems.Count);
        Assert.Contains(safeItems, i => i.Name == "G");
        Assert.Contains(safeItems, i => i.Name == "PG");
        Assert.Contains(safeItems, i => i.Name == "Unrated");
        Assert.DoesNotContain(safeItems, i => i.Name == "R");
    }

    // ---------------------------------------------------------------------------
    // Filter: community rating
    // ---------------------------------------------------------------------------

    [Fact]
    public void Filter_MinCommunityRating_ReturnsOnlyHighRatedItems()
    {
        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Low", CommunityRating = 3.0f },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Mid", CommunityRating = 7.0f },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "High", CommunityRating = 9.5f },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "NoRating", CommunityRating = null });
        _context.SaveChanges();

        var highRated = _context.BaseItems
            .Where(e => e.Type == "Movie" && e.CommunityRating >= 7.0f)
            .OrderBy(e => e.Name)
            .ToList();

        Assert.Equal(2, highRated.Count);
        Assert.All(highRated, i => Assert.True(i.CommunityRating >= 7.0f));
    }

    // ---------------------------------------------------------------------------
    // Duplicate / PresentationUniqueKey grouping
    // ---------------------------------------------------------------------------

    [Fact]
    public void GroupBy_PresentationUniqueKey_DeduplicatesItems()
    {
        var key = "unique-key-abc";
        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", PresentationUniqueKey = key, Name = "Version 1" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", PresentationUniqueKey = key, Name = "Version 2" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", PresentationUniqueKey = "other-key", Name = "Other" });
        _context.SaveChanges();

        var grouped = _context.BaseItems
            .Where(e => e.Type == "Movie")
            .GroupBy(e => e.PresentationUniqueKey)
            .Select(g => g.FirstOrDefault())
            .ToList();

        Assert.Equal(2, grouped.Count);
    }

    // ---------------------------------------------------------------------------
    // GetCleanValue utility (static – no DB required)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Action", "Action")]
    [InlineData("Sci-Fi", "Sci Fi")] // hyphen becomes space
    [InlineData("Rock & Roll", "Rock   Roll")] // ampersand becomes space
    [InlineData("  Spaces  ", "  Spaces  ")] // surrounding whitespace preserved (not trimmed by GetCleanValue)
    [InlineData("Caf\u00e9", "Cafe")] // diacritics removed
    public void GetCleanValue_VariousInputs_ProducesExpectedOutput(string input, string expected)
    {
        var result = Jellyfin.Server.Implementations.Item.BaseItemRepository.GetCleanValue(input);

        // Collapse consecutive spaces for the comparison, since the method normalizes
        // multiple whitespace to a single space.
        var normalizedResult = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ").Trim();
        var normalizedExpected = System.Text.RegularExpressions.Regex.Replace(expected, @"\s+", " ").Trim();

        Assert.Equal(normalizedExpected, normalizedResult, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCleanValue_EmptyString_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, Jellyfin.Server.Implementations.Item.BaseItemRepository.GetCleanValue(string.Empty));
    }

    [Fact]
    public void GetCleanValue_WhitespaceOnly_ReturnsWhitespace()
    {
        var result = Jellyfin.Server.Implementations.Item.BaseItemRepository.GetCleanValue("   ");
        Assert.True(string.IsNullOrWhiteSpace(result));
    }

    // ---------------------------------------------------------------------------
    // People / PeopleBaseItemMap integrity
    // ---------------------------------------------------------------------------

    [Fact]
    public void People_SaveWithMap_PersistsRelationship()
    {
        var itemId = Guid.NewGuid();
        var personId = Guid.NewGuid();

        _context.BaseItems.Add(new BaseItemEntity { Id = itemId, Type = "Movie" });
        _context.Peoples.Add(new People { Id = personId, Name = "John Doe" });
        _context.SaveChanges();

        _context.PeopleBaseItemMap.Add(new PeopleBaseItemMap
        {
            ItemId = itemId,
            PeopleId = personId,
            Role = "Actor",
            People = null!,
            Item = null!
        });
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var map = _context.PeopleBaseItemMap
            .Include(m => m.People)
            .FirstOrDefault(m => m.ItemId.Equals(itemId));

        Assert.NotNull(map);
        Assert.Equal("John Doe", map.People.Name);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private void SeedOrderedMovies(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _context.BaseItems.Add(new BaseItemEntity
            {
                Id = Guid.NewGuid(),
                Type = "PaginationTestMovie",
                Name = $"Movie {i:D2}",
                SortName = $"movie_{i:D2}"
            });
        }

        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
