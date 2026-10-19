using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockReplenishment.Api.Domain;
using StockReplenishment.Contracts.Requests;

namespace StockReplenishment.Api.Data.Configurations;

internal sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials");
        builder.Property(m => m.ArticleNumber).HasMaxLength(ValidationLimits.ArticleNumberMaxLength).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(200).IsRequired();
        builder.Property(m => m.UnitOfMeasure).HasMaxLength(10).IsRequired();
        builder.HasIndex(m => m.ArticleNumber).IsUnique();
    }
}
