using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockReplenishment.Api.Domain;

namespace StockReplenishment.Api.Data.Configurations;

internal sealed class ReplenishmentRequestLineConfiguration : IEntityTypeConfiguration<ReplenishmentRequestLine>
{
    public void Configure(EntityTypeBuilder<ReplenishmentRequestLine> builder)
    {
        builder.ToTable("ReplenishmentRequestLines");
        builder.Ignore(l => l.IsAvailable);

        builder.Property(l => l.RequestedQuantity).HasPrecision(18, 3);
        builder.Property(l => l.AvailableQuantity).HasPrecision(18, 3);
        builder.Property(l => l.FulfilledQuantity).HasPrecision(18, 3);

        builder.HasOne(l => l.Material)
            .WithMany()
            .HasForeignKey(l => l.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.RequestId, l.MaterialId }).IsUnique();
    }
}
