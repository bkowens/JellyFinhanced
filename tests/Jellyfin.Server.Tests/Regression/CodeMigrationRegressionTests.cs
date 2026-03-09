using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Server.Migrations;
using Jellyfin.Server.Migrations.Stages;
using Jellyfin.Server.ServerSetupApp;
using Moq;
using Xunit;

namespace Jellyfin.Server.Tests.Regression;

/// <summary>
/// Regression tests for <see cref="CodeMigration"/> to verify migration ID generation,
/// migration routing, and error handling remain stable across code changes.
/// </summary>
[Trait("Category", "Regression")]
public class CodeMigrationRegressionTests
{
    [Fact]
    public void BuildCodeMigrationId_FormatsOrderDateCorrectly()
    {
        const string dateString = "2025-03-15T10:30:45";
        const string name = "TestMigration";
        var order = DateTime.Parse(dateString, CultureInfo.InvariantCulture);
        var expectedPrefix = order.ToString("yyyyMMddHHmmsss", CultureInfo.InvariantCulture);

        var attribute = new JellyfinMigrationAttribute(dateString, name);
        var migration = new CodeMigration(typeof(StubSyncMigration), attribute, null);

        var id = migration.BuildCodeMigrationId();

        Assert.StartsWith(expectedPrefix, id, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCodeMigrationId_ContainsUnderscoreSeparator()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "SomeMigration");
        var migration = new CodeMigration(typeof(StubSyncMigration), attribute, null);

        var id = migration.BuildCodeMigrationId();

        Assert.Contains("_", id, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCodeMigrationId_EndsWithMigrationName()
    {
        const string name = "MySpecialMigration";
        var attribute = new JellyfinMigrationAttribute("2025-06-01T00:00:00", name);
        var migration = new CodeMigration(typeof(StubSyncMigration), attribute, null);

        var id = migration.BuildCodeMigrationId();

        Assert.EndsWith("_" + name, id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Perform_SyncMigration_WithNullServiceProvider_Executes()
    {
        StubSyncMigration.Reset();
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "StubSync");
        var migration = new CodeMigration(typeof(StubSyncMigration), attribute, null);
        var logger = Mock.Of<IStartupLogger>();

        await migration.Perform(null, logger, CancellationToken.None);

        Assert.True(StubSyncMigration.WasPerformed);
        StubSyncMigration.Reset();
    }

    [Fact]
    public async Task Perform_AsyncMigration_WithNullServiceProvider_Executes()
    {
        StubAsyncMigration.Reset();
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "StubAsync");
        var migration = new CodeMigration(typeof(StubAsyncMigration), attribute, null);
        var logger = Mock.Of<IStartupLogger>();

        await migration.Perform(null, logger, CancellationToken.None);

        Assert.True(StubAsyncMigration.WasPerformed);
        StubAsyncMigration.Reset();
    }

    [Fact]
    public async Task Perform_InvalidMigrationType_ThrowsInvalidOperationException()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "Invalid");
        var migration = new CodeMigration(typeof(StubInvalidMigration), attribute, null);
        var logger = Mock.Of<IStartupLogger>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => migration.Perform(null, logger, CancellationToken.None));
    }

    [Fact]
    public void MigrationType_Property_ReturnsConstructorValue()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "Test");
        var migration = new CodeMigration(typeof(StubSyncMigration), attribute, null);

        Assert.Equal(typeof(StubSyncMigration), migration.MigrationType);
    }

    [Fact]
    public void Metadata_Property_ReturnsConstructorValue()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "TestName");
        var migration = new CodeMigration(typeof(StubSyncMigration), attribute, null);

        Assert.Same(attribute, migration.Metadata);
        Assert.Equal("TestName", migration.Metadata.Name);
    }

    [Fact]
    public void BackupRequirements_Property_WhenNull_IsNull()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "Test");
        var migration = new CodeMigration(typeof(StubSyncMigration), attribute, null);

        Assert.Null(migration.BackupRequirements);
    }

    // --------------- Attribute tests ---------------

    [Fact]
    public void JellyfinMigrationAttribute_Order_ParsesDateCorrectly()
    {
        const string dateString = "2025-03-15T12:00:00";
        var attribute = new JellyfinMigrationAttribute(dateString, "Test");

        var expected = DateTime.Parse(dateString, CultureInfo.InvariantCulture);
        Assert.Equal(expected, attribute.Order);
    }

    [Fact]
    public void JellyfinMigrationAttribute_DefaultStage_IsAppInitialisation()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "Test");

        Assert.Equal(JellyfinMigrationStageTypes.AppInitialisation, attribute.Stage);
    }

    [Fact]
    public void JellyfinMigrationAttribute_Name_MatchesConstructorArgument()
    {
        const string name = "MyMigration";
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", name);

        Assert.Equal(name, attribute.Name);
    }

    [Fact]
    public void JellyfinMigrationAttribute_RunMigrationOnSetup_DefaultsFalse()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "Test");

        Assert.False(attribute.RunMigrationOnSetup);
    }

    [Fact]
    public void JellyfinMigrationAttribute_KeyWithoutLegacyConstructor_IsNull()
    {
        var attribute = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "Test");

        Assert.Null(attribute.Key);
    }

    [Fact]
    public void JellyfinMigrationAttribute_TwoMigrationsWithDifferentDates_OrderedCorrectly()
    {
        var earlier = new JellyfinMigrationAttribute("2024-01-01T00:00:00", "Earlier");
        var later = new JellyfinMigrationAttribute("2025-01-01T00:00:00", "Later");

        Assert.True(earlier.Order < later.Order);
    }

    // --------------- Stub migration types ---------------

#pragma warning disable CS0618
    [JellyfinMigration("2025-01-01T00:00:00", "StubSync")]
    private sealed class StubSyncMigration : IMigrationRoutine
    {
        public static bool WasPerformed { get; private set; }

        public static void Reset() => WasPerformed = false;

        public void Perform() => WasPerformed = true;
    }

    [JellyfinMigration("2025-01-01T00:00:00", "StubAsync")]
    private sealed class StubAsyncMigration : IAsyncMigrationRoutine
    {
        public static bool WasPerformed { get; private set; }

        public static void Reset() => WasPerformed = false;

        public Task PerformAsync(CancellationToken cancellationToken)
        {
            WasPerformed = true;
            return Task.CompletedTask;
        }
    }
#pragma warning restore CS0618

    // Not implementing either migration interface - validates error path
    private sealed class StubInvalidMigration
    {
    }
}
