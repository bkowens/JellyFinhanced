using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Database.Providers.MySql;

/// <summary>
/// A <see cref="DbConnectionInterceptor"/> that sets session-level variables on MySQL connections.
/// </summary>
public class MySqlConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ILogger _logger;
    private readonly string _initialCommand;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlConnectionInterceptor"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public MySqlConnectionInterceptor(ILogger logger)
    {
        _logger = logger;
        _initialCommand = "SET SESSION wait_timeout=28800; SET SESSION interactive_timeout=28800; SET NAMES utf8mb4;";
        _logger.LogInformation("MySQL connection interceptor command set to: {Command}", _initialCommand);
    }

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        base.ConnectionOpened(connection, eventData);
        using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = _initialCommand;
#pragma warning restore CA2100
        command.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
#pragma warning disable CA2100
            command.CommandText = _initialCommand;
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
