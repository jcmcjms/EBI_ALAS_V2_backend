using EBI.ALAS.Api.Features.ApprovalMatrix;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EBI.ALAS.Api.Infrastructure.Data.Configurations;

// Delegation-of-authority matrix. Seeded from the bank's
// approval matrix; read-only at runtime (no CRUD endpoints).
public class ApprovalAuthorityConfiguration : IEntityTypeConfiguration<ApprovalAuthority>
{
    public void Configure(EntityTypeBuilder<ApprovalAuthority> builder)
    {
        builder.HasKey(e => e.Key);

        builder.Property(e => e.Key)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.MaxTotalExposure)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.Tier)
            .IsRequired();

        builder.Property(e => e.Priority)
            .IsRequired();

        builder.Property(e => e.AllowNew)
            .IsRequired();

        builder.Property(e => e.AllowRenewal)
            .IsRequired();

        builder.Property(e => e.MaxSeverity)
            .IsRequired();

        builder.Property(e => e.ScopeType)
            .IsRequired();

        // Tier lookup for routing and assignment
        builder.HasIndex(e => new { e.Tier, e.Priority })
            .HasDatabaseName("IX_ApprovalAuthorities_Tier_Priority");
    }
}
