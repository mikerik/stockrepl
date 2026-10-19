using Microsoft.Data.Sqlite;

namespace StockReplenishment.Api.Data;

/// <summary>
/// Owns the SQLite in-memory database. A shared-cache in-memory database lives as long as at least one
/// connection to it is open, so this singleton keeps one open for the lifetime of the application while
/// each DbContext opens its own connection (connections are not thread-safe, so they must not be shared).
/// </summary>
public sealed class InMemoryDatabase : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;

    public InMemoryDatabase(string databaseName)
    {
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseName,
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        _keepAliveConnection = new SqliteConnection(ConnectionString);
        _keepAliveConnection.Open();
    }

    public string ConnectionString { get; }

    public void Dispose() => _keepAliveConnection.Dispose();
}
