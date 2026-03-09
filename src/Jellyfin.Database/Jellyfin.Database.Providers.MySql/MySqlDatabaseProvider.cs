using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.DbConfiguration;
using MediaBrowser.Common.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Database.Providers.MySql;

/// <summary>
/// MySQL database provider for Jellyfin.
/// </summary>
[JellyfinDatabaseProviderKey("Jellyfin-MySQL")]
public sealed class MySqlDatabaseProvider : IJellyfinDatabaseProvider
{
    private const string BackupFolderName = "MySqlBackups";
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<MySqlDatabaseProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDatabaseProvider"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public MySqlDatabaseProvider(IApplicationPaths applicationPaths, ILogger<MySqlDatabaseProvider> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <inheritdoc/>
    public IDbContextFactory<JellyfinDbContext>? DbContextFactory { get; set; }

    /// <inheritdoc/>
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration)
    {
        static T? GetOption<T>(ICollection<CustomDatabaseOption>? options, string key, Func<string, T> converter, Func<T>? defaultValue = null)
        {
            if (options is null)
            {
                return defaultValue is not null ? defaultValue() : default;
            }

            var value = options.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (value is null)
            {
                return defaultValue is not null ? defaultValue() : default;
            }

            return converter(value.Value);
        }

        var customOptions = databaseConfiguration.CustomProviderOptions?.Options;
        var connectionString = databaseConfiguration.CustomProviderOptions?.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var server = GetOption(customOptions, "server", e => e, () => "localhost");
            var port = GetOption(customOptions, "port", e => e, () => "3306");
            var database = GetOption(customOptions, "database", e => e, () => "jellyfin");
            var user = GetOption(customOptions, "user", e => e, () => "jellyfin");
            var password = GetOption(customOptions, "password", e => e, () => string.Empty);
            var sslMode = GetOption(customOptions, "sslmode", e => e, () => "Preferred");

            connectionString = $"Server={server};Port={port};Database={database};User={user};Password={password};SslMode={sslMode};CharSet=utf8mb4;";
        }

        var sanitizedConnectionString = Regex.Replace(connectionString, @"Password=[^;]*", "Password=***", RegexOptions.IgnoreCase);
        _logger.LogInformation("MySQL connection string: {ConnectionString}", sanitizedConnectionString);

        var serverVersion = DetectServerVersionWithRetry(connectionString);
        _logger.LogInformation("Detected MySQL/MariaDB server version: {ServerVersion}", serverVersion);

        options
            .UseMySql(
                connectionString,
                serverVersion,
                mysqlOptions => mysqlOptions.MigrationsAssembly(GetType().Assembly))
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.NonTransactionalMigrationOperationWarning))
            .AddInterceptors(new MySqlConnectionInterceptor(_logger));

        var enableSensitiveDataLogging = GetOption(customOptions, "EnableSensitiveDataLogging", e => e.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase), () => false);
        if (enableSensitiveDataLogging)
        {
            options.EnableSensitiveDataLogging(enableSensitiveDataLogging);
            _logger.LogInformation("EnableSensitiveDataLogging is enabled on MySQL connection");
        }
    }

    private ServerVersion DetectServerVersionWithRetry(string connectionString)
    {
        const int MaxRetries = 5;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                return ServerVersion.AutoDetect(connectionString);
            }
            catch (Exception ex) when (attempt < MaxRetries - 1)
            {
                var delaySeconds = (int)Math.Pow(2, attempt);
                _logger.LogWarning(ex, "Failed to connect to MySQL server (attempt {Attempt}/{MaxRetries}), retrying in {Delay}s...", attempt + 1, MaxRetries, delaySeconds);
                Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
            }
        }

        // Final attempt — let exception propagate.
        return ServerVersion.AutoDetect(connectionString);
    }

    /// <inheritdoc/>
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.SetDefaultDateTimeKind(DateTimeKind.Utc);

        // Constrain string columns used in composite indexes to fit within
        // MariaDB/MySQL's 3072-byte index key limit with utf8mb4 (4 bytes/char).
        modelBuilder.Entity<Jellyfin.Database.Implementations.Entities.BaseItemEntity>(entity =>
        {
            entity.Property(e => e.Type).HasMaxLength(128);
            entity.Property(e => e.MediaType).HasMaxLength(64);
            entity.Property(e => e.SeriesPresentationUniqueKey).HasMaxLength(128);
            entity.Property(e => e.PresentationUniqueKey).HasMaxLength(128);
            entity.Property(e => e.SortName).HasMaxLength(128);
            entity.Property(e => e.Path).HasMaxLength(768);
        });
    }

    /// <inheritdoc/>
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // No-op: MySQL supports the RETURNING clause equivalent via last_insert_id(),
        // so no convention override is needed.
    }

    /// <inheritdoc/>
    public async Task RunScheduledOptimisation(CancellationToken cancellationToken)
    {
        var context = await DbContextFactory!.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var tables = await context.Database
                .SqlQueryRaw<string>("SELECT TABLE_NAME FROM information_schema.tables WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var table in tables)
            {
                // Table names come from information_schema, not user input.
#pragma warning disable EF1002, EF1003
                await context.Database.ExecuteSqlRawAsync("OPTIMIZE TABLE `" + table + "`", cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002, EF1003
            }

            _logger.LogInformation("MySQL database optimized successfully!");
        }
    }

    /// <inheritdoc/>
    public Task RunShutdownTask(CancellationToken cancellationToken)
    {
        // No-op: MySQL connections are managed by the server.
        // Connection pooling cleanup is handled by the ADO.NET provider.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string> MigrationBackupFast(CancellationToken cancellationToken)
    {
        var key = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var backupDir = Path.Combine(_applicationPaths.DataPath, BackupFolderName, key);
        Directory.CreateDirectory(backupDir);

        var context = await DbContextFactory!.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var tables = await context.Database
                .SqlQueryRaw<string>("SELECT TABLE_NAME FROM information_schema.tables WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'")
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var table in tables)
            {
                var filePath = Path.Combine(backupDir, $"{table}.json");
                var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);
                await using (fileStream.ConfigureAwait(false))
                {
                    var writer = new Utf8JsonWriter(fileStream);
                    using (writer)
                    {
                        writer.WriteStartArray();

                        var command = connection.CreateCommand();
                        await using (command.ConfigureAwait(false))
                        {
                            command.CommandText = $"SELECT * FROM `{table}`";
                            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                            await using (reader.ConfigureAwait(false))
                            {
                                var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                                {
                                    writer.WriteStartObject();
                                    foreach (var col in columns)
                                    {
                                        var val = reader[col];
                                        writer.WritePropertyName(col);
                                        if (val == DBNull.Value || val is null)
                                        {
                                            writer.WriteNullValue();
                                        }
                                        else
                                        {
                                            JsonSerializer.Serialize(writer, val);
                                        }
                                    }

                                    writer.WriteEndObject();
                                }
                            }
                        }

                        writer.WriteEndArray();
                    }

                    await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        _logger.LogInformation("MySQL backup created with key: {Key}", key);
        return key;
    }

    /// <inheritdoc/>
    public Task RestoreBackupFast(string key, CancellationToken cancellationToken)
    {
        var backupDir = Path.Combine(_applicationPaths.DataPath, BackupFolderName, key);

        if (!Directory.Exists(backupDir))
        {
            _logger.LogCritical("Tried to restore a backup that does not exist: {Key}", key);
            return Task.CompletedTask;
        }

        _logger.LogWarning("MySQL backup restore from key {Key} is available at {Path}. Manual restoration via mysql client is required.", key, backupDir);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteBackup(string key)
    {
        var backupDir = Path.Combine(_applicationPaths.DataPath, BackupFolderName, key);

        if (!Directory.Exists(backupDir))
        {
            _logger.LogCritical("Tried to delete a backup that does not exist: {Key}", key);
            return Task.CompletedTask;
        }

        Directory.Delete(backupDir, true);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task PurgeDatabase(JellyfinDbContext dbContext, IEnumerable<string>? tableNames)
    {
        ArgumentNullException.ThrowIfNull(tableNames);

        var deleteQueries = new List<string>();
        deleteQueries.Add("SET foreign_key_checks = 0;");
        foreach (var tableName in tableNames)
        {
            deleteQueries.Add($"DELETE FROM `{tableName}`;");
        }

        deleteQueries.Add("SET foreign_key_checks = 1;");

        var deleteAllQuery = string.Join('\n', deleteQueries);
        await dbContext.Database.ExecuteSqlRawAsync(deleteAllQuery).ConfigureAwait(false);
    }
}
