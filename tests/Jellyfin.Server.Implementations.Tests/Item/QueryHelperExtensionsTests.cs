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
/// Tests for <see cref="JellyfinQueryHelperExtensions"/> covering expression builder
/// correctness and edge cases for both WhereOneOrMany and WhereNoneOf.
/// All tests run against an in-memory SQLite database so the expressions are actually
/// compiled into SQL and round-tripped through the EF Core query pipeline.
/// </summary>
public sealed class QueryHelperExtensionsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly JellyfinDbContext _context;

    public QueryHelperExtensionsTests()
    {
        // Use a persistent in-memory connection so the schema survives across queries.
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
        SeedBaseItems();
    }

    // ---------------------------------------------------------------------------
    // WhereOneOrMany – value-type property (Guid)
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhereOneOrMany_EmptyList_ReturnsNoResults()
    {
        var emptyIds = new List<Guid>();

        var results = _context.BaseItems
            .WhereOneOrMany(emptyIds, e => e.Id)
            .ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void WhereOneOrMany_SingleGuid_ReturnsSingleMatch()
    {
        var target = _context.BaseItems.First(e => e.Type == "Movie");

        var results = _context.BaseItems
            .WhereOneOrMany(new List<Guid> { target.Id }, e => e.Id)
            .ToList();

        Assert.Single(results);
        Assert.Equal(target.Id, results[0].Id);
    }

    [Fact]
    public void WhereOneOrMany_MultipleGuids_ReturnsAllMatches()
    {
        var movies = _context.BaseItems.Where(e => e.Type == "Movie").Take(2).ToList();
        var ids = movies.Select(m => m.Id).ToList();

        var results = _context.BaseItems
            .WhereOneOrMany(ids, e => e.Id)
            .ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains(r.Id, ids));
    }

    [Fact]
    public void WhereOneOrMany_IdsNotInDatabase_ReturnsEmpty()
    {
        var missingIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var results = _context.BaseItems
            .WhereOneOrMany(missingIds, e => e.Id)
            .ToList();

        Assert.Empty(results);
    }

    // ---------------------------------------------------------------------------
    // WhereOneOrMany – reference-type property (string)
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhereOneOrMany_SingleString_ReturnsSingleMatch()
    {
        var results = _context.BaseItems
            .WhereOneOrMany(new List<string> { "Movie" }, e => e.Type)
            .ToList();

        Assert.All(results, r => Assert.Equal("Movie", r.Type));
    }

    [Fact]
    public void WhereOneOrMany_MultipleStrings_ReturnsAllMatches()
    {
        var types = new List<string> { "Movie", "Series" };

        var results = _context.BaseItems
            .WhereOneOrMany(types, e => e.Type)
            .ToList();

        Assert.All(results, r => Assert.Contains(r.Type, types));
        Assert.Contains(results, r => r.Type == "Movie");
        Assert.Contains(results, r => r.Type == "Series");
    }

    // ---------------------------------------------------------------------------
    // WhereNoneOf
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhereNoneOf_EmptyList_ReturnsAllResults()
    {
        var totalCount = _context.BaseItems.Count();

        var results = _context.BaseItems
            .WhereNoneOf(new List<string>(), e => e.Type)
            .ToList();

        Assert.Equal(totalCount, results.Count);
    }

    [Fact]
    public void WhereNoneOf_SingleValue_ExcludesMatchingRows()
    {
        var movieCount = _context.BaseItems.Count(e => e.Type == "Movie");
        var totalCount = _context.BaseItems.Count();

        var results = _context.BaseItems
            .WhereNoneOf(new List<string> { "Movie" }, e => e.Type)
            .ToList();

        Assert.Equal(totalCount - movieCount, results.Count);
        Assert.DoesNotContain(results, r => r.Type == "Movie");
    }

    [Fact]
    public void WhereNoneOf_MultipleValues_ExcludesAllMatchingRows()
    {
        var excludedTypes = new List<string> { "Movie", "Series" };
        var excludedCount = _context.BaseItems.Count(e => excludedTypes.Contains(e.Type));
        var totalCount = _context.BaseItems.Count();

        var results = _context.BaseItems
            .WhereNoneOf(excludedTypes, e => e.Type)
            .ToList();

        Assert.Equal(totalCount - excludedCount, results.Count);
        Assert.DoesNotContain(results, r => excludedTypes.Contains(r.Type));
    }

    [Fact]
    public void WhereNoneOf_ValueTypeProperty_ExcludesMatchingRows()
    {
        // Seed an item with a specific ParentIndexNumber
        var parentIdx = 42;
        var seasonItemId = Guid.NewGuid();
        _context.BaseItems.Add(new BaseItemEntity { Id = seasonItemId, Type = "Episode", ParentIndexNumber = parentIdx });
        _context.SaveChanges();

        var results = _context.BaseItems
            .WhereNoneOf(new List<int?> { parentIdx }, e => e.ParentIndexNumber)
            .ToList();

        Assert.DoesNotContain(results, r => r.ParentIndexNumber == parentIdx);
    }

    // ---------------------------------------------------------------------------
    // OneOrManyExpressionBuilder – standalone expression building
    // ---------------------------------------------------------------------------

    [Fact]
    public void OneOrManyExpressionBuilder_EmptyList_AlwaysFalseExpression()
    {
        var expr = new List<Guid>()
            .OneOrManyExpressionBuilder<BaseItemEntity, Guid>(e => e.Id);

        var compiled = expr.Compile();
        var entity = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie" };

        Assert.False(compiled(entity));
    }

    [Fact]
    public void OneOrManyExpressionBuilder_SingleValue_MatchesCorrectly()
    {
        var targetId = Guid.NewGuid();
        var expr = new List<Guid> { targetId }
            .OneOrManyExpressionBuilder<BaseItemEntity, Guid>(e => e.Id);

        var compiled = expr.Compile();
        var matching = new BaseItemEntity { Id = targetId, Type = "Movie" };
        var notMatching = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie" };

        Assert.True(compiled(matching));
        Assert.False(compiled(notMatching));
    }

    [Fact]
    public void OneOrManyExpressionBuilder_MultipleValues_MatchesAny()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var ids = new List<Guid> { id1, id2 };
        var expr = ids.OneOrManyExpressionBuilder<BaseItemEntity, Guid>(e => e.Id);

        var compiled = expr.Compile();
        var match1 = new BaseItemEntity { Id = id1, Type = "Movie" };
        var match2 = new BaseItemEntity { Id = id2, Type = "Movie" };
        var noMatch = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie" };

        Assert.True(compiled(match1));
        Assert.True(compiled(match2));
        Assert.False(compiled(noMatch));
    }

    // ---------------------------------------------------------------------------
    // NoneOfExpressionBuilder – standalone
    // ---------------------------------------------------------------------------

    [Fact]
    public void NoneOfExpressionBuilder_EmptyList_AlwaysTrueExpression()
    {
        var expr = new List<string>()
            .NoneOfExpressionBuilder<BaseItemEntity, string>(e => e.Type);

        var compiled = expr.Compile();
        var entity = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie" };

        Assert.True(compiled(entity));
    }

    [Fact]
    public void NoneOfExpressionBuilder_SingleValue_ExcludesMatchingEntity()
    {
        var expr = new List<string> { "Movie" }
            .NoneOfExpressionBuilder<BaseItemEntity, string>(e => e.Type);

        var compiled = expr.Compile();
        var movie = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie" };
        var series = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Series" };

        Assert.False(compiled(movie));
        Assert.True(compiled(series));
    }

    [Fact]
    public void NoneOfExpressionBuilder_MultipleValues_ExcludesAllMatchingEntities()
    {
        var excluded = new List<string> { "Movie", "Series" };
        var expr = excluded.NoneOfExpressionBuilder<BaseItemEntity, string>(e => e.Type);

        var compiled = expr.Compile();
        var movie = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie" };
        var series = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Series" };
        var episode = new BaseItemEntity { Id = Guid.NewGuid(), Type = "Episode" };

        Assert.False(compiled(movie));
        Assert.False(compiled(series));
        Assert.True(compiled(episode));
    }

    // ---------------------------------------------------------------------------
    // WhereOneOrMany / WhereNoneOf symmetry
    // ---------------------------------------------------------------------------

    [Fact]
    public void WhereOneOrMany_AndWhereNoneOf_AreComplementary()
    {
        var types = new List<string> { "Movie", "Series" };

        var included = _context.BaseItems
            .WhereOneOrMany(types, e => e.Type)
            .Select(e => e.Id)
            .ToHashSet();

        var excluded = _context.BaseItems
            .WhereNoneOf(types, e => e.Type)
            .Select(e => e.Id)
            .ToHashSet();

        Assert.Empty(included.Intersect(excluded));
        Assert.Equal(_context.BaseItems.Count(), included.Count + excluded.Count);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private void SeedBaseItems()
    {
        _context.BaseItems.AddRange(
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Alpha", SortName = "alpha" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Movie", Name = "Beta", SortName = "beta" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Series", Name = "Gamma Series", SortName = "gamma series" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "Episode", Name = "S01E01", SortName = "s01e01" },
            new BaseItemEntity { Id = Guid.NewGuid(), Type = "MusicAlbum", Name = "Album One", SortName = "album one" });
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
    }
}
