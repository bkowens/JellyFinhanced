using System;
using System.Text.Json;
using Jellyfin.Extensions.Json;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.Regression;

/// <summary>
/// Regression tests for <see cref="ServerConfiguration"/> to ensure serialization
/// behavior and default values remain stable across changes.
/// </summary>
[Trait("Category", "Regression")]
public class ServerConfigurationRegressionTests
{
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Options;

    [Fact]
    public void ServerConfiguration_DefaultConstructor_HasExpectedMetadataOptions()
    {
        var config = new ServerConfiguration();

        Assert.NotNull(config.MetadataOptions);
        Assert.Equal(9, config.MetadataOptions.Length);

        var itemTypes = new[]
        {
            "Book", "Movie", "MusicVideo", "Series", "MusicAlbum",
            "MusicArtist", "BoxSet", "Season", "Episode"
        };

        for (var i = 0; i < itemTypes.Length; i++)
        {
            Assert.Equal(itemTypes[i], config.MetadataOptions[i].ItemType);
        }
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_MusicVideoHasDisabledFetchers()
    {
        var config = new ServerConfiguration();

        var musicVideoOptions = Array.Find(config.MetadataOptions, o => o.ItemType == "MusicVideo");
        Assert.NotNull(musicVideoOptions);
        Assert.Contains("The Open Movie Database", musicVideoOptions.DisabledMetadataFetchers);
        Assert.Contains("The Open Movie Database", musicVideoOptions.DisabledImageFetchers);
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_MusicAlbumHasDisabledFetchers()
    {
        var config = new ServerConfiguration();

        var albumOptions = Array.Find(config.MetadataOptions, o => o.ItemType == "MusicAlbum");
        Assert.NotNull(albumOptions);
        Assert.Contains("TheAudioDB", albumOptions.DisabledMetadataFetchers);
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_MusicArtistHasDisabledFetchers()
    {
        var config = new ServerConfiguration();

        var artistOptions = Array.Find(config.MetadataOptions, o => o.ItemType == "MusicArtist");
        Assert.NotNull(artistOptions);
        Assert.Contains("TheAudioDB", artistOptions.DisabledMetadataFetchers);
    }

    [Theory]
    [InlineData(nameof(ServerConfiguration.EnableNormalizedItemByNameIds), true)]
    [InlineData(nameof(ServerConfiguration.EnableCaseSensitiveItemIds), true)]
    [InlineData(nameof(ServerConfiguration.DisableLiveTvChannelUserDataName), true)]
    [InlineData(nameof(ServerConfiguration.QuickConnectAvailable), true)]
    [InlineData(nameof(ServerConfiguration.EnableMetrics), false)]
    [InlineData(nameof(ServerConfiguration.IsPortAuthorized), false)]
    [InlineData(nameof(ServerConfiguration.SkipDeserializationForBasicTypes), true)]
    [InlineData(nameof(ServerConfiguration.SaveMetadataHidden), false)]
    [InlineData(nameof(ServerConfiguration.EnableFolderView), false)]
    [InlineData(nameof(ServerConfiguration.EnableGroupingMoviesIntoCollections), false)]
    [InlineData(nameof(ServerConfiguration.EnableGroupingShowsIntoCollections), false)]
    [InlineData(nameof(ServerConfiguration.DisplaySpecialsWithinSeasons), true)]
    [InlineData(nameof(ServerConfiguration.EnableExternalContentInSuggestions), true)]
    [InlineData(nameof(ServerConfiguration.EnableSlowResponseWarning), true)]
    [InlineData(nameof(ServerConfiguration.AllowClientLogUpload), true)]
    [InlineData(nameof(ServerConfiguration.EnableLegacyAuthorization), false)]
    public void ServerConfiguration_DefaultConstructor_BoolPropertyHasExpectedDefault(string propertyName, bool expectedDefault)
    {
        var config = new ServerConfiguration();
        var property = typeof(ServerConfiguration).GetProperty(propertyName);
        Assert.NotNull(property);
        var value = (bool)property.GetValue(config)!;
        Assert.Equal(expectedDefault, value);
    }

    [Theory]
    [InlineData(nameof(ServerConfiguration.MinResumePct), 5)]
    [InlineData(nameof(ServerConfiguration.MaxResumePct), 90)]
    [InlineData(nameof(ServerConfiguration.MinResumeDurationSeconds), 300)]
    [InlineData(nameof(ServerConfiguration.MinAudiobookResume), 5)]
    [InlineData(nameof(ServerConfiguration.MaxAudiobookResume), 5)]
    [InlineData(nameof(ServerConfiguration.LibraryMonitorDelay), 60)]
    [InlineData(nameof(ServerConfiguration.LibraryUpdateDuration), 30)]
    [InlineData(nameof(ServerConfiguration.InactiveSessionThreshold), 0)]
    [InlineData(nameof(ServerConfiguration.DummyChapterDuration), 0)]
    [InlineData(nameof(ServerConfiguration.ImageExtractionTimeoutMs), 0)]
    [InlineData(nameof(ServerConfiguration.ParallelImageEncodingLimit), 0)]
    [InlineData(nameof(ServerConfiguration.RemoteClientBitrateLimit), 0)]
    [InlineData(nameof(ServerConfiguration.LibraryScanFanoutConcurrency), 0)]
    [InlineData(nameof(ServerConfiguration.LibraryMetadataRefreshConcurrency), 0)]
    public void ServerConfiguration_DefaultConstructor_IntPropertyHasExpectedDefault(string propertyName, int expectedDefault)
    {
        var config = new ServerConfiguration();
        var property = typeof(ServerConfiguration).GetProperty(propertyName);
        Assert.NotNull(property);
        var value = (int)property.GetValue(config)!;
        Assert.Equal(expectedDefault, value);
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_SlowResponseThresholdIs500()
    {
        var config = new ServerConfiguration();
        Assert.Equal(500L, config.SlowResponseThresholdMs);
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_ActivityLogRetentionDaysIs30()
    {
        var config = new ServerConfiguration();
        Assert.Equal(30, config.ActivityLogRetentionDays);
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_StringDefaults()
    {
        var config = new ServerConfiguration();

        Assert.Equal("en", config.PreferredMetadataLanguage);
        Assert.Equal("US", config.MetadataCountryCode);
        Assert.Equal("en-US", config.UICulture);
        Assert.Equal(string.Empty, config.ServerName);
        Assert.Equal(string.Empty, config.MetadataPath);
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_SortCharacters()
    {
        var config = new ServerConfiguration();

        Assert.Equal(new[] { ".", "+", "%" }, config.SortReplaceCharacters);
        Assert.Equal(new[] { ",", "&", "-", "{", "}", "'" }, config.SortRemoveCharacters);
        Assert.Equal(new[] { "the", "a", "an" }, config.SortRemoveWords);
    }

    [Fact]
    public void ServerConfiguration_DefaultConstructor_CorsHostsContainsWildcard()
    {
        var config = new ServerConfiguration();
        Assert.Single(config.CorsHosts);
        Assert.Equal("*", config.CorsHosts[0]);
    }

    [Fact]
    public void ServerConfiguration_RoundTrip_SerializeDeserialize_PreservesAllDefaults()
    {
        var original = new ServerConfiguration();
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ServerConfiguration>(json, _jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.PreferredMetadataLanguage, deserialized.PreferredMetadataLanguage);
        Assert.Equal(original.MetadataCountryCode, deserialized.MetadataCountryCode);
        Assert.Equal(original.EnableNormalizedItemByNameIds, deserialized.EnableNormalizedItemByNameIds);
        Assert.Equal(original.EnableCaseSensitiveItemIds, deserialized.EnableCaseSensitiveItemIds);
        Assert.Equal(original.DisableLiveTvChannelUserDataName, deserialized.DisableLiveTvChannelUserDataName);
        Assert.Equal(original.MinResumePct, deserialized.MinResumePct);
        Assert.Equal(original.MaxResumePct, deserialized.MaxResumePct);
        Assert.Equal(original.MinResumeDurationSeconds, deserialized.MinResumeDurationSeconds);
        Assert.Equal(original.LibraryMonitorDelay, deserialized.LibraryMonitorDelay);
        Assert.Equal(original.SlowResponseThresholdMs, deserialized.SlowResponseThresholdMs);
        Assert.Equal(original.ActivityLogRetentionDays, deserialized.ActivityLogRetentionDays);
        Assert.Equal(original.EnableLegacyAuthorization, deserialized.EnableLegacyAuthorization);
        Assert.Equal(original.QuickConnectAvailable, deserialized.QuickConnectAvailable);
    }

    [Fact]
    public void ServerConfiguration_RoundTrip_SerializeDeserialize_PreservesMetadataOptions()
    {
        var original = new ServerConfiguration();
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ServerConfiguration>(json, _jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.MetadataOptions.Length, deserialized.MetadataOptions.Length);

        for (var i = 0; i < original.MetadataOptions.Length; i++)
        {
            Assert.Equal(original.MetadataOptions[i].ItemType, deserialized.MetadataOptions[i].ItemType);
        }
    }

    [Fact]
    public void ServerConfiguration_RoundTrip_SerializeDeserialize_PreservesSortSettings()
    {
        var original = new ServerConfiguration();
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<ServerConfiguration>(json, _jsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.SortReplaceCharacters, deserialized.SortReplaceCharacters);
        Assert.Equal(original.SortRemoveCharacters, deserialized.SortRemoveCharacters);
        Assert.Equal(original.SortRemoveWords, deserialized.SortRemoveWords);
    }

    [Fact]
    public void ServerConfiguration_CacheSize_AtLeast10000()
    {
        var config = new ServerConfiguration();
        Assert.True(config.CacheSize >= 10000, $"CacheSize {config.CacheSize} should be at least 10000");
    }

    [Fact]
    public void ServerConfiguration_EmptyArrayDefaults_NotNull()
    {
        var config = new ServerConfiguration();

        Assert.NotNull(config.ContentTypes);
        Assert.NotNull(config.CodecsUsed);
        Assert.NotNull(config.PluginRepositories);
        Assert.NotNull(config.PathSubstitutions);
        Assert.NotNull(config.CastReceiverApplications);

        Assert.Empty(config.ContentTypes);
        Assert.Empty(config.CodecsUsed);
        Assert.Empty(config.PluginRepositories);
        Assert.Empty(config.PathSubstitutions);
        Assert.Empty(config.CastReceiverApplications);
    }

    [Fact]
    public void ServerConfiguration_TrickplayOptions_NotNull()
    {
        var config = new ServerConfiguration();
        Assert.NotNull(config.TrickplayOptions);
    }
}
