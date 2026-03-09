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
/// Validates that the EF Core model matches the expected database schema.
/// Tests cover:
///   - Column presence and nullability for all BaseItem fields
///   - Video metadata columns added in migration 20260308000000_AddVideoMetadataColumns
///   - Index creation for performance-critical query paths
///   - Foreign-key / relationship configuration
///   - Data seeded by model configuration (the placeholder item)
/// </summary>
public sealed class DatabaseSchemaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JellyfinDbContext _context;

    public DatabaseSchemaTests()
    {
        // Use a persistent in-memory connection so schema inspection queries work.
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
    // Video metadata columns (migration 20260308000000_AddVideoMetadataColumns)
    // ---------------------------------------------------------------------------

    [Fact]
    public void BaseItemsTable_VideoType_ColumnExists_AndIsNullable()
    {
        var column = GetColumnInfo("BaseItems", "VideoType");

        Assert.NotNull(column);
        Assert.True(column.IsNullable, "VideoType should be nullable (TEXT NULL)");
    }

    [Fact]
    public void BaseItemsTable_SeriesStatus_ColumnExists_AndIsNullable()
    {
        var column = GetColumnInfo("BaseItems", "SeriesStatus");

        Assert.NotNull(column);
        Assert.True(column.IsNullable, "SeriesStatus should be nullable (TEXT NULL)");
    }

    [Fact]
    public void BaseItemsTable_Is3D_ColumnExists_AndIsNullable()
    {
        var column = GetColumnInfo("BaseItems", "Is3D");

        Assert.NotNull(column);
        Assert.True(column.IsNullable, "Is3D should be nullable (INTEGER NULL)");
    }

    [Fact]
    public void BaseItemsTable_IsPlaceHolder_ColumnExists_AndIsNullable()
    {
        var column = GetColumnInfo("BaseItems", "IsPlaceHolder");

        Assert.NotNull(column);
        Assert.True(column.IsNullable, "IsPlaceHolder should be nullable (INTEGER NULL)");
    }

    // ---------------------------------------------------------------------------
    // Core BaseItem columns – non-nullable
    // ---------------------------------------------------------------------------

    [Fact]
    public void BaseItemsTable_Id_ColumnExists_AndIsNotNullable()
    {
        var column = GetColumnInfo("BaseItems", "Id");

        Assert.NotNull(column);
        Assert.False(column.IsNullable, "Id (primary key) must be NOT NULL");
    }

    [Fact]
    public void BaseItemsTable_Type_ColumnExists_AndIsNotNullable()
    {
        var column = GetColumnInfo("BaseItems", "Type");

        Assert.NotNull(column);
        Assert.False(column.IsNullable, "Type must be NOT NULL");
    }

    [Fact]
    public void BaseItemsTable_IsFolder_ColumnExists()
    {
        var column = GetColumnInfo("BaseItems", "IsFolder");
        Assert.NotNull(column);
    }

    [Fact]
    public void BaseItemsTable_IsVirtualItem_ColumnExists()
    {
        var column = GetColumnInfo("BaseItems", "IsVirtualItem");
        Assert.NotNull(column);
    }

    [Fact]
    public void BaseItemsTable_IsLocked_ColumnExists()
    {
        var column = GetColumnInfo("BaseItems", "IsLocked");
        Assert.NotNull(column);
    }

    // ---------------------------------------------------------------------------
    // Nullable scalar columns
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Name")]
    [InlineData("Path")]
    [InlineData("SortName")]
    [InlineData("ForcedSortName")]
    [InlineData("Overview")]
    [InlineData("OfficialRating")]
    [InlineData("CustomRating")]
    [InlineData("Genres")]
    [InlineData("Tags")]
    [InlineData("Studios")]
    [InlineData("MediaType")]
    [InlineData("CleanName")]
    [InlineData("PresentationUniqueKey")]
    [InlineData("SeriesPresentationUniqueKey")]
    [InlineData("OriginalTitle")]
    [InlineData("Album")]
    [InlineData("SeriesName")]
    [InlineData("SeasonName")]
    [InlineData("ExternalId")]
    [InlineData("ExternalSeriesId")]
    [InlineData("Tagline")]
    [InlineData("PrimaryVersionId")]
    [InlineData("Artists")]
    [InlineData("AlbumArtists")]
    [InlineData("EpisodeTitle")]
    [InlineData("UnratedType")]
    [InlineData("OwnerId")]
    [InlineData("ShowId")]
    public void BaseItemsTable_NullableTextColumn_Exists(string columnName)
    {
        var column = GetColumnInfo("BaseItems", columnName);

        Assert.NotNull(column);
        Assert.True(column.IsNullable, $"Column '{columnName}' should be nullable");
    }

    [Theory]
    [InlineData("CommunityRating")]
    [InlineData("CriticRating")]
    [InlineData("LUFS")]
    [InlineData("NormalizationGain")]
    [InlineData("RunTimeTicks")]
    [InlineData("Size")]
    [InlineData("IndexNumber")]
    [InlineData("ParentIndexNumber")]
    [InlineData("ProductionYear")]
    [InlineData("TotalBitrate")]
    [InlineData("Width")]
    [InlineData("Height")]
    [InlineData("InheritedParentalRatingValue")]
    [InlineData("InheritedParentalRatingSubValue")]
    [InlineData("ParentId")]
    [InlineData("TopParentId")]
    [InlineData("SeasonId")]
    [InlineData("SeriesId")]
    [InlineData("ChannelId")]
    [InlineData("DateCreated")]
    [InlineData("DateModified")]
    [InlineData("DateLastRefreshed")]
    [InlineData("DateLastSaved")]
    [InlineData("DateLastMediaAdded")]
    [InlineData("StartDate")]
    [InlineData("EndDate")]
    [InlineData("PremiereDate")]
    public void BaseItemsTable_NullableScalarColumn_Exists(string columnName)
    {
        var column = GetColumnInfo("BaseItems", columnName);

        Assert.NotNull(column);
        Assert.True(column.IsNullable, $"Column '{columnName}' should be nullable");
    }

    // ---------------------------------------------------------------------------
    // Index verification
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Path")]
    [InlineData("ParentId")]
    [InlineData("PresentationUniqueKey")]
    public void BaseItemsTable_SingleColumnIndex_Exists(string columnName)
    {
        var indexes = GetIndexColumns("BaseItems");

        Assert.True(
            indexes.Any(idx => idx.Contains(columnName, StringComparer.OrdinalIgnoreCase)),
            $"Expected an index covering column '{columnName}' on BaseItems");
    }

    [Fact]
    public void BaseItemsTable_CompositeIndex_IdTypeIsFolderIsVirtualItem_Exists()
    {
        var indexes = GetIndexColumns("BaseItems");

        Assert.True(
            indexes.Any(idx =>
                idx.Contains("Id", StringComparer.OrdinalIgnoreCase) &&
                idx.Contains("Type", StringComparer.OrdinalIgnoreCase) &&
                idx.Contains("IsFolder", StringComparer.OrdinalIgnoreCase) &&
                idx.Contains("IsVirtualItem", StringComparer.OrdinalIgnoreCase)),
            "Expected composite index on (Id, Type, IsFolder, IsVirtualItem)");
    }

    [Fact]
    public void BaseItemsTable_CompositeIndex_TopParentIdId_Exists()
    {
        var indexes = GetIndexColumns("BaseItems");

        Assert.True(
            indexes.Any(idx =>
                idx.Contains("TopParentId", StringComparer.OrdinalIgnoreCase) &&
                idx.Contains("Id", StringComparer.OrdinalIgnoreCase)),
            "Expected covering index on (TopParentId, Id)");
    }

    // ---------------------------------------------------------------------------
    // Seeded placeholder item
    // ---------------------------------------------------------------------------

    [Fact]
    public void BaseItemsTable_PlaceholderItem_IsSeededCorrectly()
    {
        var placeholder = _context.BaseItems
            .FirstOrDefault(e => e.Id.Equals(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        Assert.NotNull(placeholder);
        Assert.Equal("PLACEHOLDER", placeholder.Type);
    }

    // ---------------------------------------------------------------------------
    // Related-table schemas
    // ---------------------------------------------------------------------------

    [Fact]
    public void ItemValuesTable_RequiredColumns_Exist()
    {
        Assert.NotNull(GetColumnInfo("ItemValues", "ItemValueId"));
        Assert.NotNull(GetColumnInfo("ItemValues", "Type"));
        Assert.NotNull(GetColumnInfo("ItemValues", "Value"));
        Assert.NotNull(GetColumnInfo("ItemValues", "CleanValue"));
    }

    [Fact]
    public void ItemValuesMapTable_RequiredColumns_Exist()
    {
        Assert.NotNull(GetColumnInfo("ItemValuesMap", "ItemId"));
        Assert.NotNull(GetColumnInfo("ItemValuesMap", "ItemValueId"));
    }

    [Fact]
    public void AncestorIdsTable_RequiredColumns_Exist()
    {
        Assert.NotNull(GetColumnInfo("AncestorIds", "ItemId"));
        Assert.NotNull(GetColumnInfo("AncestorIds", "ParentItemId"));
    }

    [Fact]
    public void BaseItemImageInfosTable_RequiredColumns_Exist()
    {
        Assert.NotNull(GetColumnInfo("BaseItemImageInfos", "Id"));
        Assert.NotNull(GetColumnInfo("BaseItemImageInfos", "ItemId"));
        Assert.NotNull(GetColumnInfo("BaseItemImageInfos", "Path"));
        Assert.NotNull(GetColumnInfo("BaseItemImageInfos", "ImageType"));
    }

    [Fact]
    public void BaseItemImageInfosTable_DateModified_IsNullable()
    {
        var column = GetColumnInfo("BaseItemImageInfos", "DateModified");

        Assert.NotNull(column);
        Assert.True(column.IsNullable, "BaseItemImageInfo.DateModified must be nullable after migration BaseItemImageInfoDateModifiedNullable");
    }

    [Fact]
    public void PeoplesTable_RequiredColumns_Exist()
    {
        Assert.NotNull(GetColumnInfo("Peoples", "Id"));
        Assert.NotNull(GetColumnInfo("Peoples", "Name"));
    }

    [Fact]
    public void PeopleBaseItemMapTable_RequiredColumns_Exist()
    {
        Assert.NotNull(GetColumnInfo("PeopleBaseItemMap", "ItemId"));
        Assert.NotNull(GetColumnInfo("PeopleBaseItemMap", "PeopleId"));
    }

    // ---------------------------------------------------------------------------
    // Foreign-key: BaseItems.ParentId -> BaseItems.Id (cascade delete)
    // ---------------------------------------------------------------------------

    [Fact]
    public void BaseItems_CascadeDeleteFromParent_RemovesChildRows()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        _context.BaseItems.Add(new BaseItemEntity { Id = parentId, Type = "Folder", IsFolder = true });
        _context.BaseItems.Add(new BaseItemEntity { Id = childId, Type = "Movie", ParentId = parentId });
        _context.SaveChanges();

        _context.BaseItems.Remove(_context.BaseItems.Find(parentId)!);
        _context.SaveChanges();

        Assert.Null(_context.BaseItems.Find(childId));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private ColumnInfo? GetColumnInfo(string tableName, string columnName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);   // column name
            var notNull = reader.GetInt32(3); // 1 = NOT NULL, 0 = nullable

            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return new ColumnInfo(name, IsNullable: notNull == 0);
            }
        }

        return null;
    }

    /// <summary>Returns a list of column-name-sets for every index on the given table.</summary>
    private List<List<string>> GetIndexColumns(string tableName)
    {
        var result = new List<List<string>>();

        using var listCmd = _connection.CreateCommand();
        listCmd.CommandText = $"PRAGMA index_list(\"{tableName}\")";

        var indexNames = new List<string>();
        using (var reader = listCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                indexNames.Add(reader.GetString(1)); // index name
            }
        }

        foreach (var indexName in indexNames)
        {
            using var infoCmd = _connection.CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info(\"{indexName}\")";

            var columns = new List<string>();
            using var infoReader = infoCmd.ExecuteReader();
            while (infoReader.Read())
            {
                columns.Add(infoReader.GetString(2)); // column name
            }

            result.Add(columns);
        }

        return result;
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed record ColumnInfo(string Name, bool IsNullable);
}
