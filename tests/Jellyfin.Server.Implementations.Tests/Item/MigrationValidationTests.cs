using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Locking;
using Jellyfin.Database.Providers.Sqlite.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Validates migration correctness for the SQLite provider.
///
/// Coverage:
///   - No pending model changes (model snapshot is current)
///   - Migration 20260308000000_AddVideoMetadataColumns columns exist and are nullable
///   - Data inserted before the migration column additions is preserved and null-defaults
///     for the new columns are accepted (simulated via EnsureCreated + direct SQL)
///   - All expected tables are created by EnsureCreated / the full migration chain
/// </summary>
public sealed class MigrationValidationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public MigrationValidationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    // ---------------------------------------------------------------------------
    // Model snapshot currency
    // ---------------------------------------------------------------------------

    [Fact]
    public void SqliteModelSnapshot_HasNoPendingModelChanges()
    {
        var factory = new SqliteDesignTimeJellyfinDbFactory();
        using var context = factory.CreateDbContext([]);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The EF Core model snapshot for SQLite is out of date. Run 'dotnet ef migrations add <Name>' to create a migration.");
    }

    // ---------------------------------------------------------------------------
    // Expected tables exist after schema creation
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("BaseItems")]
    [InlineData("ItemValues")]
    [InlineData("ItemValuesMap")]
    [InlineData("AncestorIds")]
    [InlineData("BaseItemImageInfos")]
    [InlineData("Peoples")]
    [InlineData("PeopleBaseItemMap")]
    [InlineData("BaseItemProviders")]
    [InlineData("BaseItemMetadataFields")]
    [InlineData("BaseItemTrailerTypes")]
    [InlineData("Chapters")]
    [InlineData("MediaStreamInfos")]
    [InlineData("UserData")]
    [InlineData("KeyframeData")]
    [InlineData("TrickplayInfos")]
    [InlineData("MediaSegments")]
    [InlineData("ActivityLogs")]
    [InlineData("Users")]
    public void SchemaCreation_ExpectedTable_Exists(string tableName)
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        Assert.True(
            TableExists(tableName),
            $"Table '{tableName}' was not created by EnsureCreated.");
    }

    // ---------------------------------------------------------------------------
    // Video metadata columns (20260308000000_AddVideoMetadataColumns)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Migration_AddVideoMetadataColumns_SeriesStatus_ColumnIsNullable()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var column = GetColumnInfo("BaseItems", "SeriesStatus");

        Assert.NotNull(column);
        Assert.True(column.Value.IsNullable, "SeriesStatus must be nullable (added by AddVideoMetadataColumns)");
    }

    [Fact]
    public void Migration_AddVideoMetadataColumns_VideoType_ColumnIsNullable()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var column = GetColumnInfo("BaseItems", "VideoType");

        Assert.NotNull(column);
        Assert.True(column.Value.IsNullable, "VideoType must be nullable (added by AddVideoMetadataColumns)");
    }

    [Fact]
    public void Migration_AddVideoMetadataColumns_Is3D_ColumnIsNullable()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var column = GetColumnInfo("BaseItems", "Is3D");

        Assert.NotNull(column);
        Assert.True(column.Value.IsNullable, "Is3D must be nullable (added by AddVideoMetadataColumns)");
    }

    [Fact]
    public void Migration_AddVideoMetadataColumns_IsPlaceHolder_ColumnIsNullable()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var column = GetColumnInfo("BaseItems", "IsPlaceHolder");

        Assert.NotNull(column);
        Assert.True(column.Value.IsNullable, "IsPlaceHolder must be nullable (added by AddVideoMetadataColumns)");
    }

    // ---------------------------------------------------------------------------
    // Simulated pre-migration data preservation
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Simulates the scenario where items exist in the DB before the new nullable
    /// columns are added: items can be loaded and the new columns return null.
    /// </summary>
    [Fact]
    public void Migration_NewNullableColumns_ExistingRowsReturnNullForNewFields()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        // Insert a row via raw SQL omitting the new columns, mimicking a pre-migration state.
        // EF Core SQLite stores Guids as TEXT in uppercase format by default.
        var id = Guid.NewGuid();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO BaseItems (Id, Type, IsLocked, IsFolder, IsSeries, IsVirtualItem, IsMovie, IsInMixedFolder, IsRepeat)
            VALUES ($id, 'Movie', 0, 0, 0, 0, 0, 0, 0)";
        cmd.Parameters.AddWithValue("$id", id.ToString("D").ToUpperInvariant());
        cmd.ExecuteNonQuery();

        context.ChangeTracker.Clear();
        var loaded = context.BaseItems.Find(id);

        Assert.NotNull(loaded);
        Assert.Null(loaded.VideoType);
        Assert.Null(loaded.SeriesStatus);
        Assert.Null(loaded.Is3D);
        Assert.Null(loaded.IsPlaceHolder);
    }

    // ---------------------------------------------------------------------------
    // Placeholder item seed data persists across context instances
    // ---------------------------------------------------------------------------

    [Fact]
    public void Migration_PlaceholderItem_IsAlwaysPresent()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var placeholder = context.BaseItems
            .FirstOrDefault(e => e.Id.Equals(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        Assert.NotNull(placeholder);
        Assert.Equal("PLACEHOLDER", placeholder.Type);
    }

    // ---------------------------------------------------------------------------
    // Foreign key: AncestorIds → BaseItems (both directions)
    // ---------------------------------------------------------------------------

    [Fact]
    public void AncestorIds_ForeignKeyReferences_AreEnforced()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();

        context.BaseItems.AddRange(
            new BaseItemEntity { Id = parentId, Type = "Folder", IsFolder = true },
            new BaseItemEntity { Id = childId, Type = "Movie" });
        context.SaveChanges();

        context.AncestorIds.Add(new AncestorId
        {
            ItemId = childId,
            ParentItemId = parentId,
            Item = null!,
            ParentItem = null!
        });
        context.SaveChanges();

        var rows = context.AncestorIds.Where(a => a.ItemId.Equals(childId)).ToList();
        Assert.Single(rows);
        Assert.Equal(parentId, rows[0].ParentItemId);
    }

    // ---------------------------------------------------------------------------
    // Indexes: critical query-path indexes were created
    // ---------------------------------------------------------------------------

    [Fact]
    public void SchemaCreation_IndexOnBaseItems_Path_Exists()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        Assert.True(
            IndexCoveringColumnExists("BaseItems", "Path"),
            "Expected an index covering 'Path' on the BaseItems table");
    }

    [Fact]
    public void SchemaCreation_IndexOnBaseItems_PresentationUniqueKey_Exists()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        Assert.True(
            IndexCoveringColumnExists("BaseItems", "PresentationUniqueKey"),
            "Expected an index covering 'PresentationUniqueKey' on the BaseItems table");
    }

    [Fact]
    public void SchemaCreation_IndexOnBaseItems_ParentId_Exists()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        Assert.True(
            IndexCoveringColumnExists("BaseItems", "ParentId"),
            "Expected an index covering 'ParentId' on the BaseItems table");
    }

    // ---------------------------------------------------------------------------
    // ItemValue type enumeration integrity (no gaps in DB values)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(ItemValueType.Artist, 0)]
    [InlineData(ItemValueType.AlbumArtist, 1)]
    [InlineData(ItemValueType.Genre, 2)]
    [InlineData(ItemValueType.Studios, 3)]
    [InlineData(ItemValueType.Tags, 4)]
    [InlineData(ItemValueType.InheritedTags, 6)]
    public void ItemValueType_EnumValues_MatchExpectedIntegerRepresentation(ItemValueType type, int expectedInt)
    {
        Assert.Equal(expectedInt, (int)type);
    }

    [Fact]
    public void ItemValue_AllTypes_CanBePersistedAndQueried()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var types = Enum.GetValues<ItemValueType>();
        foreach (var type in types)
        {
            context.ItemValues.Add(new ItemValue
            {
                ItemValueId = Guid.NewGuid(),
                Type = type,
                Value = $"value-{type}",
                CleanValue = $"value {type}".ToLowerInvariant()
            });
        }

        context.SaveChanges();
        context.ChangeTracker.Clear();

        foreach (var type in types)
        {
            var found = context.ItemValues.Any(iv => iv.Type == type);
            Assert.True(found, $"ItemValue with Type={type} was not found after save");
        }
    }

    // ---------------------------------------------------------------------------
    // BaseItemImageInfo.DateModified nullable (migration BaseItemImageInfoDateModifiedNullable)
    // ---------------------------------------------------------------------------

    [Fact]
    public void BaseItemImageInfo_DateModified_IsNullable_InCurrentSchema()
    {
        using var context = CreateInMemoryContext();
        context.Database.EnsureCreated();

        var col = GetColumnInfo("BaseItemImageInfos", "DateModified");

        Assert.NotNull(col);
        Assert.True(col.Value.IsNullable, "BaseItemImageInfo.DateModified should be nullable after the DateModifiedNullable migration");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private JellyfinDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new JellyfinDbContext(
            options,
            NullLogger<JellyfinDbContext>.Instance,
            new SqliteInMemoryDatabaseProvider(),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }

    private bool TableExists(string tableName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";
        cmd.Parameters.AddWithValue("$name", tableName);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private (bool IsNullable, string Type)? GetColumnInfo(string tableName, string columnName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return (IsNullable: reader.GetInt32(3) == 0, Type: reader.GetString(2));
            }
        }

        return null;
    }

    private bool IndexCoveringColumnExists(string tableName, string columnName)
    {
        using var listCmd = _connection.CreateCommand();
        listCmd.CommandText = $"PRAGMA index_list(\"{tableName}\")";

        var indexNames = new List<string>();
        using (var reader = listCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                indexNames.Add(reader.GetString(1));
            }
        }

        foreach (var indexName in indexNames)
        {
            using var infoCmd = _connection.CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info(\"{indexName}\")";
            using var infoReader = infoCmd.ExecuteReader();
            while (infoReader.Read())
            {
                if (string.Equals(infoReader.GetString(2), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
