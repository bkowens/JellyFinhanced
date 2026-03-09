using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.DbConfiguration;
using Jellyfin.Database.Providers.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Server.Implementations.Tests.Item;

/// <summary>
/// Minimal <see cref="IJellyfinDatabaseProvider"/> implementation for in-memory SQLite
/// testing. It applies the same model conventions that the real SQLite provider applies
/// without requiring real file paths or connection interceptors.
/// </summary>
internal sealed class SqliteInMemoryDatabaseProvider : IJellyfinDatabaseProvider
{
    /// <inheritdoc />
    public IDbContextFactory<JellyfinDbContext>? DbContextFactory { get; set; }

    /// <inheritdoc />
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration)
    {
        // Not called during unit tests; EnsureCreated handles schema creation.
    }

    /// <inheritdoc />
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mirror the real SQLite provider: set all DateTime properties to UTC.
        modelBuilder.SetDefaultDateTimeKind(System.DateTimeKind.Utc);
    }

    /// <inheritdoc />
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // No additional conventions needed for in-memory SQLite tests.
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task RunScheduledOptimisation(System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.CompletedTask;

    /// <inheritdoc />
    public System.Threading.Tasks.Task RunShutdownTask(System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.CompletedTask;

    /// <inheritdoc />
    public System.Threading.Tasks.Task<string> MigrationBackupFast(System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(string.Empty);

    /// <inheritdoc />
    public System.Threading.Tasks.Task RestoreBackupFast(string key, System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.CompletedTask;

    /// <inheritdoc />
    public System.Threading.Tasks.Task DeleteBackup(string key)
        => System.Threading.Tasks.Task.CompletedTask;

    /// <inheritdoc />
    public System.Threading.Tasks.Task PurgeDatabase(JellyfinDbContext dbContext, System.Collections.Generic.IEnumerable<string>? tableNames)
        => System.Threading.Tasks.Task.CompletedTask;
}
