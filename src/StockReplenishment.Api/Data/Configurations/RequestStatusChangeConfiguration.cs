using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Data.Configurations;

internal sealed class RequestStatusChangeConfiguration : IEntityTypeConfiguration<RequestStatusChange>
{
    public void Configure(EntityTypeBuilder<RequestStatusChange> builder)
    {
        builder.ToTable("RequestStatusChanges");
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ChangedBy).HasMaxLength(ValidationLimits.UserNameMaxLength).IsRequired();
        builder.Property(h => h.Comment).HasMaxLength(ValidationLimits.CommentMaxLength);
        builder.HasIndex(h => new { h.RequestId, h.ChangedAt });
    }
}
