using Microsoft.EntityFrameworkCore;
using StockReplenishment.Api.Domain;

namespace StockReplenishment.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<StockLocation> Locations => Set<StockLocation>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<ReplenishmentRequest> Requests => Set<ReplenishmentRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpRequestVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpRequestVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// SQLite has no rowversion type, so the concurrency token is maintained here: the UPDATE statement is
    /// generated with "WHERE Version = @original", which detects concurrent writers (e.g. a reviewer rejecting
    /// a request while the background stock check is saving its result). Every domain operation updates
    /// <see cref="ReplenishmentRequest.UpdatedAt"/>, so any change to the aggregate marks the root as modified.
    /// </summary>
    private void BumpRequestVersions()
    {
        foreach (var entry in ChangeTracker.Entries<ReplenishmentRequest>().Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.IncrementVersion();
        }
    }
}
