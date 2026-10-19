using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Data.Configurations;

internal sealed class ReplenishmentRequestConfiguration : IEntityTypeConfiguration<ReplenishmentRequest>
{
    public void Configure(EntityTypeBuilder<ReplenishmentRequest> builder)
    {
        builder.ToTable("ReplenishmentRequests");

        builder.Ignore(r => r.RequestNumber);
        builder.Ignore(r => r.IsAwaitingStockCheck);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        // Stored as its ordinal (Low < Normal < Urgent) so that sorting by priority is meaningful.
        builder.Property(r => r.Priority).HasConversion<int>();
        builder.Property(r => r.StockCheckStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Notes).HasMaxLength(ValidationLimits.NotesMaxLength);
        builder.Property(r => r.RequestedBy).HasMaxLength(ValidationLimits.UserNameMaxLength).IsRequired();
        builder.Property(r => r.ReviewedBy).HasMaxLength(ValidationLimits.UserNameMaxLength);
        builder.Property(r => r.FulfilledBy).HasMaxLength(ValidationLimits.UserNameMaxLength);
        builder.Property(r => r.RejectionReason).HasMaxLength(ValidationLimits.CommentMaxLength);
        builder.Property(r => r.StockCheckMessage).HasMaxLength(500);
        builder.Property(r => r.Version).IsConcurrencyToken();

        builder.HasOne(r => r.Location)
            .WithMany()
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Lines)
            .WithOne()
            .HasForeignKey(l => l.RequestId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Lines).HasField("_lines");

        builder.HasMany(r => r.History)
            .WithOne()
            .HasForeignKey(h => h.RequestId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.History).HasField("_history");

        // Supports the list view filters.
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => r.Priority);
        builder.HasIndex(r => r.LocationId);
        builder.HasIndex(r => r.StockCheckStatus);
    }
}
