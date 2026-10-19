using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StockReplenishment.Api.Data;

/// <summary>
/// In SQLite shared-cache mode, locks are table-level and shared between connections, so a reader holding a
/// table read lock can deadlock a concurrent writer ("database table is locked"). With read_uncommitted,
/// readers take no table locks. Writers are still serialized by SQLite and optimistic concurrency checks
/// still run under the write lock. The trade-off is that a read can briefly observe a transaction that is
/// still being committed, which is acceptable for this in-memory demo database.
/// </summary>
internal sealed class SharedCacheConnectionInterceptor : DbConnectionInterceptor
{
    private const string Pragma = "PRAGMA read_uncommitted = 1;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragma;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
