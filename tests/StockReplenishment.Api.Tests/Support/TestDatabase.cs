using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Data;
using StockReplenishment.Api.Domain;

namespace StockReplenishment.Api.Tests.Support;

/// <summary>A private SQLite in-memory database per test, with a minimal catalog.</summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
        db.Locations.AddRange(
            new StockLocation { Code = "ASM-01", Name = "Assembly 1", Area = "Assembly" },
            new StockLocation { Code = "WLD-01", Name = "Welding 1", Area = "Welding" });
        db.Materials.AddRange(
            new Material { ArticleNumber = "100-2045", Description = "Hex bolt M8x30", UnitOfMeasure = "pcs" },
            new Material { ArticleNumber = "100-2046", Description = "Hex nut M8", UnitOfMeasure = "pcs" },
            new Material { ArticleNumber = "310-0007", Description = "Welding wire", UnitOfMeasure = "kg" });
        db.SaveChanges();
    }

    public AppDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
