using EBI.ALAS.Api.Features.ApprovalMatrix;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Seeded deviation severity catalog. Read-only at runtime.
public class DeviationCatalogItemConfiguration : IEntityTypeConfiguration<DeviationCatalogItem>
{
    public void Configure(EntityTypeBuilder<DeviationCatalogItem> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        builder.HasIndex(e => e.Description)
            .IsUnique();

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Severity)
            .IsRequired();
    }
}
