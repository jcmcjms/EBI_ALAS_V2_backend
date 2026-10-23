using Ebi.Alas.Api.Features.Users.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ebi.Alas.Api.Features.Approvals.Persistence;

public sealed class ApprovalAuthorityConfiguration : IEntityTypeConfiguration<ApprovalAuthority>
{
    public void Configure(EntityTypeBuilder<ApprovalAuthority> builder)
    {
        builder.ToTable("ApprovalAuthorities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(120).IsRequired();
        builder.HasIndex(x => x.Key).IsUnique();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MaxTotalExposure).HasPrecision(18, 2);
    }
}

public sealed class ApprovalAuthorityUserConfiguration : IEntityTypeConfiguration<ApprovalAuthorityUser>
{
    public void Configure(EntityTypeBuilder<ApprovalAuthorityUser> builder)
    {
        builder.ToTable("ApprovalAuthorityUsers");
        builder.HasKey(x => new { x.ApprovalAuthorityId, x.UserId });
        builder.HasOne<ApprovalAuthority>()
            .WithMany()
            .HasForeignKey(x => x.ApprovalAuthorityId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
