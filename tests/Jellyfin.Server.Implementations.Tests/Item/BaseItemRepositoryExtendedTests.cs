using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.Item;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Extended unit tests for <see cref="BaseItemRepository"/> static utility methods.
/// These tests focus on the pure/static methods that do not require database access.
/// </summary>
public class BaseItemRepositoryExtendedTests
{
    // -----------------------------------------------------------------------
    // DeserializeBaseItem – unknown type
    // -----------------------------------------------------------------------

    [Fact]
    public void DeserializeBaseItem_WithNullType_ThrowsArgumentNullException()
    {
        var entity = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = null!
        };

        Assert.Throws<ArgumentNullException>(
            () => BaseItemRepository.DeserializeBaseItem(entity, NullLogger.Instance, null, false));
    }

    [Fact]
    public void DeserializeBaseItem_WithEmptyType_ThrowsArgumentException()
    {
        var entity = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = string.Empty
        };

        Assert.Throws<ArgumentException>(
            () => BaseItemRepository.DeserializeBaseItem(entity, NullLogger.Instance, null, false));
    }

    [Theory]
    [InlineData("MediaBrowser.Controller.Entities.Movies.Movie")]
    [InlineData("MediaBrowser.Controller.Entities.TV.Series")]
    [InlineData("MediaBrowser.Controller.Entities.TV.Episode")]
    [InlineData("MediaBrowser.Controller.Entities.TV.Season")]
    [InlineData("MediaBrowser.Controller.Entities.Audio.MusicAlbum")]
    public void DeserializeBaseItem_WithKnownCoreType_ReturnsNonNullItem(string typeName)
    {
        var entity = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = typeName
        };

        var result = BaseItemRepository.DeserializeBaseItem(entity, NullLogger.Instance, null, false);

        Assert.NotNull(result);
    }

    [Fact]
    public void DeserializeBaseItem_WithKnownType_PreservesEntityId()
    {
        var expectedId = Guid.NewGuid();
        var entity = new BaseItemEntity
        {
            Id = expectedId,
            Type = "MediaBrowser.Controller.Entities.Movies.Movie"
        };

        var result = BaseItemRepository.DeserializeBaseItem(entity, NullLogger.Instance, null, false);

        Assert.NotNull(result);
        Assert.Equal(expectedId, result!.Id);
    }

    [Fact]
    public void DeserializeBaseItem_WithSkipDeserialization_ReturnsItem()
    {
        var entity = new BaseItemEntity
        {
            Id = Guid.NewGuid(),
            Type = "MediaBrowser.Controller.Entities.Movies.Movie",
            Data = "{\"invalid_json_that_would_fail_deserialization\":"
        };

        // When skipDeserialization=true the JSON data should be ignored,
        // so we should still get a valid object back.
        var result = BaseItemRepository.DeserializeBaseItem(entity, NullLogger.Instance, null, skipDeserialization: true);

        Assert.NotNull(result);
    }

    // -----------------------------------------------------------------------
    // GetCleanValue – string normalisation logic
    // -----------------------------------------------------------------------

    [Fact]
    public void GetCleanValue_WithNullOrWhiteSpace_ReturnsInputUnchanged()
    {
        Assert.Equal(string.Empty, BaseItemRepository.GetCleanValue(string.Empty));
        Assert.Equal("   ", BaseItemRepository.GetCleanValue("   "));
    }

    [Theory]
    [InlineData("Hello World", "hello world")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("MixedCase", "mixedcase")]
    public void GetCleanValue_NormalisesCase_ToLowerInvariant(string input, string expected)
    {
        var result = BaseItemRepository.GetCleanValue(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("hello, world!", "hello world")]
    [InlineData("star.wars", "star wars")]
    [InlineData("foo-bar_baz", "foo bar baz")]
    [InlineData("100%", "100")]
    public void GetCleanValue_ReplacesPunctuationWithSpaces(string input, string expected)
    {
        var result = BaseItemRepository.GetCleanValue(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("hello   world", "hello world")]
    [InlineData("foo  ,  bar", "foo bar")]
    public void GetCleanValue_CollapsesMultipleSpacesToOne(string input, string expected)
    {
        var result = BaseItemRepository.GetCleanValue(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetCleanValue_TrimsLeadingAndTrailingWhitespace()
    {
        var result = BaseItemRepository.GetCleanValue("  hello world  ");

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void GetCleanValue_WithDiacritics_RemovesDiacritics()
    {
        // "caf\u00e9" is "café"; diacritics should be removed.
        var result = BaseItemRepository.GetCleanValue("caf\u00e9");

        Assert.Equal("cafe", result);
    }

    [Fact]
    public void GetCleanValue_WithDigits_PreservesDigits()
    {
        var result = BaseItemRepository.GetCleanValue("Movie 2024");

        Assert.Equal("movie 2024", result);
    }

    // -----------------------------------------------------------------------
    // PlaceholderId – constant value contract
    // -----------------------------------------------------------------------

    [Fact]
    public void PlaceholderId_HasExpectedValue()
    {
        Assert.Equal(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            BaseItemRepository.PlaceholderId);
    }

    [Fact]
    public void PlaceholderId_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, BaseItemRepository.PlaceholderId);
    }
}
