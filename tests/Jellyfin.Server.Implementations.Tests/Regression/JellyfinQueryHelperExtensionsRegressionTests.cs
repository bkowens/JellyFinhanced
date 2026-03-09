using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Jellyfin.Database.Implementations;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Regression;

/// <summary>
/// Regression tests for <see cref="JellyfinQueryHelperExtensions"/> to verify that
/// the expression-building helpers for WhereOneOrMany and WhereNoneOf produce correct
/// results in-memory (simulating LINQ-to-objects behavior, which mirrors EF query logic).
/// </summary>
[Trait("Category", "Regression")]
public class JellyfinQueryHelperExtensionsRegressionTests
{
    // ---- OneOrManyExpressionBuilder ----

    [Fact]
    public void OneOrManyExpressionBuilder_EmptyList_AlwaysFalse()
    {
        var items = new List<int>();
        var expr = items.OneOrManyExpressionBuilder<SampleEntity, int>(e => e.Value);
        var compiled = expr.Compile();

        Assert.False(compiled(new SampleEntity { Value = 0 }));
        Assert.False(compiled(new SampleEntity { Value = 42 }));
        Assert.False(compiled(new SampleEntity { Value = -1 }));
    }

    [Fact]
    public void OneOrManyExpressionBuilder_SingleValueType_MatchesExactValue()
    {
        var items = new List<int> { 5 };
        var expr = items.OneOrManyExpressionBuilder<SampleEntity, int>(e => e.Value);
        var compiled = expr.Compile();

        Assert.True(compiled(new SampleEntity { Value = 5 }));
        Assert.False(compiled(new SampleEntity { Value = 4 }));
        Assert.False(compiled(new SampleEntity { Value = 6 }));
    }

    [Fact]
    public void OneOrManyExpressionBuilder_SingleGuid_MatchesExact()
    {
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();
        var items = new List<Guid> { target };
        var expr = items.OneOrManyExpressionBuilder<SampleGuidEntity, Guid>(e => e.Id);
        var compiled = expr.Compile();

        Assert.True(compiled(new SampleGuidEntity { Id = target }));
        Assert.False(compiled(new SampleGuidEntity { Id = other }));
        Assert.False(compiled(new SampleGuidEntity { Id = Guid.Empty }));
    }

    [Fact]
    public void OneOrManyExpressionBuilder_MultipleValueTypes_MatchesAny()
    {
        var items = new List<int> { 1, 2, 3 };
        var expr = items.OneOrManyExpressionBuilder<SampleEntity, int>(e => e.Value);
        var compiled = expr.Compile();

        Assert.True(compiled(new SampleEntity { Value = 1 }));
        Assert.True(compiled(new SampleEntity { Value = 2 }));
        Assert.True(compiled(new SampleEntity { Value = 3 }));
        Assert.False(compiled(new SampleEntity { Value = 4 }));
        Assert.False(compiled(new SampleEntity { Value = 0 }));
    }

    [Fact]
    public void OneOrManyExpressionBuilder_MultipleGuids_MatchesAny()
    {
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var g3 = Guid.NewGuid();
        var other = Guid.NewGuid();

        var items = new List<Guid> { g1, g2, g3 };
        var expr = items.OneOrManyExpressionBuilder<SampleGuidEntity, Guid>(e => e.Id);
        var compiled = expr.Compile();

        Assert.True(compiled(new SampleGuidEntity { Id = g1 }));
        Assert.True(compiled(new SampleGuidEntity { Id = g2 }));
        Assert.True(compiled(new SampleGuidEntity { Id = g3 }));
        Assert.False(compiled(new SampleGuidEntity { Id = other }));
        Assert.False(compiled(new SampleGuidEntity { Id = Guid.Empty }));
    }

    [Fact]
    public void WhereOneOrMany_OnQueryable_FiltersByValueType()
    {
        var data = new List<SampleEntity>
        {
            new() { Value = 1 },
            new() { Value = 2 },
            new() { Value = 3 },
            new() { Value = 4 },
        }.AsQueryable();

        var result = data.WhereOneOrMany(new List<int> { 2, 4 }, e => e.Value).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.True(e.Value == 2 || e.Value == 4));
    }

    [Fact]
    public void WhereOneOrMany_EmptyList_ReturnsNoResults()
    {
        var data = new List<SampleEntity>
        {
            new() { Value = 1 },
            new() { Value = 2 },
        }.AsQueryable();

        var result = data.WhereOneOrMany(new List<int>(), e => e.Value).ToList();

        Assert.Empty(result);
    }

    // ---- NoneOfExpressionBuilder ----

    [Fact]
    public void NoneOfExpressionBuilder_EmptyList_AlwaysTrue()
    {
        var items = new List<int>();
        var expr = items.NoneOfExpressionBuilder<SampleEntity, int>(e => e.Value);
        var compiled = expr.Compile();

        Assert.True(compiled(new SampleEntity { Value = 0 }));
        Assert.True(compiled(new SampleEntity { Value = 42 }));
        Assert.True(compiled(new SampleEntity { Value = -1 }));
    }

    [Fact]
    public void NoneOfExpressionBuilder_SingleValueType_ExcludesMatchedValue()
    {
        var items = new List<int> { 5 };
        var expr = items.NoneOfExpressionBuilder<SampleEntity, int>(e => e.Value);
        var compiled = expr.Compile();

        Assert.False(compiled(new SampleEntity { Value = 5 }));
        Assert.True(compiled(new SampleEntity { Value = 4 }));
        Assert.True(compiled(new SampleEntity { Value = 6 }));
    }

    [Fact]
    public void NoneOfExpressionBuilder_MultipleValueTypes_ExcludesAllMatched()
    {
        var items = new List<int> { 1, 2, 3 };
        var expr = items.NoneOfExpressionBuilder<SampleEntity, int>(e => e.Value);
        var compiled = expr.Compile();

        Assert.False(compiled(new SampleEntity { Value = 1 }));
        Assert.False(compiled(new SampleEntity { Value = 2 }));
        Assert.False(compiled(new SampleEntity { Value = 3 }));
        Assert.True(compiled(new SampleEntity { Value = 4 }));
        Assert.True(compiled(new SampleEntity { Value = 0 }));
    }

    [Fact]
    public void WhereNoneOf_OnQueryable_ExcludesMatchedValues()
    {
        var data = new List<SampleEntity>
        {
            new() { Value = 1 },
            new() { Value = 2 },
            new() { Value = 3 },
            new() { Value = 4 },
        }.AsQueryable();

        var result = data.WhereNoneOf(new List<int> { 2, 4 }, e => e.Value).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.True(e.Value == 1 || e.Value == 3));
    }

    [Fact]
    public void WhereNoneOf_EmptyList_ReturnsAllResults()
    {
        var data = new List<SampleEntity>
        {
            new() { Value = 1 },
            new() { Value = 2 },
        }.AsQueryable();

        var result = data.WhereNoneOf(new List<int>(), e => e.Value).ToList();

        Assert.Equal(2, result.Count);
    }

    // ---- Symmetry / Complementarity between WhereOneOrMany and WhereNoneOf ----

    [Fact]
    public void WhereOneOrMany_And_WhereNoneOf_AreComplementary()
    {
        var data = new List<SampleEntity>
        {
            new() { Value = 1 },
            new() { Value = 2 },
            new() { Value = 3 },
            new() { Value = 4 },
            new() { Value = 5 },
        }.AsQueryable();

        var filter = new List<int> { 2, 4 };

        var matched = data.WhereOneOrMany(filter, e => e.Value).ToList();
        var excluded = data.WhereNoneOf(filter, e => e.Value).ToList();

        Assert.Equal(data.Count(), matched.Count + excluded.Count);
        Assert.Empty(matched.Intersect(excluded));
    }

    [Fact]
    public void OneOrManyExpressionBuilder_TwoItemList_DoesNotUseContainsForSingleItem()
    {
        // When exactly 1 element, the expression should use Equality, not Contains.
        // We verify this by checking that the lambda body is a BinaryExpression (== for value types).
        var singleItem = new List<int> { 42 };
        var expr = singleItem.OneOrManyExpressionBuilder<SampleEntity, int>(e => e.Value);

        // For single value-type items the body should be a BinaryExpression (Equal)
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        Assert.Equal(ExpressionType.Equal, binary.NodeType);
    }

    [Fact]
    public void NoneOfExpressionBuilder_SingleItem_UsesBinaryNotEqual()
    {
        var singleItem = new List<int> { 42 };
        var expr = singleItem.NoneOfExpressionBuilder<SampleEntity, int>(e => e.Value);

        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        Assert.Equal(ExpressionType.NotEqual, binary.NodeType);
    }

    // ---- Helper entity types ----

    private sealed class SampleEntity
    {
        public int Value { get; set; }
    }

    private sealed class SampleGuidEntity
    {
        public Guid Id { get; set; }
    }
}
