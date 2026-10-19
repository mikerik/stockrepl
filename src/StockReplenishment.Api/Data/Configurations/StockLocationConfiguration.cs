using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockReplenishment.Api.Domain;

namespace StockReplenishment.Api.Data.Configurations;

internal sealed class StockLocationConfiguration : IEntityTypeConfiguration<StockLocation>
{
    public void Configure(EntityTypeBuilder<StockLocation> builder)
    {
        builder.ToTable("StockLocations");
        builder.Property(l => l.Code).HasMaxLength(20).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Area).HasMaxLength(50).IsRequired();
        builder.HasIndex(l => l.Code).IsUnique();
    }
}
