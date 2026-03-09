using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Locking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Database.Providers.MySql.Migrations;

/// <summary>
/// Design-time factory for creating <see cref="JellyfinDbContext"/> instances for MySQL migrations.
/// This class is used by EF Core tooling (dotnet ef) and is not used at runtime.
/// </summary>
internal sealed class MySqlDesignTimeJellyfinDbFactory : IDesignTimeDbContextFactory<JellyfinDbContext>
{
    /// <inheritdoc/>
    public JellyfinDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<JellyfinDbContext>();
        var connectionString = "Server=localhost;Database=jellyfin;User=jellyfin;Password=your_password;";
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        optionsBuilder.UseMySql(
            connectionString,
            serverVersion,
            f => f.MigrationsAssembly(GetType().Assembly));

        return new JellyfinDbContext(
            optionsBuilder.Options,
            NullLogger<JellyfinDbContext>.Instance,
            new MySqlDatabaseProvider(null!, NullLogger<MySqlDatabaseProvider>.Instance),
            new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));
    }
}
